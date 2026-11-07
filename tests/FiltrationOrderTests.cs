using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class FiltrationOrderTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Filter order: " + message); }
    private static SimulationItem Mix(params object[] contents)
    {
        var item = new SimulationItem();
        for (int i = 0; i < contents.Length; i += 2)
            ChemistryOperations.SetAmount(item, (string)contents[i], (int)contents[i + 1]);
        return item;
    }
    private static SimulationItem Filter(string id = "water_filter", int durability = -1)
    {
        var item = new SimulationItem { identifier = id };
        item.EnableTag("WATER_FILTER");
        int size = id == "kotton_fabric" ? 150 : 22;
        int max = id == "kotton_fabric" ? 1000 : id == "hydroreed_filter" ? 200 : 400;
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(size);
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(key).Enable().SetInt(key.StartsWith("DURABILITY_") && durability >= 0 ? durability : max);
        return item;
    }
    private sealed class NativeFilter : GameItem
    {
        internal NativeFilter(GameItem source) : base(source.identifier)
        {
            foreach (var pair in source.modifiedState.dict) state.dict.Add(pair.Key, pair.Value.Clone());
            modifiedState = state;
            itemFeatures.Add(DurabilityHelper.FeatureDurability());
        }
        public override void Validate() { modifiedState = state; }
        public override void Destroy() { }
    }
    private static BatchInputs Input(GameItem tank, params GameItem[] supplies) => new BatchInputs {
        productId = "dream_dust", chamber = tank, mashBuffer = Mix(), raw = new GameItem[0], supplies = supplies,
        heaterUpgrades = 4, finisherLoss = 0, requestedGrade = 99, maxProducts = 2560 };
    private static void Drain(IEnumerator work)
    { int n = 0; while (work.MoveNext()) Check(++n < 1000000, "planning stalled"); }
    private static BatchPlan Plan(BatchInputs input)
    {
        var snapshot = input.supplies.Select(x => DurabilityHelper.GetRealDurability(x)).ToArray();
        var planner = new BatchPlanner(input); Drain(planner.Search());
        Check(planner.Result != null, "no result: " + planner.Error);
        Check(snapshot.SequenceEqual(input.supplies.Select(x => DurabilityHelper.GetRealDurability(x))), "preview consumed filters");
        var verified = BatchPlanner.Revalidate(input, planner.Result);
        Check(verified != null, "preflight failed");
        Check(verified.fineFilterWear == planner.Result.fineFilterWear && verified.coarseFilterWear == planner.Result.coarseFilterWear,
            "preflight filter wear differs from preview");
        return planner.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        var tank = new SimulationItem(input.chamber);
        var filters = input.supplies.Select(x => new NativeFilter(x)).ToArray();
        foreach (var step in plan.actions)
            switch (step.operation)
            {
                case Operation.React:
                    for (int i = 0; i < step.amount; i++) ChemistryOperations.ReactionTick(tank);
                    break;
                case Operation.Filter:
                    // This branch invokes native HandleFilter/ChangeDurability, not the preview implementation.
                    FiltrationPolicy.Apply(tank, step, i => filters[i]);
                    break;
                case Operation.Dry: ChemistryOperations.SimulateDry(tank); break;
                case Operation.Form: break;
                default: throw new Exception("Unexpected replay step " + step.operation);
            }
        var result = ChemistryOperations.Measure(tank, input.finisherLoss, 0, 0, null, ChemicalRecipe.Get(input.productId), input.maxProducts);
        Check(result.Bottles == plan.expectedBottles && result.MinimumBottleActiveVolume == plan.minimumActive, "native output differs");
        int fine = 0, coarse = 0;
        for (int i = 0; i < filters.Length; i++)
        {
            int wear = Math.Max(0, DurabilityHelper.GetRealDurability(input.supplies[i]) - DurabilityHelper.GetRealDurability(filters[i]));
            if (input.supplies[i].identifier == "kotton_fabric") coarse += wear;
            else fine += wear;
        }
        Check(fine == plan.fineFilterWear && coarse == plan.coarseFilterWear, "native durability differs");
    }
    internal static string Run()
    {
        checks = 0; var notes = new List<string>();
        foreach (string id in new[] { "water_filter", "water_filter_adv", "hydroreed_filter" })
        {
            var tank = Mix("dream_cap_extract", 100000, "plant_matter", 50000, "physical_contaminant", 10000);
            var original = Plan(Input(tank, Filter(id)));
            var input = Input(tank, Filter(id), Filter("kotton_fabric"));
            var plan = Plan(input);
            notes.Add(id + ": fine wear " + original.fineFilterWear + " -> " + plan.fineFilterWear + "; cloth " + plan.coarseFilterWear +
                "; output " + original.expectedBottles + " -> " + plan.expectedBottles + "; purity " + original.minimumActive + " -> " + plan.minimumActive);
            Check(plan.expectedBottles == original.expectedBottles && plan.minimumActive >= 1980, "coarse filtering lost qualified output");
            Check(plan.coarseFilterWear > 0 && plan.fineFilterWear < original.fineFilterWear, "cloth failed to save fine filter");
            Check(input.supplies[plan.actions.First(a => a.operation == Operation.Filter).FilterSources.First()].identifier == "kotton_fabric", "cloth did not run first");
            Replay(input, plan);
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
            var reverse = Plan(Input(tank, Filter("kotton_fabric"), Filter(id)));
            Check(reverse.fineFilterWear == plan.fineFilterWear && reverse.coarseFilterWear == plan.coarseFilterWear && reverse.expectedBottles == plan.expectedBottles,
                "warehouse order changed result");
        }
        var coarseOnly = Input(Mix("dream_cap_extract", 100000, "plant_matter", 50000), Filter(), Filter("kotton_fabric"));
        var coarsePlan = Plan(coarseOnly);
        Check(coarsePlan.coarseFilterWear > 0 && coarsePlan.fineFilterWear == 0, "coarse-only dirt spent cartridge"); Replay(coarseOnly, coarsePlan);
        var fineOnly = Input(Mix("dream_cap_extract", 100000, "physical_contaminant", 10000), Filter(), Filter("kotton_fabric"));
        var finePlan = Plan(fineOnly);
        Check(finePlan.coarseFilterWear == 0 && finePlan.fineFilterWear > 0, "cloth used for particles it cannot filter"); Replay(fineOnly, finePlan);
        var clean = Plan(Input(Mix("dream_cap_extract", 100000), Filter(), Filter("kotton_fabric")));
        Check(!clean.actions.Any(a => a.operation == Operation.Filter) && clean.fineFilterWear == 0 && clean.coarseFilterWear == 0, "already pure batch used filters");
        foreach (int durability in new[] { 0, 1, 20 })
        {
            var partial = Input(Mix("dream_cap_extract", 100000, "plant_matter", 50000, "physical_contaminant", 10000), Filter(), Filter("kotton_fabric", durability));
            var plan = Plan(partial); Replay(partial, plan);
            Check(plan.coarseFilterWear <= durability, "spent more cloth durability than available");
            if (durability == 0) Check(!plan.actions.Any(a => a.operation == Operation.Filter && a.source == 1), "used spent cloth");
        }
        var uv = Input(Mix("dream_cap_extract", 100000, "plant_matter", 50000, "microbe", 10000, "physical_contaminant", 10000),
            Filter(), Filter("kotton_fabric"), Filter("uv_filter"));
        var uvPlan = Plan(uv); Replay(uv, uvPlan);
        Check(uvPlan.actions.Any(a => a.operation == Operation.Filter && a.source == 2), "UV-only microbe removal lost");
        var exhaustedCloths = Input(Mix("dream_cap_extract", 100000, "plant_matter", 50000, "physical_contaminant", 10000),
            Filter(), Filter("kotton_fabric", 40), Filter("kotton_fabric", 60));
        var twoCloths = Plan(exhaustedCloths); Replay(exhaustedCloths, twoCloths);
        Check(twoCloths.coarseFilterWear > 0 && twoCloths.coarseFilterWear <= 100 && twoCloths.fineFilterWear <= 14 && twoCloths.minimumActive >= 1980,
            "multiple cloths did not preserve fine cartridge");

        // Keep the old fine-first alternative where excessive coarse filtering
        // removes filler needed to make another otherwise-qualified unit.
        int retainedFineRoute = 0;
        foreach (int active in new[] { 9700, 23100, 100000 })
        foreach (int grade in new[] { 60, 80, 90, 99 })
        {
            var tank = Mix("dream_cap_extract", active, "plant_matter", 3100, "physical_contaminant", 7100);
            var baselineInput = Input(tank, Filter()); baselineInput.requestedGrade = grade;
            var withCloth = Input(tank, Filter(), Filter("kotton_fabric")); withCloth.requestedGrade = grade;
            var baseline = Plan(baselineInput); var plan = Plan(withCloth);
            Check(plan.actualGrade >= baseline.actualGrade, "cloth caused fallback");
            if (plan.actualGrade == baseline.actualGrade)
            {
                Check(plan.expectedBottles >= baseline.expectedBottles, "cloth reduced yield at " + grade);
                Check((long)plan.minimumActive * 100 >= (long)grade * 2000, "cloth missed target grade " + grade);
                if (plan.expectedBottles == baseline.expectedBottles)
                    Check(plan.fineFilterWear <= baseline.fineFilterWear, "cloth raised fine-filter wear at " + grade);
            }
            if (plan.coarseFilterWear == 0) retainedFineRoute++;
            Replay(withCloth, plan);
        }
        Check(retainedFineRoute > 0, "original route was never preserved in filler-sensitive scenarios");
        notes.Add("Retained fine-only route in " + retainedFineRoute + " filler-sensitive cases");

        var limitedInput = Input(Mix(), Mix("dream_cap_extract", 100000, "plant_matter", 50000, "physical_contaminant", 10000),
            Filter(), Filter("kotton_fabric"));
        var full = Plan(limitedInput);
        var limited = new QuantityPlanning(limitedInput, full, 13); Drain(limited.Search());
        Check(limited.Result != null && limited.Result.expectedBottles == 13, "metered batch failed: " + limited.Error);
        var checkedLimited = BatchPlanner.Revalidate(limitedInput, limited.Result);
        Check(checkedLimited != null && checkedLimited.fineFilterWear == limited.Result.fineFilterWear &&
            checkedLimited.coarseFilterWear == limited.Result.coarseFilterWear, "metered filter estimate differs");
        checks += BatchPersistenceTests.CheckRecipe(limitedInput, limited.Result);
        Check(limited.Result.reagents < full.reagents && limited.Result.coarseFilterWear < full.coarseFilterWear,
            "small batch consumed full-batch supplies");

        var hot = Input(Mix("caffeine", 100000, "plant_matter", 50000, "physical_contaminant", 10000, "water", 100000),
            Filter(), Filter("kotton_fabric"));
        hot.productId = "caffeine_pill"; hot.normalDrying = DirectDryingMethod.Heat;
        var hotPlan = Plan(hot);
        Check(hotPlan.actions.Any(a => a.operation == Operation.BoilDry), "heat selection lost");
        Check(hotPlan.coarseFilterWear > 0 && hotPlan.fineFilterWear > 0, "heat route lost staged filtering");
        checks += BatchPersistenceTests.CheckRecipe(hot, hotPlan);

        // The 0.3.43 regression: full coarse filtering lost the tie while a
        // partial coarse + fine transfer keeps the exact qualified production.
        var partialInput = Input(Mix("dream_cap_extract", 68160, "plant_matter", 42973, "physical_contaminant", 71445),
            Filter(), Filter("kotton_fabric")); partialInput.requestedGrade = 80;
        var partialPlan = Plan(partialInput);
        Check(partialPlan.expectedBottles == 42 && partialPlan.minimumActive >= 1600 && partialPlan.fineFilterWear < 156 && partialPlan.coarseFilterWear > 0,
            "0.3.43 partial-coarse regression");
        Check(partialPlan.actions.First(a => a.operation == Operation.Filter).FilterSources.First() == 1, "required portion was not coarse-filtered first");
        Replay(partialInput, partialPlan);
        checks += BatchPersistenceTests.CheckRecipe(partialInput, partialPlan);
        var selected = PlannedInputs.Read(partialInput, partialPlan);
        Check(selected.Contains(partialInput.supplies[0]) && selected.Contains(partialInput.supplies[1]), "chained filters missing from green highlights");
        notes.Add("Previously missed 80% batch: 42 units; fine wear 156 -> " + partialPlan.fineFilterWear + "; cloth " + partialPlan.coarseFilterWear);
        var bad = partialPlan.Copy(); var chain = bad.actions.First(a => a.operation == Operation.Filter); chain.preFilters = new[] { chain.source };
        bool invalid = false;
        try { BatchPersistence.Deserialize(BatchPersistence.Serialize(new RunningBatch { plan = bad })); } catch { invalid = true; }
        Check(invalid, "duplicate saved filter source accepted");

        foreach (int advancedDurability in new[] { 0, 1, 8, 400 })
        foreach (bool coarse in new[] { false, true })
        {
            var stock = new List<GameItem> { Filter(), Filter("water_filter_adv", advancedDurability) };
            if (coarse) stock.Add(Filter("kotton_fabric"));
            var priority = Input(Mix("dream_cap_extract", 100000, "plant_matter", 50000, "physical_contaminant", 10000), stock.ToArray());
            var plan = Plan(priority);
            var order = plan.actions.Where(a => a.operation == Operation.Filter).SelectMany(a => a.FilterSources).Where(i => i != 2).ToArray();
            Check(order.Length > 0 && order[0] == (advancedDurability > 0 ? 1 : 0), "advanced filter priority wrong");
            Check(advancedDurability < 400 || !order.Contains(0), "ordinary cartridge used despite sufficient advanced durability");
            if (advancedDurability > 0 && advancedDurability < 10) Check(order.Contains(0), "ordinary backup not used after advanced exhaustion");
            Replay(priority, plan); checks += BatchPersistenceTests.CheckRecipe(priority, plan);
        }

        // Independent native transfers: isolate the aliquot, filter through each
        // real native filter, and return only the retained contents.
        foreach (int amount in new[] { 1, 9128, 50000, 182578 })
        foreach (int clothDur in new[] { 1, 1000 })
        {
            var tank = Mix("dream_cap_extract", 68160, "plant_matter", 42973, "physical_contaminant", 71445);
            var manual = new SimulationItem(tank);
            var cf = Filter("kotton_fabric", clothDur); var ff = Filter();
            var nativeCloth = new NativeFilter(cf); var nativeFine = new NativeFilter(ff);
            var withdrawn = WaterHelper.SplitProportionally(ChemistryOperations.Parts(manual), amount);
            var retained = (int[])withdrawn.Clone();
            foreach (var filter in new[] { nativeCloth, nativeFine })
                for (int i = 0; i < retained.Length; i++) retained[i] = WaterHelper.HandleFilter(Liquid.Liquids[i].identifier, retained[i], manual, filter);
            for (int i = 0; i < retained.Length; i++) ChemistryOperations.SetAmount(manual, Liquid.Liquids[i].identifier,
                ChemistryOperations.Amount(manual, Liquid.Liquids[i].identifier) - withdrawn[i] + retained[i]);
            ChemistryOperations.FilterAliquot(tank, new GameItem[] { cf, ff }, amount);
            Check(ChemistryOperations.Parts(manual).SequenceEqual(ChemistryOperations.Parts(tank)), "native chained transfer composition differs");
            Check(DurabilityHelper.GetRealDurability(cf) == DurabilityHelper.GetRealDurability(nativeCloth) &&
                DurabilityHelper.GetRealDurability(ff) == DurabilityHelper.GetRealDurability(nativeFine), "native chained transfer wear differs");
        }
        return "PASS: " + checks + " filtration checks\n" + string.Join("\n", notes);
    }
}
