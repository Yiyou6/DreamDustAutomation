$ErrorActionPreference = 'Stop'
$code = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/MachineController.cs'))
$start = $code.IndexOf('private IEnumerator FormProducts(RunningBatch batch)')
$end = $code.IndexOf('private bool FormOne(', $start)
if ($start -lt 0 -or $end -lt $start) { throw 'Production output coroutine moved; inspect before testing' }
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/OutputSchedulingTests.cs')).Replace('// PRODUCTION_FORM_PRODUCTS', $code.Substring($start, $end - $start))
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source))
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework $name)))
}
$compiler = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('OutputSchedulingTests', $trees, $refs,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication))
$exe = Join-Path $PSScriptRoot 'TestResults/OutputSchedulingTests.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compiler.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Output scheduling test compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Output scheduling tests failed' }
