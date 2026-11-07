using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class PlanningIssue
    {
        internal string Key, Detail;
        internal object[] Args;
        internal int Stage;
        internal object[] SummaryArgs => Key.StartsWith("diag_reagent_") ? Args.Take(1).ToArray() : new object[0];
        internal string Context => !Key.StartsWith("diag_reagent_") ? null : Stage >= 40 ? "diag_step_synthesis" :
            Stage >= 34 ? "diag_step_stabilizer" : Stage >= 30 ? "diag_step_cleanup" : "diag_step_extraction";
    }

    // Keep evidence from failed alternatives, not whichever error ran last.
    // Diagnostics never change candidates, reagent doses or live inventory.
    internal sealed class PlanningDiagnostics
    {
        internal readonly List<PlanningIssue> Issues = new List<PlanningIssue>();
        internal PlanningIssue Primary => Issues.OrderByDescending(x => x.Stage).FirstOrDefault();
        internal void Add(string key, int stage = 0, string detail = null, params object[] args)
        {
            if (Issues.Any(x => x.Key == key && x.Detail == detail && x.Stage == stage &&
                (x.Args.Length == 0 || args.Length == 0 || Equals(x.Args[0], args[0])))) return;
            Issues.Add(new PlanningIssue { Key = key, Stage = stage, Detail = detail, Args = args });
        }
        internal void Clear() => Issues.Clear();
        internal void CopyFrom(PlanningDiagnostics other)
        {
            if (ReferenceEquals(this, other)) return;
            Clear();
            foreach (var issue in other.Issues) Add(issue.Key, issue.Stage, issue.Detail, issue.Args);
        }
        internal string Describe(Func<string, object[], string> text) => string.Join("\n\n", Issues
            .OrderByDescending(x => x.Stage).Select(x => (x.Context == null ? "" : text(x.Context, new object[0]) + "\n") + text(x.Key, x.SummaryArgs) +
                (x.Detail == null ? "" : "\n" + text(x.Detail, x.Args))));
    }

    internal sealed partial class BatchPlanner
    {
        internal PlanningDiagnostics Diagnostics { get; } = new PlanningDiagnostics();
        private void Report(string key, int stage = 0, string detail = null, params object[] args)
        {
            Diagnostics.Add(key, stage, detail, args);
            Error = Diagnostics.Primary.Key;
        }
        private void Capacity(string key, GameItem container, long needed, int stage)
        {
            Report(key, stage, "diag_capacity_detail", needed / 1000.0,
                WaterHelper.GetFreeCapacity(container) / 1000.0,
                ChemistryOperations.Total(container) / 1000.0, WaterHelper.GetTotalCapacity(container) / 1000.0);
        }
        private static string PartName(string id) => Liquid.Liquids.First(x => x.identifier == id).displayName;
        private IEnumerable<int> DoseSources(GameItem[] stock, string ingredient, int policy)
        {
            var indices = RankReagents(stock, ingredient, policy);
            if (ingredient == "alcohol" && inputs.alcoholPercent != 0 && AlcoholBudget.Supports(recipe))
                indices = indices.Where(i => ChemistryOperations.Amount(stock[i], recipe.Mush) == 0);
            return indices;
        }
        private void DoseFailure(Route route, string ingredient, int required, int stage)
        {
            int missing = Math.Max(0, required - IngredientAmount(route.batch, ingredient));
            if (missing == 0) return;
            // Re-run only the failed dose with unlimited room, using identical
            // ranking, source exclusions and native proportional rounding.
            var probe = new SimulationItem(route.batch);
            probe.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
            var stock = route.supplies.Select(x => new SimulationItem(x)).ToArray();
            int used = 0;
            Dose(probe, stock, ingredient, missing, new List<BatchAction>(), ref used, false, route.reagentPolicy);
            int obtained = IngredientAmount(probe, ingredient) - IngredientAmount(route.batch, ingredient);
            long available = DoseSources(route.supplies, ingredient, route.reagentPolicy)
                .Sum(i => (long)Math.Max(0, IngredientAmount(route.supplies[i], ingredient)));
            int free = WaterHelper.GetFreeCapacity(route.batch);
            // A branch may deliberately skip a treatment even though the stock
            // and vessel can supply it. Only a real shortage or room limit may
            // trigger the expensive smaller-feed search.
            if (obtained >= missing && used <= free) return;
            if (preparingFeed != null) preparingFeed.reagentLimited = true;
            if (obtained >= missing)
                Report("diag_reagent_capacity", stage, "diag_dose_capacity_detail", PartName(ingredient),
                    missing / 1000.0, used / 1000.0, free / 1000.0);
            else
            {
                long foreign = route.supplies.Where(x => !recipe.CanUseReagent(x)).Sum(x => (long)Math.Max(0, IngredientAmount(x, ingredient)));
                if (foreign > 0)
                {
                    Report("diag_reagent_foreign", stage, "diag_foreign_reagent_detail", PartName(ingredient), missing / 1000.0, available / 1000.0, foreign / 1000.0);
                    return;
                }
                long excluded = route.supplies.Sum(x => (long)Math.Max(0, IngredientAmount(x, ingredient))) - available;
                string key = excluded >= missing - obtained ? "diag_reagent_in_feed" : used > free ?
                    "diag_reagent_short_and_full" : "diag_reagent_short";
                Report(key, stage, "diag_dose_stock_detail", PartName(ingredient), missing / 1000.0,
                    available / 1000.0, Math.Max(0, excluded) / 1000.0, free / 1000.0);
            }
        }

        private int bestFailedPurity = -1, bestFailedDryVolume;
        private int[] bestFailedParts;
        private void TrackFailedOutcome(GameItem batch, ProductionOutcome outcome)
        {
            if (outcome.Bottles <= 0)
            {
                bestFailedDryVolume = Math.Max(bestFailedDryVolume, DryVolume(batch));
                return;
            }
            if (outcome.MinimumBottleActiveVolume <= bestFailedPurity) return;
            bestFailedPurity = outcome.MinimumBottleActiveVolume;
            bestFailedParts = ChemistryOperations.Parts(batch);
        }
        private void ExplainNoGrade()
        {
            if (bestFailedPurity < 0)
            {
                Report("diag_less_than_one", 42, "diag_one_unit_detail", bestFailedDryVolume / 1000.0, recipe.UnitVolume / 1000.0);
                return;
            }
            Report("no_grade", 42, "diag_grade_detail", 100.0 * bestFailedPurity / recipe.UnitVolume, 30);
            var advice = ConcentrationAdvice.Get(inputs, new BatchPlan { productId = recipe.Id, requestedGrade = 30, actualGrade = 0,
                minimumActive = bestFailedPurity }, bestFailedParts);
            if (advice != null) Diagnostics.Add(advice, 41, advice + "_action");
        }
    }

    // Known interrupted-batch conditions carry player-facing codes. Unexpected
    // exceptions remain explicitly unexpected and retain their full log trace.
    internal sealed class MachineFault : InvalidOperationException
    {
        internal string Key { get; }
        internal MachineFault(string key) : base(key) { Key = key; }
        internal MachineFault(string key, string detail) : base(key + ": " + detail) { Key = key; }
    }
}
