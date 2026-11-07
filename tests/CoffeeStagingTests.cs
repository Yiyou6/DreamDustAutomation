using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class CoffeeStagingTests
{
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception("Coffee staging: " + text); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG" })
            item.state.GetTag(key).Enable().SetInt(400);
        return item;
    }
    private static void Drain(IEnumerator work)
    {
        int n = 0; while (work.MoveNext()) if (++n > 1000000) throw new Exception("Coffee planning did not finish");
    }
    private static BatchInputs Input(int percent, params GameItem[] supplies) => new BatchInputs {
        productId = "caffeine_pill", raw = new GameItem[0], supplies = supplies,
        chamber = Mix(), mashBuffer = Mix(), requestedGrade = 99, heaterUpgrades = 4,
        alcoholPercent = percent, finisherLoss = 0, maxProducts = 200
    };
    private static BatchPlan Plan(BatchInputs input)
    {
        var p = new BatchPlanner(input); Drain(p.Search());
        Check(p.Result != null, input.alcoholPercent + "%: " + p.Error); return p.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "start revalidation");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    internal static string Run()
    {
        checks = 0;
        var notes = new List<string>();
        var recipe = ChemicalRecipe.Get("caffeine_pill");
        // Compare the full-dose formula directly to native first-tick extraction,
        // including the banker's rounding edges in the game's 15% reaction rate.
        foreach (int amount in Enumerable.Range(1, 200).Concat(new[] { 7999, 10001, 977777 }))
        {
            long dose = AlcoholBudget.FullDose(recipe, amount);
            var tank = Mix("coffee_mush", amount, "alcohol", (int)dose);
            tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(65);
            ChemicalHelper.HandleCoffeeBeanExtractionSolvent(tank);
            int processed = amount - ChemistryOperations.Amount(tank, "coffee_mush");
            Check(dose == 5L * processed, "coffee dose must use the native processed portion");
            Check(ChemistryOperations.Amount(tank, "caffeine") == (int)Math.Round(processed * .75), "native maximum extraction yield");
            Check(ChemistryOperations.Amount(tank, "alcohol") == dose, "solvent should remain reusable");
        }
        foreach (int percent in AlcoholBudget.Percentages)
        {
            var feed = Mix("coffee_mush", 100000);
            var alcohol = Mix("alcohol", 75000);
            var input = Input(percent, feed, alcohol, Filter());
            var budget = AlcoholBudget.Read(input);
            Check(budget.Applicable && budget.Full == 75000, "coffee controls still use the mushroom ratio or are disabled");
            Check(budget.Needed(percent) == 750 * percent, "dose estimate");
            var plan = Plan(input);
            Check(plan.actualGrade == 99, "staged coffee must remain lab-pure with filtration");
            Check(plan.extractionAlcohol == 750 * percent, "planned alcohol differs from selected dose");
            Check(percent == 100 ? plan.extractionPortions == 1 : plan.extractionPortions > 1, "reduced dose did not physically split feed");
            Check(plan.actions.Where(a => a.operation == Operation.Extract).All(a => a.targetTemperature == 65 && a.precursorId == "coffee_mush"), "mushroom temperature or water extraction leaked into coffee solvent mode");
            Check(plan.actions.Count(a => a.operation == Operation.Dry) == 1, "alcohol evaporated between portions");
            Check(ChemistryOperations.Amount(feed, "coffee_mush") == 100000 && ChemistryOperations.Amount(alcohol, "alcohol") == 75000, "planning consumed inventory");
            Replay(input, plan);
            var remainder = BatchPersistenceTests.RemainingAfter(input, plan);
            Check(ChemistryOperations.Amount(remainder.supplies[1], "alcohol") == 75000 - 750 * percent, "runtime alcohol consumption differs from preview");
            var quantity = new QuantityPlanning(input, plan, Math.Max(1, plan.expectedBottles / 2));
            Drain(quantity.Search());
            Check(quantity.Result != null, "quantity selection: " + percent + "% " + quantity.Error);
            Check(quantity.Result.expectedBottles == plan.expectedBottles / 2, "quantity mismatch");
            Check(quantity.Result.reagents < plan.reagents, "quantity did not meter inputs");
            Replay(input, quantity.Result);
            notes.Add(percent + "%: " + plan.expectedBottles + " units / " + plan.extractionPortions + " portions / " + plan.extractionAlcohol + " uL / " + plan.reactionSeconds + " seconds");
        }
        foreach (int upgrades in new[] { 0, 1 })
        foreach (int percent in new[] { 75, 25, 1 })
        {
            var input = Input(percent, Mix("coffee_mush", 100001, "water", 779), Mix("alcohol", 100000), Filter());
            input.heaterUpgrades = upgrades;
            Replay(input, Plan(input));
        }
        foreach (int percent in new[] { 25, 10, 1 })
        {
            int budget = 750 * percent;
            var mixed = Input(percent, Mix("coffee_mush", 100000, "alcohol", budget - 100), Mix("alcohol", 100), Filter());
            var plan = Plan(mixed);
            Check(plan.extractionAlcohol == budget, "carried alcohol was duplicated or omitted");
            Replay(mixed, plan);
        }
        // An actual raw bean with native fat/protein needs the same purification
        // reagents in staged mode; no free yield or skipped filter wear.
        var coffee = Mix("water", 3200, "fat", 8000, "coffee_mush", 25840, "caffeine", 4400,
            "physical_contaminant", 8000, "protein", 8000);
        coffee.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); coffee.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        var raw = Input(25, Mix("alcohol", 100000), Mix("acid", 98000, "water", 2000),
            Mix("base", 98000, "water", 2000), Mix("lipobond_agent", 100000), Filter(), Filter());
        raw.raw = new GameItem[] { coffee };
        var rawPlan = Plan(raw); Check(rawPlan.actualGrade == 99, "native bean purification"); Replay(raw, rawPlan);

        foreach (int percent in AlcoholBudget.Percentages)
        {
            int required = 750 * percent;
            var input = Input(percent, Mix("coffee_mush", 100000), Mix("alcohol", required - 1), Filter());
            Check(!AlcoholBudget.Read(input).AllowsPlanning(percent), "one uL short must disable a solvent option");
            var p = new BatchPlanner(input); Drain(p.Search());
            Check(p.Result == null && p.Error == "alcohol_unavailable", "missing alcohol was misreported");
            ChemistryOperations.SetAmount(input.supplies[1], "alcohol", required);
            Check(AlcoholBudget.Read(input).Allows(percent), "exact stock rejected");
        }
        var water = Input(100, Mix("coffee_mush", 100000), Mix("water", 25000), Filter());
        Check(AlcoholBudget.Read(water).WaterFallback(100), "water fallback not exposed");
        var waterPlan = Plan(water);
        Check(waterPlan.extractionAlcohol == 0 && waterPlan.actions.Any(a => a.operation == Operation.Extract && a.targetTemperature == 90), "legacy water extraction lost");
        Replay(water, waterPlan);
        water.alcoholPercent = 25;
        Check(!AlcoholBudget.Read(water).AllowsPlanning(25), "reduced solvent option silently switched to water");
        var prepared = Input(1, Mix("caffeine", 20000));
        var ready = Plan(prepared);
        Check(!AlcoholBudget.Read(prepared).Applicable && ready.extractionPortions == 0, "prepared caffeine requested extraction");
        Replay(prepared, ready);
        return checks + " coffee staging checks passed\n" + string.Join("\n", notes);
    }
}
