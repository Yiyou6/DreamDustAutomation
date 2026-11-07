using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

public static class ChemistryRegressionTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static SimulationItem Mixture(params object[] contents)
    {
        var item = new SimulationItem();
        for (int i = 0; i < contents.Length; i += 2)
            ChemistryOperations.SetAmount(item, (string)contents[i], (int)contents[i + 1]);
        return item;
    }
    private static SimulationItem Filter()
    {
        var filter = new SimulationItem();
        filter.identifier = "water_filter";
        filter.EnableTag("WATER_FILTER");
        filter.ModifyTag("FILTER_SIZE_TAG", s => s.Enable().SetInt(22));
        foreach (string key in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            filter.ModifyTag(key, s => s.Enable().SetInt(400));
        return filter;
    }
    private static string Snapshot(GameItem item) => string.Join(",", ChemistryOperations.Parts(item)) + ":" +
        ChemistryOperations.Temperature(item).ToString("R") + ":" + DurabilityHelper.GetRealDurability(item);

    // Exercise the live-filter branch with the game's actual HandleFilter and
    // ChangeDurability methods, keeping only the rendering out of this fixture.
    private sealed class NativeFilterProbe : GameItem
    {
        internal NativeFilterProbe(GameItem source) : base(source.identifier)
        {
            foreach (var pair in source.modifiedState.dict) state.dict.Add(pair.Key, pair.Value.Clone());
            modifiedState = state;
            itemFeatures.Add(DurabilityHelper.FeatureDurability());
        }
        public override void Validate() { modifiedState = state; }
        public override void Destroy() { }
    }

    private static void CheckNativeFilterWear(List<string> summaries)
    {
        int cases = 0;
        foreach (string id in new[] { "water_filter", "kotton_fabric", "water_filter_adv" })
        foreach (int durability in new[] { 0, 1, 400 })
        foreach (int volume in new[] { 0, 1, 1000, 12501 })
        foreach (var liquid in Liquid.Liquids)
        {
            var simulatedFilter = Filter();
            simulatedFilter.identifier = id;
            simulatedFilter.state.GetTag(DurabilityHelper.DURABILITY_TAG).SetInt(durability);
            simulatedFilter.state.GetTag(DurabilityHelper.DURABILITY_FAKE_TAG).SetInt(durability);
            var liveFilter = new NativeFilterProbe(simulatedFilter);
            var simulatedTank = Mixture(liquid.identifier, volume);
            var liveTank = new SimulationItem(simulatedTank);
            ChemistryOperations.FilterAliquot(simulatedTank, simulatedFilter, volume);
            ChemistryOperations.FilterAliquot(liveTank, liveFilter, volume);
            Check(ChemistryOperations.Parts(simulatedTank).SequenceEqual(ChemistryOperations.Parts(liveTank)),
                "Native filtration differs from preview for " + id + "/" + liquid.identifier);
            Check(DurabilityHelper.GetRealDurability(liveFilter) == DurabilityHelper.GetRealDurability(simulatedFilter),
                "Actual filter wear differs from preview for " + id + "/" + liquid.identifier);
            Check(DurabilityHelper.GetFakeDurability(liveFilter) == DurabilityHelper.GetFakeDurability(simulatedFilter),
                "Displayed filter durability differs from preview");
            if (liquid.identifier == "plant_matter" && durability > 0 && volume > 0)
                Check(DurabilityHelper.GetRealDurability(liveFilter) < durability, "Native filtering removed plant matter without wear");
            cases++;
        }
        summaries.Add("Native filter wear agrees with preview: " + cases + " cases");
    }

    private static void CheckWasteCollection(List<string> summaries)
    {
        var empty = new SimulationItem { identifier = "med_bottle_red" };
        Check(WasteCollection.Accepts(empty), "Empty reagent bottles belong in the waste bin");
        foreach (var liquid in Liquid.Liquids)
        {
            var remainder = new SimulationItem(empty);
            ChemistryOperations.SetAmount(remainder, liquid.identifier, 1);
            Check(!WasteCollection.Accepts(remainder), "Even one microlitre of " + liquid.identifier + " must keep a bottle in the input bin");
        }
        foreach (string id in new[] { "water_tank", "water_jug", "bottled_water" })
        {
            var vessel = new SimulationItem(empty) { identifier = id };
            if (id == "water_tank") vessel.EnableTag("WATER_TANK_TAG");
            Check(WasteCollection.Accepts(vessel), "Empty input water vessels should move to waste: " + id);
            foreach (string part in new[] { "water", "physical_contaminant", "dream_cap_extract" })
            {
                ChemistryOperations.SetAmount(vessel, part, 1);
                Check(!WasteCollection.Accepts(vessel), "Water vessel with remaining contents must stay in input");
                ChemistryOperations.SetAmount(vessel, part, 0);
            }
            vessel.ModifyTag(BatchInputReservation.SourceTag, t => t.Enable().SetInt(0));
            var held = new BatchInputReservation(); held.Capture(new[] { vessel });
            var pouring = new RunningBatch { plan = new BatchPlan { actions = new[] {
                new BatchAction(Operation.Pour, 0, 1), new BatchAction(Operation.Dry) } } };
            Check(!WasteCollection.CanCollect(vessel, held, pouring), "A pending pour still owns its water vessel");
            pouring.step = 1;
            Check(WasteCollection.CanCollect(vessel, held, pouring), "An emptied water vessel is released after its last pour");
        }
        Check(!WasteCollection.Accepts(new SimulationItem(empty) { identifier = "water_pitcher" }), "Pitchers remain excluded from automatic waste collection");
        var solid = new SimulationItem(empty) { identifier = "organic_waste" };
        solid.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        Check(!WasteCollection.Accepts(solid), "Raw solids and residues are not collected as trash");
        var finished = new SimulationItem(empty) { identifier = "dream_dust" };
        finished.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
        Check(!WasteCollection.Accepts(finished), "Chemical products must never enter the waste bin");
        var filterHousing = new SimulationItem(empty);
        filterHousing.EnableTag("FILTER_CONTAINER_TAG");
        Check(!WasteCollection.Accepts(filterHousing), "A container with an embedded filter is not an empty reagent bottle");

        foreach (string id in new[] { "water_filter", "water_filter_adv", "hydroreed_filter" })
        {
            var filter = Filter(); filter.identifier = id;
            filter.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
            Check(!WasteCollection.Accepts(filter), "Usable filters must stay in the input bin");
            filter.ModifyTag(DurabilityHelper.DURABILITY_TAG, t => t.SetInt(1));
            Check(!WasteCollection.Accepts(filter), "A filter with one durability left is not spent");
            filter.ModifyTag(DurabilityHelper.DURABILITY_TAG, t => t.SetInt(0));
            Check(WasteCollection.Accepts(filter) == (id == "water_filter" || id == "kotton_fabric" || id == "hydroreed_filter"),
                "Only spent disposable filters may be collected; regenerating filters must stay even at zero durability");
        }
        var unusedReservation = new BatchInputReservation();
        foreach (string id in new[] { "water_filter_adv", "uv_filter", "other_mod.reusable_filter" })
        foreach (int durability in new[] { 0, 1, 50, 400 })
        {
            var reusable = Filter(); reusable.identifier = id;
            reusable.ModifyTag(DurabilityHelper.DURABILITY_TAG, t => t.SetInt(durability));
            Check(WasteCollection.ProtectedFilter(reusable), "Reusable filters already in waste must be eligible for return at any durability");
            Check(!WasteCollection.Accepts(reusable) && !WasteCollection.CanCollect(reusable, unusedReservation, null),
                "Reusable and unknown filters must never be collected, even at zero durability");
        }
        Check(!WasteCollection.ProtectedFilter(empty) && !WasteCollection.ProtectedFilter(Filter()) &&
            !WasteCollection.ProtectedFilter(null), "Return migration must not move ordinary waste or empty bottles back to inputs");
        var uv = Filter(); uv.identifier = "uv_filter";
        uv.ModifyTag(DurabilityHelper.DURABILITY_TAG, t => t.SetInt(0));
        Check(!WasteCollection.Accepts(uv), "Reusable UV filters must not be mistaken for depleted cartridges");
        var noDurability = Filter(); noDurability.DisableTag(DurabilityHelper.DURABILITY_TAG);
        Check(!WasteCollection.Accepts(noDurability), "An absent durability tag must not imply a spent filter");

        var reservation = new BatchInputReservation();
        empty.ModifyTag(BatchInputReservation.SourceTag, t => t.Enable().SetInt(2));
        var spent = Filter(); spent.identifier = "hydroreed_filter";
        spent.ModifyTag(DurabilityHelper.DURABILITY_TAG, t => t.SetInt(0));
        spent.ModifyTag(BatchInputReservation.SourceTag, t => t.Enable().SetInt(3));
        reservation.Capture(new GameItem[] { empty, spent });
        var batch = new RunningBatch
        {
            plan = new BatchPlan { productId = "dream_dust", requestedGrade = 90, actualGrade = 90,
                expectedBottles = 1, minimumActive = 1800, actions = new[] {
                    new BatchAction(Operation.Pour, 2, 1), new BatchAction(Operation.Filter, 3, 1),
                    new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 1) } }
        };
        Check(!WasteCollection.CanCollect(empty, reservation, batch), "An empty bottle still referenced by a pending pour must stay reserved");
        Check(!WasteCollection.CanCollect(spent, reservation, batch), "A spent filter still referenced by the plan must stay reserved");
        batch.step = 1;
        Check(WasteCollection.CanCollect(empty, reservation, batch), "The bottle may leave immediately after its last pour");
        Check(!WasteCollection.CanCollect(spent, reservation, batch), "Current filtering action must retain its filter identity");
        batch.step = 2;
        var reloaded = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
        Check(WasteCollection.CanCollect(spent, reservation, reloaded), "A used filter may be collected after save/resume");
        string contents = Snapshot(empty);
        int count = empty.unitCount;
        reservation.Release(empty);
        Check(!reservation.Contains(empty) && !empty.IsTag(BatchInputReservation.SourceTag), "Collected objects must not keep stale source indices");
        Check(empty.unitCount == count && Snapshot(empty) == contents, "Releasing a collected source must not destroy or modify its contents");
        reservation.Restore(new GameItem[] { empty, spent });
        Check(!reservation.Contains(empty) && reservation.Contains(spent), "Reload must not relock an already collected bottle");
        batch.failed = true; batch.step = 0;
        Check(!WasteCollection.CanCollect(spent, reservation, batch), "Failed batches must retain any supplies needed by remaining actions");
        Check(WasteCollection.CanCollect(spent, reservation, null), "Stopping a batch releases spent filters for collection");
        Check(!WasteCollection.Accepts(null), "Waste classification must reject a missing item");
        summaries.Add("Waste collection: empty input bottles/tanks/jugs and spent disposable filters; regenerating/UV/unknown filters and pitchers protected; residues and future batch sources preserved; source release and save/resume verified");
    }

    private static ProductionOutcome ReferenceMeasure(GameItem input, int loss, ChemicalRecipe recipe = null)
    {
        recipe = recipe ?? ChemicalRecipe.Get();
        var batch = new SimulationItem(input);
        ChemistryOperations.SimulateDry(batch);
        int count = 0, active = 0, minimum = recipe.UnitVolume;
        while (ChemistryOperations.Total(batch) >= recipe.UnitVolume)
        {
            var bottle = new SimulationItem();
            ChemistryOperations.TransferToProduct(batch, bottle, recipe.UnitVolume);
            MachineChemFinisher.IntroduceImpurity(bottle, loss);
            int part = ChemistryOperations.Amount(bottle, recipe.Active);
            active += part;
            minimum = Math.Min(minimum, part);
            count++;
        }
        return new ProductionOutcome(active, count * recipe.UnitVolume, count, count == 0 ? 0 : minimum, unitVolume: recipe.UnitVolume);
    }

    private static BatchPlanner Calculate(BatchInputs inputs, out string timing)
    {
        var planner = new BatchPlanner(inputs);
        var search = planner.Search();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double longest = 0;
        int yields = 0;
        while (true)
        {
            long start = clock.ElapsedTicks;
            bool more = search.MoveNext();
            longest = Math.Max(longest, (clock.ElapsedTicks - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (!more) break;
            if (++yields > 100000) throw new Exception("Formula planner failed to terminate");
        }
        timing = clock.Elapsed.TotalMilliseconds.ToString("F1") + " ms total; " + longest.ToString("F1") + " ms longest step";
        return planner;
    }

    private static void CheckFantaContaminantSelection(List<string> summaries)
    {
        // Two precursor bottles from the reported save: the slightly stronger
        // one contains persistent chemical contamination, the other mostly
        // filterable particles. More stock must not hide the cleaner route.
        var dirty = Mixture("methylol", 120862, "stabilizing_agent", 120863,
            "physical_contaminant", 411, "heavy_metal", 368, "chemical_contaminant", 7454, "microbe", 42);
        var clean = Mixture("methylol", 120037, "stabilizing_agent", 120038,
            "physical_contaminant", 8207, "heavy_metal", 1165, "chemical_contaminant", 120, "microbe", 433);
        var auxiliaries = new GameItem[] {
            Mixture("reducing_agent", 242400, "physical_contaminant", 3875, "microbe", 358, "heavy_metal", 826, "chemical_contaminant", 2541),
            Mixture("acid", 237335, "physical_contaminant", 742, "heavy_metal", 795, "chemical_contaminant", 4634),
            Mixture("base", 241866, "physical_contaminant", 1234, "heavy_metal", 76, "chemical_contaminant", 3951), Filter() };
        Func<GameItem[], BatchInputs> inputsFor = stock => new BatchInputs { productId = "fanta_pill", raw = new GameItem[0],
            supplies = stock, chamber = new SimulationItem(), heaterUpgrades = 4, finisherLoss = 0, requestedGrade = 99 };
        var reference = Calculate(inputsFor(new GameItem[] { clean }.Concat(auxiliaries).ToArray()), out var referenceTiming);
        Check(reference.Result != null && reference.Result.actualGrade >= 60, "Clean precursor reference must reach 60%");
        foreach (bool reverse in new[] { false, true })
        {
            var stock = (reverse ? new GameItem[] { clean, dirty } : new GameItem[] { dirty, clean }).Concat(auxiliaries).ToArray();
            var before = stock.Select(Snapshot).ToArray();
            var inputs = inputsFor(stock);
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.actualGrade >= reference.Result.actualGrade,
                "Persistent contamination was mistaken for removable particles: extra stock hid the feasible 60% route");
            Check(planner.Result.expectedBottles >= reference.Result.expectedBottles, "Clean route lost qualified output");
            Check(planner.Result.actions.Where(x => x.operation == Operation.Pour).All(x => x.source != (reverse ? 1 : 0)),
                "Contaminated precursor must remain outside this higher-grade batch");
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Filtered feed selection failed native replay");
            Check(stock.Select(Snapshot).SequenceEqual(before), "Ranking consumed live stock or filter durability");
            // Partial-feed boundary probes add bounded work to the original
            // whole/single route comparison; retain a regression ceiling.
            Check(planner.CandidatesChecked < 400, "Filtered feed selection expanded: " + planner.CandidatesChecked + "; " + timing);
            summaries.Add("Mixed Fanta precursors: " + planner.Result.actualGrade + "%, " + planner.Result.expectedBottles +
                " units, " + planner.CandidatesChecked + " candidates; " + timing);
        }
    }

    private static void CheckSelectionAlternatives(List<string> summaries)
    {
        var failures = new List<string>();
        int cases = 0;
        Action<string, string, GameItem, GameItem[]> compare = (label, id, tank, stock) => {
            var referenceInputs = new BatchInputs { productId = id, raw = new GameItem[0], supplies = stock.Skip(1).ToArray(),
                chamber = tank, requestedGrade = 99, heaterUpgrades = 4 };
            var reference = Calculate(referenceInputs, out var referenceTiming);
            Check(reference.Result != null, "Selection reference has no usable route: " + label);
            foreach (bool reverse in new[] { false, true })
            {
                var supplies = reverse ? stock.AsEnumerable().Reverse().ToArray() : stock;
                var before = supplies.Select(Snapshot).ToArray();
                var inputs = new BatchInputs { productId = id, raw = new GameItem[0], supplies = supplies,
                    chamber = tank, requestedGrade = 99, heaterUpgrades = 4 };
                var planner = Calculate(inputs, out var timing);
                cases++;
                if (planner.Result == null || planner.Result.actualGrade < reference.Result.actualGrade ||
                    (planner.Result.actualGrade == reference.Result.actualGrade && planner.Result.expectedBottles < reference.Result.expectedBottles))
                    failures.Add(label + (reverse ? " reversed" : "") + ": mixed=" + planner.Result?.actualGrade + "%/" +
                        planner.Result?.expectedBottles + ", cleaner-only=" + reference.Result.actualGrade + "%/" + reference.Result.expectedBottles);
                if (planner.Result != null) Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Selection route failed replay: " + label);
                Check(supplies.Select(Snapshot).SequenceEqual(before), "Selection comparison consumed real inputs: " + label);
            }
        };
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            int active = recipe.UnitVolume * 5;
            compare("Filtered feed " + id, id, new SimulationItem(), new GameItem[] {
                Mixture(recipe.Active, active, "chemical_contaminant", active / 20),
                Mixture(recipe.Active, active, "physical_contaminant", active / 10), Filter() });
            compare("Treatable feed " + id, id, new SimulationItem(), new GameItem[] {
                Mixture(recipe.Active, active, "chemical_contaminant", active / 8),
                Mixture(recipe.Active, active, "protein", active / 5), Mixture("acid", active), Filter() });
            foreach (string reagent in new[] { "acid", "base" })
                compare("Filtered " + reagent + " " + id, id, Mixture(recipe.Active, active, "protein", active * 10), new GameItem[] {
                    Mixture(reagent, active * 30, "chemical_contaminant", active * 9 / 10),
                    Mixture(reagent, active * 30, "physical_contaminant", active * 18 / 10), Filter(), Filter(), Filter() });
        }
        foreach (string id in new[] { "dream_dust", "nightmare_dust", "caffeine_pill", "pink_alt_injector" })
        {
            string mush = ChemicalRecipe.Get(id).Mush;
            compare("Filtered precursor " + id, id, new SimulationItem(), new GameItem[] {
                Mixture(mush, 40000, "chemical_contaminant", 2000), Mixture(mush, 40000, "physical_contaminant", 4000),
                Mixture("alcohol", 500000), Mixture("reducing_agent", 100000), Mixture("lipobond_agent", 100000), Filter(), Filter() });
            compare("Filtered alcohol " + id, id, Mixture(mush, 40000), new GameItem[] {
                Mixture("alcohol", 500000, "chemical_contaminant", 15000), Mixture("alcohol", 500000, "physical_contaminant", 30000),
                Mixture("reducing_agent", 100000), Mixture("lipobond_agent", 100000), Filter(), Filter() });
        }
        compare("Filtered reducer Fanta", "fanta_pill", Mixture("methylol", 120000, "stabilizing_agent", 120000), new GameItem[] {
            Mixture("reducing_agent", 300000, "chemical_contaminant", 9000), Mixture("reducing_agent", 300000, "physical_contaminant", 18000),
            Mixture("acid", 500000), Mixture("base", 500000), Filter(), Filter() });
        compare("Filtered lipobond", "dream_dust", Mixture("dream_cap_extract", 10000, "fat", 80000), new GameItem[] {
            Mixture("lipobond_agent", 100000, "chemical_contaminant", 3000), Mixture("lipobond_agent", 100000, "physical_contaminant", 6000), Filter(), Filter() });
        compare("Equal feeds limited by one filter", "dream_dust", new SimulationItem(), new GameItem[] {
            Mixture("dream_cap_extract", 10000, "physical_contaminant", 150000),
            Mixture("dream_cap_extract", 10000, "physical_contaminant", 150000), Filter() });
        var smallTank = new SimulationItem();
        smallTank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(60000);
        compare("Solvent strength with limited tank", "dream_dust", smallTank, new GameItem[] {
            Mixture("alcohol", 70000, "water", 30000), Mixture("alcohol", 100000), Mixture("dream_cap_mush", 10000), Filter() });
        compare("Cleaner dilute solvent with limited tank", "dream_dust", smallTank, new GameItem[] {
            Mixture("alcohol", 97000, "chemical_contaminant", 3000), Mixture("alcohol", 50000, "water", 47000, "physical_contaminant", 3000),
            Mixture("dream_cap_mush", 10000), Filter() });
        var reactionTank = new SimulationItem();
        reactionTank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(20000);
        compare("Cleaner dilute acid needs extra tank room", "dream_dust", reactionTank, new GameItem[] {
            Mixture("acid", 100000, "chemical_contaminant", 3000), Mixture("acid", 50000, "water", 47000, "physical_contaminant", 3000),
            Mixture("dream_cap_extract", 9000, "protein", 9000), Filter() });
        summaries.Add("Selection alternative comparisons: " + cases + " cases");
        Check(failures.Count == 0, "Selection alternatives lost feasible routes:\n" + string.Join("\n", failures));

        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool exhausted in new[] { false, true })
        {
            var recipe = ChemicalRecipe.Get(id);
            var filter = Filter();
            if (exhausted) filter.state.GetTag("DURABILITY_TAG").SetInt(0);
            else filter.state.GetTag("FILTER_SIZE_TAG").SetInt(5);
            var inputs = new BatchInputs { productId = id, raw = new GameItem[0], chamber = new SimulationItem(), requestedGrade = 99,
                supplies = new GameItem[] { Mixture(recipe.Active, 10000, "chemical_contaminant", 500),
                    Mixture(recipe.Active, 10000, "physical_contaminant", 1000), filter } };
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.actualGrade == 90, "Unusable filter invented a high-grade route: " + id);
            Check(!planner.Result.actions.Any(a => a.operation == Operation.Filter), "Exhausted or active-removing filter was used");
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Unusable-filter route failed replay");
        }
        var mixedStock = Enumerable.Range(0, 24).Select(i => (GameItem)(i % 2 == 0 ?
            Mixture("dream_cap_mush", 20000, "chemical_contaminant", 1000) :
            Mixture("dream_cap_mush", 20000, "protein", 3000))).Concat(new GameItem[] {
                Mixture("alcohol", 3000000, "chemical_contaminant", 90000), Mixture("alcohol", 3000000, "physical_contaminant", 180000),
                Mixture("acid", 300000, "chemical_contaminant", 9000), Mixture("acid", 300000, "physical_contaminant", 18000) })
            .Concat(Enumerable.Range(0, 8).Select(i => (GameItem)Filter())).ToArray();
        var mixedInputs = new BatchInputs { raw = new GameItem[0], supplies = mixedStock, chamber = new SimulationItem(),
            requestedGrade = 99, heaterUpgrades = 4 };
        var mixed = Calculate(mixedInputs, out var mixedTiming);
        Check(mixed.Result != null && mixed.Result.actualGrade == 99, "Large mixed-stock batch lost the clean route");
        Check(BatchPlanner.Revalidate(mixedInputs, mixed.Result) != null, "Large mixed-stock preview failed native replay");
        Check(mixed.CandidatesChecked < 500, "Mixed-stock selection regressed to broad enumeration");
        summaries.Add("24 mixed feeds and mixed reagents: " + mixed.Result.actualGrade + "%, " + mixed.Result.expectedBottles +
            " units, " + mixed.CandidatesChecked + " candidates; " + mixedTiming);
    }

    private static void CheckExactBottling()
    {
        var random = new Random(47014);
        string[] solids = { "dream_cap_extract", "physical_contaminant", "protein", "mineral", "plant_matter" };
        for (int sample = 0; sample < 60; sample++)
        {
            var batch = new SimulationItem();
            foreach (string id in solids) ChemistryOperations.SetAmount(batch, id, random.Next(0, sample < 10 ? 500 : 20000));
            ChemistryOperations.SetAmount(batch, "water", random.Next(100000));
            ChemistryOperations.SetAmount(batch, "alcohol", random.Next(100000));
            string original = Snapshot(batch);
            foreach (int loss in new[] { 0, 1, 3, 6, 12 })
            {
                var expected = ReferenceMeasure(batch, loss);
                var actual = ChemistryOperations.Measure(batch, loss, 0, 0, null);
                Check(actual.Bottles == expected.Bottles, "Numerical bottle count differs from native transfer");
                Check(actual.ActiveVolume == expected.ActiveVolume, "Numerical active yield differs from native finisher");
                Check(actual.MinimumBottleActiveVolume == expected.MinimumBottleActiveVolume, "Numerical grade differs at rounding boundary");
                Check(actual.TotalVolume == expected.TotalVolume, "Numerical bottled volume differs");
                Check(Snapshot(batch) == original, "Numerical measurement mutated input");
            }
        }
    }

    private static void CheckReactionCountdown()
    {
        var plan = new BatchPlan { actions = new[] {
            new BatchAction(Operation.Blend), new BatchAction(Operation.Extract, amount: 20),
            new BatchAction(Operation.Pour, amount: 100000), new BatchAction(Operation.React, amount: 7),
            new BatchAction(Operation.Filter, amount: 100000), new BatchAction(Operation.Dry),
            new BatchAction(Operation.Form, amount: 40) } };
        Check(ReactionProgress.RemainingSeconds(plan, 0, 0) == 27, "Preview must count reaction seconds only");
        for (int elapsed = 0; elapsed <= 20; elapsed++)
            Check(ReactionProgress.RemainingSeconds(plan, 1, elapsed) == 27 - elapsed, "Countdown must decrease each actual extraction tick");
        Check(ReactionProgress.RemainingSeconds(plan, 2, 0) == 7, "Immediate steps must retain upcoming reaction time");
        for (int elapsed = 0; elapsed <= 7; elapsed++)
            Check(ReactionProgress.RemainingSeconds(plan, 3, elapsed) == 7 - elapsed, "Later reaction countdown must not reset to batch total");
        Check(ReactionProgress.RemainingSeconds(plan, 5, 0) == 0, "Evaporation cycles are not seconds");
        Check(ReactionProgress.RemainingSeconds(plan, 6, 0) == 0, "Bottle count must not become a time estimate");
        Check(ReactionProgress.RemainingSeconds(plan, 7, 0) == 0, "Completed batch must have no reaction time left");
        Check(ReactionProgress.RemainingSeconds(plan, 1, 999) == 7, "Elapsed overrun must not consume a later stage's time");
        Check(ReactionProgress.RemainingSeconds(plan, 3, -1) == 7, "Countdown must clamp invalid negative elapsed time");
        // Loading a persisted step/elapsed pair must give exactly the same clock;
        // repeated reads while paused cannot advance processing or the display.
        int savedStep = 3, savedElapsed = 4;
        for (int i = 0; i < 5; i++)
            Check(ReactionProgress.RemainingSeconds(plan.Copy(), savedStep, savedElapsed) == 3, "Paused/restored clock must follow saved progress");
    }

    private static void CheckEvaporationEstimate()
    {
        var machine = new SimulationItem();
        var tank = Mixture("water", 300000, "alcohol", 1, "dream_cap_extract", 10000);
        string before = Snapshot(tank);
        var estimate = EvaporationEstimate.Read(tank, machine);
        Check(estimate.Volume == 300001 && estimate.DailyVolume == 300000 && estimate.Days == 2,
            "Mixed volatile liquid rounds up to the required overnight cycles; dry extract is excluded");
        ChemistryOperations.SetAmount(tank, "alcohol", 0);
        Check(EvaporationEstimate.Read(tank, machine).Days == 1, "Exact cycle boundary must not add an extra night");
        machine.ModifyTag("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", s => s.Enable().SetInt(100));
        ChemistryOperations.SetAmount(tank, "water", 600001);
        estimate = EvaporationEstimate.Read(tank, machine);
        Check(estimate.DailyVolume == 600000 && estimate.Days == 2, "Current performance upgrade affects the estimate");
        ChemistryOperations.SetAmount(tank, "water", 1);
        Check(EvaporationEstimate.Read(tank, machine).Days == 1, "Even one microlitre needs one overnight tick");
        machine.ModifyTag("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", s => s.SetInt(-100));
        Check(EvaporationEstimate.Read(tank, machine).Days == null, "Zero speed must not promise a completion day");
        machine.ModifyTag("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", s => s.SetInt(-200));
        Check(EvaporationEstimate.Read(tank, machine).DailyVolume == 0, "Negative speed uses the native zero clamp");
        ChemistryOperations.SetAmount(tank, "water", 0);
        Check(EvaporationEstimate.Read(tank, machine).Days == 0, "A dry tank needs no more nights even at zero speed");
        Check(EvaporationEstimate.Read(null, machine).Days == 0, "Missing tank does not invent evaporation time");
        tank = Mixture("water", 300000, "alcohol", 1, "dream_cap_extract", 10000);
        EvaporationEstimate.Read(tank, machine);
        Check(Snapshot(tank) == before, "Reading ETA must not evaporate or mutate anything");
        Check(EvaporationEstimate.Read(tank, null).Days == null, "Missing evaporator cannot promise completion");
        foreach (int bonus in new[] { -99, -50, 0, 1, 33, 75, 100, 250, 333 })
        {
            machine.ModifyTag("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", s => s.SetInt(bonus));
            ChemistryOperations.SetAmount(tank, "water", 20000000);
            estimate = EvaporationEstimate.Read(tank, machine);
            int remaining = estimate.Volume, cycles = 0;
            // Independent native nightly quota loop checks the ceiling and float
            // rounding against a full tank, including performance penalties.
            while (remaining > 0)
            {
                int quota = (int)Math.Round(300000 * Math.Max(0f, 1f + (float)bonus / 100f));
                remaining -= Math.Min(remaining, quota); cycles++;
            }
            Check(estimate.Days == cycles, "ETA differs from repeated nightly quotas at bonus " + bonus);
        }
    }

    private static void CheckAllRecipes(List<string> summaries)
    {
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var mixture = Mixture(recipe.Active, recipe.UnitVolume * 19, "physical_contaminant", recipe.UnitVolume,
                "water", 10000, "alcohol", 30000);
            foreach (int loss in new[] { 0, 1, 3, 6, 12 })
            {
                var reference = ReferenceMeasure(mixture, loss, recipe);
                var actual = ChemistryOperations.Measure(mixture, loss, 0, 0, null, recipe);
                Check(actual.Bottles == 20 && actual.Bottles == reference.Bottles, "Native unit size differs for " + id);
                Check(actual.MinimumBottleActiveVolume == reference.MinimumBottleActiveVolume, "Native product concentration differs for " + id);
                Check(actual.ActiveVolume == reference.ActiveVolume, "Native active yield differs for " + id);
                Check(GradePolicy.Meets(actual, 80), "Grade policy must use this product's unit volume: " + id);
            }
            var inputs = new BatchInputs { productId = id, raw = new GameItem[0], chamber = new SimulationItem(),
                supplies = new GameItem[] { mixture }, requestedGrade = 90, finisherLoss = 0 };
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.productId == id, "Auto-detection of active feedstock failed for " + id);
            Check(planner.Result.actualGrade == 90 && planner.Result.expectedBottles == 20, "Direct compound plan differs for " + id);
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Direct compound replay failed for " + id);
            checks += BatchPersistenceTests.CheckRecipe(inputs, planner.Result);
        }

        foreach (string id in new[] { "nightmare_dust", "caffeine_pill", "fanta_pill", "pink_alt_injector" })
        {
            var recipe = ChemicalRecipe.Get(id);
            var feed = id == "fanta_pill" ? Mixture("methylol", 125000, "stabilizing_agent", 125000) :
                Mixture(recipe.Mush, 90000, "water", 10000, "protein", 3700, "physical_contaminant", 2000);
            if (id != "fanta_pill") feed.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var auxiliaries = new List<GameItem> { Mixture("alcohol", 700000, "water", 300000),
                Mixture("acid", 250000), Mixture("base", 250000), Mixture("reducing_agent", 250000),
                Mixture("lipobond_agent", 150000), Filter(), Filter() };
            if (id == "fanta_pill") auxiliaries.Insert(0, feed);
            // Unrelated ingredients must stay in the common bin.
            var unrelated = Mixture("nightmare_cap_mush", 5000);
            unrelated.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var inputs = new BatchInputs { productId = id,
                raw = id == "fanta_pill" ? new GameItem[] { unrelated } : new GameItem[] { feed },
                supplies = auxiliaries.ToArray(), chamber = new SimulationItem(),
                blenderLoss = 35, heaterUpgrades = 1, finisherLoss = 12, requestedGrade = 90 };
            var sourceItems = inputs.raw.Concat(inputs.supplies).ToArray();
            var snapshots = sourceItems.Select(Snapshot).ToArray();
            var planner = Calculate(inputs, out var timing);
            if (planner.Result == null)
            {
                var values = (Dictionary<string, ProductionOutcome>)typeof(BatchPlanner).GetField("measurementCache",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(planner);
                throw new Exception("Full synthesis failed for " + id + ": " + planner.Error + "; measurements: " +
                    string.Join("; ", values.Select(x => x.Value.ActiveVolume + "/" + x.Value.TotalVolume + " min=" + x.Value.MinimumBottleActiveVolume + " parts=" +
                        string.Join(",", x.Key.Split(',').Select((v, n) => Liquid.Liquids[n].identifier + "=" + v).Where(v => !v.EndsWith("=0"))))));
            }
            Check(true, "Synthesis produced a plan");
            Check(planner.Result.productId == id && planner.Result.actualGrade == 80, "Unexpected product/grade for " + id);
            Check(planner.Result.expectedBottles > 0, "Synthesis produced nothing for " + id);
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Full synthesis replay failed for " + id);
            checks += BatchPersistenceTests.CheckRecipe(inputs, planner.Result);
            foreach (double temperature in new[] { 24.0, 25.5, 28.0 })
            {
                var drifted = new BatchInputs { productId = id, raw = inputs.raw.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
                    supplies = inputs.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(), chamber = new SimulationItem(inputs.chamber),
                    blenderLoss = inputs.blenderLoss, heaterUpgrades = inputs.heaterUpgrades, finisherLoss = inputs.finisherLoss, requestedGrade = inputs.requestedGrade };
                foreach (var item in drifted.raw.Concat(drifted.supplies).Concat(new[] { drifted.chamber }))
                    item.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(temperature);
                Check(BatchPlanner.Revalidate(drifted, planner.Result) != null, "Normal temperature drift broke the selected " + id + " plan");
            }
            for (int i = 0; i < sourceItems.Length; i++) Check(Snapshot(sourceItems[i]) == snapshots[i], "Synthesis preview consumed inputs");
            if (id == "fanta_pill")
            {
                Check(planner.Result.actions.All(x => x.operation != Operation.Blend), "Unrelated mushroom must not be consumed");
                inputs.heaterUpgrades = 0;
                var unavailable = Calculate(inputs, out var ignored);
                Check(unavailable.Result == null && unavailable.Error == "heater_upgrade_required", "Native heater upgrade requirement was bypassed");
            }
            if (id == "pink_alt_injector")
                Check(planner.Result.actions.Count(x => x.operation == Operation.Dry) == 2, "Multi-stage recipe must retain its intermediate evaporation");
            summaries.Add(id + ": " + planner.Result.actualGrade + "%, " + planner.Result.expectedBottles + " units; " + timing);
        }
        var legacy = new BatchPlan { schema = 1, actions = new[] { new BatchAction(Operation.Extract, amount: 12) } };
        Check(ChemicalRecipe.Get(legacy.productId).Id == "dream_dust", "Old plans must retain Dream Dust as their product");
        Check(legacy.Copy().schema == 1, "Copy must preserve the loaded plan's schema");
        Check((int)Operation.Form == 6 && (int)Operation.Dissolve == 7, "Persisted operation values must not be reordered");
        var multipart = new BatchPlan { actions = new[] { new BatchAction(Operation.Extract, amount: 20), new BatchAction(Operation.Dry),
            new BatchAction(Operation.Synthesize, amount: 30), new BatchAction(Operation.React, amount: 5) } };
        Check(ReactionProgress.RemainingSeconds(multipart, 0, 0) == 55, "Multi-stage countdown must include synthesis");
        Check(ReactionProgress.RemainingSeconds(multipart, 1, 0) == 35, "Intermediate evaporation must preserve later reaction time");
        Check(ReactionProgress.RemainingSeconds(multipart, 2, 12) == 23, "Synthesis countdown must advance from saved elapsed ticks");
    }

    private static void CheckAuditRegressions(List<string> summaries)
    {
        // A slot created without Unity is sufficient for exercising callback
        // ownership; no inventory UI or placement method is used in this test.
        var modules = (GameSlotInventory)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameSlotInventory));
        var core = new SimulationItem { unitValue = 375 };
        int ticks = 0, cycles = 0;
        Action<GameItem, GameInventory, SlotMarker> tick = (item, inv, slot) => ticks++;
        Action<GameItem, GameInventory, SlotMarker> cycle = (item, inv, slot) => cycles++;
        Func<GameItem, GameInventory, bool> allowed = (item, inv) => item.identifier == "allowed";
        core.onOneSecondItemFunc = tick;
        core.onCycleEndSlotItemFunc = cycle;
        modules.mayInventoryAddItemFunc = allowed;
        bool busy = false;
        var binding = new DreamDustAutomation.HardwareBinding(core, modules, () => busy, true, 300);
        var permitted = new SimulationItem { identifier = "allowed" };
        Check(core.onOneSecondItemFunc == null && core.onCycleEndSlotItemFunc == null, "Bound core must not process a batch twice");
        Check(core.unitValue == 375 && modules.mayInventoryAddItemFunc(permitted, modules), "Installed core must retain its native value and module rules");
        Check(!modules.mayInventoryAddItemFunc(core, modules), "Binding must not bypass native module restrictions");
        busy = true;
        Check(!modules.mayInventoryAddItemFunc(permitted, modules) && !modules.mayInventoryRemoveItemFunc(permitted, modules), "Running equipment must stay locked");
        binding.Dispose(); binding.Dispose();
        Check(core.unitValue == 375 && modules.mayInventoryAddItemFunc == allowed && modules.mayInventoryRemoveItemFunc == null,
            "Detaching must restore original value and module callbacks exactly once");
        core.onOneSecondItemFunc(core, modules, null); core.onCycleEndSlotItemFunc(core, modules, null);
        Check(ticks == 1 && cycles == 1, "Detached hardware callbacks must work without duplicate subscriptions");
        var otherBinding = new DreamDustAutomation.HardwareBinding(core, modules, () => false, true, 300);
        Check(modules.mayInventoryAddItemFunc(permitted, modules), "Moving hardware to another machine retained the old busy lock");
        var savedCore = new SimulationItem(core) { unitValue = core.unitValue };
        otherBinding.Dispose();
        var restoredBinding = new DreamDustAutomation.HardwareBinding(savedCore, modules, () => false, true, 300);
        restoredBinding.Dispose();
        Check(savedCore.unitValue == 375, "Save/reload must retain the installed core's original value");

        var legacyCore = new SimulationItem { unitValue = 0 };
        legacyCore.ModifyTag("DDA_CORE_VALUE", s => s.Enable().SetString("425"));
        var legacyBinding = new DreamDustAutomation.HardwareBinding(legacyCore, modules, () => false, true, 300);
        Check(legacyCore.unitValue == 425 && !legacyCore.IsTag("DDA_CORE_VALUE"),
            "Old zero-valued installed cores must recover their saved value instead of the vanilla fallback");
        legacyCore.unitValue = 450;
        legacyBinding.Dispose();
        Check(legacyCore.unitValue == 450, "Detaching must not undo a value change made while installed");
        var rebound = new DreamDustAutomation.HardwareBinding(legacyCore, modules, () => false, true, 300);
        Check(legacyCore.unitValue == 450, "Rebinding must not add the device value twice or restore a stale price");
        rebound.Dispose();
        foreach (int fallback in new[] { 200, 250, 300, 300 })
        {
            var oldestCore = new SimulationItem { unitValue = 0 };
            var oldestBinding = new DreamDustAutomation.HardwareBinding(oldestCore, modules, () => false, true, fallback);
            Check(oldestCore.unitValue == fallback, "Untagged early-save cores must recover their native base value");
            oldestBinding.Dispose();
            Check(oldestCore.unitValue == fallback, "Removing migrated hardware must preserve its standalone value");
        }
        summaries.Add("Installed hardware prices: native values preserved, old zero-price cores migrated, detach/rebind stable");

        var originalPlan = new BatchPlan { actions = new[] { new BatchAction(Operation.Synthesize, amount: 30, targetTemperature: 125)
            { precursorId = "methylol", endAmount = 100 } } };
        var copy = originalPlan.Copy();
        copy.actions[0].amount = 45;
        Check(originalPlan.actions[0].amount == 30, "Adjusting a running wait must not mutate another candidate plan");
        Check(copy.actions[0].precursorId == "methylol" && copy.actions[0].endAmount == 100 && copy.actions[0].targetTemperature == 125,
            "Copy must retain reaction endpoint metadata");
        var uv = new SimulationItem();
        uv.identifier = "uv_filter";
        uv.EnableTag("WATER_FILTER");
        var inputs = new BatchInputs { productId = "zyanide_pill", raw = new GameItem[0],
            supplies = new GameItem[] { Mixture("zyanide", 6000, "microbe", 6000), uv }, chamber = new SimulationItem(),
            requestedGrade = 90, finisherLoss = 0 };
        var planner = Calculate(inputs, out var timing);
        Check(planner.Result != null && planner.Result.actualGrade == 90, "Available UV filter must prevent unnecessary grade fallback");
        Check(planner.Result.actions.Any(x => x.operation == Operation.Filter && x.source == 1), "UV filter was not used");
        Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "UV filtration replay differs from preview");
        summaries.Add("UV filtration: " + timing);
        var smallTank = new SimulationItem();
        smallTank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(15000);
        var solid = Mixture("dream_cap_extract", 10000);
        solid.EnableTag("SOLUBLE_TAG");
        var overflowInputs = new BatchInputs { raw = new GameItem[] { solid, new SimulationItem(solid), new SimulationItem(solid) },
            supplies = new GameItem[0], chamber = smallTank, requestedGrade = 90 };
        var overflow = Calculate(overflowInputs, out timing);
        Check(overflow.Result != null && overflow.Result.expectedBottles == 5, "Extra solid stock must not invalidate the portion that fits");
        Check(overflow.Result.actions.Count(x => x.operation == Operation.Dissolve) == 1, "Over-capacity stock must remain untouched");
        Check(BatchPlanner.Revalidate(overflowInputs, overflow.Result) != null, "Capacity-limited plan must replay exactly");
    }

    private static void CheckFeedstockSelection(List<string> summaries)
    {
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool reverse in new[] { false, true })
        {
            var recipe = ChemicalRecipe.Get(id);
            var pure = Mixture(recipe.Active, recipe.UnitVolume * 5);
            var dirty = Mixture(recipe.Active, 1, "physical_contaminant", recipe.UnitVolume * 5 - 1);
            var stock = reverse ? new GameItem[] { dirty, pure } : new GameItem[] { pure, dirty };
            var before = stock.Select(Snapshot).ToArray();
            var inputs = new BatchInputs { productId = id, raw = new GameItem[0], supplies = stock,
                chamber = new SimulationItem(), requestedGrade = 90 };
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.actualGrade == 90 && planner.Result.expectedBottles == 5,
                "Trace-active dirty stock caused avoidable downgrade for " + id);
            Check(planner.GradeAttempts.Count == 1, "Feasible 90% must be accepted before checking lower grades");
            Check(planner.Result.actions.Where(x => x.operation == Operation.Pour).All(x => x.source == (reverse ? 1 : 0)),
                "Dirty stock that adds no qualified units must remain in its container");
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Selected source indices failed replay for " + id);
            Check(stock.Select(Snapshot).SequenceEqual(before), "Selection changed real stock");
            Check(planner.CandidatesChecked < 80, "Selection regressed to broad dose enumeration");
        }

        foreach (int loss in new[] { 0, 12 })
        {
            var inputs = new BatchInputs { raw = new GameItem[0], chamber = new SimulationItem(), requestedGrade = 90, finisherLoss = loss,
                supplies = new GameItem[] { Mixture("dream_cap_extract", 10000), Mixture("dream_cap_extract", 2000, "physical_contaminant", 2000) } };
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.actualGrade == (loss == 0 ? 90 : 80), "Native finisher loss was omitted from blend limit");
            Check(planner.Result.expectedBottles == 6, "Partial low-purity feed should add one qualified bottle");
            int lowPurityUsed = planner.Result.actions.Where(x => x.operation == Operation.Pour && x.source == 1).Sum(x => x.amount);
            Check(lowPurityUsed > 0 && lowPurityUsed < 4000, "Use only the grade-compatible portion of low-purity feed");
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Partial feed plan differs from replay");
            summaries.Add("Partial blend (loss " + loss + "): " + planner.Result.actualGrade + "%, 6 units; " + timing);
        }

        var pureSolid = Mixture("dream_cap_extract", 10000); pureSolid.EnableTag("SOLUBLE_TAG");
        var dirtySolid = Mixture("dream_cap_extract", 10, "physical_contaminant", 9990); dirtySolid.EnableTag("SOLUBLE_TAG");
        var solidInputs = new BatchInputs { raw = new GameItem[] { dirtySolid, pureSolid }, supplies = new GameItem[0],
            chamber = new SimulationItem(), requestedGrade = 90 };
        var solidPlan = Calculate(solidInputs, out var ignoredTiming);
        Check(solidPlan.Result.actualGrade == 90 && solidPlan.Result.expectedBottles == 5, "Indivisible dirty solids must not force a downgrade");
        Check(solidPlan.Result.actions.Where(x => x.operation == Operation.Dissolve).All(x => x.source == 1), "Rejected solid was consumed");

        foreach (int strength in new[] { 100, 50 })
        {
            var tank = new SimulationItem();
            tank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(250000);
            var precursor = Mixture("methylol", 125000, "stabilizing_agent", 125000);
            var stock = new GameItem[] { precursor, Mixture("acid", 250000 * strength / 100, "water", 250000 * (100 - strength) / 100),
                Mixture("reducing_agent", 250000), Filter() };
            var snapshots = stock.Select(Snapshot).ToArray();
            var inputs = new BatchInputs { productId = "fanta_pill", raw = new GameItem[0], supplies = stock, chamber = tank,
                requestedGrade = 90, heaterUpgrades = 1 };
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null && planner.Result.expectedBottles > 0, "Full precursor container blocked auxiliary dosing at strength " + strength);
            int loaded = planner.Result.actions.Where(x => x.operation == Operation.Pour && x.source == 0).Sum(x => x.amount);
            Check(loaded > 0 && loaded < 250000, "Precursor must be metered to leave reagent capacity");
            Check(stock.Select(Snapshot).SequenceEqual(snapshots), "Capacity planning consumed real inventory");
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Capacity reservation does not survive exact native replay");
            foreach (double temperature in new[] { 24.0, 28.0 })
            {
                foreach (var source in stock.Concat(new[] { tank }))
                    source.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(temperature);
                Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Capacity-limited synthesis broke after ordinary ambient drift");
            }
            summaries.Add("Full-tank precursor (acid " + strength + "%): " + planner.Result.actualGrade + "%, " + planner.Result.expectedBottles + " units; " + timing);
        }

        // Reserve solvent for a full container of mushroom mash as well.
        var small = new SimulationItem();
        small.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(120000);
        var extractInputs = new BatchInputs { chamber = small, raw = new GameItem[0],
            supplies = new GameItem[] { Mixture("dream_cap_mush", 120000), Mixture("alcohol", 600000), Filter() },
            requestedGrade = 90, alcoholPercent = 100 };
        var extraction = Calculate(extractInputs, out var extractionTiming);
        Check(extraction.Result != null && extraction.Result.actualGrade == 90, "Full mash stock must leave room for extraction solvent");
        Check(BatchPlanner.Revalidate(extractInputs, extraction.Result) != null, "Capacity-limited extraction failed exact replay");
        int mashUsed = extraction.Result.actions.Where(x => x.operation == Operation.Pour && x.source == 0).Sum(x => x.amount);
        Check(mashUsed > 0 && mashUsed <= 20000, "Five-to-one solvent reserve must limit mash to one sixth of the tank");
        summaries.Add("Full-tank extraction: " + extractionTiming);

        var shortReagents = new BatchInputs { productId = "fanta_pill", raw = new GameItem[0], chamber = new SimulationItem(),
            supplies = new GameItem[] { Mixture("methylol", 125000, "stabilizing_agent", 125000), Mixture("acid", 5000),
                Mixture("base", 5000), Mixture("reducing_agent", 250000), Filter() }, requestedGrade = 90, heaterUpgrades = 1 };
        var limited = Calculate(shortReagents, out var limitedTiming);
        Check(limited.Result != null && limited.Result.actualGrade >= 80, "Limited essential reagents should select a smaller usable precursor batch: " + limited.Error + ", grade=" + limited.Result?.actualGrade + ", count=" + limited.Result?.expectedBottles);
        Check(BatchPlanner.Revalidate(shortReagents, limited.Result) != null, "Stock-limited precursor plan differs from replay");
        summaries.Add("Limited precursor reagents: " + limited.Result.actualGrade + "%; " + limitedTiming);

        // Independent exhaustive oracle for small, stable liquid blends. This is
        // test-only: production solves the grade boundary rather than trying doses.
        var random = new Random(43991);
        for (int sample = 0; sample < 10; sample++)
        {
            var recipe = ChemicalRecipe.Get("birth_control_pill");
            int pureAmount = random.Next(100, 900), dirtyAmount = random.Next(200, 1100);
            int dirtyActive = dirtyAmount / random.Next(2, 9);
            var pure = Mixture(recipe.Active, pureAmount);
            var dirty = Mixture(recipe.Active, dirtyActive, "physical_contaminant", dirtyAmount - dirtyActive);
            int loss = sample % 2 == 0 ? 0 : 12;
            int grade = loss == 0 ? 90 : 80;
            var inputs = new BatchInputs { productId = recipe.Id, raw = new GameItem[0], supplies = new GameItem[] { dirty, pure },
                chamber = new SimulationItem(), finisherLoss = loss, requestedGrade = 90 };
            var planner = Calculate(inputs, out var timing);
            int bestCount = 0;
            for (int take = 0; take <= dirtyAmount; take++)
            {
                var trial = new SimulationItem(pure);
                ChemistryOperations.Pour(new SimulationItem(dirty), trial, take);
                var measured = ChemistryOperations.Measure(trial, loss, 0, 0, null, recipe);
                if (GradePolicy.Meets(measured, grade)) bestCount = Math.Max(bestCount, measured.Bottles);
            }
            Check(planner.Result != null && planner.Result.actualGrade == grade && planner.Result.expectedBottles == bestCount,
                "Analytical blend missed the exhaustive qualified-yield optimum at sample " + sample + ": expected " + bestCount + ", got " + planner.Result?.expectedBottles);
            Check(BatchPlanner.Revalidate(inputs, planner.Result) != null, "Oracle sample failed exact plan replay");
        }
    }

    private static void CheckFormulaDosing(List<string> summaries, GameItem raw)
    {
        // Existing solvent is credited. A 70% solution must contribute exactly
        // the missing 45 mL of alcohol, rather than emptying the bottle.
        var cap = Mixture("dream_cap_mush", 10000);
        cap.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        var alcohol = Mixture("alcohol", 700000, "water", 300000);
        var extractionInputs = new BatchInputs { raw = new GameItem[] { cap },
            chamber = Mixture("alcohol", 5000), supplies = new GameItem[] { alcohol, Filter() },
            requestedGrade = 90, finisherLoss = 0 };
        string sourceBefore = Snapshot(alcohol);
        var extraction = Calculate(extractionInputs, out var timing);
        Check(extraction.Result != null, "Formula extraction did not produce a plan: " + extraction.Error);
        Check(Math.Abs(extraction.SolventAdded - 45000) <= 1, "Solvent formula ignored existing alcohol or reagent strength");
        Check(Snapshot(alcohol) == sourceBefore, "Solvent calculation consumed live stock");
        Check(extraction.CandidatesChecked < 100, "Solvent planner regressed to broad dose enumeration");

        // User's example: 3.7 mL protein needs 3.7 mL effective acid. At 50%
        // reagent strength that is 7.4 mL solution; no dose search is required.
        var proteinInputs = new BatchInputs { raw = new GameItem[0],
            chamber = Mixture("dream_cap_extract", 10000, "protein", 3700, "water", 10000),
            supplies = new GameItem[] { Mixture("acid", 50000, "water", 50000), Filter() },
            requestedGrade = 90, finisherLoss = 0 };
        var protein = Calculate(proteinInputs, out timing);
        Check(protein.Result != null && protein.Result.actualGrade == 90, "Protein cleanup should reach 90%");
        Check(protein.Result.actions.Where(x => x.operation == Operation.Pour && x.source == 0).Sum(x => x.amount) == 7400,
            "Protein formula must dose 7.4 mL of 50% acid for 3.7 mL protein");

        var missing = Calculate(new BatchInputs { raw = new GameItem[] { cap }, chamber = new SimulationItem(),
            supplies = new GameItem[0], requestedGrade = 90 }, out timing);
        Check(missing.Result == null && missing.Error == "diag_reagent_short" && missing.Diagnostics.Primary.Args.Length > 0,
            "Missing alcohol needs a named reagent shortage: " + missing.Error);
        var shortStock = Calculate(new BatchInputs { raw = new GameItem[] { cap }, chamber = new SimulationItem(),
            supplies = new GameItem[] { Mixture("alcohol", 15000), Filter() }, requestedGrade = 90 }, out timing);
        Check(shortStock.Result != null && shortStock.SolventAdded == 15000, "Limited solvent stock should use the available amount");

        var chamber = new SimulationItem();
        chamber.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(20000000);
        var largeInputs = new BatchInputs { chamber = chamber,
            raw = Enumerable.Range(0, 72).Select(i => (GameItem)new SimulationItem(raw)).ToArray(),
            supplies = new GameItem[] { Mixture("alcohol", 7000000, "water", 3000000), Mixture("acid", 1000000), Mixture("base", 1000000) }
                .Concat(Enumerable.Range(0, 8).Select(i => (GameItem)Filter())).ToArray(),
            blenderLoss = 35, heaterUpgrades = 0, finisherLoss = 12, requestedGrade = 90 };
        var originals = largeInputs.raw.Concat(largeInputs.supplies).Concat(new[] { largeInputs.chamber }).ToArray();
        var snapshots = originals.Select(Snapshot).ToArray();
        var large = Calculate(largeInputs, out timing);
        Check(large.Result != null && large.Result.expectedBottles > 250, "Large batch should produce a usable plan");
        Check(large.Result.actualGrade == 80, "Large batch should retain the best available grade");
        for (int i = 0; i < originals.Length; i++) Check(Snapshot(originals[i]) == snapshots[i], "Large preview mutated a live input");
        Check(BatchPlanner.Revalidate(largeInputs, large.Result) != null, "Large plan failed exact replay");
        summaries.Add("72-cap batch: " + large.Result.actualGrade + "%, " + large.Result.expectedBottles + " bottles; " + timing);
        var cancel = new BatchPlanner(largeInputs).Search();
        for (int i = 0; i < 3; i++) Check(cancel.MoveNext(), "Expected incremental calculation");
        (cancel as IDisposable)?.Dispose();
        for (int i = 0; i < originals.Length; i++) Check(Snapshot(originals[i]) == snapshots[i], "Cancelled preview consumed inputs");
    }

    private static void CheckResidueSafeguards()
    {
        Check(TankResidue.Read(null, "dream_dust").TotalVolume == 0, "Missing tank must not invent residue");
        Check(TankResidue.Read(new SimulationItem(), "dream_dust").Components.Length == 0, "Empty tank must not show a warning");
        var savedResidue = Mixture("caffeine", 3171, "protein", 610, "physical_contaminant", 613, "fat", 253, "microbe", 1, "chemical_contaminant", 1);
        string before = Snapshot(savedResidue);
        var report = TankResidue.Read(savedResidue, "dream_dust");
        Check(report.TotalVolume == 4649 && report.HasForeignCompounds, "Saved caffeine residue must warn for Dream Dust");
        Check(report.Components[0].Id == "caffeine" && report.Components[0].Volume == 3171, "Warning must name and quantify the foreign active");
        Check(Snapshot(savedResidue) == before, "Reading residue must not consume or change it");
        Check(!TankResidue.Read(savedResidue, "caffeine_pill").HasForeignCompounds, "Same medicine residue must not need mixed-drug confirmation");
        var trace = TankResidue.Read(Mixture("water", 1000000, "caffeine", 1), "dream_dust");
        Check(trace.HasForeignCompounds && trace.Components[0].Id == "caffeine", "Even trace foreign active must be visible ahead of solvent");
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var normal = Mixture(recipe.Active, 10000, "water", 1000, "alcohol", 500, "protein", 10);
            if (recipe.Mush != null) ChemistryOperations.SetAmount(normal, recipe.Mush, 1000);
            if (id == "pink_alt_injector") ChemistryOperations.SetAmount(normal, "dream_cap_extract", 1000);
            if (id == "fanta_pill") ChemistryOperations.SetAmount(normal, "methylol", 1000);
            Check(!TankResidue.Read(normal, id).HasForeignCompounds, "Valid feedstock falsely flagged for " + id);
            foreach (string other in ChemicalRecipe.Ids)
            {
                if (other == id || (id == "pink_alt_injector" && other == "dream_dust")) continue;
                var foreign = Mixture(ChemicalRecipe.Get(other).Active, 1);
                Check(TankResidue.Read(foreign, id).HasForeignCompounds, "Missing cross-product warning: " + other + " to " + id);
            }
        }
        Check(TankResidue.Read(Mixture("coffee_mush", 1000), "dream_dust").HasForeignCompounds, "Wrong mushroom/plant precursor also needs a warning");
        Check(!TankResidue.Read(Mixture("water", 1000, "alcohol", 1000, "acid", 10), "dream_dust").HasForeignCompounds,
            "Ordinary reagents should show residue without mixed-drug confirmation");

        var consent = new ResidueStartConfirmation();
        Check(!consent.Consume("preview", savedResidue), "Cannot start without explicit confirmation");
        consent.Request("preview", savedResidue);
        Check(consent.Pending, "Start request must wait for confirmation");
        Check(!consent.Consume("changed-preview", savedResidue) && !consent.Pending, "Changed materials, grade or recipe must invalidate consent");
        consent.Request("preview", savedResidue);
        Check(!consent.Consume("preview", new SimulationItem(savedResidue)), "Replacing the tank must invalidate consent even with identical contents");
        consent.Request("preview", savedResidue);
        consent.Clear();
        Check(!consent.Consume("preview", savedResidue), "Cancellation or recalculation must revoke pending consent");
        consent.Request("preview", savedResidue);
        Check(consent.Consume("preview", savedResidue), "Explicit unchanged confirmation should permit starting");
        Check(!consent.Consume("preview", savedResidue), "Consent must be consumed once, never reused for another batch");
    }

    private static void CheckTankCleanup()
    {
        var cleanup = new TankCleanup();
        var tank = Mixture("water", 10000, "caffeine", 3171, "protein", 610, "physical_contaminant", 613, "fat", 253);
        string original = Snapshot(tank);
        Check(!cleanup.Confirm(tank, false) && Snapshot(tank) == original, "Cleanup requires explicit prior review");
        Check(!cleanup.Request(null, false) && !cleanup.Request(new SimulationItem(), false), "Empty or missing tanks cannot be cleared");
        Check(!cleanup.Request(tank, true) && !cleanup.Pending && Snapshot(tank) == original, "Planning and processing must block cleanup requests");
        Check(cleanup.Request(tank, false) && cleanup.Pending && Snapshot(tank) == original, "Opening cleanup must not discard anything");
        cleanup.Cancel();
        Check(!cleanup.Confirm(tank, false) && Snapshot(tank) == original, "Cancelled cleanup must preserve all materials");
        cleanup.Request(tank, false);
        Check(!cleanup.Confirm(tank, true) && Snapshot(tank) == original, "Starting work after review must prevent cleanup");
        cleanup.Request(tank, false);
        var replacement = new SimulationItem(tank);
        Check(!cleanup.Confirm(replacement, false) && Snapshot(tank) == original && Snapshot(replacement) == original,
            "Replacing a tank with identical contents must invalidate cleanup consent");
        cleanup.Request(tank, false);
        ChemistryOperations.SetAmount(tank, "caffeine", 3172);
        ChemistryOperations.SetAmount(tank, "water", 9999);
        string changed = Snapshot(tank);
        Check(!cleanup.Confirm(tank, false) && Snapshot(tank) == changed,
            "Composition changes at the same total volume must require a new cleanup review");
        Check(!cleanup.Confirm(tank, false), "Rejected cleanup cannot leave reusable consent");

        int capacity = tank.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt();
        string identifier = tank.identifier;
        cleanup.Request(tank, false);
        tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(25.5);
        Check(cleanup.Confirm(tank, false), "Ordinary cooling must not invalidate an unchanged residue review");
        Check(ChemistryOperations.Total(tank) == 0 && ChemistryOperations.Parts(tank).All(part => part == 0),
            "Confirmed cleanup must remove both liquid and dry residue");
        Check(tank.identifier == identifier && tank.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt() == capacity &&
            ChemistryOperations.Temperature(tank) == 25.5, "Cleanup must retain the tank and its non-composition state");
        ChemistryOperations.SetAmount(tank, "dream_cap_extract", 2000);
        Check(!cleanup.Confirm(tank, false) && ChemistryOperations.Amount(tank, "dream_cap_extract") == 2000,
            "Old confirmation must never clear a newly filled tank");
    }

    private static void CheckMidBatchInputs()
    {
        var reservation = new BatchInputReservation();
        var existing = Mixture("alcohol", 10000);
        existing.EnableTag(BatchInputReservation.SourceTag);
        existing.state.GetTag(BatchInputReservation.SourceTag).SetInt(0);
        reservation.Capture(new GameItem[] { existing });
        var incoming = Mixture("alcohol", 20000);
        incoming.EnableTag(BatchInputReservation.SourceTag);
        incoming.state.GetTag(BatchInputReservation.SourceTag).SetInt(0);
        string composition = Snapshot(incoming);
        reservation.AddedDuringBatch(incoming);
        Check(!incoming.IsTag(BatchInputReservation.SourceTag) && Snapshot(incoming) == composition,
            "Incoming stock must lose a foreign source index without losing material");
        Check(!reservation.Contains(incoming) && reservation.Contains(existing), "New inputs must stay outside the current batch");
        reservation.AddedDuringBatch(existing);
        Check(existing.IsTag(BatchInputReservation.SourceTag), "Existing source identity must survive inventory callbacks");
        Check(!reservation.CanRemove(existing, false, true), "The current batch's inputs must remain locked");
        Check(reservation.CanRemove(incoming, false, true), "Newly queued inputs should remain removable");
        Check(!reservation.CanRemove(incoming, true, false), "Planning still requires stable input snapshots");
        Check(!reservation.CanStackInto(existing, true), "New stock must not merge into the current batch's source");
        Check(reservation.CanStackInto(incoming, true) && reservation.CanStackInto(null, true), "Free slots and queued stock remain available");
        var restored = new BatchInputReservation();
        restored.Restore(new GameItem[] { existing, incoming });
        Check(restored.Contains(existing) && !restored.Contains(incoming), "Save/load must retain the boundary between current and next-batch inputs");
        reservation.Clear();
        Check(reservation.CanRemove(existing, false, false) && reservation.CanStackInto(existing, false), "Completion or stop must release old inputs");
        reservation.Capture(new GameItem[] { incoming });
        Check(reservation.Contains(incoming) && !reservation.Contains(existing), "Next batch must capture its own input selection");
    }

    private static void CheckConcentrationAdvice(List<string> summaries)
    {
        var plants = Enumerable.Range(0, 56).Select(n => {
            var item = Mixture("dream_cap_mush", 2000);
            item.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            return (GameItem)item;
        }).ToArray();
        var collector = Mixture("alcohol", 3900000);
        var tank = new SimulationItem();
        var inputs = new BatchInputs { productId = "dream_dust", raw = plants, chamber = tank,
            supplies = RecoveredSolvent.Supplies(new GameItem[0], collector, tank, out int source),
            recoveredSource = source, requestedGrade = 99, heaterUpgrades = 4 };
        string beforeCollector = Snapshot(collector), beforeTank = Snapshot(tank);
        var missing = Calculate(inputs, out var timing);
        Check(missing.Result != null && missing.Result.actualGrade == 45 && missing.Result.expectedBottles == 56,
            "Saved no-filter scenario changed its grade/yield");
        Check(missing.Result.concentrationHint == "purity_filter_missing", "Saved scenario must explain missing loose filter");
        Check(missing.Result.Copy().concentrationHint == missing.Result.concentrationHint &&
            BatchPlanner.Revalidate(inputs, missing.Result).concentrationHint == missing.Result.concentrationHint,
            "Copy/revalidation lost concentration advice");
        summaries.Add("Saved-input advice: " + missing.Result.concentrationHint + "; " + timing);

        // Upgrades are not the next bottleneck when even the 60% grade fails.
        inputs.finisherLoss = 12;
        var both = Calculate(inputs, out var ignored).Result;
        Check(both != null && both.concentrationHint == "purity_filter_missing", "Do not blame finisher for a filter-limited 45% batch");
        inputs.finisherLoss = 0;
        inputs.requestedGrade = 45;
        Check(Calculate(inputs, out ignored).Result.concentrationHint == "purity_target",
            "A deliberately selected lower grade is not a filtration failure");
        inputs.requestedGrade = 99;
        var spent = Filter();
        spent.state.GetTag(DurabilityHelper.DURABILITY_TAG).SetInt(0);
        spent.state.GetTag(DurabilityHelper.DURABILITY_FAKE_TAG).SetInt(0);
        inputs.supplies = new GameItem[] { collector, spent };
        Check(Calculate(inputs, out ignored).Result.concentrationHint == "purity_filter_spent", "Worn-out filter must be distinguished from missing filter");
        var good = Filter();
        string beforeFilter = Snapshot(good);
        inputs.supplies = new GameItem[] { collector, good };
        var filtered = Calculate(inputs, out ignored).Result;
        Check(filtered != null && filtered.actualGrade == 99 && filtered.expectedBottles == 28 && filtered.concentrationHint == null,
            "A successful 99% batch must not display a stale improvement warning");
        Check(Snapshot(collector) == beforeCollector && Snapshot(tank) == beforeTank && Snapshot(good) == beforeFilter,
            "Advice calculation consumed live reagents, residue or filter durability");

        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var direct = new BatchInputs { productId = id, raw = new GameItem[0], chamber = new SimulationItem(),
                supplies = new GameItem[] { Mixture(recipe.Active, recipe.UnitVolume * 20) }, requestedGrade = 99, finisherLoss = 12 };
            var limited = Calculate(direct, out ignored).Result;
            Check(limited != null && limited.actualGrade == 80 && limited.concentrationHint == "purity_finisher",
                "Finisher ceiling should produce module advice for " + id);
            direct.finisherLoss = 0;
            direct.supplies = new GameItem[] { Mixture(recipe.Active, recipe.UnitVolume * 10, "chemical_contaminant", recipe.UnitVolume * 10), Filter() };
            var polluted = Calculate(direct, out ignored).Result;
            Check(polluted != null && polluted.actualGrade == 45 && polluted.concentrationHint == "purity_cleaner",
                "Advanced filters must not be advertised as removing chemical contamination for " + id);
        }
        var residue = new BatchInputs { productId = "dream_dust", raw = new GameItem[0], requestedGrade = 99,
            chamber = Mixture("caffeine", 10000), supplies = new GameItem[] { Mixture("dream_cap_extract", 10000), Filter() } };
        Check(Calculate(residue, out ignored).Result.concentrationHint == "purity_residue", "Foreign tank residue needs cleanup advice");

        // The winning route leaves the dirty precursor outside the tank.
        var selected = new BatchInputs { productId = "dream_dust", raw = new GameItem[0], requestedGrade = 99,
            chamber = new SimulationItem(), supplies = new GameItem[] {
                Mixture("dream_cap_extract", 10000, "chemical_contaminant", 10000),
                Mixture("dream_cap_extract", 20000, "plant_matter", 20000) } };
        Check(Calculate(selected, out ignored).Result.concentrationHint == "purity_filter_missing",
            "Advice must use the selected route rather than contamination in unused stock");
        var old = new BatchPlan { actions = new BatchAction[0] };
        Check(old.Copy().concentrationHint == null, "Old plans without advice must remain loadable");
    }

    private static void CheckRecoveredSolvent(List<string> summaries)
    {
        var collector = Mixture("alcohol", 700000, "water", 300000);
        collector.identifier = "water_tank";
        var chamber = new SimulationItem();
        var bin = new GameItem[] { Filter(), Mixture("acid", 250000), Mixture("base", 250000),
            Mixture("reducing_agent", 250000), Mixture("lipobond_agent", 150000), Filter() };
        var stock = RecoveredSolvent.Supplies(bin, collector, chamber, out int source);
        Check(stock.Length == bin.Length + 1 && stock[source] == collector, "Collector was not added as a real reagent source");
        Check(stock.Take(bin.Length).SequenceEqual(bin), "Collector must not reorder existing reagent indices");
        var duplicated = RecoveredSolvent.Supplies(stock, collector, chamber, out int duplicateIndex);
        Check(duplicated.Length == stock.Length && duplicateIndex == source, "Recovered alcohol must not be counted twice");
        var empty = new SimulationItem();
        Check(RecoveredSolvent.Supplies(bin, null, chamber, out int missing).Length == bin.Length && missing == -1,
            "Missing collector must preserve legacy inputs");
        Check(RecoveredSolvent.Supplies(bin, empty, chamber, out int emptyIndex).Length == bin.Length && emptyIndex == -1,
            "An empty collector cannot promise future recovered solvent");
        Check(RecoveredSolvent.Supplies(bin, collector, collector, out int sameTank).Length == bin.Length && sameTank == -1,
            "The reaction tank must never also count as a reagent source");
        var pitcher = new SimulationItem(collector) { identifier = "water_pitcher" };
        Check(RecoveredSolvent.Supplies(bin.Concat(new[] { pitcher }), null, chamber, out int rejectedPitcher).Length == bin.Length && rejectedPitcher == -1,
            "An old saved pitcher in the input bin must not be drawn as a reagent");
        Check(RecoveredSolvent.Supplies(bin.Concat(new[] { pitcher }), pitcher, chamber, out int pitcherIndex).Length == bin.Length + 1 && pitcherIndex == bin.Length,
            "The explicit installed collector remains available independently of input restrictions");

        foreach (string id in new[] { "dream_dust", "nightmare_dust", "caffeine_pill", "pink_alt_injector" })
        {
            var recipe = ChemicalRecipe.Get(id);
            var feed = Mixture(recipe.Mush, 90000, "water", 10000, "protein", 3700, "physical_contaminant", 2000);
            feed.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var recovered = new BatchInputs { productId = id, raw = new GameItem[] { feed }, supplies = stock,
                recoveredSource = source, chamber = chamber, requestedGrade = 90, blenderLoss = 35, heaterUpgrades = 1, finisherLoss = 12 };
            var manual = new BatchInputs { productId = id, raw = recovered.raw, supplies = stock, chamber = chamber,
                requestedGrade = 90, blenderLoss = 35, heaterUpgrades = 1, finisherLoss = 12 };
            var originals = recovered.raw.Concat(stock).Concat(new[] { chamber }).ToArray();
            var before = originals.Select(Snapshot).ToArray();
            var plan = Calculate(recovered, out var timing).Result;
            var reference = Calculate(manual, out var ignored).Result;
            Check(plan != null && reference != null, "Collected alcohol must support " + id);
            Check(plan.actualGrade == reference.actualGrade && plan.expectedBottles == reference.expectedBottles &&
                plan.minimumActive == reference.minimumActive && plan.reagents == reference.reagents,
                "Using the collector must match moving the same liquid into the input bin: " + id);
            Check(plan.recoveredVolume > 0 && plan.recoveredVolume == RecoveredSolvent.DrawnVolume(plan, source),
                "Preview must report the actual volume drawn from the collector: " + id);
            Check(plan.Copy().recoveredVolume == plan.recoveredVolume, "Preview copy lost its recovered-liquid total");
            bool evaporated = false;
            foreach (var step in plan.actions)
            {
                if (step.operation == Operation.Dry) evaporated = true;
                Check(!evaporated || step.operation != Operation.Pour || step.source != source,
                    "A plan cannot consume the collector's old mixture after native recovery changes it");
            }
            var validated = BatchPlanner.Revalidate(recovered, plan);
            Check(validated != null && validated.recoveredVolume == plan.recoveredVolume,
                "Collected-solvent plan failed exact start-time replay: " + id);
            checks += BatchPersistenceTests.CheckRecipe(recovered, validated);
            for (int n = 0; n < originals.Length; n++) Check(Snapshot(originals[n]) == before[n], "Planning/revalidation mutated live collected solvent");
            summaries.Add("Recovered solvent " + id + ": " + plan.actualGrade + "%, " + plan.expectedBottles +
                " units, " + plan.recoveredVolume / 1000.0 + " mL from collector; " + timing);
        }

        // Actual mixed composition matters; recovery is not an infinite pure-alcohol source.
        foreach (var liquid in new[] { Mixture("alcohol", 50000), Mixture("alcohol", 70000, "water", 30000),
            Mixture("alcohol", 70000, "water", 28000, "chemical_contaminant", 2000) })
        {
            var cap = Mixture("dream_cap_mush", 10000);
            cap.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var inputs = new BatchInputs { raw = new GameItem[] { cap }, chamber = new SimulationItem(), requestedGrade = 99,
                supplies = RecoveredSolvent.Supplies(new GameItem[] { Filter() }, liquid, chamber, out int index), recoveredSource = index };
            string before = Snapshot(liquid);
            var plan = Calculate(inputs, out var ignored).Result;
            Check(plan != null && plan.recoveredVolume <= ChemistryOperations.Total(liquid), "Collector use exceeded real liquid stock");
            var withdrawn = new SimulationItem(liquid);
            var receiving = new SimulationItem();
            ChemistryOperations.Pour(withdrawn, receiving, plan.recoveredVolume);
            Check(ChemistryOperations.Total(withdrawn) + ChemistryOperations.Total(receiving) == ChemistryOperations.Total(liquid),
                "Direct collection withdrawal must conserve actual mixed volume");
            Check(ChemistryOperations.Amount(receiving, "alcohol") <= 50001, "Diluted recovered alcohol was treated as pure alcohol");
            Check(Snapshot(liquid) == before && BatchPlanner.Revalidate(inputs, plan) != null, "Collected-liquid preview must be read-only and replayable");
        }

        var sourceCopy = new SimulationItem(collector);
        var copied = stock.Select(x => new SimulationItem(x)).ToArray();
        string untouched = Snapshot(collector);
        RecoveredSolvent.AfterEvaporation(copied, source);
        Check(ChemistryOperations.Total(copied[source]) == 0 && Snapshot(collector) == untouched,
            "Intermediate evaporation must disable stale collector snapshots without discarding live recovery");
        Check(Snapshot(copied[1]) == Snapshot(stock[1]), "Disabling recovery must not remove ordinary input reagents");

        var reservation = new BatchInputReservation();
        sourceCopy.ModifyTag(BatchInputReservation.SourceTag, tag => tag.Enable().SetInt(source));
        reservation.Capture(RecoveredSolvent.Sources(bin, sourceCopy));
        Check(!reservation.CanRemove(sourceCopy, false, true), "The collector used by the current batch must remain reserved");
        foreach (var liquid in Liquid.Liquids) ChemistryOperations.SetAmount(sourceCopy, liquid.identifier, 0);
        var savedCollector = new SimulationItem(sourceCopy);
        var restored = new BatchInputReservation();
        restored.Restore(RecoveredSolvent.Sources(new GameItem[0], savedCollector));
        Check(restored.Contains(savedCollector) && savedCollector.GetTagReadonly(BatchInputReservation.SourceTag).GetInt() == source,
            "An emptied collector must retain its saved source identity");
        Check(RecoveredSolvent.Sources(new GameItem[] { savedCollector }, savedCollector).Count() == 1,
            "Execution source enumeration must not duplicate containers");
        reservation.Clear();
        Check(reservation.CanRemove(sourceCopy, false, false), "Stopping or finishing must release the collector");
        Check(new BatchPlan().recoveredVolume == 0 && new BatchInputs().recoveredSource == -1, "Legacy plans must not invent a collector source");
    }

    private static void CheckCollectedLiquidBinding()
    {
        var collector = Mixture("alcohol", 50000);
        var other = new SimulationItem();
        GeneralHelper.SetItemOwned(collector); GeneralHelper.SetItemOwned(other);
        collector.canThisTargetItemFunc = other.canThisTargetItemFunc = (s, t) => t.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);
        int ticks = 0;
        Action<GameItem, GameInventory, SlotMarker> originalTick = (item, inv, slot) => ticks++;
        collector.onOneSecondItemFunc = originalTick;
        bool busy = false;
        var binding = new DreamDustAutomation.LiquidSourceBinding(collector, () => busy);
        Check(collector.CanTarget(other) && other.CanTarget(collector), "Idle collector must retain normal pouring in both directions");
        collector.onOneSecondItemFunc(collector, null, null);
        busy = true;
        Check(!collector.CanTarget(other) && !other.CanTarget(collector), "Reserved collector must block manual pouring in both directions");
        collector.onOneSecondItemFunc(collector, null, null);
        Check(ticks == 1, "Reserved collected liquid must not change composition through passive ticks");
        busy = false;
        Check(collector.CanTarget(other) && other.CanTarget(collector), "Stop/finish must immediately release collector interactions");
        collector.onOneSecondItemFunc(collector, null, null);
        Check(ticks == 2, "Idle collector must resume its native tick");
        binding.Dispose(); binding.Dispose();
        Check(collector.canItemTargetItemFunc == null && collector.onOneSecondItemFunc == originalTick,
            "Removing a collector or its evaporator must restore original callbacks exactly once");
        var second = new DreamDustAutomation.LiquidSourceBinding(collector, () => false);
        collector.onOneSecondItemFunc(collector, null, null);
        Check(ticks == 3, "Moving a collector between machines must not stack stale callbacks");
        second.Dispose();
    }

    private static void CheckStagedExtraction(List<string> summaries)
    {
        foreach (string id in new[] { "dream_dust", "nightmare_dust", "pink_alt_injector" })
        foreach (int percent in AlcoholBudget.Percentages)
        {
            var recipe = ChemicalRecipe.Get(id);
            var feed = Mixture(recipe.Mush, 100000, "protein", 2000);
            feed.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
            feed.EnableTag(ChemicalHelper.BLENDABLE_TAG);
            var alcohol = Mixture("alcohol", 500000, "water", 10000);
            var inputs = new BatchInputs { productId = id, raw = new GameItem[] { feed }, chamber = new SimulationItem(),
                mashBuffer = new SimulationItem(), alcoholPercent = percent, requestedGrade = 99, heaterUpgrades = 4,
                supplies = new GameItem[] { alcohol, Mixture("acid", 50000), Mixture("base", 50000),
                    Mixture("reducing_agent", 200000), Mixture("lipobond_agent", 50000), Filter(), Filter() } };
            string original = Snapshot(alcohol);
            var planner = Calculate(inputs, out var timing);
            Check(planner.Result != null, "Staged extraction failed: " + id + "/" + percent + " " + planner.Error);
            var plan = planner.Result;
            Check(Snapshot(alcohol) == original && ChemistryOperations.Total(inputs.mashBuffer) == 0, "Planning modified live feed or buffer");
            Check(plan.alcoholPercent == percent, "Alcohol option was lost from the plan");
            Check(plan.extractionAlcohol >= 5000 * percent && plan.extractionAlcohol <= 5000 * percent + 1, "Alcohol dose differs from selected percentage");
            Check(percent == 100 ? plan.extractionPortions == 1 : plan.extractionPortions >= 2, "Low alcohol must be physically staged");
            Check(plan.actualGrade == 99, "Staged native extraction should retain high-grade production");
            Check(plan.reactionSeconds == plan.actions.Where(a => a.operation == Operation.Extract || a.operation == Operation.React ||
                a.operation == Operation.Synthesize).Sum(a => a.amount), "Artificial reaction-time penalty detected");
            var validated = BatchPlanner.Revalidate(inputs, plan);
            Check(validated != null, "Staged preview cannot be replayed at start");
            checks += BatchPersistenceTests.CheckRecipe(inputs, validated);
            summaries.Add("Staged " + id + " " + percent + "%: " + plan.expectedBottles + " products; " + plan.extractionPortions +
                " portions; " + plan.extractionAlcohol + " uL alcohol; " + plan.reactionSeconds + " seconds; " + timing);
        }

        var limited = new BatchInputs { productId = "dream_dust", raw = new GameItem[0],
            chamber = new SimulationItem(), mashBuffer = new SimulationItem(), heaterUpgrades = 4, requestedGrade = 99,
            supplies = new GameItem[] { Mixture("dream_cap_mush", 10000), Mixture("alcohol", 20000, "water", 180000), Filter() } };
        var budget = AlcoholBudget.Read(limited);
        Check(budget.Full == 50000 && budget.Available == 20000, "Availability must use pure alcohol, not bottle volume");
        Check(!budget.Allows(100) && !budget.Allows(75) && !budget.Allows(50) && budget.Allows(25), "Unavailable options not disabled correctly");
        limited.alcoholPercent = 100;
        var refused = Calculate(limited, out _);
        Check(refused.Result == null && refused.Error == "alcohol_unavailable", "Full dose silently degraded despite insufficient stock");
        limited.alcoholPercent = 25;
        var small = Calculate(limited, out _);
        Check(small.Result != null, "Affordable quarter-dose was blocked");
        checks += BatchPersistenceTests.CheckRecipe(limited, small.Result);

        // Already-mixed alcohol is never extracted from the feed for free.
        limited.supplies = new GameItem[] { Mixture("dream_cap_mush", 10000, "alcohol", 12000), Mixture("alcohol", 500), Filter() };
        var mixed = Calculate(limited, out _);
        Check(mixed.Result != null, "Mixed alcohol feed cannot be processed");
        Check(mixed.Result.extractionAlcohol == 12500, "Mixed feed exceeded the chosen alcohol budget");
        Check(BatchPlanner.Revalidate(limited, mixed.Result) != null, "Mixed staged feed is not physically replayable");
        checks += BatchPersistenceTests.CheckRecipe(limited, mixed.Result);

        // A cancelled batch's buffered mash is physical saved material, not a
        // transient plan payload. It participates in the next batch exactly once.
        limited.supplies = new GameItem[] { Mixture("alcohol", 50000), Filter() };
        limited.mashBuffer = Mixture("dream_cap_mush", 10000);
        var residue = Calculate(limited, out _);
        Check(residue.Result != null && residue.Result.actions[0].operation == Operation.FeedMash, "Buffered leftovers were ignored");
        checks += BatchPersistenceTests.CheckRecipe(limited, residue.Result);
        limited.mashBuffer.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(1000);
        Check(Calculate(limited, out _).Result == null, "Overflowing mash buffer should reject staging, not lose feed");
        limited.mashBuffer = null;
        limited.supplies = new GameItem[] { Mixture("dream_cap_mush", 10000), Mixture("alcohol", 50000), Filter() };
        Check(Calculate(limited, out _).Error == "missing_buffer", "Missing buffer did not produce actionable feedback");

        foreach (int amount in new[] { 7999, 10001, 977777 })
        foreach (int percent in new[] { 75, 50, 25, 10, 1 })
        {
            var inputs = new BatchInputs { raw = new GameItem[0], chamber = Mixture("alcohol", 7000),
                mashBuffer = new SimulationItem(), requestedGrade = 99, heaterUpgrades = 0, alcoholPercent = percent,
                supplies = new GameItem[] { Mixture("dream_cap_mush", amount, "protein", 537, "water", 777),
                    Mixture("alcohol", amount * 5, "water", 555), Mixture("acid", 4000), Mixture("base", 4000), Filter(), Filter(), Filter() } };
            var planned = Calculate(inputs, out _);
            Check(planned.Result != null, "Odd-volume or low-upgrade staged planning failed");
            Check(BatchPlanner.Revalidate(inputs, planned.Result) != null, "Odd-volume stages changed native rounding on revalidation");
            checks += BatchPersistenceTests.CheckRecipe(inputs, planned.Result);
        }
        var emptyBudget = new BatchInputs { raw = new GameItem[0], supplies = new GameItem[0],
            chamber = Mixture("dream_cap_mush", 10000), mashBuffer = new SimulationItem() };
        Check(AlcoholBudget.Percentages.All(p => !AlcoholBudget.Read(emptyBudget).Allows(p)), "Zero alcohol must disable every dose");
        emptyBudget.supplies = new GameItem[] { Mixture("alcohol", 25000) };
        Check(AlcoholBudget.Read(emptyBudget).Allows(50) && !AlcoholBudget.Read(emptyBudget).Allows(75), "Exact dose boundary rounded incorrectly");
        emptyBudget.supplies = new GameItem[] { Mixture("alcohol", 24999) };
        Check(!AlcoholBudget.Read(emptyBudget).Allows(50), "One microlitre short must remain unavailable");
        foreach (int percent in new[] { 10, 1 })
        {
            int required = 500 * percent;
            emptyBudget.supplies = new GameItem[] { Mixture("alcohol", required) };
            Check(AlcoholBudget.Read(emptyBudget).Allows(percent), "New dose rejected exact stock");
            emptyBudget.supplies = new GameItem[] { Mixture("alcohol", required - 1) };
            Check(!AlcoholBudget.Read(emptyBudget).Allows(percent), "New dose accepted insufficient stock");

            var mixedInputs = new BatchInputs { productId = "dream_dust", alcoholPercent = percent, requestedGrade = 99, heaterUpgrades = 4,
                raw = new GameItem[0], chamber = new SimulationItem(), mashBuffer = new SimulationItem(),
                supplies = new GameItem[] { Mixture("dream_cap_mush", 100000, "alcohol", 5000 * percent - 100),
                    Mixture("alcohol", 100), Filter(), Filter() } };
            var mixedPlan = Calculate(mixedInputs, out var mixedTiming);
            Check(mixedPlan.Result != null, "Small dose with carried alcohol hit a premature portion limit: " + mixedPlan.Error);
            Check(mixedPlan.Result.extractionAlcohol == 5000 * percent, "Small dose exceeded its alcohol budget");
            Check(BatchPlanner.Revalidate(mixedInputs, mixedPlan.Result) != null, "Small mixed-alcohol plan did not replay");
            checks += BatchPersistenceTests.CheckRecipe(mixedInputs, mixedPlan.Result);
            summaries.Add("Mixed-feed " + percent + "%: " + mixedPlan.Result.extractionPortions + " portions; " + mixedTiming);
        }
    }

    private static SimulationItem Solid(params object[] parts)
    {
        var item = Mixture(parts);
        item.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        item.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        return item;
    }

    private static void CheckDirectForm(List<string> summaries)
    {
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true })
        {
            var recipe = ChemicalRecipe.Get(id);
            var source = Mixture(direct ? "starch" : recipe.Active, recipe.UnitVolume * 1400);
            var input = new BatchInputs { directForm = direct, productId = id, raw = new GameItem[0], supplies = new GameItem[0],
                chamber = source, requestedGrade = 99, maxProducts = 1280 };
            var capped = Calculate(input, out var capTiming).Result;
            Check(capped != null && capped.expectedBottles == 1280 && capped.outputCapacity == 1280 && capped.outputLimited,
                "Normal/direct batches must cap to actual available placements: " + id);
            Check(capped.actions.Last().operation == Operation.Form && capped.actions.Last().amount == 1280, "Execution count differs from preview cap");
            Check(ChemistryOperations.Total(source) == 1400 * recipe.UnitVolume, "Preview mutated stock");
            Check(BatchPlanner.Revalidate(input, capped) != null, "Capacity-limited preview must revalidate");
            input.maxProducts = 1279;
            Check(BatchPlanner.Revalidate(input, capped) == null, "Cannot start if another product occupies promised output space");
            input.maxProducts = 2560;
            var uncapped = Calculate(input, out capTiming).Result;
            Check(uncapped.expectedBottles == 1400 && !uncapped.outputLimited, "Do not claim a space limit on material-limited batches");
            input.maxProducts = 0;
            Check(Calculate(input, out capTiming).Error == "output_no_space", "Full output must explain shortage before chemistry starts");
            var saved = BatchPersistence.Deserialize(BatchPersistence.Serialize(new RunningBatch { plan = capped }));
            Check(saved.plan.outputCapacity == 1280 && saved.plan.outputLimited && saved.plan.expectedBottles == 1280,
                "Save/reload lost cap or warning");
            var remainder = new SimulationItem(source);
            int outputVolume = 0;
            for (int i = 0; i < capped.expectedBottles; i++)
            {
                var product = new SimulationItem();
                ChemistryOperations.TransferToProduct(remainder, product, recipe.UnitVolume);
                outputVolume += ChemistryOperations.Total(product);
            }
            Check(ChemistryOperations.Total(remainder) == 120 * recipe.UnitVolume && outputVolume + ChemistryOperations.Total(remainder) == ChemistryOperations.Total(source),
                "Capped bottling must preserve every unit of excess tank contents");
            var legacyJson = Newtonsoft.Json.Linq.JObject.Parse(BatchPersistence.Serialize(new RunningBatch { plan = uncapped }));
            ((Newtonsoft.Json.Linq.JObject)legacyJson["plan"]).Remove("outputCapacity");
            ((Newtonsoft.Json.Linq.JObject)legacyJson["plan"]).Remove("outputLimited");
            Check(BatchPersistence.Deserialize(legacyJson.ToString()).plan.outputCapacity == int.MaxValue, "Existing running saves must retain their original batch size");
        }
        summaries.Add("Output caps: seven products in both modes; 1280/1400-unit batches, space changes, empty/full bins, leftovers and legacy saves verified.");
        var fruit = Solid();
        fruit.identifier = "nutrifruit";
        // Nutrifruit calls the native potato composition for a 250 g fruit.
        // Use the native content writer and float arithmetic, without its food UI.
        foreach (var part in new[] { ("water", 78.55f), ("starch", 17.33f), ("protein", 2f), ("sugar", .67f),
            ("mineral", .67f), ("organic_waste", .33f), ("microbe", .33f), ("physical_contaminant", .12f) })
            ChemicalHelper.SetChemContentML(fruit, part.Item1, part.Item2 * 2.5f);
        var supplies = new GameItem[] { Mixture("dream_cap_extract", 500000), Mixture("methylol", 100000), Filter() };
        string supplyBefore = string.Join(";", supplies.Select(Snapshot)), fruitBefore = Snapshot(fruit);
        int cases = 0;
        foreach (string id in ChemicalRecipe.Ids)
        foreach (int loss in new[] { 0, 12 })
        foreach (int blendLoss in new[] { 0, 35 })
        {
            var input = new BatchInputs { directForm = true, productId = id, raw = new GameItem[] { fruit }, supplies = supplies,
                chamber = Mixture(), requestedGrade = 99, alcoholPercent = 100, heaterUpgrades = 0,
                blenderLoss = blendLoss, finisherLoss = loss };
            var planner = Calculate(input, out var timing);
            var plan = planner.Result;
            Check(plan != null, "Nutrifruit direct form failed: " + id + "/" + planner.Error);
            Check(plan.directForm && plan.schema == 3 && plan.minimumActive == 0 && plan.actualGrade == 0 && plan.requestedGrade == 0,
                "Zero-potency direct batch was assigned a target grade");
            Check(plan.reagents == 0 && plan.recoveredVolume == 0 && plan.actions.All(a => a.operation != Operation.Pour &&
                a.operation != Operation.Filter && a.operation != Operation.Extract && a.operation != Operation.Synthesize), "Direct mode used purification supplies");
            var nativeTank = Mixture();
            ChemistryOperations.Blend(new SimulationItem(fruit), nativeTank, blendLoss);
            for (int tick = 0; tick < plan.reactionSeconds; tick++) ChemistryOperations.ReactionTick(nativeTank);
            var reference = ReferenceMeasure(nativeTank, loss, ChemicalRecipe.Get(id));
            Check(reference.Bottles == plan.expectedBottles && reference.MinimumBottleActiveVolume == plan.minimumActive,
                "Direct output differs from native splitting and forming");
            Check(BatchPlanner.Revalidate(input, plan) != null, "Direct plan cannot start");
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
            Check(Snapshot(fruit) == fruitBefore && string.Join(";", supplies.Select(Snapshot)) == supplyBefore,
                "Direct preview consumed ingredients or wore a filter");
            cases++;
        }
        var low = new BatchInputs { directForm = true, productId = "dream_dust", raw = new GameItem[0], supplies = new GameItem[0],
            chamber = Mixture("dream_cap_extract", 4000, "starch", 16000), requestedGrade = 99, alcoholPercent = 100, finisherLoss = 12 };
        var lowPlan = Calculate(low, out var lowTiming).Result;
        Check(lowPlan != null && lowPlan.expectedBottles == 10 && lowPlan.minimumActive == 352,
            "Direct residue-only batch must show actual 17.6% after native forming loss");
        checks += BatchPersistenceTests.CheckRecipe(low, lowPlan);
        low.directForm = false;
        Check(BatchPlanner.Revalidate(low, lowPlan) == null, "Direct plan crossed into purification mode");
        Check(Calculate(low, out lowTiming).Result == null, "Normal mode accepted a below-30% batch");

        var full = new BatchInputs { directForm = true, raw = new GameItem[] { fruit, Solid("starch", 7000) }, supplies = supplies,
            chamber = Mixture(), mashBuffer = Mixture("starch", 3000), requestedGrade = 99, alcoholPercent = 100 };
        full.chamber.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(10000);
        var fitted = Calculate(full, out var fitTiming).Result;
        Check(fitted != null && fitted.expectedBottles == 5 && fitted.actions.Any(a => a.operation == Operation.FeedMash) &&
            fitted.actions.Count(a => a.operation == Operation.Blend) == 1 && fitted.actions.First(a => a.operation == Operation.Blend).source == 1,
            "Direct mode overflowed capacity or failed to use buffer and fitting solids");
        checks += BatchPersistenceTests.CheckRecipe(full, fitted);
        full.mashBuffer = Mixture("starch", 11000);
        Check(Calculate(full, out fitTiming).Error == "buffer_return_full", "Overfull buffer silently discarded");
        full.mashBuffer = null; full.raw = new GameItem[0];
        Check(Calculate(full, out fitTiming).Result == null, "Empty direct batch consumed medicinal bottles");
        full.raw = new GameItem[] { Solid("water", 5000) };
        Check(Calculate(full, out fitTiming).Error == "direct_no_output", "Water-only raw produced fake solid matter");
        var powder = Solid("starch", 8000); powder.DisableTag(ChemicalHelper.BLENDABLE_TAG); powder.EnableTag("SOLUBLE_TAG");
        full.raw = new GameItem[] { powder }; full.blenderLoss = 35;
        var dissolved = Calculate(full, out fitTiming).Result;
        Check(dissolved != null && dissolved.expectedBottles == 4 && dissolved.actions.Any(a => a.operation == Operation.Dissolve),
            "Soluble raw must follow native dissolution without blender loss");
        checks += BatchPersistenceTests.CheckRecipe(full, dissolved);
        full.raw = new GameItem[] { fruit }; full.chamber = Mixture(); full.directForm = false; full.supplies = new GameItem[0];
        Check(Calculate(full, out fitTiming).Result == null, "Normal mode unexpectedly selected nutrifruit");
        int protectedCases = 0;
        // A shared bin must keep medicine inputs for ALL products, even when
        // another product is selected for direct forming. Tiny amounts count.
        var protectedIngredients = ChemicalRecipe.Ids.Select(id => ChemicalRecipe.Get(id).Active)
            .Concat(new[] { "dream_cap_mush", "nightmare_cap_mush", "coffee_mush", "methylol" }).Distinct().ToArray();
        foreach (string product in ChemicalRecipe.Ids)
        {
            var baseline = new BatchInputs { directForm = true, productId = product, raw = new GameItem[] { fruit },
                supplies = new GameItem[0], chamber = Mixture(), requestedGrade = 99 };
            var fruitPlan = Calculate(baseline, out fitTiming).Result;
            foreach (string ingredient in protectedIngredients)
            foreach (bool soluble in new[] { false, true })
            {
                var medicinal = Solid(ingredient, 1, "starch", 7999);
                if (soluble) { medicinal.DisableTag(ChemicalHelper.BLENDABLE_TAG); medicinal.EnableTag("SOLUBLE_TAG"); }
                string original = Snapshot(medicinal);
                baseline.raw = new GameItem[] { medicinal, fruit };
                var mixed = Calculate(baseline, out fitTiming).Result;
                Check(mixed != null && mixed.expectedBottles == fruitPlan.expectedBottles && mixed.minimumActive == fruitPlan.minimumActive,
                    "Direct mode mixed medicinal feedstock into nutrifruit: " + product + "/" + ingredient);
                Check(mixed.actions.Where(a => a.operation == Operation.Blend || a.operation == Operation.Dissolve).All(a => a.source == 1),
                    "Direct mode reserved a medicinal raw input");
                Check(!DirectFormInputs.CanUse(medicinal) && Snapshot(medicinal) == original, "Protected medicine input was mutated");
                baseline.raw = new GameItem[] { medicinal };
                Check(Calculate(baseline, out fitTiming).Result == null, "Medicine-only bin should not start direct forming");
                protectedCases++;
            }
        }
        foreach (bool soluble in new[] { false, true })
        {
            var plain = Solid("starch", 8000);
            if (soluble) { plain.DisableTag(ChemicalHelper.BLENDABLE_TAG); plain.EnableTag("SOLUBLE_TAG"); }
            var input = new BatchInputs { directForm = true, raw = new GameItem[] { plain }, supplies = new GameItem[0],
                chamber = Mixture(), requestedGrade = 99 };
            var plan = Calculate(input, out fitTiming).Result;
            // Same volume/count and zero grade floor, but now contains medicine.
            ChemistryOperations.SetAmount(plain, "starch", 7999);
            ChemistryOperations.SetAmount(plain, "dream_cap_extract", 1);
            Check(BatchPlanner.Revalidate(input, plan) == null, "An old direct plan consumed newly medicinal feedstock");
        }
        summaries.Add("Direct forming: " + cases + " nutrifruit/product/loss cases; zero/low potency, native yield, capacity, residue, supplies and save/resume verified");
        summaries.Add("Direct feedstock protection: " + protectedCases + " selected-product/medicinal-component/solid-type cases; protected inputs stay untouched");
    }

    private static void CheckProductLabels(List<string> summaries)
    {
        string[] ids = { "lab_pure", "pharma_grade", "near_pharma", "street_standard", "cut_product", "junk_grade", "starch_grade" };
        foreach (var actual in ProductLabels.Choices())
        {
            var item = Mixture("dream_cap_extract", 400, "starch", 1600);
            item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
            item.state.GetTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG).Enable().SetDouble(20);
            var feature = new ItemFeature("CATEGORY_CHEMICAL_PURITY") { identifier = "chemical_purity" };
            feature.SetConditions(actual, actual);
            item.itemFeatures.Add(feature);
            var other = new ItemFeature("unrelated_feature"); item.itemFeatures.Add(other);
            string before = Snapshot(item);
            for (int choice = 0; choice < 8; choice++)
            {
                feature.SetExposed(true);
                Check(ProductLabels.Apply(item, choice), "Native quality label not applied");
                Check(feature.fakeCondition.identifier == (choice == 0 ? actual.identifier : ids[choice - 1]), "Wrong native label chosen");
                Check(ReferenceEquals(feature.realCondition, actual) && ChemicalProductHelper.GetPurity(item) == 20 && Snapshot(item) == before,
                    "Labeling altered real condition or chemical composition");
                Check(!feature.IsExposed() && feature.IsFeatureMatch() == (feature.fakeCondition.identifier == actual.identifier),
                    "Labeling bypassed native inspection semantics");
                Check(item.itemFeatures.Count == 2 && ReferenceEquals(item.itemFeatures[1], other), "Labeling modified unrelated features");
            }
            Check(ProductLabels.Apply(item, 0) && ReferenceEquals(feature.fakeCondition, actual), "Default failed to restore actual grade");
        }
        Check(!ProductLabels.Apply(Mixture(), 1) && !ProductLabels.Apply(null, 0), "Nonchemical item labeled");
        var missingFeature = Mixture(); missingFeature.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
        Check(!ProductLabels.Apply(missingFeature, 0), "Item without native purity feature was rewritten");
        summaries.Add("Batch labels: all 7 native grades + actual-grade default; true purity, other features and native inspection preserved");
    }

    private static SimulationItem LabelProduct(string productId, int purity = 60)
    {
        var recipe = ChemicalRecipe.Get(productId);
        var item = Mixture(recipe.Active, recipe.UnitVolume * purity / 100, "starch", recipe.UnitVolume * (100 - purity) / 100);
        item.identifier = productId;
        item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
        item.state.GetTag(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG).Enable().SetString(recipe.Active);
        var feature = new ItemFeature("CATEGORY_CHEMICAL_PURITY") { identifier = "chemical_purity" };
        item.itemFeatures.Add(feature);
        ChemicalProductHelper.GradeProduct(item);
        feature.fakeCondition = ItemConditionList.CreatePharmaGrade();
        feature.SetExposed(true);
        return item;
    }

    private static void CheckCategoryLabels(List<string> summaries)
    {
        var machine = Mixture();
        var rules = ProductLabels.LoadRules(machine);
        Check(rules.Count == 7 && rules.Values.All(rule => rule.Grade == 0 && rule.Percent == 100), "Fresh label rules must default to actual grade / 100%");
        ProductLabels.SaveRule(machine, "dream_dust", new ProductLabelRule(0, 50));
        ProductLabels.SaveRule(machine, "fanta_pill", new ProductLabelRule(1, 100));
        ProductLabels.SaveRule(machine, "nightmare_dust", new ProductLabelRule(-1, 75));
        rules = ProductLabels.LoadRules(new SimulationItem(machine));
        Check(rules["dream_dust"].Grade == 0 && rules["dream_dust"].Percent == 50 && rules["fanta_pill"].Grade == 1 &&
            rules["fanta_pill"].Percent == 100 && rules["nightmare_dust"].Grade == -1 && rules["nightmare_dust"].Percent == 75,
            "Per-product label grade/ratio did not survive reload independently");
        var legacy = Mixture(); legacy.state.GetTag(ProductLabels.LegacyTag).Enable().SetInt(3);
        Check(ProductLabels.LoadRules(legacy).Values.All(rule => rule.Grade == 3 && rule.Percent == 100), "Legacy label choice was lost on upgrade");
        ProductLabels.SaveRule(legacy, "dream_dust", new ProductLabelRule(0, 25));
        var migrated = ProductLabels.LoadRules(new SimulationItem(legacy));
        Check(migrated["dream_dust"].Grade == 0 && migrated["dream_dust"].Percent == 25 && migrated["fanta_pill"].Grade == 3,
            "Updating one category changed another category or its legacy setting");

        var dreams = Enumerable.Range(0, 10).Select(i => (GameItem)LabelProduct("dream_dust", 20 + i * 8)).ToArray();
        var fantas = Enumerable.Range(0, 5).Select(i => (GameItem)LabelProduct("fanta_pill", 40 + i * 10)).ToArray();
        var nightmare = LabelProduct("nightmare_dust");
        var missingFeature = Mixture(); missingFeature.identifier = "dream_dust"; missingFeature.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
        var ordinary = Mixture(); ordinary.identifier = "dream_dust";
        var stock = dreams.Take(3).Concat(fantas.Take(2)).Concat(new GameItem[] { ordinary, nightmare, missingFeature })
            .Concat(dreams.Skip(3)).Concat(fantas.Skip(2)).ToList();
        var chemical = dreams.Concat(fantas).Concat(new GameItem[] { nightmare }).ToArray();
        var before = chemical.ToDictionary(item => item, Snapshot);
        var real = chemical.ToDictionary(item => item, item => ChemicalProductHelper.GetChemicalFeature(item).realCondition);
        var fake = chemical.ToDictionary(item => item, item => ChemicalProductHelper.GetChemicalFeature(item).fakeCondition);
        var plan = ProductLabels.Plan(stock, rules);
        var dreamPlan = plan.First(group => group.ProductId == "dream_dust");
        var fantaPlan = plan.First(group => group.ProductId == "fanta_pill");
        Check(dreamPlan.Total == 10 && dreamPlan.Selected.SequenceEqual(dreams.Take(5)), "50% must select the first half of dream dust only");
        Check(fantaPlan.Total == 5 && fantaPlan.Selected.SequenceEqual(fantas), "Independent Fanta rule was not applied");
        Check(plan.First(group => group.ProductId == "nightmare_dust").Selected.Length == 0, "Keep-label category was selected");
        Check(ProductLabels.Plan(stock, rules, "dream_dust").Length == 1, "Selected-product action included other categories");
        Check(chemical.All(item => ReferenceEquals(ChemicalProductHelper.GetChemicalFeature(item).fakeCondition, fake[item])), "Label preview changed live labels");
        foreach (var group in plan)
        foreach (var item in group.Selected) Check(ProductLabels.ApplyPlanned(group, item, stock), "Selected product label was not applied");
        foreach (var item in dreams.Take(5))
            Check(ReferenceEquals(ChemicalProductHelper.GetChemicalFeature(item).fakeCondition, real[item]), "Actual-grade rule ignored this bottle's own grade");
        foreach (var item in dreams.Skip(5).Concat(new GameItem[] { nightmare }))
            Check(ReferenceEquals(ChemicalProductHelper.GetChemicalFeature(item).fakeCondition, fake[item]) &&
                ChemicalProductHelper.GetChemicalFeature(item).IsExposed(), "Unselected product's label or exposure changed");
        foreach (var item in fantas)
            Check(ChemicalProductHelper.GetChemicalFeature(item).fakeCondition.identifier == "lab_pure", "Fanta was not labeled lab pure");
        foreach (var item in chemical)
            Check(Snapshot(item) == before[item] && ReferenceEquals(ChemicalProductHelper.GetChemicalFeature(item).realCondition, real[item]),
                "Category labeling changed actual chemistry or grade");
        var repeat = ProductLabels.Plan(stock, rules).First(group => group.ProductId == "dream_dust");
        Check(repeat.Selected.SequenceEqual(dreamPlan.Selected), "Repeat click selected an additional half");
        foreach (var item in repeat.Selected) ProductLabels.ApplyPlanned(repeat, item, stock);
        Check(dreams.Skip(5).All(item => ReferenceEquals(ChemicalProductHelper.GetChemicalFeature(item).fakeCondition, fake[item])),
            "Repeat click gradually relabeled unselected products");
        var newcomer = LabelProduct("dream_dust"); stock.Insert(0, newcomer);
        Check(!ProductLabels.ApplyPlanned(dreamPlan, newcomer, stock), "Output arriving after click was included in old snapshot");
        stock.Remove(dreams[0]);
        Check(!ProductLabels.ApplyPlanned(dreamPlan, dreams[0], stock), "Product removed from output was still relabeled");
        Check(!ProductLabels.ApplyPlanned(dreamPlan, fantas[0], stock), "Snapshot crossed medicine categories");
        rules["dream_dust"] = new ProductLabelRule(1, 100);
        Check(dreamPlan.Rule.Grade == 0 && dreamPlan.Rule.Percent == 50 && dreamPlan.Selected.Length == 5,
            "Changing settings altered an in-progress labeling snapshot");

        int[] amounts = { 0, 1, 3, 4, 10, 11, 20, 99, 100 };
        int[][] counts = { new[] { 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0, 1 }, new[] { 0, 0, 1, 2, 3 },
            new[] { 0, 1, 2, 3, 4 }, new[] { 1, 2, 5, 7, 10 }, new[] { 1, 2, 5, 8, 11 },
            new[] { 2, 5, 10, 15, 20 }, new[] { 9, 24, 49, 74, 99 }, new[] { 10, 25, 50, 75, 100 } };
        foreach (string id in ChemicalRecipe.Ids)
        {
            var products = Enumerable.Range(0, 100).Select(i => (GameItem)LabelProduct(id)).ToArray();
            for (int c = 0; c < amounts.Length; c++)
            for (int p = 0; p < ProductLabels.Percentages.Length; p++)
            {
                rules[id] = new ProductLabelRule(1, ProductLabels.Percentages[p]);
                var group = ProductLabels.Plan(products.Take(amounts[c]), rules, id).Single();
                Check(group.Selected.Length == counts[c][p] && group.Total == amounts[c] && group.Selected.SequenceEqual(products.Take(counts[c][p])),
                    "Wrong floor/order for " + id + "/" + amounts[c] + "/" + ProductLabels.Percentages[p]);
            }
        }
        summaries.Add("Category labels: seven independent saved rules; five ratios rounded down in inventory order; mixed actual grades, repeat clicks and fixed output snapshots verified");
    }

    private static void CheckUsability(List<string> summaries)
    {
        int before = checks;
        foreach (string id in ChemicalRecipe.Ids)
        {
            var product = Solid("starch", 10000); product.identifier = id;
            Check(InventoryRules.Product(product) && !InventoryRules.Input(product) && !DirectFormInputs.CanUse(product), "Finished medicine entered intake: " + id);
            product.EnableTag(WaterHelper.LIQUID_CONTAINER_TAG);
            Check(!InventoryRules.Liquid(product), "Finished medicine masqueraded as reagent bottle");
        }
        var tagged = Mixture("water", 10000); tagged.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
        Check(InventoryRules.Product(tagged) && !InventoryRules.Input(tagged), "Tagged chemical product escaped input exclusion");
        Check(InventoryRules.Input(Solid("plant_matter", 10000)), "Food raw excluded");
        Check(InventoryRules.Input(Filter()), "Loose filter excluded");
        Check(!InventoryRules.Input(new SimulationItem()), "Empty bottle accepted as fresh material");
        var junk = Mixture(); junk.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG);
        Check(!InventoryRules.Input(junk), "Unrelated item entered intake");
        var rejectedWater = Mixture("water", 120000, "physical_contaminant", 60000); rejectedWater.identifier = "water_pitcher";
        Check(!InventoryRules.Input(rejectedWater) && !DirectFormInputs.CanUseLiquid(rejectedWater), "Pitcher must be excluded from intake and direct forming");
        foreach (string container in new[] { "bottled_water", "water_tank", "water_jug" })
        foreach (string productId in ChemicalRecipe.Ids)
        foreach (int loss in new[] { 0, 12 })
        {
            var water = Mixture("water", 120000, "physical_contaminant", 60000); water.identifier = container;
            if (container == "water_tank") water.EnableTag("WATER_TANK_TAG");
            Check(InventoryRules.Input(water) && DirectFormInputs.CanUseLiquid(water), "Filled water vessel rejected by intake: " + container);
            Check(!WasteCollection.Accepts(water), "Filled water vessel mistaken for waste");
            var drug = Mixture("water", 10000, "dream_cap_extract", 6000);
            var pure = Mixture("water", 100000);
            string original = Snapshot(water);
            Check(!RecoveredSolvent.Supplies(new GameItem[] { water, drug, pure }, null, null, out var ignored).Contains(water),
                "Normal medicine mode considered direct-only impure water");
            var input = new BatchInputs { directForm = true, productId = productId, raw = new GameItem[0],
                supplies = new GameItem[] { drug, pure, water, Filter() }, chamber = Mixture(), finisherLoss = loss, requestedGrade = 99 };
            var planner = Calculate(input, out var timing); var plan = planner.Result;
            Check(plan != null && plan.minimumActive == 0, "Impure water failed direct forming: " + container + "/" + productId);
            var draws = plan.actions.Where(a => a.operation == Operation.Pour).ToArray();
            Check(draws.Length == 1 && draws[0].source == 2 && draws[0].amount == 180000 && plan.reagents == 180000, "Direct liquids were not reserved by exact source and amount");
            Check(plan.actions.All(a => a.operation != Operation.Blend && a.operation != Operation.Filter), "Water container was blended or filtered");
            var reference = ReferenceMeasure(new SimulationItem(water), loss, ChemicalRecipe.Get(productId));
            Check(reference.Bottles == plan.expectedBottles && reference.MinimumBottleActiveVolume == plan.minimumActive, "Impure-water yield differs from native finishing");
            Check(BatchPlanner.Revalidate(input, plan) != null && Snapshot(water) == original, "Preview/revalidation changed the water source");
            checks += BatchPersistenceTests.CheckRecipe(input, plan);
            ChemistryOperations.SetAmount(water, "dream_cap_extract", 1);
            Check(BatchPlanner.Revalidate(input, plan) == null, "Changed medicinal liquid was not protected before start");
        }
        var source = Mixture("water", 18000, "physical_contaminant", 6000);
        var smallTank = Mixture(); smallTank.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(12000);
        var partial = new BatchInputs { directForm = true, productId = "dream_dust", raw = new GameItem[0], supplies = new GameItem[] { source }, chamber = smallTank };
        var partialPlan = Calculate(partial, out var smallTiming).Result;
        Check(partialPlan != null && partialPlan.reagents == 12000, "Impure water did not respect free tank capacity");
        checks += BatchPersistenceTests.CheckRecipe(partial, partialPlan);
        partial.supplies = new GameItem[] { Mixture("water", 100000) };
        Check(Calculate(partial, out smallTiming).Result == null, "Pure water generated fake medicine");
        foreach (string reagent in new[] { "alcohol", "acid", "base", "reducing_agent", "stabilizing_agent", "lipobond_agent" })
        {
            var mixture = Mixture("water", 100000, "physical_contaminant", 5000, reagent, 10000);
            Check(DirectFormInputs.CanUseLiquid(mixture) == (reagent != "alcohol" && reagent != "lipobond_agent"), "Direct reagent/alcohol eligibility changed");
            Check(RecoveredSolvent.Supplies(new GameItem[] { mixture }, null, null, out var ignored).Contains(mixture), "Real mixed reagent lost normal-mode availability");
        }
        var modules = (GameSlotInventory)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameSlotInventory));
        var core = new SimulationItem(); core.EnableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG);
        int cycles = 0; core.onCycleEndSlotItemFunc = (item, inv, slot) => cycles++;
        var binding = new DreamDustAutomation.HardwareBinding(core, modules, () => true, true, 300);
        binding.Refresh();
        Check(!MachineTurboBoosterAdv.IsTurboBoostEligible(core), "Bound core still accepts wasted turbo charges");
        var shell = new SimulationItem(); shell.EnableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG);
        shell.onCycleEndSlotItemFunc = (item, inv, slot) => binding.Cycle(core, modules, null);
        MachineTurboBoosterAdv.BoostMachine(shell);
        Check(cycles == 1, "Native turbo did not forward exactly one evaporation cycle");
        binding.Dispose();
        Check(MachineTurboBoosterAdv.IsTurboBoostEligible(core), "Detached evaporator lost native turbo eligibility");
        MachineTurboBoosterAdv.BoostMachine(core);
        Check(cycles == 2, "Detached evaporator callback not restored");
        core.identifier = "evaporator"; core.DisableTag(MachineTurboBoosterAdv.TURBO_BOOST_ELIGIBLE_TAG);
        binding = new DreamDustAutomation.HardwareBinding(core, modules, () => false, true, 300);
        binding.Refresh(); binding.Dispose();
        Check(MachineTurboBoosterAdv.IsTurboBoostEligible(core), "Previously saved bound evaporator lost eligibility on detach");
        var openCore = new SimulationItem { identifier = "evaporator" };
        int nativeCycles = 0, routedCycles = 0, originalTargetCalls = 0;
        bool dryReady = true;
        openCore.onCycleEndSlotItemFunc = (item, inv, slot) => nativeCycles++;
        Action<GameItem, GameItem> oldTarget = (s, t) => originalTargetCalls++;
        openCore.onItemTargetThisFunc = oldTarget;
        var openBinding = new DreamDustAutomation.HardwareBinding(openCore, modules, () => true, true, 300);
        openBinding.BindBoost(() => dryReady, () => { routedCycles++; openBinding.Cycle(openCore, modules, null); });
        Check(MachineTurboBoosterAdv.IsTurboBoostEligible(openCore), "Visible embedded evaporator rejected a ready turbo");
        foreach (string boosterId in new[] { "turbo_booster", "turbo_booster_adv", "turbo_booster_hypertech" })
        {
            var booster = new SimulationItem { identifier = boosterId };
            // Native GameItem.Target calls the source before the target. Its
            // cycle is detached; native charge consumption happens before our
            // receiver forwards exactly one cycle to the shell controller.
            MachineTurboBoosterAdv.BoostMachine(openCore);
            Check(nativeCycles == routedCycles, "Embedded core retained a duplicate native cycle callback");
            booster.DisableTag(MachineTurboBoosterAdv.TURBO_READY_TAG);
            openCore.onItemTargetThisFunc(booster, openCore);
            Check(nativeCycles == routedCycles && routedCycles == originalTargetCalls, "Open-window turbo did not run exactly one native cycle");
        }
        Check(routedCycles == 3, "All three native boosters must work through the visible core");
        openCore.onItemTargetThisFunc(new SimulationItem { identifier = "other_tool" }, openCore);
        Check(routedCycles == 3, "Unrelated item triggered evaporation");
        dryReady = false; openBinding.Refresh();
        Check(!MachineTurboBoosterAdv.IsTurboBoostEligible(openCore), "Idle/unpowered core still accepts boosters");
        openCore.onItemTargetThisFunc(new SimulationItem { identifier = "turbo_booster" }, openCore);
        Check(routedCycles == 3, "Stale targeting bypassed current readiness");
        openBinding.Dispose();
        Check(openCore.onItemTargetThisFunc == oldTarget, "Detaching left a stale mod turbo receiver");
        MachineTurboBoosterAdv.BoostMachine(openCore);
        Check(nativeCycles == 4 && routedCycles == 3, "Detached core failed to regain its single vanilla cycle");
        summaries.Add("Machine usability: " + (checks-before) + " checks; exclusive inventory roles, 42 impure-water native-yield/save-resume cases, partial draws, medicinal protection and native turbo callback forwarding.");
    }

    public static string Run()
    {
        checks += BatchPersistenceTests.Run();
        // These are data-only objects; any accidental native sprite refresh fails
        // outside Unity, reproducing the reported preview failure.
        var original = Mixture("alcohol", 175001, "water", 74999);
        original.identifier = "mini_alcohol_bottle";
        string unchanged = Snapshot(original);
        foreach (int volume in new[] { 0, 1, 3, 2000, 249999, 250000 })
        {
            var copy = new SimulationItem(original);
            var before = ChemistryOperations.Parts(copy);
            var expected = WaterHelper.SplitProportionally(before, volume);
            var transferred = ChemistryOperations.RemoveAndReturn(copy, volume);
            Check(transferred.Sum(p => p.Value) == volume, "Removed volume must match request");
            Check(ChemistryOperations.Total(copy) == 250000 - volume, "Source volume must be conserved");
            for (int i = 0; i < before.Length; i++)
                Check(ChemistryOperations.Amount(copy, Liquid.Liquids[i].identifier) == before[i] - expected[i], "Native proportional rounding mismatch");
            Check(Snapshot(original) == unchanged, "Preview mutated a source");
        }
        var source = new SimulationItem(original);
        var target = Mixture("water", 17);
        ChemistryOperations.Pour(source, target, 12345);
        Check(ChemistryOperations.Total(source) + ChemistryOperations.Total(target) == 250017, "Pour lost material");
        var product = new SimulationItem();
        var dry = Mixture("dream_cap_extract", 9900, "physical_contaminant", 100);
        ChemistryOperations.TransferToProduct(dry, product, 2000);
        Check(ChemistryOperations.Amount(product, "dream_cap_extract") == 1980, "Bottle composition differs");
        Check(ChemistryOperations.Total(dry) == 8000, "Bottle did not consume exactly 2000 uL");
        var measured = ChemistryOperations.Measure(Mixture("dream_cap_extract", 9900, "physical_contaminant", 100), 0, 0, 0, null);
        Check(measured.Bottles == 5 && measured.MinimumBottleActiveVolume == 1980, "99% preview incorrect");
        var raw = Mixture("dream_cap_mush", 27333, "water", 4000, "protein", 2000, "mineral", 500, "microbe", 2000, "physical_contaminant", 2000);
        raw.EnableTag(ChemicalHelper.BLENDABLE_TAG);
        var mash = new SimulationItem();
        ChemistryOperations.Blend(raw, mash, 35);
        Check(ChemistryOperations.Amount(mash, "dream_cap_mush") == 17766, "Blender loss rounding incorrect");
        var filter = Filter();
        var filterCopy = new SimulationItem(filter);
        var dirty = Mixture("dream_cap_extract", 5000, "plant_matter", 10000);
        ChemistryOperations.FilterAliquot(dirty, filterCopy, ChemistryOperations.Total(dirty));
        Check(ChemistryOperations.Amount(dirty, "dream_cap_extract") == 5000, "Standard filter consumed active extract");
        Check(ChemistryOperations.Amount(dirty, "plant_matter") < 10000, "Filter removed no plant matter");
        Check(DurabilityHelper.GetRealDurability(filterCopy) < 400, "Filter consumed no durability");
        Check(DurabilityHelper.GetRealDurability(filter) == 400, "Preview wore out the original filter");

        for (int upgrades = 0; upgrades <= 4; upgrades++)
        {
            var extraction = Mixture("dream_cap_mush", 53300, "alcohol", 300000, "water", 100000);
            int ticks = 0;
            while (ChemistryOperations.Amount(extraction, "dream_cap_mush") > 0 && ticks++ < 900)
            {
                ChemistryOperations.SimulateHeatedSecond(extraction, upgrades);
                Check(ChemistryOperations.Temperature(extraction) < 50, "Heater overheated raw Dream Cap mush");
            }
            Check(ChemistryOperations.Amount(extraction, "dream_cap_mush") == 0, "Extraction did not finish");
            // Native per-tick rounding may differ from half the initial mush by a few uL.
            Check(ChemistryOperations.Amount(extraction, "dream_cap_extract") >= 26640, "Heat destroyed usable extract");
        }

        var summaries = new List<string>();
        CheckWasteCollection(summaries);
        CheckNativeFilterWear(summaries);
        foreach (bool useFilter in new[] { false, true })
        {
            var supplies = new List<GameItem> { original, Mixture("acid", 250000), Mixture("base", 250000) };
            if (useFilter) supplies.Add(filter);
            var inputs = new BatchInputs { chamber = new SimulationItem(), raw = new GameItem[] { raw, new SimulationItem(raw), new SimulationItem(raw) },
                supplies = supplies.ToArray(), blenderLoss = 35, heaterUpgrades = 0, finisherLoss = 12, requestedGrade = 90 };
            var originals = inputs.raw.Concat(inputs.supplies).Concat(new[] { inputs.chamber }).ToArray();
            var snapshots = originals.Select(Snapshot).ToArray();
            var planner = new BatchPlanner(inputs);
            var search = planner.Search();
            int yields = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double longestStep = 0;
            while (true)
            {
                long start = watch.ElapsedTicks;
                bool more = search.MoveNext();
                longestStep = Math.Max(longestStep, (watch.ElapsedTicks - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (!more) break;
                if (++yields > 100000) throw new Exception("Planner failed to finish");
            }
            watch.Stop();
            Check(planner.Result != null, "Expected a usable fallback plan: " + planner.Error);
            Check(planner.Result.expectedBottles > 0, "Planner produced an empty plan");
            Check(planner.Result.actualGrade < 99, "Unupgraded finisher cannot reach 99%");
            Check(planner.Result.actualGrade == (useFilter ? 80 : 30), "Planner skipped the highest feasible grade");
            int[] expectedGrades = useFilter ? new[] { 90, 80 } : new[] { 90, 80, 60, 45, 30 };
            Check(planner.GradeAttempts.Select(x => x.Grade).SequenceEqual(expectedGrades), "Preview must show the complete fallback path");
            Check(planner.GradeAttempts.Take(expectedGrades.Length - 1).All(x => x.BestOutcome == null), "A higher feasible grade was skipped");
            Check(planner.GradeAttempts.Last().BestOutcome.Bottles == planner.Result.expectedBottles, "Displayed grade does not match selected plan");
            for (int i = 0; i < originals.Length; i++) Check(Snapshot(originals[i]) == snapshots[i], "Search changed input " + i);
            Check(planner.Result.minimumActive * 100 >= planner.Result.actualGrade * 2000, "Accepted grade below target");
            // Replay selected instructions independently of search candidate state.
            var replay = new SimulationItem(inputs.chamber);
            var replaySupplies = inputs.supplies.Select(x => new SimulationItem(x)).ToArray();
            foreach (var step in planner.Result.actions)
                switch (step.operation)
                {
                    case Operation.Blend: ChemistryOperations.Blend(new SimulationItem(inputs.raw[step.source]), replay, inputs.blenderLoss); break;
                    case Operation.Pour: ChemistryOperations.Pour(replaySupplies[step.source], replay, step.amount); break;
                    case Operation.Extract: for (int n = 0; n < step.amount; n++) ChemistryOperations.SimulateHeatedSecond(replay, inputs.heaterUpgrades); break;
                    case Operation.React: for (int n = 0; n < step.amount; n++) ChemistryOperations.ReactionTick(replay); break;
                    case Operation.Filter: FiltrationPolicy.Apply(replay, step, i => replaySupplies[i]); break;
                    case Operation.Dry: ChemistryOperations.SimulateDry(replay); break;
                }
            var replayed = ChemistryOperations.Measure(replay, inputs.finisherLoss, 0, 0, null);
            Check(replayed.Bottles == planner.Result.expectedBottles, "Plan replay bottle count differs from preview");
            Check(replayed.MinimumBottleActiveVolume == planner.Result.minimumActive, "Plan replay concentration differs from preview");
            checks += BatchPersistenceTests.CheckRecipe(inputs, planner.Result);
            // Containers naturally warm/cool while the player reads the preview.
            foreach (double temperature in new[] { 24.0, 25.5, 26.0 })
            {
                var changed = new BatchInputs { chamber = new SimulationItem(inputs.chamber),
                    raw = inputs.raw.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
                    supplies = inputs.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
                    blenderLoss = inputs.blenderLoss, heaterUpgrades = inputs.heaterUpgrades,
                    finisherLoss = inputs.finisherLoss, requestedGrade = inputs.requestedGrade };
                foreach (var item in changed.raw.Concat(changed.supplies).Concat(new[] { changed.chamber }))
                    item.ModifyTag(ChemicalHelper.CONTAINER_TEMP_TAG, s => s.SetDouble(temperature));
                var verified = BatchPlanner.Revalidate(changed, planner.Result);
                Check(verified != null, "Ambient temperature drift invalidated a feasible preview");
                Check(verified.expectedBottles == planner.Result.expectedBottles && verified.actualGrade == planner.Result.actualGrade,
                    "Start validation changed the promised grade or bottle count");
            }
            summaries.Add((useFilter ? "With filter" : "Without filter") + ": " + planner.Result.actualGrade + "%, " + planner.Result.expectedBottles + " bottles, " + planner.CandidatesChecked + " candidates; " + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms total; " + longestStep.ToString("F1") + " ms longest step");
        }
        CheckExactBottling();
        CheckReactionCountdown();
        CheckEvaporationEstimate();
        CheckDirectForm(summaries);
        CheckProductLabels(summaries);
        CheckCategoryLabels(summaries);
        CheckFormulaDosing(summaries, raw);
        CheckAllRecipes(summaries);
        CheckAuditRegressions(summaries);
        CheckFeedstockSelection(summaries);
        CheckFantaContaminantSelection(summaries);
        CheckSelectionAlternatives(summaries);
        CheckResidueSafeguards();
        CheckTankCleanup();
        CheckMidBatchInputs();
        CheckRecoveredSolvent(summaries);
        CheckCollectedLiquidBinding();
        CheckConcentrationAdvice(summaries);
        CheckStagedExtraction(summaries);
        summaries.Add(OutputPerformanceTests.Run());
        summaries.Add(InputGuidanceTests.Run());
        summaries.Add(InventorySortTests.Run());
        CheckUsability(summaries);
        summaries.Add(QuantityPlanningTests.CheckAll());
        summaries.Add(ReportedBugsTests.Run());
        summaries.Add(DirectSolventTests.Run());
        summaries.Add(InjectorRegressionTests.Run());
        summaries.Add(HeatedDryingTests.Run());
        summaries.Add(PlanningDiagnosticsTests.Run());
        summaries.Add(SeparateDirectTests.Run());
        summaries.Add(DirectDryingTests.Run());
        summaries.Add(ProcessingTimeTests.Run());
        summaries.Add(CoffeeStagingTests.Run());
        summaries.Add(LiveModuleTests.Run());
        summaries.Add(NormalDryingTests.Run());
        summaries.Add(FiltrationOrderTests.Run());
        summaries.Add(StockQuantityAuditTests.Run());
        summaries.Add(DeferredOutputTests.Run());
        summaries.Add(BatchRecoveryTests.Run());
        summaries.Add(WorkshopFeedbackTests.Run());
        summaries.Add(NightReactionsTests.Run());
        summaries.Add(TankCleanupTests.Run());
        summaries.Add(ResidueSnapshotTests.Run());
        summaries.Add(ControllerFlowTests.Run());
        summaries.Add(FantaPlanningTests.Run());
        summaries.Add(StagedResidueTests.Run());
        summaries.Add(ForeignReagentTests.Run());
        summaries.Add(PartialStockTests.Run());
        summaries.Add(CoffeeQualityTests.Run());
        return checks + " chemistry checks passed\n" + string.Join("\n", summaries);
    }
}
