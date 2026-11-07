using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DreamDustAutomation;
using DreamDustAutomation.Planning;

internal static class LiveModuleTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Live modules: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static GameSlotInventory Inventory() => (GameSlotInventory)FormatterServices.GetUninitializedObject(typeof(GameSlotInventory));
    private static T Unbuilt<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Field(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    private static void AttachNativeWindow(GameItem core, params PixelElement[] storages)
    {
        var panels = storages.Select(storage => {
            var panel = Unbuilt<MachineryPanel>();
            Field(panel, "<content>k__BackingField", storage); return panel;
        }).ToList();
        var row = Unbuilt<MachineryPanelRow>(); Field(row, "panels", panels);
        var window = Unbuilt<PixelWindow>(); Field(window, "<childElement>k__BackingField", row);
        typeof(GameItem).GetProperty("contentWindow").SetValue(core, window);
    }
    private static void CheckNativeSlotsAndHeating()
    {
        foreach (string id in new[] { "heating_plate", "chem_finisher" })
        {
            var core = Mix(); core.identifier = id;
            var modules = Unbuilt<GameGridScrollableInventory>();
            var installed = new List<GameItem>(); Field(modules, "items", installed);
            var vesselSlot = Inventory(); var vessel = Mix("water", 200000);
            Field(vesselSlot, "currentItem", vessel);
            AttachNativeWindow(core, modules, vesselSlot);
            Func<GameItem, GameInventory, bool> acceptsLiquid = (item, inv) => item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);
            vesselSlot.mayInventoryAddItemFunc = acceptsLiquid;
            bool busy = true;
            using (var binding = new HardwareBinding(core, modules, () => busy, false, 250, () => false))
            {
                Check(ReferenceEquals(MachineHelper.GetBodyStorage(core, 1), vesselSlot), "native vessel slot lookup");
                Check(!vesselSlot.mayInventoryAddItemFunc(vessel, vesselSlot) && !vesselSlot.mayInventoryRemoveItemFunc(vessel, vesselSlot), "embedded vessel can move during processing");
                Field(vesselSlot, "currentItem", null);
                Check(!vesselSlot.mayInventoryAddItemFunc(vessel, vesselSlot), "empty embedded vessel slot can be filled during processing");
                Field(vesselSlot, "currentItem", vessel);
                var module = Mix(); module.EnableTag("MODULE_TAG");
                Check(modules.mayInventoryAddItemFunc(module, modules), "vessel lock also blocked module installation");
                installed.Add(module); installed.Add(new SimulationItem(module));
                Check(MachineBlender.GetUpgradeCount(core) == 2, "native module count fixture");
                var frozen = new SimulationItem(vessel);
                core.state.GetTag(MachineHeatingPlate.CURRENT_TARGET_TEMP).Enable().SetInt(65);
                MachineHeatingPlate.HandleHeating(vesselSlot, 2, core);
                ChemistryOperations.ApplyBatchHeating(frozen, 2, 65);
                Check(ChemistryOperations.Temperature(frozen) == ChemistryOperations.Temperature(vessel), "batch helper differs from native two-module heating");
                installed.Add(new SimulationItem(module)); installed.Add(new SimulationItem(module));
                Check(MachineBlender.GetUpgradeCount(core) == 4, "native installation fixture");
                // This is the regression: native HandleHeating accelerates now,
                // whereas this batch must retain its two-module configuration.
                MachineHeatingPlate.HandleHeating(vesselSlot, 2, core);
                ChemistryOperations.ApplyBatchHeating(frozen, 2, 65);
                Check(ChemistryOperations.Temperature(frozen) < ChemistryOperations.Temperature(vessel), "pending module changed current batch heating");
                busy = false;
                Check(vesselSlot.mayInventoryAddItemFunc(vessel, vesselSlot) && vesselSlot.mayInventoryRemoveItemFunc(vessel, vesselSlot), "idle vessel stays locked");
            }
            Check(vesselSlot.mayInventoryAddItemFunc == acceptsLiquid && vesselSlot.mayInventoryRemoveItemFunc == null, "detached vessel retains stale locks");
        }
    }
    private static void CheckCallbacks()
    {
        foreach (string id in new[] { "blender", "heating_plate", "evaporator", "chem_finisher" })
        {
            var core = Mix(); core.identifier = id;
            var modules = Inventory();
            var module = Mix(); module.EnableTag("MODULE_TAG");
            bool running = false, searching = false;
            int effects = 0, notices = 0, cycles = 0, performanceAtCycle = -1;
            Func<GameItem, GameInventory, bool> accept = (item, inv) => item.IsTag("MODULE_TAG");
            Func<GameItem, GameInventory, bool> remove = (item, inv) => item != core;
            Action<GameItem, GameInventory, SlotMarker> add = (item, inv, slot) => {
                effects++;
                core.state.GetTag("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT").Enable().SetInt(effects * 100);
            };
            Action<GameItem, GameInventory, SlotMarker> take = (item, inv, slot) => effects--;
            modules.mayInventoryAddItemFunc = accept;
            modules.mayInventoryRemoveItemFunc = remove;
            modules.onSlotAddItemEndFunc = add;
            modules.onSlotRemoveItemEndFunc = take;
            core.onCycleEndSlotItemFunc = (item, inv, slot) => {
                cycles++; performanceAtCycle = MachineryHelper.GetCurrentPerformanceBonus(item);
            };
            var binding = new HardwareBinding(core, modules, () => running || searching, id == "evaporator", 300,
                () => searching, () => {
                    notices++;
                    Check(effects == notices, "notification ran before native installation effects");
                });
            Check(modules.mayInventoryAddItemFunc(module, modules), "idle add blocked");
            Check(modules.mayInventoryRemoveItemFunc(module, modules), "idle native removal rule lost");
            Check(!modules.mayInventoryAddItemFunc(core, modules), "native module eligibility bypassed");
            Check(!modules.mayInventoryRemoveItemFunc(core, modules), "native removal restrictions bypassed");
            searching = true;
            Check(!modules.mayInventoryAddItemFunc(module, modules) && !modules.mayInventoryRemoveItemFunc(module, modules), "search snapshot can change");
            searching = false; running = true;
            Check(modules.mayInventoryAddItemFunc(module, modules), "running installation blocked");
            Check(!modules.mayInventoryRemoveItemFunc(module, modules), "running removal allowed");
            var tankSlot = Inventory();
            using (var tankLock = new InventoryLock(tankSlot, () => running))
                Check(!tankSlot.mayInventoryAddItemFunc(module, tankSlot) && !tankSlot.mayInventoryRemoveItemFunc(module, tankSlot), "container lock was weakened");
            var forecast = EvaporationEstimate.Read(Mix("water", 900000), core);
            Check(forecast.Days == 3, "initial evaporation estimate");
            modules.onSlotAddItemEndFunc(module, modules, null);
            Check(effects == 1 && notices == 1 && cycles == 0, "install duplicates callbacks or triggers early cycle");
            Check(EvaporationEstimate.Read(Mix("water", 900000), core).Days == 2, "forecast failed to see native performance change");
            if (id == "evaporator")
            {
                Check(core.onCycleEndSlotItemFunc == null, "double evaporation hook restored during installation");
                binding.Cycle(core, modules, null);
                Check(cycles == 1 && performanceAtCycle == 100, "next evaporation did not use newly installed effects");
            }
            running = false;
            Check(modules.mayInventoryRemoveItemFunc(module, modules), "idle lock did not release");
            // Other subscribers installed after us must survive detaching.
            int later = 0;
            Action<GameItem, GameInventory, SlotMarker> late = (item, inv, slot) => later++;
            modules.onSlotAddItemEndFunc += late;
            binding.Dispose(); binding.Dispose();
            Check(modules.mayInventoryAddItemFunc == accept && modules.mayInventoryRemoveItemFunc == remove, "detach failed to restore native predicates");
            Check(modules.onSlotRemoveItemEndFunc == take, "remove callback leaked");
            modules.onSlotAddItemEndFunc(module, modules, null);
            Check(effects == 2 && notices == 1 && later == 1, "detach removed native/later handler or retained notification");
            using (var rebound = new HardwareBinding(core, modules, () => true, id == "evaporator", 300,
                () => false, () => notices++))
            {
                modules.onSlotAddItemEndFunc(module, modules, null);
                Check(effects == 3 && notices == 2 && later == 2, "rebind accumulated stale handlers");
            }
        }
    }
    private static void CheckNativeModuleEligibility()
    {
        foreach (string id in new[] { "blender", "heating_plate", "chem_finisher", "evaporator" })
        {
            var core = Mix(); core.identifier = id; core.EnableTag("IS_OWNED_TAG");
            var modules = Unbuilt<GameGridInventory>();
            typeof(TagSystem).GetProperty("dict").SetValue(modules, new Dictionary<string, TagState>());
            Field(modules, "items", new List<GameItem>());
            Field(modules, "<inventoryShape>k__BackingField", new GridShapeBuilder(4, 4).SetDataFill(0).SetDataOutside(1).Build());
            if (id == "evaporator")
                ContainerHelper.AllowOnlyTaggedItemsOr(modules, new[] { "MODULE_TYPE_UNIVERSAL", "MODULE_TYPE_EVAPORATOR" }, ownedOnly: true);
            else MachineBlender.SetupChemModuleGrid(modules, core);
            var vessel = Inventory();
            AttachNativeWindow(core, modules, vessel);
            bool running = false, searching = false;
            using (var binding = new HardwareBinding(core, modules, () => running || searching, id == "evaporator", 300, () => searching))
            {
                foreach (string type in new[] { "MODULE_TYPE_CHEM", "MODULE_TYPE_UNIVERSAL", "MODULE_TYPE_EVAPORATOR" })
                {
                    var module = Mix(); module.EnableTag("IS_OWNED_TAG"); module.EnableTag("MODULE_TAG"); module.EnableTag(type);
                    module.shape = module.modifiedShape = new GridShapeBuilder(2, 2).SetDataFill(1).Build();
                    bool compatible = id == "evaporator" ? type != "MODULE_TYPE_CHEM" : type == "MODULE_TYPE_CHEM";
                    foreach (bool active in new[] { false, true })
                    {
                        running = active;
                        Check(modules.MayHaveValidInventorySlot(module) == compatible, id + "/" + type + " running=" + active);
                    }
                    searching = true;
                    Check(!modules.MayHaveValidInventorySlot(module), "native entry bypassed preview snapshot lock");
                    searching = false;
                }
            }
        }
    }
    private static RunningBatch Batch(int upgrades) => new RunningBatch {
        blenderLoss = 20, heaterUpgrades = upgrades, finisherLoss = 6,
        plan = new BatchPlan { productId = "dream_dust", requestedGrade = 99, actualGrade = 90,
            expectedBottles = 1, minimumActive = 1850, reactionSeconds = 20,
            actions = new[] { new BatchAction(Operation.Extract, amount: 20, targetTemperature: 45),
                new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 1) } }
    };
    private static void CheckFrozenHeating()
    {
        var native = typeof(MachineHeatingPlate).GetMethod("HeatLiquidOnStove", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (int saved in Enumerable.Range(0, 5))
        foreach (int installed in Enumerable.Range(saved, 5 - saved))
        foreach (int desired in new[] { 45, 52, 65, 90, 125, 160 })
        foreach (double start in new[] { 26.0, 40.0, 51.5, 98.0, 159.0 })
        {
            var batch = Batch(saved);
            var vessel = Mix("water", 200000);
            vessel.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(start);
            // Physical installation and a save/reload must not replace any of
            // the three batch snapshots. The following batch uses new values.
            for (int second = 0; second < 12; second++)
            {
                batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
                Check(batch.heaterUpgrades == saved && batch.blenderLoss == 20 && batch.finisherLoss == 6, "reload applied pending modules early");
                double before = ChemistryOperations.Temperature(vessel);
                int mode = ChemistryOperations.HeatingMode(vessel, batch.heaterUpgrades, desired);
                double target = saved == 0 ? 99 : saved == 1 ? 160 : desired;
                double factor = mode == 2 ? 30 : 10;
                if (saved == 3) factor *= 4; else if (saved == 4) factor *= 16;
                double expected = mode == 0 ? before : (double)native.Invoke(null, new object[] { before, target, 200000, factor });
                bool heated = ChemistryOperations.ApplyBatchHeating(vessel, batch.heaterUpgrades, desired);
                Check(heated == (mode != 0) && Math.Abs(ChemistryOperations.Temperature(vessel) - expected) < 1e-10,
                    "batch heat deviates from native pre-install settings " + saved + "->" + installed);
                Check(ChemistryOperations.Amount(vessel, "water") == 200000, "heating helper reacted or evaporated liquid twice");
                // Native ambient cooling continues between heating pulses.
                ChemicalHelper.HandleAmbientTempDrop(vessel);
            }
        }
        var old = Mix("water", 200000); var next = new SimulationItem(old);
        ChemistryOperations.ApplyBatchHeating(old, 2, 65);
        ChemistryOperations.ApplyBatchHeating(next, 4, 65);
        Check(ChemistryOperations.Temperature(next) > ChemistryOperations.Temperature(old), "next batch did not gain the installed speed upgrade");
    }
    internal static string Run()
    {
        checks = 0;
        CheckCallbacks();
        CheckNativeModuleEligibility();
        CheckNativeSlotsAndHeating();
        CheckFrozenHeating();
        return checks + " live-module checks passed (native predicates/callbacks, running install, locked removal, search locks, next evaporation, saved stage parameters and native heating equivalence)";
    }
}
