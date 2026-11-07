using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class PartialStockTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Partial stock: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i+1]);
        return item;
    }
    private static SimulationItem Filter(int durability)
    {
        var item = Mix(); item.identifier = "water_filter_adv"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(durability);
        return item;
    }
    private static BatchInputs Coffee(int count, int grade, int durability, int acid = 1000000, int agent = 150000)
    {
        var raw = Enumerable.Range(0,count).Select(_ => {
            var bean = Mix("water", 3200, "fat", 8000, "coffee_mush", 25840, "caffeine", 4400, "physical_contaminant", 8000, "protein", 8000);
            bean.DisableTag(WaterHelper.LIQUID_CONTAINER_TAG); bean.EnableTag(ChemicalHelper.BLENDABLE_TAG); return (GameItem)bean;
        }).ToArray();
        return new BatchInputs { raw = raw, supplies = new GameItem[] { Mix("alcohol", 3000000), Mix("acid", acid), Mix("base", acid),
            Mix("lipobond_agent", agent), Filter(durability) }, chamber = Mix(), mashBuffer = Mix(), productId = "caffeine_pill",
            requestedGrade = grade, heaterUpgrades = 4, finisherLoss = 0, blenderLoss = 0, alcoholPercent = 100,
            skipDryingComparison = true, maxProducts = 2560 };
    }
    private static BatchPlan Plan(BatchInputs input, bool allowFailure = false)
    {
        var planner = new BatchPlanner(input); var work = planner.Search(); int n=0;
        while(work.MoveNext()) if(++n>1000000) throw new Exception("Stalled");
        if(planner.Result==null && !allowFailure) throw new Exception(planner.Error);
        return planner.Result;
    }

    // Independent amount oracle: each candidate is forced to process ALL of
    // its already blended feed. This bypasses both old and new feed selection.
    private static BatchInputs ForceWhole(BatchInputs input)
    {
        foreach (var bean in input.raw) ChemistryOperations.Blend(new SimulationItem(bean), input.chamber, input.blenderLoss);
        input.raw = new GameItem[0]; input.feedPrepared = true;
        return input;
    }
    private static int PrefixOracle(int count, int grade, Func<int, BatchInputs> factory)
    {
        int best = 0;
        for (int n = 1; n <= count; n++)
        {
            var plan = Plan(ForceWhole(factory(n)), true);
            if (plan != null && plan.actualGrade == grade) best = Math.Max(best, plan.expectedBottles);
        }
        return best;
    }
    private static void Verify(BatchInputs input, BatchPlan plan)
    {
        Check(BatchPlanner.Revalidate(input, plan) != null, "native preview replay disagrees");
        checks += BatchPersistenceTests.CheckRecipe(input, plan);
        var after = BatchPersistenceTests.RemainingAfter(input, plan);
        Check(after.raw.Length == input.raw.Length - plan.actions.Count(a => a.operation == Operation.Blend || a.operation == Operation.Dissolve),
            "unselected solids were consumed");
    }
    internal static string Run()
    {
        checks = 0;
        var rows = new List<string>(); var watch=Stopwatch.StartNew();
        foreach(int durability in new[] { 100, 200, 400, 800 })
        foreach(int grade in new[] {60,80})
        {
            var input=Coffee(20,grade,durability); var timer = Stopwatch.StartNew(); var p=Plan(input); timer.Stop();
            int best = PrefixOracle(20, grade, n => Coffee(n,grade,durability));
            Check(p.actualGrade == grade && p.expectedBottles == best, "finite filters missed a qualified intermediate feed count");
            Check(p.quantityHint == (p.actions.Count(a => a.operation == Operation.Blend) < 20 ? "purity_filter_more" : null),
                "filter amount limit guidance: " + p.quantityHint);
            Verify(input, p);
            rows.Add("filter="+durability+" grade="+grade+" plan="+p.expectedBottles+" at "+p.actualGrade+" used="+p.actions.Count(a=>a.operation==Operation.Blend)+
                " forced-prefix-oracle="+best+" plan-ms="+timer.ElapsedMilliseconds);
        }
        foreach (bool limitedAgent in new[] { false, true })
        {
            Func<int,BatchInputs> factory = n => Coffee(n,99,4000,limitedAgent ? 1000000 : 20000,limitedAgent ? 4000 : 150000);
            var input = factory(20); var plan = Plan(input);
            int best = PrefixOracle(20,99,factory);
            rows.Add((limitedAgent ? "agent" : "acid/base")+" plan="+plan.expectedBottles+" at "+plan.actualGrade+" forced-prefix-oracle="+best);
            Check(best > 0 && plan.actualGrade == 99 && plan.expectedBottles == best, "finite reagents: " + rows.Last());
            Check(plan.quantityHint == (limitedAgent ? "purity_fat" : "purity_protein"), "reagent amount limit guidance: " + plan.quantityHint);
            Verify(input,plan);
        }
        // One large premixed bottle must also be metered, without creating or
        // losing material in the unused part of its source container.
        var liquid = Coffee(20,80,800);
        var mash = Mix();
        foreach (var bean in liquid.raw) ChemistryOperations.Blend(new SimulationItem(bean),mash,0);
        liquid.raw = new GameItem[0]; liquid.supplies = liquid.supplies.Concat(new[] { mash }).ToArray();
        var lp = Plan(liquid);
        int drawn = lp.actions.Where(a => a.operation == Operation.Pour && a.source == 5).Sum(a => a.amount);
        rows.Add("large-bottle plan="+lp.expectedBottles+" at "+lp.actualGrade+" drawn="+drawn+"/"+ChemistryOperations.Total(mash));
        Check(lp.actualGrade == 80 && lp.expectedBottles >= 101 && drawn > 0 && drawn < ChemistryOperations.Total(mash), "large bottle remained all-or-nothing");
        Verify(liquid,lp);
        var remainder = BatchPersistenceTests.RemainingAfter(liquid,lp);
        Check(ChemistryOperations.Total(remainder.supplies[5]) + drawn == ChemistryOperations.Total(mash), "unused liquid was lost");

        var plenty = Coffee(20,99,4000); var full = Plan(plenty);
        Check(full.actualGrade == 99 && full.actions.Count(a => a.operation == Operation.Blend) == 20, "adequate stock was underused");
        Check(full.quantityHint == null, "adequate stock incorrectly shows a shortage");
        Verify(plenty,full);
        return checks+" partial stock checks passed\n"+string.Join("\n",rows)+"\n"+watch.Elapsed.TotalSeconds.ToString("F2")+" s";
    }
}
