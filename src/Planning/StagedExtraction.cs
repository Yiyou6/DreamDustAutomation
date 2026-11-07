using System;
using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        private IEnumerable<int> ProduceStaged(Route seed, List<Route> result)
        {
            if (inputs.mashBuffer == null) { Report("missing_buffer", 10); yield break; }
            var route = CloneRoute(seed);
            var buffer = new SimulationItem(inputs.mashBuffer);
            // EmptyFeed returns only unreserved mash. A quantity-limited batch
            // leaves its unused slurry here and must not count it as extra feed.
            int returned = Math.Max(0, ChemistryOperations.Total(buffer) - inputs.reservedBufferVolume);
            if (returned > 0) ChemistryOperations.RemoveAndReturn(buffer, returned);
            int volume = ChemistryOperations.Total(route.batch);
            if (volume > WaterHelper.GetFreeCapacity(buffer)) { Capacity("buffer_full", buffer, volume, 10); yield break; }
            int mush = ChemistryOperations.Amount(route.batch, recipe.Mush);
            int percent = AlcoholBudget.Percent(inputs.alcoholPercent);
            long budget = (AlcoholBudget.FullDose(recipe, mush) * percent + 99) / 100;
            int temperature = AlcoholBudget.Temperature(recipe);
            int carriedAlcohol = ChemistryOperations.Amount(route.batch, "alcohol");
            ChemistryOperations.Pour(route.batch, buffer, volume);
            AddStep(route, new BatchAction(Operation.StoreMash, amount: volume));
            AddReagent(route, "alcohol", (int)Math.Max(0, Math.Min(int.MaxValue, budget - carriedAlcohol)));
            int added = ChemistryOperations.Amount(route.batch, "alcohol");
            if ((long)added + carriedAlcohol < budget) { DoseFailure(route, "alcohol", (int)Math.Min(int.MaxValue, budget - carriedAlcohol), 20); yield break; }
            route.plan.extractionAlcohol = checked(added + carriedAlcohol);
            SolventAdded = Math.Max(SolventAdded, added);

            // Solve an aliquot boundary, never enumerate reagent doses. Reuse the
            // alcohol remaining in the actual reactor after each native reaction.
            // Mixed feed contributes its own alcohol proportionally: never separate
            // a component for free or pretend the source was a pure reagent.
            // Small budgets with alcohol already mixed into the feed may need
            // more than 100 portions to accumulate the reusable solvent. Scale
            // the safety bound with the chosen dose; yield after every portion.
            int maxPortions = Math.Max(128, 16 * ((100 + percent - 1) / percent));
            int remainingFeed = volume;
            for (int portion = 0; remainingFeed > 0 && portion < maxPortions; portion++)
            {
                int total = ChemistryOperations.Total(buffer);
                int remainingMush = ChemistryOperations.Amount(buffer, recipe.Mush);
                int high = Math.Min(remainingFeed, Math.Min(total, WaterHelper.GetFreeCapacity(route.batch)));
                if (high <= 0) { Capacity("diag_cycle_capacity", route.batch, remainingFeed, 25); yield break; }
                int low = 0;
                var parts = ChemistryOperations.Parts(buffer);
                int mushIndex = Liquid.Liquids.FindIndex(x => x.identifier == recipe.Mush);
                int alcoholIndex = Liquid.Liquids.FindIndex(x => x.identifier == "alcohol");
                long solvent = ChemistryOperations.Amount(route.batch, "alcohol");
                long inTank = ChemistryOperations.Amount(route.batch, recipe.Mush);
                // Analytical boundary followed by native split checks avoids the
                // tiny non-monotonic rounding edges of a pure boolean binary search.
                bool coffee = recipe.Id == "caffeine_pill";
                // Coffee's full-yield first tick needs about 3/4 of the mash
                // volume in alcohol. Correct that boundary using native rounding
                // and native proportional splits before committing the transfer.
                long numerator = coffee ? 3 : 5, denominator = coffee ? 4 : 1;
                long deficit = numerator * remainingMush - denominator * ChemistryOperations.Amount(buffer, "alcohol");
                int take = deficit <= 0 ? high : (int)Math.Min(high, Math.Max(0,
                    Math.Floor((double)(denominator * solvent - numerator * inTank) * total / deficit)));
                for (int correction = 0; take > 0 && correction < 8; correction++)
                {
                    var split = WaterHelper.SplitProportionally(parts, take);
                    long shortage = AlcoholBudget.FullDose(recipe, inTank + split[mushIndex]) - solvent - split[alcoholIndex];
                    if (shortage <= 0) { low = take; break; }
                    int correctionVolume = coffee && deficit > 0 ? (int)Math.Min(take,
                        Math.Max(1, Math.Ceiling((double)shortage * denominator * total / deficit))) : 1;
                    take -= correctionVolume;
                }
                if (low == 0)
                {
                    // Alcohol already mixed into feed cannot be isolated without
                    // evaporation. Use a real small portion and simulate the native
                    // lower extraction yield; never fabricate full-efficiency output.
                    low = Math.Min(high, Math.Max(1, (int)(((long)total * percent + 99) / 100)));
                }
                // If the final remainder contains no precursor, transfer it once.
                if (remainingMush == 0) low = high;
                ChemistryOperations.Pour(buffer, route.batch, low);
                remainingFeed -= low;
                AddStep(route, new BatchAction(Operation.FeedMash, amount: low));
                int ticks = 0;
                while (ChemistryOperations.Amount(route.batch, recipe.Mush) > 0 && ticks < 900)
                {
                    if (ChemistryOperations.Total(buffer) > 0) ChemistryOperations.ReactionTick(buffer);
                    ChemistryOperations.SimulateHeatedSecond(route.batch, inputs.heaterUpgrades, temperature);
                    if (++ticks % 8 == 0) yield return 0;
                }
                // Native reactions also consume material waiting in the buffer
                // (for example, residual acid reacting with fresh protein).
                // The original feed volume is only a withdrawal ceiling: never
                // request volume that no longer exists, or drain reserved stock.
                remainingFeed = Math.Min(remainingFeed, ChemistryOperations.Total(buffer));
                if (ChemistryOperations.Amount(route.batch, recipe.Mush) > 0) { Report("extraction_failed", 25); yield break; }
                if (ticks > 0)
                {
                    route.plan.extractionPortions++;
                    AddStep(route, new BatchAction(Operation.Extract, amount: ticks, targetTemperature: temperature) { precursorId = recipe.Mush });
                }
                yield return 0;
            }
            if (remainingFeed > 0) { Report("diag_portion_limit", 25); yield break; }
            result.Add(route);
        }
    }
}
