using System;

namespace DreamDustAutomation.Planning
{
    internal static class BottlingOperations
    {
        // Assign one complete composition, then recompute native state/rendering
        // once. Intermediate zero/partial compositions never need to be rendered.
        internal static void WriteParts(GameItem item, int[] parts)
        {
            if (parts == null || parts.Length != Liquid.Liquids.Count) throw new ArgumentException("Invalid composition.");
            foreach (int amount in parts) if (amount < 0) throw new ArgumentException("Negative composition.");
            for (int i = 0; i < parts.Length; i++)
            {
                int amount = parts[i];
                item.ModifyTag(Liquid.Liquids[i].currentPart, tag => tag.Enable().SetInt(amount), skipValidate: true);
            }
            item.ValidateShapeState();
        }

        internal static void Consume(GameItem tank, int volume)
        {
            var parts = WaterHelper.GetLiquidParts(tank);
            int total = 0;
            foreach (int amount in parts) total = checked(total + amount);
            if (volume <= 0 || volume > total) throw new InvalidOperationException("Invalid bottling volume.");
            // Same native integer proportional splitter and post-pour callback as
            // WaterHelper.RemoveAndReturn; only coalesce its per-tag refreshes.
            var taken = WaterHelper.SplitProportionally(parts, volume);
            for (int i = 0; i < parts.Length; i++) parts[i] -= taken[i];
            WriteParts(tank, parts);
            WaterFeatureHelper.OnLiquidContainerPourOutOf(tank);
        }
    }
}
