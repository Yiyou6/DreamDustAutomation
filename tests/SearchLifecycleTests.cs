using System;
using System.Collections;

internal sealed class SearchLifecycleTests
{
    private bool searching = true, quantitySearching, failThrows;
    private int searchGeneration = 1, failures;
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception(reason); }
    private void Fail(Exception error)
    {
        Check(!searching && !quantitySearching, "failure UI ran before releasing search state");
        failures++;
        if (failThrows) throw new Exception("failure UI unavailable");
    }
    // PRODUCTION_GuardSearch
    private sealed class Work : IEnumerator, IDisposable
    {
        internal int position = -1, failAt = -1, disposed;
        internal bool failCurrent, failDispose;
        internal readonly object value = new object();
        public bool MoveNext()
        { if (++position == failAt) throw new Exception("injected search/refresh failure"); return position < 3; }
        public object Current { get { if (failCurrent) throw new Exception("injected yield failure"); return value; } }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { disposed++; if (failDispose) throw new Exception("injected dispose failure"); }
    }
    public static void Main()
    {
        foreach (bool quantity in new[] { false, true })
        foreach (int failAt in new[] { -1, 0, 1, 3 })
        foreach (bool reportingFails in new[] { false, true })
        {
            var c = new SearchLifecycleTests { quantitySearching = quantity, failThrows = reportingFails };
            var work = new Work { failAt = failAt }; var guarded = c.GuardSearch(1, work);
            bool escaped = false;
            try { while (guarded.MoveNext()) Check(ReferenceEquals(guarded.Current, work.value), "yield value changed"); }
            catch { escaped = true; }
            Check(escaped == (reportingFails && failAt >= 0), "unexpected exception result");
            Check(!c.searching && !c.quantitySearching, "failure/completion left search locked");
            Check(c.failures == (failAt >= 0 ? 1 : 0), "failure reported more than once");
            Check(work.disposed == 1, "work was not disposed exactly once");
        }
        foreach (bool failCurrent in new[] { false, true })
        {
            var c = new SearchLifecycleTests { quantitySearching = true };
            var work = new Work { failCurrent = failCurrent, failDispose = !failCurrent };
            try { var guarded = c.GuardSearch(1, work); while (guarded.MoveNext()) { } } catch { }
            Check(!c.searching && !c.quantitySearching && work.disposed == 1, "yield/dispose failure left search locked");
        }
        foreach (bool started in new[] { false, true })
        {
            var c = new SearchLifecycleTests { quantitySearching = true };
            var oldWork = new Work(); var old = c.GuardSearch(1, oldWork);
            if (started) Check(old.MoveNext(), "search failed to start");
            c.searchGeneration = 2;
            Check(!old.MoveNext(), "cancelled search continued");
            Check(c.searching && c.quantitySearching && c.failures == 0, "old search altered replacement state");
            Check(oldWork.disposed == 1 && oldWork.position == (started ? 0 : -1), "old search performed work after cancellation");
            var current = c.GuardSearch(2, new Work()); while (current.MoveNext()) { }
            Check(!c.searching && !c.quantitySearching, "replacement search failed to finish");
        }
        var stopped = new SearchLifecycleTests(); var stopWork = new Work(); var stopping = stopped.GuardSearch(1, stopWork);
        Check(stopping.MoveNext(), "stop fixture did not start");
        ((IDisposable)stopping).Dispose();
        Check(!stopped.searching && !stopped.quantitySearching && stopWork.disposed == 1, "disposed active search retained lock");
        Console.WriteLine(checks + " search lifecycle checks passed; full/quantity search, initial/mid/final refresh faults, failed error UI, cancellation, replacement and disposal.");
    }
}
