param(
    [Parameter(Mandatory)][string]$RecordsPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$PerformancePath = 'Logs/Lv2PullChasePerformance.txt'
)
$ErrorActionPreference = 'Stop'
# These sibling scopes bound navigation CPU; CharacterMovePrepare also includes its caller setup.
$flowRoots = @('FlowGroupMove', 'FlowNavigationAgentUpdate',
    'CharacterMovePrepare', 'FlowNavigationCommit', 'FlowCombatApproach', 'CharacterMoveSteering')
$records = @(Import-Csv -LiteralPath $RecordsPath)
if (!$records.Count) { throw 'Scope capture is empty.' }
$performance = @(Get-Content -LiteralPath $PerformancePath)
if ($performance[0] -ne 'RESULT=PASS') { throw 'Fixed entry did not pass.' }
$hashLines = @($performance | Where-Object { $_ -match '^coldAuditRecordsSha256=' })
$sourceHash = (Get-FileHash -LiteralPath $RecordsPath).Hash
if ($hashLines.Count -ne 1 -or ($hashLines[0] -split '=', 2)[1] -ne $sourceHash) { throw 'Raw capture does not match the fixed-entry report hash.' }
$start = @($performance | Where-Object { $_ -match '^event name=approach-begin,render=\d+,logic=\d+,' })
if ($start.Count -ne 1 -or $start[0] -notmatch ',logic=(\d+),') { throw 'Chase window start is missing or ambiguous.' }
$startFrame = [ulong]$Matches[1]
$end = @($performance | Where-Object { $_ -match '^finalLogicFrame=\d+$' })
if ($end.Count -ne 1) { throw 'Capture final frame is missing or ambiguous.' }
$endFrame = [ulong]($end[0] -split '=', 2)[1]
if (!$records[0].PSObject.Properties['gcCollections']) { throw 'Capture has no GC observations.' }
$frequency = [long]$records[0].frequency
if ($frequency -le 0) { throw 'Invalid timer frequency.' }
$frames = @{}
$expectedSequence = 0
$previousFrame = 0ul
foreach ($record in $records) {
    if ([int]$record.sequence -ne $expectedSequence++) { throw 'Raw scope sequence is not contiguous.' }
    if ([long]$record.frequency -ne $frequency -or [long]$record.ticks -lt 0) { throw 'Invalid timing record.' }
    $key = [ulong]$record.logicFrame
    if ($key -lt $previousFrame -or $key -gt $endFrame) { throw 'Raw scope frames do not match the fixed entry window.' }
    $previousFrame = $key
    if (!$frames.ContainsKey($key)) {
        $frames[$key] = [pscustomobject]@{ Frame=$key; TickTicks=0L; TickCount=0; FlowTicks=0L; FirstGC=$record.gcCollections; LastGC=$record.gcCollections }
    }
    $frame = $frames[$key]
    $frame.LastGC = $record.gcCollections
    if ($record.scope -eq 'LogicFrameTick') { $frame.TickTicks += [long]$record.ticks; $frame.TickCount++ }
    if ($flowRoots -contains $record.scope) { $frame.FlowTicks += [long]$record.ticks }
}
$rows = @($frames.Values | Sort-Object Frame | ForEach-Object {
    if ($_.TickCount -ne 1 -or $_.FlowTicks -gt $_.TickTicks) { throw "Invalid Tick coverage at frame $($_.Frame)." }
    [pscustomobject]@{ Frame=$_.Frame; TickMs=$_.TickTicks*1000.0/$frequency; FlowUpperBoundMs=$_.FlowTicks*1000.0/$frequency; GCCollectionsBetweenScopeRecords=[int]$_.LastGC-[int]$_.FirstGC }
})
function Percentile99([double[]]$values) {
    $ordered = @($values | Sort-Object)
    return $ordered[[Math]::Ceiling($ordered.Count * 0.99)-1]
}
$window = @($rows | Where-Object Frame -ge $startFrame)
if (!$window.Count) { throw 'No completed Tick in chase window.' }
if ($window.Count -ne $endFrame - $startFrame + 1) { throw 'Chase window has missing completed Ticks.' }
for ($i = 0; $i -lt $window.Count; $i++) {
    if ($window[$i].Frame -ne $startFrame + $i) { throw 'Chase window frames are not contiguous.' }
}
$tickMax = $window | Sort-Object TickMs -Descending | Select-Object -First 1
$flowMax = $window | Sort-Object FlowUpperBoundMs -Descending | Select-Object -First 1
$tickP99 = Percentile99 $window.TickMs
$flowP99 = Percentile99 $window.FlowUpperBoundMs
$passed = $tickMax.TickMs -le 16.67 -and $tickP99 -le 8.33 -and $flowMax.FlowUpperBoundMs -le 8 -and $flowP99 -le 4
$report = [ordered]@{
    Status = $(if ($passed) { 'PASS' } else { 'FAIL' })
    SourceSha256 = $sourceHash
    Window = 'Fixed-entry approach-begin through capture end'
    StartFrame = $startFrame
    EndFrame = $endFrame
    PerformanceSha256 = (Get-FileHash -LiteralPath $PerformancePath).Hash
    TickCount = $window.Count
    StartupMaximum = $rows | Where-Object Frame -lt $startFrame | Sort-Object TickMs -Descending | Select-Object -First 1
    TickP99Ms = $tickP99
    TickMaximum = $tickMax
    FlowP99UpperBoundMs = $flowP99
    FlowMaximum = $flowMax
    FlowUpperBoundScopes = $flowRoots
    Thresholds = @{ TickP99Ms=8.33; TickMaxMs=16.67; FlowP99Ms=4; FlowMaxMs=8 }
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
$rows | Export-Csv -LiteralPath ($OutputPath + '.ticks.csv') -NoTypeInformation -Encoding utf8NoBOM
$report | ConvertTo-Json -Depth 6
if (!$passed) { throw 'Flow runtime performance gates are not satisfied.' }
