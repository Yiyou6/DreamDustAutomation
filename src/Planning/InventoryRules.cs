using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class InventoryRules
    {
        internal static bool Product(GameItem item) => item != null &&
            (item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG) || ChemicalRecipe.Ids.Contains(item.identifier));
        internal static bool Liquid(GameItem item) => item != null && !Product(item) &&
            item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && !item.IsTag("WATER_FILTER");
        internal static bool IntakeLiquid(GameItem item) => Liquid(item) && item.identifier != "water_pitcher";
        internal static bool Solid(GameItem item) => item != null && !Product(item) &&
            !item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && !item.IsTag("WATER_FILTER") &&
            (item.IsTag(ChemicalHelper.BLENDABLE_TAG) || item.IsTag("SOLUBLE_TAG"));
        internal static bool Input(GameItem item) => item != null && !Product(item) &&
            (item.IsTag("WATER_FILTER") || ((Solid(item) || IntakeLiquid(item)) && ChemistryOperations.Total(item) > 0));
    }
}
