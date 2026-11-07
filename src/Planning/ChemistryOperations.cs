using System;
using System.Linq;
using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    internal static class ChemistryOperations
    {
        public static int Amount(GameItem item, string id)
        {
            string key = Liquid.GetCurrentPartFromId(id);
            if (item is SimulationItem) return item.state.dict.TryGetValue(key, out var tag) ? tag.GetInt() : 0;
            return item.GetTagReadonly(key).GetInt();
        }
        public static int Total(GameItem item)
        {
            if (!(item is SimulationItem)) return WaterHelper.GetTotalVolume(item);
            int total = 0;
            foreach (var liquid in Liquid.Liquids)
                if (item.state.dict.TryGetValue(liquid.currentPart, out var tag)) total = checked(total + tag.GetInt());
            return total;
        }
        public static double Temperature(GameItem item) => item is SimulationItem ?
            item.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble() : item.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble();
        public static void SetAmount(GameItem item, string id, int amount)
        {
            if (amount < 0) throw new InvalidOperationException("Negative material amount.");
            if (item is SimulationItem) item.state.GetTag(Liquid.GetCurrentPartFromId(id)).Enable().SetInt(amount);
            else item.ModifyTag(Liquid.GetCurrentPartFromId(id), s => s.Enable().SetInt(amount));
        }
        public static int VolatileVolume(GameItem item)
        {
            int sum = 0;
            foreach (var liquid in Liquid.Liquids)
                if (liquid.isEvaporative) sum = checked(sum + Amount(item, liquid.identifier));
            return sum;
        }
        public static int[] Parts(GameItem item)
        {
            var parts = new int[Liquid.Liquids.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = Amount(item, Liquid.Liquids[i].identifier);
            return parts;
        }
        public static bool SameParts(int[] left, int[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
        public static List<KeyValuePair<string, int>> RemoveAndReturn(GameItem source, int volume)
        {
            if (!(source is SimulationItem)) return WaterHelper.RemoveAndReturn(source, volume);
            var result = new List<KeyValuePair<string, int>>();
            if (volume <= 0) return result;
            var parts = Parts(source);
            var taken = WaterHelper.SplitProportionally(parts, Math.Min(volume, Total(source)));
            for (int i = 0; i < parts.Length; i++)
            {
                string id = Liquid.Liquids[i].identifier;
                if (taken[i] > 0) result.Add(new KeyValuePair<string, int>(id, taken[i]));
                SetAmount(source, id, parts[i] - taken[i]);
            }
            return result;
        }
        public static void TransferToProduct(GameItem source, GameItem product, int volume)
        {
            // Native TransferLiquidToChem also refreshes the source sprite. Its
            // numerical transfer is public and safe after our simulation-aware removal.
            ChemicalHelper.AddLiquidToChemFromList(product, RemoveAndReturn(source, volume));
        }
        public static void Blend(GameItem ingredient, GameItem target, int loss)
        {
            if (!(target is SimulationItem)) { ChemicalHelper.BlendInto(ingredient, target, loss); return; }
            double retained = 1.0 - Math.Max(0, Math.Min(100, loss)) / 100.0;
            var amounts = new int[Liquid.Liquids.Count];
            for (int i = 0; i < amounts.Length; i++)
                amounts[i] = (int)Math.Round(Amount(ingredient, Liquid.Liquids[i].identifier) * retained, MidpointRounding.AwayFromZero);
            // Vanilla blender distributes each component over ten passes. Preserve
            // its rounding and temperature mixing without AddLiquid's sprite update.
            for (int pass = 0; pass < 10; pass++)
                for (int i = 0; i < amounts.Length; i++)
                {
                    string id = Liquid.Liquids[i].identifier;
                    int add = amounts[i] / 10 + (pass < amounts[i] % 10 ? 1 : 0);
                    add = Math.Min(add, WaterHelper.GetTotalCapacity(target) - Amount(target, id));
                    if (add <= 0) continue;
                    ChemicalHelper.HandleReceiveLiquidTemp(target, add);
                    SetAmount(target, id, checked(Amount(target, id) + add));
                }
            int excess = Total(target) - WaterHelper.GetTotalCapacity(target);
            if (excess > 0) RemoveAndReturn(target, excess);
        }
        public static void Pour(GameItem source, GameItem target, int volume)
        {
            if (volume < 0) throw new MachineFault("runtime_invalid_transfer");
            if (volume > Total(source)) throw new MachineFault("runtime_source_amount");
            if (volume > WaterHelper.GetFreeCapacity(target)) throw new MachineFault("runtime_transfer_capacity");
            if (volume == 0) return;
            double sourceTemperature = Temperature(source);
            var transferred = RemoveAndReturn(source, volume);
            foreach (var part in transferred)
            {
                ChemicalHelper.HandleReceiveLiquidTemp(target, part.Value, sourceTemperature);
                SetAmount(target, part.Key, checked(Amount(target, part.Key) + part.Value));
            }
            if (!(target is SimulationItem)) WaterFeatureHelper.OnLiquidChanged(target, true);
        }
        public static void Dissolve(GameItem source, GameItem target)
        {
            if (Total(source) > WaterHelper.GetFreeCapacity(target)) throw new MachineFault("runtime_tank_capacity");
            if (!(target is SimulationItem)) { ChemicalHelper.DissolveInto(source, target); return; }
            foreach (var liquid in Liquid.Liquids)
                SetAmount(target, liquid.identifier, checked(Amount(target, liquid.identifier) + Amount(source, liquid.identifier)));
        }
        public static void FilterAliquot(GameItem tank, GameItem filter, int volume)
            => FilterAliquot(tank, new[] { filter }, volume);
        public static void FilterAliquot(GameItem tank, GameItem[] filters, int volume)
        {
            if (volume < 0) throw new MachineFault("runtime_invalid_transfer");
            if (volume > Total(tank)) throw new MachineFault("runtime_filter_volume");
            if (filters == null || filters.Length == 0 || filters.Any(filter => filter == null || !filter.IsTag("WATER_FILTER")))
                throw new MachineFault("runtime_filter_missing");
            var parts = Parts(tank);
            var aliquot = WaterHelper.SplitProportionally(parts, volume);
            // Internal recirculation is a normal filtered transfer followed by recombination.
            // Only the amounts removed by the original filter function leave the batch.
            var retained = (int[])aliquot.Clone();
            foreach (var filter in filters)
                for (int i = 0; i < parts.Length; i++)
                    retained[i] = FilterPart(Liquid.Liquids[i], retained[i], tank, filter);
            for (int i = 0; i < parts.Length; i++)
                SetAmount(tank, Liquid.Liquids[i].identifier, parts[i] - aliquot[i] + retained[i]);
            if (!(tank is SimulationItem)) WaterFeatureHelper.OnLiquidChanged(tank, true);
        }
        private static int FilterPart(Liquid liquid, int amount, GameItem tank, GameItem filter)
        {
            if (!(filter is SimulationItem)) return WaterHelper.HandleFilter(liquid.identifier, amount, tank, filter);
            if (amount <= 0) return 0;
            if (filter.identifier == "uv_filter") return liquid.identifier == Liquid.LIQUID_MICROBE ? 0 : amount;
            if (!filter.IsTag("WATER_FILTER") || liquid.identifier == Liquid.LIQUID_MICROBE ||
                filter.GetTagReadonly("FILTER_SIZE_TAG").GetInt() > liquid.particleSize) return amount;
            int durability = DurabilityHelper.GetRealDurability(filter);
            if (durability == 0) return amount;
            int minimum = (int)((double)amount * liquid.filterMinimumPurity);
            int retained = amount;
            for (int steps = 0; durability > 0 && retained > minimum && steps < 10000; steps++)
            {
                if (liquid.filterVolumeRemoved <= 0) break;
                int max = DurabilityHelper.GetMaxDurability(filter);
                durability = Math.Max(0, Math.Min(max, durability - liquid.filterDurabilityUsage));
                int fake = Math.Max(0, Math.Min(max, DurabilityHelper.GetFakeDurability(filter) - liquid.filterDurabilityUsage));
                filter.state.GetTag(DurabilityHelper.DURABILITY_TAG).SetInt(durability);
                filter.state.GetTag(DurabilityHelper.DURABILITY_FAKE_TAG).SetInt(fake);
                retained -= liquid.filterVolumeRemoved;
            }
            return Math.Max(0, Math.Max(minimum, Math.Min(amount, retained)));
        }
        public static void ReactionTick(GameItem tank)
        {
            ChemicalHelper.HandleAmbientTempDrop(tank);
            if (!(tank is SimulationItem)) { ChemicalHelper.HandleTemperatureReaction(tank); return; }
            // Invoke the same native reactions in their native order, without the
            // live item's localization, sprites or feature UI refresh side effects.
            if (WaterHelper.IsEmpty(tank)) return;
            ChemicalHelper.HandleMicrobeHeat(tank);
            ChemicalHelper.HandleMicrobeAlcohol(tank);
            ChemicalHelper.HandleOrganicWasteHeat(tank);
            ChemicalHelper.HandleDreamCapMushReaction(tank);
            ChemicalHelper.HandleNightmareCapMushReaction(tank);
            ChemicalHelper.HandleCoffeeBeanExtractionSolvent(tank);
            ChemicalHelper.HandleCoffeeBeanExtractionWater(tank);
            ChemicalHelper.HandleNomoreReaction(tank);
            ChemicalHelper.HandleDreamCapExtractDegradation(tank);
            ChemicalHelper.HandleDreamCapMushDegradation(tank);
            ChemicalHelper.HandleNightmareCapExtractDegradation(tank);
            ChemicalHelper.HandleNightmareCapMushDegradation(tank);
            ChemicalHelper.HandleOrganicWasteMineralization(tank);
            ChemicalHelper.HandleAcidBaseMicrobeKill(tank);
            ChemicalHelper.HandleStabilizingAgentSedimentation(tank);
            ChemicalHelper.HandleProteinPrecipitation(tank);
            ChemicalHelper.HandleLipobondAgentSedimentation(tank);
            ChemicalHelper.HandleAcidBaseNeutralization(tank);
            ChemicalHelper.HandleFantaHeat(tank);
            ChemicalHelper.HandleMethylolHeatReaction(tank);
            ChemicalHelper.HandleChlorine(tank);
            ChemicalHelper.HandleBoilingReaction(tank);
        }
        public static int ExtractionTargetTemperature(int upgrades, int requestedTemperature = 45) =>
            upgrades >= 2 ? requestedTemperature : upgrades == 1 ? 160 : 99;

        public static double NextHeatedTemperature(double current, int upgrades, int mode, int requestedTemperature = 45)
        {
            if (mode == 0) return current;
            double target = ExtractionTargetTemperature(upgrades, requestedTemperature);
            double factor = mode == 2 ? 30 : 10;
            if (upgrades == 3) factor *= 4;
            else if (upgrades >= 4) factor *= 16;
            if (current >= target) return current;
            return Math.Min(current + (target - current) * factor / 350.0 + 1.0, target);
        }
        public static int HeatingMode(GameItem tank, int upgrades, int requestedTemperature = 45)
        {
            double temperature = Temperature(tank);
            // Extraction starts at 40 C, but unextracted mush already degrades
            // at 50 C. Protect the raw material as well as the finished extract.
            double ceiling = requestedTemperature == 52 ? 55 : requestedTemperature == 57 ? 60 : requestedTemperature + 5;
            if (temperature >= requestedTemperature) return 0;
            if (NextHeatedTemperature(temperature, upgrades, 2, requestedTemperature) < ceiling) return 2;
            if (NextHeatedTemperature(temperature, upgrades, 1, requestedTemperature) < ceiling) return 1;
            return 0;
        }
        // Both simulation and live processing use the batch's saved upgrades.
        // Native HandleHeating reads the current module inventory, which may
        // have gained modules for the next batch. The stove arithmetic below
        // is identical to native heating, without changing that inventory.
        public static bool ApplyBatchHeating(GameItem tank, int upgrades, int requestedTemperature = 45)
        {
            int mode = HeatingMode(tank, upgrades, requestedTemperature);
            if (mode == 0) return false;
            double next = NextHeatedTemperature(Temperature(tank), upgrades, mode, requestedTemperature);
            tank.ModifyTag(ChemicalHelper.CONTAINER_TEMP_TAG, s => s.Enable().SetDouble(next), skipValidate: true);
            return true;
        }
        public static void SimulateHeatedSecond(GameItem tank, int upgrades, int requestedTemperature = 45)
        {
            ApplyBatchHeating(tank, upgrades, requestedTemperature);
            ReactionTick(tank);
        }
        public static void SimulateDry(GameItem tank)
        {
            foreach (var liquid in Liquid.Liquids)
                if (liquid.isEvaporative) SetAmount(tank, liquid.identifier, 0);
        }
        public static ProductionOutcome Measure(GameItem wetBatch, int finisherLoss, int reagents, int seconds, object plan,
            ChemicalRecipe recipe = null, int maxProducts = int.MaxValue)
        {
            recipe = recipe ?? ChemicalRecipe.Get();
            int unitVolume = recipe.UnitVolume;
            // Bottling is integer proportional allocation, not an item/UI operation.
            // Keep vanilla's exact splitter (including tie order) for both transfers
            // and finisher losses, without constructing an item for every bottle.
            var parts = Parts(wetBatch);
            int volume = 0, activeIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (Liquid.Liquids[i].isEvaporative) parts[i] = 0;
                if (Liquid.Liquids[i].identifier == recipe.Active) activeIndex = i;
                volume = checked(volume + parts[i]);
            }
            if (activeIndex < 0) throw new InvalidOperationException("Recipe ingredient is missing from the game liquid directory.");
            int bottles = Math.Min(volume / unitVolume, Math.Max(0, maxProducts));
            int minimumActive = unitVolume;
            int activeOutput = 0;
            for (int i = 0; i < bottles; i++)
            {
                var product = WaterHelper.SplitProportionally(parts, unitVolume);
                for (int j = 0; j < parts.Length; j++) parts[j] -= product[j];
                int removedVolume = unitVolume * Math.Max(0, Math.Min(100, finisherLoss)) / 100;
                int active = product[activeIndex];
                if (removedVolume > 0) active -= WaterHelper.SplitProportionally(product, removedVolume)[activeIndex];
                activeOutput = checked(activeOutput + active);
                minimumActive = Math.Min(minimumActive, active);
            }
            return new ProductionOutcome(activeOutput, bottles * unitVolume, bottles,
                bottles > 0 ? minimumActive : 0, reagents, seconds, plan, unitVolume);
        }
    }
}
