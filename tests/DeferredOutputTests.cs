using System;
using System.Linq;
using DreamDustAutomation.Planning;
using Newtonsoft.Json.Linq;

internal static class DeferredOutputTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Deferred output: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static RunningBatch Reload(RunningBatch batch) => BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
    private static BatchPlan Plan(BatchInputs inputs)
    {
        var planner = new BatchPlanner(inputs); var work = planner.Search(); int n = 0;
        while (work.MoveNext()) Check(++n < 100000, "planner stalled");
        Check(planner.Result != null, planner.Error);
        return planner.Result;
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string id in ChemicalRecipe.Ids)
        {
            var recipe = ChemicalRecipe.Get(id);
            var input = new BatchInputs { productId = id, requestedGrade = 99, raw = new GameItem[0], supplies = new GameItem[0],
                chamber = Mix(recipe.Active, recipe.UnitVolume * 12 + 137), mashBuffer = Mix(), finisherLoss = 0, heaterUpgrades = 4 };
            var plan = new BatchPlan { productId = id, requestedGrade = 99, actualGrade = 99, expectedBottles = 12,
                minimumActive = recipe.UnitVolume, actions = new[] { new BatchAction(Operation.Form, amount: 12) } };
            checks += BatchPersistenceTests.CheckFinisherRoute(input, plan, false);
            checks += BatchPersistenceTests.CheckFinisherRoute(input, plan, true);
        }
        foreach (var method in new[] { DirectDryingMethod.Evaporate, DirectDryingMethod.Heat })
        foreach (string id in new[] { "dream_dust", "pink_alt_injector" })
        {
            var input = new BatchInputs { directForm = true, separateDirect = true, directSolvents = true,
                directDrying = method, productId = id, raw = new GameItem[0],
                supplies = new GameItem[] { Mix("water", 40000, "physical_contaminant", 8501), Mix("acid", 12013), Mix("base", 16017) },
                chamber = Mix("starch", 6607), mashBuffer = Mix("starch", 4511), heaterUpgrades = 4, finisherLoss = 12, maxProducts = 2560 };
            var plan = Plan(input);
            Check(plan.actions.Count(a => a.operation == Operation.Form) >= 2, "not a multi-source fake batch");
            checks += BatchPersistenceTests.CheckFinisherRoute(input, plan, false);
            checks += BatchPersistenceTests.CheckFinisherRoute(input, plan, true);
        }
        var normal = new RunningBatch { schema = 2, useFinisherTank = true, deferOutput = true,
            plan = new BatchPlan { productId = "dream_dust", requestedGrade = 99, actualGrade = 99, expectedBottles = 12,
                actions = new[] { new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 12) } } };
        Check(!DeferredOutput.Waiting(normal) && !DeferredOutput.Release(normal, 3), "released before processing");
        normal.step = 1;
        Check(!DeferredOutput.Waiting(normal), "reactor was not yet transferred to finisher");
        normal.prepared = 12; normal.outputCommitted = true; normal = Reload(normal);
        Check(DeferredOutput.Waiting(normal), "prepared batch not waiting");
        Check(!DeferredOutput.Release(normal, 0) && !DeferredOutput.Release(normal, 13), "invalid quantity accepted");
        Check(DeferredOutput.Release(normal, 4), "valid release rejected");
        normal.produced = 2; normal = Reload(normal);
        Check(DeferredOutput.Target(normal) == 4 && DeferredOutput.Protected(normal), "reload forgot permission or unlocked batch");
        DeferredOutput.Pause(normal); normal = Reload(normal);
        Check(DeferredOutput.Waiting(normal) && DeferredOutput.Remaining(normal) == 10, "pause lost remaining output");
        Check(DeferredOutput.Release(normal, DeferredOutput.Remaining(normal)) && DeferredOutput.Target(normal) == 12, "release all failed");
        var invalid = JObject.Parse(BatchPersistence.Serialize(normal)); invalid["releaseTarget"] = 13;
        bool rejected = false; try { BatchPersistence.Deserialize(invalid.ToString()); } catch (System.IO.InvalidDataException) { rejected = true; }
        Check(rejected, "invalid saved output permission accepted");
        invalid = JObject.Parse(BatchPersistence.Serialize(normal)); invalid.Remove("prepared"); rejected = false;
        try { BatchPersistence.Deserialize(invalid.ToString()); } catch (System.IO.InvalidDataException) { rejected = true; }
        Check(rejected, "incomplete new-format save accepted");

        // Native material conservation, insufficient-capacity atomicity, residue
        // merging and multi-vessel cleanup consent including empty vessels.
        var reactor = Mix("dream_cap_extract", 25137); var finishing = Mix();
        finishing.ModifyTag(WaterHelper.LIQUID_CONTAINER_CAPACITY, t => t.SetInt(100));
        rejected = false; try { FinishingStorage.Stage(reactor, finishing, 12, 2000, false); } catch (MachineFault) { rejected = true; }
        Check(rejected && ChemistryOperations.Total(reactor) == 25137 && ChemistryOperations.Total(finishing) == 0, "failed transfer consumed material");
        finishing.ModifyTag(WaterHelper.LIQUID_CONTAINER_CAPACITY, t => t.SetInt(10000000));
        FinishingStorage.Stage(reactor, finishing, 12, 2000, false);
        Check(ChemistryOperations.Total(reactor) == 0 && ChemistryOperations.Total(finishing) == 25137, "normal medicine not in finisher");
        var preview = FinishingStorage.PreviewStart(reactor, finishing);
        Check(ChemistryOperations.Total(preview) == 25137 && ChemistryOperations.Total(reactor) == 0 && ChemistryOperations.Total(finishing) == 25137, "preview mutated vessels");
        FinishingStorage.MoveRemainder(finishing, reactor);
        Check(ChemistryOperations.SameParts(ChemistryOperations.Parts(preview), ChemistryOperations.Parts(reactor)) && ChemistryOperations.Total(finishing) == 0, "residue start differs from preview");
        var buffer = Mix("starch", 127); var vessels = new GameItem[] { reactor, buffer, finishing };
        var cleanup = new TankCleanup(); Check(cleanup.RequestMany(vessels, false), "cleanup review rejected");
        ChemistryOperations.SetAmount(finishing, "starch", 1);
        Check(!cleanup.ConfirmMany(vessels, false) && ChemistryOperations.Total(reactor) == 25137, "changed finisher did not invalidate all-vessel consent");
        Check(TankResidue.ReadAll(vessels, "dream_dust").TotalVolume == 25265, "residue readout omitted vessel");
        cleanup.RequestMany(vessels, false);
        Check(!cleanup.ConfirmMany(vessels, true) && ChemistryOperations.Total(buffer) == 127, "cleanup allowed during pending batch");
        cleanup.RequestMany(vessels, false); Check(cleanup.ConfirmMany(vessels, false) && vessels.All(t => ChemistryOperations.Total(t) == 0), "cleanup missed vessel");
        return "Finisher storage and deferred output: " + checks + " checks passed; seven products, independent fake lots pooled, normal/heated drying, partial/all output, reload, capacity, residue merge and cleanup.";
    }
}
