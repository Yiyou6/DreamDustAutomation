using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Plan geometry without removing/reinserting items. Native bulk insertion
    // merges stacks and can discard overflow, so it is not a sorting operation.
    internal sealed class InventorySort
    {
        private sealed class Entry
        {
            internal GameItem Item;
            internal GridShape Original, Snapshot, Target;
            internal int Area, Units;
            internal string Kind;
        }
        private readonly GameGridScrollableInventory inventory;
        private readonly int width, height;
        private readonly Entry[] entries;

        private InventorySort(GameGridScrollableInventory inventory, Entry[] entries)
        {
            this.inventory = inventory; this.entries = entries;
            width = inventory.width; height = inventory.height;
        }

        internal static bool TryPlan(GameGridScrollableInventory inventory, out InventorySort plan)
        {
            plan = null;
            if (inventory == null || inventory.width <= 0 || inventory.height <= 0 ||
                inventory.width > 512 || inventory.height > 512) return false;
            var entries = new List<Entry>();
            foreach (var item in inventory.childItems)
            {
                var shape = item?.modifiedShape;
                if (shape == null || item.parentInventory != inventory || shape.GetOutsideBounds() != 0 ||
                    shape.width <= 0 || shape.height <= 0 || shape.width > 64 || shape.height > 64) return false;
                int area = 0;
                for (int y = 0; y < shape.height; y++) for (int x = 0; x < shape.width; x++)
                {
                    byte cell = shape.GetLocal(x, y);
                    if (cell > 1) return false; // Preserve unfamiliar modded placement constraints.
                    if (cell == 1) area++;
                }
                if (area == 0) return false;
                entries.Add(new Entry { Item = item, Original = shape, Snapshot = shape.Clone(),
                    Area = area, Units = item.unitCount, Kind = item.identifier ?? "" });
            }
            if (entries.Select(e => e.Item).Distinct().Count() != entries.Count ||
                entries.Sum(e => e.Area) > inventory.width * inventory.height) return false;

            // Keep each item type together. Small types first, then small items
            // within each type. Existing row order breaks ties deterministically.
            var ordered = entries.GroupBy(e => e.Kind, StringComparer.Ordinal)
                .OrderBy(g => g.Min(e => e.Area)).ThenBy(g => g.Key, StringComparer.Ordinal)
                .SelectMany(g => g.OrderBy(e => e.Area).ThenBy(e => e.Snapshot.minY).ThenBy(e => e.Snapshot.minX)).ToArray();
            var cells = new bool[inventory.width * inventory.height];
            int cursor = 0;
            foreach (var entry in ordered)
            {
                var shapes = Enumerable.Range(0, 4).Select(r => (GridShape)new GridShapeBuilder(entry.Snapshot)
                    .SetTransform(0, 0, entry.Snapshot.flipped, (entry.Snapshot.orientation + r) % 4)).ToArray();
                // Precompute occupied offsets, including holes and rotations.
                var offsets = shapes.Select(s => Enumerable.Range(0, s.globalWidth * s.globalHeight)
                    .Where(p => s.Get(p % s.globalWidth, p / s.globalWidth) == 1)
                    .Select(p => new[] { p % s.globalWidth, p / s.globalWidth }).ToArray()).ToArray();
                bool placed = false;
                for (int p = cursor; p < cells.Length && !placed; p++)
                {
                    int x = p % inventory.width, y = p / inventory.width;
                    for (int r = 0; r < shapes.Length; r++)
                    {
                        if (x + shapes[r].globalWidth > inventory.width || y + shapes[r].globalHeight > inventory.height) continue;
                        bool free = true;
                        foreach (var d in offsets[r])
                            if (cells[(y + d[1]) * inventory.width + x + d[0]]) { free = false; break; }
                        if (!free) continue;
                        entry.Target = new GridShapeBuilder(shapes[r]).SetPosition(x, y);
                        foreach (var d in offsets[r]) cells[(y + d[1]) * inventory.width + x + d[0]] = true;
                        // Never fill an earlier hole with a later group: reading
                        // left-to-right/top-to-bottom must retain the sort order.
                        cursor = p + 1; placed = true; break;
                    }
                }
                if (!placed) return false; // No mutation: a tight mixed bin stays intact.
            }
            plan = new InventorySort(inventory, entries.ToArray());
            return true;
        }

        private static bool SameShape(GridShape a, GridShape b)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height ||
                a.minX != b.minX || a.minY != b.minY || a.orientation != b.orientation || a.flipped != b.flipped ||
                a.GetOutsideBounds() != b.GetOutsideBounds()) return false;
            for (int y = 0; y < a.height; y++) for (int x = 0; x < a.width; x++)
                if (a.GetLocal(x, y) != b.GetLocal(x, y)) return false;
            return true;
        }

        private bool Current()
        {
            if (inventory.width != width || inventory.height != height) return false;
            var items = new HashSet<GameItem>(inventory.childItems);
            return items.Count == entries.Length && entries.All(e => items.Contains(e.Item) &&
                e.Item.parentInventory == inventory && e.Item.unitCount == e.Units &&
                (e.Item.identifier ?? "") == e.Kind && SameShape(e.Item.modifiedShape, e.Snapshot));
        }

        // Call on the game thread without yielding: all bins commit together.
        // Refresh only transforms, not full item features/chemistry or parent layouts.
        internal static bool TryApply(InventorySort[] plans, Action<GameItem> renderItem,
            Action<GameGridScrollableInventory> renderInventory)
        {
            if (plans.Any(p => p == null || !p.Current()) ||
                plans.Select(p => p.inventory).Distinct().Count() != plans.Length) return false;
            var changed = plans.SelectMany(p => p.entries).Where(e => !SameShape(e.Snapshot, e.Target)).ToArray();
            try
            {
                foreach (var entry in changed) entry.Item.modifiedShape = entry.Target;
                foreach (var entry in changed) renderItem(entry.Item);
                foreach (var plan in plans) renderInventory(plan.inventory);
                return true;
            }
            catch
            {
                foreach (var entry in changed) entry.Item.modifiedShape = entry.Original;
                foreach (var entry in changed) { try { renderItem(entry.Item); } catch { } }
                foreach (var plan in plans) { try { renderInventory(plan.inventory); } catch { } }
                throw;
            }
        }
    }
}
