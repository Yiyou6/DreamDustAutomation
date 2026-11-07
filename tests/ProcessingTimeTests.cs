using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using DreamDustAutomation.Planning;

internal static class ProcessingTimeTests
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Processing time: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static BatchInputs Input(params GameItem[] supplies) => new BatchInputs {
        productId = "dream_dust", directForm = true, separateDirect = true, directSolvents = true,
        directDrying = DirectDryingMethod.Evaporate, chamber = Mix(), mashBuffer = Mix(),
        raw = new GameItem[0], supplies = supplies, heaterUpgrades = 1, maxProducts = 2560
    };
    private static BatchPlan Plan(BatchInputs input)
    {
        var planner = new BatchPlanner(input); var work = planner.Search();
        while (work.MoveNext()) { }
        Check(planner.Result != null, planner.Error); return planner.Result;
    }
    internal static string Run()
    {
        checks = 0;
        var input = Input(Mix("water", 427800, "starch", 20000), Mix("water", 616425, "starch", 20000),
            Mix("water", 141600, "starch", 20000), Mix("acid", 240000), Mix("base", 240000));
        var plan = Plan(input);
        Check(PlannedInputs.Read(input, plan).SetEquals(input.supplies), "full plan missed consumed sources");
        Check(plan.actions.Where(a => a.operation == Operation.Dry).Select(a => a.evaporationVolume)
            .SequenceEqual(new[] { 427800, 616425, 141600 }), "mixed source drying volumes");
        foreach (int rate in new[] { 0, 150000, 300000, 600000, 1200000 })
        {
            var estimate = EvaporationEstimate.Forecast(plan, rate);
            Check(estimate.Complete && estimate.Volume == 1185825 && estimate.Stages == 3, "batch source sum");
            int? expected = rate == 0 ? (int?)null : new[] { 427800, 616425, 141600 }.Sum(v => (v + rate - 1) / rate);
            Check(estimate.Days == expected, "sum of per-stage nights, not rounded total");
        }
        Check(EvaporationEstimate.Forecast(plan, 300000).Days == 6, "three loads require six separate overnight cycles");
        Check(EvaporationEstimate.Forecast(plan.Copy(), 300000).Days == 6, "copy lost estimated volumes");
        var validated = BatchPlanner.Revalidate(input, plan);
        Check(validated != null && EvaporationEstimate.Forecast(validated, 300000).Days == 6, "start validation lost estimate");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
        var q = new QuantityPlanning(input, plan, 2); var work = q.Search(); while (work.MoveNext()) { }
        Check(q.Result != null && q.Result.expectedBottles == 2, "metered plan");
        Check(PlannedInputs.Read(input, q.Result).SetEquals(new[] { input.supplies[0] }), "quantity selection highlighted unused bottles");
        Check(PlannedInputs.Read(input, null).Count == 0 && PlannedInputs.Read(null, plan).Count == 0, "invalidated plan retains inputs");
        Check(EvaporationEstimate.Forecast(q.Result, 300000).Days == 1, "quantity preview kept full-batch days");
        checks += BatchPersistenceTests.CheckRecipe(input, q.Result);
        int first = Array.FindIndex(plan.actions, a => a.operation == Operation.Dry);
        Check(EvaporationEstimate.Forecast(plan, 300000, first, Mix("water", 127800, "starch", 20000)).Days == 5,
            "current live tank did not replace original volume");
        Check(EvaporationEstimate.Forecast(plan, 300000, first + 1).Days == 4, "completed dry stage counted again");
        var old = new BatchPlan { actions = new[] { new BatchAction(Operation.Dry), new BatchAction(Operation.Form) } };
        Check(!EvaporationEstimate.Forecast(old, 300000).Complete && !EvaporationEstimate.Forecast(old, 300000).Days.HasValue,
            "old metadata missing reported as zero");
        Check(EvaporationEstimate.Forecast(old, 300000, 0, Mix("water", 300000)).Days == 1, "old active stage cannot use actual tank");
        Check(EvaporationEstimate.Forecast(Plan(Input(Mix("starch", 9000))), 0).Days == 0, "dry feed requires overnight wait");
        var envelope = BatchPersistence.Serialize(new RunningBatch { plan = plan, heaterUpgrades = 1 });
        Check(EvaporationEstimate.Forecast(BatchPersistence.Deserialize(envelope).plan, 300000).Days == 6, "saved estimate lost");
        var legacy = JObject.Parse(envelope);
        foreach (var action in legacy["plan"]["actions"].Children<JObject>()) action.Remove("evaporationVolume");
        Check(!EvaporationEstimate.Forecast(BatchPersistence.Deserialize(legacy.ToString()).plan, 300000).Complete,
            "old serialized batch fabricated drying estimate");
        var selected = Input(Mix("dream_cap_extract", 40000), Mix("caffeine", 40000), Mix("acid", 240000));
        selected.directForm = selected.separateDirect = false; selected.directDrying = DirectDryingMethod.LegacyAutomatic;
        selected.requestedGrade = 99;
        Check(PlannedInputs.Read(selected, Plan(selected)).SetEquals(new[] { selected.supplies[0] }), "normal plan marked unused medicine/reagents");
        var filter = Mix(); var solid = Mix("starch", 10000);
        var markers = Input(input.supplies[0], filter); markers.raw = new GameItem[] { solid };
        var markerPlan = new BatchPlan { actions = new[] {
            new BatchAction(Operation.Dissolve, 0), new BatchAction(Operation.Pour, 0, 1000),
            new BatchAction(Operation.Pour, 0, 1000), new BatchAction(Operation.Filter, 1, 2000),
            new BatchAction(Operation.ReturnFeed, 0, 50), new BatchAction(Operation.Dry) } };
        Check(PlannedInputs.Read(markers, markerPlan).SetEquals(new GameItem[] { solid, input.supplies[0], filter }), "solid, partial source or filter highlight wrong");
        markerPlan.actions = new[] { new BatchAction(Operation.Filter, 1, 0), new BatchAction(Operation.ReturnFeed, 0, 50) };
        Check(PlannedInputs.Read(markers, markerPlan).Count == 0, "unused filter or returned liquid marked consumed");

        // Independent native stove method, with the manual high setting.
        var stove = typeof(MachineHeatingPlate).GetMethod("HeatLiquidOnStove", BindingFlags.NonPublic | BindingFlags.Static);
        input.directDrying = DirectDryingMethod.Heat;
        foreach (int modules in new[] { 1, 2, 3, 4 })
        {
            input.heaterUpgrades = modules; var heated = Plan(input);
            var durations = heated.actions.Where(a => a.operation == Operation.BoilDry).Select(a => a.amount).ToArray();
            Check(durations.Length == 3, "dry acid/base got a heat step");
            int total = 0;
            for (int n = 0; n < 3; n++)
            {
                var vessel = new SimulationItem(input.supplies[n]); int seconds = 0;
                double factor = modules == 4 ? 480 : modules == 3 ? 120 : 30;
                while (ChemistryOperations.VolatileVolume(vessel) > 0 && seconds < 900)
                {
                    double temp = (double)stove.Invoke(null, new object[] {
                        ChemistryOperations.Temperature(vessel), 160.0, ChemistryOperations.Total(vessel), factor });
                    vessel.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(temp);
                    ChemistryOperations.ReactionTick(vessel); seconds++;
                }
                Check(ChemistryOperations.VolatileVolume(vessel) == 0 && durations[n] == seconds, "planned seconds differ from native high heating");
                total += seconds;
            }
            Check(ReactionProgress.RemainingSeconds(heated, 0, 0) == total, "UI seconds differ from native lot sum");
            Check(EvaporationEstimate.Forecast(heated, 300000).Days == 0, "heat-only plan promises extra evaporation");
            checks += BatchPersistenceTests.CheckRecipe(input, heated);
        }
        return "Processing forecasts: " + checks + " checks passed (native heat timing, per-stage evaporation, quantities, preflight, performance, current tank and legacy saves)";
    }
}
