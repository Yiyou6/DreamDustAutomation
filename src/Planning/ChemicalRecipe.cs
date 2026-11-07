using System;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class ChemicalRecipe
    {
        internal const string DefaultId = "dream_dust";
        internal static readonly string[] Ids = { "dream_dust", "nightmare_dust", "caffeine_pill", "zyanide_pill",
            "birth_control_pill", "fanta_pill", "pink_alt_injector" };
        internal readonly ChemFinisherRecipe Native;
        internal string Id => Native.identifier;
        internal string Active => Native.activeIngredientId;
        internal int UnitVolume => Native.requiredVolume;
        internal string Name => Native.displayName;
        internal string Mush => Id == "dream_dust" || Id == "pink_alt_injector" ? "dream_cap_mush" :
            Id == "nightmare_dust" ? "nightmare_cap_mush" : Id == "caffeine_pill" ? "coffee_mush" : null;
        private ChemicalRecipe(ChemFinisherRecipe native) { Native = native; }
        internal static ChemicalRecipe Get(string id = null)
        {
            id = string.IsNullOrEmpty(id) ? DefaultId : id;
            if (!Ids.Contains(id)) throw new InvalidOperationException("Unsupported chemical recipe: " + id);
            return new ChemicalRecipe(ChemFinisherRecipe.GetRecipeById(id) ??
                throw new InvalidOperationException("Native chemical recipe not found: " + id));
        }
        internal int FeedstockAmount(GameItem item)
        {
            long amount = ChemistryOperations.Amount(item, Active);
            if (Mush != null) amount += ChemistryOperations.Amount(item, Mush);
            if (Id == "fanta_pill") amount += ChemistryOperations.Amount(item, "methylol");
            if (Id == "pink_alt_injector") amount += ChemistryOperations.Amount(item, "dream_cap_extract");
            return (int)Math.Min(int.MaxValue, amount);
        }

        // Other medicines can carry alcohol/water/reagents, but their bottles
        // are reserved feedstock, not auxiliary solvent for this product.
        internal bool CanUseReagent(GameItem item) => item != null &&
            (FeedstockAmount(item) > 0 || !DirectFormInputs.HasMedicinalParts(item));
    }
}
