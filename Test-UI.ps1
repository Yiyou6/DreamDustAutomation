$ErrorActionPreference = 'Stop'
$taskTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($taskFile in @('src/UI/DeferredPanelRow.cs','src/UI/MachineLayoutPass.cs','tests/UiBatchingTests.cs')) {
    $taskTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot $taskFile))))
}
$taskRefs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($taskName in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $taskRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $taskFramework $taskName)))
}
$taskOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$taskCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('UiBatchingTests', $taskTrees, $taskRefs, $taskOptions)
$taskExe = Join-Path $PSScriptRoot 'TestResults/UiBatchingTests.exe'
$taskStream = [IO.File]::Create($taskExe)
try { $taskResult = $taskCompilation.Emit($taskStream) } finally { $taskStream.Dispose() }
if (!$taskResult.Success) { $taskResult.Diagnostics | ForEach-Object ToString; throw 'UI test compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'UI batching tests failed.' }
