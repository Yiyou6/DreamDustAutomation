$ErrorActionPreference = 'Stop'
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($file in @('src/UI/PlannedInputHighlights.cs','tests/PlannedInputHighlightTests.cs')) {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot $file))))
}
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319" $name)))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('PlannedInputHighlightTests', $trees, $refs, $options)
$exe = Join-Path $PSScriptRoot 'TestResults/PlannedInputHighlightTests.exe'
$stream = [IO.File]::Create($exe)
try { $result=$compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Highlight test compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Highlight tests failed' }
