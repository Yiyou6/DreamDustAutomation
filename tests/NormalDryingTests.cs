using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class NormalDryingTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Normal drying: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static void Drain(IEnumerator work)
    { int n = 0; while (work.MoveNext()) Check(++n < 1000000, "planning did not finish"); }
    private static BatchInputs Input(string id, GameItem tank, int modules = 4, int grade = 99) => new BatchInputs {
        productId = id, chamber = tank, mashBuffer = Mix(), raw = new GameItem[0], supplies = new GameItem[0],
        heaterUpgrades = modules, finisherLoss = 0, requestedGrade = grade, maxProducts = 2560 };
    private static SimulationItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG" }) item.state.GetTag(key).Enable().SetInt(400);
        return item;
    }
    private static NormalDryingPlanner Compare(BatchInputs input)
    {
        var r = ChemicalRecipe.Get(input.productId);
        var measured = ChemistryOperations.Measure(input.chamber, input.finisherLoss, 0, 0, null, r, input.maxProducts);
        var plan = new BatchPlan { productId = r.Id, actualGrade = input.requestedGrade, requestedGrade = input.requestedGrade,
            expectedBottles = measured.Bottles, minimumActive = measured.MinimumBottleActiveVolume, outputCapacity = input.maxProducts,
            actions = new[] { new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: measured.Bottles) } };
        var before = ChemistryOperations.Parts(input.chamber);
        var comparison = new NormalDryingPlanner(input, plan); Drain(comparison.Search());
        Check(ChemistryOperations.Parts(input.chamber).SequenceEqual(before), "comparison consumed the tank");
        Check(comparison.Evaporation.expectedBottles == measured.Bottles && !comparison.Evaporation.actions.Any(a => a.operation == Operation.BoilDry), "default evaporation changed");
        return comparison;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        input.normalDrying = DirectDryingMethod.Heat;
        Check(BatchPlanner.Revalidate(input, plan) != null, "start replay failed for " + input.productId);
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
        var shell = new SimulationItem();
        var running = new RunningBatch { plan = plan, heaterUpgrades = input.heaterUpgrades, finisherLoss = input.finisherLoss };
        BatchPersistence.Save(shell, running);
        var loaded = BatchPersistence.Load(shell, out string error);
        Check(loaded != null && error == null && loaded.plan.normalDrying == DirectDryingMethod.Heat, "heat route lost on reload");
        var corrupt = plan.Copy(); corrupt.actions.First(a => a.operation == Operation.BoilDry).targetTemperature = 999;
        bool refused = false;
        try { BatchPersistence.Deserialize(BatchPersistence.Serialize(new RunningBatch { plan = corrupt })); }
        catch { refused = true; }
        Check(refused, "invalid saved target accepted");
    }
    internal static string Run()
    {
        checks = 0;
        var notes = new List<string>();
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var input = Input(id, Mix(recipe.Active, 20 * recipe.UnitVolume, "water", 100000));
            var comparison = Compare(input);
            bool sensitive = id == "dream_dust" || id == "nightmare_dust";
            Check((comparison.Heating == null) == sensitive, id + " wrong heat feasibility");
            if (sensitive) Check(comparison.Evaporation.normalHeatReason == "normal_heat_damage", "missing heat damage reason");
            else
            {
                Check(comparison.Heating.expectedBottles == 20 && comparison.Heating.minimumActive == recipe.UnitVolume, "clean medicine lost yield or purity");
                Replay(input, comparison.Heating);
                notes.Add(id + ": " + comparison.Heating.expectedBottles + " units; " + comparison.Heating.reactionSeconds + " s");
            }
        }
        var dream = Input("dream_dust", Mix("dream_cap_extract", 40000, "lipobond_agent", 10000));
        var mild = Compare(dream);
        Check(mild.Heating != null && mild.Heating.expectedBottles == 20, "safe lipobond-only dream batch disabled by drug name");
        Check(mild.Heating.actions.Single(a => a.operation == Operation.BoilDry).targetTemperature == 57, "dream heat protection missing");
        Replay(dream, mild.Heating);
        var smallPlate = Compare(Input("caffeine_pill", Mix("caffeine", 40000, "water", 100000), 0));
        Check(smallPlate.Heating == null && smallPlate.Evaporation.normalHeatReason == "normal_heat_timeout", "base plate pretends it can boil water");
        int coffeeUnit = ChemicalRecipe.Get("caffeine_pill").UnitVolume;
        var reduced = Input("caffeine_pill", Mix("caffeine", 20 * coffeeUnit, "organic_waste", 5 * coffeeUnit, "water", 100000), 4, 80);
        var reduction = Compare(reduced);
        Check(reduction.Heating != null && reduction.Heating.expectedBottles < reduction.Evaporation.expectedBottles, "burnt dirt is still counted as output");
        Check(reduction.Heating.evaporationBottles == reduction.Evaporation.expectedBottles && reduction.Heating.normalHeatBottles == reduction.Heating.expectedBottles, "yield comparison missing");
        Replay(reduced, reduction.Heating);
        notes.Add("Impurities: evaporation " + reduction.Evaporation.expectedBottles + ", heat " + reduction.Heating.expectedBottles);
        var reducedPlanner = new BatchPlanner(reduced); Drain(reducedPlanner.Search());
        Check(reducedPlanner.Result != null, "mixed feed heat planning");
        var reducedQuantity = new QuantityPlanning(reduced, reducedPlanner.Result, 10); Drain(reducedQuantity.Search());
        Check(reducedQuantity.Result != null && reducedQuantity.Result.expectedBottles == 10, "mixed feed partial heating: " + reducedQuantity.Error);
        Replay(reduced, reducedQuantity.Result);
        var dry = Compare(Input("caffeine_pill", Mix("caffeine", 40000)));
        Check(dry.Heating != null && dry.Heating.expectedBottles == dry.Evaporation.expectedBottles && dry.Evaporation.normalHeatReason == "normal_heat_not_needed", "already dry feed blocked");

        var fullInput = Input("caffeine_pill", Mix("caffeine", 20 * coffeeUnit, "alcohol", 50000));
        var defaultPlanner = new BatchPlanner(fullInput); Drain(defaultPlanner.Search());
        Check(defaultPlanner.Result != null && defaultPlanner.Result.normalDrying == DirectDryingMethod.Evaporate && defaultPlanner.Result.normalHeatBottles > 0, "default route or alternative missing");
        fullInput.normalDrying = DirectDryingMethod.Heat;
        var planner = new BatchPlanner(fullInput); Drain(planner.Search());
        Check(planner.Result != null && planner.Result.actions.Any(a => a.operation == Operation.BoilDry), "selected heat ignored");
        Replay(fullInput, planner.Result);
        Check(planner.Result.boiledAlcohol > 0, "destroyed alcohol not disclosed");
        var changedSettings = PlanningInputState.Capture(fullInput);
        fullInput.normalDrying = DirectDryingMethod.Evaporate;
        Check(changedSettings.Changes(PlanningInputState.Capture(fullInput)).Primary?.Key == "changed_settings", "drying choice missing from preview fingerprint");
        Check(BatchPlanner.Revalidate(fullInput, planner.Result) == null, "stale choice passed preflight");
        fullInput.normalDrying = DirectDryingMethod.Heat;
        var quantity = new QuantityPlanning(fullInput, planner.Result, 10); Drain(quantity.Search());
        Check(quantity.Result != null && quantity.Result.expectedBottles == 10 && quantity.Result.requestedBottles == 10, "partial heat batch: " + quantity.Error);
        Replay(fullInput, quantity.Result);
        var unsafeInput = Input("dream_dust", Mix("dream_cap_extract", 40000, "water", 100000));
        unsafeInput.normalDrying = DirectDryingMethod.Heat;
        var unsafePlanner = new BatchPlanner(unsafeInput); Drain(unsafePlanner.Search());
        Check(unsafePlanner.Result == null && unsafePlanner.Error == "normal_heat_damage", "selected heat silently evaporated unsafe medicine");
        foreach (int modules in new[] { 0, 1, 2, 3 })
        {
            var mildInput = Input("dream_dust", Mix("dream_cap_extract", 40000, "lipobond_agent", 10000), modules);
            var result = Compare(mildInput);
            Check(result.Heating != null, "safe low-temperature drying with " + modules + " modules was disabled");
            Replay(mildInput, result.Heating);
        }
        foreach (int modules in new[] { 0, 4 })
        foreach (int percent in new[] { 100, 25, 1 })
        {
            var coffee = Input("caffeine_pill", Mix(), modules);
            coffee.alcoholPercent = percent;
            coffee.normalDrying = DirectDryingMethod.Heat;
            coffee.supplies = new GameItem[] { Mix("coffee_mush", 100000), Mix("alcohol", 75000), Filter() };
            var p = new BatchPlanner(coffee); Drain(p.Search());
            Check(p.Result != null && p.Result.actualGrade == 99, "coffee from mush: " + modules + "/" + percent + ": " + p.Error);
            Check(p.Result.actions.Any(a => a.operation == Operation.BoilDry) && !p.Result.actions.Any(a => a.operation == Operation.Dry && a.evaporationVolume > 0), "coffee still waits for evaporation");
            Replay(coffee, p.Result);
            notes.Add("Coffee " + modules + " modules / " + percent + "% alcohol: " + p.Result.expectedBottles + " units; " + p.Result.reactionSeconds + " s");
        }
        var injector = Input("pink_alt_injector", Mix());
        injector.normalDrying = DirectDryingMethod.Heat;
        injector.supplies = new GameItem[] { Mix("dream_cap_extract", 120000, "alcohol", 100000),
            Mix("reducing_agent", 1000000), Mix("lipobond_agent", 1000000), Mix("acid", 1000000), Mix("base", 1000000), Filter(), Filter() };
        var injectionPlan = new BatchPlanner(injector); Drain(injectionPlan.Search());
        Check(injectionPlan.Result != null, "injector: " + injectionPlan.Error);
        Check(injectionPlan.Result.actions.Any(a => a.operation == Operation.Dry && a.evaporationVolume > 0), "heat-sensitive intermediate lost required evaporation");
        Check(injectionPlan.Result.actions.Any(a => a.operation == Operation.BoilDry), "safe final injector drying not selected");
        Replay(injector, injectionPlan.Result);
        notes.Add("Injector: required intermediate evaporation + safe final heat; " + injectionPlan.Result.expectedBottles + " units");
        return checks + " normal drying checks passed\n" + string.Join("\n", notes);
    }
}
