using System;
using System.Linq;

namespace DreamDustAutomation
{
    // Keep a reserved collector stable even when its native window is open:
    // moving it is blocked by InventoryLock, direct pouring by this binding.
    internal sealed class LiquidSourceBinding : IDisposable
    {
        private readonly GameItem item;
        private readonly Action<GameItem, GameInventory, SlotMarker> originalTick, guardedTick;
        private readonly Func<GameItem, GameItem, bool> originalTarget, guardedTarget;
        private bool disposed;
        public LiquidSourceBinding(GameItem item, Func<bool> busy, Func<bool> blockPour = null)
        {
            this.item = item;
            originalTick = item.onOneSecondItemFunc;
            originalTarget = item.canItemTargetItemFunc;
            guardedTick = (source, inventory, slot) => { if (!busy()) originalTick?.Invoke(source, inventory, slot); };
            guardedTarget = (source, target) => !(blockPour ?? busy)() && (originalTarget == null ||
                originalTarget.InvokeAllReduce(source, target, InvokeFuncExtensions.BoolReduceOR));
            item.onOneSecondItemFunc = guardedTick;
            item.canItemTargetItemFunc = guardedTarget;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (item.onOneSecondItemFunc == guardedTick) item.onOneSecondItemFunc = originalTick;
            if (item.canItemTargetItemFunc == guardedTarget) item.canItemTargetItemFunc = originalTarget;
        }
    }

    // Temporary ownership of native callbacks must end when a component leaves
    // the machine. Otherwise moving it to another machine stacks stale locks.
    internal sealed class InventoryLock : IDisposable
    {
        private readonly GameInventory inventory;
        private readonly Func<GameItem, GameInventory, bool> originalAdd, originalRemove, lockedAdd, lockedRemove;
        private bool disposed;
        public InventoryLock(GameInventory inventory, Func<bool> busy, Func<bool> blockAdd = null)
        {
            this.inventory = inventory;
            originalAdd = inventory.mayInventoryAddItemFunc;
            originalRemove = inventory.mayInventoryRemoveItemFunc;
            lockedAdd = (item, inv) => !(blockAdd ?? busy)() && (originalAdd == null || originalAdd(item, inv));
            lockedRemove = (item, inv) => !busy() && (originalRemove == null || originalRemove(item, inv));
            inventory.mayInventoryAddItemFunc = lockedAdd;
            inventory.mayInventoryRemoveItemFunc = lockedRemove;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (inventory.mayInventoryAddItemFunc == lockedAdd) inventory.mayInventoryAddItemFunc = originalAdd;
            if (inventory.mayInventoryRemoveItemFunc == lockedRemove) inventory.mayInventoryRemoveItemFunc = originalRemove;
        }
    }

    internal sealed class HardwareBinding : IDisposable
    {
        private const string ValueTag = "DDA_CORE_VALUE";
        private readonly GameItem core;
        private readonly InventoryLock modules;
        private readonly GameInventory moduleInventory;
        private readonly Action<GameItem, GameInventory, SlotMarker> modulesChanged;
        private readonly Action<GameItem, GameInventory, SlotMarker> tick;
        private readonly bool controlsCycle;
        private readonly bool originalTurboEligible;
        private Func<bool> canBoost;
        private Action boost;
        private readonly Action<GameItem, GameItem> boostTarget;
        private readonly Func<bool> busy;
        private readonly InventoryLock blenderInputs;
        private readonly InventoryLock auxiliaryContainer;
        private readonly MachineryButton blenderStart;
        private readonly bool originalDisabled;
        private readonly MachineryButton[] finisherControls;
        private readonly bool[] finisherDisabled;
        private bool disposed;
        public Action<GameItem, GameInventory, SlotMarker> Cycle { get; }
        public HardwareBinding(GameItem core, GameInventory modules, Func<bool> busy, bool controlsCycle, long fallbackValue,
            Func<bool> blockModuleAdd = null, Action onModulesChanged = null)
        {
            this.core = core;
            this.busy = busy;
            this.controlsCycle = controlsCycle;
            originalTurboEligible = core.IsTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG) ||
                (controlsCycle && core.identifier == "evaporator");
            this.modules = new InventoryLock(modules, busy, blockModuleAdd);
            moduleInventory = modules;
            if (onModulesChanged != null)
            {
                // Native callbacks apply the new module's effects first. Append
                // only our notification; never replace native upgrade handling.
                modulesChanged = (item, inventory, slot) => onModulesChanged();
                modules.onSlotAddItemEndFunc += modulesChanged;
                modules.onSlotRemoveItemEndFunc += modulesChanged;
            }
            // The blender buffer and evaporator reactor/collector are bound by
            // the controller. Also guard the native heater/finisher vessel slots,
            // even when those optional slots are empty or not used by this plan.
            if ((core.identifier == "heating_plate" || core.identifier == "chem_finisher") &&
                MachineHelper.GetBodyStorage(core, 1) is GameInventory vessel)
                auxiliaryContainer = new InventoryLock(vessel, busy);
            if (core.identifier == "blender" && core.contentWindow?.childElement is MachineryPanelRow row)
            {
                if (MachineHelper.GetBodyStorage(core, 1) is GameInventory input) blenderInputs = new InventoryLock(input, busy);
                blenderStart = row.Panels.FirstOrDefault(p => p.role == "control")?.content as MachineryButton;
                if (blenderStart != null) originalDisabled = blenderStart.isDisabled;
                core.contentWindow.openCallback += Refresh;
            }
            if (core.identifier == "chem_finisher" && core.contentWindow?.childElement is MachineryPanelRow finishingRow)
            {
                var controls = finishingRow.Panels.FirstOrDefault(p => p.role == "control")?.content as MachineryStack;
                finisherControls = controls?.elements.OfType<MachineryButton>().ToArray();
                finisherDisabled = finisherControls?.Select(button => button.isDisabled).ToArray();
                core.contentWindow.openCallback += Refresh;
            }
            tick = core.onOneSecondItemFunc;
            Cycle = core.onCycleEndSlotItemFunc;
            core.onOneSecondItemFunc = null;
            if (controlsCycle) core.onCycleEndSlotItemFunc = null;
            boostTarget = (source, target) =>
            {
                // Native source callbacks consume the booster/charge. The core
                // cycle stays detached, so nightly traversal cannot double-run.
                if (!disposed && target == core && IsBooster(source) && canBoost?.Invoke() == true)
                    boost?.Invoke();
            };
            if (controlsCycle) core.onItemTargetThisFunc += boostTarget;
            RestoreValue(core, fallbackValue);
        }
        private static bool IsBooster(GameItem item) => item != null &&
            (item.identifier == "turbo_booster" || item.identifier == "turbo_booster_adv" || item.identifier == "turbo_booster_hypertech");
        internal void BindBoost(Func<bool> ready, Action action) { canBoost = ready; boost = action; Refresh(); }
        internal static void RestoreValue(GameItem core, long fallbackValue)
        {
            // Before 0.3.10 the shell price included all four zero-valued cores.
            // Restore their saved prices once; native container pricing now adds
            // each installed device separately to the 500-credit shell.
            if (core.unitValue == 0)
            {
                long value;
                core.unitValue = long.TryParse(core.GetTagReadonly(ValueTag).GetString(), out value) ? value : fallbackValue;
            }
            // Retain an explicit zero-price legacy override across rebindings.
            if (core.unitValue != 0) core.DisableTag(ValueTag);
        }
        public void Refresh()
        {
            if (controlsCycle)
            {
                bool ready = canBoost?.Invoke() == true;
                if (core.IsTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG) != ready)
                {
                    if (ready) core.EnableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG, skipValidate: true);
                    else core.DisableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG, skipValidate: true);
                }
            }
            if (blenderStart != null && blenderStart.isDisabled != (originalDisabled || busy()))
                blenderStart.SetDisabled(originalDisabled || busy());
            if (finisherControls != null)
                for (int i = 0; i < finisherControls.Length; i++)
                    if (finisherControls[i].isDisabled != (finisherDisabled[i] || busy()))
                        finisherControls[i].SetDisabled(finisherDisabled[i] || busy());
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            modules.Dispose();
            if (modulesChanged != null)
            {
                moduleInventory.onSlotAddItemEndFunc -= modulesChanged;
                moduleInventory.onSlotRemoveItemEndFunc -= modulesChanged;
            }
            blenderInputs?.Dispose();
            auxiliaryContainer?.Dispose();
            if (core.contentWindow != null) core.contentWindow.openCallback -= Refresh;
            blenderStart?.SetDisabled(originalDisabled);
            if (finisherControls != null)
                for (int i = 0; i < finisherControls.Length; i++) finisherControls[i].SetDisabled(finisherDisabled[i]);
            core.onOneSecondItemFunc += tick;
            if (controlsCycle) core.onCycleEndSlotItemFunc += Cycle;
            if (controlsCycle) core.onItemTargetThisFunc -= boostTarget;
            canBoost = null; boost = null;
            if (controlsCycle && originalTurboEligible) core.EnableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG, skipValidate: true);
        }
    }
}
