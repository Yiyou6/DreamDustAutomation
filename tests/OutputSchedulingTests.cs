using System;
using System.Collections;

// Surround the extracted production coroutine with observable, headless effects.
// Native placement and chemistry are exercised separately in OutputPerformanceTests.
internal sealed class RunningBatch { internal int produced, target; internal bool failed; }
internal static class DeferredOutput { internal static int Target(RunningBatch b) => b.target; }
internal static class ModPerformance { internal static long Begin() => 0; internal static void Record(int n, long time) { } }
internal sealed class OutputPlacement
{
    internal static OutputPlacement Capture(OutputSchedulingTests c) { c.Captures++; return new OutputPlacement(); }
}
internal sealed class OutputSchedulingTests
{
    private RunningBatch running, formingBatch;
    private OutputSchedulingTests output;
    private bool internalMove;
    internal int Captures;
    private int Saves, SavedCount, Advances, physical, material, fullAt = int.MaxValue, failAt = -1;
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    private OutputSchedulingTests(int count)
    {
        output = this; running = new RunningBatch { target = count }; formingBatch = running; material = count;
    }
    private bool FormOne(OutputPlacement p)
    {
        Check(internalMove, "Native inventory moves must be internal");
        if (running.produced == failAt) throw new Exception("injected failure");
        if (running.produced == fullAt) return false;
        physical++; material--; running.produced++; return true;
    }
    private void Save() { Saves++; SavedCount = running == null ? 0 : running.produced; }
    private void RefreshProgress() { Check(SavedCount == physical, "Progress before checkpoint"); }
    private void Fail(Exception error) { running.failed = true; Save(); }
    private void Guard(Action action) { action(); }
    private void AdvanceImmediate() { Advances++; }
    // PRODUCTION_FORM_PRODUCTS

    private void RunUntilStop(int expected)
    {
        var work = FormProducts(running);
        int previous = physical, frames = 0;
        while (work.MoveNext())
        {
            Check(physical - previous <= 8, "Too many native products in one frame");
            Check(SavedCount == physical, "Yielded with stale save state");
            Check(!internalMove, "Internal movement flag leaked across frames");
            previous = physical;
            if (++frames > 3000) throw new Exception("Output stalled");
        }
        Check(SavedCount == physical && physical == expected, "Final checkpoint/count mismatch");
        Check(formingBatch == null && !internalMove, "Finished coroutine kept output locked");
    }
    public static void Main()
    {
        foreach (int count in new[] { 1, 7, 8, 9, 145, 350, 1700, 2560 })
        {
            var c = new OutputSchedulingTests(count); c.RunUntilStop(count);
            Check(c.material == 0 && c.Advances == 1, "Completion lost material or advanced twice");
            if (count >= 350) Check(c.Saves < count / 2, "Still checkpointing every product");
            Console.WriteLine(count + " products: " + c.Saves + " checkpoints, " + c.Captures + " placement snapshots");
        }
        var full = new OutputSchedulingTests(350) { fullAt = 19 };
        full.RunUntilStop(19);
        Check(full.material == 331 && full.Advances == 0, "Full output consumed extra material");
        full.fullAt = int.MaxValue; full.formingBatch = full.running; full.RunUntilStop(350);
        Check(full.material == 0 && full.Advances == 1, "Freeing output failed to resume");
        var error = new OutputSchedulingTests(350) { failAt = 17 };
        error.RunUntilStop(17);
        Check(error.running.failed && error.material == 333, "Failure checkpoint lost stock");
        error.running.failed = false; error.failAt = -1; error.formingBatch = error.running; error.RunUntilStop(350);
        var stop = new OutputSchedulingTests(350);
        var iterator = stop.FormProducts(stop.running);
        Check(iterator.MoveNext() && iterator.MoveNext(), "Output did not start");
        int made = stop.physical;
        stop.running = null; stop.Save(); // The real Stop() persists cancellation immediately.
        Check(!iterator.MoveNext() && stop.physical == made && stop.SavedCount == 0 && stop.formingBatch == null,
            "Old coroutine resumed or overwrote cancellation");
        var dispose = new OutputSchedulingTests(350);
        var d = dispose.FormProducts(dispose.running); d.MoveNext(); d.MoveNext(); ((IDisposable)d).Dispose();
        Check(dispose.SavedCount == dispose.physical && dispose.formingBatch == null, "Disposed coroutine left stale save/lock");
        int boundarySequences = 0;
        foreach (int count in new[] { 1, 2, 7, 8, 9, 15, 145, 350, 1700, 2560 })
        foreach (int point in new System.Collections.Generic.HashSet<int>(new[] { 0, Math.Min(1, count - 1), Math.Min(7, count - 1), Math.Min(8, count - 1), count - 1 }))
        {
            var blocked = new OutputSchedulingTests(count) { fullAt = point };
            blocked.RunUntilStop(point);
            for (int retry = 0; retry < 3; retry++)
            {
                blocked.formingBatch = blocked.running; blocked.RunUntilStop(point);
                Check(blocked.material == count - point, "repeated full-bin retry consumed stock");
            }
            blocked.fullAt = int.MaxValue; blocked.formingBatch = blocked.running; blocked.RunUntilStop(count);
            Check(blocked.material == 0 && blocked.Advances == 1, "boundary full-bin recovery duplicated output");
            boundarySequences++;
            var failed = new OutputSchedulingTests(count) { failAt = point };
            failed.RunUntilStop(point);
            Check(failed.running.failed && failed.material == count - point, "boundary failure corrupted remaining stock");
            failed.running.failed = false; failed.failAt = -1; failed.formingBatch = failed.running; failed.RunUntilStop(count);
            Check(failed.material == 0 && failed.Advances == 1, "boundary failure retry duplicated output");
            boundarySequences++;
        }
        Console.WriteLine(boundarySequences + " additional boundary output sequences passed: first/last item and frame boundary, repeated full-bin retry and error recovery.");
        Console.WriteLine(checks + " production-coroutine checks passed: frame limits, completion, full/resume, failure/retry, stop and disposal.");
    }
}
