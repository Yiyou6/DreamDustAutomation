$ErrorActionPreference = 'Stop'
function Native-Method([string]$path, [string]$signature) {
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $path))
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Native method missing: $signature" }
    $brace = $source.IndexOf('{', $start); $end = $brace + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$stockSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/PowerCompatibilityTests.cs'))
$stockSource = $stockSource.Replace('// NATIVE_RECHARGE', (Native-Method 'research/MachineHelper.cs' 'public static void RechargeMachineBattery('))
$stockSource = $stockSource.Replace('// NATIVE_CHARGE', (Native-Method 'research/PowerHelper.cs' 'public static void ChargeBattery(GameItem gameItem, int amount'))
$stockTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$stockTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($stockSource))
$stockTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/AdjacentCharging.cs'))))
$stockTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/ControllerRegistry.cs'))))
$stockRefs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$stockFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $stockRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $stockFramework $name)))
}
$stockOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$stockCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('PowerCompatibilityTests', $stockTrees, $stockRefs, $stockOptions)
$stockExe = Join-Path $PSScriptRoot 'TestResults/PowerCompatibilityTests.exe'
$stockStream = [IO.File]::Create($stockExe)
try { $stockResult = $stockCompilation.Emit($stockStream) } finally { $stockStream.Dispose() }
if (!$stockResult.Success) { $stockResult.Diagnostics | ForEach-Object ToString; throw 'Power test compilation failed.' }
& $stockExe
if ($LASTEXITCODE -ne 0) { throw 'Power compatibility tests failed.' }
