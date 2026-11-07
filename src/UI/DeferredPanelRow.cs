using System;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    // Native SetText notifies the nearest GraphNodeStorage. On this large window,
    // each notification used to measure and rebuild every tab immediately.
    // Reimplement both interfaces so inventory notifications and window layout
    // share one pending update, while retaining the native storage graph/order.
    internal sealed class DeferredPanelRow : MachineryPanelRow, PixelElement, GraphNodeStorage
    {
        private PixelWindow window;
        private bool pending, flushing;
        private int fixedUpdateDepth;
        private int screenWidth, screenHeight;

        internal DeferredPanelRow(params MachineryPanel[] panels) : base(panels) { }

        internal void AttachTo(PixelWindow target)
        {
            window = target;
            handler.gameObject.AddComponent<MachineLayoutPump>().flush = Flush;
            pending = true;
        }

        public new void Validate()
        {
            if (flushing || fixedUpdateDepth > 0) return;
            if (window == null) { base.Validate(); return; }
            pending = true;
        }

        // A counter changing within a fixed box cannot affect the rest of the
        // window. Keep any already pending full update; suppress only notifications
        // produced by this local text update and refit its own box immediately.
        internal void UpdateFixedRegion(MachineRegion region, Action update)
        {
            long started = ModPerformance.Begin();
            fixedUpdateDepth++;
            try
            {
                update();
                MachineLayoutSizing.Reset(region);
                region.Validate();
            }
            catch { pending = true; throw; }
            finally { fixedUpdateDepth--; ModPerformance.Record(4, started, "progress_readout"); }
        }

        internal void Flush()
        {
            if (window != null && window.IsVisible() && (screenWidth != Screen.width || screenHeight != Screen.height))
            { pending = true; screenWidth = Screen.width; screenHeight = Screen.height; }
            if (!pending || flushing || window == null || !window.IsVisible()) return;
            pending = false;
            flushing = true;
            long started = ModPerformance.Begin();
            try
            {
                using (MachineLayoutPass.Begin())
                {
                    foreach (var panel in Panels) MachineLayoutSizing.Reset(panel);
                    base.Validate();
                    // The row has final dimensions now. Update the frame/header;
                    // its call back to our interface must not repeat the row pass.
                    window.Validate();
                    MachineLayoutSizing.Fit(window);
                }
            }
            finally
            {
                flushing = false;
                ModPerformance.Record(4, started, "ui_layout");
            }
        }
    }

    internal sealed class MachineLayoutPump : MonoBehaviour
    {
        internal Action flush;
        private void LateUpdate()
        {
            try { flush?.Invoke(); }
            catch (Exception error) { ModEntry.Log.Error("Machine UI layout: " + error); }
        }
    }
}
