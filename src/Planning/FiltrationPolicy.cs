using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class FiltrationPolicy
    {
        // Native cloth has cutoff 150, cartridges/reed filters 22, and active
        // medicine particles 5. UV has a separate, durability-free native rule.
        internal static bool Usable(GameItem filter) => filter != null && filter.IsTag("WATER_FILTER") &&
            (filter.identifier == "uv_filter" || (filter.GetTagReadonly("FILTER_SIZE_TAG").GetInt() > 5 &&
                DurabilityHelper.GetRealDurability(filter) > 0));
        internal static bool Coarse(GameItem filter) => filter.identifier != "uv_filter" &&
            filter.GetTagReadonly("FILTER_SIZE_TAG").GetInt() > 22;
        internal static int Wear(GameItem before, GameItem after) => before.identifier == "uv_filter" ? 0 :
            Math.Max(0, DurabilityHelper.GetRealDurability(before) - DurabilityHelper.GetRealDurability(after));

        internal static IEnumerable<int[]> Orders(SimulationItem[] supplies)
        {
            var available = Enumerable.Range(0, supplies.Length).Where(i => Usable(supplies[i])).ToArray();
            var fineFirst = available.OrderBy(i => supplies[i].identifier == "uv_filter" ? 0 : 1)
                .ThenBy(i => supplies[i].GetTagReadonly("FILTER_SIZE_TAG").GetInt())
                .ThenBy(i => supplies[i].identifier == "water_filter_adv" ? 0 : 1).ToArray();
            if (available.Any(i => Coarse(supplies[i])) && available.Any(i => !Coarse(supplies[i]) && supplies[i].identifier != "uv_filter"))
            {
                // Exactly one extra ordering, not permutations of all supplies.
                // Keep the original route as a candidate: full coarse filtering
                // can remove too much filler and lose an otherwise valid unit.
                yield return available.OrderBy(i => supplies[i].identifier == "uv_filter" ? 0 : 1)
                    .ThenByDescending(i => supplies[i].GetTagReadonly("FILTER_SIZE_TAG").GetInt())
                    .ThenBy(i => supplies[i].identifier == "water_filter_adv" ? 0 : 1).ToArray();
            }
            yield return fineFirst;
        }

        internal sealed class Trial
        {
            internal SimulationItem batch;
            internal SimulationItem[] supplies;
            internal BatchPlan plan;
        }

        internal static Trial Evaluate(GameItem batch, SimulationItem[] supplies, BatchPlan plan, int source, int volume, bool clothFirst)
        {
            var indices = clothFirst && !Coarse(supplies[source]) && supplies[source].identifier != "uv_filter" ?
                Enumerable.Range(0, supplies.Length).Where(i => Coarse(supplies[i]) && Usable(supplies[i])).Concat(new[] { source }).ToArray() : new[] { source };
            var trial = new Trial { batch = new SimulationItem(batch), supplies = (SimulationItem[])supplies.Clone(), plan = plan.Copy() };
            foreach (int index in indices) trial.supplies[index] = new SimulationItem(supplies[index]);
            ChemistryOperations.FilterAliquot(trial.batch, indices.Select(i => (GameItem)trial.supplies[i]).ToArray(), volume);
            var used = indices.Where(i => Wear(supplies[i], trial.supplies[i]) > 0 || supplies[i].identifier == "uv_filter").ToArray();
            if (used.Length == 0) return trial;
            trial.plan.actions = trial.plan.actions.Concat(new[] { new BatchAction(Operation.Filter, used.Last(), volume) {
                preFilters = used.Length > 1 ? used.Take(used.Length - 1).ToArray() : null } }).ToArray();
            foreach (int index in used)
                if (Coarse(supplies[index])) trial.plan.coarseFilterWear += Wear(supplies[index], trial.supplies[index]);
                else trial.plan.fineFilterWear += Wear(supplies[index], trial.supplies[index]);
            return trial;
        }

        internal static void Apply(GameItem tank, BatchAction action, Func<int, GameItem> source) =>
            ChemistryOperations.FilterAliquot(tank, action.FilterSources.Select(source).ToArray(), action.amount);

        internal static void Recount(BatchPlan plan, GameItem[] before, SimulationItem[] after)
        {
            plan.coarseFilterWear = plan.fineFilterWear = 0;
            foreach (int index in plan.actions.Where(a => a.operation == Operation.Filter).SelectMany(a => a.FilterSources).Distinct())
            {
                int wear = Wear(before[index], after[index]);
                if (Coarse(before[index])) plan.coarseFilterWear += wear;
                else plan.fineFilterWear += wear;
            }
        }
    }
}
