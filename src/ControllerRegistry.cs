using System;
using System.Collections.Generic;

namespace DreamDustAutomation
{
    // Items own controllers through callbacks; this cross-save index must not
    // retain discarded item/UI graphs until the next save load.
    internal sealed class ControllerRegistry<T> where T : class
    {
        private readonly List<WeakReference<T>> entries = new List<WeakReference<T>>();
        internal void Add(T item)
        {
            entries.RemoveAll(entry => !entry.TryGetTarget(out var _));
            entries.Add(new WeakReference<T>(item));
        }
        internal List<T> Live(Func<T, bool> keep)
        {
            var result = new List<T>();
            entries.RemoveAll(entry =>
            {
                if (!entry.TryGetTarget(out var item) || !keep(item)) return true;
                result.Add(item);
                return false;
            });
            return result;
        }
    }
}
