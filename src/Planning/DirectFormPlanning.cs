using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        // Separate from purification: non-medicinal solids, impure liquids and residue.
        // No ingredient is promoted into an active compound by this mode.
        private IEnumerable<int> PlanDirectForm()
        {
            Stage = "plan_direct";
            var batch = new SimulationItem(inputs.chamber);
            var steps = new List<BatchAction>();
            int buffered = inputs.mashBuffer == null ? 0 : System.Math.Max(0, ChemistryOperations.Total(inputs.mashBuffer) - inputs.reservedBufferVolume);
            if (buffered > WaterHelper.GetFreeCapacity(batch)) { Capacity("buffer_return_full", batch, buffered, 10); yield break; }
            if (buffered > 0)
            {
                ChemistryOperations.Pour(new SimulationItem(inputs.mashBuffer), batch, buffered);
                steps.Add(new BatchAction(Operation.FeedMash, amount: buffered));
            }
            bool capacityLimited = false;
            for (int i = 0; !inputs.feedPrepared && i < inputs.raw.Length; i++)
            {
                var ingredient = inputs.raw[i];
                bool blend = ingredient.IsTag(ChemicalHelper.BLENDABLE_TAG);
                if (!DirectFormInputs.CanUse(ingredient)) continue;
                int volume = ChemistryOperations.Total(ingredient);
                if (volume <= 0) continue;
                if (volume > WaterHelper.GetFreeCapacity(batch)) { capacityLimited = true; Capacity("diag_solid_capacity", batch, volume, 5); continue; }
                if (blend) ChemistryOperations.Blend(new SimulationItem(ingredient), batch, inputs.blenderLoss);
                else ChemistryOperations.Dissolve(new SimulationItem(ingredient), batch);
                steps.Add(new BatchAction(blend ? Operation.Blend : Operation.Dissolve, i));
                yield return 0;
            }
            int liquidVolume = 0;
            for (int i = 0; !inputs.feedPrepared && i < inputs.supplies.Length; i++)
            {
                if (!DirectFormInputs.CanUseLiquid(inputs.supplies[i], inputs.directSolvents)) continue;
                int amount = System.Math.Min(ChemistryOperations.Total(inputs.supplies[i]), WaterHelper.GetFreeCapacity(batch));
                if (amount <= 0) { capacityLimited = true; Capacity("diag_feed_capacity", batch, ChemistryOperations.Total(inputs.supplies[i]), 5); continue; }
                ChemistryOperations.Pour(new SimulationItem(inputs.supplies[i]), batch, amount);
                steps.Add(new BatchAction(Operation.Pour, i, amount));
                liquidVolume += amount;
                yield return 0;
            }
            if (ChemistryOperations.Total(batch) == 0) { if (!capacityLimited) Report("direct_no_raw"); yield break; }
            // Native ambient reactions still affect existing residue and fresh food.
            // Let them settle with the same real-time ticks used by normal batches.
            int seconds = 0, unchanged = 0;
            while (seconds < 900 && unchanged < 2)
            {
                var before = ChemistryOperations.Parts(batch);
                ChemistryOperations.ReactionTick(batch);
                unchanged = ChemistryOperations.SameParts(before, ChemistryOperations.Parts(batch)) ? unchanged + 1 : 0;
                seconds++;
                if (seconds % 8 == 0) yield return 0;
            }
            if (unchanged < 2) { Report("direct_unstable", 30); yield break; }
            steps.Add(new BatchAction(Operation.React, amount: seconds));
            string dryingHint = null;
            if (HeatedDrying.CanUse(batch, inputs.heaterUpgrades))
            {
                // Boiling is real native heating, not instant deletion of water.
                // Microbes/organic waste may be destroyed or converted, so form
                // and preview from the heated composition, never the cold yield.
                bool containsReagents = DirectFormInputs.HasReagents(batch);
                int heated = 0;
                while (ChemistryOperations.VolatileVolume(batch) > 0 && heated < HeatedDrying.TickLimit)
                {
                    ChemistryOperations.SimulateHeatedSecond(batch, inputs.heaterUpgrades, HeatedDrying.TargetTemperature);
                    if (++heated % 8 == 0) yield return 0;
                }
                if (ChemistryOperations.VolatileVolume(batch) > 0) { Report("direct_boil_failed", 30); yield break; }
                seconds += heated;
                steps.Add(new BatchAction(Operation.BoilDry, amount: heated, targetTemperature: HeatedDrying.TargetTemperature));
                dryingHint = containsReagents ? "direct_boil_solvents" : "direct_boil_hint";
            }
            else if (ChemistryOperations.VolatileVolume(batch) > 0)
            {
                if (HeatedDrying.IsWaterFeed(batch)) dryingHint = "direct_boil_upgrade";
                else if (DirectFormInputs.HasMedicinalParts(batch)) dryingHint = "direct_boil_protected";
                steps.Add(new BatchAction(Operation.Dry) { evaporationVolume = ChemistryOperations.VolatileVolume(batch) });
                ChemistryOperations.SimulateDry(batch);
            }
            var measured = ChemistryOperations.Measure(batch, inputs.finisherLoss, 0, seconds, null, recipe, inputs.maxProducts);
            CandidatesChecked++;
            if (measured.Bottles <= 0)
            {
                int dry = DryVolume(batch);
                Report(dry == 0 ? "direct_no_output" : "diag_less_than_one", 40, "diag_one_unit_detail", dry / 1000.0, recipe.UnitVolume / 1000.0);
                yield break;
            }
            steps.Add(new BatchAction(Operation.Form, amount: measured.Bottles));
            Result = new BatchPlan { schema = 3, directForm = true, directSolvents = inputs.directSolvents, productId = recipe.Id,
                outputCapacity = inputs.maxProducts, outputLimited = OutputCapacity.DryProducts(ChemistryOperations.Parts(batch), recipe) > measured.Bottles,
                requestedGrade = 0, actualGrade = 0, expectedBottles = measured.Bottles,
                minimumActive = measured.MinimumBottleActiveVolume, reactionSeconds = seconds, reagents = liquidVolume,
                concentrationHint = dryingHint,
                alcoholPercent = AlcoholBudget.Percent(inputs.alcoholPercent), actions = steps.ToArray() };
            Error = null;
            Diagnostics.Clear();
        }
    }
}
