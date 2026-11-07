using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ReactionEndpointTests
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    internal static string Run()
    {
        foreach (int modules in new[] { 1, 2, 4 })
        foreach (int elapsedPercent in new[] { 25, 75, 99 })
        {
            var tank = Mix("water", 2000000, "debris", 1052000);
            int seconds = HeatedDrying.HeatUntilDry(new SimulationItem(tank), modules);
            Check(seconds > 0, "fixture cannot boil");
            var batch = new RunningBatch { heaterUpgrades = modules, plan = new BatchPlan {
                productId = "dream_dust", directForm = true, expectedBottles = 526,
                actions = new[] { new BatchAction(Operation.BoilDry, amount: seconds, targetTemperature: 160), new BatchAction(Operation.Form, amount: 526) }
            } };
            batch.elapsed = seconds * elapsedPercent / 100;
            for (int n = 0; n < batch.elapsed; n++) ChemistryOperations.SimulateHeatedSecond(tank, modules, 160);
            // A native overnight/cooling boundary changes the actual temperature,
            // without changing the saved stopwatch. Timers are estimates only.
            tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
            NightReactions.CompleteStep(batch, tank, null);
            Check(!WaterHelper.IsWet(tank), "night advanced to forming while still wet: modules=" + modules + " elapsed=" + elapsedPercent);
            var storage = Mix(); FinishingStorage.Stage(tank, storage, 526, 2000, true);
            Check(ChemistryOperations.Total(storage) == 1052000, "drying changed fake-drug solids");
            var wet = Mix("water", 100000, "debris", 1052000);
            batch.failed = true; batch.failureKey = "runtime_still_wet";
            Check(WetBatchRecovery.TryResume(batch, wet, null), "legacy wet save could not resume drying");
            Check(!batch.failed && batch.plan.actions[batch.step].operation == Operation.BoilDry, "recovery replayed the wrong step");
            NightReactions.CompleteStep(batch, wet, null);
            Check(!WaterHelper.IsWet(wet) && ChemistryOperations.Amount(wet, "debris") == 1052000, "recovery lost or invented material");
            Check(!WetBatchRecovery.TryResume(batch, wet, null), "recovery was repeated");
        }
        foreach (int volume in new[] { 1000, 100000, 500000 })
        {
            var tank = Mix("dream_cap_extract", volume, "reducing_agent", volume, "lipobond_agent", volume / 4);
            var action = new BatchAction(Operation.Synthesize, amount: 1, targetTemperature: 52) { precursorId = "dream_cap_extract" };
            var batch = new RunningBatch { heaterUpgrades = 4, plan = new BatchPlan { productId = "pink_alt_injector", actions = new[] { action } } };
            NightReactions.CompleteStep(batch, tank, null);
            Check(ChemistryOperations.Amount(tank, "dream_cap_extract") == 0, "night skipped unfinished injector reaction");
            Check(ChemistryOperations.Amount(tank, "nomore") > 0, "injector reaction never started");
        }
        return checks + " reaction endpoint checks passed; " + ControllerFlowTests.HeatedNightFlowAudit();
    }
}

internal sealed partial class ControllerFlowTests
{
    internal static string HeatedNightFlowAudit()
    {
        int cases = 0;
        foreach (int modules in new[] { 1, 2, 4 })
        foreach (bool deferred in new[] { false, true })
        foreach (bool reload in new[] { false, true })
        {
            var input = Inputs("dream_dust", true, DirectDryingMethod.Heat, 100);
            input.heaterUpgrades = modules;
            input.supplies = new GameItem[] { Mix("water", 2000000, "starch", 1052000) };
            var planner = new BatchPlanner(input); var search = planner.Search(); while (search.MoveNext()) { }
            Check(planner.Result?.expectedBottles == 526, "526-unit fixture changed");
            var c = new ControllerFlowTests(input, planner.Result, deferred);
            c.Guard(c.AdvanceImmediate);
            for (int second = 0; second < 50; second++) { c.Guard(c.Tick); c.Frame(); }
            Check(c.running != null && !c.running.failed, "daytime drying failed");
            c.tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
            bool legacyFailure = modules == 4 && deferred && reload;
            if (legacyFailure)
            {
                Check(c.running.plan.actions[c.running.step].operation == Operation.BoilDry, "legacy fixture left heating");
                c.running.step++; c.running.elapsed = 0;
                c.running.failed = true; c.running.failureKey = "runtime_still_wet";
            }
            if (reload) c.Reload();
            if (legacyFailure)
            {
                Check(WetBatchRecovery.TryResume(c.running, c.tank, c.mashBuffer), "persisted old wet batch did not recover");
                c.Reload(); // The inserted continuation must itself be save-compatible.
                Check(c.running != null && !c.running.failed, "recovered batch could not reload");
            }
            CurrentConverterHelper.isNightCycle = true;
            try { c.Guard(c.Cycle); } finally { CurrentConverterHelper.isNightCycle = false; }
            Check(c.running == null || !c.running.failed, "overnight drying failed: " + c.status);
            c.RunSequence(2);
            Check(c.made.Count == 526, "overnight changed output count");
            cases++;
        }
        return cases + " heated-night controller flows passed";
    }
}
