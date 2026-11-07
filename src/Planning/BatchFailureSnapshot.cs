using System;
using System.Globalization;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal static class BatchFailureSnapshot
    {
        internal static string Describe(RunningBatch batch, params GameItem[] tanks)
        {
            if (batch?.plan == null) return "No active batch.";
            try
            {
                var p = batch.plan;
                var step = batch.step < p.actions.Length ? p.actions[batch.step] : null;
                return "Batch product=" + p.productId + " grade=" + p.actualGrade + " step=" + batch.step +
                    "/" + p.actions.Length + " operation=" + step?.operation + " elapsed=" + batch.elapsed +
                    " precursor=" + step?.precursorId + " endpoint=" + step?.endAmount +
                    " heater=" + batch.heaterUpgrades + " finisherLoss=" + batch.finisherLoss +
                    " output=" + batch.produced + "/" + p.expectedBottles + "; " +
                    string.Join("; ", tanks.Select((tank, index) => tank == null ? "tank" + index + "=missing" :
                        "tank" + index + " temp=" + ChemistryOperations.Temperature(tank).ToString("F4", CultureInfo.InvariantCulture) +
                        " uL={" + string.Join(",", Liquid.Liquids.Select(l => new { l.identifier, amount = ChemistryOperations.Amount(tank, l.identifier) })
                            .Where(l => l.amount > 0).Select(l => l.identifier + "=" + l.amount)) + "}"));
            }
            catch (Exception error) { return "Batch diagnostic unavailable: " + error.GetType().Name; }
        }
    }
}
