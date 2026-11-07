using System;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class ConcentrationAdvice
    {
        // A next step based on the chosen batch, not a promise of a particular
        // grade. Only the existing preview search decides what is achievable.
        internal static string Get(BatchInputs inputs, BatchPlan plan, int[] parts, int[] cleanerAttempt = null)
        {
            if (plan.actualGrade >= GradePolicy.Grades[0]) return null;
            if (plan.actualGrade >= plan.requestedGrade) return "purity_target";
            int next = GradePolicy.Grades.Last(grade => grade > plan.actualGrade && grade <= plan.requestedGrade);
            if (next > 100 - inputs.finisherLoss) return "purity_finisher";
            if (parts == null || parts.Length != Liquid.Liquids.Count) return null;

            var recipe = ChemicalRecipe.Get(plan.productId);
            long gap = (long)next * recipe.UnitVolume - (long)plan.minimumActive * 100;
            // Native scanners round percentages to two decimals. A displayed
            // 99.00% source can still produce 98.95% units after integer splitting.
            if (gap > 0 && gap * 10 <= recipe.UnitVolume) return "purity_boundary";
            // A lower-grade winner deliberately keeps filler for more units.
            // That filler is not evidence that filters prevented the higher grade.
            // Use the already-tested, strictly purer attempt to identify what
            // remained after cleanup, without a new search or consuming stock.
            if (cleanerAttempt != null && cleanerAttempt.Length == parts.Length) parts = cleanerAttempt;
            int dominant = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                var liquid = Liquid.Liquids[i];
                if (parts[i] <= 0 || liquid.isEvaporative || liquid.identifier == recipe.Active) continue;
                if (dominant < 0 || parts[i] > parts[dominant]) dominant = i;
            }
            if (dominant < 0) return null;
            var impurity = Liquid.Liquids[dominant];
            // Recommend cleaning only when the tank actually supplied most of
            // this remaining impurity. Never discard anything automatically.
            if ((long)ChemistryOperations.Amount(inputs.chamber, impurity.identifier) * 2 >= parts[dominant])
                return "purity_residue";
            if (impurity.identifier == Liquid.LIQUID_PROTEIN) return "purity_protein";
            if (impurity.identifier == Liquid.LIQUID_FAT) return "purity_fat";
            if (impurity.identifier == "stabilizing_agent") return "purity_stabilizer";
            if (impurity.identifier == Liquid.LIQUID_MICROBE) return "purity_microbe";

            // An advanced filter has cutoff 22; smaller particles (including
            // chemical contamination and heavy metals) need cleaner feedstock.
            if (impurity.particleSize < 22) return "purity_cleaner";
            var filters = inputs.supplies.Where(item => item.IsTag("WATER_FILTER") && item.identifier != "uv_filter" &&
                item.GetTagReadonly("FILTER_SIZE_TAG").GetInt() > 5 &&
                item.GetTagReadonly("FILTER_SIZE_TAG").GetInt() <= impurity.particleSize).ToArray();
            if (filters.Length == 0) return "purity_filter_missing";
            if (filters.All(item => DurabilityHelper.GetRealDurability(item) <= 0)) return "purity_filter_spent";
            return "purity_filter_more";
        }
    }
}
