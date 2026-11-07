using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Normal medicine keeps its own saved preference. Old batches remain on
    // their recorded route; only newly calculated plans compare drying methods.
    internal static class NormalDrying
    {
        internal const string PreferenceTag = "DDA_NORMAL_DRYING";
        internal static readonly int[] Temperatures = { 57, 85, 105, 125, 160 };
        private static readonly string[] medicines = ChemicalRecipe.Ids.Select(ChemicalRecipe.Get)
            .SelectMany(r => new[] { r.Active, r.Mush }).Concat(new[] { "methylol" })
            .Where(id => id != null).Distinct().ToArray();

        internal static bool Damaged(GameItem before, GameItem after) => medicines.Any(id =>
            ChemistryOperations.Amount(after, id) < ChemistryOperations.Amount(before, id));

        internal static int HeatUntilDry(GameItem batch, int upgrades, int temperature, GameItem buffer = null)
        {
            if (!Temperatures.Contains(temperature)) return -1;
            var before = new SimulationItem(batch);
            int seconds = 0;
            while (ChemistryOperations.VolatileVolume(batch) > 0 && seconds < HeatedDrying.TickLimit)
            {
                if (buffer != null && ChemistryOperations.Total(buffer) > 0) ChemistryOperations.ReactionTick(buffer);
                ChemistryOperations.SimulateHeatedSecond(batch, upgrades, temperature);
                if (Damaged(before, batch)) return -1;
                seconds++;
            }
            return ChemistryOperations.VolatileVolume(batch) == 0 ? seconds : -1;
        }
    }

    // Compare complete native outcomes, including the stages after each drying
    // step. Work is sliced; this never touches a live item or UI object.
    internal sealed class NormalDryingPlanner
    {
        private sealed class State
        {
            internal SimulationItem tank, buffer;
            internal SimulationItem[] supplies;
            internal List<BatchAction> steps = new List<BatchAction>();
            internal int seconds, boiledAlcohol;
            internal State Copy() => new State { tank = new SimulationItem(tank),
                buffer = buffer == null ? null : new SimulationItem(buffer),
                supplies = supplies.Select(x => new SimulationItem(x)).ToArray(),
                steps = steps.Select(x => x.Copy()).ToList(), seconds = seconds, boiledAlcohol = boiledAlcohol };
        }
        private readonly BatchInputs input;
        private readonly ChemicalRecipe recipe;
        private readonly BatchPlan baseline;
        internal BatchPlan Evaporation { get; private set; }
        internal BatchPlan Heating { get; private set; }
        private string failure;
        private State replayed;
        private readonly Dictionary<int, State> checkpoints = new Dictionary<int, State>();

        internal NormalDryingPlanner(BatchInputs input, BatchPlan baseline)
        { this.input = input; this.baseline = baseline.Copy(); recipe = ChemicalRecipe.Get(baseline.productId); }

        internal IEnumerator Search()
        {
            Evaporation = baseline.Copy();
            Evaporation.normalDrying = DirectDryingMethod.Evaporate;
            Evaporation.evaporationBottles = baseline.expectedBottles;
            Evaporation.normalHeatReason = "normal_heat_check_failed";
            var original = new State { tank = new SimulationItem(input.chamber),
                buffer = input.mashBuffer == null ? null : new SimulationItem(input.mashBuffer),
                supplies = input.supplies.Select(x => new SimulationItem(x)).ToArray() };
            foreach (var ignored in Replay(original, baseline, 0, -1, 0, true)) yield return null;
            if (replayed == null) yield break;
            var reference = Measure(replayed, baseline);
            if (reference.Bottles != baseline.expectedBottles || !GradePolicy.Meets(reference, baseline.actualGrade)) yield break;
            var current = baseline.Copy();
            bool wet = false, damaged = false, unqualified = false, timeout = false, unstable = false;
            // Final drying first. A heat-sensitive precursor may still need an
            // intermediate evaporator stage even if the finished medicine is safe.
            var indices = checkpoints.Keys.OrderByDescending(i => i).ToArray();
            foreach (int index in indices)
            {
                var start = checkpoints[index];
                if (ChemistryOperations.VolatileVolume(start.tank) == 0) continue;
                wet = true;
                State bestState = null;
                ProductionOutcome best = null;
                int bestGrade = 0;
                double boilingPoint = Liquid.Liquids.Where(l => l.isEvaporative && ChemistryOperations.Amount(start.tank, l.identifier) > 0)
                    .Max(l => l.boilingPoint);
                foreach (int target in NormalDrying.Temperatures)
                {
                    if (target <= boilingPoint) continue;
                    foreach (var ignored in Replay(start, current, index, index, target, false)) yield return null;
                    if (replayed == null)
                    {
                        damaged |= failure == "normal_heat_damage";
                        timeout |= failure == "normal_heat_timeout";
                        continue;
                    }
                    var outcome = Measure(replayed, current);
                    var grade = GradePolicy.Select(baseline.requestedGrade, new[] { outcome });
                    if (grade == null || grade.ActualGrade < current.actualGrade) { unqualified = true; continue; }
                    // Once heated material enters the finisher it can wait for
                    // output space. Reject routes whose contents keep changing
                    // while cooling, rather than promise an immediate-only yield.
                    var cooling = new SimulationItem(replayed.tank);
                    var dryParts = ChemistryOperations.Parts(cooling);
                    bool stable = true;
                    for (int n = 0; n < HeatedDrying.TickLimit; n++)
                    {
                        ChemistryOperations.ReactionTick(cooling);
                        if (!ChemistryOperations.SameParts(dryParts, ChemistryOperations.Parts(cooling))) { stable = false; break; }
                        if (ChemistryOperations.Temperature(cooling) < 39) break;
                        if (n == HeatedDrying.TickLimit - 1) stable = false;
                        if (n % 8 == 7) yield return null;
                    }
                    if (!stable) { unstable = true; continue; }
                    if (best == null || grade.ActualGrade > bestGrade || (grade.ActualGrade == bestGrade && GradePolicy.Better(outcome, best)))
                    { best = outcome; bestState = replayed; bestGrade = grade.ActualGrade; }
                    yield return null;
                }
                if (bestState == null) continue;
                current.actions = bestState.steps.ToArray();
                current.reactionSeconds = bestState.seconds;
                current.expectedBottles = best.Bottles;
                current.minimumActive = best.MinimumBottleActiveVolume;
                current.actualGrade = bestGrade;
                current.outputLimited = OutputCapacity.DryProducts(ChemistryOperations.Parts(bestState.tank), recipe) > best.Bottles;
                current.boiledAlcohol = bestState.boiledAlcohol;
                current.schema = 4;
                current.normalDrying = DirectDryingMethod.Heat;
                current.concentrationHint = ConcentrationAdvice.Get(input, current, ChemistryOperations.Parts(bestState.tank));
                foreach (var step in current.actions.Where(a => a.operation == Operation.Form)) step.amount = best.Bottles;
                Heating = current.Copy();
            }
            Evaporation.normalHeatReason = Heating != null ? null : !wet ? "normal_heat_not_needed" :
                damaged ? "normal_heat_damage" : unqualified ? "normal_heat_grade" : unstable ? "normal_heat_unstable" : timeout ? "normal_heat_timeout" : "normal_heat_check_failed";
            if (!wet)
            {
                Heating = Evaporation.Copy();
                Heating.schema = 4;
                Heating.normalDrying = DirectDryingMethod.Heat;
                yield break;
            }
            if (Heating != null)
            {
                Evaporation.normalHeatBottles = Heating.expectedBottles;
                Evaporation.normalHeatMinimumActive = Heating.minimumActive;
                Evaporation.normalHeatSeconds = Heating.reactionSeconds;
                Evaporation.boiledAlcohol = Heating.boiledAlcohol;
                Heating.evaporationBottles = baseline.expectedBottles;
                Heating.normalHeatBottles = Heating.expectedBottles;
                Heating.normalHeatMinimumActive = Heating.minimumActive;
                Heating.normalHeatSeconds = Heating.reactionSeconds;
                Heating.normalHeatReason = null;
                // Recount this field if a middle-stage solvent source changed.
                Heating.recoveredVolume = RecoveredSolvent.DrawnVolume(Heating, input.recoveredSource);
            }
        }

        private ProductionOutcome Measure(State state, BatchPlan plan) => ChemistryOperations.Measure(state.tank,
            input.finisherLoss, plan.reagents, state.seconds, null, recipe, plan.FormingLimit);

        private IEnumerable<int> Replay(State initial, BatchPlan plan, int from, int replace, int target, bool capture)
        {
            replayed = null; failure = null;
            var state = initial.Copy();
            for (int i = from; i < plan.actions.Length; i++)
            {
                var step = plan.actions[i].Copy();
                if (capture && step.operation == Operation.Dry) checkpoints[i] = state.Copy();
                if (i == replace) { step.operation = Operation.BoilDry; step.targetTemperature = target; }
                int duration = 0;
                if (ReactionProgress.IsTimed(step.operation))
                {
                    var before = new SimulationItem(state.tank);
                    if (step.operation == Operation.BoilDry) state.boiledAlcohol += ChemistryOperations.Amount(state.tank, "alcohol");
                    string precursor = step.precursorId ?? recipe.Mush;
                    bool done = false;
                    int unchanged = 0;
                    for (; duration < HeatedDrying.TickLimit; duration++)
                    {
                        done = step.operation == Operation.BoilDry ? ChemistryOperations.VolatileVolume(state.tank) == 0 :
                            step.operation == Operation.React ? (step.precursorId == "fixed_wait" ? duration >= step.amount : unchanged >= 2) :
                            precursor != null && ChemistryOperations.Amount(state.tank, precursor) <= step.endAmount;
                        if (done) break;
                        if (step.operation == Operation.React)
                        {
                            var parts = ChemistryOperations.Parts(state.tank);
                            ChemistryOperations.ReactionTick(state.tank);
                            unchanged = ChemistryOperations.SameParts(parts, ChemistryOperations.Parts(state.tank)) ? unchanged + 1 : 0;
                        }
                        else
                        {
                            if (state.buffer != null && ChemistryOperations.Total(state.buffer) > 0) ChemistryOperations.ReactionTick(state.buffer);
                            ChemistryOperations.SimulateHeatedSecond(state.tank, input.heaterUpgrades, step.targetTemperature);
                        }
                        if (step.operation == Operation.BoilDry && NormalDrying.Damaged(before, state.tank))
                        { failure = "normal_heat_damage"; yield break; }
                        if (duration % 8 == 7) yield return 0;
                    }
                    if (!done && !(step.operation == Operation.BoilDry && ChemistryOperations.VolatileVolume(state.tank) == 0))
                    { failure = "normal_heat_timeout"; yield break; }
                    step.amount = duration;
                    state.seconds += duration;
                }
                else
                {
                    try
                    {
                        switch (step.operation)
                        {
                            case Operation.Blend: ChemistryOperations.Blend(new SimulationItem(input.raw[step.source]), state.tank, input.blenderLoss); break;
                            case Operation.Dissolve: ChemistryOperations.Dissolve(new SimulationItem(input.raw[step.source]), state.tank); break;
                            case Operation.Pour: ChemistryOperations.Pour(state.supplies[step.source], state.tank, step.amount); break;
                            case Operation.StoreMash: ChemistryOperations.Pour(state.tank, state.buffer, step.amount); break;
                            case Operation.FeedMash: ChemistryOperations.Pour(state.buffer, state.tank, step.amount); break;
                            case Operation.Filter: FiltrationPolicy.Apply(state.tank, step, i => state.supplies[i]); break;
                            case Operation.Dry:
                                step.evaporationVolume = ChemistryOperations.VolatileVolume(state.tank);
                                ChemistryOperations.SimulateDry(state.tank);
                                RecoveredSolvent.AfterEvaporation(state.supplies, input.recoveredSource);
                                state.tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
                                break;
                            case Operation.Form: break;
                            default: failure = "normal_heat_check_failed"; break;
                        }
                    }
                    catch (MachineFault) { failure = "normal_heat_check_failed"; }
                    if (failure != null) yield break;
                }
                // Retain zero-duration steps here so stage indices remain stable
                // while alternatives are compared. Preflight may remove them.
                state.steps.Add(step);
                yield return 0;
            }
            replayed = state;
        }
    }
}
