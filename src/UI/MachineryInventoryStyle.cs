using System.Reflection;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    internal static class MachineryInventoryStyle
    {
        // Scrollable inventories lack the native SetMachineryStyle method exposed
        // by fixed grids. Change only this inventory's render nodes, never shared
        // atlas textures, item images, hit targets, or storage structure.
        private static TreeNodeRender Node(GameGridScrollableInventory inventory, string name) =>
            typeof(GameGridScrollableInventory).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(inventory) as TreeNodeRender;

        internal static void Apply(GameGridScrollableInventory inventory)
        {
            var background = Node(inventory, "background");
            if (background != null && MachineryStyle.Cell != null)
            {
                background.image.sprite = MachineryStyle.Cell;
                background.image.color = Color.white;
            }
            var border = Node(inventory, "insideBorder");
            if (border != null)
            {
                MachineryStyle.SetSliced(border, MachineryStyle.BevelFrame);
                border.image.fillCenter = false;
            }
            var track = Node(inventory, "scrollBar");
            if (track != null) MachineryStyle.SetFlat(track, new Color32(38, 38, 38, 255));
            var thumb = Node(inventory, "scrollButton");
            if (thumb != null) MachineryStyle.SetFlat(thumb, new Color32(148, 153, 145, 255));
        }
    }
}
