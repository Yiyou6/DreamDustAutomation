$ErrorActionPreference = 'Stop'
$controller = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/MachineController.cs'))
function Read-Method([string]$signature) {
    $start = $controller.IndexOf($signature)
    if ($start -lt 0) { throw "Method missing: $signature" }
    $brace = $controller.IndexOf('{', $start); $end = $brace + 1; $depth = 1
    while ($depth -gt 0) {
        if ($controller[$end] -eq '{') { $depth++ }
        if ($controller[$end] -eq '}') { $depth-- }
        $end++
    }
    $controller.Substring($start, $end - $start)
}
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/NightCycleDispatchTests.cs'))
$fixture = $fixture.Replace('// PRODUCTION_CYCLE', (Read-Method 'private void Cycle()'))
$fixture = $fixture.Replace('// PRODUCTION_NIGHT', (Read-Method 'private void CompleteNightReactions()'))
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($fixture))
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework $name)))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('NightCycleDispatchTests', $trees, $refs, $options)
$exe = Join-Path $PSScriptRoot 'TestResults/NightCycleDispatchTests.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Night-cycle test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Night-cycle tests failed.' }
