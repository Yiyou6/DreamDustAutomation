using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed class PlanningInputState
    {
        private readonly Dictionary<string, string> fields = new Dictionary<string, string>();
        internal string Combined => string.Join("|", fields.Select(x => x.Key + ":" + x.Value));
        private static string Items(IEnumerable<GameItem> items) => string.Join(";", items.Where(x => x != null).Select(x =>
            x.identifier + ":" + x.unitCount + ":" + string.Join(",", ChemistryOperations.Parts(x)) + ":" + DurabilityHelper.GetRealDurability(x)));
        internal static PlanningInputState Capture(BatchInputs input)
        {
            var result = new PlanningInputState();
            result.fields.Add("changed_settings", input.productId + "/" + input.directForm + "/" + input.separateDirect + "/" + input.directDrying + "/" + input.normalDrying + "/" + input.directSolvents + "/" + input.requestedGrade + "/" + input.alcoholPercent);
            result.fields.Add("changed_modules", input.blenderLoss + "/" + input.heaterUpgrades + "/" + input.finisherLoss);
            result.fields.Add("changed_capacity", WaterHelper.GetTotalCapacity(input.chamber) + "/" + (input.mashBuffer == null ? 0 : WaterHelper.GetTotalCapacity(input.mashBuffer)) + "/" + (input.finisher == null ? 0 : WaterHelper.GetTotalCapacity(input.finisher)));
            result.fields.Add("changed_tank", Items(new[] { input.chamber, input.mashBuffer, input.finisher }));
            result.fields.Add("changed_inputs", Items(input.raw.Concat(input.supplies)) + "/" + input.recoveredSource);
            result.fields.Add("changed_output", input.maxProducts.ToString());
            return result;
        }
        internal PlanningDiagnostics Changes(PlanningInputState next)
        {
            var report = new PlanningDiagnostics();
            foreach (var field in fields)
                if (field.Value != next.fields[field.Key]) report.Add(field.Key);
            return report;
        }
    }
}
