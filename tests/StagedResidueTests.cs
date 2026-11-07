using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class StagedResidueTests
{
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception("Staged residue: " + text); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG" })
            item.state.GetTag(key).Enable().SetInt(400);
        return item;
    }
    private static BatchInputs Input(string id, int percent, int upgrades, GameItem residue)
    {
        var recipe = ChemicalRecipe.Get(id);
        return new BatchInputs {
            productId = id, raw = new GameItem[0], chamber = residue, mashBuffer = Mix(),
            requestedGrade = 80, heaterUpgrades = upgrades, alcoholPercent = percent, maxProducts = 200,
            supplies = new GameItem[] { Mix(recipe.Mush, 100000, "protein", 2000, "organic_waste", 1000),
                Mix("alcohol", 600000), Mix("acid", 50000), Mix("base", 50000),
                Mix("reducing_agent", 200000), Mix("lipobond_agent", 50000), Filter(), Filter() }
        };
    }
    private static void Drain(IEnumerator work)
    { int n = 0; while (work.MoveNext()) if (++n > 1000000) throw new Exception("Residue search timeout"); }
    private static BatchPlan Plan(BatchInputs input)
    {
        var state = PlanningInputState.Capture(input).Combined;
        var planner = new BatchPlanner(input); Drain(planner.Search());
        Check(planner.Result != null, input.productId + "/" + input.alcoholPercent + "%: " + planner.Error);
        Check(state == PlanningInputState.Capture(input).Combined, "planning changed live residues or stock");
        Check(BatchPlanner.Revalidate(input, planner.Result) != null, "native start replay failed");
        return planner.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
        checks += BatchPersistenceTests.CheckFinisherRoute(input, plan, true);
    }
    internal static string Run()
    {
        checks = 0;
        // A tiny acidic tail reacts with fresh protein while the remaining mash
        // waits in the blender. Native reactions shrink that physical buffer.
        var input = Input("dream_dust", 10, 0,
            Mix("dream_cap_extract", 1349, "physical_contaminant", 259, "plant_matter", 370, "acid", 1));
        var plan = Plan(input);
        Replay(input, plan);
        Check(ChemistryOperations.Total(BatchPersistenceTests.RemainingAfter(input, plan).mashBuffer) == 0,
            "full batch left buffered mash");

        // The same residual reaction can occur with any staged recipe, solvent
        // budget or heater upgrade. Native replay checks products and every tail.
        foreach (string id in new[] { "dream_dust", "nightmare_dust", "caffeine_pill", "pink_alt_injector" })
        foreach (int percent in AlcoholBudget.Percentages)
        foreach (int upgrades in new[] { 0, 4 })
        {
            var residue = Mix("acid", 107, "base", 11, "protein", 53, "organic_waste", 37);
            input = Input(id, percent, upgrades, residue);
            // Exercise the real next-batch merge from the finisher as well.
            input.chamber = FinishingStorage.PreviewStart(Mix(), residue);
            plan = Plan(input);
            Replay(input, plan);
            var left = BatchPersistenceTests.RemainingAfter(input, plan);
            Check(ChemistryOperations.Total(left.mashBuffer) == 0, id + "/" + percent + "%: unfinished full feed");
            Check(ChemistryOperations.Amount(residue, "acid") == 107, "finisher preview consumed real leftovers");
        }

        // A quantity-limited plan grinds whole solid items and keeps the excess
        // in the same buffer. Do not accidentally consume the entire reserve.
        foreach (int percent in new[] { 25, 10, 1 })
        {
            input = Input("dream_dust", percent, 0, Mix("acid", 101, "dream_cap_extract", 1349));
            var raw = (SimulationItem)input.supplies[0];
            raw.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); raw.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            input.raw = new GameItem[] { raw }; input.supplies = input.supplies.Skip(1).ToArray();
            var maximum = Plan(input);
            var quantity = new QuantityPlanning(input, maximum, maximum.expectedBottles / 2);
            Drain(quantity.Search());
            Check(quantity.Result != null, percent + "% quantity failed: " + quantity.Error);
            Check(quantity.Result.expectedBottles == maximum.expectedBottles / 2, "selected count changed");
            Check(BatchPlanner.Revalidate(input, quantity.Result) != null, "quantity cannot replay");
            Replay(input, quantity.Result);
            var remaining = BatchPersistenceTests.RemainingAfter(input, quantity.Result);
            Check(ChemistryOperations.Amount(remaining.mashBuffer, "dream_cap_mush") > 0, "reserved mash was drained");
            // The remainder is usable without clearing either physical vessel.
            remaining.requestedGrade = input.requestedGrade; remaining.alcoholPercent = percent;
            Replay(remaining, Plan(remaining));
        }

        // Real capacity failures must still be reported: actual unprocessed feed
        // remains in the buffer while solvent + accumulated extract fills up.
        input = Input("dream_dust", 25, 0, Mix("dream_cap_mush", 10000, "starch", 89900, "acid", 100));
        input.feedPrepared = true;
        input.chamber.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(100000);
        var full = new BatchPlanner(input); Drain(full.Search());
        Check(full.Result == null && full.Error == "diag_cycle_capacity", "real capacity limit was suppressed: " + full.Error);

        return checks + " staged residue checks passed";
    }
}
