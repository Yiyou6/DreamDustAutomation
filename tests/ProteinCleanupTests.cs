using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ProteinCleanupTests
{
    private static int checks, cases, treated, preserved;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static GameItem Filter(string id, int durability)
    {
        var item = Mix(); item.identifier = id; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(id == "kotton_fabric" ? 150 : 22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" }) item.state.GetTag(tag).Enable().SetInt(durability);
        return item;
    }
    internal static string Run()
    {
        checks = cases = treated = preserved = 0;
        foreach (string id in ChemicalRecipe.Ids)
        foreach (string treatment in new[] { "acid", "base", "both" })
        foreach (int strength in new[] { 100, 98 })
        foreach (int filterProfile in new[] { 0, 1, 2, 3, 4 })
        foreach (int grade in new[] { 99, 60 })
        {
            cases++; string key = id + "/" + treatment + "/strength" + strength + "/filters" + filterProfile + "/grade" + grade;
            var recipe = ChemicalRecipe.Get(id); var supplies = new List<GameItem>();
            foreach (string part in treatment == "both" ? new[] { "acid", "base" } : new[] { treatment })
                supplies.Add(Mix(part, strength * 1000, "water", (100 - strength) * 1000));
            int solvents = supplies.Count;
            if (filterProfile == 1 || filterProfile == 3) supplies.Add(Filter("kotton_fabric", 1000));
            if (filterProfile >= 2) {
                supplies.Add(Filter("water_filter", filterProfile >= 3 ? 0 : 400));
                supplies.Add(Filter("water_filter_adv", filterProfile >= 3 ? 0 : 800));
            }
            var input = new BatchInputs { productId = id, requestedGrade = grade, heaterUpgrades = 4, finisherLoss = 0,
                chamber = Mix(recipe.Active, 70000, "protein", 30000), mashBuffer = Mix(), raw = new GameItem[0], supplies = supplies.ToArray() };
            var planner = new BatchPlanner(input); var work = planner.Search(); int steps = 0;
            while (work.MoveNext()) Check(++steps < 1000000, key + ": planning stalled");
            var plan = planner.Result; Check(plan != null, key + ": no plan: " + planner.Error);
            Check(BatchPlanner.Revalidate(input, plan) != null, key + ": preflight failed");
            bool used = plan.actions.Any(a => a.operation == Operation.Pour && a.source < solvents);
            if (used) treated++; else preserved++;
            if (grade == 99 && filterProfile >= 1 && filterProfile <= 3) {
                Check(plan.actualGrade == 99, key + ": cleanup failed to reach 99%, got " + plan.actualGrade);
                Check(used, key + ": acid/base was skipped despite protein and usable filtration");
            }
            if (grade == 60) {
                Check(plan.actualGrade == 60 && plan.expectedBottles == 100000 / recipe.UnitVolume, key + ": qualified low-grade yield lost");
                Check(!used, key + ": unnecessary cleanup consumed reagent or filler");
            }
            var remaining = BatchPersistenceTests.RemainingAfter(input, plan);
            for (int i = 0; i < solvents; i++) {
                Check(ChemistryOperations.Amount(remaining.supplies[i], "protein") == 0, key + ": protein leaked into reagent bottle");
                Check(ChemistryOperations.Amount(remaining.supplies[i], "debris") == 0, key + ": sediment leaked into reagent bottle");
            }
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
        }
        return cases + " protein cleanup cases passed across seven products, acid/base/both, pure/98%, no/cloth/fine/spent filters, 99/60% grades; " + treated + " treatment plans, " + preserved + " reagent-preserving plans; " + checks + " checks. Tested against the explicitly selected game assembly.";
    }
}
