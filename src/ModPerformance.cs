using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace DreamDustAutomation
{
    // Measure only this mod's synchronous callbacks, not overall frame time.
    // Aggregate same-frame work (including nightly catch-up) and rate-limit logs.
    internal static class ModPerformance
    {
        private static int frame = -1;
        private static readonly long[] elapsed = new long[5];
        private static readonly int[] calls = new int[5];
        private static long lastReport;
        private static int depth;
        private static string lastUiAction;

        internal static long Begin() { depth++; return Stopwatch.GetTimestamp(); }

        internal static void Record(int category, long started, string operation = null)
        {
            if (--depth > 0) return; // Nested callbacks are included in their outer scope.
            long now = Stopwatch.GetTimestamp();
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                lastUiAction = null;
                for (int i = 0; i < elapsed.Length; i++) { elapsed[i] = 0; calls[i] = 0; }
            }
            elapsed[category] += now - started;
            calls[category]++;
            if (category == 4) lastUiAction = operation;
            long total = elapsed[0] + elapsed[1] + elapsed[2] + elapsed[3] + elapsed[4];
            if (total * 1000.0 / Stopwatch.Frequency < 16.0 ||
                (lastReport != 0 && now - lastReport < Stopwatch.Frequency * 10L) || ModEntry.Log == null) return;
            lastReport = now;
            string[] labels = { "tick", "cycle", "planner", "form", "ui" };
            var parts = new string[5];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = labels[i] + "=" + (elapsed[i] * 1000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture) + "ms/" + calls[i];
            ModEntry.Log.Log("[DDA_PERF] frame=" + frame + " measured_mod_work=" +
                (total * 1000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture) + "ms " + string.Join(" ", parts) +
                (lastUiAction == null ? "" : " ui_action=" + lastUiAction));
        }
    }
}
