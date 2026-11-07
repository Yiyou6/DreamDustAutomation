using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class FantaPlanningTests
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Fanta regression: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter(int size, int wear)
    {
        var item = Mix(); item.identifier = size > 50 ? "kotton_fabric" : "water_filter_adv"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(size);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(wear);
        return item;
    }
    internal static string Run()
    {
        checks = 0;
        // Recorded before the performance fix: preserve all achievable grades,
        // counts and per-unit minima. Zero means no qualifying batch.
        int[] grades = { 80, 60, 0, 99, 80, 30, 80, 60, 0, 99, 80, 30 };
        int[] counts = { 27, 36, 0, 25, 31, 83, 274, 366, 0, 252, 312, 833 };
        int[] minima = { 400, 300, 0, 499, 400, 150, 400, 300, 0, 495, 400, 150 };
        int fixture = 0;
        foreach (int bottles in new[] { 1, 10 })
        foreach (int modules in new[] { 1, 4 })
        foreach (int dirt in new[] { 0, 2500, 25000 })
        {
            var stock = Enumerable.Range(0, bottles).Select(i => (GameItem)Mix("methylol", 125000, "stabilizing_agent", 125000, "chemical_contaminant", dirt)).ToList();
            stock.AddRange(new GameItem[] { Mix("acid", 2500000, "water", 50000), Mix("base", 2500000, "water", 50000),
                Mix("reducing_agent", 2500000, "water", 50000), Filter(22, 400), Filter(22, 400), Filter(150, 1000), Filter(150, 1000) });
            var input = new BatchInputs { productId = "fanta_pill", raw = new GameItem[0], supplies = stock.ToArray(), chamber = Mix(),
                requestedGrade = 99, heaterUpgrades = modules, finisherLoss = modules == 1 ? 12 : 0 };
            var snapshot = stock.Select(x => string.Join(",", ChemistryOperations.Parts(x)) + ":" + DurabilityHelper.GetRealDurability(x)).ToArray();
            var planner = new BatchPlanner(input); var work = planner.Search();
            int steps = 0; double maxStep = 0; var watch = Stopwatch.StartNew(); var stages = new Dictionary<string, double>();
            bool complete = false;
            while (watch.Elapsed.TotalSeconds < 20)
            {
                double start = watch.Elapsed.TotalMilliseconds; string stage = planner.Stage;
                bool more = work.MoveNext(); double elapsed = watch.Elapsed.TotalMilliseconds - start;
                maxStep = Math.Max(maxStep, elapsed);
                if (!stages.ContainsKey(stage)) stages[stage] = 0; stages[stage] += elapsed;
                if (!more) { complete = true; break; } steps++;
            }
            var plan = planner.Result;
            Console.WriteLine("Fanta bottles=" + bottles + " modules=" + modules + " dirt=" + dirt + " done=" + complete +
                " ms=" + watch.Elapsed.TotalMilliseconds.ToString("F1") + " longest=" + maxStep.ToString("F1") + " yields=" + steps + " candidates=" + planner.CandidatesChecked +
                " result=" + plan?.actualGrade + "%/" + plan?.expectedBottles + " min=" + plan?.minimumActive + " error=" + planner.Error +
                " stages=" + string.Join(" ", stages.Select(p => p.Key + ":" + p.Value.ToString("F0"))));
            Check(complete && steps < 2000, "unnecessary partial-feed retries returned");
            Check((plan?.actualGrade ?? 0) == grades[fixture] && (plan?.expectedBottles ?? 0) == counts[fixture] &&
                (plan?.minimumActive ?? 0) == minima[fixture], "performance change altered native output: case " + fixture);
            Check(snapshot.SequenceEqual(stock.Select(x => string.Join(",", ChemistryOperations.Parts(x)) + ":" + DurabilityHelper.GetRealDurability(x))), "preview mutated stock");
            if (plan != null)
            {
                Check(BatchPlanner.Revalidate(input, plan) != null, "Fanta plan fails native replay");
                checks += BatchPersistenceTests.CheckRecipe(input, plan);
            }
            fixture++;
        }
        // Genuine resource and capacity shortages must retain partial-feed search.
        foreach (bool smallTank in new[] { false, true })
        {
            var tank = Mix(); if (smallTank) tank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(250000);
            var input = new BatchInputs { productId = "fanta_pill", raw = new GameItem[0], chamber = tank,
                requestedGrade = 90, heaterUpgrades = 1, supplies = new GameItem[] {
                    Mix("methylol", 125000, "stabilizing_agent", 125000), Mix("acid", smallTank ? 250000 : 5000),
                    Mix("base", smallTank ? 250000 : 5000), Mix("reducing_agent", 250000), Filter(22, 400) } };
            var planner = new BatchPlanner(input); var work = planner.Search(); int steps = 0;
            while (work.MoveNext()) Check(++steps < 100000, "genuine shortage search stalled");
            var plan = planner.Result;
            Check(plan != null && plan.actualGrade >= 80, "real shortage was pruned");
            Check(plan.actions.Where(a => a.operation == Operation.Pour && a.source == 0).Sum(a => a.amount) < 250000, "partial source not retained");
            Check(BatchPlanner.Revalidate(input, plan) != null, "shortage route does not replay");
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
        }
        return checks + " Fanta performance/native replay checks passed";
    }
}
