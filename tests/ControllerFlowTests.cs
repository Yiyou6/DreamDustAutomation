using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DreamDustAutomation.Planning;

// Actual controller dispatch/coroutines, native numerical chemistry/persistence.
// Replace scene/UI effects only. This does not simulate Unity pointer hit testing.
internal sealed partial class ControllerFlowTests
{
    private static int checks, scenarios;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Controller flow: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem(); item.EnableTag("WINE_CONTAINER_TAG");
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private static GameItem Filter()
    {
        var item = Mix(); item.identifier = "water_filter_adv"; item.EnableTag("WATER_FILTER");
        item.state.GetTag("FILTER_SIZE_TAG").Enable().SetInt(22);
        foreach (string tag in new[] { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" })
            item.state.GetTag(tag).Enable().SetInt(800);
        return item;
    }
    private sealed class Button { internal void SetDisabled(bool value) { } }
    private sealed class Tabs { internal int selected; }
    private static class ModPerformance { internal static long Begin() => 0; internal static void Record(int n, long t) { } }
    private static class MachineHelper { internal static bool CanPower(GameItem item) => item.IsTag("TEST_POWER"); }
    private sealed class OutputPlacement { internal static OutputPlacement Capture(ControllerFlowTests c) => new OutputPlacement(); }
    private sealed class OutputSpace
    {
        private ControllerFlowTests c;
        internal static OutputSpace Capture(ControllerFlowTests c) => new OutputSpace { c = c };
        internal bool Fits(int w, int h) => !c.outputFull;
    }
    private static class EmporiumEntry
    {
        internal static ControllerFlowTests Instance;
    }
    private readonly Queue<IEnumerator> workers = new Queue<IEnumerator>();
    private readonly List<GameItem> made = new List<GameItem>();
    private readonly GameItem[] cores = { Mix(), Mix(), Mix(), Mix() };
    private readonly TankCleanup tankCleanup = new TankCleanup();
    private readonly ResidueStartConfirmation residueConfirmation = new ResidueStartConfirmation();
    private readonly BatchInputReservation reservedInputs = new BatchInputReservation();
    private readonly Button previewButton = new Button();
    private readonly Tabs controlTabs = new Tabs();
    private RunningBatch running, formingBatch;
    private BatchPlan preview, maximumPreview;
    private BatchInputs previewInputs;
    private GameItem tank, mashBuffer, finisherTank, machine = Mix();
    private GameItem[] stock, rawStock;
    private readonly HashSet<int> consumedRaw = new HashSet<int>();
    private ControllerFlowTests output;
    private bool restored = true, inputGuidanceDirty, labelsDirty, searching, quantitySearching, internalMove, outputFull, WindowVisible = true;
    private int preparedHeatStep = -1, selectedQuantity, releaseStep = -1, searchGeneration;
    private GridShape blockedOutputShape;
    private Action<GameItem, GameInventory, SlotMarker> evaporate;
    private bool Busy => searching || running != null;
    private string status;
    private void SetStatus(string key, params object[] args) { status = key; }
    private void RefreshInputGuidance() { inputGuidanceDirty = false; }
    private void ScheduleWasteCollection() { }
    private void RefreshLabelPreview() { }
    private bool BindHardware() => true;
    private void RefreshResidueWarning() { }
    private void RefreshAlcoholControls() { }
    private void RefreshTurbo() { }
    private void RefreshProgress() { }
    private void ClearResidueConfirmation() { residueConfirmation.Clear(); }
    private void ClearPreviewText() { tankCleanup.Cancel(); }
    private void ResetGradeResults() { }
    private void MigrateLegacyInputs() { }
    private void CancelTankCleanup() { tankCleanup.Cancel(); }
    private void Restore() { Reload(); }
    private void Guard(Action action) { try { action(); } catch (Exception error) { Fail(error); } }
    private void Fail(Exception error)
    {
        if (running != null) { running.failed = true; running.failureKey = error is MachineFault f ? f.Key : error.GetType().Name; }
        status = error.ToString();
    }
    private void Save()
    {
        if (running != null)
            foreach (var action in running.plan.actions.Take(running.step))
                if (action.operation == Operation.Blend || action.operation == Operation.Dissolve) consumedRaw.Add(action.source);
        BatchPersistence.Save(machine, running);
    }
    private const string SourceTag = BatchInputReservation.SourceTag;
    private bool reverseSources;
    private IEnumerable<GameItem> ExecutionSources()
    {
        var sources = rawStock.Where((x, i) => !consumedRaw.Contains(i)).Concat(stock);
        return reverseSources ? sources.Reverse() : sources;
    }
    // PRODUCTION_Source
    // PRODUCTION_IsSolid
    // PRODUCTION_SetSource
    private void Reserve()
    {
        foreach (var item in rawStock.Concat(stock)) item.DisableTag(SourceTag);
        var rawIds = new HashSet<int>(running.plan.actions.Where(a => a.operation == Operation.Blend || a.operation == Operation.Dissolve).Select(a => a.source));
        var supplyIds = new HashSet<int>(running.plan.actions.Where(a => a.operation == Operation.Pour || a.operation == Operation.ReturnFeed).Select(a => a.source));
        supplyIds.UnionWith(running.plan.actions.Where(a => a.operation == Operation.Filter).SelectMany(a => a.FilterSources));
        for (int i = 0; i < rawStock.Length; i++)
            if ((!running.plan.directForm && running.plan.requestedBottles == 0) || rawIds.Contains(i)) SetSource(rawStock[i], i);
        for (int i = 0; i < stock.Length; i++)
            if ((!running.plan.directForm && running.plan.requestedBottles == 0) || supplyIds.Contains(i)) SetSource(stock[i], i);
        reservedInputs.Restore(ExecutionSources(), running.plan.separateDirect);
    }
    private void SwapProcessingTanks() { var old = tank; tank = mashBuffer; mashBuffer = old; }
    internal void StartCoroutine(IEnumerator work) { if (work.MoveNext()) workers.Enqueue(work); }
    private void Frame()
    {
        EmporiumEntry.Instance = this;
        int count = workers.Count;
        for (int n = 0; n < count; n++)
        {
            var work = workers.Dequeue();
            if (work.MoveNext()) workers.Enqueue(work); else (work as IDisposable)?.Dispose();
        }
    }
    private void Reload()
    {
        foreach (var work in workers) (work as IDisposable)?.Dispose();
        workers.Clear(); Save(); reservedInputs.Clear();
        running = BatchPersistence.Load(machine, out string error);
        Check(error == null, "saved execution cannot resume: " + error);
        tank = new SimulationItem(tank); mashBuffer = new SimulationItem(mashBuffer); finisherTank = new SimulationItem(finisherTank);
        stock = stock.Select(x => (GameItem)new SimulationItem(x)).ToArray();
        rawStock = rawStock.Select(x => (GameItem)new SimulationItem(x)).ToArray();
        if (running != null) reservedInputs.Restore(ExecutionSources(), running.plan.separateDirect);
        formingBatch = null; preparedHeatStep = -1;
    }
    private bool FormOne(OutputPlacement placements)
    {
        if (outputFull) { SetStatus("output_full"); return false; }
        var recipe = ChemicalRecipe.Get(running.plan.productId);
        var source = running.useFinisherTank ? finisherTank : tank;
        Check(!WaterHelper.IsWet(source), "formed wet stock");
        Check(ChemistryOperations.Total(source) >= recipe.UnitVolume, "output promised more stock than physically present");
        var product = Mix();
        ChemistryOperations.TransferToProduct(source, product, recipe.UnitVolume);
        MachineChemFinisher.IntroduceImpurity(product, running.finisherLoss);
        Check((long)ChemistryOperations.Amount(product, recipe.Active) * 100 >= (long)running.plan.actualGrade * recipe.UnitVolume, "output grade below preview");
        made.Add(product); running.produced++; return true;
    }
    private ControllerFlowTests(BatchInputs inputs, BatchPlan plan, bool defer)
    {
        tank = new SimulationItem(inputs.chamber); mashBuffer = new SimulationItem(inputs.mashBuffer); finisherTank = Mix();
        stock = inputs.supplies.Select(x => (GameItem)new SimulationItem(x)).ToArray(); output = this;
        rawStock = inputs.raw.Select(x => (GameItem)new SimulationItem(x)).ToArray();
        running = new RunningBatch { schema = 2, useFinisherTank = true, plan = plan.Copy(), deferOutput = defer,
            heaterUpgrades = inputs.heaterUpgrades, blenderLoss = inputs.blenderLoss, finisherLoss = inputs.finisherLoss };
        Reserve();
        cores[2].EnableTag("TEST_POWER");
        evaporate = (core, inv, slot) =>
        {
            if (!MachineHelper.CanPower(core)) return;
            int total = ChemistryOperations.VolatileVolume(tank), budget = Math.Min(total, 300000), used = 0;
            var parts = Liquid.Liquids.Where(l => l.isEvaporative && ChemistryOperations.Amount(tank, l.identifier) > 0).ToArray();
            for (int i = 0; i < parts.Length; i++)
            {
                int amount = ChemistryOperations.Amount(tank, parts[i].identifier);
                int take = i == parts.Length - 1 ? budget - used : (int)((long)budget * amount / total);
                take = Math.Max(0, Math.Min(take, amount)); used += take;
                ChemistryOperations.SetAmount(tank, parts[i].identifier, amount - take);
            }
        };
        EmporiumEntry.Instance = this; Save();
    }

    // PRODUCTION_AdvanceImmediate
    // PRODUCTION_AdvanceOutput
    // PRODUCTION_FormProducts
    // PRODUCTION_Tick
    // PRODUCTION_Cycle
    // PRODUCTION_CompleteNightReactions
    // PRODUCTION_Stop

    private static BatchInputs Inputs(string id, bool direct, DirectDryingMethod method, int percent)
    {
        var recipe = ChemicalRecipe.Get(id);
        var feed = direct ? Mix("water", 12000, "starch", 8101) : id == "fanta_pill" ? Mix("methylol", 40000, "stabilizing_agent", 40000) :
            recipe.Mush != null ? Mix(recipe.Mush, 40000, "water", 1000, "protein", 2000) : Mix(recipe.Active, recipe.UnitVolume * 9 + 137, "water", 10000);
        var supplies = direct ? new GameItem[] { feed, Mix("acid", 8001), Mix("base", 8003) } :
            new GameItem[] { feed, Mix("alcohol", 1000000), Mix("acid", 1000000), Mix("base", 1000000),
                Mix("reducing_agent", 1000000), Mix("lipobond_agent", 1000000), Filter(), Filter() };
        return new BatchInputs { productId = id, directForm = direct, separateDirect = direct, directSolvents = direct,
            directDrying = method, normalDrying = method, alcoholPercent = percent, heaterUpgrades = 4,
            requestedGrade = 99, finisherLoss = 0, raw = new GameItem[0], supplies = supplies, chamber = Mix(), mashBuffer = Mix(), maxProducts = 2560 };
    }
    private void RunSequence(int sequence)
    {
        int expected = running.plan.expectedBottles, iterations = 0;
        bool loaded = false, blocked = false, stopped = false;
        outputFull = sequence == 2 || sequence == 3;
        EmporiumEntry.Instance = this; Guard(AdvanceImmediate);
        while (running != null)
        {
            Check(++iterations < 10000, "stalled at " + status);
            Check(!running.failed, "failed at " + status);
            if (sequence == 1 && !loaded && (running.step > 0 || DeferredOutput.AtForm(running))) { Reload(); loaded = true; }
            if (DeferredOutput.Waiting(running))
            {
                Check(DeferredOutput.Release(running, Math.Min(3, DeferredOutput.Remaining(running))), "partial release rejected");
            }
            if (sequence == 2 && !blocked && DeferredOutput.AtForm(running))
            {
                outputFull = true; Guard(AdvanceImmediate); Frame();
                int count = made.Count;
                for (int n = 0; n < 3; n++) { Guard(Tick); Frame(); }
                Check(made.Count == count && running != null && !running.failed, "full output changed progress: " + status);
                outputFull = false; blocked = true;
            }
            if (sequence == 3 && !stopped && DeferredOutput.AtForm(running))
            {
                outputFull = false; Guard(AdvanceImmediate); Frame(); int count = made.Count;
                Guard(Stop); Frame();
                Check(running == null && made.Count == count && !Busy, "old output worker resumed after stop");
                var vessels = new[] { tank, mashBuffer, finisherTank };
                if (vessels.Any(t => ChemistryOperations.Total(t) > 0))
                {
                    Check(tankCleanup.RequestMany(vessels, false), "cleanup after stop refused");
                    WindowVisible = true; Guard(Tick);
                    Check(tankCleanup.ConfirmMany(vessels, false), "cleanup after output stop failed");
                }
                Check(vessels.All(t => ChemistryOperations.Total(t) == 0), "stopped vessels retained residue");
                stopped = true; break;
            }
            if (running != null && running.step < running.plan.actions.Length && running.plan.actions[running.step].operation == Operation.Dry)
            {
                cores[2].DisableTag("TEST_POWER");
                var parts = ChemistryOperations.Parts(tank); Guard(Cycle);
                Check(parts.SequenceEqual(ChemistryOperations.Parts(tank)), "unpowered evaporator consumed material");
                cores[2].EnableTag("TEST_POWER");
                CurrentConverterHelper.isNightCycle = true;
                try { Guard(Cycle); } finally { CurrentConverterHelper.isNightCycle = false; }
            }
            else if (sequence == 1 && running != null && running.step < running.plan.actions.Length && ReactionProgress.IsTimed(running.plan.actions[running.step].operation))
            {
                CurrentConverterHelper.isNightCycle = true;
                try { Guard(Cycle); } finally { CurrentConverterHelper.isNightCycle = false; }
            }
            else Guard(Tick);
            Frame();
        }
        Check(stopped || made.Count == expected, "output count differs: " + made.Count + "/" + expected);
        Check(!internalMove && !Busy, "completed batch retains a processing lock"); scenarios++;
    }
    internal static string Run()
    {
        checks = scenarios = 0; int unavailable = 0;
        foreach (string id in ChemicalRecipe.Ids)
        foreach (bool direct in new[] { false, true })
        foreach (var method in new[] { DirectDryingMethod.Evaporate, DirectDryingMethod.Heat })
        foreach (int percent in direct ? new[] { 100 } : new[] { 100, 1 })
        {
            var inputs = Inputs(id, direct, method, percent);
            var planner = new BatchPlanner(inputs); var work = planner.Search(); int yields = 0;
            while (work.MoveNext()) Check(++yields < 1000000, "planner stalled");
            var plan = planner.Result;
            if (plan == null) { Check(method == DirectDryingMethod.Heat && planner.Error != null, "unexpected unavailable recipe: " + id + " " + planner.Error); unavailable++; continue; }
            foreach (bool defer in new[] { false, true })
            for (int sequence = 0; sequence < 4; sequence++)
            {
                try { new ControllerFlowTests(inputs, plan, defer).RunSequence(sequence); }
                catch (Exception error) { throw new Exception(id + " direct=" + direct + " drying=" + method + " alcohol=" + percent + " defer=" + defer + " sequence=" + sequence, error); }
            }
        }
        return checks + " controller-flow checks passed across " + scenarios + " sequences; " + unavailable + " unsafe heat routes rejected; seven products, both modes, 100/1% extraction, drying choices, defer/partial output, reload, night, no power, full output, stop and cleanup.";
    }
}
