namespace DreamDustAutomation.Planning
{
    internal static class HeatedDrying
    {
        // Match the native upgraded hotplate's high setting. The base plate
        // stops at 99 C and cannot boil water (native boiling requires >100 C).
        internal const int TargetTemperature = 160;
        internal const int TickLimit = 900;

        // Direct-form water feed can also contain leftover reagents. They do
        // not prevent native boiling; native reactions decide the dry yield.
        // Keep medicinal residue protected from destructive high heat.
        internal static bool IsWaterFeed(GameItem batch) =>
            ChemistryOperations.Amount(batch, "water") > 0 &&
            !DirectFormInputs.HasMedicinalParts(batch);

        internal static bool CanUse(GameItem batch, int upgrades) => upgrades >= 1 && IsWaterFeed(batch);

        // Revalidate a saved/planned heating endpoint without changing live stock.
        // Planning uses the same native one-second simulation, yielding in slices.
        internal static int HeatUntilDry(GameItem batch, int upgrades, GameItem buffer = null)
        {
            if (ChemistryOperations.VolatileVolume(batch) == 0) return 0;
            // CanUse is the legacy automatic route's water-feed preference.
            // An explicitly selected heat step can also boil other native
            // volatile components; validate the same conditions as planning.
            if (DirectDryingPolicy.HeatProblem(batch, upgrades) != null) return -1;
            int seconds = 0;
            while (ChemistryOperations.VolatileVolume(batch) > 0 && seconds < TickLimit)
            {
                if (buffer != null && ChemistryOperations.Total(buffer) > 0) ChemistryOperations.ReactionTick(buffer);
                ChemistryOperations.SimulateHeatedSecond(batch, upgrades, TargetTemperature);
                seconds++;
            }
            return ChemistryOperations.VolatileVolume(batch) == 0 ? seconds : -1;
        }
    }
}
