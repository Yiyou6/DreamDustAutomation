using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class NightReactionsTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Night processing: " + why); }
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
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(400);
        return item;
    }
    private static void DaySecond(RunningBatch batch, GameItem tank, GameItem buffer)
    {
        var action = batch.plan.actions[batch.step];
        if (buffer != null && !batch.plan.separateDirect) ChemistryOperations.ReactionTick(buffer);
        if (ReactionProgress.IsHeated(action.operation)) ChemistryOperations.ApplyBatchHeating(tank, batch.heaterUpgrades, action.targetTemperature > 0 ? action.targetTemperature : 45);
        if (!batch.plan.separateDirect || ReactionProgress.IsHeated(action.operation)) ChemistryOperations.ReactionTick(tank);
        batch.elapsed++;
    }
    private static string Execute(BatchInputs input, BatchPlan plan, bool overnight, bool dailyDrying = false)
    {
        var tank = new SimulationItem(input.chamber); var buffer = new SimulationItem(input.mashBuffer);
        var stock = input.supplies.Select(x => new SimulationItem(x)).ToArray();
        var batch = new RunningBatch { plan = plan.Copy(), heaterUpgrades = input.heaterUpgrades, finisherLoss = input.finisherLoss };
        int nativeNightSeconds = 360, evaporations = 0;
        while (batch.step < plan.actions.Length)
        {
            var action = plan.actions[batch.step];
            if (ReactionProgress.IsTimed(action.operation))
            {
                while (batch.elapsed < action.amount && (!overnight || nativeNightSeconds > 0))
                { DaySecond(batch, tank, buffer); nativeNightSeconds--; }
                if (overnight)
                {
                    batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
                    NightReactions.CompleteStep(batch, tank, buffer);
                }
                else { batch.step++; batch.elapsed = 0; }
                continue;
            }
            Check(!NightReactions.CanAdvance(batch), "night timer advanced a non-timed action");
            switch (action.operation)
            {
                case Operation.Pour: ChemistryOperations.Pour(stock[action.source], tank, action.amount); break;
                case Operation.StoreMash: ChemistryOperations.Pour(tank, buffer, action.amount); break;
                case Operation.FeedMash: ChemistryOperations.Pour(buffer, tank, action.amount); break;
                case Operation.Filter: FiltrationPolicy.Apply(tank, action, i => stock[i]); break;
                case Operation.Dry:
                    // Only the normal daily evaporation event removes these solvents.
                    int[] before = ChemistryOperations.Parts(tank);
                    NightReactions.CompleteStep(batch, tank, buffer);
                    Check(before.SequenceEqual(ChemistryOperations.Parts(tank)), "reaction catch-up evaporated solvents");
                    if (!dailyDrying) ChemistryOperations.SimulateDry(tank);
                    else
                    {
                        int days = 0;
                        while (ChemistryOperations.VolatileVolume(tank) > 0)
                        {
                            Check(++days <= 8, "daily evaporation stalled");
                            // The native overnight dispatcher runs ambient
                            // chemistry before the evaporation event. Exercise
                            // partial evaporation and reload at that boundary.
                            for (int second = 0; second < 360; second++)
                            { ChemistryOperations.ReactionTick(tank); ChemistryOperations.ReactionTick(buffer); }
                            batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
                            EvaporateDay(tank, 300000);
                        }
                    }
                    evaporations++; break;
                case Operation.Form:
                    var result = ChemistryOperations.Measure(tank, input.finisherLoss, 0, 0, null, ChemicalRecipe.Get(input.productId));
                    Check(result.Bottles == plan.expectedBottles && result.MinimumBottleActiveVolume == plan.minimumActive, "night changed final yield/purity");
                    break;
                default: throw new Exception("Unsupported fixture step: " + action.operation);
            }
            batch.step++;
        }
        Check(evaporations == plan.actions.Count(a => a.operation == Operation.Dry), "extra evaporation cycle");
        return string.Join(";", new[] { tank, buffer }.Concat(stock).Select(x =>
            string.Join(",", ChemistryOperations.Parts(x)) + ":" + DurabilityHelper.GetRealDurability(x)));
    }
    private static void EvaporateDay(GameItem tank, int capacity)
    {
        // Numeric part of the installed native evaporator: preserve the final
        // volatile component's integer remainder, including a partially dry day.
        var liquids = Liquid.Liquids.Where(l => l.isEvaporative && ChemistryOperations.Amount(tank, l.identifier) > 0).ToArray();
        int total = ChemistryOperations.VolatileVolume(tank), taken = 0;
        capacity = Math.Min(capacity, total);
        for (int i = 0; i < liquids.Length; i++)
        {
            int amount = ChemistryOperations.Amount(tank, liquids[i].identifier);
            int remove = i == liquids.Length - 1 ? capacity - taken : (int)((long)capacity * amount / total);
            remove = Math.Max(0, Math.Min(amount, remove)); taken += remove;
            ChemistryOperations.SetAmount(tank, liquids[i].identifier, amount - remove);
        }
        Check(taken == capacity, "daily evaporation budget changed");
    }
    internal static string Run()
    {
        checks = 0; var notes = new List<string>();
        foreach (int mush in new[] { 40000, 100000 })
        foreach (int modules in new[] { 1, 4 })
        {
            var input = new BatchInputs { productId = "pink_alt_injector", requestedGrade = 99, alcoholPercent = 100,
                heaterUpgrades = modules, finisherLoss = 0, raw = new GameItem[0], chamber = Mix(), mashBuffer = Mix(),
                supplies = new GameItem[] { Mix("dream_cap_mush", mush), Mix("alcohol", 1000000),
                    Mix("reducing_agent", 1000000), Mix("lipobond_agent", 1000000), Filter() } };
            var planner = new BatchPlanner(input); var search = planner.Search(); int steps = 0;
            while (search.MoveNext()) Check(++steps < 1000000, "injector planning stalled");
            var plan = planner.Result; Check(plan != null, "injector plan failed: " + planner.Error);
            Check(plan.actions.Count(a => a.operation == Operation.Dry) == 2, "injector fixture must cross both evaporation stages");
            Check(Execute(input, plan, true) == Execute(input, plan, true, true), "multi-day injector changed final materials or output");
            notes.Add("injector mush=" + mush + " modules=" + modules + ": two evaporation stages, " + plan.expectedBottles + " units");
        }
        foreach (string product in new[] { "caffeine_pill", "dream_dust" })
        foreach (int percent in new[] { 1, 100 })
        {
            var recipe = ChemicalRecipe.Get(product);
            var input = new BatchInputs { productId = product, requestedGrade = 99, alcoholPercent = percent, heaterUpgrades = 4,
                finisherLoss = 0, raw = new GameItem[0], chamber = Mix(), mashBuffer = Mix(),
                supplies = new GameItem[] { Mix(recipe.Mush, 100000), Mix("alcohol", 600000), Filter() } };
            var planner = new BatchPlanner(input); var search = planner.Search(); int steps = 0;
            while (search.MoveNext()) Check(++steps < 1000000, "planner stalled");
            var plan = planner.Result; Check(plan != null, "fixture plan failed: " + planner.Error);
            if (percent == 1 && product == "caffeine_pill") Check(plan.reactionSeconds > 360, "fixture must exceed native night duration: " + product + " " + plan.reactionSeconds + " s");
            Check(Execute(input, plan, false) == Execute(input, plan, true), "overnight differs from real timed processing");
            notes.Add(product + " " + percent + "%: " + plan.reactionSeconds + " s; " + plan.expectedBottles + " units");
        }
        foreach (var operation in new[] { Operation.Extract, Operation.Synthesize, Operation.BoilDry, Operation.React })
        foreach (int modules in new[] { 0, 4 })
        {
            var batch = new RunningBatch { heaterUpgrades = modules, plan = new BatchPlan {
                actions = new[] { new BatchAction(operation, amount: 420, targetTemperature: operation == Operation.Synthesize ? 52 : 125) } } };
            var tank = Mix("water", 300000, "alcohol", 60000, "protein", 3000, "acid", 3000, "dream_cap_extract", 10000, "reducing_agent", 10000, "lipobond_agent", 3000);
            var buffer = Mix("coffee_mush", 30000, "water", 5000);
            var manual = new SimulationItem(tank); var manualBuffer = new SimulationItem(buffer);
            for (int second = 0; second < 420; second++) DaySecond(batch, manual, manualBuffer);
            batch.elapsed = 0; NightReactions.CompleteStep(batch, tank, buffer);
            Check(ChemistryOperations.Parts(tank).SequenceEqual(ChemistryOperations.Parts(manual)) &&
                ChemistryOperations.Parts(buffer).SequenceEqual(ChemistryOperations.Parts(manualBuffer)), "step chemistry mismatch: " + operation);
            Check(ChemistryOperations.Temperature(tank) == ChemistryOperations.Temperature(manual), "temperature mismatch");
            Check(batch.step == 1 && batch.elapsed == 0, "step counters not advanced");
        }
        var failed = new RunningBatch { failed = true, plan = new BatchPlan { actions = new[] { new BatchAction(Operation.React, amount: 200) } } };
        Check(!NightReactions.CanAdvance(failed), "failed batch resumed without player");
        return checks + " night reaction checks passed; " + string.Join("; ", notes);
    }
}
