using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class SaveMutationTests
{
    private static int cases, accepted, rejected, checks;
    private static readonly List<string> faults = new List<string>();
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static RunningBatch Fixture(string id, bool direct) => new RunningBatch {
        schema = 2, useFinisherTank = true, heaterUpgrades = 4,
        plan = new BatchPlan { schema = direct ? 3 : 2, directForm = direct, separateDirect = direct,
            productId = id, requestedGrade = direct ? 0 : 99, actualGrade = direct ? 0 : 99,
            expectedBottles = 5, outputCapacity = 2560,
            actions = new[] { new BatchAction(Operation.Pour, 0, 20000), new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 5) } }
    };
    private static void Probe(string name, string json, bool mustReject = false)
    {
        cases++;
        try {
            var shell = new SimulationItem();
            ChemistryOperations.SetAmount(shell, "starch", 12345);
            shell.ModifyTag("TEST_UNRELATED", t => t.Enable().SetString("keep"));
            shell.ModifyTag(BatchPersistence.SaveTag, t => t.Enable().SetString(json));
            var before = ChemistryOperations.Parts(shell);
            var batch = BatchPersistence.Load(shell, out string error);
            Check(before.SequenceEqual(ChemistryOperations.Parts(shell)), "load changed physical materials");
            Check(shell.GetTagReadonly("TEST_UNRELATED").GetString() == "keep", "load changed unrelated state");
            if (batch == null && error != null) {
                rejected++;
                Check(shell.GetTagReadonly(BatchPersistence.RecoveryTag).GetString() == json, "lost original broken metadata");
                Check(!string.IsNullOrEmpty(shell.GetTagReadonly(BatchPersistence.RecoveryReasonTag).GetString()), "missing recovery reason");
                Check(BatchPersistence.Load(shell, out string again) == null && again == null, "bad record retriggered on second load");
                BatchPersistence.Save(shell, Fixture("dream_dust", false));
                Check(BatchPersistence.Load(shell, out again) != null && again == null, "machine cannot save a replacement batch");
            } else {
                accepted++;
                Check(!mustReject, "invalid record was accepted");
                if (batch != null) {
                    string stable = BatchPersistence.Serialize(batch);
                    Check(BatchPersistence.Serialize(BatchPersistence.Deserialize(stable)) == stable, "accepted state not stable on reload");
                }
            }
        } catch (Exception e) { if (faults.Count < 30) faults.Add(name + ": " + e.GetType().Name + " / " + e.Message); }
    }
    private static IEnumerable<JToken> Values()
    {
        yield return JValue.CreateNull(); yield return new JObject(); yield return new JArray(1, 2);
        yield return new JValue("bad-value"); yield return new JValue(""); yield return new JValue(true);
        yield return new JValue(-1); yield return new JValue(0); yield return new JValue(1);
        yield return new JValue(int.MaxValue); yield return new JValue((long)int.MaxValue + 1);
        yield return new JValue(long.MaxValue); yield return new JValue(0.5);
    }
    internal static string Run()
    {
        cases = accepted = rejected = checks = 0; faults.Clear();
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true }) {
            var original = JObject.Parse(BatchPersistence.Serialize(Fixture(id, direct)));
            Probe(id + "/valid/" + direct, original.ToString());
            var paths = original.Descendants().OfType<JProperty>().Select(p => p.Value.Path).ToArray();
            foreach (string path in paths) {
                var missing = (JObject)original.DeepClone(); missing.SelectToken(path).Parent.Remove();
                Probe(id + "/missing/" + path, missing.ToString());
                foreach (var value in Values()) {
                    var changed = (JObject)original.DeepClone(); changed.SelectToken(path).Replace(value.DeepClone());
                    Probe(id + "/" + path + "=" + value.ToString(Formatting.None), changed.ToString());
                }
            }
            // Cross-field progress, malicious metadata, truncation and duplicate objects.
            for (int step = -1; step <= 4; step++)
            foreach (int made in new[] { -1, 0, 1, 5, 6 })
            foreach (bool defer in new[] { false, true }) {
                var changed = (JObject)original.DeepClone(); changed["step"] = step;
                changed["produced"] = made; changed["deferOutput"] = defer;
                Probe(id + "/progress/" + step + "/" + made + "/" + defer, changed.ToString());
            }
            Probe(id + "/trailing", original.ToString() + " {}", true);
            Probe(id + "/truncated", original.ToString().Substring(0, 50), true);
            var deep = (JObject)original.DeepClone(); JToken nested = new JValue(1);
            for (int d = 0; d < 40; d++) nested = new JArray(nested);
            deep["unknown"] = nested; Probe(id + "/depth", deep.ToString(), true);
        }
        foreach (string invalid in new[] { "null", "[]", "{", "{}", "1", "true" }) Probe("root/" + invalid, invalid, true);
        if (faults.Count > 0) throw new Exception(cases + " save mutations; failures:\n" + string.Join("\n", faults));
        return cases + " save mutations passed; " + accepted + " compatible/valid states, " + rejected + " recoverable rejections; " + checks + " checks. Original broken metadata preserved, physical contents unchanged, replacement batch saves normally.";
    }
}
