using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        // Ready-made medicine may exceed the selected trade grade. Meter spare
        // acid OR base after cleanup, then run native reactions and finishing.
        // Two bounded binary searches; no combinatorial dose enumeration.
        private IEnumerable<int> MatchReadyStockGrade()
        {
            int grade = Result.actualGrade;
            int upper = GradePolicy.Grades.Where(g => g > grade).DefaultIfEmpty(101).Min();
            if (grade == 99 || Result.minimumActive * 100L < recipe.UnitVolume * (long)upper ||
                Result.actions.Any(a => a.operation == Operation.Blend || a.operation == Operation.Extract || a.operation == Operation.Synthesize)) yield break;
            var start = new SimulationItem(inputs.chamber);
            BottlingOperations.WriteParts(start, winningParts);
            start.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
            var supplies = inputs.supplies.Select(x => new SimulationItem(x)).ToArray();
            foreach (var action in Result.actions.Where(a => a.operation == Operation.Pour))
                ChemistryOperations.RemoveAndReturn(supplies[action.source], action.amount);
            int capacity = WaterHelper.GetFreeCapacity(start);
            if (inputs.finisher != null) capacity = Math.Min(capacity, WaterHelper.GetTotalCapacity(inputs.finisher) - ChemistryOperations.Total(start));
            ProductionOutcome best = null; BatchPlan bestPlan = null; int[] bestParts = null;
            foreach (string ingredient in new[] { "acid", "base" })
            {
                var sources = Enumerable.Range(0, supplies.Length).Where(i =>
                    supplies[i].IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && !DirectFormInputs.HasMedicinalParts(supplies[i]) &&
                    IngredientAmount(supplies[i], ingredient) > 0 &&
                    (long)ChemistryOperations.Amount(supplies[i], ingredient) * 100 >= (long)ChemistryOperations.Total(supplies[i]) * 80).ToArray();
                int high = (int)Math.Min(Math.Max(0, capacity), sources.Sum(i => (long)ChemistryOperations.Total(supplies[i])));
                int low = 0;
                Func<int, Tuple<SimulationItem, BatchPlan, ProductionOutcome>> trial = dose =>
                {
                    var tank = new SimulationItem(start); var plan = Result.Copy();
                    var actions = new List<BatchAction>(plan.actions); int left = dose;
                    foreach (int i in sources)
                    {
                        int take = Math.Min(left, ChemistryOperations.Total(supplies[i]));
                        if (take == 0) continue;
                        ChemistryOperations.Pour(new SimulationItem(supplies[i]), tank, take);
                        actions.Add(new BatchAction(Operation.Pour, i, take)); left -= take;
                    }
                    int wait = Settle(tank);
                    if (wait < 0 || left > 0) return null;
                    if (wait > 0) actions.Add(new BatchAction(Operation.React, amount: wait));
                    plan.actions = actions.ToArray(); plan.reagents += dose; plan.dilutionVolume = dose; plan.reactionSeconds += wait;
                    var result = ChemistryOperations.Measure(tank, inputs.finisherLoss, plan.reagents, plan.reactionSeconds, plan, recipe, inputs.maxProducts);
                    return Tuple.Create(tank, plan, result);
                };
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    var candidate = trial(middle);
                    if (candidate != null && GradePolicy.Meets(candidate.Item3, grade)) low = middle; else high = middle - 1;
                    yield return 0;
                }
                if (low == 0) continue;
                var final = trial(low);
                if (final == null || final.Item3.Bottles < Result.expectedBottles || !GradePolicy.Meets(final.Item3, grade) ||
                    !AllUnitsBelow(final.Item1, final.Item3.Bottles, upper)) continue;
                if (GradePolicy.Better(final.Item3, best))
                { best = final.Item3; bestPlan = final.Item2; bestParts = ChemistryOperations.Parts(final.Item1); }
            }
            if (bestPlan == null) { Result.dilutionUnavailable = true; yield break; }
            Result = bestPlan; winningParts = bestParts;
            Result.minimumActive = best.MinimumBottleActiveVolume; Result.expectedBottles = best.Bottles;
            Result.outputLimited = OutputCapacity.DryProducts(winningParts, recipe) > best.Bottles;
            GradeAttempts[GradeAttempts.Count - 1] = new GradeAttempt(grade, best);
        }

        private bool AllUnitsBelow(GameItem tank, int count, int upper)
        {
            var dry = new SimulationItem(tank); ChemistryOperations.SimulateDry(dry);
            var parts = ChemistryOperations.Parts(dry);
            int active = Liquid.Liquids.FindIndex(l => l.identifier == recipe.Active);
            for (int n = 0; n < count; n++)
            {
                var unit = WaterHelper.SplitProportionally(parts, recipe.UnitVolume);
                for (int i = 0; i < parts.Length; i++) parts[i] -= unit[i];
                int loss = WaterHelper.SplitProportionally(unit, recipe.UnitVolume * inputs.finisherLoss / 100)[active];
                if ((long)(unit[active] - loss) * 100 >= (long)recipe.UnitVolume * upper) return false;
            }
            return true;
        }
    }
}
