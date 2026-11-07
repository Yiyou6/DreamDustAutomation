using UnityEngine;

namespace DreamDustAutomation.UI
{
    internal static class MachineInventoryVisuals
    {
        internal static void Bind(GameInventory inventory)
        {
            // Native Expel uses ResetParent(worldPositionStays: true); Accept
            // reparents with false, retaining that scale. Under a fitted window
            // every move would therefore multiply the item root's scale again.
            // Fix only the transferred root, not the grid, footprint or rotator.
            inventory.onSlotAddItemEndFunc += Transferred;
            inventory.onSlotRemoveItemEndFunc += Transferred;
        }

        private static void Transferred(GameItem item, GameInventory inventory, SlotMarker slot) => Normalize(item);

        internal static void Restore(GameInventory inventory)
        {
            // One pass when opening, including hidden pages. No per-frame scans
            // or item.Validate calls over the potentially large output bin.
            foreach (var item in inventory.childItems) Normalize(item);
        }

        internal static void Normalize(GameItem item)
        {
            if (!(item is GameItemElement element) || element.handler == null) return;
            var root = element.handler.rectTransform;
            if (root.localScale != Vector3.one) root.localScale = Vector3.one;
            if (element.fakeMiddle == null) return;
            var ghost = element.fakeMiddle.rectTransform;
            if (ghost.parent == root)
            {
                if (ghost.localScale != Vector3.one) ghost.localScale = Vector3.one;
            }
            else if (RenderHandler.current != null && ghost.parent == RenderHandler.current.overlayRoot.rectTransform)
            {
                // Also cover an incoming drag (and group transfers), which the
                // machine's drag pump cannot observe while owned by another bin.
                MachineDragVisual.Watch(element);
            }
        }
    }
}
