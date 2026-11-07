using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        private double[][] impurityWeights;
        private int[] reagentPolicies;

        private void InitializeSelectionQuality()
        {
            var stock = inputs.supplies.Concat(inputs.raw).Where(recipe.CanUseReagent).Concat(new[] { inputs.chamber }).ToArray();
            bool neutralizer = stock.Any(x => IngredientAmount(x, "acid") != 0);
            bool lipobond = stock.Any(x => ChemistryOperations.Amount(x, "lipobond_agent") > 0);
            var filters = inputs.supplies.Where(x => x.IsTag("WATER_FILTER")).ToArray();
            bool uv = filters.Any(x => x.identifier == "uv_filter");
            int cutoff = filters.Where(x => x.identifier != "uv_filter" &&
                x.GetTagReadonly("FILTER_SIZE_TAG").GetInt() > 5 && DurabilityHelper.GetRealDurability(x) > 0)
                .Select(x => x.GetTagReadonly("FILTER_SIZE_TAG").GetInt()).DefaultIfEmpty(int.MaxValue).Min();
            Func<Liquid, double> retained = liquid => liquid.isEvaporative ? 0 :
                liquid.identifier == "microbe" ? (uv ? 0 : 1) :
                cutoff <= liquid.particleSize ? liquid.filterMinimumPurity : 1;
            double debris = retained(Liquid.GetLiquidFromId("debris"));
            double mineral = retained(Liquid.GetLiquidFromId("mineral"));
            impurityWeights = new[] { new double[Liquid.Liquids.Count], new double[Liquid.Liquids.Count], new double[Liquid.Liquids.Count] };
            for (int i = 0; i < Liquid.Liquids.Count; i++)
            {
                var liquid = Liquid.Liquids[i];
                impurityWeights[0][i] = liquid.isEvaporative ? 0 : 1;
                impurityWeights[1][i] = retained(liquid);
                double treated = retained(liquid);
                if (neutralizer && (liquid.identifier == "protein" || liquid.identifier == "stabilizing_agent")) treated = debris;
                if (neutralizer && liquid.identifier == "organic_waste") treated = Math.Min(treated, mineral);
                if (neutralizer && liquid.identifier == "microbe") treated = Math.Min(treated, debris);
                if (lipobond && liquid.identifier == "fat") treated = Math.Min(treated, debris * 1.125);
                impurityWeights[2][i] = treated;
            }

            // Only branch when a relevant reagent really has a different order.
            // These are bounded source preferences, not reagent-dose candidates.
            var ingredients = new List<string> { "acid", "base", "lipobond_agent" };
            if (recipe.Mush != null) ingredients.Add("alcohol");
            if (recipe.Id == "caffeine_pill") ingredients.Add("water");
            if (recipe.Id == "fanta_pill" || recipe.Id == "pink_alt_injector") ingredients.Add("reducing_agent");
            var policies = new List<int> { 0 };
            for (int policy = 1; policy <= 2; policy++)
                if (!policies.Any(previous => ingredients.All(id =>
                    RankReagents(inputs.supplies, id, policy).SequenceEqual(RankReagents(inputs.supplies, id, previous)))))
                    policies.Add(policy);
            reagentPolicies = policies.ToArray();
        }

        private IEnumerable<int> RankReagents(GameItem[] supplies, string useful, int policy) =>
            Enumerable.Range(0, supplies.Length)
                .Where(i => supplies[i].IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && recipe.CanUseReagent(supplies[i]) && IngredientAmount(supplies[i], useful) > 0)
                .OrderBy(i => ImpurityPerUnit(supplies[i], useful, policy))
                .ThenBy(i => policy == 0 ? 0 : ImpurityPerUnit(supplies[i], useful, 0));

        private double ImpurityPerUnit(GameItem source, string useful, int policy)
        {
            double unwanted = 0;
            for (int i = 0; i < Liquid.Liquids.Count; i++)
                if (Liquid.Liquids[i].identifier != useful)
                    unwanted += ChemistryOperations.Amount(source, Liquid.Liquids[i].identifier) * impurityWeights[policy][i];
            return unwanted / Math.Max(1, IngredientAmount(source, useful));
        }

        private double FeedQuality(Feed feed, int policy)
        {
            double remaining = 0;
            for (int i = 0; i < Liquid.Liquids.Count; i++)
            {
                var liquid = Liquid.Liquids[i];
                // Mash is a large particle too: never rank it as removable dirt.
                bool useful = liquid.identifier == recipe.Active || liquid.identifier == recipe.Mush ||
                    (recipe.Id == "fanta_pill" && liquid.identifier == "methylol") ||
                    (recipe.Id == "pink_alt_injector" && liquid.identifier == "dream_cap_extract");
                remaining += ChemistryOperations.Amount(feed.contents, liquid.identifier) * (useful ? 1 : impurityWeights[policy][i]);
            }
            // Optimistic ranking only. Native reaction/filtration replay still
            // charges all reagents and shared filter wear before accepting a plan.
            return feed.quality * Math.Max(1, DryVolume(feed.contents)) / Math.Max(1, remaining);
        }

        private IEnumerable<int> PrepareAlternatives(Route seed, List<Route> routes)
        {
            foreach (int policy in reagentPolicies)
            {
                var alternative = CloneRoute(seed);
                alternative.reagentPolicy = policy;
                foreach (var ignored in Prepare(alternative, routes)) yield return 0;
            }
        }
    }
}
