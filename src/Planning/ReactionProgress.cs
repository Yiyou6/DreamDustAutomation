using System;

namespace DreamDustAutomation.Planning
{
    internal static class ReactionProgress
    {
        internal static bool IsHeated(Operation op) => op == Operation.Extract || op == Operation.Synthesize || op == Operation.BoilDry;
        internal static bool IsTimed(Operation op) => IsHeated(op) || op == Operation.React;
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
