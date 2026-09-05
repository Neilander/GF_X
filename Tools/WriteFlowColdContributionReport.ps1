param(
    [Parameter(Mandatory)][string]$PartitionPath,
    [Parameter(Mandatory)][string]$RecordsPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
function Ms([double]$ticks) { return $ticks * 1000.0 / $partition.Frequency }
function Number([double]$value) { return $value.ToString('F6', $culture) }
$partition = Get-Content -Raw -LiteralPath $PartitionPath | ConvertFrom-Json
if ($partition.Issues.Count -or @($partition.UnmappedTicks.psobject.Properties).Count) {
    throw 'Target partition has unresolved ownership issues.'
}
$hash = (Get-FileHash -LiteralPath $RecordsPath -Algorithm SHA256).Hash
if ($hash -ne $partition.SourceSha256) { throw 'Partition and raw capture hashes differ.' }
$raw = @(Import-Csv -LiteralPath $RecordsPath)
$firstFrames = @{}
foreach ($record in $raw) {
    if (!$firstFrames.ContainsKey($record.scope)) { $firstFrames[$record.scope] = [ulong]$record.logicFrame }
}
$validHotFrames = @($partition.HotFrames | Where-Object {
    !$_.Issues.Count -and !@($_.UnmappedTicks.psobject.Properties).Count
} | Sort-Object LogicFrame)
$targetRecords = @($partition.Records | Sort-Object Sequence)
$totalTicks = ($targetRecords | Measure-Object OwnTicks -Sum).Sum
$tick = @($targetRecords | Where-Object Scope -eq 'LogicFrameTick')
if ($tick.Count -ne 1 -or $totalTicks -ne $tick[0].InclusiveTicks) {
    throw 'Exclusive partition does not equal the measured logic Tick.'
}
$groups = @{}
foreach ($group in $targetRecords | Group-Object Scope) { $groups[$group.Name] = @($group.Group) }
$terms = [Collections.Generic.List[object]]::new()
foreach ($term in $partition.Terms) {
    $samples = $groups[$term.Scope]
    $first = $samples[0]
    $hot = @()
    $source = 'not-measured-below-threshold'
    if ($term.OwnMs -gt 0.1) {
        if ($samples.Count -gt 1) {
            $hot = @($samples | Select-Object -Skip 1)
            $source = 'later-records-in-target-tick'
        } else {
            $hot = @($validHotFrames.Records | Where-Object Scope -eq $term.Scope | Sort-Object LogicFrame,Sequence)
            $source = 'later-records-in-validated-ticks'
        }
        if (!$hot.Count) { throw "No hot samples for $($term.Scope)" }
    }
    $hotTicks = if ($hot.Count) { ($hot | Measure-Object OwnTicks -Average).Average } else { $null }
    $terms.Add([pscustomobject]@{
        Name = $term.Scope + '/self'
        MaxTickMs = $term.OwnMs
        FirstRecordLogicFrame = $firstFrames[$term.Scope]
        FirstInMaxTick = $firstFrames[$term.Scope] -eq $partition.LogicFrame
        FirstInTickSequence = $first.Sequence
        FirstInTickMs = Ms $first.OwnTicks
        HotAverageMs = $(if ($hot.Count) { Ms $hotTicks } else { $null })
        FirstMinusHotMs = $(if ($hot.Count) { Ms ($first.OwnTicks - $hotTicks) } else { $null })
        ColdContributionMs = $(if ($hot.Count) { if ($firstFrames[$term.Scope] -eq $partition.LogicFrame) { Ms ($first.OwnTicks - $hotTicks) } else { 0.0 } } else { $null })
        HotSource = $source
        HotSamples = @($hot | Select-Object Sequence,LogicFrame,OwnTicks)
    })
}
$measured = @($terms | Where-Object MaxTickMs -gt 0.1)
$gap = ($measured | Measure-Object FirstMinusHotMs -Sum).Sum
$cold = ($measured | Measure-Object ColdContributionMs -Sum).Sum
$tickMs = Ms $totalTicks
$rate = $cold / $tickMs
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Flow Cold Contribution Audit')
$lines.Add('')
$lines.Add('Status: FIRST_OCCURRENCE_COLD_ESTIMATE; finest-boundary review and acceptance remain OPEN.')
$lines.Add("Source SHA256: $hash")
$lines.Add("Logic frame: $($partition.LogicFrame); total Tick: $(Number $tickMs) ms; exclusive residual: 0 ticks.")
$lines.Add("Exclusive terms: $($terms.Count); measured >0.1ms: $($measured.Count). No historical item-count limit.")
$lines.Add('')
$lines.Add('Each /self term is inclusive duration minus the direct children of that invocation. Parent totals and alias records are excluded. FirstInTick is the first observed invocation in the target Tick, not necessarily the first invocation in the run. Hot is the mean of subsequent exclusive invocations. ColdContribution equals FirstInTick minus Hot only when that scope first occurred in the target Tick; otherwise it is zero. This is an empirical first-occurrence estimate, not proof of JIT causation; differing branch work can contribute to the difference. Terms <=0.1ms have no inferred cold value.')
$lines.Add('')
$lines.Add('| Exclusive term | Tick ms | First record frame | First in max Tick | First in Tick ms | Hot ms | First-hot ms | Cold contribution ms | Hot samples |')
$lines.Add('| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |')
foreach ($term in $terms) {
    $hot = if ($null -ne $term.HotAverageMs) { Number $term.HotAverageMs } else { 'not measured' }
    $delta = if ($null -ne $term.FirstMinusHotMs) { Number $term.FirstMinusHotMs } else { 'not measured' }
    $coldValue = if ($null -ne $term.ColdContributionMs) { Number $term.ColdContributionMs } else { 'not measured' }
    $sampleFrames = @($term.HotSamples.LogicFrame | Sort-Object -Unique) -join ','
    $lines.Add("| $($term.Name) | $(Number $term.MaxTickMs) | $($term.FirstRecordLogicFrame) | $($term.FirstInMaxTick) | $(Number $term.FirstInTickMs) | $hot | $delta | $coldValue | n=$($term.HotSamples.Count); frames=$sampleFrames |")
}
$lines.Add('')
$lines.Add('Measured >0.1ms first-occurrence cold estimate (ms; <=0.1ms cold contributions are not measured):')
$lines.Add('')
$lines.Add("$(Number $tickMs) * $($rate.ToString('F12',$culture)) = $(Number $cold) ms = " + (($measured | ForEach-Object { "$($_.Name)($(Number $_.ColdContributionMs))" }) -join ' + '))
$lines.Add('')
$lines.Add('14.66 / 14.71 coverage:')
$lines.Add('')
$historical = [ordered]@{
    DemandResolve='FlowNavigationDemandResolve'; OccupancyPrepareIteration='FlowCombatApproachOccupancyPrepareIteration'
    PathSliceGoalConnector='FlowNavigationPathSliceGoalConnector'; 'PathQueue未覆盖差额'='FlowNavigationResolvePathQueueBatch/self'
    SlotCacheBuild='FlowCombatApproachSlotCacheBuild'; DemandDispatch='FlowNavigationDemandDispatch'
    MoveIntent='LogicEntityMoveIntent'; PathSliceInitialize='FlowNavigationPathSliceInitialize'
    'CombatCore未覆盖差额'='FlowCombatApproachCore/self'; 'OccupancyPreparation未覆盖差额'='FlowCombatApproachOccupancyPreparation/self'
    OccupancyBucketFill='FlowCombatApproachOccupancyBucketFill'; 'EntityBrainCombat未覆盖差额'='EntityBrainCombat/self'
    MoveResolve='LogicEntityMoveResolve'; CombatApproachPreResolve='FlowCombatApproachPreResolve'
    'PathSliceDispatch未覆盖差额'='FlowNavigationPathSliceDispatch/self'; 'CombatScorePhase未覆盖差额'='FlowCombatApproachCoreScorePhase/self'
    CombatScore='FlowCombatApproachScoreArithmetic'; 'NavigationSync未覆盖差额'='LogicEntityNavigationSync/self'
    SlotCachePublish='FlowCombatApproachSlotCachePublish'; SlotCacheLookup='FlowCombatApproachSlotCacheLookup'
    Targeting='LogicEntityTargeting'; 'SlotCache未覆盖差额'='FlowCombatApproachSlotCache/self'
    'CombatApproach未覆盖差额'='FlowCombatApproach/self'; 'Occupancy判定'='FlowCombatApproachOccupancy'
    'ResolveRequests未覆盖差额'='FlowNavigationResolveRequests/self'; CombatApproachFinalize='FlowCombatApproachFinalize'
    'DemandBatch未覆盖差额'='FlowNavigationResolveDemandBatch/self'; CombatCoreSetup='FlowCombatApproachCoreSetup'
    Snapshot='listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener'; SlotCacheArrayMaterialize='FlowCombatApproachSlotCacheArrayMaterialize'
    'Brain未覆盖差额'='LogicEntityBrain/self'; 'NavigationCommit未覆盖差额'='FlowNavigationCommit/self'
    BaseAndBuffs='LogicEntityBaseAndBuffs'; 'CombatScore未覆盖差额'='FlowCombatApproachScore/self'
    'PathSlice内部GoalConnector'='FlowNavigationGoalConnectorLink'; Interaction='listener:LogicInteractionAuthorityService+AuthorityListener'
    CombatCoreAgentPreparation='FlowCombatApproachCoreAgentPreparation'; FrameSetup='LogicEntityFrameSetup'
    NavigationAgentUpdate='FlowNavigationAgentUpdate'; MoveCommit='LogicEntityMoveCommit'; Attack='LogicEntityAttack'
    'Listener/runtime未覆盖差额'='LogicFrameTick/self + LogicFrameListenerCallbacks/self + listener:MAEntityLogicFrameSystem+PhaseListener/self'
    CombatCoreUnattributed='FlowCombatApproachCore/self'; PostUpdate='LogicEntityPostUpdate'; GroupMove='FlowGroupMove'
    NavigationInactiveClear='FlowNavigationInactiveClear'; CombatCoreDirectionSetup='FlowCombatApproachDirectionSetup'
    PathSliceExpandHierarchy='FlowNavigationPathSliceExpandHierarchy'; Projectile='LogicEntityProjectile'
    DamageResolve='LogicEntityDamageResolve'; PathSliceDownward='FlowNavigationPathSliceDownward'
    Tutorial='listener:TutorialManager'; CombatCoreExpanded='FlowCombatApproachCoreExpandedPhase'
}
$lines.Add('Historical names -> current source scopes (traceability only, not additional addends; inclusive sources expand to their /self and descendant terms above):')
foreach ($entry in $historical.GetEnumerator()) {
    $lines.Add("- $($entry.Key) -> $($entry.Value)")
}
$lines.Add('The historical line actually lists 53 names, including overlapping SlotCacheBuild/array/publication and Core residual labels; no names are silently dropped to reach 51. Old residual labels are tracked to the owning scope, and their former numeric boundaries are superseded by the current per-invocation partition.')
$lines.Add('')
$lines.Add('- Historical scopes are traced by their actual ownership paths, not a fixed 51-item count. NavigationSync contains CharacterMovePrepare and Commit; MoveIntent contains CharacterMoveSteering. SnapshotCaptureListener, Interaction AuthorityListener, GroupMoveManager and TutorialManager are retained as independent listeners under the Tick root.')
$lines.Add('- DemandResolve expands through FlowPrepareStableGoal -> Body, Prologue (including MeasurementBegin), MeasurementFinalize, and the call-boundary remainder. Body expands through input/classification/batch lookup, anchor dictionary/goal state/publish and other executed children. FlowStableGoalProfilerRecord is overlapping recorder overhead and is listed separately, not added to the formula.')
$lines.Add('- PathQueue expands through queue, dispatch and stage records; InitializePolicy expands through lookup, anchor, construct, authority and pin. SlotCacheBuild expands into generation, clearance, line-of-sight, deduplication, array materialization and publication. Occupancy expands through bucket construction, preparation, cache-state/snapshot, slot filter and query work.')
$lines.Add('')
$lines.Add('Aliases and overlapping diagnostics (excluded from additions):')
foreach ($property in $partition.Aliases.psobject.Properties) {
    $aliasRecords = @($raw | Where-Object { [ulong]$_.logicFrame -eq $partition.LogicFrame -and $_.scope -eq $property.Name })
    $aliasTicks = ($aliasRecords | Measure-Object ticks -Sum).Sum
    $lines.Add("- $($property.Name): $(Number (Ms $aliasTicks)) ms -> $($property.Value)")
}
$lines.Add('')
$lines.Add('Full current Tick equation (ms):')
$lines.Add('')
$lines.Add("$(Number $tickMs) = " + (($terms | ForEach-Object { "$($_.Name)($(Number $_.MaxTickMs))" }) -join ' + '))
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($lines -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
$terms | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($OutputPath + '.samples.json') -Encoding utf8NoBOM
[pscustomobject]@{TickMs=$tickMs;ExclusiveTerms=$terms.Count;MeasuredTerms=$measured.Count;ObservedGapMs=$gap;ColdContributionMs=$cold;Rate=$rate;UnmeasuredSmallTerms=$terms.Count-$measured.Count;Output=$OutputPath}
