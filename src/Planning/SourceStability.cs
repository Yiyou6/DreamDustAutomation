using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class SourceStability
    {
        // A dose taken later must retain its composition while it waits in the
        // original bottle. In particular, native acid/base bottles can consume
        // their own microbes, and warm lipobond can evaporate before it is used.
        // Inspect clones only: never freeze or fast-forward the player's stock.
        internal static bool Check(BatchInputs input, BatchPlan plan, PlanningDiagnostics diagnostics)
        {
            if (plan.separateDirect) return true; // These lots already have isolated reservations.
            bool delayed = false;
            var checkedSources = new System.Collections.Generic.HashSet<int>();
            foreach (var action in plan.actions)
            {
                if (ReactionProgress.IsTimed(action.operation) || action.operation == Operation.Dry) delayed = true;
                if (!delayed || action.operation != Operation.Pour || action.source < 0 || action.source >= input.supplies.Length ||
                    !checkedSources.Add(action.source)) continue;
                var source = input.supplies[action.source];
                string changed = ChangingPart(source);
                if (changed == null) continue;
                var part = Liquid.Liquids.First(l => l.identifier == changed).displayName;
                string name = string.IsNullOrWhiteSpace(source.name) ? part : source.name;
                diagnostics?.Add("diag_stock_reacting", 100, "diag_stock_reacting_detail", name, part);
                return false;
            }
            return true;
        }

        internal static string ChangingPart(GameItem item)
        {
            var trial = new SimulationItem(item);
            var before = ChemistryOperations.Parts(trial);
            // Native reactions have their lowest heat threshold at 40 C. Below
            // that, two unchanged ticks cover room-temperature reaction chains.
            int unchanged = 0;
            for (int second = 0; second < 1800; second++)
            {
                ChemistryOperations.ReactionTick(trial);
                var after = ChemistryOperations.Parts(trial);
                for (int i = 0; i < before.Length; i++)
                    if (before[i] != after[i]) return Liquid.Liquids[i].identifier;
                if (++unchanged >= 2 && ChemistryOperations.Temperature(trial) < 40) return null;
            }
            return null;
        }
    }
}
