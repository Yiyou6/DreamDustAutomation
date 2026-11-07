using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ExpandedControllerTests
{
    internal static string Run() => ControllerFlowTests.Expanded(0);
}
internal static class ExpandedControllerSmokeTests
{
    internal static string Run() => ControllerFlowTests.Expanded(8);
}

internal sealed partial class ControllerFlowTests
{
    private sealed class Case
    {
        internal string key;
        internal BatchInputs input;
        internal bool mustSucceed;
    }
    private static int planned, rejected, quantityPlans, interrupted, followups;
    private static readonly Dictionary<string, int> stagesCovered = new Dictionary<string, int>();
    private static readonly List<string> failures = new List<string>();
    private static GameItem Solid(string part, int amount, bool soluble = false)
    {
        var item = Mix(part, amount); item.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        item.EnableTag(soluble ? "SOLUBLE_TAG" : ChemicalHelper.BLENDABLE_TAG);
        return item;
    }
    private static GameItem Filter(string id, int durability)
    {
        var item = Filter(); item.identifier = id;
        item.state.GetTag("FILTER_SIZE_TAG").SetInt(id == "kotton_fabric" ? 150 : 22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG" }) item.state.GetTag(tag).SetInt(durability);
        return item;
    }
    private static BatchInputs Setup(string id, bool direct, int modules, int grade)
    {
        return new BatchInputs { productId = id, requestedGrade = grade, directForm = direct, separateDirect = direct,
            directDrying = DirectDryingMethod.Evaporate, normalDrying = DirectDryingMethod.Evaporate,
            chamber = Mix(), mashBuffer = Mix(), raw = new GameItem[0], supplies = new GameItem[0],
            heaterUpgrades = modules, blenderLoss = new[] { 35, 20, 10, 5, 0 }[modules],
            finisherLoss = new[] { 12, 6, 3, 1, 0 }[modules], alcoholPercent = 100, maxProducts = 2560 };
    }
    private static IEnumerable<Case> Matrix()
    {
        int n = 0;
        foreach (string id in ChemicalRecipe.Ids)
        foreach (int grade in GradePolicy.Grades)
        foreach (int modules in new[] { 0, 1, 2, 3, 4 })
        {
            var recipe = ChemicalRecipe.Get(id); var input = Setup(id, false, modules, grade);
            int profile = n++ % 6;
            var stock = new List<GameItem>();
            if (profile == 0) input.raw = new[] { Solid(recipe.Mush ?? recipe.Active, recipe.UnitVolume * 14) };
            else if (profile == 1 && recipe.Mush != null) stock.Add(Mix(recipe.Mush, 40000, "protein", 3000, "plant_matter", 2000));
            else if (profile == 2 && id == "fanta_pill") stock.Add(Mix("methylol", 40000, "stabilizing_agent", 40000));
            else stock.Add(Mix(recipe.Active, recipe.UnitVolume * 15, "water", 4000, "protein", profile * 101));
            if (profile == 3) stock.Add(Mix(recipe.Active, recipe.UnitVolume * 3, "starch", recipe.UnitVolume));
            if (profile == 4) ChemistryOperations.SetAmount(input.chamber, recipe.Active, 137);
            if (profile == 5) input.raw = new[] { Solid(recipe.Active, recipe.UnitVolume * 2, true) };
            int strength = profile == 2 ? 980000 : 1000000;
            foreach (string part in new[] { "alcohol", "acid", "base", "reducing_agent", "lipobond_agent" })
                stock.Add(Mix(part, strength, "water", 1000000 - strength));
            stock.Add(Filter("water_filter_adv", profile == 4 ? 1 : profile == 3 ? 0 : 800));
            stock.Add(Filter("water_filter", profile == 3 ? 0 : 400));
            stock.Add(Filter("kotton_fabric", profile == 4 ? 1 : 800));
            input.supplies = stock.ToArray(); input.alcoholPercent = new[] { 100, 75, 50, 25, 10, 1 }[profile];
            input.normalDrying = (n % 3 == 0) ? DirectDryingMethod.Heat : DirectDryingMethod.Evaporate;
            yield return new Case { key = id + "/normal/g" + grade + "/modules" + modules + "/profile" + profile,
                input = input, mustSucceed = input.normalDrying == DirectDryingMethod.Evaporate && profile != 0 && profile != 1 && !(profile == 2 && id == "fanta_pill" && modules == 0) };
        }
        foreach (string id in ChemicalRecipe.Ids)
        foreach (int modules in new[] { 0, 1, 4 })
        foreach (bool heat in new[] { false, true })
        foreach (bool solvent in new[] { false, true })
        for (int profile = 0; profile < 5; profile++)
        {
            var recipe = ChemicalRecipe.Get(id); var input = Setup(id, true, modules, 99);
            input.directSolvents = solvent; input.directDrying = heat ? DirectDryingMethod.Heat : DirectDryingMethod.Evaporate;
            switch (profile)
            {
                case 0: input.supplies = new GameItem[] { Mix("water", 30000, "physical_contaminant", recipe.UnitVolume * 12) }; break;
                case 1: input.raw = new[] { Solid("starch", recipe.UnitVolume * 12, true), Solid("organic_waste", 9000) }; break;
                case 2: input.supplies = new GameItem[] { Mix("acid", 12001), Mix("base", 14003), Mix("water", 30000, "starch", recipe.UnitVolume * 6) }; break;
                case 3: input.supplies = new GameItem[] { Mix("alcohol", 8000, "water", 5000), Mix("lipobond_agent", 12000), Mix("starch", recipe.UnitVolume * 5) }; break;
                case 4: input.supplies = new GameItem[] { Mix("water", 9000, "starch", recipe.UnitVolume * 4) }; input.chamber = Mix("starch", recipe.UnitVolume * 3 + 199); input.mashBuffer = Mix("starch", recipe.UnitVolume * 2 + 177); break;
            }
            yield return new Case { key = id + "/fake/modules" + modules + "/heat" + heat + "/solvent" + solvent + "/profile" + profile,
                input = input, mustSucceed = !heat };
        }
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true })
        foreach (int capacity in new[] { 0, 1, 7, 128, 1700 })
        {
            var recipe = ChemicalRecipe.Get(id); var input = Setup(id, direct, 4, 99);
            input.supplies = new[] { Mix(direct ? "starch" : recipe.Active, recipe.UnitVolume * 1800 + 1) };
            input.maxProducts = capacity;
            yield return new Case { key = id + "/capacity" + capacity + "/fake" + direct, input = input, mustSucceed = capacity > 0 };
        }
        foreach (string id in new[] { "fanta_pill", "pink_alt_injector" })
        foreach (int grade in GradePolicy.Grades)
        foreach (int modules in new[] { 1, 2, 3, 4 })
        for (int profile = 0; profile < 3; profile++)
        {
            var input = Setup(id, false, modules, grade);
            var feed = id == "fanta_pill" ? Mix("methylol", 125000, "stabilizing_agent", 125000, "chemical_contaminant", profile * 2500) :
                profile == 0 ? Mix("dream_cap_mush", 90000, "protein", 4000) : Mix("dream_cap_extract", 140000, "protein", 3000);
            input.supplies = new GameItem[] { feed, Mix("alcohol", 1000000), Mix("acid", 250000), Mix("base", 250000),
                Mix("reducing_agent", profile == 2 ? 10000 : 250000), Mix("lipobond_agent", profile == 1 ? 0 : 250000),
                Filter("water_filter_adv", profile == 2 ? 1 : 800), Filter("kotton_fabric", profile == 2 ? 0 : 800) };
            input.alcoholPercent = profile == 0 ? 10 : 100;
            yield return new Case { key = id + "/synthesis/g" + grade + "/modules" + modules + "/profile" + profile,
                input = input, mustSucceed = profile == 0 };
        }
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true })
        foreach (int modules in new[] { 0, 4 })
        foreach (int offset in new[] { -1, 0, 1 })
        {
            var recipe = ChemicalRecipe.Get(id); var input = Setup(id, direct, modules, 99);
            input.supplies = new[] { Mix(direct ? "starch" : recipe.Active, recipe.UnitVolume + offset) };
            yield return new Case { key = id + "/one-unit-boundary/" + offset + "/fake" + direct + "/modules" + modules,
                input = input, mustSucceed = offset >= 0 };
        }
    }
    private static string Materials(BatchInputs input) => string.Join("|", input.raw.Concat(input.supplies).Concat(new[] { input.chamber, input.mashBuffer })
        .Where(x => x != null).Select(x => string.Join(",", ChemistryOperations.Parts(x)) + ":" + ChemistryOperations.Temperature(x).ToString("R") + ":" + DurabilityHelper.GetRealDurability(x)));
    private static void FinishSearch(IEnumerator work)
    { int steps = 0; while (work.MoveNext()) Check(++steps < 1000000, "search did not finish"); }
    private static BatchPlan Calculate(Case test)
    {
        string before = Materials(test.input);
        var planner = new BatchPlanner(test.input); FinishSearch(planner.Search()); planned++;
        Check(before == Materials(test.input), "planning mutated stock");
        if (planner.Result == null)
        {
            rejected++;
            Check(!test.mustSucceed, "expected usable plan: " + planner.Error);
            Check(!string.IsNullOrEmpty(planner.Error) && planner.Diagnostics.Primary != null, "rejection has no explanation");
            return null;
        }
        var diagnostics = new PlanningDiagnostics();
        var plan = BatchPlanner.Revalidate(test.input, planner.Result, diagnostics);
        Check(plan != null, "fresh plan fails start recheck: " + diagnostics.Primary?.Key);
        Check(before == Materials(test.input), "revalidation mutated stock");
        Check(plan.expectedBottles > 0 && plan.expectedBottles <= test.input.maxProducts, "output capacity exceeded");
        return plan;
    }
    private void Night()
    { CurrentConverterHelper.isNightCycle = true; try { Guard(Cycle); } finally { CurrentConverterHelper.isNightCycle = false; } }
    private string MaterialState() => string.Join("|", rawStock.Where((x, i) => !consumedRaw.Contains(i)).Concat(stock).Concat(new[] { tank, mashBuffer, finisherTank })
        .Select(x => string.Join(",", ChemistryOperations.Parts(x)) + ":" + DurabilityHelper.GetRealDurability(x)));
    private void AssertState()
    {
        Check(!internalMove, "internal transfer lock leaked");
        Check(running == null || !running.failed, "runtime failed: " + status);
        foreach (var vessel in new[] { tank, mashBuffer, finisherTank }.Concat(stock))
        {
            Check(ChemistryOperations.Parts(vessel).All(x => x >= 0), "negative component");
            Check(ChemistryOperations.Total(vessel) <= WaterHelper.GetTotalCapacity(vessel), "overflowing vessel");
        }
    }
    private void Stress(int seed, bool stopAndRestart, BatchInputs original)
    {
        int expected = running.plan.expectedBottles, elapsed = 0, lastStep = -1, pauses = 0;
        int fixedHeater = running.heaterUpgrades, fixedFinisher = running.finisherLoss;
        var rng = new Random(seed); bool didStop = false;
        EmporiumEntry.Instance = this;
        // Start with a full bin so a tiny batch cannot finish before event injection.
        outputFull = true; Guard(AdvanceImmediate); Frame(); AssertState();
        while (running != null)
        {
            Check(++elapsed < 12000, "stress sequence stalled: " + status);
            bool newStep = lastStep != running.step;
            if (newStep)
            {
                lastStep = running.step;
                string stage = running.step < running.plan.actions.Length ? running.plan.actions[running.step].operation.ToString() : "Deferred";
                stagesCovered[stage] = stagesCovered.TryGetValue(stage, out int count) ? count + 1 : 1;
                string contents = MaterialState(); int produced = made.Count;
                Reload();
                Check(contents == MaterialState() && made.Count == produced, "reload changed materials or output");
                // Native slot installation is covered separately by LiveModuleTests;
                // here verify the captured settings survive the actual batch reload.
                Check(running.heaterUpgrades == fixedHeater && running.finisherLoss == fixedFinisher, "reload changed captured module settings");
                WindowVisible = !WindowVisible;
                stock = stock.Concat(new GameItem[] { Mix("starch", 137) }).ToArray(); // Extra supply waits for the next plan.
                Check(running.plan.expectedBottles == expected, "restock altered current output target");
                if (stopAndRestart && elapsed >= 2)
                {
                    string retained = MaterialState(); int finished = made.Count;
                    Guard(Stop); Frame();
                    Check(!Busy && running == null && made.Count == finished && MaterialState() == retained, "stop consumed material or emitted stale output");
                    interrupted++; didStop = true;
                    ReplanAndReuse(original);
                    break;
                }
            }
            if (DeferredOutput.Waiting(running))
            {
                string held = MaterialState(); int produced = made.Count;
                for (int wait = 0; wait < 2; wait++) { Guard(Tick); Night(); Frame(); }
                Check(held == MaterialState() && made.Count == produced, "deferred stock changed during waiting/night");
                Check(DeferredOutput.Release(running, Math.Min(rng.Next(1, 5), DeferredOutput.Remaining(running))), "partial release refused");
                if (pauses++ < 2) { DeferredOutput.Pause(running); Reload(); continue; }
            }
            if (newStep || elapsed % 9 == 0)
            {
                outputFull = true; int count = made.Count;
                Guard(Tick); Frame(); Check(made.Count == count, "full output created an item");
                outputFull = false;
            }
            if (running != null && running.step < running.plan.actions.Length && running.plan.actions[running.step].operation == Operation.Dry)
            {
                cores[2].DisableTag("TEST_POWER"); var held = ChemistryOperations.Parts(tank);
                Guard(Cycle); Frame(); Check(held.SequenceEqual(ChemistryOperations.Parts(tank)), "no-power cycle changed drying stock");
                cores[2].EnableTag("TEST_POWER"); Night();
            }
            else if (elapsed % 4 == 0) Night();
            else Guard(Tick);
            Frame(); AssertState();
        }
        Check(didStop || made.Count == expected, "stress output differs from preview");
        Check(!Busy && !internalMove && formingBatch == null, "sequence retained locks or output worker");
        scenarios++;
    }
    private void InterruptAt(Operation selected, BatchInputs original)
    {
        int iterations = 0; outputFull = true;
        EmporiumEntry.Instance = this; Guard(AdvanceImmediate);
        while (running != null)
        {
            Check(++iterations < 20000, "stage interruption stalled: " + status);
            AssertState();
            bool matched = selected == Operation.Form ? DeferredOutput.AtForm(running) :
                running.step < running.plan.actions.Length && running.plan.actions[running.step].operation == selected;
            if (matched)
            {
                string before = MaterialState(); int count = made.Count;
                Guard(Stop); Frame();
                Check(!Busy && made.Count == count && MaterialState() == before, "stage stop changed material");
                interrupted++; stagesCovered["Stop:" + selected] = stagesCovered.TryGetValue("Stop:" + selected, out int n) ? n + 1 : 1;
                ReplanAndReuse(original); scenarios++; return;
            }
            if (DeferredOutput.Waiting(running)) DeferredOutput.Release(running, DeferredOutput.Remaining(running));
            outputFull = false;
            // Daytime cycle keeps intermediate synthesis observable; overnight
            // deliberately finishes those reactions in a single dispatch.
            if (running.step < running.plan.actions.Length && running.plan.actions[running.step].operation == Operation.Dry) Guard(Cycle);
            else Guard(Tick);
            Frame();
        }
        // Zero-duration/empty-drying actions are correctly skipped by the controller.
        Check(!Busy && !internalMove, "skipped stage left execution locked"); scenarios++;
    }
    private void ReplanAndReuse(BatchInputs original)
    {
        var input = Setup(original.productId, original.directForm, original.heaterUpgrades, original.requestedGrade);
        input.normalDrying = original.normalDrying; input.directDrying = original.directDrying; input.directSolvents = original.directSolvents;
        input.blenderLoss = original.blenderLoss; input.finisherLoss = original.finisherLoss; input.alcoholPercent = original.alcoholPercent;
        input.raw = rawStock.Where((x, i) => !consumedRaw.Contains(i)).ToArray(); input.supplies = stock;
        input.chamber = FinishingStorage.PreviewStart(tank, finisherTank); input.mashBuffer = mashBuffer; input.finisher = finisherTank;
        var planner = new BatchPlanner(input); FinishSearch(planner.Search());
        Check(planner.Result != null || !string.IsNullOrEmpty(planner.Error), "stopped batch cannot finish replanning");
        Check(!Busy, "replan left stopped machine busy");
        var vessels = new[] { tank, mashBuffer, finisherTank };
        if (vessels.Any(x => ChemistryOperations.Total(x) > 0))
        {
            Check(tankCleanup.RequestMany(vessels, false), "cleanup unavailable after interruption");
            WindowVisible = true; Guard(Tick); Check(tankCleanup.ConfirmMany(vessels, false), "cleanup failed after interruption");
        }
        Check(vessels.All(x => ChemistryOperations.Total(x) == 0), "cleanup retained old stock");
        var fresh = Setup(original.productId, false, 4, 99); var recipe = ChemicalRecipe.Get(original.productId);
        fresh.supplies = new[] { Mix(recipe.Active, recipe.UnitVolume * 5 + 19) };
        var replacement = Calculate(new Case { input = fresh, key = "replacement", mustSucceed = true });
        tank = new SimulationItem(fresh.chamber); mashBuffer = new SimulationItem(fresh.mashBuffer); finisherTank = Mix();
        stock = fresh.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(); rawStock = new GameItem[0]; consumedRaw.Clear();
        made.Clear(); outputFull = false; formingBatch = null;
        running = new RunningBatch { schema = 2, useFinisherTank = true, plan = replacement.Copy(), heaterUpgrades = 4, finisherLoss = 0 };
        Reserve();
        RunSequence(1); followups++;
    }
    internal static string Expanded(int limit)
    {
        checks = scenarios = planned = rejected = quantityPlans = interrupted = followups = 0;
        failures.Clear(); stagesCovered.Clear(); int cases = 0;
        var timer = Stopwatch.StartNew();
        foreach (var test in Matrix())
        {
            if (limit > 0 && cases >= limit) break;
            cases++;
            try
            {
                var plan = Calculate(test);
                if (plan != null)
                {
                    foreach (bool defer in new[] { false, true })
                    {
                        new ControllerFlowTests(test.input, plan, defer).RunSequence(1);
                        new ControllerFlowTests(test.input, plan, defer).Stress(4411 + cases, false, test.input);
                    }
                    if (cases % 7 == 0)
                        new ControllerFlowTests(test.input, plan, true).Stress(5311 + cases, true, test.input);
                    if (test.key.Contains("/synthesis/") || cases % 31 == 0)
                        foreach (var stage in plan.actions.Select(a => a.operation).Where(op => op == Operation.Extract || op == Operation.Synthesize || op == Operation.Dry || op == Operation.Form).Distinct())
                            new ControllerFlowTests(test.input, plan, true).InterruptAt(stage, test.input);
                    if (cases % 5 == 0 && plan.expectedBottles > 1 && plan.expectedBottles < 200)
                    {
                        foreach (int count in new[] { 1, Math.Max(1, plan.expectedBottles / 2), plan.expectedBottles - 1 }.Distinct())
                        {
                            var quantity = new QuantityPlanning(test.input, plan, count); FinishSearch(quantity.Search()); quantityPlans++;
                            if (quantity.Result == null)
                            { Check(!string.IsNullOrEmpty(quantity.Error) && quantity.Diagnostics.Primary != null, "quantity failed silently"); continue; }
                            Check(quantity.Result.expectedBottles == count, "quantity plan has wrong count");
                            new ControllerFlowTests(test.input, quantity.Result, true).Stress(6711 + count, false, test.input);
                        }
                    }
                }
            }
            catch (Exception error)
            { failures.Add(test.key + ": " + error); Console.WriteLine("FAIL " + failures.Last()); }
            if (cases % 25 == 0) Console.WriteLine("PROGRESS cases=" + cases + " sequences=" + scenarios + " failures=" + failures.Count + " seconds=" + (int)timer.Elapsed.TotalSeconds);
            if (failures.Count >= 12) break;
        }
        string report = "Expanded audit: " + cases + " input/settings cases, " + planned + " plans, " + rejected + " explained rejections, " + quantityPlans + " quantity plans, " + scenarios + " executed sequences, " + interrupted + " stop/replan sequences, " + followups + " replacement batches, " + checks + " checks; seconds=" + (int)timer.Elapsed.TotalSeconds + ". Stages: " + string.Join(", ", stagesCovered.Select(p => p.Key + "=" + p.Value));
        Console.WriteLine(report);
        if (failures.Count > 0) throw new Exception(report + "\n" + failures.Count + " failures:\n" + string.Join("\n", failures));
        return report + "; PASSED. Headless production dispatch and native chemistry; no pointer/UI rendering or real FPS measurements.";
    }
}
