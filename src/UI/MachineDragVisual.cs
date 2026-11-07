using System;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    // Native drag centering uses unscaled sizeDelta. Reparenting a ghost from
    // our fitted window to the overlay with worldPositionStays keeps the window
    // scale, however. Restore the overlay's native units, including after a
    // sorted item has been rotated, without changing its inventory footprint.
    internal sealed class MachineDragVisual : MonoBehaviour
    {
        internal GameItemElement item;

        internal static void Watch(GameItemElement item)
        {
            if (item?.fakeMiddle == null) return;
            var fix = item.fakeMiddle.gameObject.GetComponent<MachineDragVisual>() ??
                item.fakeMiddle.gameObject.AddComponent<MachineDragVisual>();
            fix.item = item;
            fix.Normalize();
        }

        internal void Normalize()
        {
            if (item?.fakeMiddle == null || item.handler == null) return;
            var middle = item.fakeMiddle.rectTransform;
            bool overlay = RenderHandler.current != null && middle.parent == RenderHandler.current.overlayRoot.rectTransform;
            if (!overlay && middle.parent != item.handler.rectTransform) return;
            if (middle.localScale == Vector3.one) return;
            if (overlay)
            {
                // Preserve the visible center on the initial reparent; subsequent
                // native centering, rotation and slot hit testing use matching units.
                var center = item.fakeRotator.rectTransform.TransformPoint(item.fakeRotator.rectTransform.rect.center);
                middle.localScale = Vector3.one;
                middle.position += center - item.fakeRotator.rectTransform.TransformPoint(item.fakeRotator.rectTransform.rect.center);
            }
            else middle.localScale = Vector3.one;
        }

        private void OnTransformParentChanged() => Normalize();
    }

    internal sealed class MachineDragVisualPump : MonoBehaviour
    {
        internal Func<GameItem, bool> owns;
        private GameItemElement last;

        private void LateUpdate()
        {
            var drag = ItemMouseDragHandler.current;
            if (drag == null || !drag.IsDraggingItem) { last = null; return; }
            var item = drag.GetCurrentItem() as GameItemElement;
            if (item == null || item == last) return;
            last = item;
            if (owns == null || !owns(item)) return;
            // One hook on the dragged item, never a scan/component per stored
            // bottle. It follows the ghost back even if this window is closed.
            MachineDragVisual.Watch(item);
        }
    }
}
