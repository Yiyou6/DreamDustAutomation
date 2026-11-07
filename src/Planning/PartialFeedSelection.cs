using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        private readonly Dictionary<string, List<Route>> preparedFeeds = new Dictionary<string, List<Route>>();
        private sealed class FeedAttempt
        {
            internal ProductionOutcome cleanest;
            internal int[] parts;
            internal List<Route> prepared;
            internal bool reagentLimited, filterChecked;
            internal int filterCeiling = -1;
        }
        private readonly Dictionary<Route, FeedAttempt> feedAttempts = new Dictionary<Route, FeedAttempt>();
        private readonly Dictionary<string, FeedAttempt> seedAttempts = new Dictionary<string, FeedAttempt>();
        private readonly List<KeyValuePair<Route, FeedAttempt>> subsetAttempts = new List<KeyValuePair<Route, FeedAttempt>>();
        private FeedAttempt measuringFeed;
        private FeedAttempt preparingFeed;
        private static string FeedKey(Route seed) => string.Join(";", seed.plan.actions.Select(a => (int)a.operation + ":" + a.source + ":" + a.amount));
        private static bool FeedAction(BatchAction action) => action.operation == Operation.Blend || action.operation == Operation.Dissolve || action.operation == Operation.Pour;
        private static int FeedUnits(BatchAction action) => action.operation == Operation.Pour ? action.amount : 1;
        private static bool StartsWith(BatchPlan plan, Route seed) => plan.actions.Length >= seed.plan.actions.Length &&
            seed.plan.actions.Select((a, i) => a.operation == plan.actions[i].operation && a.source == plan.actions[i].source && a.amount == plan.actions[i].amount).All(equal => equal);

        private void BeginWholeFeed(Route seed, List<Route> prepared)
        {
            var evidence = new FeedAttempt { prepared = prepared };
            seedAttempts[FeedKey(seed)] = evidence;
            preparingFeed = evidence;
        }
        private void EndWholeFeed(List<Route> prepared)
        {
            foreach (var route in prepared) feedAttempts[route] = preparingFeed;
            preparingFeed = null;
        }
        private IEnumerable<int> EvaluateFeed(Route route, int grade)
        {
            feedAttempts.TryGetValue(route, out measuringFeed);
            foreach (var ignored in FindFiltrationPlans(route.batch, route.supplies, route.plan, grade)) yield return 0;
            measuringFeed = null;
        }
        private void RecordWholeFeed(GameItem batch, ProductionOutcome outcome)
        {
            if (measuringFeed == null || outcome.Bottles == 0 ||
                (measuringFeed.cleanest != null && outcome.MinimumBottleActiveVolume <= measuringFeed.cleanest.MinimumBottleActiveVolume)) return;
            measuringFeed.cleanest = outcome;
            measuringFeed.parts = ChemistryOperations.Parts(batch);
        }
        // A one-time resource diagnostic also prevents amount searches when
        // persistent impurities, rather than available filter wear, set a cap.
        // Only detached clones get extra durability; these are never outcomes.
        private IEnumerable<int> CheckFilterCeiling(FeedAttempt full)
        {
            if (full.filterChecked) yield break;
            full.filterChecked = true;
            foreach (var route in full.prepared)
            {
                var batch = new SimulationItem(route.batch);
                var filters = route.supplies.Where(FiltrationPolicy.Usable).Select(f => new SimulationItem(f)).ToArray();
                if (filters.Length == 0) continue;
                foreach (var filter in filters.Where(f => f.identifier != "uv_filter"))
                    foreach (var tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
                        filter.state.GetTag(tag).SetInt(1000000);
                for (int pass = 0; pass < 8; pass++)
                {
                    var before = ChemistryOperations.Parts(batch);
                    ChemistryOperations.FilterAliquot(batch, filters, ChemistryOperations.Total(batch));
                    var measured = ChemistryOperations.Measure(batch, inputs.finisherLoss, 0, 0, null, recipe, inputs.maxProducts);
                    if (measured.Bottles > 0) full.filterCeiling = Math.Max(full.filterCeiling, measured.MinimumBottleActiveVolume);
                    if (ChemistryOperations.SameParts(before, ChemistryOperations.Parts(batch))) break;
                    yield return 0;
                }
                yield return 0;
            }
        }
        private IEnumerable<int> PartialFeedAdvice(BatchPlan plan)
        {
            if (plan.outputLimited) yield break;
            foreach (var pair in subsetAttempts)
            {
                if (!StartsWith(plan, pair.Key)) continue;
                var full = pair.Value;
                if (full.cleanest == null || GradePolicy.Meets(full.cleanest, plan.actualGrade)) continue;
                // A resource-limited winner can deliberately leave protein to
                // save scarce filter wear. Check whether extra durability alone
                // would let the WHOLE batch qualify before suggesting reagents.
                // These read-only diagnostic clones never enter the candidates.
                foreach (var ignored in CheckFilterCeiling(full)) yield return 0;
                if ((long)full.filterCeiling * 100 >= (long)plan.actualGrade * recipe.UnitVolume)
                { plan.quantityHint = "purity_filter_more"; yield break; }
                // Explain the tested WHOLE batch, not the successful small one.
                // An amount limit is independent of reaching the requested grade.
                plan.quantityHint = ConcentrationAdvice.Get(inputs, new BatchPlan {
                    productId = recipe.Id, requestedGrade = plan.actualGrade,
                    actualGrade = GradePolicy.Grades.FirstOrDefault(g => g < plan.actualGrade),
                    minimumActive = full.cleanest.MinimumBottleActiveVolume }, full.parts);
                yield break;
            }
        }

        private Route FeedPrefix(Route seed, int units)
        {
            var prefix = EmptyFeed();
            foreach (var action in seed.plan.actions.Where(FeedAction))
            {
                if (units <= 0) break;
                bool solid = action.operation != Operation.Pour;
                var feed = feeds.First(f => f.solid == solid && f.index == action.source);
                int take = solid ? ChemistryOperations.Total(feed.item) : Math.Min(units, action.amount);
                LoadFeed(prefix, feed, take);
                units -= solid ? 1 : take;
            }
            return prefix;
        }

        // Search the amount of feed the available cleanup stock can handle.
        // Doses remain composition-based; every candidate uses native reactions,
        // filter wear and per-unit finishing. Retain existing full/single routes
        // so rounding or heterogeneous stock can never discard a known result.
        private IEnumerable<int> ExploreFeedAmounts(List<Route> seeds, List<Route> routes, int grade)
        {
            if (inputs.feedPrepared || grade > 100 - inputs.finisherLoss) yield break;
            var checkedHere = new Dictionary<string, bool>();
            foreach (var seed in seeds)
            {
                int total = seed.plan.actions.Where(FeedAction).Sum(FeedUnits);
                if (total <= 1 || (winningOutcome != null && winningOutcome.Bottles >= inputs.maxProducts)) continue;
                // Whole stock already meets this grade. Reducing it is only
                // needed when full-batch cleanup failed with the current stock.
                if (outcomes.Any(o => GradePolicy.Meets(o, grade) && StartsWith((BatchPlan)o.Plan, seed))) continue;
                var evidence = seedAttempts[FeedKey(seed)];
                if (!evidence.reagentLimited)
                {
                    foreach (var ignored in CheckFilterCeiling(evidence)) yield return 0;
                    if ((long)evidence.filterCeiling * 100 < (long)grade * recipe.UnitVolume) continue;
                }
                int low = 0, high = total;
                bool qualified = false;
                IEnumerable<int> Probe(int units)
                {
                    var prefix = FeedPrefix(seed, units);
                    string key = FeedKey(prefix);
                    if (checkedHere.TryGetValue(key, out qualified)) yield break;
                    if (seedAttempts.TryGetValue(FeedKey(seed), out var full))
                        subsetAttempts.Add(new KeyValuePair<Route, FeedAttempt>(prefix, full));
                    if (!preparedFeeds.TryGetValue(key, out var prepared))
                    {
                        prepared = new List<Route>();
                        foreach (var ignored in PrepareAlternatives(prefix, prepared)) yield return 0;
                        preparedFeeds[key] = prepared;
                        routes.AddRange(prepared);
                    }
                    int begin = outcomes.Count;
                    foreach (var route in prepared)
                        foreach (var ignored in EvaluateFeed(route, grade)) yield return 0;
                    qualified = outcomes.Skip(begin).Any(o => GradePolicy.Meets(o, grade));
                    checkedHere[key] = qualified;
                }
                while (high - low > 1)
                {
                    int middle = low + (high - low) / 2;
                    foreach (var ignored in Probe(middle)) yield return 0;
                    if (qualified) low = middle; else high = middle;
                }
                // Native integer reactions and bottling can move the boundary
                // by a small amount. Test its neighbours explicitly.
                for (int units = Math.Max(1, low - 2); units <= Math.Min(total - 1, high + 2); units++)
                    foreach (var ignored in Probe(units)) yield return 0;
            }
        }
    }
}
