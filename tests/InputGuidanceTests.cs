using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class InputGuidanceTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Input guidance: " + message); }
    private sealed class NativeItem : GameItem
    {
        internal NativeItem(string id) : base(id) { modifiedState = state; }
        public override void Validate() { modifiedState = state; }
        public override void Destroy() { }
    }
    private static GameItem Cap(bool edible, bool harvested = false)
    {
        var item = new NativeItem(edible && !harvested ? "meal_cap" : "dream_cap");
        item.spritePath = edible ? "meal_cap_l" : "dream_cap";
        ChemicalCompositionHelper.SetDreamCapComposition(item, 32.5f, edible);
        return item;
    }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2)
            ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem Solid(string id, params object[] parts)
    {
        var item = Mix(parts); item.identifier = id;
        item.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        item.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        return item;
    }
    private static SimulationItem Filter(string id, int durability)
    {
        var item = Solid(id); item.EnableTag("WATER_FILTER");
        item.ModifyTag(DurabilityHelper.DURABILITY_TAG, s => s.Enable().SetInt(durability));
        return item;
    }
    private static InputGuidance Read(bool direct, params GameItem[] items) =>
        InputGuidance.Read("dream_dust", direct, items, null, null);
    private static string Parts(GameItem item) => string.Join(",", ChemistryOperations.Parts(item));
    internal static string Run()
    {
        var meal = Cap(true); var harvestedMeal = Cap(true, true); var dream = Cap(false);
        var alcohol = Mix("alcohol", 500000); var acid = Mix("acid", 50000); var filter = Filter("water_filter", 400);
        var stock = new[] { meal, alcohol, acid, filter };
        var snapshots = stock.Select(Parts).ToArray();
        var report = Read(false, stock);
        Check(!report.HasFeedstock && report.MissingReason == "input_meal_not_drug", "Reported Meal Cap setup has an explicit diagnosis");
        Check(Read(false, harvestedMeal).MissingReason == "input_meal_not_drug", "Harvested edible cap keeps dream_cap ID but is recognized by native sprite");
        var direct = Read(true, stock);
        Check(direct.HasDirectMaterial && direct.MissingReason == null, "Meal Caps remain valid for deliberate direct forming");
        Check(direct.Hints.Contains("input_meal_direct") && direct.Hints.Contains("input_direct_bottles") &&
            direct.Hints.Contains("input_direct_filters"), "Direct mode explains potency and ignored supplies");
        Check(Read(false, dream).HasFeedstock && Read(false, dream).MissingReason == null, "Native Dream Cap works");
        report = Read(false, meal, dream);
        Check(report.HasFeedstock && report.MissingReason == null && report.Hints.Contains("input_meal_not_drug"), "Mixed valid and edible caps are advisory, not blocked");
        foreach (var recipe in ChemicalRecipe.Ids.Select(ChemicalRecipe.Get))
        {
            var mutant = Solid("meal_cap", recipe.Active, 10000, "plant_matter", 1000);
            var mixed = InputGuidance.Read(recipe.Id, false, new[] { mutant }, null, null);
            Check(mixed.HasFeedstock && mixed.MissingReason == null && !mixed.Hints.Contains("input_meal_not_drug"), "Composition overrides edible name: " + recipe.Id);
            Check(Read(true, mutant).Hints.Contains("input_direct_protected"), "All medicines remain protected in direct mode: " + recipe.Id);
        }
        var nightmare = Solid("nightmare_cap", "nightmare_cap_mush", 20000);
        report = Read(false, nightmare);
        Check(report.MissingReason == "input_other_product" && report.OtherProducts.Contains("nightmare_dust"), "Wrong drug is identified");
        report = Read(false, nightmare, dream);
        Check(report.HasFeedstock && report.MissingReason == null && report.Hints.Contains("input_other_kept"), "Other drug alongside matching feed does not fail");
        Check(Read(false, Solid("dream_cap", "plant_matter", 20000)).MissingReason == "input_cap_no_compound", "Dream-cap appearance without precursor is explained");
        Check(Read(false, Solid("dream_cap")).MissingReason == "input_cap_no_compound", "Entirely empty composition does not masquerade as missing solvent");
        Check(Read(false, Solid("nutrifruit", "starch", 20000)).MissingReason == "input_food_not_drug", "Ordinary food is not medicinal feed");
        Check(Read(false, alcohol, acid, filter).MissingReason == "input_aux_only", "Reagents and filters alone cannot substitute for feed");
        Check(Read(false).MissingReason == "input_empty", "Empty intake explains next step");
        var mash = Mix("dream_cap_mush", 10000);
        var extract = Mix("dream_cap_extract", 10000);
        Check(Read(false, mash).Hints.Contains("input_bottled_feed"), "Normal mode accepts bottled mash");
        Check(Read(false, extract).Hints.Contains("input_already_extracted"), "Extracts are distinguished from missing feed");
        Check(Read(true, mash).MissingReason == "input_direct_protected", "Direct mode protects bottled medicinal mash");
        Check(Read(true, dream).MissingReason == "input_direct_protected", "Direct mode explains protected medicinal solids");
        report = InputGuidance.Read("dream_dust", true, new[] { dream, mash }, extract, null);
        Check(report.Summary == "input_direct_residue_only", "Explains first-batch-only residue trap");
        Check(report.HasDirectMaterial, "Residue still allows direct forming");
        Check(InputGuidance.Read("dream_dust", true, new[] { dream, mash }, Mix(), null).MissingReason != null, "Empty subsequent tank correctly reports no direct feed");
        var pitcher = Mix("dream_cap_mush", 10000); pitcher.identifier = "water_pitcher";
        Check(!Read(false, pitcher).HasFeedstock && Read(false, pitcher).MissingReason == "input_pitcher", "An old saved pitcher is excluded with a transfer instruction");
        Check(!Read(true, pitcher).HasDirectMaterial && Read(true, pitcher).MissingReason == "input_pitcher", "Direct forming also explains the pitcher restriction");
        var dirtyWater = Mix("water", 100000, "physical_contaminant", 12000);
        Check(Read(true, dirtyWater).HasDirectMaterial && Read(true, dirtyWater).Summary == "input_direct_water", "Impure water is explained as direct material");
        Check(Read(false, dirtyWater).MissingReason == "input_water_direct_only", "Normal mode explains why impure water is left untouched");
        Check(Read(true, Mix("water", 100000)).MissingReason == "input_pure_water", "Pure water cannot promise fake products");
        var product = Solid("dream_dust", "dream_cap_extract", 2000);
        Check(!Read(false, product).HasFeedstock && Read(false, product).MissingReason == "input_finished_product", "Old saved products are ignored with an output-bin instruction");
        Check(InputGuidance.Read("dream_dust", false, new GameItem[0], null, null, null, true, true).Hints.Contains("input_output_not_feed"), "Output isn't silently reused");
        Check(InputGuidance.Read("dream_dust", false, new GameItem[0], null, null, null, true, true).Hints.Contains("input_waste_not_feed"), "Waste isn't silently reused");
        Check(Read(false, dream, Filter("water_filter_adv", 0)).Hints.Contains("input_filters_empty"), "Regenerating filter may need time to recover");
        Check(!Read(false, dream, Filter("water_filter_adv", 1)).Hints.Contains("input_filters_empty"), "Usable filter isn't reported empty");
        Check(Read(false, dream, Filter("uv_filter", 0)).Hints.Contains("input_uv_only"), "UV distinguishes microbes from particles");
        Check(InputGuidance.Read("dream_dust", false, new GameItem[0], null, null, Mix("alcohol", 1000, "dream_cap_extract", 10000)).HasFeedstock, "Eligible collector composition is included");
        Check(!InputGuidance.Read("dream_dust", false, new GameItem[0], null, null, extract).HasFeedstock, "Collector without alcohol is not falsely advertised as feed");
        Check(Read(false, new NativeItem("unsupported")).MissingReason == "input_unrecognized", "Unknown items are not falsely diagnosed as reagents");
        var budget = new BatchInputs { productId = "dream_dust", raw = new GameItem[0], supplies = new[] { alcohol }, chamber = Mix() };
        Check(AlcoholBudget.Read(budget).NoExtractionMessage == "alcohol_missing_feed", "No precursor is not labelled no extraction needed");
        budget.supplies = new[] { extract };
        Check(AlcoholBudget.Read(budget).NoExtractionMessage == "alcohol_pre_extracted", "Already extracted feed has a separate explanation");
        budget.raw = new[] { dream };
        Check(AlcoholBudget.Read(budget).Applicable, "Real Dream Caps still need extraction");
        budget.productId = "caffeine_pill";
        Check(AlcoholBudget.Read(budget).NoExtractionMessage == "alcohol_missing_feed", "Selecting coffee with mushroom feed must report missing coffee, not unsupported extraction");
        budget.raw = new GameItem[0]; budget.supplies = new[] { Mix("coffee_mush", 10000) };
        Check(AlcoholBudget.Read(budget).Applicable, "Coffee mash supports staged extraction controls");
        budget.supplies = new[] { Mix("caffeine", 10000) };
        Check(AlcoholBudget.Read(budget).NoExtractionMessage == "alcohol_pre_extracted", "Prepared caffeine must not request mash extraction");
        for (int i = 0; i < stock.Length; i++) Check(Parts(stock[i]) == snapshots[i], "Diagnostics preserve every substance");
        Check(DurabilityHelper.GetRealDurability(filter) == 400, "Diagnostics do not spend filter durability");
        // Actual native ingredients + the current planner reproduce the reported
        // no_raw / deliberate zero-potency direct batch, without consuming them.
        var input = new BatchInputs { productId = "dream_dust", raw = new[] { meal }, supplies = new GameItem[0],
            chamber = Mix(), requestedGrade = 99, directForm = false };
        var normalPlanner = new BatchPlanner(input); var search = normalPlanner.Search(); while (search.MoveNext()) { }
        Check(normalPlanner.Error == "no_raw", "Normal planner agrees with edible-cap explanation");
        input.directForm = true;
        var directPlanner = new BatchPlanner(input); search = directPlanner.Search(); while (search.MoveNext()) { }
        Check(directPlanner.Result != null && directPlanner.Result.minimumActive == 0, "Direct forming still permits the intentional 0% batch");
        var crowded = Enumerable.Range(0, 288).Select(i => Cap(true)).ToArray();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) Read(false, crowded);
        watch.Stop();
        return "Input guidance: " + checks + " checks; edible/medicinal/mutated caps, all 7 medicines, wrong mode/product, bottled intermediates, residue, filters, collectors and read-only behavior. 288-item read-only scan average " + (watch.Elapsed.TotalMilliseconds / 20).ToString("0.0") + " ms (headless).";
    }
}
