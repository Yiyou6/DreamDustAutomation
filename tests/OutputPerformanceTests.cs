using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DreamDustAutomation;
using DreamDustAutomation.Planning;

internal static class OutputPerformanceTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Output performance: " + message); }
    private static GridShape Box(int w, int h, int x = 0, int y = 0) =>
        new GridShapeBuilder(w, h).SetDataFill(1).SetPosition(x, y);

    // Native geometry oracle: original all-orientations/all-cells search,
    // including native collision and irregular inventory boundary semantics.
    private static bool NativeFits(int width, int height, GridShape boundary, List<GridShape> contents, int w, int h)
    {
        GridShape shape = new GridShapeBuilder(w, h).SetDataFill(1);
        for (int flip = 0; flip < 2; flip++) for (int rotation = 0; rotation < 4; rotation++)
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                ((GridShapeBuilder)shape).SetTransform(x, y, flip != 0, 4 - rotation);
                bool inside = boundary == null ? shape.FullyIntersectsRect(width, height, (a, b) => a != 1 || b == 0) :
                    shape.FullyIntersects(boundary, (a, b) => a != 1 || b == 0);
                if (inside && contents.All(other => !shape.Intersects(other))) return true;
            }
        return false;
    }

    // Only omit Unity rendering construction. Tests use native inventory types,
    // native geometry, native callbacks and the production gate/snapshot code.
    private static GameGridScrollableInventory Bin(int w, int h, params GameItem[] items)
    {
        var bin = (GameGridScrollableInventory)FormatterServices.GetUninitializedObject(typeof(GameGridScrollableInventory));
        typeof(TagSystem).GetProperty("dict").SetValue(bin, new Dictionary<string, TagState>());
        typeof(GameGridScrollableInventory).GetProperty("width").SetValue(bin, w);
        typeof(GameGridScrollableInventory).GetProperty("height").SetValue(bin, h);
        typeof(GameGridScrollableInventory).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bin, new List<GameItem>(items));
        foreach (var item in items) item.parentInventory = bin;
        return bin;
    }
    private static List<GameItem> Items(GameGridScrollableInventory bin) =>
        (List<GameItem>)typeof(GameGridScrollableInventory).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bin);
    private static SimulationItem Item(GridShape shape)
    { var item = new SimulationItem(); item.modifiedShape = item.shape = shape; return item; }

    private sealed class RefreshProbe : GameItem
    {
        internal int Refreshes;
        internal RefreshProbe(GameItem copy = null) : base("output_refresh_probe")
        {
            if (copy != null) foreach (var tag in copy.state.dict) state.dict.Add(tag.Key, tag.Value.Clone());
            shape = modifiedShape = Box(1, 2);
            state.GetTag("WINE_CONTAINER_TAG").Enable(); // Native pour callback skips scene-only sprites.
            RefreshCache();
        }
        private void RefreshCache()
        {
            modifiedState = new TagSystem();
            foreach (var tag in state.dict) modifiedState.dict.Add(tag.Key, tag.Value.Clone());
        }
        public override void Validate() { Refreshes++; RefreshCache(); }
        public override void Destroy() { }
    }

    private sealed class VisibleItem
    {
        internal bool Active = true;
        internal GridShape Shape;
    }

    internal static string Run()
    {
        checks = 0;
        CheckPlacementSlices();
        var random = new Random(313);
        var rendered = Enumerable.Range(0, 1700).Select(n => new VisibleItem { Shape = Box(1, 1, n % 16, n / 16) }).ToArray();
        int visibilityWrites = 0;
        var viewport = new InventoryViewport<VisibleItem>(i => i.Active, (i, show) => { i.Active = show; visibilityWrites++; });
        foreach (var item in rendered) viewport.Update(item, item.Shape, 0, 8);
        Check(rendered.Count(i => i.Active) == 128, "1700 products should render only the visible eight rows");
        int initialWrites = visibilityWrites;
        foreach (var item in rendered) viewport.Update(item, item.Shape, 0, 8);
        Check(initialWrites == visibilityWrites, "Unchanged viewport must not toggle render objects repeatedly");
        foreach (int row in new[] { 40, 90, 0, 99, 10 })
        {
            foreach (var item in rendered) viewport.Update(item, item.Shape, row, 8);
            Check(rendered.All(i => i.Active == (i.Shape.maxY >= row && i.Shape.minY < row + 8)), "Scrolling must restore newly visible products");
        }
        var removed = rendered[1600];
        Check(!removed.Active, "Probe must start offscreen");
        viewport.Release(removed);
        Check(removed.Active, "Taking out a hidden product must restore its image in the destination inventory");
        viewport.Update(removed, removed.Shape, 10, 8);
        Check(!removed.Active, "Returning it to an offscreen slot hides only the image again");
        removed.Shape = Box(1, 2, 0, 9);
        viewport.Update(removed, removed.Shape, 10, 8);
        Check(removed.Active, "Partially visible multi-cell products must remain interactable");
        var externallyHidden = new VisibleItem { Active = false, Shape = Box(1, 1, 0, 80) };
        viewport.Update(externallyHidden, externallyHidden.Shape, 0, 8);
        viewport.Release(externallyHidden);
        Check(!externallyHidden.Active, "Must not override another feature's hidden state");
        Check(rendered.Length == 1700, "Visibility must not truncate the inventory");
        Check(OutputCapacity.Count(16, 160, new GridShape[0], Box(1, 1)) == 2560, "Expanded bin holds 2560 single-cell products");
        Check(OutputCapacity.Count(16, 160, new GridShape[0], Box(1, 2)) == 1280, "Expanded bin holds 1280 two-cell bottles, not 2560");
        Check(OutputCapacity.Count(16, 160, new[] { Box(2, 2), Box(1, 2, 4, 0) }, Box(1, 2)) == 1277,
            "Existing finished products consume actual output capacity");
        Check(OutputCapacity.Count(3, 3, new[] { Box(1, 1, 0, 1), Box(1, 1, 1, 0), Box(1, 1, 1, 2), Box(1, 1, 2, 1) }, Box(1, 2)) == 0,
            "Five isolated empty cells cannot hold a bottle");
        Check(OutputCapacity.Count(2, 1, new GridShape[0], Box(1, 2)) == 1, "Use rotated residual space");
        for (int test = 0; test < 100; test++)
        {
            int w = random.Next(2, 10), h = random.Next(2, 10);
            var original = new List<GridShape>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (random.Next(3) == 0) original.Add(Box(1, 1, x, y));
            var occupied = new List<GridShape>(original);
            GridShape bottleShape = Box(random.Next(1, 4), random.Next(1, 4));
            int expected = 0;
            while (true)
            {
                GridShape found = null;
                for (int f = 0; f < 2 && found == null; f++) for (int r = 0; r < 4 && found == null; r++)
                    for (int y = 0; y < h && found == null; y++) for (int x = 0; x < w && found == null; x++)
                    {
                        GridShape s = new GridShapeBuilder(bottleShape).SetTransform(x, y, f != 0, 4-r);
                        if (s.FullyIntersectsRect(w, h, (a,b) => a != 1 || b == 0) && occupied.All(o => !s.Intersects(o))) found = s;
                    }
                if (found == null) break;
                occupied.Add(found); expected++;
            }
            Check(OutputCapacity.Count(w, h, original, bottleShape) == expected, "Capacity differs from repeated native shape placement");
        }
        for (int test = 0; test < 300; test++)
        {
            int width = random.Next(2, 15), height = random.Next(2, 15);
            var contents = new List<GridShape>();
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (random.Next(4) != 0) contents.Add(Box(1, 1, x, y));
            GridShape boundary = test % 2 == 0 ? null : new GridShapeBuilder(width, height) { outside = 1 }.SetDataFill(0);
            var space = new OutputSpace(width, height, boundary, contents);
            for (int w = 1; w <= 4; w++) for (int h = 1; h <= 3; h++)
                Check(space.Fits(w, h) == NativeFits(width, height, boundary, contents, w, h), "native fit mismatch in randomized filled/fragmented grid");
        }
        Check(!OutputSpace.Rectangle(new GridShapeBuilder(2, 2).SetDataFill(0)), "non-rectangle must use native fallback");
        Check(OutputSpace.Rectangle(Box(1, 2)), "ordinary bottle rectangle must be optimized");

        var fullContents = new List<GridShape>();
        for (int y = 0; y < 16; y += 2) for (int x = 0; x < 16; x++) fullContents.Add(Box(1, 2, x, y));
        var full = new OutputSpace(16, 16, null, fullContents);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++) Check(!full.Fits(1, 2), "500 full-output checks must stay blocked");
        double fastMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        for (int i = 0; i < 500; i++) Check(!NativeFits(16, 16, null, fullContents, 1, 2), "full native oracle");
        double nativeMs = watch.Elapsed.TotalMilliseconds;
        fullContents.RemoveAt(0);
        Check(new OutputSpace(16, 16, null, fullContents).Fits(1, 2), "removing one bottle resumes output");

        var intake = Item(Box(1, 1)); intake.uniqueId = 101; intake.EnableTag(HopperHelper.INTAKE_TAG);
        var outlet = Item(Box(1, 1)); outlet.uniqueId = 102; outlet.EnableTag(HopperHelper.OUTLET_TAG);
        intake.ModifyTag(HopperHelper.LINK_INT, tag => tag.Enable().SetInt(outlet.uniqueId));
        var bottle = Item(Box(1, 2));
        var blocker = Item(Box(3, 4));
        var source = Bin(16, 16, intake, bottle);
        var target = Bin(3, 4, blocker);
        outlet.parentInventory = target;
        bool transferring = true;
        int frame = 1, resolves = 0;
        Action<GameItem, GameInventory, SlotMarker> existing = (item, inv, slot) => { };
        target.onSlotRemoveItemEndFunc = existing;
        using (var gate = new HopperOutputGate(source, () => transferring, () => frame, item => { resolves++; return outlet; }))
        {
            Check(bottle.MaxNumRemove() == 0, "native removal amount observes full-target backpressure");
            Check(!GraphUtils.CanAccept(target, bottle), "native hopper's CanAccept exits before expensive grid search");
            for (int i = 0; i < 500; i++) Check(gate.Limit(bottle) == 0, "500 native hopper searches into full destination must be skipped");
            Check(resolves == 1, "resolve linked outlet only once");
            transferring = false;
            Check(gate.Limit(bottle) == int.MaxValue, "full hopper destination must never block manual removal");
            Check(bottle.MaxNumRemove() == bottle.unitCount, "manual native removal amount unchanged");
            transferring = true;
            bottle.mayStackItemFunc = (a, b) => true;
            Check(gate.Limit(bottle) == int.MaxValue, "stackable item must retain native stack search");
            bottle.mayStackItemFunc = null;
            Items(target).Clear();
            target.onSlotRemoveItemEndFunc(blocker, target, null);
            Check(gate.Limit(bottle) == int.MaxValue, "freed destination resumes in the same frame");
            Items(target).Add(blocker);
            target.onSlotAddItemEndFunc(blocker, target, null);
            Check(gate.Limit(bottle) == 0, "destination filled in the same frame blocks again");
            target.slotSearchPassthroughForItemFunc = (item, inv, slot, other) => true;
            frame++;
            Check(gate.Limit(bottle) == int.MaxValue, "nested-container search must not be suppressed");
            target.slotSearchPassthroughForItemFunc = null;
            frame++;
            Check(gate.Limit(bottle) == 0, "new frame rechecks target geometry");
            Check(resolves == 1, "unchanged link must not rescan the entire shop on each frame");
            var otherOutlet = Item(Box(1, 1)); otherOutlet.uniqueId = 103;
            otherOutlet.parentInventory = Bin(3, 4);
            outlet.parentInventory = otherOutlet.parentInventory;
            Check(gate.Limit(bottle) == int.MaxValue, "moving linked outlet updates destination immediately");
            outlet.parentInventory = target;
            Check(gate.Limit(bottle) == 0, "moved back to full destination");
            intake.state.GetTag(HopperHelper.LINK_INT).SetInt(103);
            Check(gate.Limit(bottle) == int.MaxValue, "relink in same frame must fail open, not block new destination");
            gate.Clear();
            Check(target.onSlotRemoveItemEndFunc == existing && target.onSlotAddItemEndFunc == null &&
                target.onSlotChangeAmountItemFunc == null, "detach only our invalidators; preserve original handlers");
        }
        Check(source.maxSlotRemoveItemFunc == null && source.onSlotAddItemEndFunc == null && source.onSlotRemoveItemEndFunc == null,
            "disposed storage gate must release source handlers");
        CheckMultipleRoutes();
        // Observe conserved chemistry across repeated full/drain/resume checkpoints.
        var tank = new SimulationItem();
        ChemistryOperations.SetAmount(tank, "dream_cap_extract", 1000000);
        var recipe = ChemicalRecipe.Get("dream_dust");
        var plan = new BatchPlan { productId = recipe.Id, requestedGrade = 99, actualGrade = 99, expectedBottles = 500,
            actions = new[] { new BatchAction(Operation.Form, amount: 500) } };
        var batch = new RunningBatch { plan = plan };
        var shell = new RefreshProbe();
        int held = 0, collected = 0;
        while (batch.produced < 500)
        {
            if (held == 128)
            {
                int remaining = ChemistryOperations.Total(tank);
                batch = BatchPersistence.Deserialize(BatchPersistence.Serialize(batch));
                Check(ChemistryOperations.Total(tank) == remaining, "full output/save-resume must preserve unformed liquid");
                collected += held; held = 0;
            }
            var product = new SimulationItem();
            ChemistryOperations.TransferToProduct(tank, product, recipe.UnitVolume);
            Check(ChemistryOperations.Amount(product, recipe.Active) == recipe.UnitVolume, "native per-bottle concentration unchanged");
            batch.produced++; held++;
            BatchPersistence.Save(shell, batch);
            Check(BatchPersistence.Load(shell, out var error)?.produced == batch.produced && error == null,
                "every product checkpoint is immediately readable without rendering");
            Check(shell.state.GetTag(BatchPersistence.SaveTag).GetString() == shell.GetTagReadonly(BatchPersistence.SaveTag).GetString(),
                "serialized state and cached native state agree");
        }
        Check(collected + held == 500 && ChemistryOperations.Total(tank) == 0, "500 products without loss, duplication or quantity cap");
        Check(shell.Refreshes == 0, "500 persisted products must not rebuild whole machine inventories");
        BatchPersistence.Save(shell, null);
        Check(BatchPersistence.Load(shell, out var completedError) == null && completedError == null, "completion clears cached and serialized progress");

        for (int test = 0; test < 100; test++)
        {
            var parts = new int[Liquid.Liquids.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = random.Next(0, 4000);
            var native = new RefreshProbe();
            for (int i = 0; i < parts.Length; i++)
            {
                int amount = parts[i];
                native.ModifyTag(Liquid.Liquids[i].currentPart, tag => tag.Enable().SetInt(amount));
            }
            var optimized = new RefreshProbe(native);
            native.Refreshes = 0;
            int volume = test % 5 == 0 ? parts.Sum() : random.Next(1, parts.Sum());
            WaterHelper.RemoveAndReturn(native, volume);
            BottlingOperations.Consume(optimized, volume);
            Check(ChemistryOperations.Parts(native).SequenceEqual(ChemistryOperations.Parts(optimized)),
                "coalesced bottling consumption must exactly match native per-component rounding");
            Check(optimized.Refreshes == 1 && native.Refreshes == Liquid.Liquids.Count,
                "consumption renders once instead of once for every chemical component");
            var product = new RefreshProbe();
            BottlingOperations.WriteParts(product, parts);
            Check(ChemistryOperations.Parts(product).SequenceEqual(parts) && product.Refreshes == 1,
                "product composition is written completely with one native refresh");
        }
        foreach (string reagent in new[] { "acid", "base" })
        {
            var stock = new RefreshProbe();
            ChemistryOperations.SetAmount(stock, reagent, 1000000);
            var tankProbe = new SimulationItem();
            ChemistryOperations.SetAmount(tankProbe, "chemical_contaminant", 100000);
            ChemistryOperations.SetAmount(tankProbe, "protein", 30000);
            ChemistryOperations.SetAmount(tankProbe, "acid", 50000);
            ChemistryOperations.SetAmount(tankProbe, "base", 50000);
            int left = 1000000;
            for (int n = 0; n < 100; n++)
            {
                int dose = n == 99 ? left : 137 + n * 13;
                ChemistryOperations.Pour(stock, tankProbe, dose); // Real native removal, not the SimulationItem branch.
                left -= dose;
                ChemistryOperations.ReactionTick(tankProbe);
                Check(ChemistryOperations.Amount(stock, reagent) == left, "native pure-reagent withdrawal lost extra stock");
                Check(ChemistryOperations.Total(stock) == left, "reactor contamination entered native reagent source");
                Check(ChemistryOperations.Amount(stock, "chemical_contaminant") == 0, "native acid/base gained chemical contamination");
            }
        }
        return "Output performance: " + checks + " checks; 500 full-grid geometry probes: native search oracle " +
            nativeMs.ToString("F1") + " ms, indexed geometry " + fastMs.ToString("F1") + " ms. Headless, not in-game FPS. " +
            "Hopper full/drain/relink/manual/stack/passthrough and 500-product save/resume verified. 1700-product viewport: 128 visible; scroll, reorder, partial visibility and removal verified.";
    }

    private static void CheckMultipleRoutes()
    {
        var first = Item(Box(1, 1)); first.EnableTag(HopperHelper.INTAKE_TAG);
        var second = Item(Box(1, 1)); second.EnableTag(HopperHelper.INTAKE_TAG);
        var fullOutlet = Item(Box(1, 1)); fullOutlet.uniqueId = 2001;
        var emptyOutlet = Item(Box(1, 1)); emptyOutlet.uniqueId = 2002;
        var full = Bin(16, 160, Item(Box(16, 160)));
        var empty = Bin(16, 160);
        fullOutlet.parentInventory = full; emptyOutlet.parentInventory = empty;
        first.state.GetTag(HopperHelper.LINK_INT).Enable().SetInt(fullOutlet.uniqueId);
        var products = Enumerable.Range(0, 1700).Select(n => Item(Box(1, 1, n % 16, n / 16))).ToArray();
        foreach (var p in products) p.identifier = "dream_dust";
        var source = Bin(16, 160, new GameItem[] { first, second }.Concat(products).ToArray());
        int frame = 1, resolves = 0;
        var nativeFlag = typeof(HopperHelper).GetField("transferring", BindingFlags.Static | BindingFlags.NonPublic);
        Check(nativeFlag?.FieldType == typeof(bool), "native hopper transfer flag changed");
        bool previous = (bool)nativeFlag.GetValue(null);
        try
        {
            nativeFlag.SetValue(null, true);
            using (var gate = new HopperOutputGate(source, currentFrame: () => frame, findOutlet: i =>
            {
                resolves++;
                int link = i.GetTagReadonly(HopperHelper.LINK_INT).GetInt();
                return link == 2001 ? fullOutlet : link == 2002 ? emptyOutlet : null;
            }))
            {
                foreach (int count in new[] { 145, 350, 1700 })
                {
                    frame++;
                    for (int i = 0; i < count; i++)
                        Check(!GraphUtils.CanAccept(full, products[i]), "unlinked second hopper disabled full-destination protection");
                }
                Check(resolves == 1, "full runs repeatedly resolved an unchanged outlet");
                var p = products[0];
                second.state.GetTag(HopperHelper.LINK_INT).Enable().SetInt(2002);
                second.state.GetTag(HopperHelper.FILTER_MODE_INT).Enable().SetInt(HopperHelper.FILTER_ALLOW);
                second.state.GetTag(HopperHelper.FILTER_STRING).Enable().SetString("caffeine_pill");
                Check(p.MaxNumRemove() == 0, "unrelated allow-list route disabled protection");
                second.state.GetTag(HopperHelper.FILTER_STRING).SetString("dream_dust");
                Check(p.MaxNumRemove() == p.unitCount, "valid empty alternative route was blocked");
                second.state.GetTag(HopperHelper.FILTER_MODE_INT).SetInt(HopperHelper.FILTER_BLOCK);
                Check(p.MaxNumRemove() == 0, "block-list route disabled protection");
                second.state.GetTag(HopperHelper.FILTER_MODE_INT).SetInt(HopperHelper.FILTER_ALL);
                second.state.GetTag(HopperHelper.LINK_INT).SetInt(9999);
                int before = resolves;
                for (int i = 0; i < 1700; i++) Check(products[i].MaxNumRemove() == 0, "deleted outlet disabled protection");
                Check(resolves == before + 1, "missing outlet repeatedly scanned the shop in one frame");
                first.state.GetTag(HopperHelper.KEEP_INT).Enable().SetInt(20);
                Check(p.MayRemove(), "capacity rejection must preserve native keep-count membership");
                Items(full).Clear(); full.onSlotRemoveItemEndFunc(null, full, null);
                Check(p.MaxNumRemove() == p.unitCount, "full-to-empty transition did not resume immediately");
                Items(full).Add(Item(Box(16, 160))); full.onSlotAddItemEndFunc(null, full, null);
                Check(p.MaxNumRemove() == 0, "empty-to-full transition was stale");
                nativeFlag.SetValue(null, false);
                Check(p.MaxNumRemove() == p.unitCount, "native manual-removal flag was ignored");
            }
            Check(source.maxSlotRemoveItemFunc == null && full.onSlotAddItemEndFunc == null,
                "disposing large-batch gate leaked callbacks");
        }
        finally { nativeFlag.SetValue(null, previous); }
    }

    private static void CheckPlacementSlices()
    {
        var random = new Random(356);
        for (int test = 0; test < 60; test++)
        {
            int w = random.Next(2, 10), h = random.Next(2, 10);
            var contents = new List<GridShape>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (random.Next(3) == 0) contents.Add(Box(1, 1, x, y));
            var cache = new OutputPlacement(w, h, contents);
            for (int insertion = 0; insertion < 10; insertion++)
            {
                var original = Box(random.Next(1, 4), random.Next(1, 4));
                Func<GridShape, SlotMarker> validate = shape =>
                    shape.FullyIntersectsRect(w, h, (a, b) => a != 1 || b == 0) &&
                    contents.All(other => !shape.Intersects(other)) &&
                    (test % 2 == 0 || shape.minX != 0) // Native custom rule can reject a free slot.
                    ? new SlotMarker { numTransfer = 1, itemGridShape = shape.Clone() } : null;
                SlotMarker native = null;
                for (int f = 0; f < 2 && native == null; f++) for (int r = 0; r < 4 && native == null; r++)
                    for (int y = 0; y < h && native == null; y++) for (int x = 0; x < w && native == null; x++)
                        native = validate(new GridShapeBuilder(original).SetTransform(x, y, f != 0, 4-r));
                var fast = cache.Find(original, validate);
                Check((native == null) == (fast == null), "cached/native availability differs");
                if (fast == null) continue;
                Check(native.itemGridShape.minX == fast.itemGridShape.minX && native.itemGridShape.minY == fast.itemGridShape.minY &&
                    native.itemGridShape.globalWidth == fast.itemGridShape.globalWidth && native.itemGridShape.globalHeight == fast.itemGridShape.globalHeight,
                    "cached/native orientation or first slot differs");
                contents.Add(fast.itemGridShape); cache.Add(fast.itemGridShape);
                if (insertion % 3 == 0)
                {
                    contents.RemoveAt(0); // A hopper/player frees a slot between coroutine slices.
                    cache = new OutputPlacement(w, h, contents);
                }
            }
        }
        foreach (int count in new[] { 350, 1700, 2560 })
        {
            var contents = new List<GridShape>();
            OutputPlacement cache = null;
            for (int n = 0; n < count; n++)
            {
                if (n % 8 == 0) cache = new OutputPlacement(16, 160, contents);
                var slot = cache.Find(Box(1, 1), shape =>
                {
                    Check(contents.All(other => !shape.Intersects(other)), "large batch overlap");
                    return new SlotMarker { numTransfer = 1, itemGridShape = shape.Clone() };
                });
                Check(slot != null && slot.itemGridShape.minX == n % 16 && slot.itemGridShape.minY == n / 16,
                    "large batch order/capacity");
                contents.Add(slot.itemGridShape); cache.Add(slot.itemGridShape);
            }
            if (count == 2560) Check(cache.Find(Box(1, 1), s => new SlotMarker { numTransfer = 1 }) == null, "full bin accepted extra product");
            contents.RemoveAt(0);
            var resumed = new OutputPlacement(16, 160, contents).Find(Box(1, 1), s => new SlotMarker { numTransfer = 1, itemGridShape = s.Clone() });
            Check(resumed.itemGridShape.minX == 0 && resumed.itemGridShape.minY == 0, "fresh slice missed freed slot");
        }
    }
}
