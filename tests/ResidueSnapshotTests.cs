using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class ResidueSnapshotTests
{
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception("Residue snapshot: " + reason); }
    private static SimulationItem Tank(string part, int volume, int capacity, double temperature)
    {
        var item = new SimulationItem();
        ChemistryOperations.SetAmount(item, part, volume);
        item.ModifyTag(WaterHelper.LIQUID_CONTAINER_CAPACITY, t => t.SetInt(capacity));
        item.ModifyTag(ChemicalHelper.CONTAINER_TEMP_TAG, t => t.SetDouble(temperature));
        return item;
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string product in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true })
        foreach (int excess in new[] { -1, 0, 1, 40000 })
        {
            var recipe = ChemicalRecipe.Get(product);
            var reactor = Tank(recipe.Active, 6000, 10000, 45);
            var finisher = Tank(direct ? "starch" : recipe.Active, 4000 + excess, 100000, 26);
            var originalReactor = ChemistryOperations.Parts(reactor);
            var originalFinisher = ChemistryOperations.Parts(finisher);
            // Both opening the window and refreshing idle guidance call this.
            var preview = FinishingStorage.PreviewStart(reactor, finisher);
            Check(ChemistryOperations.Total(preview) == 10000 + excess, "snapshot lost residue");
            Check(WaterHelper.GetTotalCapacity(preview) == 10000, "snapshot invented vessel capacity");
            Check(ChemistryOperations.Parts(preview).SequenceEqual(originalReactor.Zip(originalFinisher, (a, b) => a + b)), "snapshot changed composition");
            double expectedTemperature = (6000.0 * 45 + (4000 + excess) * 26.0) / (10000 + excess);
            Check(Math.Abs(ChemistryOperations.Temperature(preview) - expectedTemperature) < 0.000001, "snapshot temperature differs from pouring");
            var inputs = new BatchInputs { productId = product, requestedGrade = 30, directForm = direct,
                separateDirect = direct, directSolvents = true, chamber = preview, mashBuffer = new SimulationItem(),
                finisher = finisher, raw = new GameItem[0], supplies = new GameItem[0], heaterUpgrades = 4, finisherLoss = 0 };
            Check(NeutralizerUsage.Read(inputs) != null && AlcoholBudget.Read(inputs) != null, "idle UI guidance failed");
            var planner = new BatchPlanner(inputs);
            var work = planner.Search(); int yields = 0;
            while (work.MoveNext()) Check(++yields < 10000, "planner stalled on residue");
            if (excess > 0)
            {
                Check(planner.Result == null && planner.Error == "runtime_residue_capacity", "capacity error was hidden or misclassified");
                var diagnostics = new PlanningDiagnostics();
                var stale = new BatchPlan { productId = product, directForm = direct, separateDirect = direct };
                Check(BatchPlanner.Revalidate(inputs, stale, diagnostics) == null && diagnostics.Primary?.Key == "runtime_residue_capacity", "start recheck missed overfull snapshot");
                bool rejected = false;
                try { FinishingStorage.MoveRemainder(finisher, reactor); }
                catch (MachineFault fault) { rejected = fault.Key == "runtime_residue_capacity"; }
                Check(rejected, "physical transfer accepted overflowing residue");
            }
            else Check(planner.Result != null, "valid residue no longer plans: " + planner.Error);
            Check(ChemistryOperations.Parts(reactor).SequenceEqual(originalReactor) &&
                ChemistryOperations.Parts(finisher).SequenceEqual(originalFinisher), "reading/planning changed real materials");
            Check(ChemistryOperations.Temperature(reactor) == 45 && ChemistryOperations.Temperature(finisher) == 26, "reading changed real temperatures");
            if (excess > 0)
            {
                var cleanup = new TankCleanup(); var vessels = new GameItem[] { reactor, finisher };
                Check(cleanup.RequestMany(vessels, false) && cleanup.ConfirmMany(vessels, false), "overfull residue could not be cleared");
                Check(ChemistryOperations.Total(FinishingStorage.PreviewStart(reactor, finisher)) == 0, "cleanup did not restore empty snapshot");
            }
        }
        return checks + " residue snapshot checks passed; seven products, normal/fake modes, exact capacity, overflowing residue, UI guidance, recheck, atomic transfer and cleanup.";
    }
}
