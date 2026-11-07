using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

public static class QuantityPlanningTests
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Quantity: " + message); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i+1]);
        return item;
    }
    private static void Run(IEnumerator work) { int n = 0; while (work.MoveNext()) if (++n > 1000000) throw new Exception("Quantity search did not finish"); }
    private static SimulationItem Filter()
    {
        var filter = Mix(); filter.identifier = "water_filter"; filter.EnableTag("WATER_FILTER");
        filter.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG" })
            filter.state.GetTag(key).Enable().SetInt(400);
        return filter;
    }
    private static BatchPlan Full(BatchInputs input) { var p = new BatchPlanner(input); Run(p.Search()); Check(p.Result != null, "full plan: " + p.Error); return p.Result; }
    private static BatchPlan Limited(BatchInputs input, BatchPlan full, int count)
    {
        var p = new QuantityPlanning(input, full, count); Run(p.Search());
        Check(p.Result != null, input.productId + " x" + count + ": " + p.Error);
        Check(p.Result.expectedBottles == count && p.Result.actualGrade >= full.actualGrade, "wrong count/grade");
        Check(p.Result.outputCapacity == input.maxProducts && !p.Result.outputLimited, "quantity mistaken for warehouse limit");
        Check(BatchPlanner.Revalidate(input, p.Result) != null, "replay cannot validate");
        checks += BatchPersistenceTests.CheckRecipe(input, p.Result);
        return p.Result;
    }
    internal static string CheckAll()
    {
        checks = 0;
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var water = Mix("water", 150000, "physical_contaminant", recipe.UnitVolume * 100); water.identifier = "water_jug";
            var input = new BatchInputs { productId = id, directForm = true, raw = new GameItem[0], supplies = new GameItem[] { water },
                chamber = Mix(), mashBuffer = Mix(), finisherLoss = 12, maxProducts = 200 };
            var full = Full(input);
            foreach (int count in new[] { 1, 25, 50, 99 })
            {
                var plan = Limited(input, full, count);
                Check(plan.reagents < full.reagents, "impure-water portion was not reduced");
                Check(plan.actions.Where(a => a.operation == Operation.Pour).Sum(a => a.amount) == plan.reagents, "liquid accounting");
                Check(ChemistryOperations.Total(water) == 150000 + recipe.UnitVolume * 100, "preview consumed water");
            }
            input.directForm = false; input.finisherLoss = 0; input.requestedGrade = 99;
            input.supplies = new GameItem[] { Mix(recipe.Active, recipe.UnitVolume * 100) };
            full = Full(input);
            var small = Limited(input, full, 13);
            Check(small.reagents < full.reagents && small.minimumActive >= recipe.UnitVolume * .99, "active stock not metered");
            input.supplies = new GameItem[0]; input.chamber = Mix(recipe.Active, recipe.UnitVolume * 100);
            full = Full(input); small = Limited(input, full, 13);
            Check(small.actions.Any(a => a.operation == Operation.StoreMash), "reactor surplus not preserved");
        }
        foreach (int percent in new[] { 100, 25, 10, 1 })
        {
            var mush = Mix("dream_cap_mush", 20000); mush.identifier = "dream_cap";
            mush.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); mush.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var input = new BatchInputs { productId = "dream_dust", requestedGrade = 99, alcoholPercent = percent,
                raw = new GameItem[] { mush, new SimulationItem(mush), new SimulationItem(mush), new SimulationItem(mush) },
                supplies = new GameItem[] { Mix("alcohol", 1000000) }, chamber = Mix(), mashBuffer = Mix(), maxProducts = 100,
                blenderLoss = 0, finisherLoss = 0, heaterUpgrades = 2 };
            var full = Full(input);
            if (percent == 1)
            {
                var tiny = new QuantityPlanning(input, full, 2); Run(tiny.Search());
                Check(tiny.Result == null && tiny.Error == "diag_quantity_minimum", "tiny solvent batch must explain the minimum: " + tiny.Error);
                Limited(input, full, 30);
                continue;
            }
            var limited = Limited(input, full, 2);
            Check(limited.actions.Count(a => a.operation == Operation.Blend) == 1, "unneeded whole mushrooms consumed at " + percent + "%: " +
                string.Join(";", limited.actions.Select(a => a.operation + ":" + a.source + ":" + a.amount)) + "; full=" + full.expectedBottles + "/" + full.actualGrade);
            Check(limited.reagents < full.reagents, "smaller batch still took full reagent dose");
            Check(limited.actions.Any(a => a.operation == Operation.StoreMash), "last mushroom surplus missing");
        }
        foreach (string id in new[] { "dream_dust", "nightmare_dust", "caffeine_pill", "fanta_pill", "pink_alt_injector" })
        {
            var recipe = ChemicalRecipe.Get(id);
            var feed = id == "fanta_pill" ? Mix("methylol", 125000, "stabilizing_agent", 125000) :
                Mix(recipe.Mush, 90000, "water", 10000, "protein", 3700, "physical_contaminant", 2000);
            if (id != "fanta_pill") { feed.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); feed.EnableTag(ChemicalHelper.BLENDABLE_TAG); }
            var stock = new List<GameItem> { Mix("alcohol", 700000, "water", 300000), Mix("acid", 250000),
                Mix("base", 250000), Mix("reducing_agent", 250000), Mix("lipobond_agent", 150000), Filter(), Filter() };
            if (id == "fanta_pill") stock.Insert(0, feed);
            var input = new BatchInputs { productId = id, requestedGrade = 90, raw = id == "fanta_pill" ? new GameItem[0] : new GameItem[] { feed },
                supplies = stock.ToArray(), chamber = Mix(), mashBuffer = Mix(), blenderLoss = 35, heaterUpgrades = 1, finisherLoss = 12, maxProducts = 200 };
            var full = Full(input);
            foreach (int count in new[] { 1, full.expectedBottles / 2, full.expectedBottles - 1 }.Distinct())
            {
                var plan = Limited(input, full, count);
                Check(plan.reagents < full.reagents, "multi-stage reagents not reduced: " + id + " x" + count);
                if (id == "pink_alt_injector") Check(plan.actions.Count(a => a.operation == Operation.Dry) == 2, "intermediate evaporation omitted");
            }
        }
        var fruit = Mix("water", 196375, "starch", 40000, "physical_contaminant", 13625);
        fruit.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); fruit.EnableTag(ChemicalHelper.BLENDABLE_TAG); fruit.identifier = "nutrifruit";
        var food = new BatchInputs { productId = "dream_dust", directForm = true, raw = new GameItem[] { fruit, new SimulationItem(fruit) },
            supplies = new GameItem[0], chamber = Mix(), mashBuffer = Mix(), blenderLoss = 35, finisherLoss = 12, maxProducts = 1280 };
        var foodPlan = Limited(food, Full(food), 1);
        Check(foodPlan.actions.Count(a => a.operation == Operation.Blend) == 1, "extra fruit blended");
        var large = new BatchInputs { productId = "dream_dust", directForm = true, raw = new GameItem[0],
            supplies = new GameItem[] { Mix("water", 3120000, "physical_contaminant", 2880000) },
            chamber = Mix(), mashBuffer = Mix(), maxProducts = 1280 };
        large.supplies[0].identifier = "water_tank";
        var maximum = Full(large);
        foreach (int n in new[] { 1, 257, 1000, 1279 }) Limited(large, maximum, n);
        var powder = Mix("starch", 100000); powder.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); powder.EnableTag("SOLUBLE_TAG");
        var direct = new BatchInputs { productId = "dream_dust", directForm = true, raw = new GameItem[] { powder },
            supplies = new GameItem[0], chamber = Mix(), mashBuffer = Mix(), maxProducts = 100 };
        var all = Full(direct); Limited(direct, all, 1);
        var mushroom = Mix("dream_cap_mush", 80000); mushroom.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); mushroom.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        var split = new BatchInputs { productId = "dream_dust", raw = new GameItem[] { mushroom },
            supplies = new GameItem[] { Mix("alcohol", 1000000), Mix("reducing_agent", 250000), Mix("lipobond_agent", 250000), Filter(), Filter() },
            chamber = Mix(), mashBuffer = Mix(), heaterUpgrades = 2, requestedGrade = 99, maxProducts = 200 };
        var first = Limited(split, Full(split), 5);
        var remaining = BatchPersistenceTests.RemainingAfter(split, first);
        Check(ChemistryOperations.Amount(remaining.mashBuffer, "dream_cap_mush") > 0 && remaining.raw.Length == 0, "unused final mushroom slurry not preserved");
        Check(ChemistryOperations.Amount(remaining.supplies[0], "alcohol") > 0, "unused reagent stock consumed");
        remaining.productId = "pink_alt_injector";
        var second = Limited(remaining, Full(remaining), 2);
        Check(second.actions.All(a => a.operation != Operation.Blend) && second.actions.Any(a => a.operation == Operation.FeedMash), "next product could not use leftover slurry");
        var rest = BatchPersistenceTests.RemainingAfter(remaining, second);
        Check(ChemistryOperations.Total(rest.mashBuffer) > 0, "second small batch consumed all remaining feed");
        var same = new QuantityPlanning(direct, all, all.expectedBottles); Run(same.Search());
        Check(same.Result.requestedBottles == 0 && same.Result.actions.Length == all.actions.Length, "default all changed");
        foreach (int invalid in new[] { 0, -1, all.expectedBottles + 1 })
        { var p = new QuantityPlanning(direct, all, invalid); Run(p.Search()); Check(p.Result == null, "invalid quantity accepted"); }
        return "Requested quantities: " + checks + " checks; all seven products, exact counts, metered liquids/reagents, whole-solid leftovers, 100/25/10/1% alcohol, native replay and save/resume.";
    }
}
