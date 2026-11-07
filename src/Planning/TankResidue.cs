using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class ResidueComponent
    {
        internal readonly string Id;
        internal readonly int Volume;
        internal readonly bool Foreign;
        internal ResidueComponent(string id, int volume, bool foreign)
        { Id = id; Volume = volume; Foreign = foreign; }
    }

    internal sealed class TankResidue
    {
        internal readonly ResidueComponent[] Components;
        internal int TotalVolume => Components.Sum(part => part.Volume);
        internal bool HasForeignCompounds => Components.Any(part => part.Foreign);
        private TankResidue(ResidueComponent[] components) { Components = components; }

        private static IEnumerable<string> FeedstockIds(ChemicalRecipe recipe)
        {
            yield return recipe.Active;
            if (recipe.Mush != null) yield return recipe.Mush;
            if (recipe.Id == "pink_alt_injector") yield return "dream_cap_extract";
            if (recipe.Id == "fanta_pill") yield return "methylol";
        }

        internal static TankResidue Read(GameItem tank, string productId)
        {
            return ReadAll(new[] { tank }, productId);
        }

        internal static TankResidue ReadAll(IEnumerable<GameItem> tanks, string productId)
        {
            var vessels = tanks.Where(t => t != null).Distinct().ToArray();
            var allowed = new HashSet<string>(FeedstockIds(ChemicalRecipe.Get(productId)));
            var medicinal = new HashSet<string>(ChemicalRecipe.Ids.SelectMany(id => FeedstockIds(ChemicalRecipe.Get(id))));
            var components = Liquid.Liquids.Select(liquid => new ResidueComponent(liquid.identifier,
                vessels.Sum(t => ChemistryOperations.Amount(t, liquid.identifier)), medicinal.Contains(liquid.identifier) && !allowed.Contains(liquid.identifier)))
                .Where(part => part.Volume > 0).OrderByDescending(part => part.Foreign)
                .ThenByDescending(part => part.Volume).ThenBy(part => part.Id, StringComparer.Ordinal).ToArray();
            return new TankResidue(components);
        }
    }

    // Consent is valid for one unchanged preview and one physical tank only.
    // It is deliberately not persisted across loading, cancellation or recalculation.
    internal sealed class ResidueStartConfirmation
    {
        private string fingerprint;
        private GameItem tank;
        internal bool Pending => fingerprint != null;
        internal void Request(string current, GameItem container) { fingerprint = current; tank = container; }
        internal void Clear() { fingerprint = null; tank = null; }
        internal bool Consume(string current, GameItem container)
        {
            bool valid = Pending && fingerprint == current && ReferenceEquals(tank, container);
            Clear();
            return valid;
        }
    }
}
