using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DreamDustAutomation.Planning;

namespace DreamDustAutomation
{
    // Only this machine's storage is guarded. Native hopper scheduling, filters,
    // keep counts and transfers remain intact. Manual removal is never gated.
    internal sealed class HopperOutputGate : IDisposable
    {
        private static readonly FieldInfo transferring = typeof(HopperHelper).GetField("transferring", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly GameInventory source;
        private readonly Func<bool> isTransferring;
        private readonly Func<int> currentFrame;
        private readonly Func<GameItem, GameItem> findOutlet;
        private readonly Dictionary<GameInventory, OutputSpace> snapshots = new Dictionary<GameInventory, OutputSpace>();
        private readonly HashSet<GameInventory> watched = new HashSet<GameInventory>();
        private sealed class Route
        {
            internal GameItem Intake, Outlet;
            internal int Link;
            internal int ResolvedFrame = -1;
            internal string Filter;
            internal HashSet<string> Allowed;
        }
        private readonly Dictionary<GameItem, Route> routes = new Dictionary<GameItem, Route>();
        private readonly Action<GameItem, GameInventory, SlotMarker> invalidate;
        private readonly Action<GameItem, GameInventory, SlotMarker> sourceChanged;
        private readonly Func<GameItem, GameInventory, SlotMarker, int> limit;
        private int frame = -1;
        private bool routesDirty = true, disposed;

        internal HopperOutputGate(GameInventory source, Func<bool> isTransferring = null,
            Func<int> currentFrame = null, Func<GameItem, GameItem> findOutlet = null)
        {
            this.source = source;
            this.isTransferring = isTransferring ?? (() => transferring?.FieldType == typeof(bool) && (bool)transferring.GetValue(null));
            this.currentFrame = currentFrame ?? (() => Time.frameCount);
            this.findOutlet = findOutlet ?? HopperHelper.FindOutlet;
            invalidate = (item, inventory, slot) => snapshots.Remove(inventory);
            sourceChanged = (item, inventory, slot) => SourceChanged(item);
            limit = (item, inventory, slot) => Limit(item);
            source.onSlotAddItemEndFunc += sourceChanged;
            source.onSlotRemoveItemEndFunc += sourceChanged;
            source.maxSlotRemoveItemFunc += limit;
        }

        internal int Limit(GameItem item)
        {
            // Fail open if a later game update changes the private transfer flag.
            // Read it only; never alter native hopper state or user configuration.
            if (disposed || !isTransferring() || item == null ||
                item.mayStackItemFunc != null || !OutputSpace.Rectangle(item.modifiedShape)) return int.MaxValue;
            if (frame != currentFrame())
            {
                ClearSnapshots(); frame = currentFrame();
                routesDirty = true;
            }
            if (routesDirty)
            {
                var intakes = new HashSet<GameItem>(source.childItems.Where(HopperHelper.IsIntake));
                foreach (var removed in routes.Keys.Where(i => !intakes.Contains(i)).ToArray()) routes.Remove(removed);
                foreach (var intake in intakes)
                    if (!routes.ContainsKey(intake)) routes[intake] = new Route { Intake = intake };
                routesDirty = false;
            }
            bool route = false;
            foreach (var connection in routes.Values)
            {
                var intake = connection.Intake;
                if (intake.parentInventory != source || !Passes(connection, item)) continue;
                int link = intake.GetTagReadonly(HopperHelper.LINK_INT).GetInt();
                if (link <= 0) continue;
                var outlet = connection.Outlet;
                if (connection.Link != link || outlet == null || outlet.uniqueId != link || outlet.parentInventory == null)
                {
                    if (connection.Link == link && connection.ResolvedFrame == frame) continue;
                    connection.Link = link;
                    connection.ResolvedFrame = frame;
                    connection.Outlet = outlet = findOutlet(intake);
                }
                if (outlet == null || outlet.uniqueId != link) continue;
                var target = outlet.parentInventory;
                if (target == null || target == source) continue;
                route = true;
                if (!snapshots.TryGetValue(target, out var space))
                {
                    space = OutputSpace.Capture(target);
                    snapshots[target] = space;
                    if (watched.Add(target))
                    {
                        target.onSlotAddItemEndFunc += invalidate;
                        target.onSlotRemoveItemEndFunc += invalidate;
                        target.onSlotChangeAmountItemFunc += invalidate;
                    }
                }
                if (space == null || space.Fits(item.modifiedShape.width, item.modifiedShape.height)) return int.MaxValue;
            }
            // Limit only when EVERY linked destination is provably too full.
            // MaxNumRemove (not MayRemove) preserves native per-category keep counts.
            return route ? 0 : int.MaxValue;
        }

        private static bool Passes(Route route, GameItem item)
        {
            int mode = HopperHelper.GetFilterMode(route.Intake);
            if (mode == HopperHelper.FILTER_ALL) return true;
            string filter = route.Intake.GetTagReadonly(HopperHelper.FILTER_STRING).GetString();
            if (route.Allowed == null || filter != route.Filter)
            {
                route.Filter = filter;
                route.Allowed = new HashSet<string>(HopperHelper.GetFilterList(route.Intake));
            }
            bool contains = route.Allowed.Contains(item.identifier);
            return mode == HopperHelper.FILTER_ALLOW ? contains : !contains;
        }

        internal void SourceChanged(GameItem item)
        {
            if (HopperHelper.IsHopper(item)) routesDirty = true;
        }

        internal void Clear()
        {
            ClearSnapshots(); routes.Clear(); routesDirty = true; frame = -1;
        }

        private void ClearSnapshots()
        {
            foreach (var inventory in watched)
            {
                inventory.onSlotAddItemEndFunc -= invalidate;
                inventory.onSlotRemoveItemEndFunc -= invalidate;
                inventory.onSlotChangeAmountItemFunc -= invalidate;
            }
            watched.Clear(); snapshots.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            source.onSlotAddItemEndFunc -= sourceChanged;
            source.onSlotRemoveItemEndFunc -= sourceChanged;
            source.maxSlotRemoveItemFunc -= limit;
            Clear();
        }
    }
}
