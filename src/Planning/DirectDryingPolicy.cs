namespace DreamDustAutomation.Planning
{
    // Zero exists only for pre-option running plans and their original policy.
    internal enum DirectDryingMethod { LegacyAutomatic = 0, Evaporate = 1, Heat = 2 }

    internal static class DirectDryingPolicy
    {
        internal const string PreferenceTag = "DDA_DIRECT_DRYING";
        internal static bool Valid(DirectDryingMethod method) => method >= DirectDryingMethod.LegacyAutomatic && method <= DirectDryingMethod.Heat;
        internal static DirectDryingMethod Preference(int saved) => saved == (int)DirectDryingMethod.Heat ? DirectDryingMethod.Heat : DirectDryingMethod.Evaporate;
        internal static bool CanSelectHeat(int modules) => modules >= 1;
        internal static string HeatProblem(GameItem batch, int modules)
        {
            if (ChemistryOperations.VolatileVolume(batch) == 0) return null;
            if (!CanSelectHeat(modules)) return "direct_heat_modules";
            if (DirectFormInputs.HasMedicinalParts(batch)) return "direct_heat_medicine";
            // Native boiling acts on each volatile component, including
            // water-free lipobond. Dry acids/bases never enter this stage.
            return null;
        }
        internal static bool UseHeat(GameItem batch, int modules, DirectDryingMethod method) =>
            method == DirectDryingMethod.Heat || (method == DirectDryingMethod.LegacyAutomatic && HeatedDrying.CanUse(batch, modules));
        internal static bool Allows(DirectDryingMethod method, Operation operation) =>
            !(method == DirectDryingMethod.Heat && operation == Operation.Dry) &&
            !(method == DirectDryingMethod.Evaporate && operation == Operation.BoilDry);
    }
}
