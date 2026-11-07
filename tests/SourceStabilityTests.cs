using System;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class SourceStabilityTests
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    internal static string Run()
    {
        var source = Mix("acid", 960000, "microbe", 19000, "chemical_contaminant", 21000);
        var before = ChemistryOperations.Parts(source);
        var input = new BatchInputs { supplies = new GameItem[] { source, Mix("alcohol", 500000) } };
        var plan = new BatchPlan { actions = new[] { new BatchAction(Operation.Dry), new BatchAction(Operation.Pour, 0, 10000) } };
        var diagnostics = new PlanningDiagnostics();
        Check(!SourceStability.Check(input, plan, diagnostics), "reactive reagent accepted");
        Check(diagnostics.Primary?.Key == "diag_stock_reacting" && diagnostics.Primary.Args.Length == 2, "source explanation missing");
        Check(before.SequenceEqual(ChemistryOperations.Parts(source)), "preflight changed the original bottle");
        plan.actions[1].source = 1;
        Check(SourceStability.Check(input, plan, null), "unused reactive stock blocked the batch");
        plan.actions = new[] { new BatchAction(Operation.Pour, 0, 10000), new BatchAction(Operation.Dry) };
        Check(SourceStability.Check(input, plan, null), "immediate feed was treated as a waiting reagent");
        plan.actions = new[] { new BatchAction(Operation.Dry), new BatchAction(Operation.Pour, 0, 10000) };
        plan.separateDirect = true;
        Check(SourceStability.Check(input, plan, null), "isolated fake lot was blocked");
        plan.separateDirect = false;
        for (int i = 0; i < 5; i++) ChemistryOperations.ReactionTick(source);
        Check(SourceStability.Check(input, plan, null), "settled reagent stays blocked");

        foreach (bool deferred in new[] { false, true })
        foreach (int shortfall in new[] { 0, 3, 100, 101, 4000 })
        {
            var vessel = Mix("nomore", 40000 - shortfall);
            var batch = new RunningBatch { schema = 2, useFinisherTank = true, deferOutput = deferred,
                plan = new BatchPlan { productId = "pink_alt_injector", requestedGrade = 99, actualGrade = 99,
                    expectedBottles = 10, requestedBottles = 10, availableBottles = 10,
                    actions = new[] { new BatchAction(Operation.Form, amount: 10) } } };
            var savedParts = ChemistryOperations.Parts(vessel);
            bool adjusted = FinishingStorage.ReconcileRoundingTail(batch, vessel);
            Check(adjusted == (shortfall > 0 && shortfall <= 100), "shortfall boundary changed");
            Check(savedParts.SequenceEqual(ChemistryOperations.Parts(vessel)), "shortfall correction fabricated/removed contents");
            var restored = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
            Check(restored.plan.expectedBottles == (adjusted ? 9 : 10), "corrected target lost on reload");
            Check(!FinishingStorage.ReconcileRoundingTail(restored, vessel), "target corrected twice");
            string diagnostic = BatchFailureSnapshot.Describe(restored, vessel, null);
            Check(diagnostic.Contains("pink_alt_injector") && diagnostic.Contains("operation=Form") &&
                diagnostic.Contains("nomore=") && diagnostic.Contains("tank1=missing"), "failure diagnostic omitted batch evidence");
        }
        return checks + " source stability and rounding-tail checks passed";
    }
}
