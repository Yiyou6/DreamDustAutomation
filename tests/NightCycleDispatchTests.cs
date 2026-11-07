using System;

// Runs the production Cycle/CompleteNightReactions methods with a small event
// harness. Native chemical arithmetic is covered separately by NightReactionsTests.
enum Operation { Extract, React, Dry, Form }
sealed class BatchAction { internal Operation operation; internal int amount; }
sealed class BatchPlan { internal BatchAction[] actions; }
sealed class RunningBatch { internal bool failed; internal int step; internal BatchPlan plan; }
sealed class GameItem { internal object parentInventory; }
static class CurrentConverterHelper { internal static bool isNightCycle; }
static class NightReactions
{
    internal static int seconds;
    internal static bool CanAdvance(RunningBatch batch) => batch != null && !batch.failed &&
        batch.step < batch.plan.actions.Length && (batch.plan.actions[batch.step].operation == Operation.Extract || batch.plan.actions[batch.step].operation == Operation.React);
    internal static void CompleteStep(RunningBatch batch, GameItem tank, GameItem buffer)
    { seconds += batch.plan.actions[batch.step].amount; batch.step++; }
}
sealed class DispatchHarness
{
    internal RunningBatch running;
    internal bool restored, hardware = true, powered = true, failOnAdvance;
    internal int evaporations, saves, restores, refreshes, water = 2;
    private readonly GameItem tank = new GameItem(), mashBuffer = new GameItem();
    private readonly GameItem[] cores = { new GameItem(), new GameItem(), new GameItem() };
    private Action<GameItem, object, object> evaporate;
    internal DispatchHarness(params Operation[] operations)
    {
        var actions = new BatchAction[operations.Length];
        for (int i = 0; i < actions.Length; i++) actions[i] = new BatchAction { operation = operations[i], amount = 700 };
        running = new RunningBatch { plan = new BatchPlan { actions = actions } };
        evaporate = (item, inventory, slot) => { if (powered) { evaporations++; water--; } };
    }
    private void Restore() { restored = true; restores++; }
    private bool BindHardware() => hardware;
    private void Save() { saves++; }
    private void RefreshTurbo() { refreshes++; }
    private void AdvanceImmediate()
    {
        if (running == null || running.failed) return;
        if (failOnAdvance) { running.failed = true; return; }
        if (running.step < running.plan.actions.Length && running.plan.actions[running.step].operation == Operation.Dry && water <= 0)
        { running.step++; water = 1; }
        if (running.step >= running.plan.actions.Length) running = null;
    }
    internal void Run(bool night) { CurrentConverterHelper.isNightCycle = night; Cycle(); }
    // PRODUCTION_CYCLE
    // PRODUCTION_NIGHT
}
static class NightCycleDispatchTests
{
    static int checks;
    static void Check(bool result, string why) { checks++; if (!result) throw new Exception(why); }
    static void Main()
    {
        var longBatch = new DispatchHarness(Operation.Extract, Operation.Extract, Operation.Extract, Operation.Dry, Operation.React, Operation.Dry, Operation.Form);
        longBatch.Run(true);
        Check(longBatch.restores == 1 && longBatch.saves == 3, "restore and persist each completed reaction");
        Check(NightReactions.seconds == 2100 && longBatch.running.step == 3, "complete long extraction before daily evaporation");
        Check(longBatch.evaporations == 1 && longBatch.water == 1, "one evaporation budget per night");
        longBatch.Run(true);
        Check(longBatch.running.step == 5 && NightReactions.seconds == 2800, "complete reaction after first drying stage");
        Check(longBatch.evaporations == 2 && longBatch.water == 1, "second drying stage waits for its own budget");
        longBatch.Run(true);
        Check(longBatch.running.step == 6 && longBatch.evaporations == 3, "ready material waits at output stage");
        var turbo = new DispatchHarness(Operation.Extract, Operation.Dry);
        int secondsBefore = NightReactions.seconds;
        turbo.Run(false);
        Check(turbo.running.step == 0 && turbo.evaporations == 0 && secondsBefore == NightReactions.seconds, "daytime boost must not fast-forward extraction");
        turbo = new DispatchHarness(Operation.Dry, Operation.React, Operation.Form) { water = 1 };
        turbo.Run(false);
        Check(turbo.evaporations == 1 && turbo.running.step == 1 && secondsBefore == NightReactions.seconds, "boost evaporates once and leaves later reaction timed");
        var noPower = new DispatchHarness(Operation.Extract, Operation.Dry, Operation.React) { powered = false };
        noPower.Run(true);
        Check(noPower.running.step == 1 && noPower.evaporations == 0 && noPower.water == 2, "lack of power pauses evaporation");
        var failed = new DispatchHarness(Operation.Dry) { failOnAdvance = true };
        failed.Run(true);
        Check(failed.running.failed && failed.evaporations == 0, "newly failed batch cannot evaporate");
        failed = new DispatchHarness(Operation.Extract); failed.running.failed = true;
        secondsBefore = NightReactions.seconds; failed.Run(true);
        Check(secondsBefore == NightReactions.seconds, "existing failure remains paused");
        var missing = new DispatchHarness(Operation.Extract) { hardware = false };
        missing.Run(true);
        Check(missing.running.step == 0 && missing.saves == 0, "missing hardware remains paused");
        var done = new DispatchHarness(Operation.React); done.Run(true);
        Check(done.running == null && done.saves == 1 && done.evaporations == 0, "no solvents implies no extra evaporation");
        Console.WriteLine(checks + " night-cycle dispatch checks passed");
    }
}
