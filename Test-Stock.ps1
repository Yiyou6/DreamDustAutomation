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
$stockSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/MachineStockTests.cs'))
$stockSource = $stockSource.Replace('// NATIVE_GET_OWNED', (Native-Method 'research/EmporiumEntry.cs' 'public List<GameItem> GetAllOwnedItems('))
$stockSource = $stockSource.Replace('// NATIVE_FIND_ALL', (Native-Method 'research/GraphUtils.cs' 'public static List<T> FindAllChildrenType<T>('))
$stockSource = $stockSource.Replace('// NATIVE_IS_OWNED', (Native-Method 'research/GeneralHelper.cs' 'public static bool IsItemOwned(GameItem item)'))
# Both ordinary visits (including the player's shop) and the special store use
# this exact early hook before stock placement, with the native owned snapshot.
foreach ($signature in @('public static void PlaceInventorInventory(', 'public static void PlaceInventorSpecialStock(')) {
    $native = Native-Method 'research/StoreClientList.cs' $signature
    if ($native -notmatch 'GetAllOwnedItems\(\);\s*(?:HashSet<string>[\s\S]*?)?ModHook.FireOnPlaceInventorInventoryItemEarly\(allOwnedItems\);') {
        throw "Native stock entry contract changed: $signature"
    }
}
$stockTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$stockTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($stockSource))
$stockTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/ModEntry.cs'))))
$stockRefs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$stockFramework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $stockRefs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $stockFramework $name)))
}
$stockOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication)
$stockCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('MachineStockTests', $stockTrees, $stockRefs, $stockOptions)
$stockExe = Join-Path $PSScriptRoot 'TestResults/MachineStockTests.exe'
$stockStream = [IO.File]::Create($stockExe)
try { $stockResult = $stockCompilation.Emit($stockStream) } finally { $stockStream.Dispose() }
if (!$stockResult.Success) { $stockResult.Diagnostics | ForEach-Object ToString; throw 'Stock test compilation failed.' }
& $stockExe
if ($LASTEXITCODE -ne 0) { throw 'Machine stock tests failed.' }
Write-Output 'Native ordinary/special shop hook contracts verified.'
