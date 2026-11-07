using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        public BatchPlan Result { get; private set; }
        public string Error { get; private set; }
        public int CandidatesChecked { get; private set; }
        public string Stage { get; private set; } = "plan_mix";
        public int SolventAdded { get; private set; }
        public List<GradeAttempt> GradeAttempts { get; } = new List<GradeAttempt>();
        private readonly List<ProductionOutcome> outcomes = new List<ProductionOutcome>();
        private readonly Dictionary<string, ProductionOutcome> measurementCache = new Dictionary<string, ProductionOutcome>();
        private readonly HashSet<string> selectedFeeds = new HashSet<string>();
        private int evaluatingGrade;
        private ProductionOutcome winningOutcome;
        private int[] winningParts;
        private sealed class Route
        {
            public SimulationItem batch;
            public SimulationItem[] supplies;
            public BatchPlan plan;
            public int reagentPolicy;
            public bool synthesisTimedOut;
            public bool catalystLimited;
        }
        private readonly BatchInputs inputs;
        private readonly ChemicalRecipe recipe;
        public BatchPlanner(BatchInputs inputs)
        {
            // Native containers keep ticking while the coroutine yields. Every
            // candidate must start from the same snapshot, with no live objects.
            recipe = ChemicalRecipe.Get(inputs.productId);
            this.inputs = new BatchInputs { maxProducts = inputs.maxProducts, feedPrepared = inputs.feedPrepared, reservedBufferVolume = inputs.reservedBufferVolume,
                normalDrying = inputs.normalDrying, skipDryingComparison = inputs.skipDryingComparison,
                directForm = inputs.directForm, separateDirect = inputs.separateDirect, directDrying = inputs.directDrying, directSolvents = inputs.directSolvents, productId = recipe.Id, raw = inputs.raw.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
                supplies = inputs.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(), recoveredSource = inputs.recoveredSource,
                chamber = new SimulationItem(inputs.chamber), blenderLoss = inputs.blenderLoss,
                mashBuffer = inputs.mashBuffer == null ? null : new SimulationItem(inputs.mashBuffer), alcoholPercent = inputs.alcoholPercent,
                heaterUpgrades = inputs.heaterUpgrades, finisherLoss = inputs.finisherLoss, requestedGrade = inputs.requestedGrade };
        }

        public static BatchPlan Revalidate(BatchInputs inputs, BatchPlan plan, PlanningDiagnostics diagnostics = null)
        {
            BatchPlan Invalid(string key, string detail = null, params object[] args)
            { diagnostics?.Add(key, 100, detail, args); return null; }
            if (ChemistryOperations.Total(inputs.chamber) > WaterHelper.GetTotalCapacity(inputs.chamber))
                return Invalid("runtime_residue_capacity");
            if (plan.separateDirect) return RevalidateSeparateDirect(inputs, plan, diagnostics);
            if (!SourceStability.Check(inputs, plan, diagnostics)) return null;
            if (inputs.directForm != plan.directForm) return Invalid("changed_settings");
            if (!plan.directForm && inputs.normalDrying != plan.normalDrying) return Invalid("changed_settings");
            if (inputs.maxProducts < plan.expectedBottles) return Invalid("changed_output");
            if (plan.directForm && inputs.directSolvents != plan.directSolvents) return Invalid("changed_settings");
            var recipe = ChemicalRecipe.Get(plan.productId);
            // Normal ambient temperature changes must not make a preview unusable.
            // Replay the selected route at the current temperatures before consuming
            // anything, updating wait times and requiring the promised grade/count.
            var batch = new SimulationItem(inputs.chamber);
            var buffer = inputs.mashBuffer == null ? null : new SimulationItem(inputs.mashBuffer);
            if (!plan.directForm && plan.requestedBottles == 0 && inputs.alcoholPercent != 0 && !AlcoholBudget.Read(inputs).AllowsPlanning(AlcoholBudget.Percent(inputs.alcoholPercent))) return Invalid("alcohol_unavailable");
            var supplies = inputs.supplies.Select(x => new SimulationItem(x)).ToArray();
            var steps = new List<BatchAction>();
            int seconds = 0;
            try
            {
            foreach (var step in plan.actions)
            {
                bool solid = step.operation == Operation.Blend || step.operation == Operation.Dissolve;
                bool supply = step.operation == Operation.Pour || step.operation == Operation.Filter;
                if ((solid || supply) && (step.source < 0 || step.source >= (solid ? inputs.raw.Length : supplies.Length)))
                    return Invalid("runtime_source_missing");
                if (plan.directForm && (step.operation == Operation.Blend || step.operation == Operation.Dissolve) &&
                    !DirectFormInputs.CanUse(inputs.raw[step.source])) return Invalid(DirectFormInputs.ProtectionReason(inputs.raw[step.source]));
                if (plan.directForm && step.operation == Operation.Pour &&
                    !DirectFormInputs.CanUseLiquid(inputs.supplies[step.source], plan.directSolvents)) return Invalid(DirectFormInputs.ProtectionReason(inputs.supplies[step.source]));
                int duration = 0, evaporationVolume = -1;
                switch (step.operation)
                {
                    case Operation.Blend:
                        if (ChemistryOperations.Total(inputs.raw[step.source]) > WaterHelper.GetFreeCapacity(batch)) return Invalid("runtime_tank_capacity");
                        ChemistryOperations.Blend(new SimulationItem(inputs.raw[step.source]), batch, inputs.blenderLoss);
                        break;
                    case Operation.Dissolve:
                        ChemistryOperations.Dissolve(new SimulationItem(inputs.raw[step.source]), batch);
                        break;
                    case Operation.Pour:
                        ChemistryOperations.Pour(supplies[step.source], batch, step.amount);
                        break;
                    case Operation.StoreMash:
                        if (buffer == null) return Invalid("missing_buffer");
                        ChemistryOperations.Pour(batch, buffer, step.amount);
                        break;
                    case Operation.FeedMash:
                        if (buffer == null) return Invalid("missing_buffer");
                        ChemistryOperations.Pour(buffer, batch, step.amount);
                        break;
                    case Operation.Extract:
                    case Operation.Synthesize:
                        duration = HeatUntil(batch, inputs.heaterUpgrades, step, recipe, buffer);
                        if (duration < 0) return Invalid("runtime_reaction_endpoint");
                        break;
                    case Operation.React:
                        if (step.precursorId == "fixed_wait")
                        {
                            duration = step.amount;
                            for (int n = 0; n < duration; n++) ChemistryOperations.ReactionTick(batch);
                        }
                        else duration = Settle(batch);
                        if (duration < 0) return Invalid("diag_cleanup_timeout");
                        break;
                    case Operation.BoilDry:
                        duration = plan.directForm ? HeatedDrying.HeatUntilDry(batch, inputs.heaterUpgrades, buffer) :
                            NormalDrying.HeatUntilDry(batch, inputs.heaterUpgrades, step.targetTemperature, buffer);
                        if (duration < 0) return Invalid(plan.directForm ? "direct_boil_failed" : "normal_heat_recheck");
                        break;
                    case Operation.Filter:
                        if (step.FilterSources.Any(i => i < 0 || i >= supplies.Length || !supplies[i].IsTag("WATER_FILTER"))) return Invalid("runtime_filter_missing");
                        FiltrationPolicy.Apply(batch, step, i => supplies[i]);
                        break;
                    case Operation.Dry:
                        evaporationVolume = ChemistryOperations.VolatileVolume(batch);
                        ChemistryOperations.SimulateDry(batch);
                        RecoveredSolvent.AfterEvaporation(supplies, inputs.recoveredSource);
                        // A later reaction must tolerate the ordinary cooling that
                        // happens while the native evaporator waits for game cycles.
                        batch.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
                        break;
                    case Operation.Form:
                        break;
                    default:
                        return Invalid("runtime_saved_operation");
                }
                if (ReactionProgress.IsTimed(step.operation))
                {
                    seconds += duration;
                    if (duration > 0) steps.Add(new BatchAction(step.operation, step.source, duration, step.targetTemperature)
                        { precursorId = step.precursorId, endAmount = step.endAmount });
                }
                else { var retained = step.Copy(); retained.evaporationVolume = evaporationVolume; steps.Add(retained); }
            }
            }
            catch (MachineFault fault) { return Invalid(fault.Key); }
            var measured = ChemistryOperations.Measure(batch, inputs.finisherLoss, plan.reagents, seconds, null, recipe, plan.FormingLimit);
            if (inputs.finisher != null && ChemistryOperations.Total(batch) > WaterHelper.GetTotalCapacity(inputs.finisher))
                return Invalid("runtime_finisher_capacity");
            int available = OutputCapacity.DryProducts(ChemistryOperations.Parts(batch), recipe);
            if (plan.requestedBottles > 0 && available != plan.requestedBottles)
                return Invalid("diag_recheck_quantity", "diag_quantity_yield_detail", available, plan.requestedBottles);
            if (measured.Bottles != plan.expectedBottles)
                return Invalid("diag_recheck_quantity", "diag_quantity_yield_detail", measured.Bottles, plan.expectedBottles);
            if (!GradePolicy.Meets(measured, plan.actualGrade))
                return Invalid("diag_recheck_grade", "diag_quantity_grade_detail", 100.0 * measured.MinimumBottleActiveVolume / recipe.UnitVolume, plan.actualGrade);
            var result = plan.Copy();
            result.actions = steps.ToArray();
            result.reactionSeconds = seconds;
            result.minimumActive = measured.MinimumBottleActiveVolume;
            FiltrationPolicy.Recount(result, inputs.supplies, supplies);
            return result;
        }

        internal static int HeatUntil(GameItem batch, int upgrades, BatchAction action, ChemicalRecipe recipe, GameItem buffer = null)
        {
            string precursor = action.precursorId ?? recipe.Mush;
            if (precursor == null) return -1;
            int target = action.targetTemperature > 0 ? action.targetTemperature : 45;
            int duration = 0;
            while (ChemistryOperations.Amount(batch, precursor) > action.endAmount && duration < 900)
            {
                if (buffer != null && ChemistryOperations.Total(buffer) > 0) ChemistryOperations.ReactionTick(buffer);
                ChemistryOperations.SimulateHeatedSecond(batch, upgrades, target); duration++;
            }
            return ChemistryOperations.Amount(batch, precursor) <= action.endAmount ? duration : -1;
        }

        public IEnumerator Search()
        {
            if (ChemistryOperations.Total(inputs.chamber) > WaterHelper.GetTotalCapacity(inputs.chamber))
            { Report("runtime_residue_capacity"); yield break; }
            if (inputs.maxProducts <= 0) { Report("output_no_space"); yield break; }
            if (inputs.directForm)
            {
                foreach (var ignored in inputs.separateDirect ? PlanSeparateDirect() : PlanDirectForm()) yield return null;
                yield break;
            }
            var budget = AlcoholBudget.Read(inputs);
            if (inputs.alcoholPercent != 0 && !budget.AllowsPlanning(AlcoholBudget.Percent(inputs.alcoholPercent)))
            {
                if (budget.ForeignAlcohol > 0)
                    Report("diag_reagent_foreign", 10, "diag_foreign_reagent_detail", PartName("alcohol"),
                        budget.Needed(AlcoholBudget.Percent(inputs.alcoholPercent)) / 1000.0, budget.Available / 1000.0, budget.ForeignAlcohol / 1000.0);
                else Report("alcohol_unavailable", 10, "diag_budget_detail", budget.Needed(AlcoholBudget.Percent(inputs.alcoholPercent)) / 1000.0, budget.Available / 1000.0);
                yield break;
            }
            if (inputs.mashBuffer != null && ChemistryOperations.Total(inputs.mashBuffer) - inputs.reservedBufferVolume > WaterHelper.GetFreeCapacity(inputs.chamber))
            { Capacity("buffer_return_full", inputs.chamber, ChemistryOperations.Total(inputs.mashBuffer) - inputs.reservedBufferVolume, 10); yield break; }
            var routes = new List<Route>();
            var seeds = new List<Route>();
            foreach (var ignored in SelectFeedstocks(seeds)) yield return null;
            foreach (var seed in seeds)
            {
                var prepared = new List<Route>();
                BeginWholeFeed(seed, prepared);
                foreach (var ignored in PrepareAlternatives(seed, prepared)) yield return null;
                preparedFeeds[FeedKey(seed)] = prepared;
                EndWholeFeed(prepared);
                routes.AddRange(prepared);
            }
            Stage = "plan_grade";
            GradeSelection selection = null;
            foreach (int grade in GradePolicy.Grades)
            {
                if (grade > inputs.requestedGrade) continue;
                outcomes.Clear();
                evaluatingGrade = grade;
                winningOutcome = null;
                winningParts = null;
                // Even completely pure input cannot exceed the finisher's ceiling.
                if (grade <= 100 - inputs.finisherLoss)
                {
                    // Meter ready-made compounds for this exact grade. Keeping
                    // lower quality stock outside the tank must precede fallback.
                    var blends = new List<Route>();
                    foreach (var ignored in SelectDirectBlends(grade, blends)) yield return null;
                    foreach (var blend in blends)
                        foreach (var ignored in PrepareAlternatives(blend, routes)) yield return null;
                    Stage = "plan_grade";
                    foreach (var route in routes)
                        foreach (var ignored in EvaluateFeed(route, grade)) yield return null;
                    foreach (var ignored in ExploreFeedAmounts(seeds, routes, grade)) yield return null;
                }
                var candidate = GradePolicy.Select(grade, outcomes);
                var best = candidate != null && candidate.ActualGrade == grade ? candidate.Outcome : null;
                var attempt = new GradeAttempt(grade, best);
                GradeAttempts.Add(attempt);
                yield return null;
                if (attempt.BestOutcome != null)
                {
                    selection = new GradeSelection(inputs.requestedGrade, attempt.Grade, attempt.BestOutcome);
                    break;
                }
            }
            if (selection == null)
            {
                if (routes.Count > 0) ExplainNoGrade();
                else if (Diagnostics.Primary == null) Report("no_raw");
                Error = Diagnostics.Primary.Key;
                yield break;
            }
            Result = ((BatchPlan)selection.Outcome.Plan).Copy();
            Result.actualGrade = selection.ActualGrade;
            Result.expectedBottles = selection.Outcome.Bottles;
            Result.outputCapacity = inputs.maxProducts;
            Result.outputLimited = OutputCapacity.DryProducts(winningParts, recipe) > Result.expectedBottles;
            Result.minimumActive = selection.Outcome.MinimumBottleActiveVolume;
            foreach (var ignored in MatchReadyStockGrade()) yield return null;
            var final = new List<BatchAction>(Result.actions);
            final.Add(new BatchAction(Operation.Dry) { evaporationVolume = Liquid.Liquids
                .Select((liquid, index) => liquid.isEvaporative ? winningParts[index] : 0).Sum() });
            final.Add(new BatchAction(Operation.Form, amount: Result.expectedBottles));
            Result.actions = final.ToArray();
            Result.recoveredVolume = RecoveredSolvent.DrawnVolume(Result, inputs.recoveredSource);
            Result.concentrationHint = ConcentrationAdvice.Get(inputs, Result, winningParts,
                bestFailedPurity > Result.minimumActive ? bestFailedParts : null);
            foreach (var ignored in PartialFeedAdvice(Result)) yield return null;
            Error = null;
            Diagnostics.Clear();
            if (!inputs.skipDryingComparison)
            {
                Stage = "normal_drying_check";
                var drying = new NormalDryingPlanner(inputs, Result);
                var work = drying.Search();
                while (work.MoveNext()) yield return null;
                Result = inputs.normalDrying == DirectDryingMethod.Heat ? drying.Heating : drying.Evaporation;
                if (Result == null) Report(drying.Evaporation.normalHeatReason ?? "normal_heat_check_failed", 100);
            }
        }

        private IEnumerable<int> Prepare(Route seed, List<Route> routes)
        {
            Stage = "plan_extract";
            var production = new List<Route>();
            foreach (var ignored in Produce(seed, production)) yield return 0;
            Stage = "plan_cleanup";
            // Three genuinely different choices at most: keep soluble impurities,
            // precipitate with available acid, or with available base. Doses are
            // calculated from composition; they are never searched as a range.
            foreach (var produced in production)
            foreach (string treatment in new[] { "", Liquid.LIQUID_ACID, Liquid.LIQUID_BASE })
            {
                var batch = new SimulationItem(produced.batch);
                var supplies = produced.supplies.Select(x => new SimulationItem(x)).ToArray();
                var prefix = new List<BatchAction>(produced.plan.actions);
                int used = produced.plan.reagents, reactionTicks = 0;
                int fat = ChemistryOperations.Amount(batch, "fat");
                if (fat > 0)
                {
                    // Lipobond boils at 55 C. Coffee extraction can leave the
                    // tank at 65/90 C; dosing immediately burns off the reagent
                    // before native sedimentation finishes removing the fat.
                    // Cooling is an actual, replayable wait with native reactions.
                    if (supplies.Any(x => recipe.CanUseReagent(x) && ChemistryOperations.Amount(x, "lipobond_agent") > 0))
                    {
                        double ceiling = Liquid.Liquids.First(x => x.identifier == "lipobond_agent").boilingPoint;
                        while (ChemistryOperations.Temperature(batch) > ceiling && reactionTicks < 900)
                        {
                            ChemistryOperations.ReactionTick(batch);
                            if (++reactionTicks % 8 == 0) yield return 0;
                        }
                        if (ChemistryOperations.Temperature(batch) > ceiling) { Report("diag_cooling_timeout", 25); continue; }
                        if (reactionTicks > 0)
                            prefix.Add(new BatchAction(Operation.React, amount: reactionTicks) { precursorId = "fixed_wait" });
                    }
                    fat = ChemistryOperations.Amount(batch, "fat");
                    Dose(batch, supplies, "lipobond_agent", Math.Max(0, (fat + 7) / 8 - ChemistryOperations.Amount(batch, "lipobond_agent")), prefix, ref used, requireAll: false, policy: produced.reagentPolicy);
                    DoseFailure(new Route { batch = batch, supplies = supplies, reagentPolicy = produced.reagentPolicy },
                        "lipobond_agent", (fat + 7) / 8, 30);
                }
                if (treatment.Length > 0)
                {
                    if (CleanupDemand(batch) == 0 || !supplies.Any(x => IngredientAmount(x, treatment) > 0)) continue;
                    // Small corrections account for native per-tick rounding and
                    // contaminant consumption. Each addition fills an actual deficit.
                    for (int correction = 0; correction < 8; correction++)
                    {
                        int demand = CleanupDemand(batch);
                        int wanted = Math.Max(0, demand - IngredientAmount(batch, treatment));
                        int before = used;
                        Dose(batch, supplies, treatment, wanted, prefix, ref used, requireAll: false, policy: produced.reagentPolicy);
                        DoseFailure(new Route { batch = batch, supplies = supplies, reagentPolicy = produced.reagentPolicy }, treatment, demand, 30);
                        int wait = Settle(batch);
                        if (wait < 0) { Report("diag_cleanup_timeout", 30); reactionTicks = -1; break; }
                        reactionTicks += wait;
                        if (wait > 0) prefix.Add(new BatchAction(Operation.React, amount: wait));
                        yield return 0;
                        if (CleanupDemand(batch) == 0 || before == used) break;
                    }
                }
                else
                {
                    int wait = Settle(batch);
                    if (wait < 0) { Report("diag_cleanup_timeout", 30); continue; }
                    reactionTicks += wait;
                    if (wait > 0) prefix.Add(new BatchAction(Operation.React, amount: wait));
                    yield return 0;
                }
                if (reactionTicks < 0) continue;
                int imbalance = IngredientAmount(batch, Liquid.LIQUID_ACID);
                if (imbalance != 0)
                {
                    Dose(batch, supplies, imbalance > 0 ? Liquid.LIQUID_BASE : Liquid.LIQUID_ACID,
                        Math.Abs(imbalance), prefix, ref used, requireAll: false, policy: produced.reagentPolicy);
                    DoseFailure(new Route { batch = batch, supplies = supplies, reagentPolicy = produced.reagentPolicy },
                        imbalance > 0 ? Liquid.LIQUID_BASE : Liquid.LIQUID_ACID, 0, 30);
                    int wait = Settle(batch);
                    if (wait < 0) { Report("diag_cleanup_timeout", 30); continue; }
                    reactionTicks += wait;
                    if (wait > 0) prefix.Add(new BatchAction(Operation.React, amount: wait));
                }
                routes.Add(new Route { batch = batch, supplies = supplies, reagentPolicy = produced.reagentPolicy,
                    plan = new BatchPlan { productId = recipe.Id, requestedGrade = inputs.requestedGrade, actions = prefix.ToArray(),
                        alcoholPercent = produced.plan.alcoholPercent, extractionAlcohol = produced.plan.extractionAlcohol,
                        extractionPortions = produced.plan.extractionPortions,
                        reactionSeconds = produced.plan.reactionSeconds + reactionTicks, reagents = used } });
                yield return 0;
            }

        }

        private static Route CloneRoute(Route source) => new Route { batch = new SimulationItem(source.batch),
            supplies = source.supplies.Select(x => new SimulationItem(x)).ToArray(), plan = source.plan.Copy(), reagentPolicy = source.reagentPolicy };

        private static void AddStep(Route route, BatchAction action)
        {
            route.plan.actions = route.plan.actions.Concat(new[] { action }).ToArray();
            if (ReactionProgress.IsTimed(action.operation))
                route.plan.reactionSeconds += action.amount;
        }

        private void AddReagent(Route route, string ingredient, int wanted)
        {
            var actions = new List<BatchAction>(route.plan.actions);
            int used = route.plan.reagents;
            Dose(route.batch, route.supplies, ingredient, wanted, actions, ref used, requireAll: false, policy: route.reagentPolicy);
            route.plan.actions = actions.ToArray();
            route.plan.reagents = used;
        }

        private IEnumerable<int> Produce(Route seed, List<Route> result)
        {
            var extracted = new List<Route>();
            int mush = recipe.Mush == null ? 0 : ChemistryOperations.Amount(seed.batch, recipe.Mush);
            if (mush > 0 && AlcoholBudget.Supports(recipe) && AlcoholBudget.Percent(inputs.alcoholPercent) < 100)
            {
                foreach (var ignored in ProduceStaged(seed, extracted)) yield return 0;
            }
            else if (mush > 0)
            {
                foreach (string solvent in recipe.Id == "caffeine_pill" ? new[] { "alcohol", "water" } : new[] { "alcohol" })
                {
                    var route = CloneRoute(seed);
                    int target = solvent == "water" ? 90 : AlcoholBudget.Temperature(recipe);
                    long demand = solvent == "water" ? 5L * Math.Max(1, (int)Math.Round(mush * .05)) : AlcoholBudget.FullDose(recipe, mush);
                    int before = ChemistryOperations.Amount(route.batch, solvent);
                    AddReagent(route, solvent, (int)Math.Max(0, Math.Min(int.MaxValue, demand - before)));
                    if (solvent == "alcohol" && inputs.alcoholPercent != 0 && AlcoholBudget.Supports(recipe) && ChemistryOperations.Amount(route.batch, solvent) < demand)
                    { DoseFailure(route, solvent, (int)Math.Min(int.MaxValue, demand), 20); continue; }
                    if (solvent == "alcohol") SolventAdded = Math.Max(SolventAdded, ChemistryOperations.Amount(route.batch, solvent) - before);
                    if (ChemistryOperations.Amount(route.batch, solvent) == 0) { DoseFailure(route, solvent, 1, 20); continue; }
                    int ticks = 0;
                    while (ChemistryOperations.Amount(route.batch, recipe.Mush) > 0 && ticks < 900)
                    {
                        ChemistryOperations.SimulateHeatedSecond(route.batch, inputs.heaterUpgrades, target);
                        if (++ticks % 8 == 0) yield return 0;
                    }
                    if (ChemistryOperations.Amount(route.batch, recipe.Mush) > 0) { Report("extraction_failed", 25); continue; }
                    route.plan.extractionAlcohol = solvent == "alcohol" ? ChemistryOperations.Amount(route.batch, solvent) : 0;
                    route.plan.extractionPortions = 1;
                    AddStep(route, new BatchAction(Operation.Extract, amount: ticks, targetTemperature: target) { precursorId = recipe.Mush });
                    extracted.Add(route);
                }
                if (extracted.Count == 0 && ChemistryOperations.Amount(seed.batch, recipe.Active) > 0) extracted.Add(CloneRoute(seed));
            }
            else extracted.Add(CloneRoute(seed));

            foreach (var route in extracted)
            {
                if (recipe.Id == "fanta_pill" && ChemistryOperations.Amount(route.batch, "methylol") > 0)
                {
                    if (inputs.heaterUpgrades == 0)
                    {
                        Report("heater_upgrade_required", 35);
                        if (ChemistryOperations.Amount(route.batch, recipe.Active) > 0) result.Add(route);
                        continue;
                    }
                    Stage = "plan_synthesize";
                    foreach (string treatment in new[] { "", "acid", "base" })
                    {
                        var prepared = CloneRoute(route);
                        int stabilizer = ChemistryOperations.Amount(prepared.batch, "stabilizing_agent");
                        if (treatment.Length > 0)
                        {
                            if (stabilizer == 0 || !prepared.supplies.Any(x => IngredientAmount(x, treatment) > 0)) continue;
                            // Native sedimentation removes 20% of the available
                            // reacting amount while consuming 5% acid/base. Dose
                            // from that 4:1 ratio, then correct actual deficits at
                            // the native minimum-step/rounding boundaries.
                            for (int n = 0; n < 512 && stabilizer > 0; n++)
                            {
                                AddReagent(prepared, treatment, Math.Max(0, Math.Max(100, (stabilizer + 3) / 4) - IngredientAmount(prepared.batch, treatment)));
                                if (ChemistryOperations.Amount(prepared.batch, "acid") + ChemistryOperations.Amount(prepared.batch, "base") == 0) break;
                                ChemistryOperations.ReactionTick(prepared.batch);
                                AddStep(prepared, new BatchAction(Operation.React, amount: 1) { precursorId = "fixed_wait" });
                                int next = ChemistryOperations.Amount(prepared.batch, "stabilizing_agent");
                                if (next >= stabilizer) break;
                                stabilizer = next;
                                yield return 0;
                            }
                        }
                        foreach (var ignored in Synthesize(prepared, "methylol", 125)) yield return 0;
                        if (!prepared.synthesisTimedOut && ChemistryOperations.Amount(prepared.batch, recipe.Active) > 0) result.Add(prepared);
                    }
                }
                else if (recipe.Id == "pink_alt_injector" && ChemistryOperations.Amount(route.batch, "dream_cap_extract") > 0)
                {
                    Stage = "plan_synthesize";
                    // The native conversion is inhibited by alcohol. Retain the
                    // original evaporator cycle and its power cost between stages.
                    if (ChemistryOperations.VolatileVolume(route.batch) > 0)
                    {
                        AddStep(route, new BatchAction(Operation.Dry) { evaporationVolume = ChemistryOperations.VolatileVolume(route.batch) });
                        ChemistryOperations.SimulateDry(route.batch);
                        RecoveredSolvent.AfterEvaporation(route.supplies, inputs.recoveredSource);
                        route.batch.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
                    }
                    var availableCatalyst = CloneRoute(route);
                    foreach (var ignored in Synthesize(route, "dream_cap_extract", 52)) yield return 0;
                    if (!route.synthesisTimedOut && ChemistryOperations.Amount(route.batch, recipe.Active) > 0) result.Add(route);
                    // If full-speed catalyst cannot be supplied, also consider
                    // using catalyst already carried by the required reducer.
                    // Otherwise chasing a trace in that bottle can withdraw a
                    // huge surplus of reducer and ruin a valid, slower batch.
                    if (route.catalystLimited)
                    {
                        foreach (var ignored in Synthesize(availableCatalyst, "dream_cap_extract", 52, topUpCatalyst: false)) yield return 0;
                        if (!availableCatalyst.synthesisTimedOut && ChemistryOperations.Amount(availableCatalyst.batch, recipe.Active) > 0)
                            result.Add(availableCatalyst);
                    }
                }
                else result.Add(route);
            }
        }

        private IEnumerable<int> Synthesize(Route route, string precursor, int target, bool topUpCatalyst = true)
        {
            int segment = 0, ticks = 0;
            while (ChemistryOperations.Amount(route.batch, precursor) > 0 && ticks < 900)
            {
                int amount = SynthesisRemaining(route.batch, precursor);
                if (amount <= 0)
                {
                    Report("missing_stabilizer_reagent", 35);
                    int demand = Math.Max(100, (ChemistryOperations.Amount(route.batch, "stabilizing_agent") + 3) / 4);
                    DoseFailure(route, "acid", demand, 34);
                    DoseFailure(route, "base", demand, 34);
                    break;
                }
                int required = precursor == "methylol" ? (int)(((long)amount * 3 + 3) / 4) : amount;
                TopUpSynthesisReagent(route, "reducing_agent", required, precursor, target, ref segment);
                if (ChemistryOperations.Amount(route.batch, "reducing_agent") == 0) { DoseFailure(route, "reducing_agent", required, 40); break; }
                if (precursor == "dream_cap_extract")
                {
                    // Native Nomore catalysis reaches full speed at extract/4;
                    // it does not consume lipobond, but fat sedimentation does.
                    // Re-read after the reducer pour: mixed reagents can carry
                    // catalyst, fat or more precursor. A trace is not enough.
                    amount = ChemistryOperations.Amount(route.batch, precursor);
                    int catalyst = (amount + 3) / 4 + (ChemistryOperations.Amount(route.batch, "fat") + 7) / 8;
                    if (topUpCatalyst || ChemistryOperations.Amount(route.batch, "lipobond_agent") == 0)
                        TopUpSynthesisReagent(route, "lipobond_agent", catalyst, precursor, target, ref segment);
                    if (ChemistryOperations.Amount(route.batch, "lipobond_agent") < catalyst) route.catalystLimited = true;
                    if (ChemistryOperations.Amount(route.batch, "lipobond_agent") == 0) { DoseFailure(route, "lipobond_agent", catalyst, 40); break; }
                }
                if (precursor == "dream_cap_extract" && (long)ChemistryOperations.Amount(route.batch, "alcohol") * 10 > ChemistryOperations.Total(route.batch))
                { Report("alcohol_inhibits_recipe", 45, "diag_alcohol_inhibits_detail", 100.0 * ChemistryOperations.Amount(route.batch, "alcohol") / ChemistryOperations.Total(route.batch)); break; }
                ChemistryOperations.SimulateHeatedSecond(route.batch, inputs.heaterUpgrades, target);
                segment++; ticks++;
                if (ticks % 8 == 0) yield return 0;
            }
            // The simulation guard is not a chemical stopping condition. Do not
            // turn an unfinished, safety-truncated reaction into a low-grade
            // candidate (or let it lose to one unit of ready-made active stock).
            if (ticks >= 900 && SynthesisRemaining(route.batch, precursor) > 0)
            {
                route.synthesisTimedOut = true;
                Report("synthesis_timeout", 45);
            }
            if (segment > 0)
                AddStep(route, new BatchAction(Operation.Synthesize, amount: segment, targetTemperature: target)
                    { precursorId = precursor, endAmount = ChemistryOperations.Amount(route.batch, precursor) });
        }

        private static int SynthesisRemaining(GameItem batch, string precursor) => ChemistryOperations.Amount(batch, precursor)
            - (precursor == "methylol" ? ChemistryOperations.Amount(batch, "stabilizing_agent") : 0);

        private void TopUpSynthesisReagent(Route route, string ingredient, int required, string precursor, int target, ref int segment)
        {
            int missing = required - ChemistryOperations.Amount(route.batch, ingredient);
            if (missing <= 0 || WaterHelper.GetFreeCapacity(route.batch) <= 0 ||
                !route.supplies.Any(x => IngredientAmount(x, ingredient) > 0)) return;
            // End the existing heat segment before inserting a real pour. The
            // recorded boundary is replayed identically after save/resume.
            if (segment > 0)
            {
                AddStep(route, new BatchAction(Operation.Synthesize, amount: segment, targetTemperature: target)
                    { precursorId = precursor, endAmount = ChemistryOperations.Amount(route.batch, precursor) });
                segment = 0;
            }
            AddReagent(route, ingredient, missing);
        }

        private IEnumerable<int> FindFiltrationPlans(SimulationItem batch, SimulationItem[] supplies, BatchPlan basePlan, int grade)
        {
            var unfiltered = AddOutcome(batch, basePlan);
            yield return 0;
            // Filtering only removes material with these extract-safe filters.
            // An already-qualified batch cannot gain bottles by further filtering.
            if (GradePolicy.Meets(unfiltered, grade)) yield break;
            var orders = FiltrationPolicy.Orders(supplies).ToArray();
            foreach (var order in orders)
                foreach (var ignored in FilterInOrder(batch, supplies, basePlan, grade, order)) yield return 0;
            if (orders.Length > 1)
            {
                // Filter only the required portion through cloth, then the fine
                // filter, before returning it. This avoids coarse-filtering all
                // the filler when only part of the batch needs purification.
                foreach (var ignored in FilterInOrder(batch, supplies, basePlan, grade,
                    orders.Last().Where(i => !FiltrationPolicy.Coarse(supplies[i])).ToArray(), true)) yield return 0;
            }
        }

        private IEnumerable<int> FilterInOrder(SimulationItem batch, SimulationItem[] supplies, BatchPlan basePlan, int grade, int[] filters, bool clothFirst = false)
        {
            var current = new SimulationItem(batch);
            var stock = (SimulationItem[])supplies.Clone();
            var plan = basePlan.Copy();
            foreach (int index in filters)
            {
                while (stock[index].identifier == "uv_filter" || DurabilityHelper.GetRealDurability(stock[index]) > 0)
                {
                    int volume = ChemistryOperations.Total(current);
                    var full = FiltrationPolicy.Evaluate(current, stock, plan, index, volume, clothFirst);
                    if (ChemistryOperations.SameParts(ChemistryOperations.Parts(full.batch), ChemistryOperations.Parts(current))) break;
                    var fullOutcome = AddOutcome(full.batch, full.plan);
                    yield return 0;
                    if (GradePolicy.Meets(fullOutcome, grade))
                    {
                        // Only solve the current target's removal threshold, not
                        // every grade for every solvent dose. Keep exact bottle
                        // rounding in the checks around the threshold.
                        int low = 0, high = volume;
                        while (low < high)
                        {
                            int middle = low + (high - low) / 2;
                            var trial = FiltrationPolicy.Evaluate(current, stock, plan, index, middle, clothFirst);
                            var measured = Measure(trial.batch, trial.plan);
                            if (GradePolicy.Meets(measured, grade)) high = middle;
                            else low = middle + 1;
                            yield return 0;
                        }
                        // Check the neighborhood explicitly for the game's proportional rounding.
                        for (int amount = Math.Max(0, low - 2); amount <= Math.Min(volume, low + 2); amount++)
                        {
                            var trial = FiltrationPolicy.Evaluate(current, stock, plan, index, amount, clothFirst);
                            AddOutcome(trial.batch, trial.plan);
                            yield return 0;
                        }
                        yield break;
                    }
                    current = full.batch;
                    stock = full.supplies;
                    plan = full.plan;
                    yield return 0;
                }
            }
        }

        private ProductionOutcome Measure(GameItem batch, BatchPlan plan)
        {
            string key = string.Join(",", ChemistryOperations.Parts(batch));
            if (!measurementCache.TryGetValue(key, out var measured))
            {
                measured = ChemistryOperations.Measure(batch, inputs.finisherLoss, 0, 0, null, recipe, inputs.maxProducts);
                measurementCache.Add(key, measured);
                CandidatesChecked++;
            }
            return new ProductionOutcome(measured.ActiveVolume, measured.TotalVolume, measured.Bottles,
                measured.MinimumBottleActiveVolume, plan.reagents, plan.reactionSeconds, plan, recipe.UnitVolume,
                plan.fineFilterWear, plan.coarseFilterWear);
        }
        private ProductionOutcome AddOutcome(GameItem batch, BatchPlan plan)
        {
            var measured = Measure(batch, plan);
            outcomes.Add(measured);
            RecordWholeFeed(batch, measured);
            // Keep only the winning composition for advice. Do not replay the
            // search or inspect unused stock as if it had entered this batch.
            if (GradePolicy.Meets(measured, evaluatingGrade) && GradePolicy.Better(measured, winningOutcome))
            {
                winningOutcome = measured;
                winningParts = ChemistryOperations.Parts(batch);
            }
            TrackFailedOutcome(batch, measured);
            return measured;
        }
        private static int CleanupDemand(GameItem batch) => ChemistryOperations.Amount(batch, Liquid.LIQUID_PROTEIN)
            + (ChemistryOperations.Amount(batch, Liquid.LIQUID_MICROBE) + 99) / 100
            + (ChemistryOperations.Amount(batch, Liquid.LIQUID_ORGANIC_WASTE) + 39) / 40;
        private static int IngredientAmount(GameItem item, string ingredient)
        {
            int amount = ChemistryOperations.Amount(item, ingredient);
            if (ingredient == Liquid.LIQUID_ACID) amount -= ChemistryOperations.Amount(item, Liquid.LIQUID_BASE);
            if (ingredient == Liquid.LIQUID_BASE) amount -= ChemistryOperations.Amount(item, Liquid.LIQUID_ACID);
            return amount;
        }
        private static BatchPlan Append(BatchPlan plan, BatchAction action)
        {
            var result = plan.Copy();
            result.actions = result.actions.Concat(new[] { action }).ToArray();
            return result;
        }
        private static int Settle(GameItem batch)
        {
            int unchanged = 0;
            for (int ticks = 0; ticks < 900; ticks++)
            {
                var before = ChemistryOperations.Parts(batch);
                ChemistryOperations.ReactionTick(batch);
                unchanged = ChemistryOperations.SameParts(before, ChemistryOperations.Parts(batch)) ? unchanged + 1 : 0;
                if (unchanged >= 2) return ticks + 1;
            }
            return -1;
        }
        private bool Dose(GameItem batch, SimulationItem[] supplies, string ingredient, int wanted,
            List<BatchAction> actions, ref int used, bool requireAll = true, int policy = 0)
        {
            if (wanted <= 0) return true;
            var indices = DoseSources(supplies, ingredient, policy).ToArray();
            // Mushroom-bearing liquids are selected as feed first. Using an
            // unselected mash bottle as auxiliary alcohol would silently change
            // the very mush quantity on which the chosen percentage is based.
            if (requireAll && indices.Sum(i => IngredientAmount(supplies[i], ingredient)) < wanted) return false;
            foreach (int i in indices)
            {
                int available = IngredientAmount(supplies[i], ingredient);
                int volume = ChemistryOperations.Total(supplies[i]);
                int take = (int)Math.Min(volume, ((long)wanted * volume + available - 1) / available);
                int free = WaterHelper.GetFreeCapacity(batch);
                if (take > free && requireAll) return false;
                take = Math.Min(take, free);
                if (take <= 0) continue;
                int before = IngredientAmount(batch, ingredient);
                ChemistryOperations.Pour(supplies[i], batch, take);
                wanted -= IngredientAmount(batch, ingredient) - before;
                actions.Add(new BatchAction(Operation.Pour, i, take));
                used = checked(used + take);
                if (wanted <= 0) return true;
            }
            return !requireAll;
        }
    }
}
