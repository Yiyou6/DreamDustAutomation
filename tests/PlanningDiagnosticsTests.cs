using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using DreamDustAutomation.Planning;
using Newtonsoft.Json.Linq;

public static class PlanningDiagnosticsTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception("Diagnostics: " + message); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static GameItem Size(GameItem item, int capacity)
    { item.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(capacity); return item; }
    private static BatchInputs Input() => new BatchInputs { productId = "dream_dust", requestedGrade = 99,
        raw = new GameItem[0], supplies = new GameItem[0], chamber = Mix(), mashBuffer = Mix(), maxProducts = 2000 };
    private static void Run(IEnumerator work)
    { int n = 0; while (work.MoveNext()) if (++n > 1000000) throw new Exception("Diagnostics search timeout"); }
    private static BatchPlanner Plan(BatchInputs input, string expected)
    {
        var before = PlanningInputState.Capture(input).Combined;
        var planner = new BatchPlanner(input); Run(planner.Search());
        Check(planner.Error == expected, "expected " + expected + "; got " + planner.Error);
        Check((planner.Result != null) == (expected == null), "result/error inconsistent");
        Check(before == PlanningInputState.Capture(input).Combined, "diagnosis mutated supplied material");
        Check(expected == null ? planner.Diagnostics.Issues.Count == 0 : planner.Diagnostics.Primary != null,
            "successful search retained a failed candidate, or failure lost evidence");
        return planner;
    }

    // Isolate the failed-dose boundary, then execute the actual native proportional
    // transfer via DoseFailure. The route has already attempted the allowed pour.
    private static void DoseCase(string reagent, int required, GameItem tank, GameItem[] stock, string expected, int alcoholPercent = 0)
    {
        var input = Input(); input.alcoholPercent = alcoholPercent; input.chamber = tank; input.supplies = stock;
        var planner = new BatchPlanner(input);
        var attemptType = typeof(BatchPlanner).GetNestedType("FeedAttempt", BindingFlags.NonPublic);
        var evidence = Activator.CreateInstance(attemptType, true);
        typeof(BatchPlanner).GetField("preparingFeed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(planner, evidence);
        typeof(BatchPlanner).GetMethod("InitializeSelectionQuality", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(planner, null);
        var type = typeof(BatchPlanner).GetNestedType("Route", BindingFlags.NonPublic);
        var route = Activator.CreateInstance(type, true);
        route.GetType().GetField("batch").SetValue(route, tank);
        route.GetType().GetField("supplies").SetValue(route, stock.Cast<SimulationItem>().ToArray());
        var before = string.Join(";", stock.Concat(new[] { tank }).Select(x => string.Join(",", ChemistryOperations.Parts(x))));
        typeof(BatchPlanner).GetMethod("DoseFailure", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(planner, new object[] { route, reagent, required, 20 });
        Check(planner.Error == expected, reagent + " dose expected " + expected + "; got " + planner.Error);
        Check(before == string.Join(";", stock.Concat(new[] { tank }).Select(x => string.Join(",", ChemistryOperations.Parts(x)))),
            "diagnostic probe consumed live source/tank");
        bool limited = (bool)attemptType.GetField("reagentLimited", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(evidence);
        Check(limited == (expected != null), "false shortage flag triggered smaller-feed search");
        if (expected == null) Check(planner.Diagnostics.Primary == null, "available reagent was called a shortage");
        else Check(planner.Diagnostics.Primary.Detail != null && planner.Diagnostics.Primary.Args.Length >= 4, "missing numeric dose evidence");
    }
    private static void Fault(Action action, string key)
    {
        try { action(); Check(false, "missing known fault " + key); }
        catch (MachineFault exception) { Check(exception.Key == key, "fault incorrectly classified: " + exception.Key); }
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string reagent in new[] { "alcohol", "acid", "base", "reducing_agent", "lipobond_agent", "water" })
        {
            DoseCase(reagent, 10000, Mix(), new GameItem[] { Mix(reagent, 20000) }, null);
            DoseCase(reagent, 10000, Size(Mix(), 20000), new GameItem[] { Mix(reagent, 10000, "starch", 10000) }, null);
            DoseCase(reagent, 10000, Size(Mix("starch", 5000), 5000), new GameItem[] { Mix(reagent, 20000) }, "diag_reagent_capacity");
            DoseCase(reagent, 10000, Mix(), new GameItem[] { Mix(reagent, 5000) }, "diag_reagent_short");
            DoseCase(reagent, 10000, Size(Mix("starch", 5000), 5000), new GameItem[] { Mix(reagent, 5000) }, "diag_reagent_short_and_full");
            DoseCase(reagent, 10000, Mix(), new GameItem[0], "diag_reagent_short");
            DoseCase(reagent, 10000, Mix(), new GameItem[] { Mix(reagent, 20000, "nightmare_extract", 1000) }, "diag_reagent_foreign");
        }
        DoseCase("alcohol", 10000, Mix(), new GameItem[] { Mix("alcohol", 10000, "dream_cap_mush", 5000) }, "diag_reagent_in_feed", 100);
        DoseCase("alcohol", 10000, Mix(), new GameItem[] { Mix("alcohol", 1000, "dream_cap_mush", 5000) }, "diag_reagent_short", 100);
        // Total solution volume, not pure reagent volume, determines capacity.
        DoseCase("alcohol", 10000, Size(Mix(), 15000), new GameItem[] { Mix("alcohol", 10000, "water", 10000) }, "diag_reagent_capacity");

        var input = Input(); Plan(input, "no_raw");
        input.maxProducts = 0; Plan(input, "output_no_space");
        input = Input(); input.chamber = Mix("dream_cap_mush", 20000); input.alcoholPercent = 100;
        Plan(input, "alcohol_unavailable");
        input.supplies = new GameItem[] { Mix("alcohol", 100000) }; Size(input.chamber, 100000);
        Plan(input, "diag_reagent_capacity");
        input.alcoholPercent = 25; input.mashBuffer = null;
        Plan(input, "missing_buffer");
        input.mashBuffer = Size(Mix(), 10000); Plan(input, "buffer_full");
        input = Input(); input.mashBuffer = Mix("dream_cap_extract", 20000); Size(input.chamber, 10000);
        Plan(input, "buffer_return_full");
        input.directForm = true; Plan(input, "buffer_return_full");
        input = Input(); input.supplies = new GameItem[] { Mix("methylol", 20000) }; input.productId = "fanta_pill";
        Plan(input, "heater_upgrade_required"); input.heaterUpgrades = 1;
        Plan(input, "diag_reagent_short");
        input = Input(); input.productId = "pink_alt_injector"; input.supplies = new GameItem[] { Mix("dream_cap_extract", 20000) };
        Plan(input, "diag_reagent_short");

        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            input = Input(); input.productId = id;
            input.supplies = new GameItem[] { Mix(recipe.Active, recipe.UnitVolume * 2, "starch", recipe.UnitVolume * 20) };
            var failed = Plan(input, "no_grade");
            Check(failed.Diagnostics.Primary.Detail == "diag_grade_detail", "no measured grade evidence for " + id);
            input.supplies = new GameItem[] { Mix(recipe.Active, recipe.UnitVolume - 1) };
            Plan(input, "diag_less_than_one");
            input.supplies = new GameItem[] { Mix(recipe.Active, recipe.UnitVolume * 3) };
            var success = Plan(input, null);
            Check(success.Result.actualGrade == 99 && success.Result.expectedBottles == 3, "diagnostics changed native yield");
            input.directForm = true; input.supplies = new GameItem[] { Mix("starch", recipe.UnitVolume - 1) };
            Plan(input, "diag_less_than_one");
            input.supplies = new GameItem[0]; input.chamber = Mix("water", 20000);
            Plan(input, "direct_no_output");
        }

        foreach (var mutate in new Action<BatchInputs>[] { x => x.directForm = true, x => x.heaterUpgrades++,
            x => Size(x.chamber, 30000), x => ChemistryOperations.SetAmount(x.chamber, "water", 3000),
            x => x.supplies = new GameItem[] { Mix("acid", 10000) }, x => x.maxProducts-- })
        {
            input = Input(); var before = PlanningInputState.Capture(input); mutate(input);
            var report = before.Changes(PlanningInputState.Capture(input));
            Check(report.Issues.Count == 1, "input change missing or duplicated");
        }
        input = Input(); var unchanged = PlanningInputState.Capture(input);
        input.chamber.ModifyTag(ChemicalHelper.CONTAINER_TEMP_TAG, t => t.SetDouble(29));
        Check(unchanged.Changes(PlanningInputState.Capture(input)).Issues.Count == 0, "ambient temperature spuriously invalidated preview");
        var all = new PlanningDiagnostics(); all.Add("diag_solid_capacity", 5); all.Add("diag_reagent_short", 20, null, "alcohol");
        all.Add("diag_reagent_short", 20, null, "alcohol"); all.CopyFrom(all);
        Check(all.Issues.Count == 2 && all.Primary.Key == "diag_reagent_short", "alternatives lost/dedup/ranking incorrect");
        Check(all.Describe((k, a) => k).Contains("diag_solid_capacity"), "earlier failed route lost");
        Fault(() => ChemistryOperations.Pour(Mix("water", 10), Mix(), -1), "runtime_invalid_transfer");
        Fault(() => ChemistryOperations.Pour(Mix("water", 10), Mix(), 11), "runtime_source_amount");
        Fault(() => ChemistryOperations.Pour(Mix("water", 10), Size(Mix(), 1), 10), "runtime_transfer_capacity");
        Fault(() => ChemistryOperations.Dissolve(Mix("starch", 10), Size(Mix(), 1)), "runtime_tank_capacity");
        Fault(() => ChemistryOperations.FilterAliquot(Mix("starch", 10), Mix(), 10), "runtime_filter_missing");
        Fault(() => ChemistryOperations.FilterAliquot(Mix("starch", 10), Mix(), 11), "runtime_filter_volume");
        foreach (var expected in new[] { "changed_settings", "changed_output", "runtime_source_missing", "runtime_source_amount",
            "diag_recheck_quantity", "diag_recheck_grade" })
        {
            input = Input(); input.supplies = new GameItem[] { Mix("dream_cap_extract", 20000) };
            var preview = Plan(input, null).Result;
            switch (expected)
            {
                case "changed_settings": input.directForm = true; break;
                case "changed_output": input.maxProducts = 1; break;
                case "runtime_source_missing": input.supplies = new GameItem[0]; break;
                case "runtime_source_amount": ChemistryOperations.SetAmount(input.supplies[0], "dream_cap_extract", 1); break;
                case "diag_recheck_quantity": preview.expectedBottles++; break;
                case "diag_recheck_grade": input.finisherLoss = 12; break;
            }
            var report = new PlanningDiagnostics();
            Check(BatchPlanner.Revalidate(input, preview, report) == null && report.Primary?.Key == expected,
                "preflight lost its known reason: " + expected + " / " + report.Primary?.Key);
        }
        return checks + " diagnostics checks: dose/capacity, synthesis, all 7 products, modes, quantities, input changes and runtime faults";
    }
}
