using System;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Explain the actual contents and chosen withdrawals. A reagent bottle's
    // name/label is not proof of its contents; this never initiates a new search.
    internal sealed class NeutralizerUsage
    {
        internal long Acid, Base, DrawnAcid, DrawnBase;
        internal static NeutralizerUsage Read(BatchInputs inputs, BatchPlan plan = null)
        {
            var result = new NeutralizerUsage();
            foreach (var item in inputs.supplies)
            {
                result.Acid += ChemistryOperations.Amount(item, "acid");
                result.Base += ChemistryOperations.Amount(item, "base");
            }
            if (plan == null || plan.directForm) return result;
            var stock = inputs.supplies.Select(ChemistryOperations.Parts).ToArray();
            int acid = Liquid.Liquids.FindIndex(x => x.identifier == "acid");
            int alkali = Liquid.Liquids.FindIndex(x => x.identifier == "base");
            foreach (var step in plan.actions.Where(a => a.operation == Operation.Pour))
            {
                if (step.source < 0 || step.source >= stock.Length) continue;
                var parts = stock[step.source];
                var taken = WaterHelper.SplitProportionally(parts, Math.Min(step.amount, parts.Sum()));
                result.DrawnAcid += taken[acid]; result.DrawnBase += taken[alkali];
                for (int i = 0; i < parts.Length; i++) parts[i] -= taken[i];
            }
            return result;
        }
    }
}
