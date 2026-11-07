using System;
using System.Linq;
using DreamDustAutomation;
using DreamDustAutomation.Planning;

internal static class TankCleanupTests
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Tank cleanup: " + why); }
    private static SimulationItem Mix(params object[] parts)
    {
        var item = new SimulationItem();
        for (int i = 0; i < parts.Length; i += 2) ChemistryOperations.SetAmount(item, (string)parts[i], (int)parts[i + 1]);
        return item;
    }
    private sealed class NativeVessel : GameItem
    {
        internal NativeVessel(GameItem source) : base("cleanup_native_vessel")
        {
            foreach (var pair in source.state.dict) state.dict.Add(pair.Key, pair.Value.Clone());
            shape = modifiedShape = new GridShapeBuilder(1, 2).SetDataFill(1);
            state.GetTag("WINE_CONTAINER_TAG").Enable(); // Skip scene-only sprite callback; removal stays native.
            Validate();
        }
        public override void Validate()
        {
            modifiedState = new TagSystem();
            foreach (var pair in state.dict) modifiedState.dict.Add(pair.Key, pair.Value.Clone());
        }
        public override void Destroy() { }
    }

    internal static string Run()
    {
        checks = 0;
        // Fake feeds are processed separately, then pooled in the finisher.
        // Reproduce stopping after some bottles have already left that vessel.
        var reactor = Mix("acid", 40013);
        var finisher = Mix();
        var buffer = Mix("starch", 137);
        FinishingStorage.Stage(reactor, finisher, 20, 2000, true);
        ChemistryOperations.SetAmount(reactor, "base", 40000);
        FinishingStorage.Stage(reactor, finisher, 20, 2000, true);
        for (int n = 0; n < 8; n++) ChemistryOperations.TransferToProduct(finisher, Mix(), 2000);
        Check(ChemistryOperations.Total(finisher) == 64000, "fixture did not partially dispense");
        var cleanup = new TankCleanup();
        var vessels = new GameItem[] { reactor, buffer, finisher };
        Check(cleanup.RequestMany(vessels, false), "review refused after stopping output");
        var before = ChemistryOperations.Parts(finisher);
        ChemistryOperations.ReactionTick(finisher);
        Check(!before.SequenceEqual(ChemistryOperations.Parts(finisher)), "mixed fake residue did not react while idle");
        Check(!cleanup.ConfirmMany(vessels, false), "expected legacy confirmation race was not reproduced");
        Check(vessels.Any(t => ChemistryOperations.Total(t) > 0), "legacy path unexpectedly cleared the residue");

        var recoveredAlcohol = Mix("alcohol", 123456);
        var unusedStock = Mix("acid", 654321);
        foreach (bool nativeTickFirst in new[] { false, true })
        {
            // Reactor/buffer ticks are driven by the controller; the finisher
            // uses its guarded native callback. Exercise both traversal orders.
            reactor = Mix("water", 12000, "acid", 6000, "base", 7000);
            buffer = Mix("protein", 5000, "acid", 9000);
            finisher = Mix("acid", 32000, "base", 32000, "starch", 51);
            vessels = new GameItem[] { reactor, buffer, finisher };
            finisher.onOneSecondItemFunc = (item, inv, slot) => ChemistryOperations.ReactionTick(item);
            using (var binding = new LiquidSourceBinding(finisher, () => cleanup.Holds(finisher), () => false))
            {
                Check(cleanup.RequestMany(vessels, false), "review refused");
                var snapshots = vessels.Select(ChemistryOperations.Parts).ToArray();
                for (int second = 0; second < 360; second++)
                {
                    if (nativeTickFirst) finisher.onOneSecondItemFunc(finisher, null, null);
                    cleanup.TickIdle(reactor); cleanup.TickIdle(buffer);
                    if (!nativeTickFirst) finisher.onOneSecondItemFunc(finisher, null, null);
                }
                for (int i = 0; i < vessels.Length; i++)
                    Check(snapshots[i].SequenceEqual(ChemistryOperations.Parts(vessels[i])), "reviewed residue reacted before confirmation");
                Check(cleanup.ConfirmMany(vessels, false), "confirmation failed after waiting");
                for (int second = 0; second < 3; second++)
                { cleanup.TickIdle(reactor); cleanup.TickIdle(buffer); finisher.onOneSecondItemFunc(finisher, null, null); }
                Check(vessels.All(t => ChemistryOperations.Total(t) == 0), "residue returned after cleanup");
                Check(!cleanup.Pending && vessels.All(t => !cleanup.Holds(t)), "confirmation left reaction holds active");

                ChemistryOperations.SetAmount(finisher, "acid", 32000);
                ChemistryOperations.SetAmount(finisher, "base", 32000);
                cleanup.RequestMany(vessels, false);
                cleanup.Cancel(); // Also used when closing the machine window.
                before = ChemistryOperations.Parts(finisher);
                finisher.onOneSecondItemFunc(finisher, null, null);
                Check(!before.SequenceEqual(ChemistryOperations.Parts(finisher)), "cancel/close failed to resume native chemistry");
                Check(!cleanup.ConfirmMany(vessels, false), "cancelled confirmation remained usable");

                cleanup.RequestMany(vessels, false);
                ChemistryOperations.SetAmount(finisher, "starch", 1000);
                Check(!cleanup.ConfirmMany(vessels, false) && ChemistryOperations.Amount(finisher, "starch") == 1000,
                    "manual changes must still require another review");
            }
        }
        var single = Mix("acid", 3000, "base", 3000);
        Check(cleanup.Request(single, false) && cleanup.Holds(single), "single-tank review did not hold reactions");
        before = ChemistryOperations.Parts(single);
        cleanup.TickIdle(single);
        Check(before.SequenceEqual(ChemistryOperations.Parts(single)) && cleanup.Confirm(single, false), "single-tank cleanup failed");
        Check(ChemistryOperations.Total(single) == 0, "single-tank residue remained");
        Check(ChemistryOperations.Total(recoveredAlcohol) == 123456 && ChemistryOperations.Total(unusedStock) == 654321,
            "cleanup touched solvent recovery or input stock");
        // Run the live-container branch of RemoveAndReturn as well; simulation
        // alone would not verify refreshed native state after each component.
        var native = new GameItem[] { new NativeVessel(Mix("starch", 137)),
            new NativeVessel(Mix("acid", 7000, "base", 6000)), new NativeVessel(Mix("chemical_contaminant", 1860)) };
        Check(cleanup.RequestMany(native, false) && cleanup.ConfirmMany(native, false), "native vessel cleanup refused");
        Check(native.All(t => ChemistryOperations.Total(t) == 0 && ChemistryOperations.Parts(t).All(x => x == 0)),
            "native removal left material or stale cached composition");
        return checks + " cleanup checks passed: partial fake output, native/controller tick order, delayed confirmation, all three vessels, cancel/resume and changed contents.";
    }
}
