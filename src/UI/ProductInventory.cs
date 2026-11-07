using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    internal sealed class ProductInventory : GameGridScrollableInventory, IMachineOutputInventory
    {
        private readonly Dictionary<string, int> capacities = new Dictionary<string, int>();
        private TreeNodeRender selectionEdge;
        private InventoryViewport<GameItemElement> viewport;
        private bool viewportDirty = true;
        private int firstRow = -1, visibleRows;

        internal ProductInventory() : base(OutputCapacity.Width, OutputCapacity.VisibleRows, OutputCapacity.Rows, 1f)
        {
            viewport = new InventoryViewport<GameItemElement>(item => item.handler != null && item.handler.gameObject.activeSelf,
                (item, show) => { if (item.handler != null) item.handler.gameObject.SetActive(show); });
            handler.gameObject.AddComponent<MachineLayoutPump>().flush = RefreshViewport;
            // Use the intake's existing left margin. A full grid otherwise leaves
            // no non-item hit target from which vanilla can start a selection band.
            selectionEdge = TreeNodeRender.Instantiate().SetParent(handler);
            MachineryStyle.SetFlat(selectionEdge, new Color32(90, 96, 88, 255));
            selectionEdge.image.raycastTarget = true;
            selectionEdge.InitEventHandler();
            selectionEdge.eventHandler.mouseDownFunc.Add(r => { handler.FindParentWindow()?.ToFront(); return true; });
            selectionEdge.eventHandler.mouseScrollFunc.Add(r => { Scroll(r.scrollDelta); return false; });
            Validate();
        }

        public override bool UncheckedAccept(GameItem item)
        {
            bool accepted = base.UncheckedAccept(item);
            if (accepted)
            {
                InvalidateCapacity();
                // Hide below-viewport products before the next Canvas render,
                // including while the output tab or machine window is closed.
                UpdateVisibility(item);
            }
            return accepted;
        }

        public override bool Expel(GameItem item)
        {
            try { return base.Expel(item); }
            finally
            {
                if (item != null && item.parentInventory != this)
                {
                    if (item is GameItemElement element) viewport?.Release(element);
                    viewportDirty = true;
                    InvalidateCapacity();
                }
            }
        }

        private void UpdateVisibility(GameItem item)
        {
            if (item is GameItemElement element && ((PixelElement)element).IsRenderingChildOf(this))
                viewport?.Update(element, item.modifiedShape,
                    Mathf.RoundToInt(inner.rectTransform.anchoredPosition.y / UISettings.current.gridSize), GetVisibleHeight());
        }

        private void RefreshViewport()
        {
            int row = Mathf.RoundToInt(inner.rectTransform.anchoredPosition.y / UISettings.current.gridSize);
            int rows = GetVisibleHeight();
            if (!viewportDirty && row == firstRow && rows == visibleRows) return;
            // Native drag previews temporarily relocate render nodes. Reconcile
            // after release rather than hiding a dragged/selected item's image.
            if ((ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) ||
                (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging)) return;
            if (viewportDirty)
            {
                base.Validate(false);
                row = Mathf.RoundToInt(inner.rectTransform.anchoredPosition.y / UISettings.current.gridSize);
            }
            viewportDirty = false; firstRow = row; visibleRows = rows;
            foreach (var item in childItems) UpdateVisibility(item);
        }

        internal void InvalidateViewport() { viewportDirty = true; InvalidateCapacity(); }

        // Native UncheckedAccept already validates the newly added item. Its
        // subsequent parent refresh must not validate every older bottle again.
        public override void Validate()
        {
            base.Validate(false);
            if (selectionEdge == null) return;
            selectionEdge.rectTransform.anchoredPosition = new Vector2(-4, 0);
            selectionEdge.rectTransform.sizeDelta = new Vector2(3, heightPixels);
        }

        internal void InvalidateCapacity() => capacities.Clear();
        internal int Remaining(string productId, GridShape shape)
        {
            if (!capacities.TryGetValue(productId, out int count))
                capacities[productId] = count = OutputCapacity.Count(width, height, childItems.Select(i => i.modifiedShape), shape);
            return count;
        }
    }
}
