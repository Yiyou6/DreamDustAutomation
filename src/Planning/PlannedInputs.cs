using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    internal static class PlannedInputs
    {
        // Only actions in the selected plan, never every reserved/eligible item.
        // A partial liquid draw or used filter still marks its original item.
        internal static HashSet<GameItem> Read(BatchInputs input, BatchPlan plan)
        {
            var selected = new HashSet<GameItem>();
            if (input == null || plan?.actions == null) return selected;
            foreach (var action in plan.actions)
            {
                if (action.operation == Operation.Filter && action.amount > 0 && input.supplies != null)
                    foreach (int index in action.FilterSources)
                        if (index >= 0 && index < input.supplies.Length && input.supplies[index] != null) selected.Add(input.supplies[index]);
                bool solid = action.operation == Operation.Blend || action.operation == Operation.Dissolve;
                bool supply = (action.operation == Operation.Pour || action.operation == Operation.Filter) && action.amount > 0;
                if (!solid && !supply) continue;
                var sources = solid ? input.raw : input.supplies;
                if (sources != null && action.source >= 0 && action.source < sources.Length && sources[action.source] != null)
                    selected.Add(sources[action.source]);
            }
            return selected;
        }
    }
}
