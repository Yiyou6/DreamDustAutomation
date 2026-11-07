using System;

namespace DreamDustAutomation.Planning
{
    internal static class ReactionProgress
    {
        internal static bool IsHeated(Operation op) => op == Operation.Extract || op == Operation.Synthesize || op == Operation.BoilDry;
        internal static bool IsTimed(Operation op) => IsHeated(op) || op == Operation.React;
        // A saved duration is an estimate. Cooling and overnight dispatch can
        // change the remaining work; only the physical endpoint ends heating.
        internal static int RequiredSeconds(RunningBatch batch, GameItem tank, GameItem buffer)
        {
            var action = batch.plan.actions[batch.step];
            if (!IsHeated(action.operation)) return Math.Max(0, action.amount - batch.elapsed);
            var reactor = new SimulationItem(tank);
            var reserve = buffer != null && !batch.plan.separateDirect ? new SimulationItem(buffer) : null;
            int seconds = action.operation == Operation.BoilDry ?
                (batch.plan.directForm ? HeatedDrying.HeatUntilDry(reactor, batch.heaterUpgrades, reserve) :
                    NormalDrying.HeatUntilDry(reactor, batch.heaterUpgrades, action.targetTemperature, reserve)) :
                BatchPlanner.HeatUntil(reactor, batch.heaterUpgrades, action, ChemicalRecipe.Get(batch.plan.productId), reserve);
            if (seconds < 0) throw new MachineFault(action.operation == Operation.BoilDry ?
                (batch.plan.directForm ? "direct_boil_failed" : "normal_heat_recheck") : "runtime_reaction_endpoint");
            return seconds;
        }

        internal static void FinishOrContinue(RunningBatch batch, GameItem tank, GameItem buffer)
        {
            int remaining = RequiredSeconds(batch, tank, buffer);
            if (remaining == 0) { batch.step++; batch.elapsed = 0; return; }
            // Keep each persisted segment within its normal duration bound.
            batch.plan.actions[batch.step].amount = remaining;
            batch.plan.reactionSeconds = checked(batch.plan.reactionSeconds + remaining);
            batch.elapsed = 0;
        }
        // Read the same persisted step/elapsed counters that drive production.
        // Non-reaction actions use amount for volume/count, never for seconds.
        internal static int RemainingSeconds(BatchPlan plan, int step, int elapsed)
        {
            if (plan?.actions == null) return 0;
            long remaining = 0;
            for (int i = Math.Max(0, step); i < plan.actions.Length; i++)
            {
                var action = plan.actions[i];
                if (!IsTimed(action.operation)) continue;
                remaining += Math.Max(0L, (long)action.amount - (i == step ? Math.Max(0, elapsed) : 0));
            }
            return (int)Math.Min(int.MaxValue, remaining);
        }
    }
}
