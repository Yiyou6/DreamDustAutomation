using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class DirectFormInputs
    {
        // Protect feedstock for every medicine, not just the selected product.
        // Use native active IDs plus the precursors supported by our normal planner.
        private static readonly string[] protectedParts = ChemicalRecipe.Ids
            .Select(ChemicalRecipe.Get)
            .SelectMany(recipe => new[] { recipe.Active, recipe.Mush })
            .Concat(new[] { "methylol" }).Where(id => id != null).Distinct().ToArray();
        private static readonly string[] reagents = { "alcohol", "acid", "base", "reducing_agent", "stabilizing_agent", "lipobond_agent" };

        internal static bool HasMedicinalParts(GameItem item) => item != null &&
            protectedParts.Any(id => ChemistryOperations.Amount(item, id) > 0);

        internal static bool HasLipobond(GameItem item) => item != null && ChemistryOperations.Amount(item, "lipobond_agent") > 0;
        internal static string ProtectionReason(GameItem item) => HasLipobond(item) ? "direct_lipobond_protected" : "direct_protected_input";

        internal static bool CanUse(GameItem item) => InventoryRules.Solid(item) && !HasMedicinalParts(item) && !HasLipobond(item);

        internal static bool HasReagents(GameItem item) => item != null &&
            reagents.Any(id => ChemistryOperations.Amount(item, id) > 0);

        // Draw contents, never dissolve the vessel. Evaporated non-medicinal
        // residue and reagent mixtures can both be feedstock. Native reactions
        // and evaporation still decide whether any formable material remains.
        internal static bool CanUseLiquid(GameItem item) => InventoryRules.IntakeLiquid(item) &&
            !HasMedicinalParts(item) && !HasLipobond(item) && ChemistryOperations.Amount(item, "alcohol") == 0 &&
            (HasReagents(item) || ChemistryOperations.Total(item) > ChemistryOperations.VolatileVolume(item));

        internal static bool CanUseLiquid(GameItem item, bool useSolvents) =>
            CanUseLiquid(item) && (useSolvents || !HasReagents(item));

        // Describe actual selected withdrawals, not every reagent in the bin.
        // Replaying just the transfers also respects native integer rounding and
        // repeated draws from the same bottle, without doing a chemistry search.
        internal static string[] ReagentsInPlan(BatchInputs input, BatchPlan plan)
        {
            if (input == null || plan == null || !plan.directForm) return new string[0];
            if (plan.separateDirect)
            {
                // Each selected source is processed in isolation. Track sources
                // rather than pretending every transfer shares one giant tank.
                var selected = new System.Collections.Generic.List<GameItem>();
                bool swapped = false, initialTanks = true;
                foreach (var step in plan.actions)
                {
                    if (step.operation == Operation.SwapTanks) swapped = !swapped;
                    if (step.operation == Operation.Pour || step.operation == Operation.Blend || step.operation == Operation.Dissolve) initialTanks = false;
                    if (initialTanks && step.operation == Operation.Form)
                    {
                        var vessel = swapped ? input.mashBuffer : input.chamber;
                        if (vessel != null) selected.Add(vessel);
                    }
                    if (step.operation == Operation.Pour && step.source >= 0 && step.source < input.supplies.Length)
                    {
                        var drawn = new SimulationItem();
                        drawn.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
                        ChemistryOperations.Pour(new SimulationItem(input.supplies[step.source]), drawn, step.amount);
                        selected.Add(drawn);
                    }
                    else if ((step.operation == Operation.Blend || step.operation == Operation.Dissolve) && step.source >= 0 && step.source < input.raw.Length)
                        selected.Add(input.raw[step.source]);
                }
                return reagents.Where(id => selected.Any(item => ChemistryOperations.Amount(item, id) > 0)).ToArray();
            }
            var used = new SimulationItem(input.chamber);
            used.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
            var supplies = input.supplies.Select(item => new SimulationItem(item)).ToArray();
            var buffer = input.mashBuffer == null ? null : new SimulationItem(input.mashBuffer);
            foreach (var action in plan.actions)
            {
                if (action.operation == Operation.Pour && action.source >= 0 && action.source < supplies.Length)
                    ChemistryOperations.Pour(supplies[action.source], used, action.amount);
                else if (action.operation == Operation.FeedMash && buffer != null)
                    ChemistryOperations.Pour(buffer, used, action.amount);
                else if ((action.operation == Operation.Blend || action.operation == Operation.Dissolve) &&
                    action.source >= 0 && action.source < input.raw.Length)
                {
                    var raw = new SimulationItem(input.raw[action.source]);
                    if (action.operation == Operation.Blend) ChemistryOperations.Blend(raw, used, input.blenderLoss);
                    else ChemistryOperations.Dissolve(raw, used);
                }
            }
            return reagents.Where(id => ChemistryOperations.Amount(used, id) > 0).ToArray();
        }
    }
}
