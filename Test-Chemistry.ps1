param([string]$Suite = 'ChemistryRegressionTests', [string]$ManagedPath = '')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$managed = if ($ManagedPath) { (Resolve-Path -LiteralPath $ManagedPath).Path } else { Join-Path (Resolve-Path (Join-Path $taskRoot '../..')).Path 'Probably Stolen_Data/Managed' }
$testRoot = Join-Path $taskRoot $(if ($ManagedPath) { 'TestResults/chemistry-external' } else { 'TestResults/chemistry' })
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

# Read-only access to the installed game. A test-only copy replaces localization
# text lookups so the game's numerical methods can run without a Unity scene.
# No chemistry or sprite methods are patched; a leaked rendering call still fails.
[void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
$resolver = [DseCecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader = [DseCecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$fixture = [DseCecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'), $reader)
try {
    $locType = $fixture.MainModule.Types | Where-Object FullName -eq 'LocHelper'
    foreach ($method in $locType.Methods | Where-Object { $_.HasBody -and $_.ReturnType.FullName -eq 'System.String' -and $_.Name.StartsWith('GetLocalized') }) {
        $method.Body.Instructions.Clear()
        $method.Body.ExceptionHandlers.Clear()
        $method.Body.Variables.Clear()
        $il = $method.Body.GetILProcessor()
        $il.Append($il.Create([DseCecil.Cil.OpCodes]::Ldstr, '[headless localization]'))
        $il.Append($il.Create([DseCecil.Cil.OpCodes]::Ret))
    }
    $fixture.Write((Join-Path $testRoot 'Assembly-CSharp.dll'))
} finally { $fixture.Dispose() }

$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$sources = @(Get-ChildItem (Join-Path $taskRoot 'src/Planning') -Filter '*.cs') + @(Get-Item (Join-Path $taskRoot 'tests/ChemistryRegressionTests.cs')) + @(Get-Item (Join-Path $taskRoot 'tests/BatchPersistenceTests.cs')) + @(Get-Item (Join-Path $taskRoot 'src/HardwareBinding.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'src/HopperOutputGate.cs')) + @(Get-Item (Join-Path $taskRoot 'tests/OutputPerformanceTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/InputGuidanceTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/InventorySortTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/QuantityPlanningTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ReportedBugsTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/DirectSolventTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/InjectorRegressionTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/HeatedDryingTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/PlanningDiagnosticsTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/SeparateDirectTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/DirectDryingTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ProcessingTimeTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/CoffeeStagingTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/CoffeeQualityTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/LiveModuleTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/NormalDryingTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/FiltrationOrderTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/StockQuantityAuditTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/DeferredOutputTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/BatchRecoveryTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/PartialStockTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/WorkshopFeedbackTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/NightReactionsTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/FantaPlanningTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/StagedResidueTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ForeignReagentTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/TankCleanupTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ResidueSnapshotTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ExpandedControllerTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/SaveMutationTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/PairwiseControllerTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/MaterialCatalogTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/ProteinCleanupTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/OvernightFeedbackTests.cs'))
$sources += @(Get-Item (Join-Path $taskRoot 'tests/SourceStabilityTests.cs'))
foreach ($source in $sources) { $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source.FullName))) }
$controllerSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'src/MachineController.cs'))
$controllerTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($controllerSource)
$flowTests = [IO.File]::ReadAllText((Join-Path $taskRoot 'tests/ControllerFlowTests.cs'))
foreach ($name in @('AdvanceImmediate','AdvanceOutput','FormProducts','Tick','Cycle','CompleteNightReactions','Stop','Source','IsSolid','SetSource')) {
    $method = @($controllerTree.GetRoot().DescendantNodes() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name })
    if ($method.Count -ne 1) { throw "Controller method changed: $name" }
    $flowTests = $flowTests.Replace("// PRODUCTION_$name", $method[0].ToFullString())
}
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($flowTests))
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($library in Get-ChildItem $managed -Filter '*.dll') { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($library.FullName)) }
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('DreamDustChemistryTests', $trees, $refs, $options)
$stream = [IO.File]::Create((Join-Path $testRoot 'DreamDustChemistryTests.dll'))
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Chemistry test compilation failed.' }

# CLR resolves installed dependencies from the supplied directory; no game DLLs
# enter the mod package. Keep this host in a fresh PowerShell process per run.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class ChemistryTestHost {
    public static string Run(string managed, string tests, string suite) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(name == "Assembly-CSharp" ? tests : managed, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var assembly = Assembly.LoadFrom(Path.Combine(tests, "DreamDustChemistryTests.dll"));
        return (string)assembly.GetType(suite).GetMethod("Run", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null).Invoke(null, null);
    }
}
'@
try { [ChemistryTestHost]::Run($managed, $testRoot, $Suite) }
catch { Write-Output $_.Exception.ToString(); throw }
