using System;

namespace DreamDustAutomation.Planning
{
    internal static class NightReactions
    {
        internal static bool CanAdvance(RunningBatch batch) => batch != null && !batch.failed &&
            batch.step < batch.plan.actions.Length && ReactionProgress.IsTimed(batch.plan.actions[batch.step].operation);

        // Replay all remaining seconds; update live vessels once per step.
        internal static void CompleteStep(RunningBatch batch, GameItem tank, GameItem buffer)
        {
            if (!CanAdvance(batch)) return;
            var action = batch.plan.actions[batch.step];
            int seconds = ReactionProgress.RequiredSeconds(batch, tank, buffer);
            if (seconds > 900) throw new MachineFault("runtime_reaction_duration");
            var reactor = new SimulationItem(tank);
            var reserve = buffer != null && !batch.plan.separateDirect ? new SimulationItem(buffer) : null;
            for (int second = 0; second < seconds; second++)
            {
                if (reserve != null) ChemistryOperations.ReactionTick(reserve);
                if (ReactionProgress.IsHeated(action.operation))
                    ChemistryOperations.ApplyBatchHeating(reactor, batch.heaterUpgrades, action.targetTemperature > 0 ? action.targetTemperature : 45);
                if (!batch.plan.separateDirect || ReactionProgress.IsHeated(action.operation)) ChemistryOperations.ReactionTick(reactor);
            }
            Commit(tank, reactor);
            if (reserve != null) Commit(buffer, reserve);
            batch.elapsed = action.amount;
            ReactionProgress.FinishOrContinue(batch, tank, buffer);
        }
        private static void Commit(GameItem vessel, SimulationItem result)
        {
            BottlingOperations.WriteParts(vessel, ChemistryOperations.Parts(result));
            vessel.ModifyTag(ChemicalHelper.CONTAINER_TEMP_TAG, tag => tag.Enable().SetDouble(ChemistryOperations.Temperature(result)), skipValidate: true);
            if (result.IsTag("HEATING_TAG")) vessel.EnableTag("HEATING_TAG", skipValidate: true);
            else vessel.DisableTag("HEATING_TAG", skipValidate: true);
            if (!(vessel is SimulationItem))
                WaterFeatureHelper.OnLiquidChanged(vessel, WaterFeatureHelper.GetWaterFeature(vessel).IsExposed());
        }
    }
}
