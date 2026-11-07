namespace DreamDustAutomation.UI
{
    internal static class MachineUiNames
    {
        // Vanilla StorageExpansionHelper treats the role "storage" as an
        // expandable storage bay and overwrites every tab caption on save load.
        // Roles are presentation metadata, not saved inventory indices.
        internal const string StorageRole = "dream_dust_automation.storage";

        internal static string Product(string id)
        {
            string key = "item_" + id + "_name";
            string fallback = ModEntry.Text("product_" + id);
            // ChemFinisherRecipe.displayName is an initialization-time snapshot:
            // it can retain missing translations or the previous locale. Resolve
            // the current table when presenting the UI, not every tick.
            string current = LocHelper.GetLocalizedOrDefault("Item", key, fallback);
            return string.IsNullOrWhiteSpace(current) || current == key ? fallback : current;
        }
    }
}
