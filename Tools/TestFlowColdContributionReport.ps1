param(
    [Parameter(Mandatory)][string]$PartitionPath,
    [Parameter(Mandatory)][string]$RecordsPath,
    [Parameter(Mandatory)][string]$SamplesPath,
    [string]$PerformancePath = 'Logs/Lv2PullChasePerformance.txt'
)
$ErrorActionPreference = 'Stop'
$partition = Get-Content -Raw -LiteralPath $PartitionPath | ConvertFrom-Json
$terms = @(Get-Content -Raw -LiteralPath $SamplesPath | ConvertFrom-Json)
$raw = @(Import-Csv -LiteralPath $RecordsPath)
$performance = @(Get-Content -LiteralPath $PerformancePath -TotalCount 32)
if ($performance[0] -ne 'RESULT=PASS') { throw 'Fixed entry did not pass' }
$reportedFrame = [ulong](($performance | Where-Object { $_ -like 'maxLogicTickLogicFrame=*' }) -split '=',2)[1]
$reportedMs = [double]::Parse((($performance | Where-Object { $_ -like 'maxLogicTickMs=*' }) -split '=',2)[1], [Globalization.CultureInfo]::InvariantCulture)
if ($reportedFrame -ne $partition.LogicFrame) { throw 'Report uses a different maximum Tick' }
if ((Get-FileHash -LiteralPath $RecordsPath -Algorithm SHA256).Hash -ne $partition.SourceSha256) { throw 'Source hash mismatch' }
$rawBySequence = @{}
$firstFrames = @{}
foreach ($row in $raw) {
    $rawBySequence[[int]$row.sequence] = $row
    if (!$firstFrames.ContainsKey($row.scope)) { $firstFrames[$row.scope] = [ulong]$row.logicFrame }
}
$bySequence = @{}
foreach ($row in $partition.Records) {
    if ($bySequence.ContainsKey($row.Sequence)) { throw 'Duplicate partition record' }
    if ($row.OwnTicks -lt 0) { throw 'Negative exclusive ticks' }
    if ($row.OwnTicks + $row.ChildrenTicks -ne $row.InclusiveTicks) { throw 'Local decomposition mismatch' }
    if ($row.InclusiveTicks -ne [long]$rawBySequence[[int]$row.Sequence].ticks) { throw 'Raw record mismatch' }
    $bySequence[$row.Sequence] = $row
}
$root = @($partition.Records | Where-Object ParentSequence -eq -1)
if ($root.Count -ne 1 -or $root[0].Scope -ne 'LogicFrameTick') { throw 'Partition has missing or multiple roots' }
foreach ($row in $partition.Records) {
    $seen = [Collections.Generic.HashSet[int]]::new()
    $cursor = $row
    while ($cursor.ParentSequence -ne -1) {
        if (!$seen.Add($cursor.Sequence)) { throw 'Cycle in scope ownership' }
        if (!$bySequence.ContainsKey($cursor.ParentSequence)) { throw 'Parent outside target Tick' }
        $cursor = $bySequence[$cursor.ParentSequence]
    }
    if ($cursor.Sequence -ne $root[0].Sequence) { throw 'Record is not owned by the target Tick' }
}
$sum = ($partition.Records | Measure-Object OwnTicks -Sum).Sum
if ($sum -ne $root[0].InclusiveTicks) { throw 'Target Tick total mismatch' }
$termByName = @{}
foreach ($term in $terms) {
    if ($termByName.ContainsKey($term.Name)) { throw 'Duplicate report term' }
    $termByName[$term.Name] = $term
}
foreach ($group in $partition.Records | Group-Object Scope) {
    $name = $group.Name + '/self'
    if (!$termByName.ContainsKey($name)) { throw "Missing exclusive term $name" }
    $term = $termByName[$name]
    $ms = ($group.Group | Measure-Object OwnTicks -Sum).Sum * 1000.0 / $partition.Frequency
    if ([Math]::Abs($ms - $term.MaxTickMs) -gt 1e-9) { throw "Incorrect current milliseconds for $name" }
    if ($ms -le .1) { continue }
    if (!$term.HotSamples.Count) { throw "Missing hot samples for $name" }
    $first = $group.Group | Sort-Object Sequence | Select-Object -First 1
    $hotTotal = 0L
    foreach ($sample in $term.HotSamples) {
        if ($sample.Sequence -le $first.Sequence) { throw 'Hot sample precedes first target record' }
        if ($sample.LogicFrame -eq $partition.LogicFrame) { $owner = $partition }
        else { $owner = $partition.HotFrames | Where-Object LogicFrame -eq $sample.LogicFrame }
        if (!$owner -or $owner.Issues.Count -or @($owner.UnmappedTicks.psobject.Properties).Count) { throw 'Hot sample comes from invalid partition' }
        $actual = $owner.Records | Where-Object Sequence -eq $sample.Sequence
        if (!$actual -or $actual.Scope -ne $group.Name -or $actual.OwnTicks -ne $sample.OwnTicks) { throw 'Hot sample mismatch' }
        $hotTotal += $sample.OwnTicks
    }
    $expectedHot = $hotTotal * 1000.0 / $partition.Frequency / $term.HotSamples.Count
    if ([Math]::Abs($expectedHot - $term.HotAverageMs) -gt 1e-9) { throw 'Hot mean mismatch' }
    $cold = if ($firstFrames[$group.Name] -eq $partition.LogicFrame) { $first.OwnTicks * 1000.0 / $partition.Frequency - $expectedHot } else { 0.0 }
    if ([Math]::Abs($cold - $term.ColdContributionMs) -gt 1e-9) { throw 'Cold contribution mismatch' }
}
if ($terms.Count -ne @($partition.Records | Group-Object Scope).Count) { throw 'Unexpected report terms' }
$measured = @($terms | Where-Object MaxTickMs -gt .1)
$coldTotal = ($measured | Measure-Object ColdContributionMs -Sum).Sum
$tickMs = $sum * 1000.0 / $partition.Frequency
if ([Math]::Abs($tickMs - $reportedMs) -gt .000501) { throw 'Raw Tick does not match fixed-entry maximum' }
$rate = $coldTotal / $tickMs
if ([Math]::Abs($tickMs * $rate - $coldTotal) -gt 1e-9) { throw 'Contribution rate equation mismatch' }
[pscustomobject]@{ Result='PASS'; TargetFrame=$partition.LogicFrame; ExclusiveTerms=$terms.Count; MeasuredTerms=$measured.Count; TickMs=$tickMs; ColdEstimateMs=$coldTotal; Rate=$rate; Coverage='tree+raw+per-term+sample-provenance+sum'; Limitation='statistical first-occurrence estimate; business-work matching not proven' }
