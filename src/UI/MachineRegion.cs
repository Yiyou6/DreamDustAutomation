using System.Collections.Generic;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    // Fixed presentation space, not an inventory/scrolling node. Native storage
    // collection passes through unchanged. Oversized localized content fits
    // inside its own region without resizing the frame or moving other regions.
    internal sealed class MachineRegion : PixelElement, MachineryContainer
    {
        private MachineryStorageOwner storageOwner;
        private bool validating;
        private long measuredRevision = -1;
        internal PixelElement Content { get; }
        public bool canResize => false;
        public int widthPixels { get; }
        public int heightPixels { get; }
        public TreeNodeRender handler { get; }
        public RectTransform rectTransform => handler.rectTransform;
        public TreeNodeRender inner { get; }
        public MachineryStorageOwner owner
        {
            get => storageOwner;
            set { storageOwner = value; MachineryContainerUtils.SetOwner(Content, value); }
        }
        internal MachineRegion(PixelElement content, int width, int height)
        {
            Content = content; widthPixels = width; heightPixels = height;
            handler = TreeNodeRender.Instantiate(this, "Machine fixed region");
            handler.image.enabled = false;
            inner = TreeNodeRender.Instantiate().SetParent(handler);
            inner.image.enabled = false;
            Content.SetRenderingParent(this);
            Validate();
        }
        public void Validate()
        {
            if (validating) return;
            using (MachineLayoutPass.Begin())
            {
                if (measuredRevision == MachineLayoutPass.Revision) return;
                validating = true;
                try
                {
                    MachineryContainerUtils.Refresh(Content);
                    MachineryContainerUtils.TryResize(Content, widthPixels, 0);
                    float scale = Mathf.Min(1f, (float)widthPixels / Mathf.Max(1, Content.widthPixels),
                        (float)heightPixels / Mathf.Max(1, Content.heightPixels));
                    Content.rectTransform.localScale = new Vector3(scale, scale, 1);
                    Content.rectTransform.anchoredPosition = Vector2.zero;
                    rectTransform.sizeDelta = new Vector2(widthPixels, heightPixels);
                    MachineryStyle.Place(inner, 0, 0, widthPixels, heightPixels);
                    measuredRevision = MachineLayoutPass.Revision;
                }
                finally { validating = false; }
            }
        }
        public void CollectStorage(List<GraphNodeStorage> list) => MachineryContainerUtils.Collect(Content, list);
        public void ResizePixels(int width, int height) { }
        public int WidthPixelsClamp(int width) => widthPixels;
        public int HeightPixelsClamp(int height) => heightPixels;
        public int XClamp(int x) => 0;
        public int YClamp(int y) => 0;
        public bool Attach(PixelElement element) => false;
        public bool Detach(PixelElement element) => false;
    }
}
