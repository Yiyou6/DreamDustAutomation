using System;
using System.Collections;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class InjectorRegressionTests
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Injector: " + message); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter_adv"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(key).Enable().SetInt(400);
        return item;
    }
    private static BatchInputs Input(int trace = 0, bool ready = false, int modules = 4, int fat = 0)
    {
        return new BatchInputs { productId = "pink_alt_injector", requestedGrade = 99, alcoholPercent = 100,
            heaterUpgrades = modules, raw = new GameItem[0], chamber = Mix(), mashBuffer = Mix(), maxProducts = 200,
            supplies = new GameItem[] { Mix("dream_cap_extract", 120000), Mix("nomore", ready ? 4000 : 0),
                Mix("reducing_agent", 1000000, "lipobond_agent", trace, "fat", fat), Mix("lipobond_agent", 1000000),
                Mix("acid", 1000000), Mix("base", 1000000), Filter(), Filter() } };
    }
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 1000000, "search did not finish"); }
    private static BatchPlan Calculate(BatchInputs input)
    {
        var planner = new BatchPlanner(input); Drain(planner.Search());
        Check(planner.Result != null, "no plan: " + planner.Error); return planner.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "preview cannot be replayed");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    private static int Drawn(BatchPlan plan, int source) => plan.actions.Where(a => a.operation == Operation.Pour && a.source == source).Sum(a => a.amount);

    internal static string Run()
    {
        checks = 0;
        foreach (int modules in new[] { 0, 1, 4 })
        foreach (int trace in new[] { 0, 100, 1000, 5000 })
        foreach (bool ready in new[] { false, true })
        {
            var input = Input(trace, ready, modules);
            var original = input.supplies.Select(ChemistryOperations.Parts).ToArray();
            var plan = Calculate(input);
            Check(plan.actualGrade == 99 && plan.expectedBottles == (ready ? 19 : 18),
                "trace=" + trace + " modules=" + modules + " ready=" + ready + " produced " + plan.actualGrade + "% x" + plan.expectedBottles);
            Check(plan.actions.Last(a => a.operation == Operation.Synthesize).endAmount == 0, "unconverted extract accepted as completed");
            Check(plan.reactionSeconds < 120, "trace catalyst still causes a long reaction");
            int imported = (int)((long)Drawn(plan, 2) * trace / (1000000 + trace));
            Check(Drawn(plan, 3) + imported >= 29999 && Drawn(plan, 3) + imported <= 30001,
                "catalyst must fill the quarter-extract deficit, counting mixed reagent contents");
            for (int i = 0; i < original.Length; i++) Check(original[i].SequenceEqual(ChemistryOperations.Parts(input.supplies[i])), "preview mutated supply");
            Replay(input, plan);
        }

        // The guide's half-volume dose and the native full-speed quarter-volume
        // dose must give identical reaction progress and products, not bonus yield.
        var quarter = Mix("dream_cap_extract", 120000, "reducing_agent", 120000, "lipobond_agent", 30000);
        var half = Mix("dream_cap_extract", 120000, "reducing_agent", 120000, "lipobond_agent", 60000);
        int ticks = 0;
        while (ChemistryOperations.Amount(quarter, "dream_cap_extract") > 0 && ticks++ < 120)
        {
            ChemistryOperations.SimulateHeatedSecond(quarter, 4, 52);
            ChemistryOperations.SimulateHeatedSecond(half, 4, 52);
            foreach (string id in new[] { "dream_cap_extract", "reducing_agent", "nomore", "debris" })
                Check(ChemistryOperations.Amount(quarter, id) == ChemistryOperations.Amount(half, id), "quarter/half native reaction differs: " + id);
        }
        Check(ChemistryOperations.Amount(quarter, "dream_cap_extract") == 0, "native reference did not finish");

        var fatty = Input(100, true, fat: 80000);
        var fattyPlan = Calculate(fatty);
        Check(fattyPlan.actualGrade == 99 && fattyPlan.expectedBottles >= 19, "fat consumed the synthesis catalyst");
        Check(Drawn(fattyPlan, 3) > Drawn(Calculate(Input(100, true)), 3), "fat sedimentation allowance missing");
        Replay(fatty, fattyPlan);

        var dirty = Input();
        ChemistryOperations.SetAmount(dirty.supplies[0], "protein", 20000);
        ChemistryOperations.SetAmount(dirty.supplies[0], "fat", 10000);
        var dirtyPlan = Calculate(dirty);
        Check(Drawn(dirtyPlan, 4) + Drawn(dirtyPlan, 5) > 0, "acid/base regression did not exercise a reagent withdrawal");
        Check(!dirtyPlan.actions.Any(a => a.operation == Operation.ReturnFeed), "normal production returned reacted feed to stock");
        var left = BatchPersistenceTests.RemainingAfter(dirty, dirtyPlan);
        foreach (int index in new[] { 4, 5 })
        {
            string part = index == 4 ? "acid" : "base";
            int remaining = 1000000 - Drawn(dirtyPlan, index);
            Check(ChemistryOperations.Amount(left.supplies[index], part) == remaining, "pure reagent quantity changed unexpectedly");
            Check(ChemistryOperations.Total(left.supplies[index]) == remaining, "pure acid/base gained other components");
        }
        Replay(dirty, dirtyPlan);

        var excess = Input(300000, true);
        var excessPlan = Calculate(excess);
        Check(excessPlan.actualGrade == 99 && excessPlan.expectedBottles == 19, "mixed excess catalyst failed");
        Check(Drawn(excessPlan, 3) == 0, "already sufficient catalyst was overdosed");
        Replay(excess, excessPlan);

        var metered = Input(100, false);
        var full = Calculate(metered);
        foreach (int count in new[] { 1, 9, 17 })
        {
            var quantity = new QuantityPlanning(metered, full, count); Drain(quantity.Search());
            Check(quantity.Result != null, "partial-batch planning failed: " + quantity.Error);
            Check(quantity.Result.expectedBottles == count && quantity.Result.actualGrade == 99, "partial-batch count/grade changed");
            Check(quantity.Result.reagents < full.reagents, "partial batch took full stock");
            Replay(metered, quantity.Result);
        }

        var slow = Input(100); slow.requestedGrade = 30; slow.supplies[3] = Mix();
        var failed = new BatchPlanner(slow); Drain(failed.Search());
        Check(failed.Result == null && failed.Error == "synthesis_timeout", "safety cutoff silently accepted partial synthesis: " + failed.Error);
        var scarce = Input(5000); scarce.supplies[3] = Mix();
        var scarcePlan = Calculate(scarce);
        Check(scarcePlan.actualGrade == 99 && scarcePlan.expectedBottles == 18 && scarcePlan.reactionSeconds > 120,
            "a slow but complete native reaction should remain possible");
        Replay(scarce, scarcePlan);
        return checks + " injector checks passed: mixed-catalyst top-up, native ratios, fat, excess, quantity and save/resume";
    }
}
