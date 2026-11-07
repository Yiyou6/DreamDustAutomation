using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Meter feed, then run the normal composition-based chemistry planner on
    // that portion. Never scale a finished recipe's reagent/filter doses: native
    // reactions and integer rounding must be recalculated for the actual batch.
    internal sealed class QuantityPlanning
    {
        private sealed class Feed
        {
            internal bool solid;
            internal int index, volume;
        }
        private sealed class Prepared
        {
            internal BatchInputs input;
            internal List<BatchAction> prefix = new List<BatchAction>();
            internal int poured;
        }
        private readonly BatchInputs source;
        private readonly BatchPlan full;
        private readonly int requested;
        private readonly List<Feed> feeds = new List<Feed>();
        private Prepared prepared;
        private BatchPlan candidate;
        internal BatchPlan Result { get; private set; }
        internal string Error { get; private set; }
        internal PlanningDiagnostics Diagnostics { get; } = new PlanningDiagnostics();
        private void Fail(string key, string detail = null, params object[] args)
        { Error = key; Diagnostics.Add(key, 100, detail, args); }
        internal int Attempts { get; private set; }

        internal QuantityPlanning(BatchInputs input, BatchPlan maximum, int count)
        {
            source = Copy(input);
            full = maximum.Copy();
            requested = count;
        }

        private static BatchInputs Copy(BatchInputs input) => new BatchInputs {
            normalDrying = input.normalDrying, skipDryingComparison = input.skipDryingComparison,
            raw = input.raw.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
            supplies = input.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(),
            chamber = new SimulationItem(input.chamber),
            mashBuffer = input.mashBuffer == null ? null : new SimulationItem(input.mashBuffer),
            productId = input.productId, directForm = input.directForm, separateDirect = input.separateDirect, directDrying = input.directDrying, directSolvents = input.directSolvents, maxProducts = input.maxProducts,
            recoveredSource = input.recoveredSource, requestedGrade = input.requestedGrade,
            alcoholPercent = input.alcoholPercent, blenderLoss = input.blenderLoss,
            heaterUpgrades = input.heaterUpgrades, finisherLoss = input.finisherLoss
        };

        internal IEnumerator Search()
        {
            if (requested <= 0 || requested > full.expectedBottles || requested > source.maxProducts)
            { Fail("quantity_changed"); yield break; }
            if (requested == full.expectedBottles) { Result = full.Copy(); yield break; }
            if (full.separateDirect)
            {
                var limited = Copy(source); limited.maxProducts = requested;
                var planner = new BatchPlanner(limited); var work = planner.Search();
                while (work.MoveNext()) yield return null;
                Diagnostics.CopyFrom(planner.Diagnostics); Error = planner.Error;
                if (planner.Result == null) yield break;
                if (planner.Result.expectedBottles != requested) { Fail("diag_quantity_yield", "diag_quantity_yield_detail", planner.Result.expectedBottles, requested); yield break; }
                Result = planner.Result.Copy(); Result.requestedBottles = requested;
                Result.availableBottles = full.expectedBottles; Result.outputCapacity = source.maxProducts; Result.outputLimited = false;
                Diagnostics.Clear(); Result = BatchPlanner.Revalidate(source, Result, Diagnostics);
                if (Result == null) Error = Diagnostics.Primary?.Key ?? "diag_quantity_recheck";
                yield break;
            }
            var recipe = ChemicalRecipe.Get(full.productId);
            foreach (var action in full.actions)
            {
                if (action.operation == Operation.Blend || action.operation == Operation.Dissolve)
                {
                    if (feeds.Any(x => x.solid && x.index == action.source)) continue;
                    var contents = new SimulationItem();
                    contents.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
                    if (action.operation == Operation.Blend)
                        ChemistryOperations.Blend(new SimulationItem(source.raw[action.source]), contents, source.blenderLoss);
                    else ChemistryOperations.Dissolve(new SimulationItem(source.raw[action.source]), contents);
                    feeds.Add(new Feed { solid = true, index = action.source, volume = ChemistryOperations.Total(contents) });
                }
                else if (action.operation == Operation.Pour && (source.directForm ?
                    DirectFormInputs.CanUseLiquid(source.supplies[action.source], source.directSolvents) : recipe.FeedstockAmount(source.supplies[action.source]) > 0))
                {
                    var feed = feeds.FirstOrDefault(x => !x.solid && x.index == action.source);
                    if (feed == null) { feed = new Feed { index = action.source }; feeds.Add(feed); }
                    feed.volume = checked(feed.volume + action.amount);
                }
                yield return null;
            }
            long available = ChemistryOperations.Total(source.chamber) +
                (long)(source.mashBuffer == null ? 0 : ChemistryOperations.Total(source.mashBuffer));
            foreach (var feed in feeds) available += feed.volume;
            if (available <= 0 || available > int.MaxValue) { Fail(available <= 0 ? "diag_quantity_no_feed" : "diag_quantity_range"); yield break; }

            // Search a material-quantity boundary, not a grid of reagent doses.
            // Each probe yields through the normal planner's small work steps.
            int low = 0, high = (int)available;
            int estimate = Math.Max(1, (int)(available * requested / full.expectedBottles));
            foreach (var ignored in Evaluate(estimate)) yield return null;
            if (candidate != null) { high = estimate; Result = candidate; }
            else
            {
                low = estimate;
                foreach (var ignored in Evaluate(high)) yield return null;
                if (candidate == null) { if (Error == null) Fail("quantity_unavailable"); yield break; }
                Result = candidate;
            }
            while (high - low > 1)
            {
                int middle = low + (high - low) / 2;
                foreach (var ignored in Evaluate(middle)) yield return null;
                if (candidate == null) low = middle;
                else { high = middle; Result = candidate; }
            }
            // A minimum reaction volume can make tiny orders impossible at a
            // very low solvent setting. Never silently process a larger batch
            // and merely stop bottling at the requested number.
            if (Result.outputLimited) { Result = null; Diagnostics.Clear(); Fail("diag_quantity_minimum"); yield break; }
            // Replay the exact prefix, real buffer leftovers and doses together.
            // This catches shared-stock and integer-transfer differences before
            // any live item can be consumed.
            Diagnostics.Clear();
            Result = BatchPlanner.Revalidate(source, Result, Diagnostics);
            if (Result == null) { if (Diagnostics.Primary != null) Error = Diagnostics.Primary.Key; else Fail("diag_quantity_recheck"); }
            else
            {
                Error = null; Diagnostics.Clear();
                if (!source.directForm && source.normalDrying == DirectDryingMethod.Evaporate)
                {
                    var drying = new NormalDryingPlanner(source, Result); var work = drying.Search();
                    while (work.MoveNext()) yield return null;
                    Result = drying.Evaporation;
                }
            }
        }

        private IEnumerable<int> Evaluate(int volume)
        {
            candidate = null;
            prepared = null;
            Error = null;
            Diagnostics.Clear();
            Attempts++;
            foreach (var ignored in Prepare(volume)) yield return 0;
            if (prepared == null) yield break;
            var planner = new BatchPlanner(prepared.input);
            var work = planner.Search();
            while (work.MoveNext()) yield return 0;
            var plan = planner.Result;
            if (plan == null) { Diagnostics.CopyFrom(planner.Diagnostics); Error = planner.Error; yield break; }
            if (plan.expectedBottles != requested)
            { Fail("diag_quantity_yield", "diag_quantity_yield_detail", plan.expectedBottles, requested); yield break; }
            if (plan.actualGrade < full.actualGrade)
            { Fail("diag_quantity_grade", "diag_quantity_grade_detail", plan.actualGrade, full.actualGrade); yield break; }
            plan = plan.Copy();
            plan.actions = prepared.prefix.Concat(plan.actions).ToArray();
            plan.reagents = checked(plan.reagents + prepared.poured);
            plan.recoveredVolume = RecoveredSolvent.DrawnVolume(plan, source.recoveredSource);
            plan.requestedBottles = requested;
            plan.availableBottles = full.expectedBottles;
            plan.outputCapacity = source.maxProducts;
            candidate = plan;
        }

        private IEnumerable<int> Prepare(int volume)
        {
            var working = Copy(source);
            var next = new Prepared { input = working };
            var tank = working.chamber;
            var buffer = working.mashBuffer;
            if (buffer != null && ChemistryOperations.Total(buffer) > 0)
            {
                int amount = ChemistryOperations.Total(buffer);
                if (amount > WaterHelper.GetFreeCapacity(tank)) { Fail("buffer_return_full"); yield break; }
                ChemistryOperations.Pour(buffer, tank, amount);
                next.prefix.Add(new BatchAction(Operation.FeedMash, amount: amount));
            }
            foreach (var feed in feeds)
            {
                int needed = volume - ChemistryOperations.Total(tank);
                if (needed <= 0) break;
                if (feed.solid)
                {
                    var item = working.raw[feed.index];
                    if (ChemistryOperations.Total(item) > WaterHelper.GetFreeCapacity(tank)) { Fail("diag_solid_capacity"); yield break; }
                    bool blend = item.IsTag(ChemicalHelper.BLENDABLE_TAG);
                    if (blend) ChemistryOperations.Blend(item, tank, working.blenderLoss);
                    else ChemistryOperations.Dissolve(item, tank);
                    next.prefix.Add(new BatchAction(blend ? Operation.Blend : Operation.Dissolve, feed.index));
                }
                else
                {
                    int amount = Math.Min(needed, feed.volume);
                    if (amount > WaterHelper.GetFreeCapacity(tank)) { Fail("diag_feed_capacity"); yield break; }
                    if (amount > ChemistryOperations.Total(working.supplies[feed.index])) { Fail("diag_quantity_no_feed"); yield break; }
                    ChemistryOperations.Pour(working.supplies[feed.index], tank, amount);
                    next.prefix.Add(new BatchAction(Operation.Pour, feed.index, amount));
                    next.poured += amount;
                }
                yield return 0;
            }
            int excess = ChemistryOperations.Total(tank) - volume;
            if (excess < 0) { Fail("diag_quantity_no_feed"); yield break; }
            if (excess > 0)
            {
                // Vanilla grinds whole solid items. Reserve the final item's
                // surplus as real slurry, before extraction/cleanup/synthesis.
                if (buffer == null || excess > WaterHelper.GetFreeCapacity(buffer))
                { Fail(buffer == null ? "missing_buffer" : "quantity_buffer_full"); yield break; }
                ChemistryOperations.Pour(tank, buffer, excess);
                next.prefix.Add(new BatchAction(Operation.StoreMash, amount: excess));
            }
            working.raw = new GameItem[0];
            working.feedPrepared = true;
            working.reservedBufferVolume = buffer == null ? 0 : ChemistryOperations.Total(buffer);
            working.maxProducts = requested;
            working.skipDryingComparison = !working.directForm && working.normalDrying == DirectDryingMethod.Evaporate;
            prepared = next;
        }
    }
}
