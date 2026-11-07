using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ForeignReagentTests
{
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception("Foreign reagent: " + reason); }
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
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(400);
        return item;
    }
    private static BatchPlanner Plan(BatchInputs input)
    {
        string before = PlanningInputState.Capture(input).Combined;
        var planner = new BatchPlanner(input); var work = planner.Search();
        while (work.MoveNext()) { }
        Check(before == PlanningInputState.Capture(input).Combined, "planning changed stock");
        return planner;
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string id in new[] { "dream_dust", "nightmare_dust", "caffeine_pill", "pink_alt_injector" })
        foreach (int percent in new[] { 0, 100, 25, 1 })
        {
            var recipe = ChemicalRecipe.Get(id);
            string other = id == "nightmare_dust" ? "dream_cap_extract" : "nightmare_extract";
            var foreign = Mix(other, 10000, "alcohol", 600000);
            var input = new BatchInputs { productId = id, requestedGrade = 30, alcoholPercent = percent,
                heaterUpgrades = 4, chamber = Mix(), mashBuffer = Mix(), raw = new GameItem[0],
                supplies = new GameItem[] { Mix(recipe.Mush, 100000), foreign, Filter(),
                    Mix("reducing_agent", 200000), Mix("lipobond_agent", 50000) } };
            var unavailable = Plan(input);
            Check(unavailable.Result == null && unavailable.Error == "diag_reagent_foreign",
                id + "/" + percent + "% consumed another medicine as solvent: " + unavailable.Error);
            var budget = AlcoholBudget.Read(input);
            Check(budget.Available == 0 && budget.ForeignAlcohol == 600000, "foreign alcohol counted as available");
            input.supplies = input.supplies.Concat(new[] { Mix("alcohol", 600000) }).ToArray();
            var plan = Plan(input).Result;
            Check(plan != null, "separate solvent cannot produce " + id);
            Check(!plan.actions.Any(a => a.operation == Operation.Pour && a.source == 1), "foreign bottle used with separate solvent");
            Check(BatchPlanner.Revalidate(input, plan) != null, "plan cannot replay");
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
            var left = BatchPersistenceTests.RemainingAfter(input, plan);
            Check(ChemistryOperations.Parts(left.supplies[1]).SequenceEqual(ChemistryOperations.Parts(foreign)), "foreign bottle changed at runtime");
        }
        // Existing mixed feed and physical residues still retain their actual
        // composition; protection does not delete or separate any ingredient.
        var dream = ChemicalRecipe.Get("dream_dust");
        Check(dream.CanUseReagent(Mix("dream_cap_extract", 20000, "nightmare_extract", 10)), "pre-mixed selected feed rejected");
        Check(ChemicalRecipe.Get("pink_alt_injector").CanUseReagent(Mix("dream_cap_extract", 20000)), "injector precursor rejected");
        Check(!dream.CanUseReagent(Mix("methylol", 1, "reducing_agent", 100000)), "unrelated precursor used as reducer");
        Check(!dream.CanUseReagent(Mix("nightmare_cap_mush", 1, "water", 100000)), "foreign mash used as water");
        return checks + " foreign-reagent checks passed; other medicines remain untouched, native replay and reload agree";
    }
}
