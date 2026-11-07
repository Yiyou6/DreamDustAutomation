using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Read-only explanation of intake rules. No search, simulated processing,
    // inventory movement or assumptions that an item name guarantees chemistry.
    internal sealed class InputGuidance
    {
        internal readonly List<string> Hints = new List<string>();
        internal string[] OtherProducts = new string[0];
        internal bool HasFeedstock, HasDirectMaterial;
        internal string MissingReason;
        internal string Summary => Hints.Count == 0 ? "input_recognized" : Hints[0];

        private void Add(string key) { if (!Hints.Contains(key)) Hints.Add(key); }
        private static bool Solid(GameItem item) => InventoryRules.Solid(item);
        private static bool Bottle(GameItem item) => InventoryRules.IntakeLiquid(item);
        private static bool MealCap(GameItem item) => item.identifier == "meal_cap" ||
            item.spritePath == "meal_cap" || (item.spritePath ?? "").StartsWith("meal_cap_", StringComparison.Ordinal);

        internal static InputGuidance Read(string productId, bool direct, IEnumerable<GameItem> inputs,
            GameItem tank, GameItem buffer, GameItem collector = null, bool hasOutput = false, bool hasWaste = false, bool useSolvents = false,
            GameItem finisher = null)
        {
            var result = new InputGuidance();
            var recipe = ChemicalRecipe.Get(productId);
            var recipes = ChemicalRecipe.Ids.Select(ChemicalRecipe.Get).ToArray();
            var bins = inputs.Where(item => item != null).Distinct().ToArray();
            var usable = bins.Where(item => Solid(item) || Bottle(item)).ToList();
            bool recovered = collector != null && collector != tank && InventoryRules.Liquid(collector) &&
                ChemistryOperations.Amount(collector, "alcohol") > 0;
            if (recovered && !usable.Contains(collector)) usable.Add(collector);
            foreach (var item in new[] { tank, buffer, finisher }.Where(item => item != null))
                if (!usable.Contains(item)) usable.Add(item);
            result.HasFeedstock = usable.Any(item => recipe.FeedstockAmount(item) > 0);
            result.OtherProducts = recipes.Where(other => other.Id != recipe.Id &&
                usable.Any(item => other.FeedstockAmount(item) > 0 && recipe.FeedstockAmount(item) == 0))
                .Select(other => other.Id).ToArray();

            var nonmedicinal = bins.Where(DirectFormInputs.CanUse).ToArray();
            var foods = nonmedicinal.Where(item => ChemistryOperations.Total(item) > 0).ToArray();
            var containerFeed = bins.Where(item => DirectFormInputs.CanUseLiquid(item, useSolvents)).ToArray();
            bool keptSolvents = !useSolvents && bins.Any(item => DirectFormInputs.CanUseLiquid(item) && DirectFormInputs.HasReagents(item));
            var impureWater = containerFeed.Where(item => !DirectFormInputs.HasReagents(item) && ChemistryOperations.VolatileVolume(item) > 0).ToArray();
            var dryContents = containerFeed.Where(item => !DirectFormInputs.HasReagents(item) && ChemistryOperations.VolatileVolume(item) == 0).ToArray();
            bool solvents = containerFeed.Any(DirectFormInputs.HasReagents) ||
                new[] { tank, buffer, finisher }.Any(DirectFormInputs.HasReagents);
            bool volatileSolvents = containerFeed.Any(item => DirectFormInputs.HasReagents(item) &&
                ChemistryOperations.Total(item) == ChemistryOperations.VolatileVolume(item));
            bool excludedAlcohol = bins.Any(item => Bottle(item) && !DirectFormInputs.HasMedicinalParts(item) &&
                ChemistryOperations.Amount(item, "alcohol") > 0);
            bool excludedLipobond = bins.Any(item => (Bottle(item) || Solid(item)) && DirectFormInputs.HasLipobond(item));
            bool meals = nonmedicinal.Any(MealCap);
            bool emptyCaps = nonmedicinal.Any(item => item.identifier == "dream_cap" && !MealCap(item));
            bool leftovers = new[] { tank, buffer, finisher }.Any(item => item != null && ChemistryOperations.Total(item) > 0);
            bool liquids = bins.Any(item => item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && ChemistryOperations.Total(item) > 0);
            bool pitcher = bins.Any(item => item.identifier == "water_pitcher");
            bool protectedSolids = bins.Any(item => Solid(item) && DirectFormInputs.HasMedicinalParts(item));
            bool protectedLiquids = bins.Any(item => Bottle(item) && DirectFormInputs.HasMedicinalParts(item));
            bool pureWater = bins.Any(item => Bottle(item) && !DirectFormInputs.HasMedicinalParts(item) && !DirectFormInputs.HasReagents(item) &&
                ChemistryOperations.Amount(item, "water") > 0 && ChemistryOperations.Total(item) == ChemistryOperations.VolatileVolume(item));
            bool products = bins.Any(InventoryRules.Product);
            bool bottledFeed = bins.Any(item => Bottle(item) && recipe.FeedstockAmount(item) > 0);
            result.HasDirectMaterial = foods.Length > 0 || containerFeed.Length > 0 || leftovers;

            if (direct)
            {
                if (solvents) result.Add("input_direct_solvents");
                if (keptSolvents) result.Add("input_direct_solvents_kept");
                if (excludedAlcohol) result.Add("input_direct_alcohol_kept");
                if (excludedLipobond) result.Add("input_direct_lipobond_kept");
                if (volatileSolvents) result.Add("input_direct_volatile_solvents");
                if (foods.Length == 0 && containerFeed.Length == 0 && leftovers) result.Add("input_direct_residue_only");
                if (protectedSolids || protectedLiquids) result.Add("input_direct_protected");
                if (dryContents.Length > 0) result.Add("input_direct_dry");
                if (impureWater.Length > 0) result.Add("input_direct_water");
                if (pureWater) result.Add("input_pure_water");
                if (liquids && bins.Any(item => Bottle(item) && ChemistryOperations.Total(item) > 0 &&
                    !DirectFormInputs.CanUseLiquid(item))) result.Add("input_direct_bottles");
                if (!result.HasDirectMaterial)
                {
                    result.MissingReason = products ? "input_finished_product" : pitcher ? "input_pitcher" :
                        protectedSolids || protectedLiquids ? "input_direct_protected" :
                        keptSolvents ? "input_direct_solvents_kept" : excludedAlcohol ? "input_direct_alcohol_kept" :
                        excludedLipobond ? "input_direct_lipobond_kept" :
                        pureWater ? "input_pure_water" : liquids ? "input_direct_bottles" : "input_direct_empty";
                    result.Add(result.MissingReason);
                }
                if (meals) result.Add("input_meal_direct");
                if (foods.Length > 0) result.Add("input_direct_food");
                if (leftovers && (foods.Length > 0 || containerFeed.Length > 0)) result.Add("input_direct_residue");
                if (bins.Any(item => item.IsTag("WATER_FILTER"))) result.Add("input_direct_filters");
            }
            else
            {
                if (!result.HasFeedstock)
                {
                    result.MissingReason = result.OtherProducts.Length > 0 ? "input_other_product" :
                        meals ? "input_meal_not_drug" : emptyCaps ? "input_cap_no_compound" :
                        foods.Length > 0 ? "input_food_not_drug" : products ? "input_finished_product" :
                        dryContents.Length > 0 ? "input_dry_direct_only" : impureWater.Length > 0 ? "input_water_direct_only" : pitcher ? "input_pitcher" :
                        bins.Length > 0 ? (bins.All(item => item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) || item.IsTag("WATER_FILTER")) ?
                            "input_aux_only" : "input_unrecognized") : leftovers ? "input_residue_no_feed" : "input_empty";
                    result.Add(result.MissingReason);
                }
                if (meals) result.Add("input_meal_not_drug");
                if (dryContents.Length > 0) result.Add("input_dry_direct_only");
                if (impureWater.Length > 0) result.Add("input_water_direct_only");
                if (emptyCaps) result.Add("input_cap_no_compound");
                if (foods.Any(item => !MealCap(item) && item.identifier != "dream_cap")) result.Add("input_food_not_drug");
                if (result.HasFeedstock && result.OtherProducts.Length > 0) result.Add("input_other_kept");
                if (bottledFeed) result.Add("input_bottled_feed");
                if (result.HasFeedstock)
                {
                    bool mush = recipe.Mush != null && usable.Any(item => ChemistryOperations.Amount(item, recipe.Mush) > 0);
                    if (AlcoholBudget.Supports(recipe) && !mush) result.Add("input_already_extracted");
                    result.Add("input_recognized");
                }
                if (recovered) result.Add("input_recovered");
                var filters = bins.Where(item => item.IsTag("WATER_FILTER") && item.identifier != "uv_filter").ToArray();
                if (filters.Length > 0 && filters.All(item => DurabilityHelper.GetRealDurability(item) <= 0)) result.Add("input_filters_empty");
                if (filters.Length == 0 && bins.Any(item => item.identifier == "uv_filter")) result.Add("input_uv_only");
                if (!result.HasFeedstock && hasOutput) result.Add("input_output_not_feed");
                if (!result.HasFeedstock && hasWaste) result.Add("input_waste_not_feed");
            }
            if (products) result.Add("input_finished_product");
            if (pitcher) result.Add("input_pitcher");
            return result;
        }
    }
}
