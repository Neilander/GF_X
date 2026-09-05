param(
    [Parameter(Mandatory)][string]$RecordsPath,
    [Parameter(Mandatory)][ulong]$LogicFrame,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$records = @(Import-Csv -LiteralPath $RecordsPath | Where-Object { [ulong]$_.logicFrame -eq $LogicFrame } | ForEach-Object {
    [pscustomobject]@{
        Sequence = [int]$_.sequence
        Scope = [string]$_.scope
        Ticks = [long]$_.ticks
        EndTicks = [long]$_.recordedAt
        StartTicks = ([long]$_.recordedAt - [long]$_.ticks)
        Frequency = [long]$_.frequency
    }
})
if (!$records.Count) { throw "No scope records for logic frame $LogicFrame." }

# These scopes are inclusive totals or diagnostic mirrors and must not be counted as work leaves.
$aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
@(
    'FlowNavigationPathAdvance', 'FlowNavigationPathSliceDispatchUnattributed',
    'FlowNavigationGoalConnectorUnattributed', 'FlowNavigationPathQueueProcessingUnattributed',
    'FlowNavigationDemandPathValidation', 'FlowCombatApproachOccupancyPrepareSlotLookup',
    'FlowCombatApproachOccupancyBucketIncremental', 'FlowCombatApproachCoreScoreUnattributed',
    'FlowCombatApproachCoreUnattributed', 'LogicFrameListenerUnattributed'
) | ForEach-Object { [void]$aliases.Add($_) }

# Records with identical intervals are aggregate mirrors. The first scope wins as the owner.
$duplicateOwner = @{}
for ($i = 0; $i -lt $records.Count; $i++) {
    $r = $records[$i]
    $key = "$($r.StartTicks):$($r.EndTicks)"
    if (!$duplicateOwner.ContainsKey($key)) { $duplicateOwner[$key] = $i }
}

$parentIndex = [int[]]::new($records.Count)
$childrenTicks = [long[]]::new($records.Count)
[Array]::Fill($parentIndex, -1)
$issues = [Collections.Generic.List[string]]::new()
for ($child = 0; $child -lt $records.Count; $child++) {
    $c = $records[$child]
    if ($aliases.Contains($c.Scope)) { continue }
    $best = -1
    $bestDuration = [long]::MaxValue
    for ($container = 0; $container -lt $records.Count; $container++) {
        if ($container -eq $child) { continue }
        $p = $records[$container]
        if ($p.StartTicks -gt $c.StartTicks -or $p.EndTicks -lt $c.EndTicks) { continue }
        if ($p.Ticks -le $c.Ticks) { continue }
        if ($p.StartTicks -eq $c.StartTicks -and $p.EndTicks -eq $c.EndTicks) { continue }
        if ($p.Ticks -lt $bestDuration) {
            $best = $container
            $bestDuration = $p.Ticks
        }
    }
    $parentIndex[$child] = $best
    if ($best -ge 0) { $childrenTicks[$best] += $c.Ticks }
}

$rows = [Collections.Generic.List[object]]::new()
$scopeTotals = @{}
$scopeCalls = @{}
$exclusiveSum = 0L
for ($i = 0; $i -lt $records.Count; $i++) {
    $r = $records[$i]
    if ($aliases.Contains($r.Scope)) { continue }
    $exclusive = $r.Ticks - $childrenTicks[$i]
    if ($exclusive -lt 0) {
        $issues.Add("Negative exclusive interval: sequence=$($r.Sequence),scope=$($r.Scope),ticks=$($r.Ticks),children=$($childrenTicks[$i])")
        continue
    }
    $isLeaf = $childrenTicks[$i] -eq 0
    if (!$isLeaf) { continue }
    $exclusiveSum += $exclusive
    if (!$scopeTotals.ContainsKey($r.Scope)) { $scopeTotals[$r.Scope] = 0L; $scopeCalls[$r.Scope] = 0 }
    $scopeTotals[$r.Scope] += $exclusive
    $scopeCalls[$r.Scope]++
    $rows.Add([pscustomobject]@{
        sequence=$r.Sequence; scope=$r.Scope; startTicks=$r.StartTicks; endTicks=$r.EndTicks
        inclusiveTicks=$r.Ticks; childTicks=$childrenTicks[$i]; exclusiveTicks=$exclusive
    })
}

$frequency = $records[0].Frequency
$terms = @($scopeTotals.GetEnumerator() | ForEach-Object {
    [pscustomobject]@{
        scope=$_.Key
        contributionMs=([double]$_.Value * 1000.0 / $frequency)
        callCount=[int]$scopeCalls[$_.Key]
    }
} | Sort-Object contributionMs -Descending)
$logicTick = @($records | Where-Object Scope -eq 'LogicFrameTick' | Select-Object -First 1)
$result = [ordered]@{
    status = 'INTERVAL_EXCLUSIVE_AUDIT'
    sourceSha256 = (Get-FileHash -LiteralPath $RecordsPath -Algorithm SHA256).Hash
    logicFrame = $LogicFrame
    recordCount = $records.Count
    exclusiveLeafInvocations = $rows.Count
    exclusiveContributionMs = [double]$exclusiveSum * 1000.0 / $frequency
    logicTickInclusiveMs = $(if ($logicTick) { [double]$logicTick.Ticks * 1000.0 / $frequency } else { $null })
    residualMs = $(if ($logicTick) { ([double]$exclusiveSum - [double]$logicTick.Ticks) * 1000.0 / $frequency } else { $null })
    issues = @($issues)
    terms = $terms
    invocations = @($rows)
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
[pscustomobject]@{ Records=$records.Count; ExclusiveLeafInvocations=$rows.Count; Terms=$terms.Count; Issues=$issues.Count; Output=$OutputPath; ResidualMs=$result.residualMs }
