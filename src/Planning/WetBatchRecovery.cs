using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    internal static class WetBatchRecovery
    {
        // Repair the 0.3.61 save at the failed staging boundary. Resume drying
        // only; do not replay any feeds, filters or already staged products.
        internal static bool TryResume(RunningBatch batch, GameItem tank, GameItem buffer)
        {
            if (batch == null || !batch.failed || batch.failureKey != "runtime_still_wet" ||
                tank == null || !WaterHelper.IsWet(tank) || batch.step <= 0 || batch.step >= batch.plan.actions.Length ||
                batch.plan.actions[batch.step].operation != Operation.Form ||
                (batch.useFinisherTank && batch.prepared >= batch.plan.FormTarget(batch.step))) return false;
            int previous = batch.step - 1;
            while (previous >= 0 && batch.plan.actions[previous].operation == Operation.Filter) previous--;
            if (previous < 0 || batch.plan.actions[previous].operation != Operation.BoilDry) return false;
            var retry = batch.plan.actions[previous].Copy();
            var probePlan = batch.plan.Copy(); probePlan.actions = new[] { retry };
            var probe = new RunningBatch { plan = probePlan, heaterUpgrades = batch.heaterUpgrades };
            try { retry.amount = ReactionProgress.RequiredSeconds(probe, tank, buffer); }
            catch (MachineFault) { return false; }
            var actions = new List<BatchAction>(batch.plan.actions); actions.Insert(batch.step, retry);
            batch.plan.actions = actions.ToArray();
            batch.plan.reactionSeconds = checked(batch.plan.reactionSeconds + retry.amount);
            batch.elapsed = 0; batch.failed = false; batch.failureKey = null;
            return true;
        }
    }
}
