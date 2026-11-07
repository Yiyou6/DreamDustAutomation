$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$treeList = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($source in @('src/Planning/GradePolicy.cs','tests/GradePolicyTests.cs')) {
    $treeList.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText((Join-Path $root $source))))
}
$refs = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework $name)))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('DreamDustTests', $treeList, $refs, $options)
$testDir = Join-Path $root 'TestResults'
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
$exe = Join-Path $testDir 'DreamDustTests.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
