using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation
{
    // Wrap only the one-time network reward. The retired chemist's recurring
    // shop dialogue and Jackson's stock generator are separate native paths.
    internal static class RetiredChemistGift
    {
        private const string UpgradeId = "RETIRED_CHEMIST";
        private static Action original, wrapper;
        private static bool enabled, handling;

        internal static void Install()
        {
            if (enabled) return;
            if (!NetworkUpgradeList.UnlockActions.TryGetValue(UpgradeId, out var native) || native == null)
            { ModEntry.Log.Warning("Retired chemist reward unavailable; native rewards left unchanged."); return; }
            enabled = true;
            original = native;
            wrapper = () => Give(native);
            NetworkUpgradeList.UnlockActions[UpgradeId] = wrapper;
        }

        internal static void Uninstall()
        {
            enabled = false;
            if (wrapper != null && NetworkUpgradeList.UnlockActions.TryGetValue(UpgradeId, out var current) && current == wrapper)
                NetworkUpgradeList.UnlockActions[UpgradeId] = original;
            original = wrapper = null;
        }

        private static GameItem[] Items(GameInventory[] inventories) => inventories
            .SelectMany(inv => GraphUtils.FindChildrenType<GameItem>(inv, passthrough: item => true)).Distinct().ToArray();

        private static void Give(Action native)
        {
            if (!enabled || handling || SaveManager.IsDecoding || EmporiumEntry.Instance == null)
            { native(); return; }
            handling = true;
            try
            {
                var entry = EmporiumEntry.Instance;
                var inventories = new GameInventory[] { entry.backInvinvElement, entry.invElement,
                    entry.showcaseElement, entry.frontInvinvElement, entry.afterhourInventory }.Where(inv => inv != null).Distinct().ToArray();
                var before = new HashSet<GameItem>(Items(inventories));
                // Keep vanilla's appointment and every other gift, including
                // scanner condition, supplies and changes made by other mods.
                native();
                var added = Items(inventories).Where(item => !before.Contains(item)).ToArray();
                if (added.Any(item => item.identifier == ModEntry.ItemId)) return;
                var equipment = ChemistStarter.Equipment.Select(id => added.FirstOrDefault(item => item.identifier == id)).ToArray();
                if (equipment.Any(item => item == null))
                { ModEntry.Log.Warning("Retired chemist reward kit incomplete; delivered items kept."); return; }
                try { Replace(equipment, inventories); }
                catch (Exception error) { ModEntry.Log.Warning("Unable to create chemist reward machine; original gift kit kept. " + error); }
            }
            finally { handling = false; }
        }

        private static void Replace(GameItem[] equipment, GameInventory[] inventories)
        {
            var parents = equipment.Select(item => item.parentInventory).ToArray();
            var machine = ModHelper.SetItemOwnerShip(DirectoryMaster.Item(ModEntry.ItemId), true);
            bool replaced = false;
            try
            {
                // These are ONLY the four freshly awarded objects. Temporarily
                // release their cells so a full reward shelf can fit the new kit.
                // Expel preserves the item's exact grid shape for rollback.
                for (int i = 0; i < equipment.Length; i++)
                    if (parents[i] == null || !parents[i].Expel(equipment[i]) || equipment[i].parentInventory != null)
                        throw new InvalidOperationException("Unable to release a reward device.");
                foreach (var inventory in inventories)
                {
                    if (GraphUtils.TryAcceptAll(inventory, machine) != 0) continue;
                    if (!machine.IsTag("DDA_ASSEMBLED_V1"))
                        throw new InvalidOperationException("Reward machine did not assemble.");
                    replaced = true;
                    foreach (var item in equipment) item.Destroy();
                    return;
                }
                ModEntry.Log.Warning("No 4x4 space for the chemist reward machine; original gift kit restored.");
            }
            catch (Exception error) { ModEntry.Log.Warning("Chemist reward replacement failed; original gift kit kept. " + error); }
            finally
            {
                if (!replaced)
                {
                    machine.Destroy();
                    for (int i = 0; i < equipment.Length; i++)
                        if (equipment[i].parentInventory == null && parents[i] != null && !parents[i].UncheckedAccept(equipment[i]))
                            GraphUtils.EmporiumTryAdd(parents[i], equipment[i]);
                }
            }
        }
    }
}
