using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        private sealed class Feed
        {
            internal int index;
            internal bool solid;
            internal GameItem item;
            internal GameItem contents;
            internal double quality;
            internal double selectedQuality;
        }
        private List<Feed> feeds;
        private static int DryVolume(GameItem item) => ChemistryOperations.Total(item) - ChemistryOperations.VolatileVolume(item);
        private bool HasPrecursor(GameItem item) =>
            (recipe.Mush != null && ChemistryOperations.Amount(item, recipe.Mush) > 0) ||
            (recipe.Id == "fanta_pill" && ChemistryOperations.Amount(item, "methylol") > 0) ||
            (recipe.Id == "pink_alt_injector" && ChemistryOperations.Amount(item, "dream_cap_extract") > 0);

        private Route EmptyFeed()
        {
            var route = new Route { batch = new SimulationItem(inputs.chamber),
                supplies = inputs.supplies.Select(x => new SimulationItem(x)).ToArray(),
                plan = new BatchPlan { productId = recipe.Id, requestedGrade = inputs.requestedGrade,
                    alcoholPercent = AlcoholBudget.Percent(inputs.alcoholPercent), actions = new BatchAction[0] } };
            if (inputs.mashBuffer != null && ChemistryOperations.Total(inputs.mashBuffer) > 0)
            {
                int volume = Math.Max(0, ChemistryOperations.Total(inputs.mashBuffer) - inputs.reservedBufferVolume);
                if (volume > 0)
                {
                    ChemistryOperations.Pour(new SimulationItem(inputs.mashBuffer), route.batch, volume);
                    AddStep(route, new BatchAction(Operation.FeedMash, amount: volume));
                }
            }
            return route;
        }

        private void KeepFeed(Route route, List<Route> result)
        {
            if (recipe.FeedstockAmount(route.batch) == 0) return;
            string key = string.Join(";", route.plan.actions.Select(a => (int)a.operation + ":" + a.source + ":" + a.amount));
            if (selectedFeeds.Add(key)) result.Add(CloneRoute(route));
        }

        private void LoadFeed(Route route, Feed feed, int amount)
        {
            if (feed.solid)
            {
                bool blend = feed.item.IsTag(ChemicalHelper.BLENDABLE_TAG);
                if (blend) ChemistryOperations.Blend(new SimulationItem(feed.item), route.batch, inputs.blenderLoss);
                else ChemistryOperations.Dissolve(new SimulationItem(feed.item), route.batch);
                AddStep(route, new BatchAction(blend ? Operation.Blend : Operation.Dissolve, feed.index));
            }
            else
            {
                ChemistryOperations.Pour(route.supplies[feed.index], route.batch, amount);
                AddStep(route, new BatchAction(Operation.Pour, feed.index, amount));
                route.plan.reagents = checked(route.plan.reagents + amount);
            }
        }

        private IEnumerable<int> SelectFeedstocks(List<Route> result)
        {
            InitializeSelectionQuality();
            feeds = new List<Feed>();
            if (inputs.feedPrepared) { KeepFeed(EmptyFeed(), result); yield break; }
            for (int i = 0; i < inputs.raw.Length + inputs.supplies.Length; i++)
            {
                bool solid = i < inputs.raw.Length;
                int index = solid ? i : i - inputs.raw.Length;
                var item = solid ? inputs.raw[index] : inputs.supplies[index];
                if (recipe.FeedstockAmount(item) == 0 || (solid ?
                    !item.IsTag(ChemicalHelper.BLENDABLE_TAG) && !item.IsTag("SOLUBLE_TAG") :
                    !item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))) continue;
                GameItem contents = item;
                if (solid)
                {
                    contents = new SimulationItem();
                    contents.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
                    if (item.IsTag(ChemicalHelper.BLENDABLE_TAG)) ChemistryOperations.Blend(new SimulationItem(item), contents, inputs.blenderLoss);
                    else ChemistryOperations.Dissolve(new SimulationItem(item), contents);
                }
                double potential = ChemistryOperations.Amount(contents, recipe.Active);
                if (recipe.Mush != null) potential += ChemistryOperations.Amount(contents, recipe.Mush) *
                    (recipe.Id == "caffeine_pill" ? 0.75 : recipe.Id == "pink_alt_injector" ? 0.3 : 0.5);
                if (recipe.Id == "fanta_pill") potential += ChemistryOperations.Amount(contents, "methylol") * 0.1;
                if (recipe.Id == "pink_alt_injector") potential += ChemistryOperations.Amount(contents, "dream_cap_extract") * 0.6;
                feeds.Add(new Feed { index = index, solid = solid, item = item, contents = contents,
                    quality = potential / Math.Max(1, DryVolume(contents)) });
                yield return 0;
            }
            feeds = feeds.OrderByDescending(f => f.quality)
                .ThenByDescending(f => (double)DryVolume(f.contents) / Math.Max(1, ChemistryOperations.Total(f.item)))
                .ThenBy(f => f.solid ? 0 : 1).ThenBy(f => f.index).ToList();

            // Keep the original bulk and raw-purity choices. A precursor bottle
            // with removable particles may rank below one with persistent
            // contamination before filtering, so also retain that cleaner route.
            // Compare filtration and chemical cleanup separately; keep old
            // choices for limited filters/reagents. No subset or dose enumeration.
            bool distinct = feeds.Count > 1 && feeds.Last().quality < feeds[0].quality - 1e-12;
            var selections = new List<List<Feed>> { feeds };
            if (distinct) selections.Add(feeds.Where(f => f.quality >= feeds[0].quality - 1e-12).ToList());
            if (feeds.Count > 1)
            {
                for (int policy = 1; policy <= 2; policy++)
                {
                    foreach (var feed in feeds)
                    {
                        feed.selectedQuality = FeedQuality(feed, policy);
                        yield return 0;
                    }
                    double best = feeds.Max(f => f.selectedQuality);
                    var cleaner = feeds.Where(f => f.selectedQuality >= best - 1e-12).ToList();
                    if (!selections.Any(s => s.SequenceEqual(cleaner))) selections.Add(cleaner);
                }
            }
            // Equal-quality stock can exceed finite filter/reagent capacity as
            // a group. Keep bounded individual alternatives as well as the bulk
            // choices; otherwise adding an identical bottle may force fallback.
            var individuals = new List<List<Feed>>();
            foreach (var selection in selections)
            {
                if (selection.Count < 2) continue;
                Func<Feed, double> potential = f => f.quality * Math.Max(1, DryVolume(f.contents));
                var ordered = selection.OrderByDescending(potential).ThenBy(f => ChemistryOperations.Total(f.contents)).ToArray();
                var smallest = ordered.Where(f => potential(f) >= recipe.UnitVolume).LastOrDefault();
                foreach (var feed in new[] { ordered[0], smallest }.Where(f => f != null).Distinct())
                {
                    var single = new List<Feed> { feed };
                    if (!selections.Concat(individuals).Any(s => s.SequenceEqual(single))) individuals.Add(single);
                }
            }
            selections.AddRange(individuals);
            foreach (var selection in selections)
            {
                Route lastUnreserved = null;
                for (int mode = 0; mode < 2; mode++)
                {
                    bool reserve = mode == 1;
                    if (reserve && lastUnreserved != null && RequiredCapacity(lastUnreserved) <= WaterHelper.GetTotalCapacity(lastUnreserved.batch))
                        continue;
                    var route = EmptyFeed();
                    foreach (var feed in selection)
                    {
                        int total = ChemistryOperations.Total(feed.item);
                        int amount = Math.Min(total, WaterHelper.GetFreeCapacity(route.batch));
                        if (feed.solid && amount < total) { Capacity("diag_solid_capacity", route.batch, total, 5); continue; }
                        if (amount <= 0) { Capacity("diag_feed_capacity", route.batch, total, 5); continue; }
                        if (reserve)
                        {
                            // Solve only the capacity boundary. Each predicate uses
                            // native transfer rounding and composition-based doses.
                            if (feed.solid)
                            {
                                var trial = CloneRoute(route);
                                LoadFeed(trial, feed, total);
                                if (RequiredCapacity(trial) > WaterHelper.GetTotalCapacity(route.batch)) continue;
                            }
                            else
                            {
                                int low = 0, high = amount;
                                while (low < high)
                                {
                                    int middle = low + (high - low + 1) / 2;
                                    var trial = CloneRoute(route);
                                    LoadFeed(trial, feed, middle);
                                    if (RequiredCapacity(trial) <= WaterHelper.GetTotalCapacity(route.batch)) low = middle;
                                    else high = middle - 1;
                                    yield return 0;
                                }
                                amount = low;
                            }
                        }
                        if (amount > 0) LoadFeed(route, feed, amount);
                        yield return 0;
                    }
                    if (!reserve) lastUnreserved = route;
                    KeepFeed(route, result);
                }
            }
        }

        // A conservative peak-volume envelope for the native reaction route.
        // Reserve actual solution volume, credit reagents already in the tank,
        // and debit shared stock so a mixed bottle is not counted twice.
        private long RequiredCapacity(Route route)
        {
            var batch = new SimulationItem(route.batch);
            batch.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(int.MaxValue);
            var stock = route.supplies.Select(x => new SimulationItem(x)).ToArray();
            int used = 0;
            var ignored = new List<BatchAction>();
            Func<string, int, bool> reserve = (id, demand) =>
            {
                Dose(batch, stock, id, Math.Max(0, demand - IngredientAmount(batch, id)), ignored, ref used, requireAll: false, policy: route.reagentPolicy);
                return IngredientAmount(batch, id) >= demand;
            };
            int mush = recipe.Mush == null ? 0 : ChemistryOperations.Amount(batch, recipe.Mush);
            if (mush > 0)
            {
                string solvent = "alcohol";
                if (recipe.Id == "caffeine_pill" && ChemistryOperations.Amount(batch, solvent) == 0 &&
                      !stock.Any(x => recipe.CanUseReagent(x) && ChemistryOperations.Amount(x, solvent) > 0)) solvent = "water";
                long demand = solvent == "water" ? 5L * Math.Max(1, (int)Math.Round(mush * .05)) : AlcoholBudget.FullDose(recipe, mush);
                if (solvent == "alcohol" && AlcoholBudget.Supports(recipe)) demand = (demand * AlcoholBudget.Percent(inputs.alcoholPercent) + 99) / 100;
                if (!reserve(solvent, (int)Math.Min(int.MaxValue, demand))) return long.MaxValue;
            }
            long peak = ChemistryOperations.Total(batch);
            int stabilizer = recipe.Id == "fanta_pill" ? ChemistryOperations.Amount(batch, "stabilizing_agent") : 0;
            int cleanup = CleanupDemand(batch);
            if (stabilizer > 0 || cleanup > 0)
            {
                int demand = checked(cleanup + (stabilizer == 0 ? 0 : Math.Max(100, (stabilizer + 3) / 4)));
                // Use the smaller available neutralizer solution for the volume
                // envelope; both chemical routes remain verified by Produce.
                var acidBatch = new SimulationItem(batch);
                var acidStock = stock.Select(x => new SimulationItem(x)).ToArray();
                var baseBatch = new SimulationItem(batch);
                var baseStock = stock.Select(x => new SimulationItem(x)).ToArray();
                int a = 0, b = 0;
                Dose(acidBatch, acidStock, "acid", Math.Max(0, demand - IngredientAmount(batch, "acid")), new List<BatchAction>(), ref a, false, route.reagentPolicy);
                Dose(baseBatch, baseStock, "base", Math.Max(0, demand - IngredientAmount(batch, "base")), new List<BatchAction>(), ref b, false, route.reagentPolicy);
                if (stabilizer > 0 && Math.Max(IngredientAmount(acidBatch, "acid"), IngredientAmount(baseBatch, "base")) < Math.Max(100, (stabilizer + 3) / 4))
                    return long.MaxValue;
                bool useAcid = IngredientAmount(acidBatch, "acid") > 0 &&
                    (IngredientAmount(baseBatch, "base") <= 0 || (IngredientAmount(acidBatch, "acid") >= demand && IngredientAmount(baseBatch, "base") < demand) ||
                        (IngredientAmount(baseBatch, "base") < demand && a <= b) ||
                        (IngredientAmount(acidBatch, "acid") >= demand && a <= b));
                batch = useAcid ? acidBatch : baseBatch;
                stock = useAcid ? acidStock : baseStock;
            }
            peak = Math.Max(peak, ChemistryOperations.Total(batch));
            if (recipe.Id == "fanta_pill" && !reserve("reducing_agent",
                (int)Math.Min(int.MaxValue, ((long)ChemistryOperations.Amount(batch, "methylol") * 3 + 3) / 4))) return long.MaxValue;
            if (recipe.Id == "pink_alt_injector")
            {
                // Native intermediate evaporation frees solvent room before the
                // second reaction. Do not reserve both stages simultaneously.
                ChemistryOperations.SimulateDry(batch);
                RecoveredSolvent.AfterEvaporation(stock, inputs.recoveredSource);
                int extract = ChemistryOperations.Amount(batch, "dream_cap_extract") + mush / 2;
                if (!reserve("reducing_agent", extract) ||
                    !reserve("lipobond_agent", (extract + 3) / 4 + (ChemistryOperations.Amount(batch, "fat") + 7) / 8)) return long.MaxValue;
            }
            else reserve("lipobond_agent", (ChemistryOperations.Amount(batch, "fat") + 7) / 8);
            return Math.Max(peak, ChemistryOperations.Total(batch));
        }

        private IEnumerable<int> SelectDirectBlends(int grade, List<Route> result)
        {
            if (inputs.feedPrepared) yield break;
            if (HasPrecursor(inputs.chamber)) yield break;
            var direct = feeds.Where(f => !HasPrecursor(f.contents) && ChemistryOperations.Amount(f.contents, recipe.Active) > 0).ToArray();
            if (direct.Length == 0) yield break;
            int retained = recipe.UnitVolume - recipe.UnitVolume * Math.Max(0, Math.Min(100, inputs.finisherLoss)) / 100;
            int minimum = (recipe.UnitVolume * grade + 99) / 100;
            // The second boundary protects one microlitre of native per-unit
            // rounding. Exact finishing, not this average, accepts the result.
            for (int rounding = 0; rounding <= 1; rounding++)
            {
                int required = minimum + rounding;
                if (required > retained) continue;
                Func<GameItem, long> margin = item => (long)ChemistryOperations.Amount(item, recipe.Active) * retained - (long)DryVolume(item) * required;
                var route = EmptyFeed();
                bool keptPure = false;
                foreach (var feed in direct)
                {
                    long sourceMargin = margin(feed.contents);
                    if (sourceMargin < 0 && !keptPure) { KeepFeed(route, result); keptPure = true; }
                    int total = ChemistryOperations.Total(feed.item);
                    int take = Math.Min(total, WaterHelper.GetFreeCapacity(route.batch));
                    if (sourceMargin < 0)
                    {
                        long availableMargin = margin(route.batch);
                        if (availableMargin <= 0) continue;
                        take = (int)Math.Min(take, Math.Floor((decimal)availableMargin * total / -sourceMargin));
                    }
                    if (take <= 0 || (feed.solid && take < total)) continue;
                    var trial = CloneRoute(route);
                    LoadFeed(trial, feed, take);
                    // Check the native splitter at the calculated boundary;
                    // retain a conservative neighbour if rounding crossed it.
                    if (sourceMargin < 0 && margin(trial.batch) < 0)
                    {
                        if (feed.solid) continue;
                        int low = 0, high = take;
                        while (low < high)
                        {
                            int middle = low + (high - low + 1) / 2;
                            var probe = CloneRoute(route);
                            LoadFeed(probe, feed, middle);
                            if (margin(probe.batch) >= 0) low = middle; else high = middle - 1;
                            yield return 0;
                        }
                        if (low == 0) continue;
                        trial = CloneRoute(route);
                        LoadFeed(trial, feed, low);
                    }
                    route = trial;
                    yield return 0;
                }
                KeepFeed(route, result);
            }
        }
    }
}
