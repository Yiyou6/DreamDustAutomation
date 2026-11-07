using System;
using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    // Render visibility only: never remove an inventory item or alter its state.
    // Remember only nodes hidden by us, so external visibility remains untouched.
    internal sealed class InventoryViewport<T> where T : class
    {
        private readonly HashSet<T> hidden = new HashSet<T>();
        private readonly Func<T, bool> active;
        private readonly Action<T, bool> show;
        internal InventoryViewport(Func<T, bool> active, Action<T, bool> show)
        { this.active = active; this.show = show; }

        internal void Update(T item, GridShape shape, int firstRow, int rows)
        {
            if (shape == null || rows <= 0 || (shape.maxY >= firstRow && shape.minY < firstRow + rows))
            { Release(item); return; }
            if (active(item)) { hidden.Add(item); show(item, false); }
        }

        internal void Release(T item)
        {
            if (hidden.Remove(item)) show(item, true);
        }
    }
}
