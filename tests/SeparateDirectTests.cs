using System;
using System.Collections;
using System.Linq;
using DreamDustAutomation;
using DreamDustAutomation.Planning;

internal static class SeparateDirectTests
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Separate direct: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Solid(bool blend, params object[] parts)
    {
        var item = Mix(parts); item.state.GetTag(WaterHelper.LIQUID_CONTAINER_TAG).Disable();
        item.state.GetTag(blend ? ChemicalHelper.BLENDABLE_TAG : "SOLUBLE_TAG").Enable();
        return item;
    }
    private static BatchInputs Input(params GameItem[] stock) => new BatchInputs {
        directForm = true, separateDirect = true, directSolvents = true, productId = "dream_dust", raw = new GameItem[0], supplies = stock,
        chamber = Mix(), mashBuffer = Mix(), maxProducts = 3000, finisherLoss = 12
    };
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 100000, "search failed to finish"); }
    private static BatchPlanner Calculate(BatchInputs input) { var p = new BatchPlanner(input); Drain(p.Search()); return p; }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(plan != null, "no plan");
        var diagnostics = new PlanningDiagnostics();
        Check(BatchPlanner.Revalidate(input, plan, diagnostics) != null, "start rejected: " + diagnostics.Primary?.Key);
        Check(plan.actions.All(a => a.operation != Operation.React && a.operation != Operation.Filter && a.operation != Operation.Extract && a.operation != Operation.Synthesize), "extra chemistry stage");
        Check(plan.actions.Where(a => a.operation == Operation.Form).Sum(a => a.amount) == plan.expectedBottles, "lot sum differs from preview");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    // Independent reference: native heated ticks, then native per-unit transfers
    // and finishing impurities, with each source kept in its own vessel.
    private static int NativeCount(GameItem source, BatchInputs input)
    {
        var tank = new SimulationItem(source);
        if (HeatedDrying.CanUse(tank, input.heaterUpgrades))
        {
            int seconds = 0;
            while (ChemistryOperations.VolatileVolume(tank) > 0 && seconds++ < HeatedDrying.TickLimit)
                ChemistryOperations.SimulateHeatedSecond(tank, input.heaterUpgrades, HeatedDrying.TargetTemperature);
            Check(ChemistryOperations.VolatileVolume(tank) == 0, "native drying failed");
        }
        else ChemistryOperations.SimulateDry(tank);
        int count = 0; var recipe = ChemicalRecipe.Get(input.productId);
        while (ChemistryOperations.Total(tank) >= recipe.UnitVolume)
        {
            var product = Mix(); ChemistryOperations.TransferToProduct(tank, product, recipe.UnitVolume);
            MachineChemFinisher.IntroduceImpurity(product, input.finisherLoss); count++;
        }
        return count;
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string product in ChemicalRecipe.Ids)
        foreach (int modules in new[] { 0, 1, 4 })
        {
            var input = Input(Mix("acid", 20001), Mix("base", 20003), Mix("water", 40000, "organic_waste", 20000, "microbe", 5000, "physical_contaminant", 5333));
            input.productId = product; input.heaterUpgrades = modules;
            int expected = input.supplies.Sum(s => NativeCount(s, input));
            var plan = Calculate(input).Result;
            Check(plan != null && plan.expectedBottles == expected, "native per-source total " + product + "/" + modules);
            Replay(input, plan);
            var rest = BatchPersistenceTests.RemainingAfter(input, plan);
            Check(ChemistryOperations.Amount(rest.supplies[0], "acid") == 20001 % ChemicalRecipe.Get(product).UnitVolume, "acid tail lost/mixed");
            Check(ChemistryOperations.Amount(rest.supplies[1], "base") == 20003 % ChemicalRecipe.Get(product).UnitVolume, "base tail lost/mixed");
            Check(DirectFormInputs.ReagentsInPlan(input, plan).SequenceEqual(new[] { "acid", "base" }), "source warning missing");
            var quantity = new QuantityPlanning(input, plan, 3); Drain(quantity.Search());
            Check(quantity.Result != null && quantity.Result.expectedBottles == 3 && quantity.Result.requestedBottles == 3, "quantity planning failed: " + quantity.Error);
            Replay(input, quantity.Result);
            var partial = BatchPersistenceTests.RemainingAfter(input, quantity.Result);
            Check(ChemistryOperations.Amount(partial.supplies[1], "base") == 20003, "unselected bottle consumed");
        }

        // The reported save: two dirty-water bottles and three reagent bottles.
        var water1 = Mix("water", 427800, "organic_waste", 96338, "microbe", 48330, "mineral", 48330, "chemical_contaminant", 96660, "physical_contaminant", 32220, "heavy_metal", 322);
        var water2 = Mix("water", 616425, "organic_waste", 39941, "microbe", 20036, "mineral", 20036, "chemical_contaminant", 40072, "physical_contaminant", 13357, "heavy_metal", 133);
        var acid = Mix("acid", 241959, "heavy_metal", 414, "chemical_contaminant", 5157, "physical_contaminant", 944);
        var alkali = Mix("base", 237318, "heavy_metal", 233, "chemical_contaminant", 9741, "physical_contaminant", 2079);
        var lipid = Mix("lipobond_agent", 141600, "heavy_metal", 720, "microbe", 293, "chemical_contaminant", 2183, "physical_contaminant", 5204);
        var report = Input(water1, water2, acid, alkali, lipid);
        var on = Calculate(report).Result;
        Check(on != null && on.expectedBottles == 475 && !on.actions.Any(a => a.operation == Operation.Pour && a.source == 4), "reported save must leave lipobond intact"); Replay(report, on);
        report.directSolvents = false;
        var off = Calculate(report).Result;
        Check(off != null && off.expectedBottles == 227, "water-only output should remain 227"); Replay(report, off);
        Check(BatchPlanner.Revalidate(report, on) == null, "changed solvent switch accepted old plan");
        report.directSolvents = true; report.heaterUpgrades = 4;
        var heated = Calculate(report).Result;
        Check(heated != null && heated.expectedBottles == report.supplies.Where(s => DirectFormInputs.CanUseLiquid(s, true)).Sum(s => NativeCount(s, report)), "heated save differs from native per-source sum"); Replay(report, heated);

        var tiny = Calculate(Input(Mix("acid", 1500), Mix("base", 1500)));
        Check(tiny.Result == null && tiny.Error == "direct_separate_no_output", "fractional units incorrectly combined");
        var stored = Input(Mix("starch", 10400)); stored.chamber = Mix("acid", 8400); stored.mashBuffer = Mix("base", 6700);
        var storedPlan = Calculate(stored).Result;
        Check(storedPlan != null && storedPlan.expectedBottles == 12 && storedPlan.actions.Count(a => a.operation == Operation.SwapTanks) == 2, "saved tanks not isolated");
        Replay(stored, storedPlan);
        var storedRest = BatchPersistenceTests.RemainingAfter(stored, storedPlan);
        Check(ChemistryOperations.Amount(storedRest.mashBuffer, "base") == 700 && ChemistryOperations.Amount(storedRest.mashBuffer, "acid") == 400 &&
            ChemistryOperations.Amount(storedRest.supplies[0], "starch") == 400, "saved residues vanished");

        var solid = Input(); solid.raw = new GameItem[] { Solid(true, "water", 10000, "starch", 24000), Solid(false, "starch", 13000) };
        solid.blenderLoss = 35; solid.heaterUpgrades = 4;
        var full = Calculate(solid).Result; Replay(solid, full);
        for (int number = 1; number <= full.expectedBottles; number++)
        {
            var q = new QuantityPlanning(solid, full, number); Drain(q.Search());
            Check(q.Result != null && q.Result.expectedBottles == number, "solid metering " + number + ": " + q.Error);
            Replay(solid, q.Result);
        }
        var hot = Input(); hot.heaterUpgrades = 4;
        hot.chamber.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(140);
        hot.raw = new GameItem[] { Solid(false, "water", 10000, "starch", 9000) };
        Replay(hot, Calculate(hot).Result);
        var chunks = Input(Mix("water", 100000, "starch", 33000));
        chunks.chamber.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(10000);
        var chunked = Calculate(chunks).Result;
        Check(chunked != null && chunked.expectedBottles == 16 && chunked.actions.Count(a => a.operation == Operation.Pour) > 1, "large bottle did not use successive chamber loads");
        Replay(chunks, chunked);
        var dilute = Input(Mix("water", 100000, "starch", 3500));
        dilute.chamber.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(10000);
        var concentrated = Calculate(dilute).Result;
        Check(concentrated != null && concentrated.expectedBottles == 1, "dilute large bottle could not accumulate its own solids"); Replay(dilute, concentrated);
        var mixture = Input(Mix("water", 50000, "acid", 10000, "base", 5000, "starch", 20000)); mixture.heaterUpgrades = 4;
        var mixed = Calculate(mixture).Result;
        Check(mixed != null && mixed.expectedBottles == NativeCount(mixture.supplies[0], mixture), "within-bottle heated losses bypassed"); Replay(mixture, mixed);
        var bufferOnly = Input(); bufferOnly.chamber = Mix("acid", 3000); bufferOnly.mashBuffer = Mix("base", 20000); bufferOnly.maxProducts = 3;
        var bufferPlan = Calculate(bufferOnly).Result;
        Check(bufferPlan != null && DirectFormInputs.ReagentsInPlan(bufferOnly, bufferPlan).SequenceEqual(new[] { "base" }), "unselected reactor reagent entered warning"); Replay(bufferOnly, bufferPlan);
        stored.maxProducts = 5;
        var capped = Calculate(stored).Result;
        Check(capped != null && capped.expectedBottles == 5 && capped.outputLimited, "output-space cap"); Replay(stored, capped);
        var blocked = Input(Mix("starch", 10000)); blocked.chamber = Mix("starch", 1000); blocked.mashBuffer = Mix();
        blocked.mashBuffer.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(500);
        var noBuffer = Calculate(blocked);
        Check(noBuffer.Result == null && noBuffer.Error == "direct_separate_buffer_full", "buffer-full explanation");

        var empty = Mix(); empty.EnableTag(BatchInputReservation.SourceTag); empty.GetTagReadonly(BatchInputReservation.SourceTag).SetInt(0);
        var reservation = new BatchInputReservation(); int ticked = 0;
        empty.onOneSecondItemFunc = (i, inv, slot) => ticked++;
        reservation.Capture(new[] { empty }, true);
        empty.onOneSecondItemFunc(empty, null, null); Check(ticked == 0, "reserved feed reacted between lots");
        var waiting = new RunningBatch { plan = new BatchPlan { actions = new[] { new BatchAction(Operation.ReturnFeed, 0, 500) } } };
        Check(!WasteCollection.CanCollect(empty, reservation, waiting), "return vessel sent to trash too early");
        reservation.Clear(); empty.onOneSecondItemFunc(empty, null, null); Check(ticked == 1, "release did not restore native callback");
        return "Independent direct forming: " + checks + " checks passed (isolated sources, native heat loss, save/resume, quantity, residue and source locks; reported save 227 -> 475 with lipobond protected)";
    }
}
