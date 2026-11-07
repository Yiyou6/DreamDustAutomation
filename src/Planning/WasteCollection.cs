using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class WasteCollection
    {
        // This is storage, not disposal. The controller collects from input bins
        // only, never installed reaction/buffer/collector tanks. Do not classify
        // liquid residues or reusable filters as trash. Only the known native
        // disposable cartridges and cloth are eligible; unknown filters stay safe.
        internal static bool ProtectedFilter(GameItem item) => item != null && item.IsTag("WATER_FILTER") &&
            item.identifier != "water_filter" && item.identifier != "kotton_fabric" && item.identifier != "hydroreed_filter";

        internal static bool Accepts(GameItem item)
        {
            if (item == null || item.unitCount <= 0 || InventoryRules.Product(item)) return false;
            if (item.IsTag("WATER_FILTER"))
                return !ProtectedFilter(item) && item.IsTag(DurabilityHelper.DURABILITY_TAG) &&
                    DurabilityHelper.GetMaxDurability(item) > 0 && DurabilityHelper.GetRealDurability(item) == 0;
            return InventoryRules.IntakeLiquid(item) && !item.IsTag("FILTER_CONTAINER_TAG") &&
                WaterHelper.IsEmpty(item);
        }

        internal static bool CanCollect(GameItem item, BatchInputReservation reserved, RunningBatch batch)
        {
            if (!Accepts(item)) return false;
            if (batch == null || !reserved.Contains(item)) return true;
            // A later action can still reference an exhausted filter or an empty
            // container. Keep its identity until its last action has finished.
            if (!item.IsTag(BatchInputReservation.SourceTag) || batch.plan?.actions == null) return false;
            int source = item.GetTagReadonly(BatchInputReservation.SourceTag).GetInt();
            for (int i = batch.step; i < batch.plan.actions.Length; i++)
            {
                var action = batch.plan.actions[i];
                if (action.operation == Operation.Filter && action.FilterSources.Contains(source)) return false;
                if (action.source == source && (action.operation == Operation.Pour || action.operation == Operation.Filter || action.operation == Operation.ReturnFeed))
                    return false;
            }
            return true;
        }
    }
}
