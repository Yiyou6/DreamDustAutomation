using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class BatchInputReservation
    {
        internal const string SourceTag = "DDA_SOURCE_INDEX";
        private readonly HashSet<GameItem> active = new HashSet<GameItem>();
        private readonly Dictionary<GameItem, LiquidSourceBinding> isolated = new Dictionary<GameItem, LiquidSourceBinding>();
        internal void Capture(IEnumerable<GameItem> items, bool separateDirect = false)
        {
            Clear(); active.UnionWith(items);
            if (separateDirect)
                foreach (var item in active.Where(i => i.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)))
                    isolated[item] = new LiquidSourceBinding(item, () => true);
        }
        internal void Restore(IEnumerable<GameItem> items, bool separateDirect = false) { Capture(items.Where(item => item.IsTag(SourceTag)), separateDirect); }
        internal void Clear() { foreach (var binding in isolated.Values) binding.Dispose(); isolated.Clear(); active.Clear(); }
        internal void Release(GameItem item)
        {
            active.Remove(item);
            if (isolated.TryGetValue(item, out var binding)) { binding.Dispose(); isolated.Remove(item); }
            item.DisableTag(SourceTag, skipValidate: true);
        }
        internal bool Contains(GameItem item) => active.Contains(item);
        internal bool CanRemove(GameItem item, bool searching, bool running) => !searching && (!running || !Contains(item));
        internal bool CanStackInto(GameItem target, bool running) => !running || target == null || !Contains(target);
        internal void AddedDuringBatch(GameItem item)
        {
            // An incoming container may carry an old source index from a different
            // machine. It must not impersonate an ingredient in this running plan,
            // including after saving and loading the machine.
            if (!Contains(item)) item.DisableTag(SourceTag, skipValidate: true);
        }
    }
}
