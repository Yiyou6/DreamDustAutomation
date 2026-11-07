using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Geometry is only an inexpensive rejection test. Native inventory rules
    // still validate every actual insertion; stacking and passthrough use native search.
    internal sealed class OutputSpace
    {
        private readonly int width, height;
        private readonly int[] occupied;
        private readonly Dictionary<long, bool> fits = new Dictionary<long, bool>();

        internal OutputSpace(int width, int height, GridShape boundary, IEnumerable<GridShape> contents)
        {
            this.width = width; this.height = height;
            var cells = new bool[width * height];
            if (boundary != null)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) cells[y * width + x] = boundary.Get(x, y) != 0;
            foreach (var shape in contents)
                for (int y = Math.Max(0, shape.minY); y <= Math.Min(height - 1, shape.maxY); y++)
                    for (int x = Math.Max(0, shape.minX); x <= Math.Min(width - 1, shape.maxX); x++)
                        if (shape.Get(x, y) == 1) cells[y * width + x] = true;
            occupied = new int[(width + 1) * (height + 1)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    occupied[(y + 1) * (width + 1) + x + 1] = (cells[y * width + x] ? 1 : 0) +
                        occupied[y * (width + 1) + x + 1] + occupied[(y + 1) * (width + 1) + x] - occupied[y * (width + 1) + x];
        }

        internal static bool Rectangle(GridShape shape)
        {
            if (shape == null || shape.width <= 0 || shape.height <= 0 || shape.GetOutsideBounds() != 0) return false;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++) if (shape.GetLocal(x, y) != 1) return false;
            return true;
        }

        internal bool Free(int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > width || y + h > height) return false;
            int stride = width + 1;
            return occupied[(y + h) * stride + x + w] - occupied[y * stride + x + w] -
                occupied[(y + h) * stride + x] + occupied[y * stride + x] == 0;
        }

        internal bool Fits(int w, int h)
        {
            if (w <= 0 || h <= 0) return false;
            long key = ((long)Math.Min(w, h) << 32) | (uint)Math.Max(w, h);
            if (fits.TryGetValue(key, out var result)) return result;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (Free(x, y, w, h) || (w != h && Free(x, y, h, w))) return fits[key] = true;
            return fits[key] = false;
        }

        internal static OutputSpace Capture(GameInventory inventory)
        {
            int w, h;
            GridShape boundary = null;
            // Unknown modded inventory subclasses may have different placement rules.
            if (inventory?.GetType() == typeof(GameGridScrollableInventory) || inventory is IMachineOutputInventory)
            { var grid = (GameGridScrollableInventory)inventory; w = grid.width; h = grid.height; }
            else if (inventory?.GetType() == typeof(GameGridInventory))
            { boundary = ((GameGridInventory)inventory).inventoryShape; w = boundary.width; h = boundary.height; }
            else return null;
            if (w <= 0 || h <= 0 || w > 512 || h > 512 || inventory.slotSearchPassthroughForItemFunc != null) return null;
            var items = inventory.childItems;
            if (items.Any(item => item.modifiedShape == null || item.slotSearchPassthroughForItemFunc != null ||
                item.modifiedShape.GetOutsideBounds() != 0)) return null;
            return new OutputSpace(w, h, boundary, items.Select(item => item.modifiedShape));
        }

        internal static SlotMarker FindSlot(GameGridScrollableInventory inventory, GameItem product)
        {
            OutputSpace space;
            if (product.mayStackItemFunc != null || !Rectangle(product.modifiedShape) ||
                inventory.childItems.Contains(product) || (space = Capture(inventory)) == null)
                return inventory.TryFindOneValidInventorySlot(product, false);
            int amount = product.MaxNumRemove();
            if (amount < 1 || !inventory.MayHaveValidInventorySlot(product)) return null;
            var shape = new GridShapeBuilder(product.modifiedShape);
            // Same orientation/position order as vanilla; skip only occupied cells.
            for (int flip = 0; flip < 2; flip++)
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    shape.SetTransform(0, 0, flip != 0, 4 - rotation);
                    for (int y = 0; y < inventory.height; y++)
                        for (int x = 0; x < inventory.width; x++)
                            if (space.Free(x, y, shape.globalWidth, shape.globalHeight))
                            {
                                shape.SetTransform(x, y, flip != 0, 4 - rotation);
                                var slot = inventory.TryInventorySlot(product, amount, shape);
                                if (slot.numTransfer > 0) return slot;
                            }
                }
            return null;
        }
    }
}
