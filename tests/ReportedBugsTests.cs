using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ReportedBugsTests
{
    private static int checks;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception("Reported regression: " + text); }
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
        foreach (var key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(key).Enable().SetInt(400);
        return item;
    }
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 1000000, "search did not finish"); }
    private static BatchPlan Calculate(BatchInputs input)
    {
        var p = new BatchPlanner(input); Drain(p.Search());
        Check(p.Result != null, input.productId + ": " + p.Error); return p.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "revalidation changed the result");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    private static BatchInputs Input(string id, params GameItem[] stock) => new BatchInputs {
        productId = id, raw = new GameItem[0], supplies = stock, chamber = Mix(), mashBuffer = Mix(),
        requestedGrade = 99, heaterUpgrades = 4, finisherLoss = 0, maxProducts = 200
    };

    internal static string Run()
    {
        checks = 0;
        foreach (string impurity in new[] { "water", "physical_contaminant", "chemical_contaminant" })
        foreach (bool agent in new[] { false, true })
        {
            // Native factory coffee composition for 80 g. The original planner
            // lost lipobond to boiling and left 3.2 mL fat, capping this at 80%.
            var coffee = Mix("water", 3200, "fat", 8000, "coffee_mush", 25840, "caffeine", 4400,
                "physical_contaminant", 8000, "protein", 8000);
            coffee.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); coffee.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var stock = new List<GameItem> { Mix("alcohol", 1000000), Mix("acid", 980000, impurity, 20000),
                Mix("base", 980000, impurity, 20000), Filter(), Filter() };
            if (agent) stock.Add(Mix("lipobond_agent", 100000));
            var input = Input("caffeine_pill", stock.ToArray()); input.raw = new GameItem[] { coffee };
            var before = ChemistryOperations.Parts(coffee);
            var plan = Calculate(input);
            Check(plan.actualGrade == (agent ? 99 : 60), "coffee agent effect: " + impurity);
            Check(ChemistryOperations.Parts(coffee).SequenceEqual(before), "preview consumed coffee");
            if (agent)
            {
                Check(plan.actions.Any(a => a.operation == Operation.React && a.precursorId == "fixed_wait"), "cooling was skipped");
                Check(plan.reactionSeconds == plan.actions.Where(a => a.operation == Operation.React || a.operation == Operation.Extract || a.operation == Operation.Synthesize).Sum(a => a.amount), "cooling missing from countdown");
                Replay(input, plan);
                var rest = BatchPersistenceTests.RemainingAfter(input, plan);
                Check(ChemistryOperations.Amount(rest.chamber, "fat") == 0, "fat remained after native replay");
                Check(ChemistryOperations.Amount(rest.supplies[5], "lipobond_agent") < 100000, "agent was not consumed");
            }
        }
        // Pre-extracted hot caffeine still requires cooling before lipobond;
        // it must not rely on an Extract step being present in the plan.
        var hotStock = Mix("caffeine", 50000, "fat", 8000);
        hotStock.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(90);
        var hot = Input("caffeine_pill", hotStock, Mix("lipobond_agent", 100000), Filter());
        var hotPlan = Calculate(hot); Check(hotPlan.actualGrade == 99, "hot finished extract cleanup"); Replay(hot, hotPlan);

        // Lab-pure prepared Dream Dust must remain lab-pure with native 0% loss.
        // A lower requested tier is only a threshold: measured purity stays high.
        foreach (int purity in new[] { 9900, 9950, 10000 })
        foreach (int target in new[] { 90, 99 })
        {
            var input = Input("dream_dust", Mix("dream_cap_extract", purity * 20, "chemical_contaminant", (10000 - purity) * 20));
            input.requestedGrade = target;
            var plan = Calculate(input);
            Check(plan.actualGrade == target && plan.minimumActive >= 1980 && plan.expectedBottles == 100, "prepared Dream Dust was diluted");
            Check(!plan.actions.Any(a => a.operation == Operation.Extract || a.operation == Operation.Filter), "unnecessary processing of prepared extract");
            Replay(input, plan);
        }
        // Exact ranges consistent with the player's scanner: total 257.321 mL,
        // active displayed as 254.73 mL, chemical contamination 1.86 mL,
        // heavy metal 709 uL, minerals 1 uL and base 7 uL.
        // Native volume text truncates to .01 mL but percentage rounds to F2.
        var screenshotNotes = new List<string>();
        for (int active = 254735; active <= 254739; active++)
        {
            var sample = Mix("dream_cap_extract", active, "heavy_metal", 709,
                "chemical_contaminant", 257321 - active - 717, "mineral", 1, "base", 7);
            var screenInput = Input("dream_dust", sample);
            var screenPlan = Calculate(screenInput);
            Check(ChemistryOperations.Total(sample) == 257321, "screenshot volume");
            Check(((float)active / 257321 * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) == "99.00", "scanner rounding changed");
            var wholeInput = Input("dream_dust"); wholeInput.chamber = new SimulationItem(sample); wholeInput.feedPrepared = true;
            var whole = Calculate(wholeInput);
            Check(whole.actualGrade == 90 && whole.expectedBottles == 128 && whole.minimumActive == 1979,
                "native whole-bottle result changed");
            Check(whole.concentrationHint == "purity_boundary", "rounded 99% needs a boundary explanation");
            // Metering can leave the final impure aliquot outside this batch.
            // Require exact native replay, not the old all-or-nothing fallback.
            Check(screenPlan.actualGrade >= whole.actualGrade && screenPlan.expectedBottles <= 128,
                "metered source lost its grade or created extra material");
            Check(screenPlan.actualGrade == 99 ? screenPlan.minimumActive >= 1980 :
                screenPlan.expectedBottles == 128 && screenPlan.minimumActive == 1979, "metered boundary grade");
            Replay(screenInput, screenPlan);
            screenshotNotes.Add(active + " uL active: " + (active * 100.0 / 257321).ToString("F6") + "% bulk, min bottle " + screenPlan.minimumActive * 100.0 / 2000 + "%, " + screenPlan.expectedBottles + " units; whole source remains 128 at 98.95%");
        }
        // A real limitation still falls back: do not manufacture purity to hide
        // residues or missing finisher upgrades.
        var dirty = Input("dream_dust", Mix("dream_cap_extract", 200000));
        dirty.chamber = Mix("chemical_contaminant", 10000);
        var limited = Calculate(dirty); Check(limited.actualGrade == 90, "foreign residue must limit purity"); Replay(dirty, limited);
        dirty.chamber = Mix(); dirty.finisherLoss = 3;
        limited = Calculate(dirty); Check(limited.actualGrade == 90, "native finisher losses must remain"); Replay(dirty, limited);

        // Dry contents stay in their vessels and participate only in direct
        // forming; metered batches and a second batch use the remaining contents.
        foreach (string id in ChemicalRecipe.Ids)
        foreach (string material in new[] { "starch", "physical_contaminant" })
        {
            var recipe = ChemicalRecipe.Get(id);
            var vessel = Mix(material, recipe.UnitVolume * 8);
            var input = Input(id, vessel); input.directForm = true;
            Check(DirectFormInputs.CanUseLiquid(vessel), "dry contents were rejected");
            var guide = InputGuidance.Read(id, true, input.supplies, input.chamber, input.mashBuffer);
            Check(guide.HasDirectMaterial && guide.Hints.Contains("input_direct_dry"), "dry feed guidance");
            var normal = new BatchPlanner(Input(id, vessel)); Drain(normal.Search());
            Check(normal.Result == null, "non-medicinal powder entered normal production");
            var full = Calculate(input);
            Check(full.expectedBottles == 8 && full.minimumActive == 0, "dry powder native yield"); Replay(input, full);
            Check(ChemistryOperations.Total(vessel) == recipe.UnitVolume * 8, "preview consumed the vessel");
            var meter = new QuantityPlanning(input, full, 3); Drain(meter.Search());
            Check(meter.Result != null && meter.Result.expectedBottles == 3, "dry powder quantity selection"); Replay(input, meter.Result);
            var rest = BatchPersistenceTests.RemainingAfter(input, meter.Result);
            Check(ChemistryOperations.Total(rest.supplies[0]) == recipe.UnitVolume * 5, "unused powder was lost");
            Check(Calculate(rest).expectedBottles == 5, "dry powder second batch");
        }
        foreach (string reagent in new[] { "alcohol", "acid", "base", "lipobond_agent", "reducing_agent", "stabilizing_agent" })
            Check(DirectFormInputs.CanUseLiquid(Mix("starch", 10000, reagent, 1)) == (reagent != "alcohol" && reagent != "lipobond_agent"), "reagent/alcohol eligibility changed: " + reagent);
        foreach (string medicine in new[] { "dream_cap_extract", "methylol" })
            Check(!DirectFormInputs.CanUseLiquid(Mix("starch", 10000, medicine, 1)), "protected medicine was treated as starch: " + medicine);
        return string.Join("; ", screenshotNotes) + "\nReported bugs: " + checks + " checks passed (native coffee cleanup, prepared Dream Dust, dry residue, quantity and save/resume)";
    }
}
