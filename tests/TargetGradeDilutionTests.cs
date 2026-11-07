using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class TargetGradeDilutionTests
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Dilution: " + why); }
    private static SimulationItem Mix(params object[] parts)
    { var item = new SimulationItem(); for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]); return item; }
    private static BatchPlan Plan(BatchInputs input)
    { var planner = new BatchPlanner(input); var work = planner.Search(); int n = 0; while (work.MoveNext()) Check(++n < 100000, "search stalled"); Check(planner.Result != null, planner.Error); return planner.Result; }
    internal static string Run()
    {
        foreach (string id in ChemicalRecipe.Ids)
        foreach (int grade in new[] { 30, 45, 60, 80, 90, 99 })
        foreach (string reagent in new[] { "acid", "base" })
        {
            var recipe = ChemicalRecipe.Get(id);
            var input = new BatchInputs { productId = id, requestedGrade = grade, heaterUpgrades = 4, finisherLoss = 0,
                raw = new GameItem[0], chamber = Mix(), mashBuffer = Mix(),
                supplies = new GameItem[] { Mix(recipe.Active, 200000), Mix(reagent, 300000), Mix(reagent, 300000) }, skipDryingComparison = true };
            var before = input.supplies.Select(ChemistryOperations.Parts).Select(x => string.Join(",", x)).ToArray();
            var plan = Plan(input);
            Check(plan.minimumActive * 100L >= (long)recipe.UnitVolume * grade, "below requested purity: " + id);
            if (grade < 99)
            {
                int upper = GradePolicy.Grades.Where(g => g > grade).Min();
                Check(plan.dilutionVolume > 0 && plan.minimumActive * 100L < (long)recipe.UnitVolume * upper, "trade grade not matched: " + id + " " + grade);
            }
            else Check(plan.dilutionVolume == 0, "99% received needless filler");
            Check(BatchPlanner.Revalidate(input, plan) != null, "native replay rejected mixed plan: " + id);
            if (id == "dream_dust" && grade == 60 && reagent == "acid")
            {
                var partial = new QuantityPlanning(input, plan, 2); var search = partial.Search();
                while (search.MoveNext()) { }
                Check(partial.Result != null && partial.Result.expectedBottles == 2, "quantity replan failed");
                Check(partial.Result.dilutionVolume > 0 && partial.Result.reagents < plan.reagents / 10, "small order consumed full supplies");
            }
            Check(input.supplies.Select(ChemistryOperations.Parts).Select(x => string.Join(",", x)).SequenceEqual(before), "preview changed live supplies");
        }
        var missing = new BatchInputs { productId = "dream_dust", requestedGrade = 60, finisherLoss = 0,
            raw = new GameItem[0], chamber = Mix(), supplies = new GameItem[] { Mix("dream_cap_extract", 200000) }, skipDryingComparison = true };
        var unchanged = Plan(missing);
        Check(unchanged.dilutionUnavailable && unchanged.dilutionVolume == 0 && unchanged.minimumActive == 2000, "missing filler concealed or invented");
        return checks + " ready-stock grade checks passed; " + ControllerFlowTests.ReadyStockFlowAudit();
    }
}

internal sealed partial class ControllerFlowTests
{
    internal static string ReadyStockFlowAudit()
    {
        int cases = 0;
        foreach (string id in new[] { "dream_dust", "pink_alt_injector" })
        foreach (bool deferred in new[] { false, true })
        {
            var input = Inputs(id, false, DirectDryingMethod.Evaporate, 100);
            input.requestedGrade = 60;
            input.supplies = new GameItem[] { Mix(ChemicalRecipe.Get(id).Active, 40000), Mix("acid", 100000) };
            var planner = new BatchPlanner(input); var work = planner.Search(); while (work.MoveNext()) { }
            Check(planner.Result != null && planner.Result.dilutionVolume > 0, "ready stock did not use filler");
            var c = new ControllerFlowTests(input, planner.Result, deferred); c.RunSequence(2);
            Check(c.made.Count == planner.Result.expectedBottles, "dilution output count differs");
            foreach (var product in c.made)
                Check((long)ChemistryOperations.Amount(product, ChemicalRecipe.Get(id).Active) * 100 < (long)ChemicalRecipe.Get(id).UnitVolume * 80,
                    "a finished unit exceeded the requested trade grade");
            cases++;
        }
        return cases + " grade-adjusted controller flows passed";
    }
}
