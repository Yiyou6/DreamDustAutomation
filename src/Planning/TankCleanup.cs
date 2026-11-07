using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Only the explicitly reviewed tank contents can be discarded. Opening the
    // prompt and cancelling never mutate materials; confirmation is single-use.
    internal sealed class TankCleanup
    {
        private GameItem reviewedTank;
        private int[] reviewedParts;
        private GameItem[] reviewedTanks;
        private int[][] reviewedContents;
        internal bool Pending => reviewedTank != null || reviewedTanks != null;

        internal bool Holds(GameItem tank) => tank != null &&
            (ReferenceEquals(tank, reviewedTank) || (reviewedTanks != null && reviewedTanks.Contains(tank)));

        internal void TickIdle(GameItem tank)
        {
            // Independent fake feeds can contain acid/base after they are pooled
            // for bottling. Once output stops, idle chemistry resumes. Freeze only
            // the reviewed vessels until confirm/cancel, otherwise the machine's
            // own tick invalidates the confirmation before the player clicks it.
            if (tank != null && !Holds(tank)) ChemistryOperations.ReactionTick(tank);
        }

        internal void Cancel() { reviewedTank = null; reviewedParts = null; reviewedTanks = null; reviewedContents = null; }

        internal bool RequestMany(IEnumerable<GameItem> tanks, bool busy)
        {
            Cancel();
            if (busy) return false;
            var all = tanks.Where(t => t != null).Distinct().ToArray();
            if (!all.Any(t => ChemistryOperations.Total(t) > 0)) return false;
            reviewedTanks = all;
            reviewedContents = all.Select(ChemistryOperations.Parts).ToArray();
            return true;
        }

        internal bool ConfirmMany(IEnumerable<GameItem> tanks, bool busy)
        {
            var all = tanks.Where(t => t != null).Distinct().ToArray();
            bool valid = !busy && reviewedTanks != null && all.SequenceEqual(reviewedTanks) &&
                all.Select((t, i) => ChemistryOperations.SameParts(reviewedContents[i], ChemistryOperations.Parts(t))).All(x => x);
            Cancel();
            if (!valid) return false;
            foreach (var tank in all) ChemistryOperations.RemoveAndReturn(tank, ChemistryOperations.Total(tank));
            return true;
        }

        internal bool Request(GameItem tank, bool busy)
        {
            Cancel();
            if (busy || tank == null || ChemistryOperations.Total(tank) == 0) return false;
            reviewedTank = tank;
            reviewedParts = ChemistryOperations.Parts(tank);
            return true;
        }

        internal bool Confirm(GameItem tank, bool busy)
        {
            bool valid = !busy && Pending && ReferenceEquals(tank, reviewedTank) &&
                ChemistryOperations.SameParts(reviewedParts, ChemistryOperations.Parts(tank));
            Cancel();
            if (!valid) return false;
            // Uses vanilla pouring updates for live containers; removes both
            // liquid and dry residue, leaving the container itself intact.
            ChemistryOperations.RemoveAndReturn(tank, ChemistryOperations.Total(tank));
            return true;
        }
    }
}
