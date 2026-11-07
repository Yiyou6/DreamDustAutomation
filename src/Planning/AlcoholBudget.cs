using System;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class AlcoholBudget
    {
        internal static readonly int[] Percentages = { 100, 75, 50, 25, 10, 1 };
        internal long Full, Available, WaterAvailable, ForeignAlcohol;
        internal bool HasFeedstock, Supported, Coffee;
        internal string NoExtractionMessage => !Supported ? "alcohol_not_applicable" :
            HasFeedstock ? "alcohol_pre_extracted" : "alcohol_missing_feed";
        internal bool Applicable => Full > 0;
        internal long Needed(int percent) => (Full * percent + 99) / 100;
        internal bool Allows(int percent) => !Applicable || Available >= Needed(percent);
        // Full-load coffee keeps the existing native water-extraction alternative.
        // Reduced-alcohol options explicitly request solvent reuse, not water.
        internal bool AllowsPlanning(int percent) => Allows(percent) ||
            (Coffee && percent == 100 && WaterAvailable > 0);
        internal bool WaterFallback(int percent) => !Allows(percent) && AllowsPlanning(percent);
        internal static bool Supports(ChemicalRecipe recipe) => recipe.Id == "dream_dust" ||
            recipe.Id == "nightmare_dust" || recipe.Id == "pink_alt_injector" || recipe.Id == "caffeine_pill";
        internal static int Percent(int value) => Percentages.Contains(value) ? value : 100;
        // Coffee solvent extraction processes round(15% of mash) each native
        // tick. Mushrooms instead compare alcohol with all remaining mash.
        internal static long FullDose(ChemicalRecipe recipe, long mush) => mush <= 0 ? 0 :
            recipe.Id == "caffeine_pill" ? checked(5L * Math.Max(1L, (long)Math.Round(mush * .15))) : checked(5L * mush);
        internal static int Temperature(ChemicalRecipe recipe) => recipe.Id == "caffeine_pill" ? 65 : 45;

        // Pure alcohol, not the total size of reagent bottles. The controls refer
        // to all relevant feed currently loaded; the planner can still leave
        // unsuitable ingredients unused. No combinatorial search on UI refresh.
        internal static AlcoholBudget Read(BatchInputs inputs)
        {
            var result = new AlcoholBudget();
            var recipe = ChemicalRecipe.Get(inputs.productId);
            result.Coffee = recipe.Id == "caffeine_pill";
            result.Supported = Supports(recipe);
            if (!result.Supported) return result;
            long mush = 0;
            foreach (var item in inputs.raw)
            {
                if (!recipe.CanUseReagent(item)) continue;
                result.HasFeedstock |= recipe.FeedstockAmount(item) > 0;
                double retained = item.IsTag(ChemicalHelper.BLENDABLE_TAG) ?
                    1 - Math.Max(0, Math.Min(100, inputs.blenderLoss)) / 100.0 : 1;
                mush += (long)Math.Round(ChemistryOperations.Amount(item, recipe.Mush) * retained, MidpointRounding.AwayFromZero);
                result.Available += (long)Math.Round(ChemistryOperations.Amount(item, "alcohol") * retained, MidpointRounding.AwayFromZero);
                result.WaterAvailable += (long)Math.Round(ChemistryOperations.Amount(item, "water") * retained, MidpointRounding.AwayFromZero);
            }
            foreach (var item in inputs.supplies.Concat(new[] { inputs.chamber, inputs.mashBuffer }).Where(x => x != null).Distinct())
            {
                if (item != inputs.chamber && item != inputs.mashBuffer && !recipe.CanUseReagent(item))
                {
                    result.ForeignAlcohol += ChemistryOperations.Amount(item, "alcohol");
                    continue;
                }
                if (inputs.feedPrepared && item != inputs.chamber)
                {
                    // Only the metered reactor contents need extraction; unused
                    // stock and reserved mash are not part of this batch.
                    if (item != inputs.mashBuffer)
                    {
                        result.Available += ChemistryOperations.Amount(item, "alcohol");
                        result.WaterAvailable += ChemistryOperations.Amount(item, "water");
                    }
                    continue;
                }
                result.HasFeedstock |= recipe.FeedstockAmount(item) > 0;
                mush += ChemistryOperations.Amount(item, recipe.Mush);
                result.Available += ChemistryOperations.Amount(item, "alcohol");
                result.WaterAvailable += ChemistryOperations.Amount(item, "water");
            }
            result.Full = FullDose(recipe, mush);
            return result;
        }
    }
}
