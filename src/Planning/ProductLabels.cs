using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class ProductLabels
    {
        internal static readonly int[] Percentages = { 10, 25, 50, 75, 100 };
        internal const string LegacyTag = "DDA_OUTPUT_LABEL";
        private const string GradePrefix = "DDA_LABEL_GRADE_", PercentPrefix = "DDA_LABEL_PERCENT_";

        // Index zero restores each item's own native, actual quality condition.
        internal static ItemCondition[] Choices() => new[] {
            ItemConditionList.CreateLabPure(), ItemConditionList.CreatePharmaGrade(), ItemConditionList.CreateNearPharma(),
            ItemConditionList.CreateStreetStandard(), ItemConditionList.CreateCutProduct(),
            ItemConditionList.CreateJunkGrade(), ItemConditionList.CreateStarchGrade() };

        internal static bool Apply(GameItem item, int selection)
        {
            if (selection < 0 || selection > 7) throw new ArgumentOutOfRangeException(nameof(selection));
            if (!CanLabel(item)) return false;
            var feature = ChemicalProductHelper.GetChemicalFeature(item);
            // Same state change as the native labeler. Never regrade or alter
            // composition, realCondition, numerical purity or other features.
            feature.fakeCondition = selection == 0 ? feature.realCondition : Choices()[selection - 1];
            feature.SetExposed(false);
            return true;
        }

        internal static bool CanLabel(GameItem item) => item != null && item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG) &&
            ChemicalProductHelper.GetChemicalFeature(item)?.realCondition != null;

        internal static bool ApplyPlanned(ProductLabelGroup group, GameItem item, ICollection<GameItem> currentOutput) =>
            group.Rule.Grade >= 0 && group.Selected.Contains(item) && currentOutput.Contains(item) &&
            item.identifier == group.ProductId && Apply(item, group.Rule.Grade);

        internal static Dictionary<string, ProductLabelRule> LoadRules(GameItem machine)
        {
            int legacy = machine.GetTagReadonly(LegacyTag).GetInt();
            if (legacy < 0 || legacy > 7) legacy = 0;
            var result = new Dictionary<string, ProductLabelRule>();
            foreach (string id in ChemicalRecipe.Ids)
            {
                int grade = machine.IsTag(GradePrefix + id) ? machine.GetTagReadonly(GradePrefix + id).GetInt() : legacy;
                int percent = machine.GetTagReadonly(PercentPrefix + id).GetInt();
                result[id] = new ProductLabelRule(grade >= -1 && grade <= 7 ? grade : 0,
                    Percentages.Contains(percent) ? percent : 100);
            }
            return result;
        }

        internal static void SaveRule(GameItem machine, string productId, ProductLabelRule rule)
        {
            if (!ChemicalRecipe.Ids.Contains(productId)) throw new ArgumentOutOfRangeException(nameof(productId));
            machine.ModifyTag(GradePrefix + productId, tag => tag.Enable().SetInt(rule.Grade));
            machine.ModifyTag(PercentPrefix + productId, tag => tag.Enable().SetInt(rule.Percent));
        }

        // Caller supplies visible inventory order. Take a fixed fraction of ALL
        // current products in each category, never of only the still-unlabeled
        // products, so repeated clicks do not progressively relabel the remainder.
        internal static ProductLabelGroup[] Plan(IEnumerable<GameItem> orderedOutput,
            IDictionary<string, ProductLabelRule> rules, string onlyProduct = null)
        {
            if (onlyProduct != null && !ChemicalRecipe.Ids.Contains(onlyProduct))
                throw new ArgumentOutOfRangeException(nameof(onlyProduct));
            var stock = orderedOutput.Where(CanLabel).Distinct().ToArray();
            return ChemicalRecipe.Ids.Where(id => onlyProduct == null || id == onlyProduct).Select(id =>
            {
                var rule = rules.TryGetValue(id, out var saved) ? saved : new ProductLabelRule(0, 100);
                var category = stock.Where(item => item.identifier == id).ToArray();
                int take = rule.Grade < 0 ? 0 : (int)((long)category.Length * rule.Percent / 100);
                return new ProductLabelGroup(id, rule, category.Length, category.Take(take).ToArray());
            }).ToArray();
        }
    }

    internal sealed class ProductLabelRule
    {
        // -1 leaves this category untouched; 0 uses each item's own real grade.
        internal int Grade { get; }
        internal int Percent { get; }
        internal ProductLabelRule(int grade, int percent)
        {
            if (grade < -1 || grade > 7) throw new ArgumentOutOfRangeException(nameof(grade));
            if (!ProductLabels.Percentages.Contains(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
            Grade = grade; Percent = percent;
        }
    }

    internal sealed class ProductLabelGroup
    {
        internal string ProductId { get; }
        internal ProductLabelRule Rule { get; }
        internal int Total { get; }
        internal GameItem[] Selected { get; }
        internal ProductLabelGroup(string productId, ProductLabelRule rule, int total, GameItem[] selected)
        { ProductId = productId; Rule = rule; Total = total; Selected = selected; }
    }
}
