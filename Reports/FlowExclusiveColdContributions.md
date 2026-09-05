# Flow Cold Contribution Audit

Status: FIRST_OCCURRENCE_COLD_ESTIMATE; finest-boundary review and acceptance remain OPEN.
Source SHA256: 95D49F007D819B37100600A92194893BBE9BF29DEEC46CACA57BD991519EEEC0
Logic frame: 80; total Tick: 34.648600 ms; exclusive residual: 0 ticks.
Exclusive terms: 162; measured >0.1ms: 57. No historical item-count limit.

Each /self term is inclusive duration minus the direct children of that invocation. Parent totals and alias records are excluded. FirstInTick is the first observed invocation in the target Tick, not necessarily the first invocation in the run. Hot is the mean of subsequent exclusive invocations. ColdContribution equals FirstInTick minus Hot only when that scope first occurred in the target Tick; otherwise it is zero. This is an empirical first-occurrence estimate, not proof of JIT causation; differing branch work can contribute to the difference. Terms <=0.1ms have no inferred cold value.

| Exclusive term | Tick ms | First record frame | First in max Tick | First in Tick ms | Hot ms | First-hot ms | Cold contribution ms | Hot samples |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |
| FlowCombatApproachOccupancyBucketFill/self | 2.376900 | 80 | True | 2.376900 | 0.008800 | 2.368100 | 2.368100 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowNavigationPathSliceGoalConnector/self | 1.816400 | 80 | True | 1.212600 | 0.100633 | 1.111967 | 1.111967 | n=6; frames=80 |
| CharacterMoveSteering/self | 1.575700 | 80 | True | 1.571700 | 0.000571 | 1.571129 | 1.571129 | n=7; frames=80 |
| FlowPrepareStableGoal/self | 1.568400 | 80 | True | 1.565000 | 0.000486 | 1.564514 | 1.564514 | n=7; frames=80 |
| FlowCombatApproachCore/self | 1.485700 | 80 | True | 1.478500 | 0.001029 | 1.477471 | 1.477471 | n=7; frames=80 |
| FlowNavigationGoalConnectorSearchSlice/self | 1.187100 | 80 | True | 0.998800 | 0.094150 | 0.904650 | 0.904650 | n=2; frames=80 |
| EntityBrainCombat/self | 1.160300 | 80 | True | 1.147600 | 0.001814 | 1.145786 | 1.145786 | n=7; frames=80 |
| FlowCombatApproachSlotCache/self | 1.070700 | 80 | True | 1.068700 | 0.000286 | 1.068414 | 1.068414 | n=7; frames=80 |
| FlowCombatApproachOccupancyPrepareInitialization/self | 1.028300 | 80 | True | 1.027700 | 0.000600 | 1.027100 | 1.027100 | n=1; frames=80 |
| FlowNavigationPathQueueEligibility/self | 1.005100 | 80 | True | 1.002600 | 0.002500 | 1.000100 | 1.000100 | n=1; frames=80 |
| FlowCombatApproachSlotClearance/self | 0.957000 | 80 | True | 0.778600 | 0.178400 | 0.600200 | 0.600200 | n=1; frames=80 |
| FlowCombatApproachPreResolve/self | 0.760900 | 80 | True | 0.751600 | 0.001329 | 0.750271 | 0.750271 | n=7; frames=80 |
| FlowCombatApproachCoreScorePhase/self | 0.757600 | 80 | True | 0.593100 | 0.023500 | 0.569600 | 0.569600 | n=7; frames=80 |
| FlowNavigationDemandEnqueue/self | 0.724700 | 80 | True | 0.720900 | 0.000543 | 0.720357 | 0.720357 | n=7; frames=80 |
| FlowMovingTargetPolicyAnchorGoalState/self | 0.685400 | 80 | True | 0.684400 | 0.001000 | 0.683400 | 0.683400 | n=1; frames=80 |
| FlowNavigationDemandResolve/self | 0.669400 | 80 | True | 0.663700 | 0.000814 | 0.662886 | 0.662886 | n=7; frames=80 |
| FlowNavigationPathWorldOuterUnattributed/self | 0.660600 | 80 | True | 0.660600 | 0.000933 | 0.659667 | 0.659667 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachSlotLineOfSight/self | 0.633000 | 80 | True | 0.382900 | 0.250100 | 0.132800 | 0.132800 | n=1; frames=80 |
| FlowCombatApproachScoreArithmetic/self | 0.612900 | 80 | True | 0.553200 | 0.008529 | 0.544671 | 0.544671 | n=7; frames=80 |
| FlowNavigationPathQueueLoopUnattributed/self | 0.609400 | 80 | True | 0.607200 | 0.001100 | 0.606100 | 0.606100 | n=2; frames=80 |
| FlowCombatApproach/self | 0.606000 | 80 | True | 0.604100 | 0.000271 | 0.603829 | 0.603829 | n=7; frames=80 |
| FlowCombatApproachSlotCacheLookup/self | 0.573900 | 80 | True | 0.487200 | 0.017340 | 0.469860 | 0.469860 | n=5; frames=80 |
| FlowCombatApproachOccupancyPreparation/self | 0.567100 | 80 | True | 0.563900 | 0.000457 | 0.563443 | 0.563443 | n=7; frames=80 |
| LogicMoveResolveStaticSolver/self | 0.546100 | 1 | False | 0.153100 | 0.131000 | 0.022100 | 0.000000 | n=3; frames=80 |
| FlowMovingTargetPolicyAnchorDictionaryInsert/self | 0.479400 | 80 | True | 0.479200 | 0.000200 | 0.479000 | 0.479000 | n=1; frames=80 |
| FlowMovingTargetPolicyBatchResolutionLookup/self | 0.472500 | 80 | True | 0.405600 | 0.013380 | 0.392220 | 0.392220 | n=5; frames=80 |
| FlowNavigationPathSliceDispatch/self | 0.464300 | 80 | True | 0.463700 | 0.000600 | 0.463100 | 0.463100 | n=1; frames=80 |
| FlowNavigationDemandQueueCreate/self | 0.459900 | 80 | True | 0.449300 | 0.010600 | 0.438700 | 0.438700 | n=1; frames=80 |
| CharacterMovePrepare/self | 0.445800 | 1 | False | 0.000300 | 0.011423 | -0.011123 | 0.000000 | n=39; frames=80 |
| FlowCombatApproachSlotCachePublish/self | 0.444200 | 80 | True | 0.443800 | 0.000400 | 0.443400 | 0.443400 | n=1; frames=80 |
| FlowNavigationPathInitializePolicy/self | 0.415300 | 80 | True | 0.414300 | 0.001000 | 0.413300 | 0.413300 | n=1; frames=80 |
| LogicMoveResolvePairSolver/self | 0.402400 | 1 | False | 0.112600 | 0.096600 | 0.016000 | 0.000000 | n=3; frames=80 |
| FlowNavigationPolicyInitializeAnchor/self | 0.349400 | 80 | True | 0.348500 | 0.000900 | 0.347600 | 0.347600 | n=1; frames=80 |
| FlowMovingTargetPolicyClassification/self | 0.348800 | 80 | True | 0.347500 | 0.000433 | 0.347067 | 0.347067 | n=3; frames=80 |
| LogicEntityMoveIntent/self | 0.338200 | 1 | False | 0.338200 | 0.076256 | 0.261944 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowNavigationDemandSourceInsert/self | 0.325300 | 80 | True | 0.106700 | 0.031229 | 0.075471 | 0.075471 | n=7; frames=80 |
| CharacterTargetingCandidateScan/self | 0.312500 | 38 | False | 0.036200 | 0.009210 | 0.026990 | 0.000000 | n=30; frames=80 |
| FlowNavigationResolvePathQueueBatch/self | 0.307900 | 80 | True | 0.307900 | 0.000444 | 0.307456 | 0.307456 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowMovingTargetPolicyAnchorDictionaryLookup/self | 0.298300 | 80 | True | 0.297800 | 0.000500 | 0.297300 | 0.297300 | n=1; frames=80 |
| FlowNavigationPathDiagnosticCapture/self | 0.290700 | 80 | True | 0.290700 | 0.001500 | 0.289200 | 0.289200 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowNavigationPathWorldStateEnumeration/self | 0.288000 | 80 | True | 0.288000 | 0.002256 | 0.285744 | 0.285744 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowMovingTargetPolicyAnchorPublish/self | 0.277300 | 80 | True | 0.277100 | 0.000200 | 0.276900 | 0.276900 | n=1; frames=80 |
| FlowNavigationPathInitializeHierarchySelection/self | 0.246400 | 80 | True | 0.246200 | 0.000200 | 0.246000 | 0.246000 | n=1; frames=80 |
| FlowNavigationPathSliceInitialize/self | 0.242300 | 80 | True | 0.241700 | 0.000600 | 0.241100 | 0.241100 | n=1; frames=80 |
| FlowCombatApproachSlotGenerate/self | 0.235800 | 80 | True | 0.166000 | 0.069800 | 0.096200 | 0.096200 | n=1; frames=80 |
| FlowNavigationPolicyInitializeAuthority/self | 0.215600 | 80 | True | 0.214800 | 0.000800 | 0.214000 | 0.214000 | n=1; frames=80 |
| FlowNavigationRequestSort/self | 0.210300 | 80 | True | 0.210300 | 0.001922 | 0.208378 | 0.208378 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachCoreSetup/self | 0.204600 | 80 | True | 0.203400 | 0.000171 | 0.203229 | 0.203229 | n=7; frames=80 |
| FlowNavigationDemandReuse/self | 0.195400 | 80 | True | 0.194900 | 0.000100 | 0.194800 | 0.194800 | n=5; frames=80 |
| FlowCombatApproachFinalize/self | 0.189400 | 80 | True | 0.188300 | 0.000220 | 0.188080 | 0.188080 | n=5; frames=80 |
| FlowNavigationDemandRemoveOther/self | 0.178000 | 80 | True | 0.177500 | 0.000125 | 0.177375 | 0.177375 | n=4; frames=80 |
| FlowCombatApproachOccupancyCandidate/self | 0.146100 | 80 | True | 0.143800 | 0.000100 | 0.143700 | 0.143700 | n=23; frames=80 |
| FlowNavigationPathSliceDispatchFinalization/self | 0.136600 | 80 | True | 0.132900 | 0.000463 | 0.132438 | 0.132438 | n=8; frames=80 |
| FlowCombatApproachSlotCacheArrayMaterialize/self | 0.136200 | 80 | True | 0.121000 | 0.015200 | 0.105800 | 0.105800 | n=1; frames=80 |
| LogicEntityTargeting/self | 0.121300 | 1 | False | 0.121300 | 0.072744 | 0.048556 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self | 0.121100 | 1 | False | 0.121100 | 0.124711 | -0.003611 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| LogicEntityBrain/self | 0.117400 | 1 | False | 0.117400 | 0.079111 | 0.038289 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachOccupancyReservations/self | 0.087400 | 80 | True | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyAgentSync/self | 0.083800 | 80 | True | 0.083800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancySlotFilter/self | 0.079500 | 80 | True | 0.067400 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityBaseAndBuffs/self | 0.073000 | 1 | False | 0.073000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathInitializeHierarchyResolve/self | 0.054700 | 80 | True | 0.054000 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueActiveDemand/self | 0.051100 | 1 | False | 0.037000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializePin/self | 0.051000 | 80 | True | 0.051000 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityMoveCommit/self | 0.047500 | 1 | False | 0.047500 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalMeasurementBegin/self | 0.044700 | 80 | True | 0.044600 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializeConstruct/self | 0.044500 | 80 | True | 0.030600 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveProjection/self | 0.042200 | 1 | False | 0.011600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreAgentPreparation/self | 0.040500 | 80 | True | 0.025000 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolvePrepare/self | 0.039600 | 1 | False | 0.039600 | not measured | not measured | not measured | n=0; frames= |
| listener:LogicInteractionAuthorityService+AuthorityListener/self | 0.037500 | 1 | False | 0.037500 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancy/self | 0.037100 | 80 | True | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityFrameSetup/self | 0.034700 | 1 | False | 0.034700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationAgentUpdate/self | 0.031100 | 1 | False | 0.003100 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalMeasurementFinalize/self | 0.031100 | 80 | True | 0.030800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachSlotCacheBuild/self | 0.030400 | 80 | True | 0.024400 | not measured | not measured | not measured | n=0; frames= |
| CharacterTargetingEvaluate/self | 0.029300 | 38 | False | 0.002600 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityNavigationSync/self | 0.028700 | 1 | False | 0.028700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationResolveRequests/self | 0.028100 | 1 | False | 0.028100 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityAttack/self | 0.027600 | 1 | False | 0.027600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachScore/self | 0.026400 | 80 | True | 0.003100 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancySnapshot/self | 0.024300 | 80 | True | 0.014000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandAgentPreparation/self | 0.024200 | 80 | True | 0.007700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationResolveDemandBatch/self | 0.023000 | 80 | True | 0.023000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyBuckets/self | 0.023000 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyInputResolution/self | 0.018100 | 80 | True | 0.014600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachSlotDeduplicate/self | 0.016100 | 80 | True | 0.004400 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityPostUpdate/self | 0.015900 | 1 | False | 0.015900 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalBody/self | 0.015800 | 80 | True | 0.011600 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityMoveResolve/self | 0.015500 | 1 | False | 0.015500 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyBucketBuild/self | 0.015400 | 80 | True | 0.015400 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareCacheState/self | 0.014400 | 80 | True | 0.011900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationTileQueue/self | 0.014400 | 1 | False | 0.014400 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareIteration/self | 0.014400 | 80 | True | 0.007400 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityNavigationPositionSync/self | 0.013200 | 1 | False | 0.013200 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerCallbacks/self | 0.011700 | 1 | False | 0.011700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationRequestPrune/self | 0.011600 | 1 | False | 0.011600 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupAgent/self | 0.011100 | 80 | True | 0.009100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationGoalConnectorInputCollection/self | 0.011100 | 80 | True | 0.005500 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreScoreIslandFilter/self | 0.011000 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorState/self | 0.009400 | 80 | True | 0.008900 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorDictionary/self | 0.008800 | 80 | True | 0.008100 | not measured | not measured | not measured | n=0; frames= |
| FlowCorridorPolicyAuthorityHash/self | 0.008300 | 80 | True | 0.006000 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorDictionaryCreate/self | 0.007900 | 80 | True | 0.006100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathQueueProcessingUnattributed/self | 0.007700 | 80 | True | 0.007700 | not measured | not measured | not measured | n=0; frames= |
| FlowPrepareStartCell/self | 0.007400 | 80 | True | 0.003300 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathQueueCommitLoop/self | 0.007200 | 80 | True | 0.007000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachDirectionSetup/self | 0.006500 | 80 | True | 0.005200 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameTick/self | 0.006100 | 1 | False | 0.006100 | not measured | not measured | not measured | n=0; frames= |
| FlowWorldBuildQueue/self | 0.006000 | 1 | False | 0.006000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationInactiveClear/self | 0.006000 | 1 | False | 0.001200 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorProjectionIslandResolve/self | 0.005200 | 80 | True | 0.004400 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandAssembly/self | 0.005100 | 80 | True | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPortalOwners/self | 0.005000 | 1 | False | 0.005000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandQueueMutation/self | 0.005000 | 80 | True | 0.003900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSnapshotPublish/self | 0.004900 | 80 | True | 0.004000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathWorldActivation/self | 0.004700 | 80 | True | 0.004100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSourceBinding/self | 0.004700 | 80 | True | 0.003100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathWorldRestore/self | 0.004600 | 80 | True | 0.004600 | not measured | not measured | not measured | n=0; frames= |
| listener:MAEntityLogicFrameSystem+PhaseListener/self | 0.004500 | 1 | False | 0.004500 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveRegionConstraint/self | 0.004400 | 1 | False | 0.001300 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSectorValidation/self | 0.004300 | 80 | True | 0.003400 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandDispatch/self | 0.004100 | 80 | True | 0.002700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathSliceDispatchPreparation/self | 0.004100 | 80 | True | 0.003800 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupOccupancy/self | 0.004100 | 80 | True | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializeLookup/self | 0.004000 | 80 | True | 0.003500 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorLookup/self | 0.004000 | 80 | True | 0.001800 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathBudgetAccounting/self | 0.003800 | 80 | True | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityProjectile/self | 0.003500 | 1 | False | 0.003500 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyBatchEarlyPath/self | 0.003400 | 80 | True | 0.002700 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupWorld/self | 0.003300 | 80 | True | 0.002500 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityDamageResolve/self | 0.003200 | 1 | False | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| listener:GroupMoveManager/self | 0.002900 | 1 | False | 0.002900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandQueueLookup/self | 0.002600 | 80 | True | 0.002000 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueReferenceTrim/self | 0.002500 | 1 | False | 0.001400 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveRebuild/self | 0.002400 | 1 | False | 0.000900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSourceLookup/self | 0.002300 | 80 | True | 0.002000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandPathValidation/self | 0.002100 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerSnapshot/self | 0.002000 | 1 | False | 0.002000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareGuard/self | 0.002000 | 80 | True | 0.001000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathBudgetEnd/self | 0.001800 | 80 | True | 0.001800 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorKeyBuild/self | 0.001700 | 80 | True | 0.001700 | not measured | not measured | not measured | n=0; frames= |
| CharacterMoveSetInput/self | 0.001600 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalPrologue/self | 0.001600 | 80 | True | 0.001300 | not measured | not measured | not measured | n=0; frames= |
| listener:TutorialManager/self | 0.001100 | 1 | False | 0.001100 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyBatchStatePublish/self | 0.001000 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreExpandedPhase/self | 0.000900 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueCommit/self | 0.000900 | 1 | False | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorNavState/self | 0.000900 | 80 | True | 0.000900 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorTargetResolve/self | 0.000800 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyOutputInitialization/self | 0.000800 | 80 | True | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| FlowRuntimeRebuildQueue/self | 0.000700 | 1 | False | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowGroupMove/self | 0.000700 | 1 | False | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorProjectionCacheCheck/self | 0.000700 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationCommit/self | 0.000600 | 1 | False | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveBookkeeping/self | 0.000500 | 1 | False | 0.000500 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityFrameComplete/self | 0.000300 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueuePrune/self | 0.000300 | 1 | False | 0.000100 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueMovingTargetProjection/self | 0.000300 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueSharedGoal/self | 0.000300 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| FlowSourceGate/self | 0.000100 | 1 | False | 0.000100 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerAttributed/self | 0.000000 | 1 | False | 0.000000 | not measured | not measured | not measured | n=0; frames= |

Measured >0.1ms first-occurrence cold estimate (ms; <=0.1ms cold contributions are not measured):

34.648600 * 0.802741574157 = 27.813872 ms = FlowCombatApproachOccupancyBucketFill/self(2.368100) + FlowNavigationPathSliceGoalConnector/self(1.111967) + CharacterMoveSteering/self(1.571129) + FlowPrepareStableGoal/self(1.564514) + FlowCombatApproachCore/self(1.477471) + FlowNavigationGoalConnectorSearchSlice/self(0.904650) + EntityBrainCombat/self(1.145786) + FlowCombatApproachSlotCache/self(1.068414) + FlowCombatApproachOccupancyPrepareInitialization/self(1.027100) + FlowNavigationPathQueueEligibility/self(1.000100) + FlowCombatApproachSlotClearance/self(0.600200) + FlowCombatApproachPreResolve/self(0.750271) + FlowCombatApproachCoreScorePhase/self(0.569600) + FlowNavigationDemandEnqueue/self(0.720357) + FlowMovingTargetPolicyAnchorGoalState/self(0.683400) + FlowNavigationDemandResolve/self(0.662886) + FlowNavigationPathWorldOuterUnattributed/self(0.659667) + FlowCombatApproachSlotLineOfSight/self(0.132800) + FlowCombatApproachScoreArithmetic/self(0.544671) + FlowNavigationPathQueueLoopUnattributed/self(0.606100) + FlowCombatApproach/self(0.603829) + FlowCombatApproachSlotCacheLookup/self(0.469860) + FlowCombatApproachOccupancyPreparation/self(0.563443) + LogicMoveResolveStaticSolver/self(0.000000) + FlowMovingTargetPolicyAnchorDictionaryInsert/self(0.479000) + FlowMovingTargetPolicyBatchResolutionLookup/self(0.392220) + FlowNavigationPathSliceDispatch/self(0.463100) + FlowNavigationDemandQueueCreate/self(0.438700) + CharacterMovePrepare/self(0.000000) + FlowCombatApproachSlotCachePublish/self(0.443400) + FlowNavigationPathInitializePolicy/self(0.413300) + LogicMoveResolvePairSolver/self(0.000000) + FlowNavigationPolicyInitializeAnchor/self(0.347600) + FlowMovingTargetPolicyClassification/self(0.347067) + LogicEntityMoveIntent/self(0.000000) + FlowNavigationDemandSourceInsert/self(0.075471) + CharacterTargetingCandidateScan/self(0.000000) + FlowNavigationResolvePathQueueBatch/self(0.307456) + FlowMovingTargetPolicyAnchorDictionaryLookup/self(0.297300) + FlowNavigationPathDiagnosticCapture/self(0.289200) + FlowNavigationPathWorldStateEnumeration/self(0.285744) + FlowMovingTargetPolicyAnchorPublish/self(0.276900) + FlowNavigationPathInitializeHierarchySelection/self(0.246000) + FlowNavigationPathSliceInitialize/self(0.241100) + FlowCombatApproachSlotGenerate/self(0.096200) + FlowNavigationPolicyInitializeAuthority/self(0.214000) + FlowNavigationRequestSort/self(0.208378) + FlowCombatApproachCoreSetup/self(0.203229) + FlowNavigationDemandReuse/self(0.194800) + FlowCombatApproachFinalize/self(0.188080) + FlowNavigationDemandRemoveOther/self(0.177375) + FlowCombatApproachOccupancyCandidate/self(0.143700) + FlowNavigationPathSliceDispatchFinalization/self(0.132438) + FlowCombatApproachSlotCacheArrayMaterialize/self(0.105800) + LogicEntityTargeting/self(0.000000) + listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self(0.000000) + LogicEntityBrain/self(0.000000)

14.66 / 14.71 coverage:

Historical names -> current source scopes (traceability only, not additional addends; inclusive sources expand to their /self and descendant terms above):
- DemandResolve -> FlowNavigationDemandResolve
- OccupancyPrepareIteration -> FlowCombatApproachOccupancyPrepareIteration
- PathSliceGoalConnector -> FlowNavigationPathSliceGoalConnector
- PathQueue未覆盖差额 -> FlowNavigationResolvePathQueueBatch/self
- SlotCacheBuild -> FlowCombatApproachSlotCacheBuild
- DemandDispatch -> FlowNavigationDemandDispatch
- MoveIntent -> LogicEntityMoveIntent
- PathSliceInitialize -> FlowNavigationPathSliceInitialize
- CombatCore未覆盖差额 -> FlowCombatApproachCore/self
- OccupancyPreparation未覆盖差额 -> FlowCombatApproachOccupancyPreparation/self
- OccupancyBucketFill -> FlowCombatApproachOccupancyBucketFill
- EntityBrainCombat未覆盖差额 -> EntityBrainCombat/self
- MoveResolve -> LogicEntityMoveResolve
- CombatApproachPreResolve -> FlowCombatApproachPreResolve
- PathSliceDispatch未覆盖差额 -> FlowNavigationPathSliceDispatch/self
- CombatScorePhase未覆盖差额 -> FlowCombatApproachCoreScorePhase/self
- CombatScore -> FlowCombatApproachScoreArithmetic
- NavigationSync未覆盖差额 -> LogicEntityNavigationSync/self
- SlotCachePublish -> FlowCombatApproachSlotCachePublish
- SlotCacheLookup -> FlowCombatApproachSlotCacheLookup
- Targeting -> LogicEntityTargeting
- SlotCache未覆盖差额 -> FlowCombatApproachSlotCache/self
- CombatApproach未覆盖差额 -> FlowCombatApproach/self
- Occupancy判定 -> FlowCombatApproachOccupancy
- ResolveRequests未覆盖差额 -> FlowNavigationResolveRequests/self
- CombatApproachFinalize -> FlowCombatApproachFinalize
- DemandBatch未覆盖差额 -> FlowNavigationResolveDemandBatch/self
- CombatCoreSetup -> FlowCombatApproachCoreSetup
- Snapshot -> listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener
- SlotCacheArrayMaterialize -> FlowCombatApproachSlotCacheArrayMaterialize
- Brain未覆盖差额 -> LogicEntityBrain/self
- NavigationCommit未覆盖差额 -> FlowNavigationCommit/self
- BaseAndBuffs -> LogicEntityBaseAndBuffs
- CombatScore未覆盖差额 -> FlowCombatApproachScore/self
- PathSlice内部GoalConnector -> FlowNavigationGoalConnectorLink
- Interaction -> listener:LogicInteractionAuthorityService+AuthorityListener
- CombatCoreAgentPreparation -> FlowCombatApproachCoreAgentPreparation
- FrameSetup -> LogicEntityFrameSetup
- NavigationAgentUpdate -> FlowNavigationAgentUpdate
- MoveCommit -> LogicEntityMoveCommit
- Attack -> LogicEntityAttack
- Listener/runtime未覆盖差额 -> LogicFrameTick/self + LogicFrameListenerCallbacks/self + listener:MAEntityLogicFrameSystem+PhaseListener/self
- CombatCoreUnattributed -> FlowCombatApproachCore/self
- PostUpdate -> LogicEntityPostUpdate
- GroupMove -> FlowGroupMove
- NavigationInactiveClear -> FlowNavigationInactiveClear
- CombatCoreDirectionSetup -> FlowCombatApproachDirectionSetup
- PathSliceExpandHierarchy -> FlowNavigationPathSliceExpandHierarchy
- Projectile -> LogicEntityProjectile
- DamageResolve -> LogicEntityDamageResolve
- PathSliceDownward -> FlowNavigationPathSliceDownward
- Tutorial -> listener:TutorialManager
- CombatCoreExpanded -> FlowCombatApproachCoreExpandedPhase
The historical line actually lists 53 names, including overlapping SlotCacheBuild/array/publication and Core residual labels; no names are silently dropped to reach 51. Old residual labels are tracked to the owning scope, and their former numeric boundaries are superseded by the current per-invocation partition.

- Historical scopes are traced by their actual ownership paths, not a fixed 51-item count. NavigationSync contains CharacterMovePrepare and Commit; MoveIntent contains CharacterMoveSteering. SnapshotCaptureListener, Interaction AuthorityListener, GroupMoveManager and TutorialManager are retained as independent listeners under the Tick root.
- DemandResolve expands through FlowPrepareStableGoal -> Body, Prologue (including MeasurementBegin), MeasurementFinalize, and the call-boundary remainder. Body expands through input/classification/batch lookup, anchor dictionary/goal state/publish and other executed children. FlowStableGoalProfilerRecord is overlapping recorder overhead and is listed separately, not added to the formula.
- PathQueue expands through queue, dispatch and stage records; InitializePolicy expands through lookup, anchor, construct, authority and pin. SlotCacheBuild expands into generation, clearance, line-of-sight, deduplication, array materialization and publication. Occupancy expands through bucket construction, preparation, cache-state/snapshot, slot filter and query work.

Aliases and overlapping diagnostics (excluded from additions):
- FlowCombatApproachCoreUnattributed: 0.022000 ms -> FlowCombatApproachCore/self
- FlowNavigationPathSliceDispatchUnattributed: 0.605000 ms -> FlowNavigationPathSliceDispatch/self
- FlowCombatApproachOccupancyBucketIncremental: 2.376900 ms -> FlowCombatApproachOccupancyBucketFill
- FlowStableGoalProfilerRecord: 0.013300 ms -> observer-overhead-already-in-inclusive-scopes
- FlowNavigationGoalConnectorUnattributed: 0.762500 ms -> FlowNavigationPathSliceGoalConnector/self
- FlowCombatApproachCoreScoreUnattributed: 0.057500 ms -> FlowCombatApproachCoreScorePhase/self
- LogicFrameListenerUnattributed: 0.011700 ms -> LogicFrameListenerCallbacks/self
- FlowNavigationPathAdvance: 8.152300 ms -> FlowNavigationResolvePathQueueBatch
- FlowCombatApproachOccupancyPrepareSlotLookup: 1.161100 ms -> FlowCombatApproachOccupancyPrepareIteration

Full current Tick equation (ms):

34.648600 = FlowCombatApproachOccupancyBucketFill/self(2.376900) + FlowNavigationPathSliceGoalConnector/self(1.816400) + CharacterMoveSteering/self(1.575700) + FlowPrepareStableGoal/self(1.568400) + FlowCombatApproachCore/self(1.485700) + FlowNavigationGoalConnectorSearchSlice/self(1.187100) + EntityBrainCombat/self(1.160300) + FlowCombatApproachSlotCache/self(1.070700) + FlowCombatApproachOccupancyPrepareInitialization/self(1.028300) + FlowNavigationPathQueueEligibility/self(1.005100) + FlowCombatApproachSlotClearance/self(0.957000) + FlowCombatApproachPreResolve/self(0.760900) + FlowCombatApproachCoreScorePhase/self(0.757600) + FlowNavigationDemandEnqueue/self(0.724700) + FlowMovingTargetPolicyAnchorGoalState/self(0.685400) + FlowNavigationDemandResolve/self(0.669400) + FlowNavigationPathWorldOuterUnattributed/self(0.660600) + FlowCombatApproachSlotLineOfSight/self(0.633000) + FlowCombatApproachScoreArithmetic/self(0.612900) + FlowNavigationPathQueueLoopUnattributed/self(0.609400) + FlowCombatApproach/self(0.606000) + FlowCombatApproachSlotCacheLookup/self(0.573900) + FlowCombatApproachOccupancyPreparation/self(0.567100) + LogicMoveResolveStaticSolver/self(0.546100) + FlowMovingTargetPolicyAnchorDictionaryInsert/self(0.479400) + FlowMovingTargetPolicyBatchResolutionLookup/self(0.472500) + FlowNavigationPathSliceDispatch/self(0.464300) + FlowNavigationDemandQueueCreate/self(0.459900) + CharacterMovePrepare/self(0.445800) + FlowCombatApproachSlotCachePublish/self(0.444200) + FlowNavigationPathInitializePolicy/self(0.415300) + LogicMoveResolvePairSolver/self(0.402400) + FlowNavigationPolicyInitializeAnchor/self(0.349400) + FlowMovingTargetPolicyClassification/self(0.348800) + LogicEntityMoveIntent/self(0.338200) + FlowNavigationDemandSourceInsert/self(0.325300) + CharacterTargetingCandidateScan/self(0.312500) + FlowNavigationResolvePathQueueBatch/self(0.307900) + FlowMovingTargetPolicyAnchorDictionaryLookup/self(0.298300) + FlowNavigationPathDiagnosticCapture/self(0.290700) + FlowNavigationPathWorldStateEnumeration/self(0.288000) + FlowMovingTargetPolicyAnchorPublish/self(0.277300) + FlowNavigationPathInitializeHierarchySelection/self(0.246400) + FlowNavigationPathSliceInitialize/self(0.242300) + FlowCombatApproachSlotGenerate/self(0.235800) + FlowNavigationPolicyInitializeAuthority/self(0.215600) + FlowNavigationRequestSort/self(0.210300) + FlowCombatApproachCoreSetup/self(0.204600) + FlowNavigationDemandReuse/self(0.195400) + FlowCombatApproachFinalize/self(0.189400) + FlowNavigationDemandRemoveOther/self(0.178000) + FlowCombatApproachOccupancyCandidate/self(0.146100) + FlowNavigationPathSliceDispatchFinalization/self(0.136600) + FlowCombatApproachSlotCacheArrayMaterialize/self(0.136200) + LogicEntityTargeting/self(0.121300) + listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self(0.121100) + LogicEntityBrain/self(0.117400) + FlowCombatApproachOccupancyReservations/self(0.087400) + FlowCombatApproachOccupancyAgentSync/self(0.083800) + FlowCombatApproachOccupancySlotFilter/self(0.079500) + LogicEntityBaseAndBuffs/self(0.073000) + FlowNavigationPathInitializeHierarchyResolve/self(0.054700) + FlowTileQueueActiveDemand/self(0.051100) + FlowNavigationPolicyInitializePin/self(0.051000) + LogicEntityMoveCommit/self(0.047500) + FlowStableGoalMeasurementBegin/self(0.044700) + FlowNavigationPolicyInitializeConstruct/self(0.044500) + LogicMoveResolveProjection/self(0.042200) + FlowCombatApproachCoreAgentPreparation/self(0.040500) + LogicMoveResolvePrepare/self(0.039600) + listener:LogicInteractionAuthorityService+AuthorityListener/self(0.037500) + FlowCombatApproachOccupancy/self(0.037100) + LogicEntityFrameSetup/self(0.034700) + FlowNavigationAgentUpdate/self(0.031100) + FlowStableGoalMeasurementFinalize/self(0.031100) + FlowCombatApproachSlotCacheBuild/self(0.030400) + CharacterTargetingEvaluate/self(0.029300) + LogicEntityNavigationSync/self(0.028700) + FlowNavigationResolveRequests/self(0.028100) + LogicEntityAttack/self(0.027600) + FlowCombatApproachScore/self(0.026400) + FlowCombatApproachOccupancySnapshot/self(0.024300) + FlowNavigationDemandAgentPreparation/self(0.024200) + FlowNavigationResolveDemandBatch/self(0.023000) + FlowCombatApproachOccupancyBuckets/self(0.023000) + FlowMovingTargetPolicyInputResolution/self(0.018100) + FlowCombatApproachSlotDeduplicate/self(0.016100) + LogicEntityPostUpdate/self(0.015900) + FlowStableGoalBody/self(0.015800) + LogicEntityMoveResolve/self(0.015500) + FlowCombatApproachOccupancyBucketBuild/self(0.015400) + FlowCombatApproachOccupancyPrepareCacheState/self(0.014400) + FlowNavigationTileQueue/self(0.014400) + FlowCombatApproachOccupancyPrepareIteration/self(0.014400) + LogicEntityNavigationPositionSync/self(0.013200) + LogicFrameListenerCallbacks/self(0.011700) + FlowNavigationRequestPrune/self(0.011600) + FlowSteeringSetupAgent/self(0.011100) + FlowNavigationGoalConnectorInputCollection/self(0.011100) + FlowCombatApproachCoreScoreIslandFilter/self(0.011000) + FlowMovingTargetPolicyAnchorState/self(0.009400) + FlowMovingTargetPolicyAnchorDictionary/self(0.008800) + FlowCorridorPolicyAuthorityHash/self(0.008300) + FlowMovingTargetPolicyAnchorDictionaryCreate/self(0.007900) + FlowNavigationPathQueueProcessingUnattributed/self(0.007700) + FlowPrepareStartCell/self(0.007400) + FlowNavigationPathQueueCommitLoop/self(0.007200) + FlowCombatApproachDirectionSetup/self(0.006500) + LogicFrameTick/self(0.006100) + FlowWorldBuildQueue/self(0.006000) + FlowNavigationInactiveClear/self(0.006000) + FlowMovingTargetPolicyAnchorProjectionIslandResolve/self(0.005200) + FlowNavigationDemandAssembly/self(0.005100) + FlowNavigationPortalOwners/self(0.005000) + FlowNavigationDemandQueueMutation/self(0.005000) + FlowNavigationDemandSnapshotPublish/self(0.004900) + FlowNavigationPathWorldActivation/self(0.004700) + FlowNavigationDemandSourceBinding/self(0.004700) + FlowNavigationPathWorldRestore/self(0.004600) + listener:MAEntityLogicFrameSystem+PhaseListener/self(0.004500) + LogicMoveResolveRegionConstraint/self(0.004400) + FlowNavigationDemandSectorValidation/self(0.004300) + FlowNavigationDemandDispatch/self(0.004100) + FlowNavigationPathSliceDispatchPreparation/self(0.004100) + FlowSteeringSetupOccupancy/self(0.004100) + FlowNavigationPolicyInitializeLookup/self(0.004000) + FlowMovingTargetPolicyAnchorLookup/self(0.004000) + FlowNavigationPathBudgetAccounting/self(0.003800) + LogicEntityProjectile/self(0.003500) + FlowMovingTargetPolicyBatchEarlyPath/self(0.003400) + FlowSteeringSetupWorld/self(0.003300) + LogicEntityDamageResolve/self(0.003200) + listener:GroupMoveManager/self(0.002900) + FlowNavigationDemandQueueLookup/self(0.002600) + FlowTileQueueReferenceTrim/self(0.002500) + LogicMoveResolveRebuild/self(0.002400) + FlowNavigationDemandSourceLookup/self(0.002300) + FlowNavigationDemandPathValidation/self(0.002100) + LogicFrameListenerSnapshot/self(0.002000) + FlowCombatApproachOccupancyPrepareGuard/self(0.002000) + FlowNavigationPathBudgetEnd/self(0.001800) + FlowMovingTargetPolicyAnchorKeyBuild/self(0.001700) + CharacterMoveSetInput/self(0.001600) + FlowStableGoalPrologue/self(0.001600) + listener:TutorialManager/self(0.001100) + FlowMovingTargetPolicyBatchStatePublish/self(0.001000) + FlowCombatApproachCoreExpandedPhase/self(0.000900) + FlowTileQueueCommit/self(0.000900) + FlowMovingTargetPolicyAnchorNavState/self(0.000900) + FlowMovingTargetPolicyAnchorTargetResolve/self(0.000800) + FlowMovingTargetPolicyOutputInitialization/self(0.000800) + FlowRuntimeRebuildQueue/self(0.000700) + FlowGroupMove/self(0.000700) + FlowMovingTargetPolicyAnchorProjectionCacheCheck/self(0.000700) + FlowNavigationCommit/self(0.000600) + LogicMoveResolveBookkeeping/self(0.000500) + LogicEntityFrameComplete/self(0.000300) + FlowTileQueuePrune/self(0.000300) + FlowTileQueueMovingTargetProjection/self(0.000300) + FlowTileQueueSharedGoal/self(0.000300) + FlowSourceGate/self(0.000100) + LogicFrameListenerAttributed/self(0.000000)
