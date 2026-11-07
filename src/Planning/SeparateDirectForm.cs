using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    internal sealed partial class BatchPlanner
    {
        private sealed class DirectLot
        {
            internal SimulationItem batch;
            internal int seconds, bottles, minimumActive, evaporationVolume;
            internal bool boil, dry;
        }
        private DirectLot directLot;

        private IEnumerable<int> EvaluateDirectLot(GameItem feed)
        {
            directLot = null;
            var lot = new DirectLot { batch = new SimulationItem(feed) };
            if (ChemistryOperations.VolatileVolume(lot.batch) > 0)
            {
                string heatProblem = inputs.directDrying == DirectDryingMethod.Heat ? DirectDryingPolicy.HeatProblem(lot.batch, inputs.heaterUpgrades) : null;
                if (heatProblem != null) { Report(heatProblem, 30); yield break; }
                if (DirectDryingPolicy.UseHeat(lot.batch, inputs.heaterUpgrades, inputs.directDrying))
                {
                    lot.boil = true;
                    while (ChemistryOperations.VolatileVolume(lot.batch) > 0 && lot.seconds < HeatedDrying.TickLimit)
                    {
                        ChemistryOperations.SimulateHeatedSecond(lot.batch, inputs.heaterUpgrades, HeatedDrying.TargetTemperature);
                        if (++lot.seconds % 8 == 0) yield return 0;
                    }
                    if (ChemistryOperations.VolatileVolume(lot.batch) > 0) { Report("direct_boil_failed", 30); yield break; }
                }
                else
                {
                    lot.dry = true; lot.evaporationVolume = ChemistryOperations.VolatileVolume(lot.batch);
                    ChemistryOperations.SimulateDry(lot.batch);
                }
            }
            // No separate reaction wait, no combined acid/base treatment. Only
            // the selected portion's native heated reactions affect this lot.
            var outcome = ChemistryOperations.Measure(lot.batch, inputs.finisherLoss, 0, lot.seconds, null, recipe);
            lot.bottles = outcome.Bottles; lot.minimumActive = outcome.MinimumBottleActiveVolume;
            CandidatesChecked++;
            directLot = lot;
        }

        private SimulationItem DirectSample(GameItem feed, int volume, bool liquid)
        {
            if (!liquid)
            {
                var result = new SimulationItem(feed);
                ChemistryOperations.RemoveAndReturn(result, ChemistryOperations.Total(result) - volume);
                return result;
            }
            var empty = new SimulationItem();
            empty.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).SetInt(WaterHelper.GetTotalCapacity(inputs.chamber));
            ChemistryOperations.Pour(new SimulationItem(feed), empty, volume);
            return empty;
        }

        internal static void ConsumeDirectUnits(GameItem batch, int count, int unitVolume)
        {
            var parts = ChemistryOperations.Parts(batch);
            for (int n = 0; n < count; n++)
            {
                var taken = WaterHelper.SplitProportionally(parts, unitVolume);
                for (int i = 0; i < parts.Length; i++) parts[i] -= taken[i];
            }
            for (int i = 0; i < parts.Length; i++) ChemistryOperations.SetAmount(batch, Liquid.Liquids[i].identifier, parts[i]);
            ChemicalHelper.HandleEmptyContainerTemp(batch);
        }

        private IEnumerable<int> PlanSeparateDirect()
        {
            Stage = "plan_direct";
            if (!DirectDryingPolicy.Valid(inputs.directDrying)) { Report("changed_settings"); yield break; }
            var reactor = new SimulationItem(inputs.chamber);
            var buffer = inputs.mashBuffer == null ? null : new SimulationItem(inputs.mashBuffer);
            var stock = inputs.supplies.Select(item => new SimulationItem(item)).ToArray();
            var steps = new List<BatchAction>();
            int bottles = 0, seconds = 0, liquids = 0, minimum = recipe.UnitVolume;
            bool limited = false, any = false;
            // Existing buffer and reactor are also independent lots. A real tank
            // swap lets the installed heater/evaporator process either vessel.
            for (int source = -2; source < inputs.raw.Length + inputs.supplies.Length; source++)
            {
                bool saved = source < 0, swap = source == -2, solid = source >= 0 && source < inputs.raw.Length;
                bool liquid = !saved && !solid;
                int index = liquid ? source - inputs.raw.Length : source;
                GameItem original = saved ? (swap ? buffer : reactor) : solid ? inputs.raw[index] : stock[index];
                if (original == null || ChemistryOperations.Total(original) == 0) continue;
                if (!saved && (solid ? !DirectFormInputs.CanUse(original) : !DirectFormInputs.CanUseLiquid(original, inputs.directSolvents))) continue;
                any = true;
                SimulationItem feed;
                int available;
                if (solid)
                {
                    if (ChemistryOperations.Total(original) > WaterHelper.GetTotalCapacity(reactor))
                    { Capacity("diag_solid_capacity", reactor, ChemistryOperations.Total(original), 5); continue; }
                    feed = new SimulationItem(reactor);
                    ChemistryOperations.RemoveAndReturn(feed, ChemistryOperations.Total(feed));
                    if (ChemistryOperations.Total(reactor) > 0) ChemicalHelper.HandleEmptyContainerTemp(feed);
                    if (original.IsTag(ChemicalHelper.BLENDABLE_TAG)) ChemistryOperations.Blend(new SimulationItem(original), feed, inputs.blenderLoss);
                    else ChemistryOperations.Dissolve(new SimulationItem(original), feed);
                    available = ChemistryOperations.Total(feed);
                }
                else
                {
                    feed = new SimulationItem(original);
                    available = liquid ? Math.Min(ChemistryOperations.Total(feed), WaterHelper.GetTotalCapacity(reactor)) : ChemistryOperations.Total(feed);
                    if (available == 0) { Capacity("diag_feed_capacity", reactor, ChemistryOperations.Total(feed), 5); continue; }
                }
                foreach (var ignored in EvaluateDirectLot(DirectSample(feed, available, liquid))) yield return 0;
                if (directLot == null) yield break;
                var chosen = directLot;
                // A dilute bottle larger than the reactor can require several
                // drying loads before its own retained solids reach one unit.
                bool concentrate = liquid && available < ChemistryOperations.Total(original) &&
                    ChemistryOperations.Total(chosen.batch) > 0 && ChemistryOperations.VolatileVolume(feed) > 0;
                if (chosen.bottles <= 0 && !concentrate) continue;
                if (bottles >= inputs.maxProducts) { limited = true; break; }
                int take = available, goal = Math.Min(chosen.bottles, inputs.maxProducts - bottles);
                if (chosen.bottles > goal)
                {
                    limited = true;
                    int low = 0, high = available;
                    while (high - low > 1)
                    {
                        int middle = low + (high - low) / 2;
                        foreach (var ignored in EvaluateDirectLot(DirectSample(feed, middle, liquid))) yield return 0;
                        if (directLot != null && directLot.bottles >= goal) { high = middle; chosen = directLot; }
                        else low = middle;
                    }
                    take = high;
                    if (chosen.bottles != goal) { Report("direct_quantity_step", 100); yield break; }
                }

                if (swap)
                {
                    steps.Add(new BatchAction(Operation.SwapTanks));
                    var temporary = reactor; reactor = buffer; buffer = temporary;
                }
                if (!saved && ChemistryOperations.Total(reactor) > 0)
                {
                    int residue = ChemistryOperations.Total(reactor);
                    if (buffer == null || residue > WaterHelper.GetFreeCapacity(buffer))
                    { Report(buffer == null ? "missing_buffer" : "direct_separate_buffer_full", 10); yield break; }
                    ChemistryOperations.Pour(reactor, buffer, residue);
                    ChemicalHelper.HandleEmptyContainerTemp(reactor);
                    steps.Add(new BatchAction(Operation.StoreMash, amount: residue) { precursorId = "direct_remainder" });
                }
                if (solid)
                {
                    if (original.IsTag(ChemicalHelper.BLENDABLE_TAG)) ChemistryOperations.Blend(new SimulationItem(original), reactor, inputs.blenderLoss);
                    else ChemistryOperations.Dissolve(new SimulationItem(original), reactor);
                    steps.Add(new BatchAction(original.IsTag(ChemicalHelper.BLENDABLE_TAG) ? Operation.Blend : Operation.Dissolve, index));
                }
                if (liquid)
                {
                    ChemistryOperations.Pour(original, reactor, take);
                    steps.Add(new BatchAction(Operation.Pour, index, take));
                    liquids = checked(liquids + take);
                }
                int excess = ChemistryOperations.Total(reactor) - take;
                if (excess > 0)
                {
                    // A whole solid must really be blended. Keep the unprocessed
                    // fraction in the installed buffer instead of processing it.
                    if (buffer == null || excess > WaterHelper.GetFreeCapacity(buffer))
                    { Report(buffer == null ? "missing_buffer" : "quantity_buffer_full", 10); yield break; }
                    ChemistryOperations.Pour(reactor, buffer, excess);
                    steps.Add(new BatchAction(Operation.StoreMash, amount: excess));
                }
                if (chosen.boil) steps.Add(new BatchAction(Operation.BoilDry, amount: chosen.seconds, targetTemperature: HeatedDrying.TargetTemperature));
                else if (chosen.dry) steps.Add(new BatchAction(Operation.Dry) { evaporationVolume = chosen.evaporationVolume });
                if (chosen.bottles > 0) steps.Add(new BatchAction(Operation.Form, amount: chosen.bottles));
                seconds = checked(seconds + chosen.seconds); bottles += chosen.bottles;
                if (chosen.bottles > 0) minimum = Math.Min(minimum, chosen.minimumActive);
                reactor = new SimulationItem(chosen.batch);
                ConsumeDirectUnits(reactor, chosen.bottles, recipe.UnitVolume);
                if (liquid && ChemistryOperations.Total(reactor) > 0)
                {
                    steps.Add(new BatchAction(Operation.ReturnFeed, index, ChemistryOperations.Total(reactor)));
                    // The original bottle has room for its own smaller remainder.
                    ChemistryOperations.Pour(reactor, original, ChemistryOperations.Total(reactor));
                    ChemicalHelper.HandleEmptyContainerTemp(reactor);
                }
                if (swap)
                {
                    steps.Add(new BatchAction(Operation.SwapTanks));
                    var temporary = reactor; reactor = buffer; buffer = temporary;
                }
                // A large source container may need several real chamber loads.
                // Its own sub-unit tail returns to that source between loads.
                if (liquid && take == available && available == WaterHelper.GetTotalCapacity(reactor) &&
                    ChemistryOperations.Total(original) >= recipe.UnitVolume) source--;
                yield return 0;
            }
            if (bottles == 0)
            {
                if (Diagnostics.Primary == null) Report(any ? "direct_separate_no_output" : "direct_no_raw", 40);
                yield break;
            }
            Result = new BatchPlan { schema = 3, directForm = true, separateDirect = true, directDrying = inputs.directDrying, directSolvents = inputs.directSolvents,
                productId = recipe.Id, actualGrade = 0, requestedGrade = 0, minimumActive = minimum,
                expectedBottles = bottles, outputCapacity = inputs.maxProducts, outputLimited = limited,
                reagents = liquids, reactionSeconds = seconds, concentrationHint = inputs.directDrying == DirectDryingMethod.Heat ? "direct_heat_preview" :
                    inputs.directDrying == DirectDryingMethod.Evaporate ? "direct_evaporate_preview" : "direct_separate_hint", actions = steps.ToArray() };
            Error = null; Diagnostics.Clear();
        }

        private static BatchPlan RevalidateSeparateDirect(BatchInputs input, BatchPlan plan, PlanningDiagnostics diagnostics)
        {
            BatchPlan Fail(string key) { diagnostics?.Add(key, 100); return null; }
            if (!input.directForm || !input.separateDirect || input.directSolvents != plan.directSolvents ||
                input.directDrying != plan.directDrying || !DirectDryingPolicy.Valid(plan.directDrying)) return Fail("changed_settings");
            if (input.maxProducts < plan.expectedBottles) return Fail("changed_output");
            var reactor = new SimulationItem(input.chamber);
            var buffer = input.mashBuffer == null ? null : new SimulationItem(input.mashBuffer);
            var stock = input.supplies.Select(x => new SimulationItem(x)).ToArray();
            var recipe = ChemicalRecipe.Get(plan.productId);
            var result = plan.Copy(); int count = 0, seconds = 0, minimum = recipe.UnitVolume;
            try
            {
                foreach (var step in result.actions)
                {
                    if (!DirectDryingPolicy.Allows(plan.directDrying, step.operation)) return Fail("changed_settings");
                    bool solid = step.operation == Operation.Blend || step.operation == Operation.Dissolve;
                    bool liquid = step.operation == Operation.Pour || step.operation == Operation.ReturnFeed;
                    if ((solid || liquid) && (step.source < 0 || step.source >= (solid ? input.raw.Length : stock.Length))) return Fail("runtime_source_missing");
                    if (solid && !DirectFormInputs.CanUse(input.raw[step.source])) return Fail(DirectFormInputs.ProtectionReason(input.raw[step.source]));
                    if (step.operation == Operation.Pour && !DirectFormInputs.CanUseLiquid(stock[step.source], input.directSolvents)) return Fail(DirectFormInputs.ProtectionReason(stock[step.source]));
                    switch (step.operation)
                    {
                        case Operation.Blend:
                            if (ChemistryOperations.Total(input.raw[step.source]) > WaterHelper.GetFreeCapacity(reactor)) return Fail("runtime_tank_capacity");
                            ChemistryOperations.Blend(new SimulationItem(input.raw[step.source]), reactor, input.blenderLoss); break;
                        case Operation.Dissolve: ChemistryOperations.Dissolve(new SimulationItem(input.raw[step.source]), reactor); break;
                        case Operation.Pour: ChemistryOperations.Pour(stock[step.source], reactor, step.amount); break;
                        case Operation.ReturnFeed:
                            step.amount = ChemistryOperations.Total(reactor);
                            if (step.amount >= recipe.UnitVolume) return Fail("diag_recheck_quantity");
                            ChemistryOperations.Pour(reactor, stock[step.source], step.amount); break;
                        case Operation.StoreMash:
                            if (buffer == null) return Fail("missing_buffer");
                            if (step.precursorId == "direct_remainder") step.amount = ChemistryOperations.Total(reactor);
                            ChemistryOperations.Pour(reactor, buffer, step.amount); break;
                        case Operation.SwapTanks:
                            if (buffer == null) return Fail("missing_buffer");
                            var temporary = reactor; reactor = buffer; buffer = temporary; break;
                        case Operation.BoilDry:
                            var problem = DirectDryingPolicy.HeatProblem(reactor, input.heaterUpgrades);
                            if (problem != null) return Fail(problem);
                            step.amount = HeatedDrying.HeatUntilDry(reactor, input.heaterUpgrades);
                            if (step.amount < 0) return Fail("direct_boil_failed");
                            seconds += step.amount; break;
                        case Operation.Dry:
                            step.evaporationVolume = ChemistryOperations.VolatileVolume(reactor);
                            ChemistryOperations.SimulateDry(reactor); break;
                        case Operation.Form:
                            if (ChemistryOperations.VolatileVolume(reactor) > 0) return Fail("runtime_still_wet");
                            var measured = ChemistryOperations.Measure(reactor, input.finisherLoss, 0, 0, null, recipe);
                            if (measured.Bottles != step.amount) return Fail("diag_recheck_quantity");
                            minimum = Math.Min(minimum, measured.MinimumBottleActiveVolume);
                            ConsumeDirectUnits(reactor, step.amount, recipe.UnitVolume); count += step.amount; break;
                        default: return Fail("runtime_saved_operation");
                    }
                    if (step.operation == Operation.ReturnFeed || step.operation == Operation.StoreMash)
                        ChemicalHelper.HandleEmptyContainerTemp(reactor);
                }
            }
            catch (MachineFault fault) { return Fail(fault.Key); }
            if (count != plan.expectedBottles) return Fail("diag_recheck_quantity");
            int heldUnits = input.deferOutput ? count : result.actions.Where(a => a.operation == Operation.Form).Max(a => a.amount);
            if (input.finisher != null && (long)heldUnits * recipe.UnitVolume > WaterHelper.GetTotalCapacity(input.finisher))
                return Fail("runtime_finisher_capacity");
            result.reactionSeconds = seconds; result.minimumActive = minimum;
            return result;
        }
    }
}
