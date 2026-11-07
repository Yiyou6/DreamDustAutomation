using System.Linq;

namespace DreamDustAutomation
{
    internal static class ChemistStarter
    {
        internal static readonly string[] Equipment = { "blender", "heating_plate", "evaporator", "chem_finisher" };

        // Called only by the native new-game event after the vanilla kit exists.
        // Never run from save-load: established inventories must not be replaced.
        internal static void Give(string scenario)
        {
            if (SaveManager.IsDecoding || PlayerStore.Instance == null || !PlayerStore.Instance.IsChemistStart()) return;
            var entry = EmporiumEntry.Instance;
            var inventories = new GameInventory[] { entry.invElement, entry.backInvinvElement, entry.showcaseElement, entry.frontInvinvElement };
            var items = inventories.Where(inv => inv != null).SelectMany(inv => inv.childItems).ToArray();
            if (items.Any(item => item.identifier == ModEntry.ItemId)) return;
            var originals = Equipment.Select(id => items.FirstOrDefault(item => item.identifier == id)).ToArray();
            if (originals.Any(item => item == null))
            { ModEntry.Log.Warning("Chemist starter kit was changed by another scenario; original equipment kept."); return; }
            var machine = ModHelper.SetItemOwnerShip(DirectoryMaster.Item(ModEntry.ItemId), true);
            foreach (var inventory in inventories.Where(inv => inv != null))
            {
                if (GraphUtils.TryAcceptAll(inventory, machine) != 0) continue;
                if (!machine.IsTag("DDA_ASSEMBLED_V1"))
                { machine.Destroy(); ModEntry.Log.Warning("Starter machine assembly failed; original kit kept."); return; }
                // Placement and assembly succeeded before removing the four
                // explicitly replaced starter devices. All other supplies stay.
                foreach (var item in originals) item.Destroy();
                // Prefer the player's main bag now that the kit freed its slots.
                if (machine.parentInventory != entry.invElement) GraphUtils.TryAcceptAll(entry.invElement, machine);
                return;
            }
            machine.Destroy();
            ModEntry.Log.Warning("No space for the starter machine; original chemistry kit kept.");
        }
    }
}
