using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DreamDustAutomation.Planning;

internal static class HeatedDryingTests
{
    private static int checks;
    private static readonly MethodInfo NativeStove = typeof(MachineHeatingPlate).GetMethod("HeatLiquidOnStove", BindingFlags.Static | BindingFlags.NonPublic);
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Boiling: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static SimulationItem RustWater(int water, int dirt)
    {
        var item = Mix("water", water);
        // Use the installed game's rust-water composition, including the
        // organic waste and microbes lost by native high-temperature reactions.
        foreach (var part in WaterPremadeHelper.rustWaterContaminantDistribution)
            item.state.GetTag(part.Key).Enable().SetInt((int)Math.Round(dirt * (double)part.Value));
        return item;
    }
    private static BatchInputs Input(GameItem water, int upgrades = 4, string id = "dream_dust") => new BatchInputs {
        directForm = true, productId = id, raw = new GameItem[0], supplies = new[] { water },
        chamber = Mix(), mashBuffer = Mix(), heaterUpgrades = upgrades, maxProducts = 2560
    };
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 1000000, "planner did not finish"); }
    private static BatchPlan Plan(BatchInputs input)
    {
        var planner = new BatchPlanner(input); Drain(planner.Search());
        Check(planner.Result != null, planner.Error); return planner.Result;
    }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "native replay rejected preview");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    private static SimulationItem ManualBoil(GameItem input, int upgrades, out int seconds)
    {
        var item = new SimulationItem(input);
        seconds = 0;
        double factor = upgrades == 4 ? 480.0 : upgrades == 3 ? 120.0 : 30.0;
        while (ChemistryOperations.VolatileVolume(item) > 0 && seconds < 900)
        {
            // Invoke the native stove formula directly, independently of the
            // planner's heater implementation, with the manual high setting.
            double temperature = (double)NativeStove.Invoke(null, new object[] {
                ChemistryOperations.Temperature(item), 160.0, ChemistryOperations.Total(item), factor });
            item.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(temperature);
            ChemistryOperations.ReactionTick(item);
            seconds++;
        }
        Check(ChemistryOperations.VolatileVolume(item) == 0, "manual reference failed to boil dry");
        return item;
    }
    internal static string Run()
    {
        checks = 0;
        var summaries = new List<string>();
        foreach (int upgrades in new[] { 0, 1, 2, 3, 4 })
        {
            var water = RustWater(5400000, 1000000);
            var original = ChemistryOperations.Parts(water);
            var input = Input(water, upgrades);
            var plan = Plan(input);
            Check(ChemistryOperations.Parts(water).SequenceEqual(original), "preview used the source water");
            Check(plan.minimumActive == 0, "rust water gained medicinal content");
            Check(plan.reactionSeconds == ReactionProgress.RemainingSeconds(plan, 0, 0), "boiling missing from countdown");
            if (upgrades == 0)
            {
                Check(!plan.actions.Any(a => a.operation == Operation.BoilDry) && plan.actions.Any(a => a.operation == Operation.Dry), "99 C plate boiled water");
                Check(plan.concentrationHint == "direct_boil_upgrade", "module requirement missing");
                Check(EvaporationEstimate.Read(water, null).Volume == 5400000, "18-day reference volume changed");
            }
            else
            {
                var action = plan.actions.Single(a => a.operation == Operation.BoilDry);
                Check(!plan.actions.Any(a => a.operation == Operation.Dry), "boiled batch still waits overnight");
                Check(plan.concentrationHint == "direct_boil_hint", "native heat losses not explained");
                var reference = new SimulationItem(water);
                foreach (var wait in plan.actions.Where(a => a.operation == Operation.React))
                    for (int n = 0; n < wait.amount; n++) ChemistryOperations.ReactionTick(reference);
                reference = ManualBoil(reference, upgrades, out int seconds);
                Check(action.amount == seconds && action.targetTemperature == 160, "does not match native manual heating time");
                Check(plan.expectedBottles == ChemistryOperations.Total(reference) / 2000, "preview kept destroyed dirt as output");
                Check(plan.expectedBottles < 500 && plan.expectedBottles > 0, "rust water heat losses absent");
                Check(ChemistryOperations.Amount(reference, "microbe") == 0 && ChemistryOperations.Amount(reference, "organic_waste") == 0, "native heat reactions omitted");
                Check(seconds < 600, "5.4 L water took more than ten minutes");
                Check(ReactionProgress.RemainingSeconds(plan, Array.IndexOf(plan.actions, action), 10) == seconds - 10, "countdown does not advance");
                var downgraded = Input(water, 0);
                Check(BatchPlanner.Revalidate(downgraded, plan) == null, "removed heater module did not invalidate boiling preview");
                summaries.Add(upgrades + " modules: " + seconds + " s, " + plan.expectedBottles + " products");
            }
            Replay(input, plan);
        }
        foreach (string product in ChemicalRecipe.Ids)
        {
            var input = Input(RustWater(100000, 20000), 4, product);
            var plan = Plan(input);
            Check(plan.actions.Any(a => a.operation == Operation.BoilDry) && plan.minimumActive == 0, "missing zero-potency boiling recipe: " + product);
            Replay(input, plan);
        }
        var largeInput = Input(RustWater(5400000, 1000000));
        var large = Plan(largeInput);
        foreach (int count in new[] { 1, 17, large.expectedBottles / 2 })
        {
            var quantity = new QuantityPlanning(largeInput, large, count); Drain(quantity.Search());
            Check(quantity.Result != null && quantity.Result.expectedBottles == count, "metered boiling failed: " + quantity.Error);
            Check(quantity.Result.actions.Any(a => a.operation == Operation.BoilDry), "quantity planner reverted to overnight evaporation");
            Check(quantity.Result.reagents < large.reagents, "quantity planner boiled the full supply");
            Replay(largeInput, quantity.Result);
        }
        // Wet medicine residues are never silently burned. Dry food/powder
        // must not acquire a pointless boiling or evaporator wait either.
        var protectedInput = Input(RustWater(100000, 20000));
        protectedInput.chamber = Mix("dream_cap_extract", 4000);
        var protectedPlan = Plan(protectedInput);
        Check(protectedPlan.actions.All(a => a.operation != Operation.BoilDry) && protectedPlan.concentrationHint == "direct_boil_protected", "active residue burned");
        Replay(protectedInput, protectedPlan);
        var dryInput = Input(Mix("starch", 40000));
        var dryPlan = Plan(dryInput);
        Check(dryPlan.actions.All(a => a.operation != Operation.BoilDry && a.operation != Operation.Dry), "dry powder waits for water removal");
        Replay(dryInput, dryPlan);
        var solventInput = Input(Mix("water", 100000, "acid", 10000, "starch", 10000)); solventInput.directSolvents = true;
        var solventPlan = Plan(solventInput);
        Check(solventPlan.actions.Any(a => a.operation == Operation.BoilDry) &&
            solventPlan.actions.All(a => a.operation != Operation.Dry), "acid in water forced unnecessary overnight evaporation");
        Check(solventPlan.concentrationHint == "direct_boil_solvents", "mixed-water boiling losses not explained");
        Replay(solventInput, solventPlan);
        MixedWaterRegression();
        var pureInput = Input(Mix()); pureInput.chamber = Mix("water", 100000);
        var pure = new BatchPlanner(pureInput); Drain(pure.Search());
        Check(pure.Result == null && pure.Error == "direct_no_output", "pure water created fake medicine");
        var normal = Input(RustWater(100000, 20000)); normal.directForm = false;
        var normalPlanner = new BatchPlanner(normal); Drain(normalPlanner.Search());
        Check(normalPlanner.Result == null, "normal medicine accepted rusty water");
        return checks + " heated-drying checks passed; native rust water 5.4 L: " + string.Join("; ", summaries);
    }

    private static void MixedWaterRegression()
    {
        foreach (var distribution in new[] { WaterPremadeHelper.waterContaminantDistribution,
            WaterPremadeHelper.rustWaterContaminantDistribution, WaterPremadeHelper.gutterflowContaminantDistribution,
            WaterPremadeHelper.ghostwaterContaminantDistribution })
        {
            var feed = Mix("water", 100000);
            foreach (var part in distribution)
                feed.state.GetTag(part.Key).Enable().SetInt((int)Math.Round(20000 * (double)part.Value));
            foreach (int upgrades in new[] { 0, 1, 4 })
            {
                var input = Input(feed, upgrades); var plan = Plan(input);
                Check(plan.actions.Any(a => a.operation == Operation.BoilDry) == (upgrades > 0), "native water preset took wrong drying route");
                Replay(input, plan);
            }
        }
        // Old CanUse rejected even one microliter of a reagent, although the
        // same mixture dries on the native hotplate. Exercise existing tank
        // leftovers as well as opted-in water/reagent supplies.
        foreach (string reagent in new[] { "acid", "base", "alcohol", "reducing_agent", "stabilizing_agent", "lipobond_agent" })
        foreach (int amount in new[] { 1, 10000 })
        foreach (int upgrades in new[] { 1, 4 })
        {
            var feed = Mix("water", 100000, "starch", 20000, reagent, amount);
            var original = ChemistryOperations.Parts(feed);
            var input = Input(Mix(), upgrades); input.chamber = feed;
            var plan = Plan(input);
            Check(plan.actions.Any(a => a.operation == Operation.BoilDry) && plan.actions.All(a => a.operation != Operation.Dry),
                reagent + "/" + amount + " unnecessarily waits overnight");
            Check(plan.concentrationHint == "direct_boil_solvents", "reagent heat warning missing");
            var reference = new SimulationItem(feed);
            foreach (var wait in plan.actions.Where(a => a.operation == Operation.React))
                for (int n = 0; n < wait.amount; n++) ChemistryOperations.ReactionTick(reference);
            reference = ManualBoil(reference, upgrades, out int duration);
            Check(plan.actions.Single(a => a.operation == Operation.BoilDry).amount == duration, "mixed-water native time mismatch");
            Check(plan.expectedBottles == ChemistryOperations.Total(reference) / 2000 && plan.minimumActive == 0, "mixed-water native yield mismatch");
            Check(original.SequenceEqual(ChemistryOperations.Parts(feed)), "mixed-water preview consumed live contents");
            Replay(input, plan);
        }
        foreach (string reagent in new[] { "acid", "base", "reducing_agent", "stabilizing_agent" })
        {
            var feed = Mix("water", 100000, "starch", 20000, reagent, 10000);
            var off = new BatchPlanner(Input(feed)); Drain(off.Search());
            Check(off.Result == null, "fix opted into solvent feed without consent");
            var on = Input(feed); on.directSolvents = true; var plan = Plan(on);
            Check(plan.actions.Any(a => a.operation == Operation.BoilDry), "opted-in mixed supply did not boil");
            Replay(on, plan);
            var quantity = new QuantityPlanning(on, plan, 3); Drain(quantity.Search());
            Check(quantity.Result != null && quantity.Result.expectedBottles == 3 && quantity.Result.reagents < plan.reagents,
                "mixed-water metering failed: " + quantity.Error);
            Check(quantity.Result.actions.Any(a => a.operation == Operation.BoilDry), "small batch reverted to evaporation");
            Replay(on, quantity.Result);
        }
        foreach (string product in ChemicalRecipe.Ids)
        {
            var input = Input(Mix("water", 100000, "acid", 1, "starch", 40000), 4, product); input.directSolvents = true;
            var plan = Plan(input); Check(plan.actions.Any(a => a.operation == Operation.BoilDry), "mixed-water product route: " + product);
            Replay(input, plan);
        }
        var low = Input(Mix()); low.heaterUpgrades = 0; low.chamber = Mix("water", 100000, "acid", 1, "starch", 20000);
        var lowPlan = Plan(low);
        Check(lowPlan.actions.Any(a => a.operation == Operation.Dry) && lowPlan.concentrationHint == "direct_boil_upgrade", "99 C limit not explained for mixed water");
        var medicinal = Input(Mix()); medicinal.chamber = Mix("water", 100000, "acid", 1, "starch", 20000, "dream_cap_extract", 1);
        var medicinalPlan = Plan(medicinal);
        Check(medicinalPlan.actions.All(a => a.operation != Operation.BoilDry) && medicinalPlan.concentrationHint == "direct_boil_protected", "mixed-water fix burned protected medicine");
        Replay(medicinal, medicinalPlan);
    }
}
