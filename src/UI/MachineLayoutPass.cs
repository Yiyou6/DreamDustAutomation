using System;

namespace DreamDustAutomation.UI
{
    // Native containers measure, then resize (and remeasure) each child. Nested
    // stacks multiply this work. Fixed regions have the same allocation throughout
    // a pass, so their contents need only one measurement in that pass.
    internal sealed class MachineLayoutPass : IDisposable
    {
        private static int depth;
        internal static long Revision { get; private set; }
        private bool disposed;
        private MachineLayoutPass() { if (depth++ == 0) Revision++; }
        internal static MachineLayoutPass Begin() => new MachineLayoutPass();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            depth--;
        }
    }
}
