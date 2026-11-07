using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class MaterialCatalogTests
{
    internal static string Run() => ControllerFlowTests.MaterialCatalogAudit();
}

internal sealed partial class ControllerFlowTests
{
    internal static string MaterialCatalogAudit()
    {
        checks = scenarios = planned = rejected = quantityPlans = interrupted = followups = 0;
        failures.Clear(); var records = new List<string> { "product,component,source,mode,drying,result" };
        var liquids = Liquid.Liquids.Select(l => l.identifier).Distinct().ToArray(); int cases = 0;
        foreach (string id in ChemicalRecipe.Ids)
        foreach (string component in liquids)
        foreach (bool solid in new[] { false, true })
        foreach (int mode in new[] { 0, 1, 2 }) {
            // Normal; fake without solvents; fake with solvents. Alternate drying
            // in fake mode to cover both choices for every native component.
            cases++; string result = "";
            try {
                var recipe = ChemicalRecipe.Get(id); var input = Setup(id, mode > 0, 4, 99);
                input.maxProducts = 7; input.directSolvents = mode == 2;
                input.directDrying = solid ? DirectDryingMethod.Heat : DirectDryingMethod.Evaporate;
                if (solid) input.raw = new[] { Solid(component, recipe.UnitVolume * 9 + 137, true) };
                else input.supplies = new[] { Mix(component, recipe.UnitVolume * 9 + 137) };
                var plan = Calculate(new Case { key = id + "/" + component, input = input });
                result = plan == null ? "explained rejection" : "executed";
                if (plan != null) new ControllerFlowTests(input, plan, cases % 2 == 0).RunSequence(1);
            } catch (Exception error) {
                result = "FAILED"; failures.Add(id + "/" + component + "/solid" + solid + "/mode" + mode + ": " + error);
                Console.WriteLine(failures.Last());
            }
            records.Add(id + "," + component + "," + (solid ? "soluble solid" : "container") + "," + mode + "," + (solid && mode > 0 ? "heat" : "evaporate") + "," + result);
            if (cases % 100 == 0) Console.WriteLine("MATERIAL PROGRESS cases=" + cases + " sequences=" + scenarios + " failures=" + failures.Count);
            if (failures.Count >= 10) goto done;
        }
        done:
        File.WriteAllLines(Path.Combine("TestResults", "material-catalog-0.3.60.csv"), records);
        string report = liquids.Length + " native components; " + cases + " product/component/mode/source cases; " + planned + " plans; " + rejected + " explained rejections; " + scenarios + " executed sequences; " + checks + " checks. Synthetic single-component feeds exercise numerical rules, not eligibility of every item prefab.";
        Console.WriteLine(report);
        if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
        return report;
    }
}
