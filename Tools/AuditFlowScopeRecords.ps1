param(
    [Parameter(Mandatory)][string]$RecordsPath,
    [Parameter(Mandatory)][ulong]$LogicFrame,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$rules = @{}
function Branch([string]$parent, [string[]]$children) {
    foreach ($child in $children) {
        if (!$rules.ContainsKey($child)) { $rules[$child] = @() }
        $rules[$child] += $parent
    }
}
Branch 'LogicFrameTick' @('LogicFrameListenerSnapshot','LogicFrameListenerCallbacks')
Branch 'LogicFrameListenerCallbacks' @('LogicFrameListenerAttributed')
Branch 'LogicFrameListenerAttributed' @('listener:MAEntityLogicFrameSystem+PhaseListener','listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener','listener:LogicInteractionAuthorityService+AuthorityListener','listener:GroupMoveManager','listener:TutorialManager')
Branch 'listener:MAEntityLogicFrameSystem+PhaseListener' @('LogicEntityFrameSetup','LogicEntityBaseAndBuffs','LogicEntityNavigationPositionSync','LogicEntityNavigationSync','LogicEntityBrain','LogicEntityTargeting','LogicEntityProjectile','LogicEntityAttack','LogicEntityDamageResolve','LogicEntityMoveIntent','LogicEntityMoveResolve','LogicEntityMoveCommit','LogicEntityPostUpdate','LogicEntityFrameComplete')
Branch 'listener:GroupMoveManager' @('FlowGroupMove')
Branch 'FlowGroupMove' @('FlowConfig','FlowSourceGate','FlowWorldBuildQueue','FlowRuntimeRebuildQueue')
Branch 'LogicEntityNavigationPositionSync' @('FlowNavigationAgentUpdate','FlowNavigationInactiveClear')
Branch 'LogicEntityNavigationSync' @('CharacterMovePrepare','FlowNavigationCommit')
Branch 'CharacterMovePrepare' @('FlowNavigationInactiveClear','FlowSteeringSetupAgent','FlowSteeringSetupWorld','FlowSteeringSetupOccupancy')
Branch 'FlowNavigationCommit' @('FlowNavigationResolveRequests','FlowNavigationTileQueue','FlowNavigationPortalOwners')
Branch 'FlowNavigationResolveRequests' @('FlowNavigationResolveDemandBatch','FlowNavigationResolvePathQueueBatch','FlowNavigationPathDiagnosticCapture','FlowNavigationPathBudgetEnd')
Branch 'FlowNavigationResolveDemandBatch' @('FlowNavigationRequestSort','FlowNavigationRequestPrune','FlowNavigationDemandResolve','FlowNavigationDemandDispatch')
Branch 'FlowNavigationDemandResolve' @('FlowNavigationDemandAgentPreparation','FlowPrepareStartCell','FlowPrepareStableGoal','FlowNavigationDemandSectorValidation','FlowNavigationDemandAssembly')
Branch 'FlowPrepareStableGoal' @('FlowMovingTargetPolicyInputResolution','FlowMovingTargetPolicyAnchorLookup','FlowMovingTargetPolicyAnchorState','FlowMovingTargetPolicyFinalGoalResolution','FlowMovingTargetPolicyBatchEarlyPath','FlowMovingTargetPolicyBatchContinuation','FlowMovingTargetPolicyRawGoalPath','FlowMovingTargetPolicyOutputInitialization')
Branch 'FlowMovingTargetPolicyInputResolution' @('FlowMovingTargetPolicyBatchResolutionLookup','FlowMovingTargetPolicyClassification')
Branch 'FlowPrepareStableGoal' @('FlowStableGoalBody','FlowStableGoalPrologue','FlowStableGoalMeasurementFinalize')
Branch 'FlowStableGoalPrologue' @('FlowStableGoalMeasurementBegin')
foreach ($scope in @($rules.Keys)) {
    if ($scope -like 'FlowMovingTarget*' -and $rules[$scope] -contains 'FlowPrepareStableGoal') {
        $rules[$scope] = @($rules[$scope] | ForEach-Object { if ($_ -eq 'FlowPrepareStableGoal') { 'FlowStableGoalBody' } else { $_ } })
    }
}
Branch 'FlowMovingTargetPolicyBatchEarlyPath' @('FlowMovingTargetPolicyBatchStatePublish')
Branch 'FlowMovingTargetPolicyAnchorState' @('FlowMovingTargetPolicyAnchorTargetResolve','FlowMovingTargetPolicyAnchorKeyBuild','FlowMovingTargetPolicyAnchorDictionary','FlowMovingTargetPolicyAnchorGoalState','FlowMovingTargetPolicyAnchorNavState')
Branch 'FlowMovingTargetPolicyAnchorDictionary' @('FlowMovingTargetPolicyAnchorDictionaryLookup','FlowMovingTargetPolicyAnchorDictionaryCreate','FlowMovingTargetPolicyAnchorDictionaryInsert')
Branch 'FlowMovingTargetPolicyAnchorGoalState' @('FlowMovingTargetPolicyAnchorProjectionCacheCheck','FlowMovingTargetPolicyAnchorProjectionIslandResolve','FlowMovingTargetPolicyAnchorProjectionRequest','FlowMovingTargetPolicyAnchorPublish','FlowMovingTargetPolicyAnchorRebind')
Branch 'FlowNavigationDemandDispatch' @('FlowNavigationDemandReuse','FlowNavigationDemandEnqueue')
Branch 'FlowNavigationDemandReuse' @('FlowNavigationDemandPathValidation','FlowNavigationDemandPathAdvance','FlowNavigationDemandPortalParticipation','FlowNavigationDemandTileBuilds')
Branch 'FlowNavigationDemandEnqueue' @('FlowNavigationDemandRemoveOther','FlowNavigationDemandQueueMutation','FlowNavigationDemandSourceBinding','FlowNavigationDemandPathValidation','FlowNavigationDemandSnapshotPublish')
Branch 'FlowNavigationDemandQueueMutation' @('FlowNavigationDemandQueueLookup','FlowNavigationDemandQueueCreate')
Branch 'FlowNavigationDemandSourceBinding' @('FlowNavigationDemandSourceLookup','FlowNavigationDemandSourceInsert')
Branch 'FlowNavigationResolvePathQueueBatch' @('FlowNavigationPathWorldStateEnumeration','FlowNavigationPathWorldActivation','FlowNavigationPathWorldRestore','FlowNavigationPathWorldOuterUnattributed','FlowNavigationPathQueueEligibility','FlowNavigationPathQueueLoopUnattributed','FlowNavigationPathQueueProcessingUnattributed','FlowNavigationPathQueueCommitLoop','FlowNavigationPathBudgetAccounting','FlowNavigationPathSliceDispatch')
Branch 'FlowNavigationPathSliceDispatch' @('FlowNavigationPathSliceInitialize','FlowNavigationPathSliceGoalConnector','FlowNavigationPathSliceCreateHierarchy','FlowNavigationPathSliceExpandHierarchy','FlowNavigationPathSliceDownward','FlowNavigationPathSliceL0','FlowNavigationPathSliceMaterialize','FlowNavigationPathSliceComplete','FlowNavigationPathSliceDispatchPreparation','FlowNavigationPathSliceDispatchFinalization')
Branch 'FlowNavigationPathSliceInitialize' @('FlowNavigationPathInitializeSameSector','FlowNavigationPathInitializePolicy','FlowNavigationPathInitializeHierarchyResolve','FlowNavigationPathInitializeHierarchySelection')
Branch 'FlowNavigationPathInitializePolicy' @('FlowMovingTargetPolicyAnchorLookup','FlowCorridorPolicyAuthorityHash')
Branch 'FlowNavigationPathInitializePolicy' @('FlowNavigationPolicyInitializeLookup','FlowNavigationPolicyInitializeAnchor','FlowNavigationPolicyInitializeConstruct','FlowNavigationPolicyInitializeAuthority','FlowNavigationPolicyInitializePin')
Branch 'FlowNavigationPolicyInitializeAnchor' @('FlowMovingTargetPolicyAnchorLookup')
Branch 'FlowNavigationPolicyInitializeAuthority' @('FlowCorridorPolicyAuthorityHash')
Branch 'FlowNavigationPathSliceGoalConnector' @('FlowNavigationGoalConnectorSearchSlice','FlowNavigationGoalConnectorInputCollection','FlowNavigationGoalConnectorLink')
Branch 'LogicEntityBrain' @('EntityBrainCombat')
Branch 'EntityBrainCombat' @('FlowCombatApproach')
Branch 'FlowCombatApproach' @('FlowCombatApproachPreResolve','FlowCombatApproachCore')
Branch 'FlowCombatApproachCore' @('FlowCombatApproachCoreAgentPreparation','FlowCombatApproachCoreSetup','FlowCombatApproachSlotCache','FlowCombatApproachDirectionSetup','FlowCombatApproachCoreScorePhase','FlowCombatApproachCoreExpandedPhase','FlowCombatApproachFinalize')
Branch 'FlowCombatApproachSlotCache' @('FlowCombatApproachSlotCacheLookup','FlowCombatApproachSlotCacheBuild')
Branch 'FlowCombatApproachSlotCacheBuild' @('FlowCombatApproachSlotGenerate','FlowCombatApproachSlotClearance','FlowCombatApproachSlotLineOfSight','FlowCombatApproachSlotDeduplicate','FlowCombatApproachSlotCacheArrayMaterialize','FlowCombatApproachSlotCachePublish')
Branch 'FlowCombatApproachCoreScorePhase' @('FlowCombatApproachOccupancyPreparation','FlowCombatApproachOccupancy','FlowCombatApproachScore','FlowCombatApproachCoreScoreIslandFilter')
Branch 'FlowCombatApproachOccupancyPreparation' @('FlowCombatApproachOccupancyBucketBuild','FlowCombatApproachOccupancyPrepareGuard','FlowCombatApproachOccupancyPrepareIteration')
Branch 'FlowCombatApproachOccupancyBucketBuild' @('FlowCombatApproachOccupancyAgentSync','FlowCombatApproachOccupancyBucketFill')
Branch 'FlowCombatApproachOccupancyBucketFill' @('FlowCombatApproachOccupancyBucketPosition','FlowCombatApproachOccupancyBucketTarget','FlowCombatApproachOccupancyBucketThreshold')
Branch 'FlowCombatApproachOccupancyPrepareIteration' @('FlowCombatApproachOccupancyPrepareInitialization','FlowCombatApproachOccupancySlotFilter')
Branch 'FlowCombatApproachOccupancyPrepareInitialization' @('FlowCombatApproachOccupancyPrepareCacheState','FlowCombatApproachOccupancySlotFilter')
Branch 'FlowCombatApproachOccupancyPrepareCacheState' @('FlowCombatApproachOccupancySnapshot')
Branch 'FlowCombatApproachScore' @('FlowCombatApproachScoreArithmetic')
Branch 'FlowCombatApproachOccupancy' @('FlowCombatApproachOccupancyReservations','FlowCombatApproachOccupancyBuckets','FlowCombatApproachOccupancyCandidate')
Branch 'FlowCombatApproachOccupancyBuckets' @('FlowCombatApproachOccupancyQuerySetup')
Branch 'FlowCombatApproachOccupancyQuerySetup' @('FlowCombatApproachOccupancyBucketBuild')
Branch 'FlowCombatApproach' @('FlowCombatApproachOccupancy')
Branch 'LogicEntityTargeting' @('CharacterTargetingEvaluate')
Branch 'CharacterTargetingEvaluate' @('CharacterTargetingCandidateScan','CharacterTargetingReachability','CharacterTargetingWallDetour')
Branch 'LogicEntityMoveIntent' @('CharacterMoveSteering','CharacterMoveSetInput','CharacterMoveDebugLog')
Branch 'CharacterMoveSteering' @('FlowSteeringSetupAgent','FlowSteeringSetupWorld','FlowSteeringSetupOccupancy')
Branch 'LogicEntityMoveResolve' @('LogicMoveResolvePrepare','LogicMoveResolvePairSolver','LogicMoveResolveProjection','LogicMoveResolveRebuild','LogicMoveResolveBookkeeping')
Branch 'LogicMoveResolveProjection' @('LogicMoveResolveStaticSolver','LogicMoveResolveRegionConstraint')
Branch 'FlowNavigationTileQueue' @('FlowTileQueueMovingTargetProjection','FlowTileQueueActiveDemand','FlowTileQueuePrune','FlowTileQueueReferenceTrim','FlowTileQueueCommit','FlowTileQueueSharedGoal')
Branch 'FlowNavigationPathQueueCommitLoop' @('FlowNavigationPathPolicyCommit','FlowNavigationPathRequestRemoval')
Branch 'FlowNavigationPathPolicyCommit' @('FlowCorridorPolicyAuthorityHash')
Branch 'FlowNavigationPathSliceExpandHierarchy' @('FlowNavigationPathSearchSchedule','FlowNavigationPathSearchComplete')

# These are repeated totals or independently recorded remainders, not additional work.
$aliases = @{
    FlowStableGoalProfilerRecord = 'observer-overhead-already-in-inclusive-scopes'
    FlowNavigationPathAdvance = 'FlowNavigationResolvePathQueueBatch'
    FlowNavigationPathSliceDispatchUnattributed = 'FlowNavigationPathSliceDispatch/self'
    FlowNavigationGoalConnectorUnattributed = 'FlowNavigationPathSliceGoalConnector/self'
    FlowCombatApproachOccupancyPrepareSlotLookup = 'FlowCombatApproachOccupancyPrepareIteration'
    FlowCombatApproachOccupancyBucketIncremental = 'FlowCombatApproachOccupancyBucketFill'
    FlowCombatApproachCoreScoreUnattributed = 'FlowCombatApproachCoreScorePhase/self'
    FlowCombatApproachCoreUnattributed = 'FlowCombatApproachCore/self'
    LogicFrameListenerUnattributed = 'LogicFrameListenerCallbacks/self'
}
# ScoreArithmetic is accumulated over the same loop and published after Score.
$afterParent = @{
    FlowCombatApproachOccupancyBucketPosition = $true
    FlowCombatApproachOccupancyBucketTarget = $true
    FlowCombatApproachOccupancyBucketThreshold = $true
    FlowCombatApproachScoreArithmetic = $true
    FlowCombatApproachOccupancyReservations = $true
    FlowCombatApproachOccupancyBuckets = $true
    FlowCombatApproachOccupancyCandidate = $true
}
function Get-FramePartition([object[]]$records, [ulong]$frame) {
if (!$records.Count) { throw "No scope records for logic frame $LogicFrame" }
$frequency = [long]$records[0].frequency
$parents = [int[]]::new($records.Count)
[Array]::Fill($parents, -1)
$childrenTicks = [long[]]::new($records.Count)
$next = @{}
$issues = [Collections.Generic.List[string]]::new()
for ($i = $records.Count - 1; $i -ge 0; $i--) {
    $record = $records[$i]
    $scope = $record.scope
    if ($aliases.ContainsKey($scope)) { continue }
    if ($rules.ContainsKey($scope) -and !$afterParent.ContainsKey($scope)) {
        $candidate = [int]::MaxValue
        foreach ($name in $rules[$scope]) {
            if ($next.ContainsKey($name)) { $candidate = [Math]::Min($candidate, $next[$name]) }
        }
        if ($candidate -ne [int]::MaxValue) { $parents[$i] = $candidate }
        else { $issues.Add("Parent record missing: sequence=$($record.sequence) scope=$scope") }
    }
    $next[$scope] = $i
}
$previous = @{}
for ($i = 0; $i -lt $records.Count; $i++) {
    $record = $records[$i]
    $scope = $record.scope
    if ($afterParent.ContainsKey($scope)) {
        foreach ($name in $rules[$scope]) {
            if (!$previous.ContainsKey($name)) { throw "Earlier parent $name missing for $scope" }
            $parents[$i] = $previous[$name]
        }
    }
    if ($parents[$i] -ge 0) { $childrenTicks[$parents[$i]] += [long]$record.ticks }
    $previous[$scope] = $i
}
$rows = [Collections.Generic.List[object]]::new()
$unmapped = @{}
for ($i = 0; $i -lt $records.Count; $i++) {
    $record = $records[$i]
    $scope = $record.scope
    if ($aliases.ContainsKey($scope)) { continue }
    if (!$rules.ContainsKey($scope) -and $scope -ne 'LogicFrameTick') {
        $unmapped[$scope] += [long]$record.ticks
        continue
    }
    $ownTicks = [long]$record.ticks - $childrenTicks[$i]
    if ($ownTicks -lt 0) { $issues.Add("Negative exclusive time: sequence=$($record.sequence) scope=$scope ownTicks=$ownTicks") }
    $rows.Add([pscustomobject]@{
        Sequence = [int]$record.sequence; Scope = $scope; LogicFrame = $frame
        ParentSequence = $(if ($parents[$i] -ge 0) { [int]$records[$parents[$i]].sequence } else { -1 })
        InclusiveTicks = [long]$record.ticks; ChildrenTicks = $childrenTicks[$i]; OwnTicks = $ownTicks
    })
}
$terms = @($rows | Group-Object Scope | ForEach-Object {
    $total = 0L
    foreach ($row in $_.Group) { $total += $row.OwnTicks }
    [pscustomobject]@{ Scope = $_.Name; OwnMs = $total * 1000.0 / $frequency; RecordCount = $_.Count }
} | Sort-Object OwnMs -Descending)
$result = [ordered]@{
    Status = 'CANDIDATE_REQUIRES_CALL_BOUNDARY_VERIFICATION'
    LogicFrame = $frame; RecordCount = $records.Count; Frequency = $frequency
    Issues = @($issues); UnmappedTicks = $unmapped; Aliases = $aliases; Terms = $terms; Records = @($rows)
}
return $result
}
$allRecords = @(Import-Csv -LiteralPath $RecordsPath)
$frames = @($allRecords | Group-Object logicFrame)
$target = $frames | Where-Object { [ulong]$_.Name -eq $LogicFrame }
if (!$target) { throw "Target logic frame $LogicFrame missing" }
$result = Get-FramePartition $target.Group $LogicFrame
$result.SourceSha256 = (Get-FileHash -LiteralPath $RecordsPath -Algorithm SHA256).Hash
$hotFrames = [Collections.Generic.List[object]]::new()
foreach ($frameGroup in $frames) {
    $frame = [ulong]$frameGroup.Name
    if ($frame -le $LogicFrame) { continue }
    $partition = Get-FramePartition $frameGroup.Group $frame
    $hotFrames.Add([pscustomobject]@{
        LogicFrame = $frame; Issues = $partition.Issues; UnmappedTicks = $partition.UnmappedTicks
        Records = $partition.Records
    })
}
$result.HotFrames = @($hotFrames)
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
[pscustomobject]@{ Records = $result.RecordCount; Issues = $result.Issues.Count; UnmappedScopes = $result.UnmappedTicks.Count; HotFrames = $hotFrames.Count; Output = $OutputPath }
