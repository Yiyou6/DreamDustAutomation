param([switch]$PreviewOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$coverSource = if (Test-Path (Join-Path $PSScriptRoot 'assets/workshop-cover.png')) { 'assets/workshop-cover.png' } else { 'assets/machine-original.png' }
$entries = @(
    @{Size=64;Path='package/Textures/Items/synthesizer.png';Source='assets/machine-original.png'},
    @{Size=512;Path='package/preview.png';Source=$coverSource}
)
foreach ($entry in $entries) {
    if ($PreviewOnly -and $entry.Path -ne 'package/preview.png') { continue }
    $assetSource = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot $entry.Source))
    try {
        $assetBitmap = [Drawing.Bitmap]::new($entry.Size, $entry.Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $assetGraphics = [Drawing.Graphics]::FromImage($assetBitmap)
        try {
            $assetGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $assetGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $assetGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            $assetGraphics.DrawImage($assetSource, [Drawing.Rectangle]::new(0,0,$entry.Size,$entry.Size), 0,0,$assetSource.Width,$assetSource.Height,[Drawing.GraphicsUnit]::Pixel)
            $assetBitmap.Save((Join-Path $PSScriptRoot $entry.Path), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $assetGraphics.Dispose(); $assetBitmap.Dispose() }
    } finally { $assetSource.Dispose() }
}
