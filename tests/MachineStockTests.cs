using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation;

namespace UnityEngine
{
    public static class Random
    {
        public static int roll, calls;
        public static int Range(int minimum, int maximum) { calls++; return roll; }
    }
}

// Run the production mod event handler against the extracted native ownership
// walk. Inventory and sales surfaces are headless; no Unity UI is instantiated.
public class GraphNodeStorage { public List<GraphNodeStorage> children = new List<GraphNodeStorage>(); }
public class GameInventory : GraphNodeStorage { }
public class GameItem : GraphNodeStorage
{
    public string identifier;
    public bool owned;
    public bool IsTag(string tag) { return tag == "IS_OWNED_TAG" && owned; }
    public void Destroy() { }
}
public static class GeneralHelper
{
    // NATIVE_IS_OWNED
}
public static class GraphUtils
{
    // NATIVE_FIND_ALL
    public static int TryAcceptAll(GameInventory inventory, GameItem item) { inventory.children.Add(item); return 0; }
}
public class EmporiumEntry
{
    public static EmporiumEntry Instance;
    public GameInventory invElement = new GameInventory(), backInvinvElement = new GameInventory(),
        backInvinvElementCounter = new GameInventory(), frontInvinvElement = new GameInventory(),
        docInvElement = new GameInventory(), showcaseElement = new GameInventory(), trashInvElement = new GameInventory(),
        vendingFountainElement = new GameInventory(), vendingMachineElement = new GameInventory(),
        afterhourWindow = new GameInventory(), hiddenElement = new GameInventory();
    public GameInventory[] Roots { get { return new[] { invElement, backInvinvElement, backInvinvElementCounter,
        frontInvinvElement, docInvElement, showcaseElement, trashInvElement, vendingFountainElement,
        vendingMachineElement, afterhourWindow, hiddenElement }; } }
    // NATIVE_GET_OWNED
}
public class PlayerStore
{
    public static PlayerStore Instance;
    public int attempts;
    public bool full;
    public bool TryAddDirectSellingItemToTable(string id)
    {
        attempts++;
        if (full) return false;
        EmporiumEntry.Instance.frontInvinvElement.children.Add(new GameItem { identifier = id });
        return true;
    }
}
public static class NetworkUpgrade { public static bool unlocked; public static bool IsRetiredChemistUnlocked() { return unlocked; } }
public interface IMod { }
public class ModManifest { }
public class ModLog
{
    public int warnings;
    public ModLog(ModManifest manifest) { }
    public void Warning(string text) { warnings++; }
    public void Log(string text) { }
}
public class ModItemDirectory { public void Add(string id, Func<GameItem> factory) { } }
public static class ModHook
{
    public static event Action<ModItemDirectory> OnModItemDirectoryInit;
    public static event Action<List<GameItem>> OnPlaceInventorInventoryItemEarly;
    public static event Action OnGameLoadedLate;
    public static event Action<string> OnNewGameStarted;
    public static void Stock(List<GameItem> owned) { OnPlaceInventorInventoryItemEarly?.Invoke(owned); }
    public static void Load() { OnGameLoadedLate?.Invoke(); }
}
public static class ModHelper
{
    public const string DEBUG_CATEGORY_SPAWN = "spawn";
    public static void AddDebugFunction(string id, string category, string title, Action action) { }
    public static void RemoveDebugFunctions(string id) { }
    public static GameItem SpawnItem(string id) { return new GameItem { identifier = id }; }
    public static string GetLocalized(string id, string key, params object[] args) { return key; }
}
namespace DreamDustAutomation
{
    public static class MachineController { public static GameItem Create() { return new GameItem(); } public static void RestoreAll() { } }
    public static class ChemistStarter { public static void Give(string scenario) { } }
    public static class RetiredChemistGift { public static void Install() { } public static void Uninstall() { } }
}
public static class MachineStockTests
{
    private static int checks;
    private static readonly ModEntry Mod = new ModEntry();
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Reset()
    {
        EmporiumEntry.Instance = new EmporiumEntry();
        PlayerStore.Instance = new PlayerStore();
        NetworkUpgrade.unlocked = true;
        UnityEngine.Random.roll = UnityEngine.Random.calls = 0;
        Mod.Init(new ModManifest());
    }
    private static GameItem Machine(bool owned = true) { return new GameItem { identifier = ModEntry.ItemId, owned = owned }; }
    private static void Stock() { ModHook.Stock(EmporiumEntry.Instance.GetAllOwnedItems()); }
    private static int Offers { get { return GraphUtils.FindAllChildrenType<GameItem>(EmporiumEntry.Instance.frontInvinvElement).Count(i => i.identifier == ModEntry.ItemId); } }
    public static void Main()
    {
        Mod.OnEnable();
        Reset(); NetworkUpgrade.unlocked = false; Stock();
        Check(PlayerStore.Instance.attempts == 0, "Locked shop offered a machine");
        for (int roll = 0; roll < 100; roll++)
        {
            Reset(); UnityEngine.Random.roll = roll; Stock();
            Check(Offers == (roll < 50 ? 1 : 0), "50% stock boundary: " + roll);
            Check(UnityEngine.Random.calls == 1, "One roll per eligible stock refresh");
        }
        Reset(); UnityEngine.Random.roll = 99; Stock();
        UnityEngine.Random.roll = 0; Stock();
        Check(Offers == 1, "Missed roll must allow next refresh on an existing save");
        Reset(); Stock(); Check(Offers == 1 && PlayerStore.Instance.attempts == 1, "Unlocked/no machine must offer one");
        for (int i = 0; i < 20; i++) Stock();
        Check(Offers == 1 && PlayerStore.Instance.attempts == 1, "Repeated regular/special refresh created duplicates");
        var offer = EmporiumEntry.Instance.frontInvinvElement.children.Single();
        EmporiumEntry.Instance.frontInvinvElement.children.Clear();
        EmporiumEntry.Instance.backInvinvElement.children.Add(offer); Stock();
        Check(Offers == 0 && PlayerStore.Instance.attempts == 1, "Offer in negotiation area allowed a duplicate");
        ((GameItem)offer).owned = true;
        EmporiumEntry.Instance.backInvinvElement.children.Clear();
        EmporiumEntry.Instance.invElement.children.Add(offer); Stock();
        Check(Offers == 0 && PlayerStore.Instance.attempts == 1, "Buying the offer did not close future stock");

        for (int root = 0; root < 11; root++)
        {
            Reset(); var machine = Machine(); var inventory = EmporiumEntry.Instance.Roots[root];
            inventory.children.Add(machine); Stock();
            Check(PlayerStore.Instance.attempts == 0, "Owned machine missed in root " + root);
            Check(inventory.children.Contains(machine) && machine.owned, "Stock altered owned machine " + root);
            Reset(); inventory = EmporiumEntry.Instance.Roots[root];
            var outer = new GameItem { identifier = "backpack", owned = true };
            var inner = new GameItem { identifier = "storage_box", owned = true };
            outer.children.Add(new GameInventory()); outer.children[0].children.Add(inner);
            inner.children.Add(new GameInventory()); inner.children[0].children.Add(machine);
            inventory.children.Add(outer); Stock();
            Check(PlayerStore.Instance.attempts == 0, "Nested machine missed in root " + root);
            inner.children[0].children.Clear(); Stock();
            Check(Offers == 1, "Lost nested machine did not restore eligibility " + root);
        }
        Reset(); var existing = Machine(); EmporiumEntry.Instance.invElement.children.Add(existing);
        ModHook.Stock(null); Check(PlayerStore.Instance.attempts == 0, "Null snapshot did not use live ownership");
        EmporiumEntry.Instance.invElement.children.Clear(); existing.owned = false;
        Stock(); Check(Offers == 1, "Sold/lost machine permanently blocked replacements");

        Reset(); EmporiumEntry.Instance.invElement.children.Add(new GameItem { identifier = "evaporator", owned = true });
        Stock(); Check(Offers == 1, "Vanilla equipment blocked machine stock");
        Reset(); var a = Machine(); var b = Machine();
        EmporiumEntry.Instance.invElement.children.Add(a); EmporiumEntry.Instance.showcaseElement.children.Add(b);
        Stock(); ModHook.Load(); Stock();
        Check(PlayerStore.Instance.attempts == 0 && EmporiumEntry.Instance.GetAllOwnedItems().Count == 2,
            "Old saves with multiple machines must retain both and block further stock");
        Mod.OnDisable(); Mod.OnEnable(); Stock();
        Check(PlayerStore.Instance.attempts == 0, "Reload lost possession cap");
        Reset(); Stock(); Check(Offers == 1, "Different save inherited a permanent purchase flag");
        Reset(); Mod.OnDisable(); Stock();
        Check(PlayerStore.Instance.attempts == 0, "Disabled mod still adds stock");
        Mod.OnEnable(); Stock(); Check(Offers == 1, "Reenabled mod did not restore stock hook");
        Reset(); PlayerStore.Instance.full = true; Stock();
        Check(Offers == 0 && ModEntry.Log.warnings == 1, "Full counter must retain existing warning and no offer");
        PlayerStore.Instance.full = false; Stock(); Check(Offers == 1, "Failed placement consumed eligibility");
        Reset(); EmporiumEntry.Instance = null; ModHook.Stock(null);
        Check(PlayerStore.Instance.attempts == 0, "Absent inventory attempted stock");
        Mod.OnDisable();
        Console.WriteLine("Machine stock: " + checks + " checks passed; production event handler with native recursive ownership traversal (headless).");
    }
}
