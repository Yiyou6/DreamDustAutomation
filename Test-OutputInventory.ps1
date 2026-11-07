$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$managed = Join-Path (Resolve-Path (Join-Path $project '../..')).Path 'Probably Stolen_Data/Managed'
$testRoot = Join-Path $project 'TestResults/output-inventory'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
[void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
$resolver = [DseCecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader = [DseCecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$fixture = [DseCecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'), $reader)
try {
    $type = $fixture.MainModule.Types | Where-Object Name -eq 'GameGridScrollableInventory'
    $method = $type.Methods | Where-Object { $_.Name -eq 'Validate' -and $_.Parameters.Count -eq 1 }
    # Retain the game's real validateItems branch and foreach/Dispose loop.
    # Remove only Unity layout before/after it, allowing an unrendered inventory.
    $instructions = $method.Body.Instructions
    $start = @($instructions | Where-Object { $_.OpCode.Code -eq 'Ldarg_1' -and $_.Next.OpCode.Code -eq 'Brfalse_S' })
    if ($start.Count -ne 1) { throw 'Native validation loop changed; inspect before testing' }
    $end = $start[0].Next.Operand
    $keep = $false
    foreach ($instruction in @($instructions)) {
        if ($instruction -eq $start[0]) { $keep = $true }
        if (!$keep) { [void]$instructions.Remove($instruction) }
        if ($instruction -eq $end) { $instruction.OpCode = [DseCecil.Cil.OpCodes]::Ret; $instruction.Operand = $null; $keep = $false }
    }
    $fixture.Write((Join-Path $testRoot 'Assembly-CSharp.dll'))
} finally { $fixture.Dispose() }
$source = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
public class OutputInventoryHost {
    private sealed class Probe : GameItem {
        public int Calls;
        public Probe() : base("product_refresh_probe") { }
        public override void Validate() { Calls++; }
        public override void Destroy() { }
    }
    private static GameGridScrollableInventory Make(Type type, List<GameItem> items) {
        var inventory = (GameGridScrollableInventory)FormatterServices.GetUninitializedObject(type);
        typeof(GameGridScrollableInventory).GetField("items", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(inventory, items);
        return inventory;
    }
    public static string Run(string modPath) {
        var type = Assembly.LoadFrom(modPath).GetType("DreamDustAutomation.UI.ProductInventory", true);
        var items = new List<GameItem>();
        for(int i=0;i<1280;i++) items.Add(new Probe());
        GameInventory normal = Make(typeof(GameGridScrollableInventory), items);
        normal.Validate();
        foreach(Probe p in items) { if(p.Calls != 1) throw new Exception("Native baseline did not refresh all products"); p.Calls=0; }
        var optimized = Make(type, items);
        for(int i=0;i<1280;i++) {
            ((GameInventory)optimized).Validate();
            ((PixelElement)optimized).Validate();
            ((GraphNodeStorage)optimized).Validate();
        }
        foreach(Probe p in items) if(p.Calls != 0) throw new Exception("Output insertion/layout revalidated older products");
        // Explicit per-item updates still dispatch normally.
        items[0].Validate();
        if(((Probe)items[0]).Calls!=1) throw new Exception("Individual item updates suppressed");
        return "Output refresh: native baseline refreshes all 1280 items; 3840 optimized native/interface refresh calls revalidate 0 older items. Native foreach retained; Unity layout omitted.";
    }
}
'@
$refs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName)
# Use the same managed-reference compilation as the other headless tests.
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source))
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($file in $refs) { $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file)) }
$compiler = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('OutputInventoryTests', $trees, $references,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary))
$stream = [IO.File]::Create((Join-Path $testRoot 'OutputInventoryTests.dll'))
try { $result = $compiler.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Output test compilation failed' }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class OutputFixtureHost {
    public static string Run(string managed, string tests, string mod) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args) => {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(name == "Assembly-CSharp" ? tests : managed, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return (string)Assembly.LoadFrom(Path.Combine(tests,"OutputInventoryTests.dll")).GetType("OutputInventoryHost")
            .GetMethod("Run").Invoke(null,new object[]{mod});
    }
}
'@
[OutputFixtureHost]::Run($managed, $testRoot, (Join-Path $project 'dist/DreamDustAutomation/DreamDustAutomation.dll'))
