using System;
using System.Linq;
using System.Runtime.Serialization;
using DreamDustAutomation;
using DreamDustAutomation.Planning;

internal static class BatchRecoveryTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Batch recovery: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter_adv";
        item.EnableTag("WATER_FILTER"); item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(400);
        return item;
    }
    private static BatchPlanner Calculate(BatchInputs input)
    {
        var planner = new BatchPlanner(input); var work = planner.Search(); int n = 0;
        while (work.MoveNext()) Check(++n < 1000000, "planner stalled");
        return planner;
    }
    private static void CheckNeutralizers()
    {
        foreach (string product in new[] { "dream_dust", "nightmare_dust" })
        foreach (string vessel in new[] { "med_bottle_small_red", "water_tank" })
        foreach (bool alkaline in new[] { false, true })
        {
            var recipe = ChemicalRecipe.Get(product);
            var reagent = Mix(alkaline ? "base" : "acid", 98000, "water", 2000); reagent.identifier = vessel;
            var feed = Mix(recipe.Active, 70000, "protein", 30000);
            var input = new BatchInputs { productId = product, requestedGrade = 99, chamber = Mix(), mashBuffer = Mix(),
                heaterUpgrades = 4, finisherLoss = 0, supplies = new GameItem[] { feed, reagent, Filter() }, raw = new GameItem[0] };
            var before = input.supplies.Select(ChemistryOperations.Parts).ToArray();
            BatchPlan previous = null;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                var planner = Calculate(input); var plan = planner.Result;
                Check(plan != null && plan.actualGrade == 99, product + "/" + vessel + ": " + planner.Error);
                var usage = NeutralizerUsage.Read(input, plan);
                Check((alkaline ? usage.DrawnBase : usage.DrawnAcid) > 0, "neutralizer omitted from cleanup");
                Check((alkaline ? usage.Base : usage.Acid) == 98000, "actual reagent content misreported");
                Check(plan.actions.Any(a => a.operation == Operation.Filter), "cleanup omitted filtration");
                Check(BatchPlanner.Revalidate(input, plan) != null, "revalidation changed valid cleanup");
                if (previous != null) Check(plan.expectedBottles == previous.expectedBottles && plan.minimumActive == previous.minimumActive &&
                    plan.reagents == previous.reagents, "repeated planning changed neutralizer use");
                for (int i = 0; i < before.Length; i++) Check(before[i].SequenceEqual(ChemistryOperations.Parts(input.supplies[i])), "preview mutated supplies");
                previous = plan;
                // Data restored into fresh game objects, without stale reference identity.
                input.supplies = input.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray();
            }
            checks += BatchPersistenceTests.CheckRecipe(input, previous);
            var rest = BatchPersistenceTests.RemainingAfter(input, previous);
            var selected = NeutralizerUsage.Read(input, previous);
            Check(selected.DrawnAcid == input.supplies.Sum(x => (long)ChemistryOperations.Amount(x, "acid")) -
                    rest.supplies.Sum(x => (long)ChemistryOperations.Amount(x, "acid")) &&
                selected.DrawnBase == input.supplies.Sum(x => (long)ChemistryOperations.Amount(x, "base")) -
                    rest.supplies.Sum(x => (long)ChemistryOperations.Amount(x, "base")), "shown withdrawal disagrees with replay");
            input.supplies = input.supplies.Take(2).ToArray();
            var noFilter = Calculate(input).Result;
            Check(noFilter != null && noFilter.actualGrade < 99, "acid alone must not remove precipitated particles");
        }
        var pure = new BatchInputs { productId = "dream_dust", requestedGrade = 99, chamber = Mix("dream_cap_extract", 30000),
            raw = new GameItem[0], supplies = new GameItem[] { Mix("acid", 100000), Mix("base", 100000), Filter() }, finisherLoss = 0 };
        var purePlan = Calculate(pure).Result;
        Check(purePlan != null && purePlan.actualGrade == 99, "pure prepared material");
        var pureUse = NeutralizerUsage.Read(pure, purePlan);
        Check(pureUse.DrawnAcid == 0 && pureUse.DrawnBase == 0, "forced reagent use on pure material");
    }
    private static void CheckNightmareBudget()
    {
        var raw = Mix("nightmare_cap_mush", 10000, "protein", 1000);
        raw.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); raw.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        var input = new BatchInputs { productId = "nightmare_dust", raw = new GameItem[] { raw },
            supplies = new GameItem[] { Mix("alcohol", 5000), Mix("acid", 5000), Filter() }, chamber = Mix(), mashBuffer = Mix(),
            requestedGrade = 99, alcoholPercent = 100, heaterUpgrades = 4, finisherLoss = 0 };
        var budget = AlcoholBudget.Read(input);
        Check(!budget.AllowsPlanning(100) && budget.AllowsPlanning(10), "nightmare budget fixture");
        var limited = Calculate(input);
        Check(limited.Result == null && limited.Error == "alcohol_unavailable" && limited.Diagnostics.Primary.Detail == "diag_budget_detail", "missing stock feedback");
        input.alcoholPercent = 10;
        var staged = Calculate(input);
        Check(staged.Result != null, "nightmare lower-ratio plan: " + staged.Error);
        checks += BatchPersistenceTests.CheckRecipe(input, staged.Result);
    }
    private static void CheckHeldStock()
    {
        var reactive = Mix("acid", 8000, "base", 6000, "starch", 5000);
        int[] held = ChemistryOperations.Parts(reactive);
        bool locked = true;
        reactive.onOneSecondItemFunc = (item, inv, slot) => ChemistryOperations.ReactionTick(item);
        using (var binding = new LiquidSourceBinding(reactive, () => locked))
        {
            for (int second = 0; second < 720; second++) reactive.onOneSecondItemFunc(reactive, null, null);
            Check(held.SequenceEqual(ChemistryOperations.Parts(reactive)), "held stock reacted during overnight catch-up");
            locked = false; reactive.onOneSecondItemFunc(reactive, null, null);
            Check(!held.SequenceEqual(ChemistryOperations.Parts(reactive)), "released vessel failed to resume native chemistry");
        }
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var shell = Mix(); var reactor = Mix(recipe.Active, recipe.UnitVolume * 15);
            var finishing = Mix();
            // Skip the native water-feature UI update, which needs a Unity save
            // manager. Native proportional splitting and tag writes are intact.
            finishing.EnableTag("WINE_CONTAINER_TAG");
            var batch = new RunningBatch { schema = 2, useFinisherTank = true, deferOutput = true,
                plan = new BatchPlan { productId = id, requestedGrade = 99, actualGrade = 99, expectedBottles = 15,
                    actions = new[] { new BatchAction(Operation.Form, amount: 15) } } };
            Check(DeferredOutput.CanFinish(batch), "unprocessed batch has no exit");
            FinishingStorage.Stage(reactor, finishing, 15, recipe.UnitVolume, false);
            batch.prepared = 15; batch.outputCommitted = true;
            var guidance = InputGuidance.Read(id, false, new GameItem[0], reactor, Mix(), finisher: finishing);
            Check(guidance.HasFeedstock && guidance.MissingReason == null, "finisher stock misreported as empty feed");
            Check(InputGuidance.Read(id, true, new GameItem[0], reactor, Mix(), finisher: finishing).HasDirectMaterial,
                "finisher stock omitted from fake-material guidance");
            Check(DeferredOutput.CanFinish(batch), "held stock has no exit");
            Check(DeferredOutput.Release(batch, 15) && DeferredOutput.CanFinish(batch), "active release has no exit");
            for (int n = 0; n < 14; n++)
            {
                BottlingOperations.Consume(finishing, recipe.UnitVolume); batch.produced++;
                batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
            }
            Check(ChemistryOperations.Total(finishing) == recipe.UnitVolume, "native splitter lost the last unit");
            // Reproduce an already-damaged player's save. Retain the exact
            // shortfall; an escape must not conjure the promised last bottle.
            ChemistryOperations.SetAmount(finishing, recipe.Active, recipe.UnitVolume - 1);
            batch.failed = true; batch.failureKey = "runtime_less_than_one"; DeferredOutput.Pause(batch);
            batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
            Check(DeferredOutput.CanFinish(batch), "failed 14/15 saved batch stays trapped");
            var slot = (GameSlotInventory)FormatterServices.GetUninitializedObject(typeof(GameSlotInventory));
            var reservation = new BatchInputReservation();
            var reagent = Mix("acid", 12345); reagent.EnableTag(BatchInputReservation.SourceTag);
            reservation.Capture(new[] { reagent });
            using (var vesselLock = new InventoryLock(slot, () => batch != null))
            using (var liquidLock = new LiquidSourceBinding(finishing, () => batch != null))
            {
                Check(!slot.mayInventoryRemoveItemFunc(finishing, slot) && !finishing.canItemTargetItemFunc(finishing, reactor), "held vessel unlocked early");
                int[] before = ChemistryOperations.Parts(finishing);
                batch = null; reservation.Clear(); BatchPersistence.Save(shell, batch);
                Check(slot.mayInventoryRemoveItemFunc(finishing, slot) && finishing.canItemTargetItemFunc(finishing, reactor), "ended batch vessel stays locked");
                Check(!reservation.Contains(reagent) && ChemistryOperations.Amount(reagent, "acid") == 12345, "ending consumed reserved supply");
                Check(before.SequenceEqual(ChemistryOperations.Parts(finishing)), "ending discarded remainder");
                Check(BatchPersistence.Load(shell, out var error) == null && error == null, "ended batch restored obsolete lock");
                ChemistryOperations.Pour(finishing, reactor, ChemistryOperations.Total(finishing));
                Check(ChemistryOperations.Total(reactor) == recipe.UnitVolume - 1 && ChemistryOperations.Total(finishing) == 0, "manual transfer lost stock");
            }
            bool shortfall = false;
            try { FinishingStorage.Stage(reactor, finishing, 1, recipe.UnitVolume, false); }
            catch (MachineFault ex) { shortfall = ex.Key == "runtime_batch_short"; }
            Check(shortfall && ChemistryOperations.Total(reactor) == recipe.UnitVolume - 1 && ChemistryOperations.Total(finishing) == 0, "staging failed to preserve short stock");
            // Refill and plan again, without replacing the machine or the save.
            ChemistryOperations.SetAmount(reactor, recipe.Active, recipe.UnitVolume * 2);
            var input = new BatchInputs { productId = id, requestedGrade = 99, raw = new GameItem[0], supplies = new GameItem[0], chamber = reactor, finisherLoss = 0 };
            Check(Calculate(input).Result?.expectedBottles == 2, "recovered machine cannot plan next batch");
        }
    }
    internal static string Run()
    {
        checks = 0;
        CheckNeutralizers(); CheckNightmareBudget(); CheckHeldStock();
        return "Batch recovery: " + checks + " checks; acid/base vessel and repeated-preview/reload cases, nightmare stock diagnostics, seven-product 14/15 recovery, native vessel unlock, conserved remainder and replanning.";
    }
}
