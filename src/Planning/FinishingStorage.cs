using System;

namespace DreamDustAutomation.Planning
{
    internal static class FinishingStorage
    {
        internal static bool ReconcileRoundingTail(RunningBatch batch, GameItem reactor)
        {
            // Native synthesis rounds each reaction tick. A different cooling
            // history can leave the final unit a few uL short. Preserve that tail
            // and finish the whole units, rather than locking all output on it.
            if (batch == null || !batch.useFinisherTank || batch.plan.directForm || batch.prepared != 0 ||
                batch.produced != 0 || batch.originalExpectedBottles != 0 || ChemistryOperations.VolatileVolume(reactor) > 0)
                return false;
            var recipe = ChemicalRecipe.Get(batch.plan.productId);
            int total = ChemistryOperations.Total(reactor);
            long shortage = (long)batch.plan.expectedBottles * recipe.UnitVolume - total;
            // Only the integer-rounding boundary: larger losses retain the
            // existing explicit error and require a new plan.
            if (shortage <= 0 || shortage > 100 || batch.plan.expectedBottles <= 1) return false;
            var outcome = ChemistryOperations.Measure(reactor, batch.finisherLoss, 0, 0, null, recipe);
            if (!GradePolicy.Meets(outcome, batch.plan.actualGrade) || outcome.Bottles != batch.plan.expectedBottles - 1) return false;
            batch.originalExpectedBottles = batch.plan.expectedBottles;
            batch.plan.expectedBottles = outcome.Bottles;
            if (batch.plan.requestedBottles > 0) batch.plan.requestedBottles = outcome.Bottles;
            batch.plan.minimumActive = outcome.MinimumBottleActiveVolume;
            foreach (var action in batch.plan.actions)
                if (action.operation == Operation.Form) action.amount = outcome.Bottles;
            return true;
        }

        internal static GameItem PreviewStart(GameItem reactor, GameItem finisher)
        {
            if (reactor == null || finisher == null || ChemistryOperations.Total(finisher) == 0) return reactor;
            var combined = new SimulationItem(reactor);
            // Opening/refreshing the UI also reads this snapshot. Preserve all
            // residue even when it cannot physically fit; planning/start checks
            // report that shortage without breaking the window or cleanup.
            var parts = ChemistryOperations.Parts(combined);
            var residue = ChemistryOperations.Parts(finisher);
            ChemicalHelper.HandleReceiveLiquidTemp(combined, ChemistryOperations.Total(finisher), finisher);
            for (int i = 0; i < parts.Length; i++) parts[i] = checked(parts[i] + residue[i]);
            BottlingOperations.WriteParts(combined, parts);
            return combined;
        }

        internal static void MoveRemainder(GameItem finisher, GameItem reactor)
        {
            if (finisher == null) return;
            int amount = ChemistryOperations.Total(finisher);
            if (amount > WaterHelper.GetFreeCapacity(reactor)) throw new MachineFault("runtime_residue_capacity");
            ChemistryOperations.Pour(finisher, reactor, amount);
        }

        internal static void Stage(GameItem reactor, GameItem finisher, int count, int unitVolume, bool separate)
        {
            if (finisher == null) throw new MachineFault("missing_finisher_tank");
            if (WaterHelper.IsWet(reactor)) throw new MachineFault("runtime_still_wet");
            if ((long)count * unitVolume > ChemistryOperations.Total(reactor))
                throw new MachineFault("runtime_batch_short", "staging " + count + " x " + unitVolume +
                    " uL; available " + ChemistryOperations.Total(reactor) + " uL");
            int volume = separate ? checked(count * unitVolume) : ChemistryOperations.Total(reactor);
            if (volume > WaterHelper.GetFreeCapacity(finisher)) throw new MachineFault("runtime_finisher_capacity");
            if (volume > ChemistryOperations.Total(reactor)) throw new MachineFault("runtime_less_than_one");
            if (!separate) { ChemistryOperations.Pour(reactor, finisher, volume); return; }
            // Keep the existing per-unit native rounding for independent fake
            // feeds. Their sub-unit tails still follow the saved return/buffer
            // actions. All whole-unit material goes into one physical vessel.
            var remaining = ChemistryOperations.Parts(reactor);
            var stored = ChemistryOperations.Parts(finisher);
            for (int n = 0; n < count; n++)
            {
                var taken = WaterHelper.SplitProportionally(remaining, unitVolume);
                for (int i = 0; i < remaining.Length; i++)
                { remaining[i] -= taken[i]; stored[i] = checked(stored[i] + taken[i]); }
            }
            double temperature = ChemistryOperations.Temperature(reactor);
            ChemicalHelper.HandleReceiveLiquidTemp(finisher, volume, temperature);
            BottlingOperations.WriteParts(finisher, stored);
            BottlingOperations.WriteParts(reactor, remaining);
            ChemicalHelper.HandleEmptyContainerTemp(reactor);
            if (!(finisher is SimulationItem)) WaterFeatureHelper.OnLiquidChanged(finisher, true);
            if (!(reactor is SimulationItem)) WaterFeatureHelper.OnLiquidChanged(reactor, false);
        }
    }
}
