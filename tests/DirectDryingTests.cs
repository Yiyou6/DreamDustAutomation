using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using DreamDustAutomation.Planning;

internal static class DirectDryingTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Drying choice: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static BatchInputs Input(DirectDryingMethod method, int modules, params GameItem[] sources) => new BatchInputs {
        directForm = true, separateDirect = true, directDrying = method, directSolvents = true,
        productId = "dream_dust", chamber = Mix(), mashBuffer = Mix(), raw = new GameItem[0], supplies = sources,
        heaterUpgrades = modules, maxProducts = 3000, finisherLoss = 12
    };
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 100000, "search did not finish"); }
    private static BatchPlanner Plan(BatchInputs input) { var p = new BatchPlanner(input); Drain(p.Search()); return p; }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(plan != null && plan.directDrying == input.directDrying && plan.Copy().directDrying == input.directDrying, "method lost from plan");
        var diagnostics = new PlanningDiagnostics();
        Check(BatchPlanner.Revalidate(input, plan, diagnostics) != null, "start rejected: " + diagnostics.Primary?.Key);
        Check(plan.actions.All(a => a.operation != Operation.React && DirectDryingPolicy.Allows(plan.directDrying, a.operation)), "wrong drying operation");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    private static int NativeCount(GameItem source, BatchInputs input)
    {
        var tank = new SimulationItem(source);
        if (input.directDrying == DirectDryingMethod.Heat)
        {
            int n = 0;
            while (ChemistryOperations.VolatileVolume(tank) > 0 && n++ < HeatedDrying.TickLimit)
                ChemistryOperations.SimulateHeatedSecond(tank, input.heaterUpgrades, HeatedDrying.TargetTemperature);
            Check(ChemistryOperations.VolatileVolume(tank) == 0, "manual heating did not dry");
        }
        else ChemistryOperations.SimulateDry(tank);
        var recipe = ChemicalRecipe.Get(input.productId); int count = 0;
        while (ChemistryOperations.Total(tank) >= recipe.UnitVolume)
        {
            var product = Mix(); ChemistryOperations.TransferToProduct(tank, product, recipe.UnitVolume);
            MachineChemFinisher.IntroduceImpurity(product, input.finisherLoss); count++;
        }
        return count;
    }
    internal static string Run()
    {
        checks = 0;
        Check(DirectDryingPolicy.Preference(0) == DirectDryingMethod.Evaporate && DirectDryingPolicy.Preference(-1) == DirectDryingMethod.Evaporate &&
            DirectDryingPolicy.Preference(99) == DirectDryingMethod.Evaporate, "new/old machine default is not evaporation");
        Check(DirectDryingPolicy.Preference((int)DirectDryingMethod.Heat) == DirectDryingMethod.Heat, "heat preference not restored");
        foreach (int modules in new[] { 0, 1, 2, 3, 4 }) Check(DirectDryingPolicy.CanSelectHeat(modules) == (modules >= 1), "heat-button gate");
        foreach (string product in ChemicalRecipe.Ids)
        foreach (var method in new[] { DirectDryingMethod.Evaporate, DirectDryingMethod.Heat })
        foreach (int modules in new[] { 0, 1, 4 })
        {
            var input = Input(method, modules, Mix("water", 20000, "organic_waste", 12000, "microbe", 6000, "starch", 32000),
                Mix("water", 18000, "physical_contaminant", 24000));
            input.productId = product;
            string before = string.Join(";", input.supplies.Select(s => string.Join(",", ChemistryOperations.Parts(s))));
            var planner = Plan(input);
            if (method == DirectDryingMethod.Heat && modules == 0)
            { Check(planner.Result == null && planner.Error == "direct_heat_modules", "no-module heat silently fell back"); continue; }
            var plan = planner.Result; Replay(input, plan);
            Check(plan.expectedBottles == input.supplies.Sum(s => NativeCount(s, input)), "yield differs from native selected process: " + product + "/" + method);
            Check((plan.reactionSeconds == 0) == (method == DirectDryingMethod.Evaporate), "wrong reaction countdown");
            Check(plan.actions.Any(a => a.operation == (method == DirectDryingMethod.Evaporate ? Operation.Dry : Operation.BoilDry)), "missing requested drying stage");
            var quantity = new QuantityPlanning(input, plan, 2); Drain(quantity.Search());
            if (quantity.Result == null)
                Check(quantity.Error == "direct_quantity_step" && method == DirectDryingMethod.Heat, "unexplained metering failure: " + quantity.Error);
            else
            {
                Check(quantity.Result.expectedBottles == 2, "metering produced a different count");
                Replay(input, quantity.Result);
                var rest = BatchPersistenceTests.RemainingAfter(input, quantity.Result);
                Check(rest.directDrying == method, "next batch lost drying choice");
            }
            Check(before == string.Join(";", input.supplies.Select(s => string.Join(",", ChemistryOperations.Parts(s)))), "preview changed live stock");
            var snapshot = PlanningInputState.Capture(input);
            input.directDrying = method == DirectDryingMethod.Heat ? DirectDryingMethod.Evaporate : DirectDryingMethod.Heat;
            var report = new PlanningDiagnostics();
            Check(BatchPlanner.Revalidate(input, plan, report) == null && report.Primary.Key == "changed_settings", "changed drying method kept old plan");
            Check(snapshot.Changes(PlanningInputState.Capture(input)).Issues.Any(issue => issue.Key == "changed_settings"), "fingerprint omitted drying choice");
        }
        foreach (string product in ChemicalRecipe.Ids)
        foreach (var method in new[] { DirectDryingMethod.Evaporate, DirectDryingMethod.Heat })
        {
            var input = Input(method, 4, Mix("water", 12000, "starch", 50000)); input.productId = product;
            var full = Plan(input).Result;
            var q = new QuantityPlanning(input, full, 2); Drain(q.Search());
            Check(q.Result != null && q.Result.expectedBottles == 2, "simple metered count failed: " + product + "/" + method);
            Replay(input, q.Result);
        }
        var medicinal = Input(DirectDryingMethod.Heat, 4); medicinal.chamber = Mix("water", 10000, "dream_cap_extract", 10000);
        Check(Plan(medicinal).Error == "direct_heat_medicine", "medicinal residue heated destructively");
        medicinal.directDrying = DirectDryingMethod.Evaporate; Replay(medicinal, Plan(medicinal).Result);
        var nonwater = Input(DirectDryingMethod.Heat, 4, Mix("lipobond_agent", 10000, "starch", 5000));
        Check(Plan(nonwater).Result == null, "lipobond was consumed by heat drying fake mode");
        nonwater.directDrying = DirectDryingMethod.Evaporate;
        Check(Plan(nonwater).Result == null, "lipobond was consumed by evaporating fake mode");
        // A dry sediment, dry acid/base, dirty water and water-free lipobond
        // share the input bin but retain separate native processing and yields.
        foreach (string product in ChemicalRecipe.Ids)
        foreach (int modules in new[] { 1, 4 })
        foreach (bool solvents in new[] { false, true })
        {
            var sediment = Mix("starch", 11000);
            sediment.state.GetTag(WaterHelper.LIQUID_CONTAINER_TAG).Disable();
            sediment.state.GetTag("SOLUBLE_TAG").Enable();
            var mixed = Input(DirectDryingMethod.Heat, modules,
                Mix("lipobond_agent", 10000, "starch", 5000),
                Mix("acid", 13000), Mix("base", 17000),
                Mix("water", 20000, "starch", 19000, "organic_waste", 9000));
            mixed.raw = new GameItem[] { sediment }; mixed.productId = product; mixed.directSolvents = solvents;
            var plan = Plan(mixed).Result; Replay(mixed, plan);
            int expected = NativeCount(sediment, mixed) + mixed.supplies
                .Where(s => DirectFormInputs.CanUseLiquid(s, solvents)).Sum(s => NativeCount(s, mixed));
            Check(plan.expectedBottles == expected, "mixed feed total differs from independent native yields: " + product);
            Check(plan.actions.Count(a => a.operation == Operation.BoilDry) == 1, "dry feed heated or protected lipobond consumed");
            Check(plan.actions.Any(a => a.operation == Operation.Dissolve), "sediment excluded");
            Check(!plan.actions.Any(a => a.operation == Operation.Pour && a.source == 0), "lipobond not kept");
            foreach (int source in new[] { 1, 2 })
                Check(plan.actions.Any(a => a.operation == Operation.Pour && a.source == source) == solvents, "reagent opt-in ignored");
            int currentSource = -1;
            foreach (var action in plan.actions)
            {
                if (action.operation == Operation.Pour) currentSource = action.source;
                if (action.operation == Operation.BoilDry)
                    Check(currentSource == 3, "dry sediment, protected lipobond or acid/base given a boiling stage");
            }
            var quantity = new QuantityPlanning(mixed, plan, 2); Drain(quantity.Search());
            Check(quantity.Result != null && quantity.Result.expectedBottles == 2, "mixed-source quantity selection failed");
            Replay(mixed, quantity.Result);
        }
        // Reproduce the reported save: lipobond was first, followed by dry
        // acid/base and two dirty-water bottles, with one heater module.
        var reported = Input(DirectDryingMethod.Heat, 1,
            Mix("lipobond_agent", 141600, "heavy_metal", 720, "microbe", 293, "chemical_contaminant", 2183, "physical_contaminant", 5204),
            Mix("base", 237318, "heavy_metal", 233, "chemical_contaminant", 9741, "physical_contaminant", 2079),
            Mix("acid", 241959, "heavy_metal", 414, "chemical_contaminant", 5157, "physical_contaminant", 944),
            Mix("water", 427800, "organic_waste", 96338, "microbe", 48330, "mineral", 48330, "chemical_contaminant", 96660, "physical_contaminant", 32220, "heavy_metal", 322),
            Mix("water", 616425, "organic_waste", 39941, "microbe", 20036, "mineral", 20036, "chemical_contaminant", 40072, "physical_contaminant", 13357, "heavy_metal", 133));
        var reportedPlan = Plan(reported).Result; Replay(reported, reportedPlan);
        Check(reportedPlan.expectedBottles == reported.supplies.Where(s => DirectFormInputs.CanUseLiquid(s, true)).Sum(s => NativeCount(s, reported)), "reported save yield differs from native reference");
        reported.supplies = Enumerable.Reverse(reported.supplies).ToArray();
        Check(Plan(reported).Result.expectedBottles == reportedPlan.expectedBottles, "input order changed full-batch yield");
        foreach (var method in new[] { DirectDryingMethod.Evaporate, DirectDryingMethod.Heat })
        {
            var dry = Input(method, 0, Mix("starch", 9000));
            var p = Plan(dry).Result; Replay(dry, p);
            Check(!p.actions.Any(a => a.operation == Operation.BoilDry || a.operation == Operation.Dry), "already-dry feed wasted processing");
        }
        var savedInput = Input(DirectDryingMethod.Evaporate, 4, Mix("water", 10000, "starch", 9000));
        var savedPlan = Plan(savedInput).Result;
        var saved = new RunningBatch { plan = savedPlan, heaterUpgrades = 4 };
        string json = BatchPersistence.Serialize(saved);
        Check(BatchPersistence.Deserialize(json).plan.directDrying == DirectDryingMethod.Evaporate, "load lost explicit evaporation");
        var old = JObject.Parse(json); ((JObject)old["plan"]).Remove("directDrying");
        Check(BatchPersistence.Deserialize(old.ToString()).plan.directDrying == DirectDryingMethod.LegacyAutomatic, "old running batch migrated unexpectedly");
        foreach (int corrupt in new[] { (int)DirectDryingMethod.Heat, 99 })
        {
            var data = JObject.Parse(json); data["plan"]["directDrying"] = corrupt; bool rejected = false;
            try { BatchPersistence.Deserialize(data.ToString()); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid/conflicting saved drying accepted");
        }
        savedInput.directDrying = DirectDryingMethod.LegacyAutomatic;
        var legacy = Plan(savedInput).Result;
        Check(legacy.actions.Any(a => a.operation == Operation.BoilDry), "legacy automatic planning changed"); Replay(savedInput, legacy);
        return "Direct drying choices: " + checks + " checks passed (default evaporation, module gate, selected native losses, all products, quantity, preflight, old/new saves)";
    }
}
