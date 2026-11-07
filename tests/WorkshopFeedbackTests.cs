using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class WorkshopFeedbackTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Workshop feedback: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter(string id, int durability)
    {
        var item = Mix(); item.identifier = id; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(id == "kotton_fabric" ? 150 : 22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(tag.StartsWith("DURABILITY_") ? durability : id == "kotton_fabric" ? 1000 : 400);
        return item;
    }
    private static BatchPlan Plan(BatchInputs input)
    {
        var planner = new BatchPlanner(input); var search = planner.Search(); int n = 0;
        while (search.MoveNext()) Check(++n < 1000000, "planning stalled");
        Check(planner.Result != null, "planning failed: " + planner.Error);
        Check(BatchPlanner.Revalidate(input, planner.Result) != null, "preflight failed");
        checks += BatchPersistenceTests.CheckRecipe(input, planner.Result);
        return planner.Result;
    }
    private static void CheckCottonNeutralization()
    {
        foreach (string neutralizer in new[] { "acid", "base" })
        foreach (bool spentFine in new[] { false, true })
        {
            var supplies = new GameItem[] { Mix(neutralizer, 98000, "water", 2000), Filter("kotton_fabric", 1000) };
            if (spentFine) supplies = supplies.Concat(new[] { Filter("water_filter", 0), Filter("water_filter_adv", 0) }).ToArray();
            var input = new BatchInputs { productId = "dream_dust", requestedGrade = 99, heaterUpgrades = 4, finisherLoss = 0,
                chamber = Mix("dream_cap_extract", 70000, "protein", 30000), mashBuffer = Mix(), raw = new GameItem[0], supplies = supplies };
            var plan = Plan(input);
            Check(plan.actualGrade == 99 && plan.expectedBottles >= 35, "cotton-only cleanup failed with " + neutralizer + "; spent=" + spentFine);
            Check(plan.actions.Any(a => a.operation == Operation.Pour && a.source == 0), "neutralizer skipped with usable cotton");
            Check(plan.coarseFilterWear > 0 && plan.fineFilterWear == 0, "wrong filter consumed");
            Check(!plan.actions.Where(a => a.operation == Operation.Filter).SelectMany(a => a.FilterSources).Any(i => i >= 2), "spent fine filter selected");
        }
    }
    private static void CheckUnrelatedPrecursor()
    {
        var input = new BatchInputs { productId = "pink_alt_injector", requestedGrade = 99, heaterUpgrades = 4, finisherLoss = 0,
            chamber = Mix(), mashBuffer = Mix(), raw = new GameItem[0], supplies = new GameItem[] {
                Mix("dream_cap_extract", 120000), Mix("reducing_agent", 1000000), Mix("lipobond_agent", 1000000),
                Filter("water_filter_adv", 400), Mix("methylol", 100000, "stabilizing_agent", 10000) } };
        var original = ChemistryOperations.Parts(input.supplies[4]);
        var plan = Plan(input);
        Check(plan.actualGrade == 99 && plan.expectedBottles == 18, "injector output changed with unrelated precursor");
        Check(ChemicalRecipe.Get(input.productId).Active == "nomore", "native injector ingredient changed");
        Check(!plan.actions.Any(a => a.operation == Operation.Pour && a.source == 4), "Fanta precursor consumed for injector");
        var rest = BatchPersistenceTests.RemainingAfter(input, plan);
        Check(original.SequenceEqual(ChemistryOperations.Parts(rest.supplies[4])), "unrelated precursor changed during replay");
    }
    private static void CheckExit()
    {
        Check(!DeferredOutput.CanFinish(null), "idle batch");
        foreach (bool held in new[] { false, true })
        foreach (bool failed in new[] { false, true })
        foreach (Operation phase in Enum.GetValues(typeof(Operation)))
        {
            var batch = new RunningBatch { deferOutput = held, outputCommitted = held, failed = failed,
                plan = new BatchPlan { actions = new[] { new BatchAction(phase) }, expectedBottles = 302 },
                prepared = 302, produced = 300, releaseTarget = 302 };
            Check(DeferredOutput.CanFinish(batch), "trapped batch: " + phase + "/held=" + held + "/failed=" + failed);
        }
    }
    private static void CheckWaste()
    {
        foreach (string id in new[] { "kotton_fabric", "water_filter", "water_filter_adv", "hydroreed_filter", "modded_filter" })
        foreach (int durability in new[] { 0, 1, 400 })
            Check(WasteCollection.Accepts(Filter(id, durability)) == (durability == 0 && (id == "kotton_fabric" || id == "water_filter" || id == "hydroreed_filter")),
                "waste policy: " + id + "/" + durability);
    }
    internal static string Run()
    {
        checks = 0;
        CheckCottonNeutralization(); CheckUnrelatedPrecursor(); CheckWaste(); CheckExit();
        return checks + " workshop feedback checks passed: spent fine filters + cotton + acid/base, unrelated Fanta stock, waste policy and all-stage exit.";
    }
}
