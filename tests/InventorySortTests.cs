using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DreamDustAutomation.Planning;

internal static class InventorySortTests
{
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    private static SimulationItem Item(string kind, int w, int h, int x, int y)
    {
        var item = new SimulationItem { identifier = kind };
        item.shape = new GridShapeBuilder(w, h).SetDataFill(1);
        item.modifiedShape = new GridShapeBuilder(item.shape).SetPosition(x, y);
        return item;
    }
    private static GameGridScrollableInventory Bin(int w, int h, params GameItem[] items)
    {
        var bin = (GameGridScrollableInventory)FormatterServices.GetUninitializedObject(typeof(GameGridScrollableInventory));
        typeof(TagSystem).GetProperty("dict").SetValue(bin, new Dictionary<string, TagState>());
        typeof(GameGridScrollableInventory).GetProperty("width").SetValue(bin, w);
        typeof(GameGridScrollableInventory).GetProperty("height").SetValue(bin, h);
        typeof(GameGridScrollableInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bin, new List<GameItem>(items));
        foreach (var item in items) item.parentInventory = bin;
        // Sorting is a relocation within the bin, including legacy items which
        // today's intake rules reject. It must not invoke transfer callbacks.
        bin.mayInventoryAddItemFunc = (item, inv) => { throw new Exception("Unexpected add"); };
        bin.mayInventoryRemoveItemFunc = (item, inv) => { throw new Exception("Unexpected remove"); };
        bin.onSlotAddItemEndFunc += (item, inv, slot) => { throw new Exception("Unexpected add notification"); };
        bin.onSlotRemoveItemEndFunc += (item, inv, slot) => { throw new Exception("Unexpected remove notification"); };
        return bin;
    }
    private static string Position(GameItem item) => item.modifiedShape.minX + "," + item.modifiedShape.minY + "," + item.modifiedShape.orientation;
    private static void Valid(GameGridScrollableInventory bin)
    {
        var items = bin.childItems;
        for (int i = 0; i < items.Count; i++)
        {
            Check(items[i].parentInventory == bin, "Changed inventory identity");
            Check(items[i].modifiedShape.FullyIntersectsRect(bin.width, bin.height, (a, b) => a != 1 || b == 0), "Item escaped bin");
            for (int j = 0; j < i; j++) Check(!items[i].modifiedShape.Intersects(items[j].modifiedShape), "Native collision after sorting");
        }
    }
    private static bool Apply(params InventorySort[] plans) => InventorySort.TryApply(plans, item => { }, bin => { });

    internal static string Run()
    {
        checks = 0;
        var big = Item("bottle", 2, 2, 6, 4);
        var small2 = Item("bottle", 1, 1, 5, 3);
        var small1 = Item("bottle", 1, 1, 2, 3);
        var other = Item("filter", 1, 1, 0, 0);
        small1.unitCount = 17;
        small1.state.GetTag("test_label").Enable().SetInt(6);
        small1.state.GetTag(BatchInputReservation.SourceTag).Enable().SetInt(4);
        small1.state.GetTag(Liquid.GetCurrentPartFromId("alcohol")).Enable().SetInt(123456);
        var oldState = small1.state;
        var oldBase = small1.shape;
        var reserved = new BatchInputReservation();
        reserved.Capture(new[] { small1 });
        // Use original item references as running batches do.
        var bin = Bin(12, 24, big, small2, other, small1);
        var enumeration = bin.childItems.ToArray();
        Check(InventorySort.TryPlan(bin, out var plan), "Mixed plan");
        Check(big.modifiedShape.minX == 6, "Planning modified live item");
        Check(Apply(plan), "Apply mixed plan");
        Check(bin.childItems.SequenceEqual(enumeration), "Supply indices/order changed");
        var ordered = bin.childItems.OrderBy(i => i.modifiedShape.minY).ThenBy(i => i.modifiedShape.minX).ToArray();
        Check(ordered.SequenceEqual(new GameItem[] { small1, small2, big, other }), "Type grouping, occupied size, stable ties");
        Check(small1.unitCount == 17 && ReferenceEquals(oldState, small1.state) && ReferenceEquals(oldBase, small1.shape), "Item state or base shape changed");
        Check(small1.state.GetTag("test_label").GetInt() == 6 && small1.state.GetTag(BatchInputReservation.SourceTag).GetInt() == 4 &&
            small1.state.GetTag(Liquid.GetCurrentPartFromId("alcohol")).GetInt() == 123456, "Labels, batch identity or liquid changed");
        Check(reserved.Contains(small1) && !reserved.CanRemove(small1, false, true), "Batch input reservation was lost");
        Valid(bin);
        string before = string.Join(";", bin.childItems.Select(Position));
        Check(InventorySort.TryPlan(bin, out plan) && Apply(plan), "Repeat sorting");
        Check(before == string.Join(";", bin.childItems.Select(Position)), "Repeated sort is unstable");

        var rotor = Bin(4, 3, Item("small", 2, 1, 0, 2), Item("large", 3, 2, 0, 0));
        Check(InventorySort.TryPlan(rotor, out plan) && Apply(plan), "Rotation fit");
        Check(rotor.childItems[1].modifiedShape.orientation != 0, "Rotation was not used"); Valid(rotor);
        var hole = Item("hole", 2, 2, 2, 2);
        hole.modifiedShape = new GridShapeBuilder(2, 2).SetData(new byte[] { 1, 0, 1, 1 }, 2).SetPosition(2, 2);
        var irregular = Bin(5, 5, hole, Item("small", 1, 1, 0, 0));
        Check(InventorySort.TryPlan(irregular, out plan) && Apply(plan), "Irregular footprint"); Valid(irregular);

        // A valid packed bin may not admit this sorted reading order. Never
        // spill remaining items or partially sort the other two inventories.
        var tight = Bin(4, 4, Item("small", 1, 1, 3, 3), Item("large", 3, 4, 0, 0), Item("middle", 1, 3, 3, 0));
        var tightShapes = tight.childItems.Select(i => i.modifiedShape).ToArray();
        Check(!InventorySort.TryPlan(tight, out plan), "Tight mixed packing should refuse safely");
        Check(tight.childItems.Select(i => i.modifiedShape).SequenceEqual(tightShapes), "Failed planning changed items"); Valid(tight);

        var a = Bin(8, 8, Item("a", 1, 1, 7, 7));
        var b = Bin(8, 8, Item("b", 1, 2, 6, 6));
        var c = Bin(8, 8, Item("c", 2, 2, 5, 5));
        InventorySort.TryPlan(a, out var pa); InventorySort.TryPlan(b, out var pb); InventorySort.TryPlan(c, out var pc);
        b.childItems[0].unitCount++;
        Check(!Apply(pa, pb, pc) && a.childItems[0].modifiedShape.minX == 7, "Changed snapshot must abort all bins");
        InventorySort.TryPlan(b, out pb);
        var originals = new[] { a, b, c }.SelectMany(i => i.childItems).Select(i => i.modifiedShape).ToArray();
        bool threw = false;
        try { InventorySort.TryApply(new[] { pa, pb, pc }, i => { if (i.identifier == "b") throw new Exception("render failure"); }, i => { }); }
        catch { threw = true; }
        Check(threw && new[] { a, b, c }.SelectMany(i => i.childItems).Select(i => i.modifiedShape).SequenceEqual(originals), "Rollback must restore every exact shape");
        Check(Apply(pa, pb, pc), "All three bins commit");
        foreach (var current in new[] { a, b, c }) Valid(current);
        Check(InventorySort.TryPlan(Bin(16, 16), out plan) && Apply(plan), "Empty bin");

        // Three full bins, 800 objects. Count refreshes and measure bounded work;
        // no native whole-inventory validation is called per item.
        var full = new[] { Bin(12, 24), Bin(16, 16), Bin(16, 16) };
        for (int k = 0; k < full.Length; k++)
            full[k] = Bin(full[k].width, full[k].height, Enumerable.Range(0, full[k].width * full[k].height)
                .Select(n => (GameItem)Item("kind" + (n % 7), 1, 1, n % full[k].width, n / full[k].width)).ToArray());
        var fullPlans = new InventorySort[3];
        int refreshes = 0, binRefreshes = 0;
        var watch = Stopwatch.StartNew();
        for (int k = 0; k < 3; k++) Check(InventorySort.TryPlan(full[k], out fullPlans[k]), "Full bin plan");
        Check(InventorySort.TryApply(fullPlans, i => refreshes++, i => binRefreshes++), "Full bins commit");
        double elapsed = watch.Elapsed.TotalMilliseconds;
        Check(refreshes <= 800 && binRefreshes == 3, "Repeated inventory rebuilds during sort");
        foreach (var current in full) Valid(current);
        Check(elapsed < 250, "800-item geometry sort took over 250 ms");
        var enlarged = Bin(16, 160, Enumerable.Range(0, 2560).Select(n => (GameItem)Item("kind" + n % 7, 1, 1, n % 16, n / 16)).ToArray());
        Check(InventorySort.TryPlan(enlarged, out var enlargedPlan) && Apply(enlargedPlan), "2560-cell output bin must support sorting");
        Valid(enlarged);
        return "Inventory sorting: " + checks + " checks passed; 800 objects planned/applied in " + elapsed.ToString("0.0") + " ms (rendering omitted).";
    }
}
