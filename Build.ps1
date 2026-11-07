param([switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$gameRoot = (Resolve-Path (Join-Path $projectRoot '../..')).Path
$managed = Join-Path $gameRoot 'Probably Stolen_Data/Managed'
$sources = @(Get-ChildItem (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse)
if ($sources.Count -eq 0) { throw 'No mod source files exist yet.' }

# PowerShell 7 ships Roslyn. Compile against the installed Unity runtime,
# never against the host runtime, and never redistribute game assemblies.
$trees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest)
foreach ($source in $sources) {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [IO.File]::ReadAllText($source.FullName), $parseOptions, $source.FullName))
}
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($library in Get-ChildItem $managed -Filter '*.dll') {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($library.FullName))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel(
    [Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'DreamDustAutomation', $trees, $references, $options)
$buffer = [IO.MemoryStream]::new()
try {
    $result = $compilation.Emit($buffer)
    foreach ($diagnostic in $result.Diagnostics) {
        if ($diagnostic.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning) {
            Write-Output $diagnostic.ToString()
        }
    }
    if (!$result.Success) { throw 'Compilation failed; installed files have not been changed.' }
    $packageRoot = Join-Path $projectRoot 'dist/DreamDustAutomation'
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $packageRoot 'DreamDustAutomation.dll'), $buffer.ToArray())
    if (Test-Path (Join-Path $projectRoot 'package')) {
        Get-ChildItem (Join-Path $projectRoot 'package') | Copy-Item -Destination $packageRoot -Recurse -Force
    }
    & (Join-Path $projectRoot 'Test-Manifest.ps1') -Package $packageRoot | Out-Null
    Write-Output 'Native mod-scan passed: declared capabilities cover the package.'
    if ($Install) {
        if (!(Test-Path (Join-Path $packageRoot 'manifest.xml'))) { throw 'Package has no manifest.xml; installation refused.' }
        $destination = Join-Path $gameRoot 'Mods/DreamDustAutomation'
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Get-ChildItem $packageRoot | Copy-Item -Destination $destination -Recurse -Force
        Write-Output "Installed: $destination"
    }
    Write-Output "Built: $packageRoot"
} finally {
    $buffer.Dispose()
}
