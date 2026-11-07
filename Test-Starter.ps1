$ErrorActionPreference = 'Stop'
function Native-DelegateBody([string]$path, [string]$anchor) {
    $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $path))
    $start=$source.IndexOf($anchor)
    if($start -lt 0){throw "Native reward/sale anchor missing: $anchor"}
    $start=$source.IndexOf('delegate',$start)
    $brace=$source.IndexOf('{',$start);$depth=1;$end=$brace+1
    while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
    return $source.Substring($brace+1,$end-$brace-2)
}
$taskTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($taskFile in @('src/ChemistStarter.cs','src/RetiredChemistGift.cs','tests/ChemistStarterTests.cs','tests/RetiredChemistGiftTests.cs')) {
    $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $taskFile))
    if($taskFile -eq 'tests/RetiredChemistGiftTests.cs'){
        $source=$source.Replace('// NATIVE_GIFT',(Native-DelegateBody 'research/NetworkUpgradeList.cs' '"RETIRED_CHEMIST",'))
        $source=$source.Replace('// NATIVE_WEEKLY',(Native-DelegateBody 'research/StoreClientListSpec.cs' 'public static StoreClient CreateRetiredChemist()'))
    }
    $taskTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source))
}
$taskRefs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($taskName in @('mscorlib.dll','System.dll','System.Core.dll')) {
    $taskRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $taskFramework $taskName)))
}
$taskOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$taskCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('ChemistStarterTests', $taskTrees, $taskRefs, $taskOptions)
$taskExe = Join-Path $PSScriptRoot 'TestResults/ChemistStarterTests.exe'
$taskStream = [IO.File]::Create($taskExe)
try { $taskResult = $taskCompilation.Emit($taskStream) } finally { $taskStream.Dispose() }
if (!$taskResult.Success) { $taskResult.Diagnostics | ForEach-Object ToString; throw 'UI test compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Chemist starter tests failed.' }
