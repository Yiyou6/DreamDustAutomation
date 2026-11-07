using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class OvernightFeedbackTests
{
    internal static string Run() => ControllerFlowTests.OvernightFeedbackAudit();
}

internal sealed partial class ControllerFlowTests
{
    internal static string OvernightFeedbackAudit()
    {
        var errors = new List<string>(); int cases = 0;
        foreach (string id in new[] { "pink_alt_injector", "caffeine_pill" })
        foreach (int modules in new[] { 0, 1, 2, 4 })
        foreach (int grade in new[] { 99, 80, 60 })
        foreach (int percent in new[] { 100, 1 })
        foreach (int impurities in new[] { 0, 1, 2 })
        {
            var input = Inputs(id, false, DirectDryingMethod.Evaporate, percent);
            input.heaterUpgrades = modules; input.requestedGrade = grade;
            input.finisherLoss = modules >= 4 ? 0 : 25 - modules * 5;
            input.supplies[0] = id == "caffeine_pill"
                ? Mix("water", 6400, "fat", 16000, "coffee_mush", 51680, "caffeine", 8800, "physical_contaminant", 16000, "protein", 16000)
                : Mix("dream_cap_mush", 100000, "water", 20000, "protein", 10000, "physical_contaminant", 15000, "fat", 5000);
            if (impurities > 0)
            {
                // Original reagent bottles have 2-8% of these four impurities.
                for (int index = 2; index <= 5; index++)
                {
                    string ingredient = new[] { "acid", "base", "reducing_agent", "lipobond_agent" }[index - 2];
                    input.supplies[index] = Mix(ingredient, 960000, "microbe", 19000, "physical_contaminant", 10000,
                        "chemical_contaminant", 10000, "heavy_metal", 1000);
                }
                if (impurities == 2)
                {
                    // Re-used and warm reagent containers, not just new bottles.
                    ChemistryOperations.SetAmount(input.supplies[4], "fat", 10000);
                    ChemistryOperations.SetAmount(input.supplies[5], "fat", 10000);
                    foreach (var vessel in input.supplies.Skip(1).Take(5))
                        vessel.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(65);
                }
            }
            var planner = new BatchPlanner(input); var search = planner.Search(); int steps = 0;
            while (search.MoveNext()) Check(++steps < 1000000, "feedback planning stalled");
            if (planner.Result == null) continue;
            var diagnostics = new PlanningDiagnostics();
            var plan = BatchPlanner.Revalidate(input, planner.Result, diagnostics);
            if (plan == null && diagnostics.Primary?.Key == "diag_stock_reacting")
            {
                // Waiting uses native ambient reactions, not a frozen or edited
                // input bottle. The same supplies must become usable afterwards.
                int waited = 0;
                while (!SourceStability.Check(input, planner.Result, null) && waited++ < 1800)
                    foreach (var source in input.supplies.Where(i => i.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)))
                        ChemistryOperations.ReactionTick(source);
                Check(waited < 1800, "stable-source preflight never clears");
                planner = new BatchPlanner(input); search = planner.Search(); steps = 0;
                while (search.MoveNext()) Check(++steps < 1000000, "replanning stalled");
                Check(planner.Result != null, id + "/" + modules + "/" + grade + "/" + percent + "/" + impurities + " stable source plan failed: " + planner.Error);
                plan = BatchPlanner.Revalidate(input, planner.Result);
            }
            if (plan == null) { errors.Add(id + "/" + modules + "/" + grade + "/" + percent + "/impurities" + impurities + " revalidation failed"); continue; }
            foreach (bool reload in new[] { false, true })
            {
                cases++;
                var c = new ControllerFlowTests(input, plan, false);
                try
                {
                    c.Guard(c.AdvanceImmediate); int seconds = 0, cycles = 0;
                    while (c.running != null && !c.running.failed && seconds < 10000)
                    {
                        c.Guard(c.Tick); c.Frame(); seconds++;
                        if (impurities > 0)
                            foreach (var source in c.stock.Where(i => i.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)))
                                ChemistryOperations.ReactionTick(source);
                        if (seconds % 360 != 0 || c.running == null) continue;
                        if (reload) c.Reload();
                        CurrentConverterHelper.isNightCycle = true;
                        try { c.Guard(c.Cycle); } finally { CurrentConverterHelper.isNightCycle = false; }
                        c.Frame(); cycles++;
                    }
                    Check(c.running == null, "stalled/failed: " + c.status);
                    bool roundedTail = id == "pink_alt_injector" && modules == 1 && percent == 1 && impurities == 2;
                    Check(c.made.Count == (roundedTail ? 9 : plan.expectedBottles), "output count differs: " + c.made.Count + "/" + plan.expectedBottles);
                    if (roundedTail)
                    {
                        Check(c.status == "complete_rounding_tail", "shortfall was hidden");
                        Check(ChemistryOperations.Total(c.finisherTank) == 3997, "rounding tail was deleted or topped up");
                    }
                }
                catch (Exception error)
                {
                    var measured = ChemistryOperations.Measure(c.finisherTank, input.finisherLoss, 0, 0, null, ChemicalRecipe.Get(id));
                    string detail = id + "/modules" + modules + "/grade" + grade + "/alcohol" + percent + "/reload" + reload +
                        "/impurities" + impurities + " step=" + c.running?.step + " temp=" + ChemistryOperations.Temperature(c.tank) +
                        " planned=" + plan.actualGrade + "%/" + plan.minimumActive + " actualActive=" + measured.MinimumBottleActiveVolume + " " + error.Message;
                    errors.Add(detail); Console.WriteLine(detail);
                }
            }
        }
        Console.WriteLine(cases + " overnight flows; " + errors.Count + " failures");
        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
        return cases + " overnight flows passed";
    }
}
