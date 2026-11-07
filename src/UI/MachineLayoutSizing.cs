using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DreamDustAutomation.UI
{
    // Native containers remember their parent's last allocation as a minimum.
    // Clear those allocations together before measuring, so hidden/shortened
    // content cannot leave a growing minimum in the next bottom-up layout pass.
    // Only our presentation tree is visited: never inventories or native core UIs.
    internal static class MachineLayoutSizing
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        // Only known game layout fields. Dynamic GetType/BaseType traversal is
        // unnecessary and requires reflection-external under the native scanner.
        private static readonly FieldInfo[] panelRequests = {
            typeof(MachineryPanel).GetField("requestedWidth", Fields), typeof(MachineryPanel).GetField("requestedHeight", Fields) };
        private static readonly FieldInfo[] stackRequests = {
            typeof(MachineryStack).GetField("requestedWidth", Fields), typeof(MachineryStack).GetField("requestedHeight", Fields) };
        private static readonly FieldInfo[] tabRequests = {
            typeof(MachineryTabs).GetField("requestedWidth", Fields), typeof(MachineryTabs).GetField("requestedHeight", Fields) };
        private static readonly FieldInfo[] intakeRequests = {
            typeof(MachineryIntake).GetField("requestedWidth", Fields), typeof(MachineryIntake).GetField("requestedHeight", Fields) };
        private static readonly FieldInfo[] textRequests = { typeof(MachineryText).GetField("requestedWidth", Fields) };
        private static readonly FieldInfo[] progressRequests = { typeof(MachineryProgressBar).GetField("requestedWidth", Fields) };
        internal static void Reset(PixelElement element)
        {
            if (element == null || element is GraphNodeStorage) return;
            var fields = element is MachineryPanel ? panelRequests : element is MachineryStack ? stackRequests :
                element is MachineryTabs ? tabRequests : element is MachineryIntake ? intakeRequests :
                element is MachineryText ? textRequests : element is MachineryProgressBar ? progressRequests : null;
            if (fields != null) foreach (var field in fields) field?.SetValue(element, 0);
            if (element is MachineRegion region) Reset(region.Content);
            else if (element is MachineryPanel panel) Reset(panel.content);
            else if (element is MachineryStack stack) foreach (var child in stack.elements) Reset(child);
            else if (element is MachineryTabs tabs) for (int i = 0; i < tabs.count; i++) Reset(tabs.GetPage(i));
            else if (element is MachineryIntake intake) Reset(intake.content);
        }

        internal static void Fit(PixelWindow window)
        {
            if (window == null || !window.IsVisible()) return;
            var rect = window.rectTransform;
            var parent = rect.parent as RectTransform;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (parent == null || canvas == null || window.widthPixels <= 0 || window.heightPixels <= 0) return;
            canvas = canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect screen = canvas.pixelRect;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen.min + new Vector2(8, 8), camera, out var low) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen.max - new Vector2(8, 8), camera, out var high)) return;
            float width = high.x - low.x, height = high.y - low.y;
            if (width <= 0 || height <= 0) return;
            // A window that merely fits can still obscure nearly the whole game.
            // Reserve breathing room in canvas coordinates, including when the
            // game's pixel-perfect canvas uses a different scale or letterboxing.
            float scale = Mathf.Min(1f, width * .85f / window.widthPixels, height * .80f / window.heightPixels);
            rect.localScale = new Vector3(scale, scale, 1);
            Vector3 position = rect.localPosition;
            position.x = Mathf.Clamp(position.x, low.x, high.x - window.widthPixels * scale);
            position.y = Mathf.Clamp(position.y, low.y + window.heightPixels * scale, high.y);
            rect.localPosition = position;
        }
    }
}
