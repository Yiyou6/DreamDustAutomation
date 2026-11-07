$ErrorActionPreference = 'Stop'
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/DragVisualTests.cs')) + "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/InventoryVisualTests.cs'))
$native = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'research/GameItemElement.cs'))
foreach ($name in @('UpdateRectTransform','GetCenteredPosition','SetCenteredPosition')) {
    $sig = switch ($name) { 'UpdateRectTransform' { 'public void UpdateRectTransform()' }; 'GetCenteredPosition' { 'public (float, float) GetCenteredPosition()' }; 'SetCenteredPosition' { 'public void SetCenteredPosition(float x, float y)' } }
    $start = $native.IndexOf($sig); if ($start -lt 0) { throw "Missing native $name" }
    $brace=$native.IndexOf('{',$start); $depth=1; $end=$brace+1
    while($depth -gt 0) { if($native[$end] -eq '{'){$depth++}; if($native[$end] -eq '}'){$depth--}; $end++ }
    $fixture=$fixture.Replace("// NATIVE_$name",$native.Substring($start,$end-$start))
}
foreach ($spec in @(
    @('TreeNodeRender','TreeSetParent','public TreeNodeRender SetParent(TreeNodeRender parent, bool worldPositionStays)'),
    @('TreeNodeRender','ResetParent','public TreeNodeRender ResetParent()'),
    @('PixelElement','SetRenderingParent','bool SetRenderingParent(PixelElement parent)'),
    @('PixelElement','IsRenderingChildOf','bool IsRenderingChildOf(PixelElement parent)'),
    @('PixelElement','RemoveRenderingParent','bool RemoveRenderingParent(PixelElement parent)'),
    @('GameGridScrollableInventory','UncheckedAccept','public override bool UncheckedAccept(GameItem item)'),
    @('GameGridScrollableInventory','Expel','public override bool Expel(GameItem item)')
)) {
    $native=[IO.File]::ReadAllText((Join-Path $PSScriptRoot ('research/'+$spec[0]+'.cs')))
    $start=$native.IndexOf($spec[2]);if($start -lt 0){throw "Missing native $($spec[1])"}
    $brace=$native.IndexOf('{',$start);$depth=1;$end=$brace+1
    while($depth -gt 0){if($native[$end] -eq '{'){$depth++};if($native[$end] -eq '}'){$depth--};$end++}
    $fixture=$fixture.Replace("// NATIVE_$($spec[1])",$native.Substring($start,$end-$start))
}
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineDragVisual.cs')) + "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/UI/MachineInventoryVisuals.cs'))
Add-Type -TypeDefinition ($fixture + "`n" + $source.Replace('using System;', '').Replace('using UnityEngine;', '')) -Language CSharp -CompilerOptions /nowarn:0649
[DragVisualTests]::Main()
