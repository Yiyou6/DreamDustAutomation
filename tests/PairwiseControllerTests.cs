using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DreamDustAutomation.Planning;

internal static class PairwiseControllerTests
{
    internal static string Run() => ControllerFlowTests.PairwiseAudit();
}

internal sealed partial class ControllerFlowTests
{
    // Every pair of these finite factor levels must occur. This is not a claim
    // to exhaust every higher-order combination or arbitrary modded item state.
    private static readonly string[] factorNames = { "product", "mode", "drying", "grade", "blender", "heater", "finisher", "alcohol", "feed", "reagents", "filters", "residue", "capacity", "defer" };
    private static readonly int[] levels = { 7, 2, 2, 6, 5, 5, 5, 6, 6, 3, 5, 4, 5, 2 };
    private static long PairKey(int i, int j, int a, int b) => (((long)i * 100 + j) * 100 + a) * 100 + b;
    private static IEnumerable<long> Pairs(int[] row)
    { for (int i = 0; i < row.Length; i++) for (int j = i + 1; j < row.Length; j++) yield return PairKey(i, j, row[i], row[j]); }
    private static List<int[]> CoverPairs(out int required)
    {
        var missing = new SortedSet<long>();
        for (int i = 0; i < levels.Length; i++) for (int j = i + 1; j < levels.Length; j++)
            for (int a = 0; a < levels[i]; a++) for (int b = 0; b < levels[j]; b++) missing.Add(PairKey(i, j, a, b));
        required = missing.Count; var rows = new List<int[]>(); var rng = new Random(601106);
        while (missing.Count > 0) {
            long first = missing.Min; int b = (int)(first % 100); first /= 100;
            int a = (int)(first % 100); first /= 100; int j = (int)(first % 100), i = (int)(first / 100);
            int[] best = null; int score = -1;
            for (int attempt = 0; attempt < 80; attempt++) {
                var row = levels.Select(n => rng.Next(n)).ToArray(); row[i] = a; row[j] = b;
                int n = Pairs(row).Count(p => missing.Contains(p));
                if (n > score) { score = n; best = row; }
            }
            rows.Add(best); foreach (long p in Pairs(best)) missing.Remove(p);
        }
        return rows;
    }
    private static BatchInputs PairInput(int[] r)
    {
        string id = ChemicalRecipe.Ids[r[0]]; bool direct = r[1] == 1; var recipe = ChemicalRecipe.Get(id);
        var input = Setup(id, direct, r[5], GradePolicy.Grades[r[3]]);
        input.blenderLoss = new[] { 35, 20, 10, 5, 0 }[r[4]];
        input.finisherLoss = new[] { 12, 6, 3, 1, 0 }[r[6]];
        input.normalDrying = input.directDrying = r[2] == 0 ? DirectDryingMethod.Evaporate : DirectDryingMethod.Heat;
        input.alcoholPercent = AlcoholBudget.Percentages[r[7]];
        input.maxProducts = new[] { 0, 1, 7, 128, 2560 }[r[12]];
        var supplies = new List<GameItem>(); string feed = recipe.Mush ?? recipe.Active;
        switch (r[8]) {
            case 0: input.raw = new[] { Solid(direct ? "starch" : feed, recipe.UnitVolume * 15 + 137) }; break;
            case 1: supplies.Add(Mix(direct ? "starch" : feed, recipe.UnitVolume * 15, "water", 17000, "protein", 1000)); break;
            case 2: supplies.Add(Mix(direct ? "starch" : recipe.Active, recipe.UnitVolume * 15 + 1)); break;
            case 3:
                input.raw = new[] { Solid(direct ? "starch" : feed, recipe.UnitVolume * 5) };
                supplies.Add(Mix(direct ? "physical_contaminant" : recipe.Active, recipe.UnitVolume * 7, "water", 15000)); break;
            case 4:
                supplies.Add(Mix("water", 30000, "starch", recipe.UnitVolume * 10));
                supplies.Add(Mix(ChemicalRecipe.Get(ChemicalRecipe.Ids[(r[0] + 1) % 7]).Active, 40000)); break;
            case 5: break; // Empty feed, possibly usable residue only.
        }
        foreach (string part in new[] { "alcohol", "acid", "base", "reducing_agent", "lipobond_agent", "stabilizing_agent" }) {
            int volume = r[9] == 0 ? 1000000 : r[9] == 1 ? 980000 : 1;
            supplies.Add(Mix(part, volume, "water", r[9] == 1 ? 20000 : 0));
        }
        if (r[10] > 0) supplies.Add(Filter("kotton_fabric", r[10] == 4 ? 0 : 800));
        if (r[10] > 1) supplies.Add(Filter("water_filter", r[10] == 4 ? 0 : r[10] == 3 ? 1 : 400));
        if (r[10] > 2) supplies.Add(Filter("water_filter_adv", r[10] == 4 ? 0 : 800));
        if (r[11] == 1) input.chamber = Mix(recipe.Active, recipe.UnitVolume * 3 + 1);
        if (r[11] == 2) input.chamber = Mix("acid", 1000, "base", 1000, "physical_contaminant", 137);
        if (r[11] == 3) input.mashBuffer = Mix(direct ? "starch" : feed, recipe.UnitVolume * 4 + 1);
        input.supplies = supplies.ToArray(); input.directSolvents = (r[9] + r[11]) % 2 == 0;
        return input;
    }
    private static readonly Dictionary<string, int> randomEvents = new Dictionary<string, int>();
    private static void Event(string key) { randomEvents[key] = randomEvents.TryGetValue(key, out int n) ? n + 1 : 1; }
    private void RandomSequence(int seed)
    {
        var rng = new Random(seed); int expected = running.plan.expectedBottles, rounds = 0;
        EmporiumEntry.Instance = this;
        while (running != null) {
            Check(++rounds < 24000, "random sequence stalled: " + status);
            AssertState(); int choice = rng.Next(12);
            if (choice == 0) {
                string before = MaterialState(); int produced = made.Count;
                Reload(); Check(before == MaterialState() && made.Count == produced, "random reload changed contents"); Event("reload");
            } else if (choice == 1) {
                reverseSources = !reverseSources; Event("source enumeration reordered");
            } else if (choice == 2 && rounds < 300) {
                var added = Mix("starch", 137); SetSource(added, 0);
                reservedInputs.AddedDuringBatch(added); stock = stock.Concat(new[] { added }).ToArray();
                Check(!added.IsTag(SourceTag) && !reservedInputs.Contains(added), "new stock stole reserved source"); Event("restock with stale source tag");
            } else if (choice == 3) {
                string before = BatchPersistence.Serialize(running);
                Check(!DeferredOutput.Release(running, 0) && !DeferredOutput.Release(running, -1) && !DeferredOutput.Release(running, int.MaxValue), "invalid release accepted");
                Check(BatchPersistence.Serialize(running) == before, "rejected release changed progress"); Event("invalid release");
            } else if (choice == 4) {
                string before = MaterialState();
                Check(!tankCleanup.RequestMany(new[] { tank, mashBuffer, finisherTank }, Busy), "busy cleanup accepted");
                Check(before == MaterialState(), "busy cleanup changed contents"); Event("busy cleanup refused");
            } else if (choice == 5) {
                DeferredOutput.Pause(running); Event("pause partial output");
            } else if (choice == 6) { WindowVisible = !WindowVisible; Event("window visibility changed"); }
            outputFull = rng.Next(4) == 0;
            if (rng.Next(4) == 0) cores[2].DisableTag("TEST_POWER"); else cores[2].EnableTag("TEST_POWER");
            if (DeferredOutput.Waiting(running) && rng.Next(3) != 0)
                Check(DeferredOutput.Release(running, rng.Next(1, DeferredOutput.Remaining(running) + 1)), "valid random release refused");
            int count = made.Count; bool full = outputFull;
            if (rng.Next(3) == 0) { Night(); Event("night cycle"); }
            else { Guard(Tick); Event("day tick"); }
            Frame(); AssertState();
            Check(!full || made.Count == count, "full bin emitted product");
        }
        Check(made.Count == expected && !Busy && formingBatch == null, "random completion count/lock mismatch");
        Check(ExecutionSources().All(i => !reservedInputs.Contains(i)), "completed batch retained reserved inputs"); scenarios++;
    }
    private void MissingSourceSequence(BatchInputs original)
    {
        var action = running.plan.actions.FirstOrDefault(a => a.operation == Operation.Pour || a.operation == Operation.Blend || a.operation == Operation.Dissolve);
        if (action == null) return;
        bool solid = action.operation != Operation.Pour; var source = Source(solid, action.source);
        source.DisableTag(SourceTag); // Simulate an old/externally modified save losing an ingredient identity.
        Guard(AdvanceImmediate);
        Check(running != null && running.failed && running.failureKey == "runtime_source_missing", "missing input did not yield a specific recoverable error: " + status);
        string before = MaterialState(); Guard(Stop); Frame();
        Check(!Busy && before == MaterialState(), "stop after missing input changed contents");
        ReplanAndReuse(original); Event("missing source -> stop -> cleanup -> replacement batch"); scenarios++;
    }
    internal static string PairwiseAudit()
    {
        checks = scenarios = planned = rejected = quantityPlans = interrupted = followups = 0;
        failures.Clear(); randomEvents.Clear(); var rows = CoverPairs(out int required);
        Check(rows.SelectMany(Pairs).Distinct().Count() == required, "pairwise factor coverage incomplete");
        var csv = new List<string> { "case," + string.Join(",", factorNames) + ",result" }; int index = 0;
        foreach (var row in rows) {
            index++; string result = "";
            try {
                var input = PairInput(row); var plan = Calculate(new Case { key = "pair-" + index, input = input });
                result = plan == null ? "explained rejection" : "executed";
                if (plan != null) {
                    new ControllerFlowTests(input, plan, row[13] == 1).RandomSequence(811000 + index);
                    new ControllerFlowTests(input, plan, row[13] == 0).RandomSequence(911000 + index);
                    new ControllerFlowTests(input, plan, true).MissingSourceSequence(input);
                }
            } catch (Exception error) { result = "FAILED"; failures.Add("pair-" + index + " [" + string.Join(",", row) + "]: " + error); Console.WriteLine(failures.Last()); }
            csv.Add(index + "," + string.Join(",", row) + "," + result);
            if (index % 20 == 0) Console.WriteLine("PAIR PROGRESS " + index + "/" + rows.Count + " failures=" + failures.Count);
            if (failures.Count >= 10) break;
        }
        File.WriteAllLines(Path.Combine("TestResults", "pairwise-cases-0.3.60.csv"), csv);
        string report = rows.Count + " covering configurations; " + required + "/" + required + " factor pairs across 14 dimensions; " + planned + " plans; " + rejected + " explained rejections; " + scenarios + " sequences; " + checks + " checks. Events: " + string.Join(", ", randomEvents.Select(p => p.Key + "=" + p.Value));
        Console.WriteLine(report);
        if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
        return report;
    }
}
