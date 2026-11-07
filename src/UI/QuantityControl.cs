using System;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    // Native slider, with wheel fine-tuning for exact values on large batches.
    // No storage nodes or saved tab IDs are introduced by this control.
    internal sealed class QuantityControl : MachineryStack
    {
        private readonly MachineryText caption;
        private readonly MachinerySlider slider;
        internal Action<int> Changed;
        internal QuantityControl(int width, string help) : base(true, 0)
        {
            caption = new MachineryText("", 6, uppercase: false);
            slider = new MachinerySlider(1, width).SetRange(1, 1, 1).SetDisabled(true);
            slider.description = help;
            slider.onChanged = value => Changed?.Invoke(slider.GetInt());
            Add(caption); Add(slider);
            foreach (var node in new[] { handler, slider.handler, caption.handler })
            {
                node.InitEventHandler();
                node.eventHandler.mouseScrollFunc.Add(data =>
                {
                    if (!slider.isDisabled && data.scrollDelta.y != 0)
                        slider.SetInt(slider.GetInt() + Math.Sign(data.scrollDelta.y), true);
                    return true;
                });
            }
        }
        internal void Show(int selected, int maximum, string text, bool disabled)
        {
            int limit = Math.Max(1, maximum);
            if (slider.max != limit) slider.SetRange(1, limit, 1);
            int value = Math.Max(1, Math.Min(limit, selected));
            if (slider.GetInt() != value) slider.SetInt(value);
            if (caption.GetText() != text) caption.SetText(text);
            SetDisabled(disabled || maximum <= 1);
        }
        internal void SetHelp(string help) { slider.description = help; }
        internal void SetDisabled(bool disabled)
        {
            // Native SetDisabled also ends dragging: never call it repeatedly
            // while an unchanged enabled slider is being dragged.
            if (slider.isDisabled != disabled) slider.SetDisabled(disabled);
        }
    }
}
