using System;

namespace DreamDustAutomation.Planning
{
    // Output permission is independent of the chemistry plan: selecting bottles
    // from finished stock must never rerun dosing or change the planned grade.
    internal static class DeferredOutput
    {
        internal const string PreferenceTag = "DDA_DEFER_OUTPUT";
        internal static bool AtForm(RunningBatch batch) => batch?.plan?.actions != null &&
            (batch.step < batch.plan.actions.Length ? batch.plan.actions[batch.step].operation == Operation.Form :
                batch.deferOutput && batch.prepared == batch.plan.expectedBottles);
        internal static bool Ready(RunningBatch batch) => AtForm(batch) &&
            (!batch.useFinisherTank || batch.prepared == batch.plan.expectedBottles) &&
            (!batch.plan.separateDirect || batch.step == batch.plan.actions.Length);
        internal static int Remaining(RunningBatch batch) => AtForm(batch) ?
            Math.Max(0, batch.plan.FormTarget(batch.step) - batch.produced) : 0;
        internal static bool Waiting(RunningBatch batch) => batch?.deferOutput == true &&
            Ready(batch) && Remaining(batch) > 0 && batch.releaseTarget <= batch.produced;
        internal static bool Protected(RunningBatch batch) => batch?.deferOutput == true && batch.outputCommitted;
        // Ending a batch is always allowed, including between deferred fake lots
        // or during an output-space wait. Drop the record, preserve every vessel.
        internal static bool CanFinish(RunningBatch batch) => batch != null;
        internal static int Target(RunningBatch batch) => !AtForm(batch) ? batch?.produced ?? 0 :
            batch.deferOutput ? Math.Min(batch.plan.FormTarget(batch.step), batch.releaseTarget) : batch.plan.FormTarget(batch.step);
        internal static bool Release(RunningBatch batch, int count)
        {
            if (!Waiting(batch) || count <= 0 || count > Remaining(batch)) return false;
            batch.outputCommitted = true;
            batch.releaseTarget = batch.produced + count;
            return true;
        }
        internal static void Pause(RunningBatch batch)
        {
            if (Protected(batch)) batch.releaseTarget = batch.produced;
        }
    }
}
