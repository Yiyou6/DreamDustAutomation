using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class RecoveredSolvent
    {
        internal static GameItem[] Supplies(IEnumerable<GameItem> bin, GameItem collector, GameItem chamber, out int source)
        {
            // Plain impure water is direct-forming feed, never a purification
            // solvent. Real reagent mixtures retain their full composition.
            var items = bin.Where(item => (InventoryRules.IntakeLiquid(item) &&
                (!DirectFormInputs.CanUseLiquid(item) || DirectFormInputs.HasReagents(item))) ||
                item.IsTag("WATER_FILTER")).Distinct().ToList();
            source = -1;
            if (collector != null && collector != chamber && InventoryRules.Liquid(collector) && ChemistryOperations.Amount(collector, "alcohol") > 0)
            {
                source = items.IndexOf(collector);
                if (source < 0) { source = items.Count; items.Add(collector); }
            }
            return items.ToArray();
        }

        // Include empty reserved containers too: pouring the last drop must not
        // lose their identity across execution or a save/load boundary.
        internal static IEnumerable<GameItem> Sources(IEnumerable<GameItem> bin, GameItem collector) =>
            collector == null ? bin : bin.Concat(new[] { collector }).Distinct();

        internal static int DrawnVolume(BatchPlan plan, int source) => source < 0 ? 0 :
            plan.actions.Where(step => step.operation == Operation.Pour && step.source == source).Sum(step => step.amount);

        internal static void AfterEvaporation(SimulationItem[] supplies, int source)
        {
            if (source < 0) return;
            // The native evaporator can change the collector's mixture. Only
            // pre-evaporation withdrawals may use its initial snapshot; fresh
            // distillate and the remaining mixture are planned next batch.
            foreach (var liquid in Liquid.Liquids) ChemistryOperations.SetAmount(supplies[source], liquid.identifier, 0);
        }
    }
}
