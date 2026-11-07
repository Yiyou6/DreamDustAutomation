using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class StockQuantityAuditTests
{
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception("Stock quantity: " + text); }
    private static SimulationItem Mix(int active, int dirt)
    {
        var item = new SimulationItem(); ChemistryOperations.SetAmount(item, "dream_cap_extract", active);
        ChemistryOperations.SetAmount(item, "physical_contaminant", dirt); return item;
    }
    private static BatchInputs Input(int grade, int loss = 0) => new BatchInputs {
        productId = "dream_dust", requestedGrade = grade, finisherLoss = loss, heaterUpgrades = 4,
        chamber = Mix(0, 0), mashBuffer = Mix(0, 0), raw = new GameItem[0], maxProducts = 2560,
        supplies = new GameItem[] { Mix(4000, 0), Mix(17600, 2400) } };
    private static BatchPlan Plan(BatchInputs input)
    {
        var planner = new BatchPlanner(input); var work = planner.Search(); int count = 0;
        while (work.MoveNext()) Check(++count < 100000, "planner stalled");
        Check(planner.Result != null, planner.Error);
        Check(planner.Result.requestedBottles == 0, "all-stock calculation kept a quantity limit");
        Check(BatchPlanner.Revalidate(input, planner.Result) != null, "preflight");
        checks += BatchPersistenceTests.CheckRecipe(input, planner.Result);
        return planner.Result;
    }
    internal static string Run()
    {
        checks = 0;
        var high = Input(99); var highPlan = Plan(high);
        var low = Input(90); var lowPlan = Plan(low);
        Check(highPlan.actualGrade == 99 && highPlan.expectedBottles == 2, "high-grade selection");
        Check(lowPlan.actualGrade == 90 && lowPlan.expectedBottles == 12, "lower-grade stock was not combined");
        var rest = BatchPersistenceTests.RemainingAfter(high, highPlan);
        Check(rest.supplies.Sum(ChemistryOperations.Total) > 19000, "unused low-grade stock lost");
        var lowRest = BatchPersistenceTests.RemainingAfter(low, lowPlan);
        Check(lowRest.supplies.Sum(ChemistryOperations.Total) == 0, "qualified stock left behind");
        foreach (int loss in new[] { 12, 9, 6, 3, 0 })
        {
            var input = Input(60, loss); var plan = Plan(input);
            Check(plan.expectedBottles == 12, "60% failed to use qualified stock at finisher loss " + loss);
            var snapshot = PlanningInputState.Capture(input); input.heaterUpgrades--;
            Check(snapshot.Changes(PlanningInputState.Capture(input)).Issues.Any(x => x.Key == "changed_modules"), "module changes did not invalidate preview");
        }
        return "Stock quantity audit: " + checks + " checks passed. Same stock: 99% -> 2, 90% -> 12; all qualified stock consumed at 60% across five finisher module settings. This is a synthetic fixture, not the player's unknown save.";
    }
}
