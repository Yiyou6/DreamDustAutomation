$ErrorActionPreference = 'Stop'
# Use the installed native sizing algorithm, with rendering-only test doubles.
# This checks numerical layout; it is not a screenshot or an in-game UI test.
function Native-Method([string]$file, [string]$signature) {
    $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "research/$file.cs"))
    $start=$source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing native method $file $signature" }
    $brace=$source.IndexOf('{',$start); $depth=1; $end=$brace+1
    while($depth -gt 0) { if($source[$end] -eq '{'){$depth++}; if($source[$end] -eq '}'){$depth--}; $end++ }
    return $source.Substring($start,$end-$start)
}
$fixture=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/LayoutSizingTests.cs'))
foreach($name in @('MachineryPanel','MachineryStack','MachineryTabs')) {
    $methods=(Native-Method $name 'public void Validate()')+"`n"+(Native-Method $name 'public void ResizePixels(int width, int height)')
    $fixture=$fixture.Replace("// NATIVE_$name",$methods)
}
$trees=[Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($fixture))
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineLayoutSizing.cs'))))
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineRegion.cs'))))
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineLayoutPass.cs'))))
$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineGeometry.cs'))))
$refs=[Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll')) {
 $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319" $name)))
}
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LayoutSizingTests',$trees,$refs,[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication))
$exe=Join-Path $PSScriptRoot 'TestResults/LayoutSizingTests.exe'
$stream=[IO.File]::Create($exe)
try{$result=$compilation.Emit($stream)}finally{$stream.Dispose()}
if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Layout fixture compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Layout sizing tests failed'}
