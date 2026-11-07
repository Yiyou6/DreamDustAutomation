using System.Collections.Generic;

namespace DreamDustAutomation
{
    public sealed class ModEntry : IMod
    {
        public const string Id = "dream_dust_automation";
        public const string ItemId = "dream_dust_automation.synthesizer";
        internal const int StockChancePercent = 50;
        internal static ModLog Log;
        public void Init(ModManifest manifest) { Log = new ModLog(manifest); }
        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += Register;
            ModHook.OnPlaceInventorInventoryItemEarly += Stock;
            ModHook.OnGameLoadedLate += Restore;
            ModHook.OnNewGameStarted += ChemistStarter.Give;
            RetiredChemistGift.Install();
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_SPAWN, "Chemical Manufacturer", SpawnForTesting);
        }
        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= Register;
            ModHook.OnPlaceInventorInventoryItemEarly -= Stock;
            ModHook.OnGameLoadedLate -= Restore;
            ModHook.OnNewGameStarted -= ChemistStarter.Give;
            RetiredChemistGift.Uninstall();
            ModHelper.RemoveDebugFunctions(Id);
        }
        private static void Register(ModItemDirectory directory) { directory.Add(ItemId, MachineController.Create); }
        private static void Stock(List<GameItem> owned)
        {
            if (!NetworkUpgrade.IsRetiredChemistUnlocked()) return;
            var entry = EmporiumEntry.Instance;
            if (entry == null || PlayerStore.Instance == null) return;
            // Both Jackson's visits and his shop supply the native recursive
            // ownership snapshot (including storage, bags and after-hours items).
            // Do not persist a purchase flag: losing/selling the machine must
            // allow a replacement on the next stock refresh.
            if (ContainsMachine(owned ?? entry.GetAllOwnedItems())) return;
            // A repeated stock callback must not offer a second copy, including
            // when the first offer has already been moved to the buying area.
            if (ContainsMachine(GraphUtils.FindAllChildrenType<GameItem>(entry.frontInvinvElement)) ||
                ContainsMachine(GraphUtils.FindAllChildrenType<GameItem>(entry.backInvinvElement))) return;
            if (UnityEngine.Random.Range(0, 100) >= StockChancePercent) return;
            // Reserve the machine's 4x4 footprint before vanilla stock fragments the counter.
            if (!PlayerStore.Instance.TryAddDirectSellingItemToTable(ItemId))
                Log.Warning("制造机未能上架：请检查售货台空间及原版出售限制。 / Manufacturer offer could not be placed; check counter space and vanilla selling restrictions.");
        }
        private static bool ContainsMachine(IEnumerable<GameItem> items)
        {
            if (items == null) return false;
            foreach (var item in items)
                if (item != null && item.identifier == ItemId) return true;
            return false;
        }
        private static void Restore() { MachineController.RestoreAll(); }
        private static void SpawnForTesting()
        {
            var item = ModHelper.SpawnItem(ItemId);
            if (GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, item) == 0)
            {
                Log.Log("Synthesizer placed in player inventory.");
                return;
            }
            // Only discard the freshly spawned, unplaced shell. Existing inventory
            // items are never moved or removed to make space for a debug spawn.
            item.Destroy();
            Log.Warning("机器未生成：请在库存腾出连续 4×4 空间后重试。 / Clear a 4x4 inventory space and retry.");
        }
        internal static string Text(string key, params object[] args) => ModHelper.GetLocalized(Id, key, args);
    }
}
