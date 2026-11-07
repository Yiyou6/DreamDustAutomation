using System;
using System.Collections;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class DirectSolventTests
{
    private static int checks;
    private static readonly string[] reagents = { "acid", "base", "reducing_agent", "stabilizing_agent" };
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Direct solvents: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static BatchInputs Input(string product, params GameItem[] stock) => new BatchInputs {
        directForm = true, directSolvents = true, productId = product, raw = new GameItem[0], supplies = stock,
        chamber = Mix(), mashBuffer = Mix(), requestedGrade = 99, maxProducts = 200
    };
    private static void Drain(IEnumerator work) { int n = 0; while (work.MoveNext()) Check(++n < 100000, "search failed to finish"); }
    private static BatchPlanner Calculate(BatchInputs input) { var p = new BatchPlanner(input); Drain(p.Search()); return p; }
    private static void Replay(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "start revalidation rejected preview");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
    }
    internal static string Run()
    {
        checks = 0;
        foreach (string product in ChemicalRecipe.Ids)
        foreach (string reagent in reagents)
        {
            var recipe = ChemicalRecipe.Get(product);
            var bottle = Mix("starch", recipe.UnitVolume * 8, reagent, recipe.UnitVolume * 4);
            int[] before = ChemistryOperations.Parts(bottle);
            var input = Input(product, bottle); input.finisherLoss = 12;
            var plan = Calculate(input).Result;
            Check(plan != null && plan.minimumActive == 0, "reagent feed failed: " + product + "/" + reagent);
            Check(plan.actions.Count(a => a.operation == Operation.Pour) == 1 && plan.reagents == ChemistryOperations.Total(bottle), "wrong withdrawal");
            Check(plan.actions.All(a => a.operation != Operation.Filter && a.operation != Operation.Extract && a.operation != Operation.Synthesize), "direct mode started purification");
            Check(DirectFormInputs.ReagentsInPlan(input, plan).SequenceEqual(new[] { reagent }), "selected reagent missing from warning");
            var reference = new SimulationItem(bottle);
            for (int n = 0; n < plan.reactionSeconds; n++) ChemistryOperations.ReactionTick(reference);
            foreach (var liquid in Liquid.Liquids.Where(x => x.isEvaporative)) ChemistryOperations.SetAmount(reference, liquid.identifier, 0);
            int count = 0;
            while (ChemistryOperations.Total(reference) >= recipe.UnitVolume)
            {
                var output = Mix();
                ChemistryOperations.TransferToProduct(reference, output, recipe.UnitVolume);
                MachineChemFinisher.IntroduceImpurity(output, input.finisherLoss);
                Check(ChemistryOperations.Amount(output, recipe.Active) == 0, "native fake product contains active medicine");
                count++;
            }
            Check(plan.expectedBottles == count, "yield differs from native manual forming");
            Replay(input, plan);
            Check(before.SequenceEqual(ChemistryOperations.Parts(bottle)), "preview/warning consumed contents");
            Check(RecoveredSolvent.Supplies(new[] { bottle }, null, null, out var ignored).Contains(bottle), "reagent disappeared from normal mode");
            var guide = InputGuidance.Read(product, true, new[] { bottle }, input.chamber, input.mashBuffer, useSolvents: true);
            Check(guide.Summary == "input_direct_solvents", "reagent warning hidden by generic hint");
            guide = InputGuidance.Read(product, false, new[] { bottle }, input.chamber, input.mashBuffer);
            Check(guide.MissingReason == "input_aux_only" && !guide.Hints.Contains("input_direct_solvents"), "normal-mode guidance changed");
        }
        foreach (var bottle in new[] { Mix("lipobond_agent", 20000), Mix("lipobond_agent", 1, "starch", 20000),
            Mix("lipobond_agent", 10000, "water", 10000, "physical_contaminant", 10000) })
        {
            foreach (bool separate in new[] { false, true })
            foreach (bool enabled in new[] { false, true })
            {
                var input = Input("dream_dust", bottle); input.separateDirect = separate; input.directSolvents = enabled;
                Check(!DirectFormInputs.CanUseLiquid(bottle, enabled), "lipobond bottle was admitted");
                Check(Calculate(input).Result == null, "lipobond bottle produced fake medicine");
                var guide = InputGuidance.Read("dream_dust", true, input.supplies, input.chamber, null, useSolvents: enabled);
                Check(guide.MissingReason == "input_direct_lipobond_kept", "missing lipobond exclusion explanation");
                input.supplies = new[] { bottle, Mix("starch", 20000) };
                var plan = Calculate(input).Result;
                Check(plan != null && plan.expectedBottles == 10 && !plan.actions.Any(a => a.operation == Operation.Pour && a.source == 0), "lipobond affected other filler");
                var remaining = BatchPersistenceTests.RemainingAfter(input, plan);
                Check(ChemistryOperations.Parts(remaining.supplies[0]).SequenceEqual(ChemistryOperations.Parts(bottle)), "lipobond mixture changed");
                Check(!DirectFormInputs.ReagentsInPlan(input, plan).Contains("lipobond_agent"), "unused lipobond warning");
                Replay(input, plan);
            }
            Check(RecoveredSolvent.Supplies(new[] { bottle }, null, null, out var ignored).Contains(bottle), "normal medicine lost lipobond supply");
        }
        foreach (var bottle in new[] { Mix("alcohol", 10000), Mix("alcohol", 7000, "water", 3000),
            Mix("alcohol", 1, "starch", 9999), Mix("alcohol", 1, "acid", 9999) })
        {
            Check(!DirectFormInputs.CanUseLiquid(bottle), "alcohol-containing solution used as fake feed");
            var input = Input("dream_dust", bottle);
            Check(Calculate(input).Result == null, "alcohol-containing stock produced fake medicine");
            Check(InputGuidance.Read("dream_dust", true, input.supplies, input.chamber, null).MissingReason == "input_direct_alcohol_kept", "missing alcohol exclusion explanation");
            Check(RecoveredSolvent.Supplies(input.supplies, null, null, out var ignored).Contains(bottle), "normal alcohol extraction lost its supplies");
        }
        foreach (var alcohol in new[] { Mix("alcohol", 10000), Mix("alcohol", 7000, "water", 3000) })
            Check(WaterHelper.IsWet(alcohol) && !ChemFinisherRecipe.GetRecipeById("dream_dust").CanOutput(alcohol), "native alcohol wetness/finishing gate changed");
        Check(ChemFinisherRecipe.GetRecipeById("dream_dust").CanOutput(Mix("acid", 10000)), "native dry acid should be formable");
        foreach (string reagent in new[] { "acid", "base", "reducing_agent", "stabilizing_agent" })
        {
            var input = Input("dream_dust", Mix(reagent, 20000));
            var p = Calculate(input).Result;
            Check(p != null && p.expectedBottles == 10, "nonvolatile native reagent could not form"); Replay(input, p);
        }
        var neutral = Input("dream_dust", Mix("acid", 20000, "starch", 8000), Mix("base", 20000));
        var neutralPlan = Calculate(neutral).Result;
        Check(neutralPlan != null, "neutralization with filler produced no batch");
        var native = Mix("acid", 20000, "base", 20000, "starch", 8000);
        for (int n = 0; n < neutralPlan.reactionSeconds; n++) ChemicalHelper.HandleAcidBaseNeutralization(native);
        ChemistryOperations.SimulateDry(native);
        Check(neutralPlan.expectedBottles == ChemistryOperations.Total(native) / 2000, "neutralization output inflated");
        Check(DirectFormInputs.ReagentsInPlan(neutral, neutralPlan).SequenceEqual(new[] { "acid", "base" }), "reacted-away reagents still need warning");
        Replay(neutral, neutralPlan);
        var consumed = Calculate(Input("dream_dust", Mix("acid", 20000), Mix("base", 20000)));
        Check(consumed.Result == null && consumed.Error == "direct_no_output", "native acid/base neutralization must not invent solids");

        var metered = Input("dream_dust", Mix("acid", 20000), Mix("stabilizing_agent", 10000), Mix("alcohol", 10000));
        var all = Calculate(metered).Result;
        Check(all != null, "metered stock produced no batch");
        Check(all.directSolvents && all.Copy().directSolvents, "option missing from batch/copy");
        metered.directSolvents = false;
        Check(BatchPlanner.Revalidate(metered, all) == null, "changed solvent toggle kept old preview");
        Check(Calculate(metered).Result == null, "disabled solvent feed consumed reagents");
        Check(InputGuidance.Read("dream_dust", true, metered.supplies, metered.chamber, null).MissingReason == "input_direct_solvents_kept", "missing disabled-switch explanation");
        metered.directSolvents = true;
        Check(DirectFormInputs.ReagentsInPlan(metered, all).SequenceEqual(new[] { "acid", "stabilizing_agent" }), "full plan warning incomplete or includes excluded alcohol");
        var quantity = new QuantityPlanning(metered, all, 3); Drain(quantity.Search());
        var small = quantity.Result;
        Check(small != null && small.expectedBottles == 3 && small.reagents == 6000, "metered reagent draw");
        Check(small.directSolvents, "quantity selection lost the option");
        Check(DirectFormInputs.ReagentsInPlan(metered, small).SequenceEqual(new[] { "acid" }), "warning includes unselected solvent stock");
        Replay(metered, small);
        var rest = BatchPersistenceTests.RemainingAfter(metered, small);
        Check(ChemistryOperations.Amount(rest.supplies[0], "acid") == 14000 && ChemistryOperations.Amount(rest.supplies[1], "stabilizing_agent") == 10000 &&
            ChemistryOperations.Amount(rest.supplies[2], "alcohol") == 10000, "unused solvents were lost");
        var second = Calculate(rest).Result;
        var remainingNative = Mix("acid", 14000, "stabilizing_agent", 10000);
        for (int n = 0; n < 100; n++) ChemicalHelper.HandleStabilizingAgentSedimentation(remainingNative);
        Check(second != null && second.expectedBottles == ChemistryOperations.Total(remainingNative) / 2000,
            "second reagent batch differs from native sedimentation"); Replay(rest, second);

        foreach (string medicine in ChemicalRecipe.Ids.Select(x => ChemicalRecipe.Get(x).Active).Concat(new[] { "dream_cap_mush", "coffee_mush", "methylol" }).Distinct())
            Check(!DirectFormInputs.CanUseLiquid(Mix("acid", 10000, medicine, 1)), "medicinal feed consumed as reagent: " + medicine);
        var pitcher = Mix("acid", 10000); pitcher.identifier = "water_pitcher";
        Check(!DirectFormInputs.CanUseLiquid(pitcher), "pitcher exclusion removed");
        var tank = Input("dream_dust"); tank.chamber = Mix("starch", 8000, "alcohol", 10000);
        var tankPlan = Calculate(tank).Result;
        Check(DirectFormInputs.ReagentsInPlan(tank, tankPlan).SequenceEqual(new[] { "alcohol" }), "tank solvent omitted from warning");
        tank.mashBuffer = tank.chamber; tank.chamber = Mix(); tankPlan = Calculate(tank).Result;
        Check(DirectFormInputs.ReagentsInPlan(tank, tankPlan).SequenceEqual(new[] { "alcohol" }), "buffer solvent omitted from warning");
        tankPlan.directForm = false;
        Check(DirectFormInputs.ReagentsInPlan(tank, tankPlan).Length == 0, "ordinary medicine receives fake-feed warning");
        var confirmation = new ResidueStartConfirmation();
        Check(!confirmation.Consume("batch", tank.chamber), "start without consent");
        confirmation.Request("batch", tank.chamber);
        Check(!confirmation.Consume("changed", tank.chamber), "changed batch retained consent");
        confirmation.Request("batch", tank.chamber); confirmation.Clear();
        Check(!confirmation.Pending, "cancel retained consent");
        confirmation.Request("batch", tank.chamber);
        Check(confirmation.Consume("batch", tank.chamber) && !confirmation.Consume("batch", tank.chamber), "consent not single-use");
        return "Direct solvents: " + checks + " checks passed (native yield, reactions, warnings, metered feed, normal-mode protection and save/resume)";
    }
}
