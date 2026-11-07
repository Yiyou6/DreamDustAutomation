using System;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;
using Newtonsoft.Json.Linq;

public static class BatchPersistenceTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Save/resume: " + message);
        checks++;
    }

    private static RunningBatch Fixture() => new RunningBatch
    {
        step = 1, elapsed = 3, produced = 0, blenderLoss = 10, heaterUpgrades = 3, finisherLoss = 1,
        usesRecoveredSolvent = true,
        plan = new BatchPlan
        {
            productId = "dream_dust", requestedGrade = 99, actualGrade = 90, expectedBottles = 8,
            minimumActive = 1850, reactionSeconds = 20, reagents = 45000, recoveredVolume = 30000,
            concentrationHint = "purity_add_filter",
            actions = new[] { new BatchAction(Operation.Pour, 5, 30000),
                new BatchAction(Operation.Extract, amount: 20, targetTemperature: 45) { precursorId = "dream_cap_mush", endAmount = 2 },
                new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 8) }
        }
    };

    private static RunningBatch Reload(RunningBatch batch)
    {
        var shell = new SimulationItem();
        BatchPersistence.Save(shell, batch);
        string json = shell.GetTagReadonly(BatchPersistence.SaveTag).GetString();
        var data = JObject.Parse(json);
        Check(data["plan"]?["actions"] is JArray, "nested plan/actions were omitted");
        // Exercise the escaped string stored inside the game's outer save JSON.
        var envelope = new JObject { ["internalValueString"] = json }.ToString();
        var loadedShell = new SimulationItem(shell);
        loadedShell.ModifyTag(BatchPersistence.SaveTag, tag => tag.SetString((string)JObject.Parse(envelope)["internalValueString"]));
        var loaded = BatchPersistence.Load(loadedShell, out var error);
        Check(error == null && loaded != null, "valid batch did not restore");
        Check(BatchPersistence.Serialize(loaded) == json, "plan, progress, grades, upgrades or recovery flags changed");
        Check(ReactionProgress.RemainingSeconds(loaded.plan, loaded.step, loaded.elapsed) ==
            ReactionProgress.RemainingSeconds(batch.plan, batch.step, batch.elapsed), "countdown reset on load");
        return loaded;
    }

    public static int Run()
    {
        checks = 0;
        Reload(Fixture());
        var failed = Fixture(); failed.failed = true;
        Check(Reload(failed).failed, "a real processing failure must not silently resume");
        failed.failureKey = "runtime_source_missing";
        Check(Reload(failed).failureKey == "runtime_source_missing", "specific stop reason lost on reload");
        var complete = Fixture(); complete.step = complete.plan.actions.Length; complete.elapsed = 0; complete.produced = 8;
        Reload(complete);
        var old = JObject.Parse(BatchPersistence.Serialize(Fixture()));
        old["plan"]["schema"] = 1;
        ((JObject)old["plan"]).Remove("productId");
        ((JObject)old["plan"]).Remove("recoveredVolume");
        ((JObject)old["plan"]).Remove("requestedBottles");
        ((JObject)old["plan"]).Remove("availableBottles");
        ((JObject)old["plan"]).Remove("directSolvents");
        old.Remove("usesRecoveredSolvent");
        old.Remove("failureKey");
        var legacy = BatchPersistence.Deserialize(old.ToString());
        Check(ChemicalRecipe.Get(legacy.plan.productId).Id == "dream_dust" && !legacy.usesRecoveredSolvent,
            "complete legacy plans must retain their default product and recovery semantics");
        Check(legacy.plan.requestedBottles == 0, "old batches must keep all-output behavior");
        Check(!legacy.plan.directSolvents, "legacy saves must not opt into reagent feed");
        Check(legacy.failureKey == null, "legacy stop reason fabricated");
        var invalid = new List<string>
        {
            // Exact state from the user's save: Unity serialized counters only.
            "{\"schema\":1,\"step\":61,\"elapsed\":0,\"produced\":0,\"blenderLoss\":0,\"heaterUpgrades\":4,\"finisherLoss\":0,\"failed\":false,\"usesRecoveredSolvent\":true}",
            "{", "null", "{}"
        };
        var direct = Fixture();
        direct.plan.schema = 3; direct.plan.directForm = true;
        direct.plan.actualGrade = direct.plan.requestedGrade = 0;
        direct.step = 0; direct.elapsed = 1;
        direct.plan.actions = new[] { new BatchAction(Operation.React, amount: 2), new BatchAction(Operation.Dry), new BatchAction(Operation.Form, amount: 8) };
        Check(Reload(direct).plan.directForm, "Direct mode was lost on reload");
        foreach (var change in new Action<JObject>[] {
            j => ((JObject)j["plan"]).Remove("directForm"), j => j["plan"]["directForm"] = false,
            j => j["plan"]["schema"] = 2, j => j["plan"]["actualGrade"] = 30,
            j => j["plan"]["actions"][0]["operation"] = (int)Operation.Extract })
        {
            var data = JObject.Parse(BatchPersistence.Serialize(direct)); change(data); invalid.Add(data.ToString());
        }
        foreach (var change in new Action<JObject>[] {
            j => j.Remove("step"), j => j["schema"] = 42, j => j["step"] = -1, j => j["step"] = 500,
            j => j["elapsed"] = 500, j => j["produced"] = 9, j => j["plan"]["actions"] = null,
            j => j["plan"]["requestedBottles"] = -1, j => j["plan"]["requestedBottles"] = 3,
            j => { j["plan"]["requestedBottles"] = 8; j["plan"]["availableBottles"] = 7; },
            j => j["plan"]["actions"][0]["operation"] = 99, j => j["plan"]["productId"] = "unknown_product",
            j => ((JObject)j["plan"]["actions"][0]).Remove("amount") })
        {
            var data = JObject.Parse(BatchPersistence.Serialize(Fixture())); change(data); invalid.Add(data.ToString());
        }
        foreach (string broken in invalid)
        {
            var shell = new SimulationItem();
            ChemistryOperations.SetAmount(shell, "dream_cap_extract", 12345);
            shell.ModifyTag(BatchPersistence.SaveTag, tag => tag.Enable().SetString(broken));
            Check(BatchPersistence.Load(shell, out var error) == null && error != null, "invalid batch accepted");
            Check(shell.IsTag(BatchPersistence.RecoveryTag) && shell.GetTagReadonly(BatchPersistence.RecoveryTag).GetString() == broken,
                "incomplete metadata must be archived unchanged");
            Check(!string.IsNullOrEmpty(shell.GetTagReadonly(BatchPersistence.RecoveryReasonTag).GetString()), "recovery reason missing");
            Check(ChemistryOperations.Amount(shell, "dream_cap_extract") == 12345, "recovery consumed remaining material");
            Check(BatchPersistence.Load(shell, out error) == null && error == null, "same broken batch retried after recovery");
            var loadedShell = new SimulationItem(shell);
            Check(loadedShell.IsTag(BatchPersistence.RecoveryTag), "recalculation notice must survive another save/load");
            BatchPersistence.Save(loadedShell, Fixture());
            Check(!loadedShell.IsTag(BatchPersistence.RecoveryTag), "starting a replacement batch must clear the old warning");
            Check(!loadedShell.IsTag(BatchPersistence.RecoveryReasonTag), "replacement batch retained old recovery reason");
        }
        foreach (string reason in new[] { "restore_incomplete", "restore_version", "restore_product", "restore_operation", "restore_progress", "restore_invalid" })
        {
            var json = JObject.Parse(BatchPersistence.Serialize(Fixture()));
            switch (reason)
            {
                case "restore_incomplete": json.Remove("plan"); break;
                case "restore_version": json["schema"] = 42; break;
                case "restore_product": json["plan"]["productId"] = "unknown"; break;
                case "restore_operation": json["plan"]["actions"][0]["operation"] = 99; break;
                case "restore_progress": json["elapsed"] = 500; break;
            }
            var shell = new SimulationItem();
            shell.ModifyTag(BatchPersistence.SaveTag, tag => tag.Enable().SetString(reason == "restore_invalid" ? "{" : json.ToString()));
            BatchPersistence.Load(shell, out var failure);
            Check(failure != null && shell.GetTagReadonly(BatchPersistence.RecoveryReasonTag).GetString() == reason, "wrong recovery classification: " + reason);
        }
        var idle = new SimulationItem();
        BatchPersistence.Save(idle, null);
        Check(BatchPersistence.Load(idle, out var idleError) == null && idleError == null, "idle save must remain idle");
        return checks;
    }

    internal static int CheckRecipe(BatchInputs inputs, BatchPlan plan)
    {
        int before = checks;
        string uninterrupted = Replay(inputs, plan, false);
        string resumed = Replay(inputs, plan, true);
        Check(resumed == uninterrupted, "reloading changed material balance, filter wear or products for " + plan.productId);
        return checks - before;
    }

    internal static int CheckFinisherRoute(BatchInputs inputs, BatchPlan plan, bool defer)
    {
        int before = checks;
        // Ordinary medicine must retain every bottle's exact native composition.
        // Independent fake lots are intentionally pooled only when deferred;
        // their count and source/buffer tails must still match the original plan.
        string baseline = Replay(inputs, plan, false);
        string moved = Replay(inputs, plan, true, useFinisher: true, defer: defer);
        if (defer && plan.separateDirect)
            Check(baseline.Substring(baseline.IndexOf('|')) == moved.Substring(moved.IndexOf('|')), "pooling changed input/tank remainders");
        else Check(baseline == moved, "finisher transfer changed products, sources or residues");
        return checks - before;
    }

    internal static BatchInputs RemainingAfter(BatchInputs inputs, BatchPlan plan)
    {
        BatchInputs remaining = null;
        Replay(inputs, plan, false, result => remaining = result);
        return remaining;
    }

    private static string Replay(BatchInputs inputs, BatchPlan plan, bool reload, Action<BatchInputs> after = null, bool useFinisher = false, bool defer = false)
    {
        var tank = new SimulationItem(inputs.chamber);
        var buffer = inputs.mashBuffer == null ? null : new SimulationItem(inputs.mashBuffer);
        var stock = inputs.supplies.Select(item => new SimulationItem(item)).ToArray();
        var raw = inputs.raw.Select(item => new SimulationItem(item)).ToArray();
        var products = new List<string>();
        var finishing = new SimulationItem();
        var recipe = ChemicalRecipe.Get(plan.productId);
        var batch = new RunningBatch { schema = useFinisher ? 2 : 1, useFinisherTank = useFinisher, deferOutput = defer,
            plan = plan.Copy(), blenderLoss = inputs.blenderLoss, heaterUpgrades = inputs.heaterUpgrades,
            finisherLoss = inputs.finisherLoss, usesRecoveredSolvent = plan.recoveredVolume > 0 };
        void Dispense(int target)
        {
            var vessel = useFinisher ? finishing : tank;
            while (batch.produced < target)
            {
                if (defer && DeferredOutput.Waiting(batch))
                {
                    string beforeHold = string.Join(",", ChemistryOperations.Parts(vessel));
                    for (int wait = 0; wait < 3; wait++)
                    {
                        batch = Reload(batch);
                        Check(DeferredOutput.Target(batch) <= batch.produced, "waiting save gained automatic output permission");
                    }
                    Check(beforeHold == string.Join(",", ChemistryOperations.Parts(vessel)), "waiting consumed stock");
                    Check(DeferredOutput.Protected(batch), "pending stock did not protect batch");
                    int selected = Math.Min(3, DeferredOutput.Remaining(batch));
                    Check(DeferredOutput.Release(batch, selected), "could not release selected output");
                }
                if (reload) batch = Reload(batch);
                Check(batch.produced < DeferredOutput.Target(batch), "production exceeded selected output");
                var product = new SimulationItem();
                ChemistryOperations.TransferToProduct(vessel, product, recipe.UnitVolume);
                MachineChemFinisher.IntroduceImpurity(product, batch.finisherLoss);
                Check((long)ChemistryOperations.Amount(product, recipe.Active) * 100 >= (long)batch.plan.actualGrade * recipe.UnitVolume,
                    "resumed product fell below the accepted grade");
                products.Add(string.Join(",", ChemistryOperations.Parts(product)));
                batch.produced++;
            }
        }
        while (batch.step < batch.plan.actions.Length)
        {
            if (reload) batch = Reload(batch);
            var action = batch.plan.actions[batch.step];
            switch (action.operation)
            {
                case Operation.Blend: ChemistryOperations.Blend(raw[action.source], tank, batch.blenderLoss); break;
                case Operation.Dissolve: ChemistryOperations.Dissolve(raw[action.source], tank); break;
                case Operation.Pour: ChemistryOperations.Pour(stock[action.source], tank, action.amount); break;
                case Operation.ReturnFeed: ChemistryOperations.Pour(tank, stock[action.source], ChemistryOperations.Total(tank)); break;
                case Operation.SwapTanks: var exchange = tank; tank = buffer; buffer = exchange; break;
                case Operation.StoreMash: ChemistryOperations.Pour(tank, buffer, action.amount); break;
                case Operation.FeedMash: ChemistryOperations.Pour(buffer, tank, action.amount); break;
                case Operation.Filter: FiltrationPolicy.Apply(tank, action, i => stock[i]); break;
                case Operation.Extract:
                case Operation.Synthesize:
                case Operation.BoilDry:
                case Operation.React:
                    while (batch.elapsed < action.amount)
                    {
                        if (!batch.plan.separateDirect && buffer != null && ChemistryOperations.Total(buffer) > 0) ChemistryOperations.ReactionTick(buffer);
                        if (action.operation == Operation.React) ChemistryOperations.ReactionTick(tank);
                        else ChemistryOperations.SimulateHeatedSecond(tank, batch.heaterUpgrades, action.targetTemperature > 0 ? action.targetTemperature : 45);
                        batch.elapsed++;
                        if (reload) { batch = Reload(batch); if (buffer != null) buffer = new SimulationItem(buffer); }
                    }
                    break;
                case Operation.Dry:
                    if (action.evaporationVolume >= 0)
                        Check(action.evaporationVolume == ChemistryOperations.VolatileVolume(tank), "evaporation preview volume differs from replayed stage");
                    // A waiting evaporator must retain its position without
                    // replaying earlier pours on successive loads.
                    if (reload) { batch = Reload(batch); batch = Reload(batch); }
                    ChemistryOperations.SimulateDry(tank);
                    RecoveredSolvent.AfterEvaporation(stock, inputs.recoveredSource);
                    if (!batch.plan.separateDirect) tank.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).SetDouble(26);
                    break;
                case Operation.Form:
                    int target = batch.plan.FormTarget(batch.step);
                    if (useFinisher)
                    {
                        FinishingStorage.Stage(tank, finishing, target - batch.prepared, recipe.UnitVolume, plan.separateDirect);
                        batch.prepared = target; batch.outputCommitted = defer;
                        if (reload) { batch = Reload(batch); finishing = new SimulationItem(finishing); }
                    }
                    if (!(defer && plan.separateDirect)) Dispense(target);
                    else Check(batch.produced == 0 && products.Count == 0, "fake lots produced before all processing completed");
                    break;
                default: throw new Exception("Unexpected saved operation");
            }
            if (batch.plan.separateDirect && (action.operation == Operation.Form || action.operation == Operation.ReturnFeed || action.operation == Operation.StoreMash))
                ChemicalHelper.HandleEmptyContainerTemp(tank);
            batch.step++; batch.elapsed = 0;
        }
        if (defer && plan.separateDirect) Dispense(plan.expectedBottles);
        if (reload) batch = Reload(batch);
        Check(batch.produced == plan.expectedBottles && products.Count == plan.expectedBottles, "duplicate or missing finished products");
        var consumed = new HashSet<int>(plan.actions.Where(a => a.operation == Operation.Blend || a.operation == Operation.Dissolve).Select(a => a.source));
        after?.Invoke(new BatchInputs { productId = inputs.productId, directForm = inputs.directForm, separateDirect = inputs.separateDirect, directDrying = inputs.directDrying, directSolvents = inputs.directSolvents,
            raw = raw.Where((item, i) => !consumed.Contains(i)).Cast<GameItem>().ToArray(), supplies = stock,
            chamber = tank, mashBuffer = buffer, recoveredSource = inputs.recoveredSource,
            requestedGrade = inputs.requestedGrade, alcoholPercent = inputs.alcoholPercent,
            blenderLoss = inputs.blenderLoss, heaterUpgrades = inputs.heaterUpgrades,
            finisherLoss = inputs.finisherLoss, maxProducts = inputs.maxProducts });
        var residueParts = ChemistryOperations.Parts(tank);
        if (useFinisher)
        {
            var heldParts = ChemistryOperations.Parts(finishing);
            for (int i = 0; i < residueParts.Length; i++) residueParts[i] += heldParts[i];
        }
        return string.Join(";", products) + "|" + string.Join(",", residueParts) + "|" +
            (buffer == null ? "" : string.Join(",", ChemistryOperations.Parts(buffer))) + "|" +
            string.Join(";", stock.Select(item => string.Join(",", ChemistryOperations.Parts(item)) + ":" + DurabilityHelper.GetRealDurability(item)));
    }
}
