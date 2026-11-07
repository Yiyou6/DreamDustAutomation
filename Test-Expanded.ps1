$ErrorActionPreference = 'Stop'
$results = Join-Path $PSScriptRoot 'TestResults/expanded'
New-Item -ItemType Directory -Path $results -Force | Out-Null
# The chemistry runner shares one fixture DLL, so its suites run sequentially.
foreach ($suite in @('ExpandedControllerTests','ControllerFlowTests','LiveModuleTests')) {
    $log = Join-Path $results ($suite + '.log')
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-Chemistry.ps1') -Suite $suite *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 30; throw "Failed: $suite" }
    Get-Content -LiteralPath $log -Tail 1
}
$outputLog = Join-Path $results 'output-boundaries.log'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-OutputScheduling.ps1') *> $outputLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $outputLog -Tail 30; throw 'Output boundary tests failed' }
Get-Content -LiteralPath $outputLog -Tail 2
Write-Output 'Expanded tests passed. These are headless execution/chemistry tests, not live game rendering or FPS measurements.'
