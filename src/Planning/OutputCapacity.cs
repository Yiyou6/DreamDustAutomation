using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Marks our native-compatible output inventory, not arbitrary modded grids.
    internal interface IMachineOutputInventory { }

    internal static class OutputCapacity
    {
        internal const int Width = 16, Rows = 160, VisibleRows = 8;

        // Pack actual shapes in vanilla orientation/row order. Empty-cell division
        // overestimates fragmented bins; every counted product has a legal position.
        internal static int Count(int width, int height, IEnumerable<GridShape> contents, GridShape product)
        {
            if (width <= 0 || height <= 0 || width > 512 || height > 512 || product == null ||
                product.GetOutsideBounds() != 0) return 0;
            var cells = new bool[width * height];
            foreach (var shape in contents)
                for (int y = Math.Max(0, shape.minY); y <= Math.Min(height - 1, shape.maxY); y++)
                    for (int x = Math.Max(0, shape.minX); x <= Math.Min(width - 1, shape.maxX); x++)
                        if (shape.Get(x, y) == 1) cells[y * width + x] = true;
            int count = 0;
            var candidate = new GridShapeBuilder(product);
            // Once an orientation has no fit, adding items cannot create one.
            for (int flip = 0; flip < 2; flip++) for (int rotation = 0; rotation < 4; rotation++)
            {
                candidate.SetTransform(0, 0, flip != 0, 4 - rotation);
                var rotated = candidate.Build();
                var points = new List<Tuple<int, int>>();
                for (int y = 0; y < candidate.globalHeight; y++) for (int x = 0; x < candidate.globalWidth; x++)
                    if (rotated.Get(x, y) == 1) points.Add(Tuple.Create(x, y));
                if (points.Count == 0) return 0;
                for (int y = 0; y <= height - candidate.globalHeight; y++)
                    for (int x = 0; x <= width - candidate.globalWidth; x++)
                    {
                        bool fits = true;
                        foreach (var p in points) if (cells[(y + p.Item2) * width + x + p.Item1]) { fits = false; break; }
                        if (!fits) continue;
                        foreach (var p in points) cells[(y + p.Item2) * width + x + p.Item1] = true;
                        count++;
                    }
            }
            return count;
        }

        internal static int DryProducts(int[] parts, ChemicalRecipe recipe) =>
            parts.Where((amount, i) => !Liquid.Liquids[i].isEvaporative).Sum() / recipe.UnitVolume;
    }
}
