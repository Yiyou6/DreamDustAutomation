using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class CoffeeQualityTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Coffee quality: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter(bool coarse = false)
    {
        var item = Mix(); item.identifier = coarse ? "kotton_fabric" : "water_filter_adv";
        item.EnableTag("WATER_FILTER"); item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(coarse ? 150 : 22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(coarse ? 1000 : 400);
        return item;
    }
    private static BatchInputs Input(int beans, int agentPurity, string impurity, string vessel)
    {
        var agent = Mix("lipobond_agent", agentPurity * 1500, impurity, (100 - agentPurity) * 1500);
        agent.identifier = vessel;
        // Same actual contents in a large vessel or the original reagent bottle.
        agent.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(vessel == "water_tank" ? 10000000 : 150000);
        var stock = new GameItem[] { Mix("alcohol", 1000000), Mix("acid", 980000, "chemical_contaminant", 20000),
            Mix("base", 980000, "chemical_contaminant", 20000), agent, Filter(), Filter(), Filter(true), Filter(true) };
        var raw = Enumerable.Range(0, beans).Select(_ => {
            // Native 80 g coffee composition, including fat and protein.
            var item = Mix("water", 3200, "fat", 8000, "coffee_mush", 25840, "caffeine", 4400,
                "physical_contaminant", 8000, "protein", 8000);
            item.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); item.EnableTag(ChemicalHelper.BLENDABLE_TAG); return (GameItem)item;
        }).ToArray();
        int source;
        return new BatchInputs { productId = "caffeine_pill", raw = raw,
            supplies = RecoveredSolvent.Supplies(stock, null, null, out source), chamber = Mix(), mashBuffer = Mix(),
            requestedGrade = 99, heaterUpgrades = 4, finisherLoss = 0, alcoholPercent = 100, maxProducts = 2560 };
    }
    private static BatchPlan Plan(BatchInputs input)
    {
        var planner = new BatchPlanner(input); var work = planner.Search(); int count = 0;
        while (work.MoveNext()) Check(++count < 1000000, "planning stalled");
        Check(planner.Result != null, planner.Error);
        Check(BatchPlanner.Revalidate(input, planner.Result) != null, "preview disagrees with native replay");
        return planner.Result;
    }
    internal static string Run()
    {
        checks = 0; int cases = 0;
        foreach (int beans in new[] { 1, 2 })
        foreach (int purity in new[] { 100, 98, 92 })
        foreach (string impurity in new[] { "water", "chemical_contaminant", "heavy_metal" })
        {
            BatchPlan bottle = null;
            foreach (string vessel in new[] { "med_bottle_small_pink", "water_tank" })
            {
                var input = Input(beans, purity, impurity, vessel);
                Check(input.supplies.Length == 8, "reagent vessel omitted from live supply selection");
                var plan = Plan(input); cases++;
                bool limited = purity == 92 && impurity != "water";
                Check(plan.actualGrade == (limited ? 90 : 99), "native impurity limit: " + purity + "/" + impurity);
                Check(plan.concentrationHint == (limited ? "purity_cleaner" : null),
                    "spent only a small fraction of available filters but blamed filter quantity: " + purity + "/" + impurity);
                Check(plan.fineFilterWear < 100 && plan.coarseFilterWear < 200, "fixture exhausted its filters");
                Check(plan.actions.Any(a => a.operation == Operation.Pour && a.source == 3), "lipobond was ignored");
                if (bottle != null)
                    Check(plan.actualGrade == bottle.actualGrade && plan.expectedBottles == bottle.expectedBottles &&
                        plan.minimumActive == bottle.minimumActive && plan.reagents == bottle.reagents, "vessel shape changed chemistry");
                bottle = plan;
                if (limited)
                {
                    var extra = Input(beans, purity, impurity, vessel);
                    extra.supplies = extra.supplies.Concat(new[] { Filter(), Filter() }).ToArray();
                    var extraPlan = Plan(extra);
                    Check(extraPlan.actualGrade == 90 && extraPlan.concentrationHint == "purity_cleaner", "adding filters hides persistent dirt");
                    var cleaner = Input(beans, purity, impurity, vessel);
                    cleaner.supplies[1] = Mix("acid", 1000000); cleaner.supplies[2] = Mix("base", 1000000);
                    Check(Plan(cleaner).actualGrade == 99, "cleaner reagents failed to improve the attainable grade");
                }
            }
        }
        var representative = Input(2, 92, "chemical_contaminant", "water_tank");
        checks += BatchPersistenceTests.CheckRecipe(representative, Plan(representative));
        return checks + " coffee quality checks passed; " + cases + " vessel/composition cases; original game chemistry, two full fine filters and two cloth filters, extra-filter and cleaner-reagent controls.";
    }
}
