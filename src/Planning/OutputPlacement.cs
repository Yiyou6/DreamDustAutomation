using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // One occupancy map per uninterrupted output slice. Native callbacks still
    // validate every proposed slot. Discard this map whenever the coroutine
    // yields, so player/hopper movement cannot leave a stale placement cache.
    internal sealed class OutputPlacement
    {
        private readonly int width, height;
        private readonly bool[] occupied;
        internal OutputPlacement(int width, int height, IEnumerable<GridShape> items)
        {
            this.width = width; this.height = height;
            occupied = new bool[width * height];
            foreach (var shape in items) Add(shape);
        }
        internal void Add(GridShape shape)
        {
            for (int y = Math.Max(0, shape.minY); y <= Math.Min(height - 1, shape.maxY); y++)
                for (int x = Math.Max(0, shape.minX); x <= Math.Min(width - 1, shape.maxX); x++)
                    if (shape.Get(x, y) == 1) occupied[y * width + x] = true;
        }
        private bool Free(int x, int y, int w, int h)
        {
            if (x + w > width || y + h > height) return false;
            for (int row = y; row < y + h; row++)
                for (int col = x; col < x + w; col++) if (occupied[row * width + col]) return false;
            return true;
        }
        internal SlotMarker Find(GridShape original, Func<GridShape, SlotMarker> validate)
        {
            var shape = new GridShapeBuilder(original);
            for (int flip = 0; flip < 2; flip++)
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    shape.SetTransform(0, 0, flip != 0, 4 - rotation);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            if (Free(x, y, shape.globalWidth, shape.globalHeight))
                            {
                                shape.SetTransform(x, y, flip != 0, 4 - rotation);
                                var slot = validate(shape);
                                if (slot != null && slot.numTransfer > 0) return slot;
                            }
                }
            return null;
        }
        internal static OutputPlacement Capture(GameGridScrollableInventory inventory)
        {
            if (!(inventory is IMachineOutputInventory) || inventory.slotSearchPassthroughForItemFunc != null ||
                inventory.width <= 0 || inventory.height <= 0 || inventory.width > 512 || inventory.height > 512 ||
                inventory.childItems.Any(i => i.modifiedShape == null || i.modifiedShape.GetOutsideBounds() != 0 ||
                    i.slotSearchPassthroughForItemFunc != null)) return null;
            return new OutputPlacement(inventory.width, inventory.height, inventory.childItems.Select(i => i.modifiedShape));
        }
        internal SlotMarker FindSlot(GameGridScrollableInventory inventory, GameItem item)
        {
            if (item.mayStackItemFunc != null || !OutputSpace.Rectangle(item.modifiedShape) || item.parentInventory != null)
                return OutputSpace.FindSlot(inventory, item);
            int amount = item.MaxNumRemove();
            if (amount < 1 || !inventory.MayHaveValidInventorySlot(item)) return null;
            // A callback could have freed space within this slice. A fresh native
            // search on exhaustion avoids falsely reporting a full inventory.
            return Find(item.modifiedShape, shape => inventory.TryInventorySlot(item, amount, shape)) ??
                OutputSpace.FindSlot(inventory, item);
        }
    }
}
