using System;
using System.Collections.Generic;
using DreamDustAutomation.UI;

// Minimal native UI contract for testing dispatch and layout scheduling without
// starting Unity. Chemistry/inventory implementation is not replaced in-game.
namespace UnityEngine
{
    public static class Screen { public static int width = 1920, height = 1080; }
    public class MonoBehaviour { }
    public sealed class GameObject { public T AddComponent<T>() where T : new() => new T(); }
}
public interface PixelElement { void Validate(); }
public interface GraphNodeStorage { void Validate(); }
public sealed class TreeNodeRender { public UnityEngine.GameObject gameObject = new UnityEngine.GameObject(); }
public sealed class MachineryPanel { public object Storage; }
public class MachineryPanelRow : PixelElement, GraphNodeStorage
{
    public readonly List<MachineryPanel> Panels = new List<MachineryPanel>();
    public readonly TreeNodeRender handler = new TreeNodeRender();
    public readonly List<object> children = new List<object>();
    public int Passes;
    public Action DuringLayout;
    public MachineryPanelRow(params MachineryPanel[] panels)
    { foreach (var panel in panels) { children.Add(panel.Storage); Panels.Add(panel); } }
    public void Validate() { Passes++; DuringLayout?.Invoke(); }
}
public sealed class PixelWindow
{
    public bool Visible = true;
    public int Passes;
    public PixelElement Child;
    public bool IsVisible() => Visible;
    public void Validate() { Passes++; Child?.Validate(); }
}
namespace DreamDustAutomation
{
    internal static class ModPerformance
    {
        public static int Depth;
        public static long Begin() { Depth++; return 0; }
        public static void Record(int category, long start, string operation) { Depth--; }
    }
    internal static class ModEntry { public static readonly TestLog Log = new TestLog(); }
    internal sealed class TestLog { public void Error(string message) { } }
}
namespace DreamDustAutomation.UI
{
    internal sealed class MachineRegion { internal int Passes; public void Validate() { Passes++; } }
    internal static class MachineLayoutSizing
    { public static void Reset(object panel) { } public static void Fit(PixelWindow window) { } }
}
internal static class UiBatchingTests
{
    private static int checks;
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new Exception(label); }
    public static void Main()
    {
        var inputs = new object();
        var outputs = new object();
        var row = new DeferredPanelRow(new MachineryPanel { Storage = inputs }, new MachineryPanel { Storage = outputs });
        ((GraphNodeStorage)row).Validate();
        Check(row.Passes == 1, "Construction remains synchronous before attachment");
        var window = new PixelWindow { Child = row };
        row.AttachTo(window);
        row.Flush();
        int baseline = row.Passes;
        // Real text setters use GraphNodeStorage; PixelWindow uses PixelElement.
        // Both must dispatch to the derived scheduler, not the native base body.
        for (int i = 0; i < 100; i++)
        {
            ((GraphNodeStorage)row).Validate();
            ((PixelElement)row).Validate();
        }
        Check(row.Passes == baseline, "200 notifications do not synchronously rebuild the row");
        row.Flush();
        Check(row.Passes == baseline + 1, "200 notifications become exactly one row pass");
        Check(window.Passes == 2, "Window frame refreshed after the row without recursively rebuilding it");
        row.Flush();
        Check(row.Passes == baseline + 1, "Idle frames do no layout work");
        Check(ReferenceEquals(row.children[0], inputs) && ReferenceEquals(row.children[1], outputs),
            "Native inventory identities and storage ordering preserved");
        window.Visible = false;
        row.Validate();
        row.Flush();
        Check(row.Passes == baseline + 1, "Closed windows defer rendering");
        window.Visible = true;
        row.Flush();
        Check(row.Passes == baseline + 2, "Hidden changes render when reopened");
        row.DuringLayout = () => ((GraphNodeStorage)row).Validate();
        row.Validate(); row.Flush(); row.Flush();
        Check(row.Passes == baseline + 3, "Reentrant layout notifications do not cause a frame loop");
        row.DuringLayout = () => { throw new Exception("fixture layout failure"); };
        row.Validate();
        try { row.Flush(); } catch (Exception) { }
        Check(DreamDustAutomation.ModPerformance.Depth == 0, "Timing scope closes on layout failure");
        row.DuringLayout = null;
        row.Validate(); row.Flush();
        Check(row.Passes == baseline + 5, "Next edit can schedule after a failure");
        var second = new DeferredPanelRow();
        var secondWindow = new PixelWindow { Child = second };
        second.AttachTo(secondWindow); second.Flush();
        row.Validate(); second.Flush();
        Check(second.Passes == 1, "Machine windows schedule independently");
        row.Flush();
        int beforeProgress = row.Passes;
        var counter = new MachineRegion();
        for (int i = 0; i < 1700; i++)
        {
            row.UpdateFixedRegion(counter, () => ((GraphNodeStorage)row).Validate());
            row.Flush();
        }
        Check(counter.Passes == 1700 && row.Passes == beforeProgress, "1700 bottle counters must refresh only the readout, not the whole window");
        row.Validate();
        row.UpdateFixedRegion(counter, () => ((GraphNodeStorage)row).Validate());
        row.Flush();
        Check(row.Passes == beforeProgress + 1, "A local update must preserve an earlier pending tab/layout update");
        try { row.UpdateFixedRegion(counter, () => { throw new Exception("readout failure"); }); } catch (Exception) { }
        row.Flush();
        Check(row.Passes == beforeProgress + 2, "Failed readout must release suppression and request layout repair");
        long revision = MachineLayoutPass.Revision;
        using (MachineLayoutPass.Begin())
        {
            Check(MachineLayoutPass.Revision == revision + 1, "New pass must invalidate previous region caches");
            using (MachineLayoutPass.Begin()) Check(MachineLayoutPass.Revision == revision + 1, "Nested fixed regions must share a measurement pass");
        }
        Console.WriteLine("UI batching: " + checks + " checks passed. 200 native-interface notifications => 1 layout pass (headless contract test, not an in-game FPS benchmark).");
        Console.WriteLine("Bottling counter: 1700 updates => 0 whole-window layouts; pending unrelated changes and exception recovery preserved.");
    }
}
