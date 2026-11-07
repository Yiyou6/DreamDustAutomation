$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/MachineController.cs'))
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source)
$method = @($tree.GetRoot().DescendantNodes() | Where-Object { $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'GuardSearch' })
if ($method.Count -ne 1) { throw 'GuardSearch method changed' }
foreach ($call in @('GuardSearch(generation, Search(generation, new BatchPlanner(previewInputs)))','GuardSearch(generation, SearchQuantity(generation, count))')) {
    if (!$source.Contains($call)) { throw "Search path bypasses lifecycle guard: $call" }
}
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/SearchLifecycleTests.cs')).Replace('// PRODUCTION_GuardSearch', $method[0].ToFullString())
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($fixture))
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $framework $name)))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SearchLifecycleTests', $trees, $refs, $options)
$exe = Join-Path $PSScriptRoot 'TestResults/SearchLifecycleTests.exe'
$stream = [IO.File]::Create($exe)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Search lifecycle compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Search lifecycle tests failed.' }
