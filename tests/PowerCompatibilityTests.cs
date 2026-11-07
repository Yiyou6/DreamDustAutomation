using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DreamDustAutomation;

public class TagState
{
    private int value;
    public int GetInt() { return value; }
    public TagState SetInt(int number) { value = number; return this; }
}
public class GameItem
{
    public string identifier;
    public object contentWindow = new object();
    public GameSlotInventory batterySlot;
    public bool disposable;
    private readonly Dictionary<string, TagState> tags = new Dictionary<string, TagState>();
    public TagState GetTagReadonly(string key) { if (!tags.ContainsKey(key)) tags[key] = new TagState(); return tags[key]; }
    public void ModifyTag(string key, Action<TagState> action) { action(GetTagReadonly(key)); }
}
public class GameSlotInventory { public GameItem childItem; }
public static class MachineHelper
{
    public static GameSlotInventory GetBatterySlot(GameItem machine) { return machine.batterySlot; }
    // NATIVE_RECHARGE
}
public static class PowerHelper
{
    public static bool IsDisposablePowerSource(GameItem item) { return item.disposable; }
    public static void UpdateEnergyCreditSprite(GameItem item) { }
    // NATIVE_CHARGE
}
public static class CurrentConverterHelper { public static bool IsCurrentConverter(GameItem item) { return item.identifier == "converter"; } }
public static class HypertechBatteryHelper { public static bool IsHypertechBattery(GameItem item) { return item.identifier == "hypertech"; } }
public class StoreStation
{
    public static StoreStation Instance = new StoreStation();
    public EventManager storeEventManager = new EventManager();
}
public class EventManager
{
    public bool blackout, affected;
    public bool IsBlackoutEventActive() { return blackout; }
    public bool IsPlayerAffectedByBlackout() { return affected; }
}
public static class PowerCompatibilityTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static GameItem Evaporator(int energy)
    {
        var battery = new GameItem { identifier = "battery" };
        battery.ModifyTag("power_source_item_energy", t => t.SetInt(energy));
        battery.ModifyTag("power_source_item_max_energy", t => t.SetInt(100));
        return new GameItem { identifier = "evaporator", batterySlot = new GameSlotInventory { childItem = battery } };
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddDiscarded(ControllerRegistry<object> registry)
    {
        var discarded = new object(); registry.Add(discarded); return new WeakReference(discarded);
    }
    public static void Main()
    {
        var array = new GameItem { identifier = "powerblock" };
        var other = new GameItem { identifier = "blender" };
        foreach (int start in new[] { 0, 98, 99, 100 })
        foreach (bool commercial in new[] { false, true })
        foreach (bool powerOn in new[] { false, true })
        foreach (bool blackout in new[] { false, true })
        {
            StoreStation.Instance.storeEventManager.blackout = blackout;
            StoreStation.Instance.storeEventManager.affected = blackout;
            var evaporator = Evaporator(start);
            AdjacentCharging.Apply(evaporator, new[] { array, other, array }, powerOn, commercial);
            int expected = powerOn && !blackout ? Math.Min(100, start + (commercial ? 4 : 2)) : start;
            Check(evaporator.batterySlot.childItem.GetTagReadonly("power_source_item_energy").GetInt() == expected, "Native charging parity");
        }
        StoreStation.Instance.storeEventManager.blackout = false;
        foreach (string id in new[] { "battery", "converter", "hypertech" })
        {
            var evaporator = Evaporator(20); evaporator.batterySlot.childItem.identifier = id;
            evaporator.batterySlot.childItem.disposable = id == "battery";
            AdjacentCharging.Apply(evaporator, new[] { array }, true, true);
            Check(evaporator.batterySlot.childItem.GetTagReadonly("power_source_item_energy").GetInt() == 20, "Native special/disposable battery rule");
        }
        var multiple = Evaporator(0);
        AdjacentCharging.Apply(multiple, new[] { array, new GameItem { identifier = "powerblock" } }, true, false);
        Check(multiple.batterySlot.childItem.GetTagReadonly("power_source_item_energy").GetInt() == 4, "Two distinct arrays");
        var noNeighbour = Evaporator(0);
        AdjacentCharging.Apply(noNeighbour, new[] { other }, true, true);
        Check(noNeighbour.batterySlot.childItem.GetTagReadonly("power_source_item_energy").GetInt() == 0, "Non-array neighbor charged battery");
        AdjacentCharging.Apply(null, new[] { array }, true, true);
        AdjacentCharging.Apply(new GameItem { identifier = "evaporator" }, new[] { array }, true, true);
        AdjacentCharging.Apply(new GameItem { identifier = "evaporator", batterySlot = new GameSlotInventory() }, new[] { array }, true, true);
        var registry = new ControllerRegistry<object>(); var live = new object(); registry.Add(live);
        var dead = AddDiscarded(registry);
        for (int i = 0; i < 2000; i++) AddDiscarded(registry);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!dead.IsAlive && registry.Live(_ => true).Count == 1, "Registry retained discarded controllers");
        Check(registry.Live(x => x != live).Count == 0, "Destroyed controller survived pruning");
        GC.KeepAlive(live);
        Console.WriteLine(checks + " power/lifetime checks passed: native charge rules and discarded-controller collection.");
    }
}
