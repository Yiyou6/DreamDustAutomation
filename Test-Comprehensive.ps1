param([ValidateSet('All','Chemistry','Interface','World')][string]$Group = 'All')
$ErrorActionPreference = 'Stop'
$results = Join-Path $PSScriptRoot 'TestResults/comprehensive'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$jobs = @()
if ($Group -in @('All','Chemistry')) {
    foreach ($suite in @('ChemistryRegressionTests','ExpandedControllerTests','PairwiseControllerTests','SaveMutationTests','MaterialCatalogTests','ProteinCleanupTests','OvernightFeedbackTests','SourceStabilityTests','ReactionEndpointTests','TargetGradeDilutionTests')) {
        $jobs += @{name=$suite;script='Test-Chemistry.ps1';arguments=@('-Suite',$suite)}
    }
}
if ($Group -in @('All','Interface')) {
    foreach ($name in @('UI','UIRecovery','Layout','DragVisual','OutputScheduling','OutputInventory','SearchLifecycle','Highlights','Feedback')) {
        $jobs += @{name=$name;script="Test-$name.ps1";arguments=@()}
    }
}
if ($Group -in @('All','World')) {
    foreach ($name in @('NightCycle','Power','Stock','Starter','Manifest')) {
        $jobs += @{name=$name;script="Test-$name.ps1";arguments=@()}
    }
    $jobs += @{name='GradePolicy';script='Test.ps1';arguments=@()}
}
$summary = @()
Push-Location $PSScriptRoot
try {
foreach ($job in $jobs) {
    $log = Join-Path $results ($job.name + '.log')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $arguments = $job.arguments
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot $job.script) @arguments *> $log
    $code = $LASTEXITCODE
    $summary += [ordered]@{suite=$job.name;passed=($code -eq 0);seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);log=$log}
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results "$Group.json") -Encoding utf8
    if ($code -ne 0) { Get-Content -LiteralPath $log -Tail 30; throw "Failed: $($job.name)" }
    Write-Output ("PASS " + $job.name + ' (' + $summary[-1].seconds + ' s)')
    Get-Content -LiteralPath $log -Tail 1
}
} finally { Pop-Location }
Write-Output 'Completed headless tests. Live UI hit testing, rendering/FPS and other-mod combinations require separate game verification.'
