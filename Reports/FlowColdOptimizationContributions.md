# Flow Cold Contribution Audit

Status: FIRST_OCCURRENCE_COLD_ESTIMATE; finest-boundary review and acceptance remain OPEN.
Source SHA256: 0679B0578D793B989B513B5214A40434C5094258289210C0C6773ED60346E3E1
Logic frame: 80; total Tick: 21.891500 ms; exclusive residual: 0 ticks.
Exclusive terms: 161; measured >0.1ms: 45. No historical item-count limit.

Each /self term is inclusive duration minus the direct children of that invocation. Parent totals and alias records are excluded. FirstInTick is the first observed invocation in the target Tick, not necessarily the first invocation in the run. Hot is the mean of subsequent exclusive invocations. ColdContribution equals FirstInTick minus Hot only when that scope first occurred in the target Tick; otherwise it is zero. This is an empirical first-occurrence estimate, not proof of JIT causation; differing branch work can contribute to the difference. Terms <=0.1ms have no inferred cold value.

| Exclusive term | Tick ms | First record frame | First in max Tick | First in Tick ms | Hot ms | First-hot ms | Cold contribution ms | Hot samples |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |
| FlowCombatApproachSlotClearance/self | 1.467600 | 80 | True | 1.107500 | 0.360100 | 0.747400 | 0.747400 | n=1; frames=80 |
| FlowNavigationPathQueueLoopUnattributed/self | 1.177600 | 80 | True | 1.175500 | 0.001050 | 1.174450 | 1.174450 | n=2; frames=80 |
| EntityBrainCombat/self | 1.004800 | 80 | True | 0.995800 | 0.001286 | 0.994514 | 0.994514 | n=7; frames=80 |
| FlowNavigationPathSliceGoalConnector/self | 0.803600 | 80 | True | 0.123600 | 0.113333 | 0.010267 | 0.010267 | n=6; frames=80 |
| FlowNavigationPathInitializePolicy/self | 0.736400 | 80 | True | 0.735400 | 0.001000 | 0.734400 | 0.734400 | n=1; frames=80 |
| FlowNavigationPathQueueEligibility/self | 0.732200 | 80 | True | 0.729300 | 0.002900 | 0.726400 | 0.726400 | n=1; frames=80 |
| FlowCombatApproachSlotLineOfSight/self | 0.730200 | 80 | True | 0.430300 | 0.299900 | 0.130400 | 0.130400 | n=1; frames=80 |
| FlowNavigationDemandQueueCreate/self | 0.700300 | 80 | True | 0.687300 | 0.013000 | 0.674300 | 0.674300 | n=1; frames=80 |
| FlowCombatApproachPreResolve/self | 0.693200 | 80 | True | 0.684900 | 0.001186 | 0.683714 | 0.683714 | n=7; frames=80 |
| FlowNavigationGoalConnectorSearchSlice/self | 0.676300 | 80 | True | 0.474600 | 0.100850 | 0.373750 | 0.373750 | n=2; frames=80 |
| FlowNavigationDemandSourceInsert/self | 0.642500 | 80 | True | 0.237000 | 0.057929 | 0.179071 | 0.179071 | n=7; frames=80 |
| FlowMovingTargetPolicyAnchorGoalState/self | 0.593400 | 80 | True | 0.592300 | 0.001100 | 0.591200 | 0.591200 | n=1; frames=80 |
| FlowNavigationPolicyInitializeAnchor/self | 0.587500 | 80 | True | 0.586500 | 0.001000 | 0.585500 | 0.585500 | n=1; frames=80 |
| FlowCombatApproach/self | 0.584100 | 80 | True | 0.582100 | 0.000286 | 0.581814 | 0.581814 | n=7; frames=80 |
| FlowCombatApproachScoreArithmetic/self | 0.547000 | 80 | True | 0.482900 | 0.009157 | 0.473743 | 0.473743 | n=7; frames=80 |
| LogicMoveResolveStaticSolver/self | 0.545300 | 1 | False | 0.185000 | 0.120100 | 0.064900 | 0.000000 | n=3; frames=80 |
| CharacterMovePrepare/self | 0.512200 | 1 | False | 0.000200 | 0.013128 | -0.012928 | 0.000000 | n=39; frames=80 |
| FlowCombatApproachSlotGenerate/self | 0.500300 | 80 | True | 0.413800 | 0.086500 | 0.327300 | 0.327300 | n=1; frames=80 |
| FlowCombatApproachSlotCacheLookup/self | 0.475900 | 80 | True | 0.388400 | 0.012500 | 0.375900 | 0.375900 | n=7; frames=80 |
| FlowCombatApproachSlotCachePublish/self | 0.445200 | 80 | True | 0.445000 | 0.000200 | 0.444800 | 0.444800 | n=1; frames=80 |
| FlowNavigationPathInitializeHierarchySelection/self | 0.438100 | 80 | True | 0.437900 | 0.000200 | 0.437700 | 0.437700 | n=1; frames=80 |
| FlowNavigationPathWorldStateEnumeration/self | 0.433300 | 80 | True | 0.433300 | 0.002533 | 0.430767 | 0.430767 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowMovingTargetPolicyBatchResolutionLookup/self | 0.411400 | 80 | True | 0.318500 | 0.013271 | 0.305229 | 0.305229 | n=7; frames=80 |
| LogicMoveResolvePairSolver/self | 0.400500 | 1 | False | 0.106700 | 0.097933 | 0.008767 | 0.000000 | n=3; frames=80 |
| FlowMovingTargetPolicyAnchorDictionaryInsert/self | 0.388000 | 80 | True | 0.387600 | 0.000400 | 0.387200 | 0.387200 | n=1; frames=80 |
| FlowCombatApproachOccupancyBucketFill/self | 0.330800 | 80 | True | 0.330800 | 0.009133 | 0.321667 | 0.321667 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowMovingTargetPolicyClassification/self | 0.320800 | 80 | True | 0.319800 | 0.001000 | 0.318800 | 0.318800 | n=1; frames=80 |
| CharacterTargetingCandidateScan/self | 0.302300 | 38 | False | 0.033800 | 0.008950 | 0.024850 | 0.000000 | n=30; frames=80 |
| FlowMovingTargetPolicyAnchorDictionaryLookup/self | 0.288700 | 80 | True | 0.288600 | 0.000100 | 0.288500 | 0.288500 | n=1; frames=80 |
| LogicEntityMoveIntent/self | 0.285700 | 1 | False | 0.285700 | 0.075567 | 0.210133 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowNavigationPathDiagnosticCapture/self | 0.252100 | 80 | True | 0.252100 | 0.001533 | 0.250567 | 0.250567 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowNavigationPolicyInitializeAuthority/self | 0.239800 | 80 | True | 0.238900 | 0.000900 | 0.238000 | 0.238000 | n=1; frames=80 |
| FlowNavigationRequestSort/self | 0.203000 | 80 | True | 0.203000 | 0.002000 | 0.201000 | 0.201000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachFinalize/self | 0.200500 | 80 | True | 0.199100 | 0.000200 | 0.198900 | 0.198900 | n=7; frames=80 |
| FlowNavigationDemandRemoveOther/self | 0.193900 | 80 | True | 0.193000 | 0.000150 | 0.192850 | 0.192850 | n=6; frames=80 |
| FlowNavigationPathSliceDispatchFinalization/self | 0.191700 | 80 | True | 0.188900 | 0.000350 | 0.188550 | 0.188550 | n=8; frames=80 |
| FlowCombatApproachCoreScorePhase/self | 0.185600 | 80 | True | 0.017300 | 0.024043 | -0.006743 | -0.006743 | n=7; frames=80 |
| FlowCombatApproachCoreSetup/self | 0.156300 | 80 | True | 0.155500 | 0.000114 | 0.155386 | 0.155386 | n=7; frames=80 |
| FlowMovingTargetPolicyAnchorPublish/self | 0.146700 | 80 | True | 0.146500 | 0.000200 | 0.146300 | 0.146300 | n=1; frames=80 |
| FlowNavigationDemandEnqueue/self | 0.144300 | 80 | True | 0.138700 | 0.000800 | 0.137900 | 0.137900 | n=7; frames=80 |
| FlowCombatApproachSlotCacheArrayMaterialize/self | 0.141200 | 80 | True | 0.128900 | 0.012300 | 0.116600 | 0.116600 | n=1; frames=80 |
| listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self | 0.117900 | 1 | False | 0.117900 | 0.115967 | 0.001933 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachOccupancyPrepareInitialization/self | 0.114600 | 80 | True | 0.114200 | 0.000400 | 0.113800 | 0.113800 | n=1; frames=80 |
| LogicEntityTargeting/self | 0.114000 | 1 | False | 0.114000 | 0.064822 | 0.049178 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| LogicEntityBrain/self | 0.101800 | 1 | False | 0.101800 | 0.073167 | 0.028633 | 0.000000 | n=9; frames=81,82,83,84,85,87,88,89,98 |
| FlowCombatApproachOccupancyReservations/self | 0.091100 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityBaseAndBuffs/self | 0.069300 | 1 | False | 0.069300 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancySlotFilter/self | 0.065700 | 80 | True | 0.047600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyAgentSync/self | 0.064000 | 80 | True | 0.064000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializePin/self | 0.058300 | 80 | True | 0.058000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathInitializeHierarchyResolve/self | 0.058000 | 80 | True | 0.057200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializeConstruct/self | 0.047500 | 80 | True | 0.032000 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueActiveDemand/self | 0.045800 | 1 | False | 0.032800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancy/self | 0.038900 | 80 | True | 0.003100 | not measured | not measured | not measured | n=0; frames= |
| listener:LogicInteractionAuthorityService+AuthorityListener/self | 0.035300 | 1 | False | 0.035300 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityFrameSetup/self | 0.033900 | 1 | False | 0.033900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationResolveDemandBatch/self | 0.033500 | 80 | True | 0.033500 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalMeasurementBegin/self | 0.033100 | 80 | True | 0.032400 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveProjection/self | 0.032700 | 1 | False | 0.010200 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreAgentPreparation/self | 0.031400 | 80 | True | 0.018300 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachScore/self | 0.030900 | 80 | True | 0.003400 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachSlotCacheBuild/self | 0.029200 | 80 | True | 0.025300 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationRequestPrune/self | 0.028400 | 1 | False | 0.028400 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandAgentPreparation/self | 0.028400 | 80 | True | 0.006600 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationResolveRequests/self | 0.027900 | 1 | False | 0.027900 | not measured | not measured | not measured | n=0; frames= |
| CharacterTargetingEvaluate/self | 0.027500 | 38 | False | 0.002800 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityMoveCommit/self | 0.025900 | 1 | False | 0.025900 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalMeasurementFinalize/self | 0.025700 | 80 | True | 0.025200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationAgentUpdate/self | 0.025300 | 1 | False | 0.003000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancySnapshot/self | 0.024300 | 80 | True | 0.011500 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalBody/self | 0.024100 | 80 | True | 0.008900 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCore/self | 0.023300 | 80 | True | 0.017100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandResolve/self | 0.021800 | 80 | True | 0.014500 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSourceLookup/self | 0.021300 | 80 | True | 0.012900 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyBuckets/self | 0.020900 | 80 | True | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachSlotDeduplicate/self | 0.019800 | 80 | True | 0.004200 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityAttack/self | 0.019700 | 1 | False | 0.019700 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityNavigationSync/self | 0.019200 | 1 | False | 0.019200 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareIteration/self | 0.016900 | 80 | True | 0.009600 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreScoreIslandFilter/self | 0.015100 | 80 | True | 0.001000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationTileQueue/self | 0.014600 | 1 | False | 0.014600 | not measured | not measured | not measured | n=0; frames= |
| CharacterMoveSteering/self | 0.014100 | 80 | True | 0.010400 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolvePrepare/self | 0.013500 | 1 | False | 0.013500 | not measured | not measured | not measured | n=0; frames= |
| FlowPrepareStableGoal/self | 0.012900 | 80 | True | 0.008100 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyInputResolution/self | 0.012900 | 80 | True | 0.008900 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityPostUpdate/self | 0.012300 | 1 | False | 0.012300 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPreparation/self | 0.011900 | 80 | True | 0.009600 | not measured | not measured | not measured | n=0; frames= |
| FlowPrepareStartCell/self | 0.011100 | 80 | True | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationGoalConnectorInputCollection/self | 0.010300 | 80 | True | 0.004800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareCacheState/self | 0.010300 | 80 | True | 0.008500 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerCallbacks/self | 0.010100 | 1 | False | 0.010100 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityNavigationPositionSync/self | 0.010100 | 1 | False | 0.010100 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityMoveResolve/self | 0.009900 | 1 | False | 0.009900 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyBucketBuild/self | 0.009600 | 80 | True | 0.009600 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathSliceDispatch/self | 0.009400 | 80 | True | 0.008500 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSourceBinding/self | 0.009400 | 80 | True | 0.005700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathSliceInitialize/self | 0.009300 | 80 | True | 0.008600 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupAgent/self | 0.008600 | 80 | True | 0.006900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathQueueCommitLoop/self | 0.008600 | 80 | True | 0.008300 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathWorldOuterUnattributed/self | 0.008500 | 80 | True | 0.008500 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachSlotCache/self | 0.008400 | 80 | True | 0.006100 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorState/self | 0.008200 | 80 | True | 0.007500 | not measured | not measured | not measured | n=0; frames= |
| FlowCorridorPolicyAuthorityHash/self | 0.007700 | 80 | True | 0.005800 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathQueueProcessingUnattributed/self | 0.007100 | 80 | True | 0.007100 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorDictionary/self | 0.006800 | 80 | True | 0.006300 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationInactiveClear/self | 0.006200 | 1 | False | 0.001000 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandQueueMutation/self | 0.006000 | 80 | True | 0.004800 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorDictionaryCreate/self | 0.005900 | 80 | True | 0.005000 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameTick/self | 0.005900 | 1 | False | 0.005900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandAssembly/self | 0.005800 | 80 | True | 0.002400 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueCommit/self | 0.005300 | 1 | False | 0.005200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSnapshotPublish/self | 0.005200 | 80 | True | 0.003300 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveRegionConstraint/self | 0.005200 | 1 | False | 0.001800 | not measured | not measured | not measured | n=0; frames= |
| FlowWorldBuildQueue/self | 0.005100 | 1 | False | 0.005100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathWorldActivation/self | 0.004900 | 80 | True | 0.004200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPortalOwners/self | 0.004800 | 1 | False | 0.004800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachDirectionSetup/self | 0.004600 | 80 | True | 0.003500 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorLookup/self | 0.004400 | 80 | True | 0.001700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyBatchEarlyPath/self | 0.004400 | 80 | True | 0.002200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathSliceDispatchPreparation/self | 0.004400 | 80 | True | 0.004200 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationResolvePathQueueBatch/self | 0.004300 | 80 | True | 0.004300 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityProjectile/self | 0.004300 | 1 | False | 0.004300 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorProjectionIslandResolve/self | 0.004200 | 80 | True | 0.003600 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandDispatch/self | 0.004000 | 80 | True | 0.002400 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPolicyInitializeLookup/self | 0.004000 | 80 | True | 0.003500 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandQueueLookup/self | 0.003900 | 80 | True | 0.002500 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupWorld/self | 0.003700 | 80 | True | 0.002900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandSectorValidation/self | 0.003600 | 80 | True | 0.002700 | not measured | not measured | not measured | n=0; frames= |
| FlowSteeringSetupOccupancy/self | 0.003600 | 80 | True | 0.002800 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyCandidate/self | 0.003600 | 80 | True | 0.001400 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathBudgetAccounting/self | 0.003500 | 80 | True | 0.002900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandReuse/self | 0.003500 | 80 | True | 0.002800 | not measured | not measured | not measured | n=0; frames= |
| listener:MAEntityLogicFrameSystem+PhaseListener/self | 0.003200 | 1 | False | 0.003200 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityDamageResolve/self | 0.002800 | 1 | False | 0.002800 | not measured | not measured | not measured | n=0; frames= |
| listener:GroupMoveManager/self | 0.002800 | 1 | False | 0.002800 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueReferenceTrim/self | 0.002600 | 1 | False | 0.001500 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerSnapshot/self | 0.002500 | 1 | False | 0.002500 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveRebuild/self | 0.002400 | 1 | False | 0.000900 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathBudgetEnd/self | 0.002100 | 80 | True | 0.002100 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationDemandPathValidation/self | 0.002100 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationPathWorldRestore/self | 0.002000 | 80 | True | 0.002000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachOccupancyPrepareGuard/self | 0.001800 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorKeyBuild/self | 0.001700 | 80 | True | 0.001700 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyBatchStatePublish/self | 0.001600 | 80 | True | 0.001000 | not measured | not measured | not measured | n=0; frames= |
| FlowStableGoalPrologue/self | 0.001400 | 80 | True | 0.001300 | not measured | not measured | not measured | n=0; frames= |
| CharacterMoveSetInput/self | 0.001100 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| listener:TutorialManager/self | 0.001000 | 1 | False | 0.001000 | not measured | not measured | not measured | n=0; frames= |
| FlowCombatApproachCoreExpandedPhase/self | 0.000900 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| FlowGroupMove/self | 0.000900 | 1 | False | 0.000900 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorNavState/self | 0.000800 | 80 | True | 0.000800 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyOutputInitialization/self | 0.000800 | 80 | True | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorProjectionCacheCheck/self | 0.000700 | 80 | True | 0.000700 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueuePrune/self | 0.000700 | 1 | False | 0.000500 | not measured | not measured | not measured | n=0; frames= |
| FlowMovingTargetPolicyAnchorTargetResolve/self | 0.000700 | 80 | True | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| FlowNavigationCommit/self | 0.000600 | 1 | False | 0.000600 | not measured | not measured | not measured | n=0; frames= |
| LogicEntityFrameComplete/self | 0.000400 | 1 | False | 0.000400 | not measured | not measured | not measured | n=0; frames= |
| LogicMoveResolveBookkeeping/self | 0.000400 | 1 | False | 0.000400 | not measured | not measured | not measured | n=0; frames= |
| FlowRuntimeRebuildQueue/self | 0.000400 | 1 | False | 0.000400 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueMovingTargetProjection/self | 0.000300 | 1 | False | 0.000300 | not measured | not measured | not measured | n=0; frames= |
| FlowTileQueueSharedGoal/self | 0.000200 | 1 | False | 0.000200 | not measured | not measured | not measured | n=0; frames= |
| LogicFrameListenerAttributed/self | 0.000000 | 1 | False | 0.000000 | not measured | not measured | not measured | n=0; frames= |

Measured >0.1ms first-occurrence cold estimate (ms; <=0.1ms cold contributions are not measured):

21.891500 * 0.650110556065 = 14.231895 ms = FlowCombatApproachSlotClearance/self(0.747400) + FlowNavigationPathQueueLoopUnattributed/self(1.174450) + EntityBrainCombat/self(0.994514) + FlowNavigationPathSliceGoalConnector/self(0.010267) + FlowNavigationPathInitializePolicy/self(0.734400) + FlowNavigationPathQueueEligibility/self(0.726400) + FlowCombatApproachSlotLineOfSight/self(0.130400) + FlowNavigationDemandQueueCreate/self(0.674300) + FlowCombatApproachPreResolve/self(0.683714) + FlowNavigationGoalConnectorSearchSlice/self(0.373750) + FlowNavigationDemandSourceInsert/self(0.179071) + FlowMovingTargetPolicyAnchorGoalState/self(0.591200) + FlowNavigationPolicyInitializeAnchor/self(0.585500) + FlowCombatApproach/self(0.581814) + FlowCombatApproachScoreArithmetic/self(0.473743) + LogicMoveResolveStaticSolver/self(0.000000) + CharacterMovePrepare/self(0.000000) + FlowCombatApproachSlotGenerate/self(0.327300) + FlowCombatApproachSlotCacheLookup/self(0.375900) + FlowCombatApproachSlotCachePublish/self(0.444800) + FlowNavigationPathInitializeHierarchySelection/self(0.437700) + FlowNavigationPathWorldStateEnumeration/self(0.430767) + FlowMovingTargetPolicyBatchResolutionLookup/self(0.305229) + LogicMoveResolvePairSolver/self(0.000000) + FlowMovingTargetPolicyAnchorDictionaryInsert/self(0.387200) + FlowCombatApproachOccupancyBucketFill/self(0.321667) + FlowMovingTargetPolicyClassification/self(0.318800) + CharacterTargetingCandidateScan/self(0.000000) + FlowMovingTargetPolicyAnchorDictionaryLookup/self(0.288500) + LogicEntityMoveIntent/self(0.000000) + FlowNavigationPathDiagnosticCapture/self(0.250567) + FlowNavigationPolicyInitializeAuthority/self(0.238000) + FlowNavigationRequestSort/self(0.201000) + FlowCombatApproachFinalize/self(0.198900) + FlowNavigationDemandRemoveOther/self(0.192850) + FlowNavigationPathSliceDispatchFinalization/self(0.188550) + FlowCombatApproachCoreScorePhase/self(-0.006743) + FlowCombatApproachCoreSetup/self(0.155386) + FlowMovingTargetPolicyAnchorPublish/self(0.146300) + FlowNavigationDemandEnqueue/self(0.137900) + FlowCombatApproachSlotCacheArrayMaterialize/self(0.116600) + listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self(0.000000) + FlowCombatApproachOccupancyPrepareInitialization/self(0.113800) + LogicEntityTargeting/self(0.000000) + LogicEntityBrain/self(0.000000)

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
- FlowStableGoalProfilerRecord: 0.020600 ms -> observer-overhead-already-in-inclusive-scopes
- FlowNavigationGoalConnectorUnattributed: 0.796900 ms -> FlowNavigationPathSliceGoalConnector/self
- FlowCombatApproachOccupancyPrepareSlotLookup: 0.231800 ms -> FlowCombatApproachOccupancyPrepareIteration
- FlowCombatApproachCoreScoreUnattributed: 0.063600 ms -> FlowCombatApproachCoreScorePhase/self
- FlowCombatApproachCoreUnattributed: 0.017700 ms -> FlowCombatApproachCore/self
- FlowCombatApproachOccupancyBucketIncremental: 0.330800 ms -> FlowCombatApproachOccupancyBucketFill
- FlowNavigationPathSliceDispatchUnattributed: 0.205500 ms -> FlowNavigationPathSliceDispatch/self
- FlowNavigationPathAdvance: 6.267000 ms -> FlowNavigationResolvePathQueueBatch
- LogicFrameListenerUnattributed: 0.010100 ms -> LogicFrameListenerCallbacks/self

Full current Tick equation (ms):

21.891500 = FlowCombatApproachSlotClearance/self(1.467600) + FlowNavigationPathQueueLoopUnattributed/self(1.177600) + EntityBrainCombat/self(1.004800) + FlowNavigationPathSliceGoalConnector/self(0.803600) + FlowNavigationPathInitializePolicy/self(0.736400) + FlowNavigationPathQueueEligibility/self(0.732200) + FlowCombatApproachSlotLineOfSight/self(0.730200) + FlowNavigationDemandQueueCreate/self(0.700300) + FlowCombatApproachPreResolve/self(0.693200) + FlowNavigationGoalConnectorSearchSlice/self(0.676300) + FlowNavigationDemandSourceInsert/self(0.642500) + FlowMovingTargetPolicyAnchorGoalState/self(0.593400) + FlowNavigationPolicyInitializeAnchor/self(0.587500) + FlowCombatApproach/self(0.584100) + FlowCombatApproachScoreArithmetic/self(0.547000) + LogicMoveResolveStaticSolver/self(0.545300) + CharacterMovePrepare/self(0.512200) + FlowCombatApproachSlotGenerate/self(0.500300) + FlowCombatApproachSlotCacheLookup/self(0.475900) + FlowCombatApproachSlotCachePublish/self(0.445200) + FlowNavigationPathInitializeHierarchySelection/self(0.438100) + FlowNavigationPathWorldStateEnumeration/self(0.433300) + FlowMovingTargetPolicyBatchResolutionLookup/self(0.411400) + LogicMoveResolvePairSolver/self(0.400500) + FlowMovingTargetPolicyAnchorDictionaryInsert/self(0.388000) + FlowCombatApproachOccupancyBucketFill/self(0.330800) + FlowMovingTargetPolicyClassification/self(0.320800) + CharacterTargetingCandidateScan/self(0.302300) + FlowMovingTargetPolicyAnchorDictionaryLookup/self(0.288700) + LogicEntityMoveIntent/self(0.285700) + FlowNavigationPathDiagnosticCapture/self(0.252100) + FlowNavigationPolicyInitializeAuthority/self(0.239800) + FlowNavigationRequestSort/self(0.203000) + FlowCombatApproachFinalize/self(0.200500) + FlowNavigationDemandRemoveOther/self(0.193900) + FlowNavigationPathSliceDispatchFinalization/self(0.191700) + FlowCombatApproachCoreScorePhase/self(0.185600) + FlowCombatApproachCoreSetup/self(0.156300) + FlowMovingTargetPolicyAnchorPublish/self(0.146700) + FlowNavigationDemandEnqueue/self(0.144300) + FlowCombatApproachSlotCacheArrayMaterialize/self(0.141200) + listener:LogicEntityFrameSnapshotService+SnapshotCaptureListener/self(0.117900) + FlowCombatApproachOccupancyPrepareInitialization/self(0.114600) + LogicEntityTargeting/self(0.114000) + LogicEntityBrain/self(0.101800) + FlowCombatApproachOccupancyReservations/self(0.091100) + LogicEntityBaseAndBuffs/self(0.069300) + FlowCombatApproachOccupancySlotFilter/self(0.065700) + FlowCombatApproachOccupancyAgentSync/self(0.064000) + FlowNavigationPolicyInitializePin/self(0.058300) + FlowNavigationPathInitializeHierarchyResolve/self(0.058000) + FlowNavigationPolicyInitializeConstruct/self(0.047500) + FlowTileQueueActiveDemand/self(0.045800) + FlowCombatApproachOccupancy/self(0.038900) + listener:LogicInteractionAuthorityService+AuthorityListener/self(0.035300) + LogicEntityFrameSetup/self(0.033900) + FlowNavigationResolveDemandBatch/self(0.033500) + FlowStableGoalMeasurementBegin/self(0.033100) + LogicMoveResolveProjection/self(0.032700) + FlowCombatApproachCoreAgentPreparation/self(0.031400) + FlowCombatApproachScore/self(0.030900) + FlowCombatApproachSlotCacheBuild/self(0.029200) + FlowNavigationRequestPrune/self(0.028400) + FlowNavigationDemandAgentPreparation/self(0.028400) + FlowNavigationResolveRequests/self(0.027900) + CharacterTargetingEvaluate/self(0.027500) + LogicEntityMoveCommit/self(0.025900) + FlowStableGoalMeasurementFinalize/self(0.025700) + FlowNavigationAgentUpdate/self(0.025300) + FlowCombatApproachOccupancySnapshot/self(0.024300) + FlowStableGoalBody/self(0.024100) + FlowCombatApproachCore/self(0.023300) + FlowNavigationDemandResolve/self(0.021800) + FlowNavigationDemandSourceLookup/self(0.021300) + FlowCombatApproachOccupancyBuckets/self(0.020900) + FlowCombatApproachSlotDeduplicate/self(0.019800) + LogicEntityAttack/self(0.019700) + LogicEntityNavigationSync/self(0.019200) + FlowCombatApproachOccupancyPrepareIteration/self(0.016900) + FlowCombatApproachCoreScoreIslandFilter/self(0.015100) + FlowNavigationTileQueue/self(0.014600) + CharacterMoveSteering/self(0.014100) + LogicMoveResolvePrepare/self(0.013500) + FlowPrepareStableGoal/self(0.012900) + FlowMovingTargetPolicyInputResolution/self(0.012900) + LogicEntityPostUpdate/self(0.012300) + FlowCombatApproachOccupancyPreparation/self(0.011900) + FlowPrepareStartCell/self(0.011100) + FlowNavigationGoalConnectorInputCollection/self(0.010300) + FlowCombatApproachOccupancyPrepareCacheState/self(0.010300) + LogicFrameListenerCallbacks/self(0.010100) + LogicEntityNavigationPositionSync/self(0.010100) + LogicEntityMoveResolve/self(0.009900) + FlowCombatApproachOccupancyBucketBuild/self(0.009600) + FlowNavigationPathSliceDispatch/self(0.009400) + FlowNavigationDemandSourceBinding/self(0.009400) + FlowNavigationPathSliceInitialize/self(0.009300) + FlowSteeringSetupAgent/self(0.008600) + FlowNavigationPathQueueCommitLoop/self(0.008600) + FlowNavigationPathWorldOuterUnattributed/self(0.008500) + FlowCombatApproachSlotCache/self(0.008400) + FlowMovingTargetPolicyAnchorState/self(0.008200) + FlowCorridorPolicyAuthorityHash/self(0.007700) + FlowNavigationPathQueueProcessingUnattributed/self(0.007100) + FlowMovingTargetPolicyAnchorDictionary/self(0.006800) + FlowNavigationInactiveClear/self(0.006200) + FlowNavigationDemandQueueMutation/self(0.006000) + FlowMovingTargetPolicyAnchorDictionaryCreate/self(0.005900) + LogicFrameTick/self(0.005900) + FlowNavigationDemandAssembly/self(0.005800) + FlowTileQueueCommit/self(0.005300) + FlowNavigationDemandSnapshotPublish/self(0.005200) + LogicMoveResolveRegionConstraint/self(0.005200) + FlowWorldBuildQueue/self(0.005100) + FlowNavigationPathWorldActivation/self(0.004900) + FlowNavigationPortalOwners/self(0.004800) + FlowCombatApproachDirectionSetup/self(0.004600) + FlowMovingTargetPolicyAnchorLookup/self(0.004400) + FlowMovingTargetPolicyBatchEarlyPath/self(0.004400) + FlowNavigationPathSliceDispatchPreparation/self(0.004400) + FlowNavigationResolvePathQueueBatch/self(0.004300) + LogicEntityProjectile/self(0.004300) + FlowMovingTargetPolicyAnchorProjectionIslandResolve/self(0.004200) + FlowNavigationDemandDispatch/self(0.004000) + FlowNavigationPolicyInitializeLookup/self(0.004000) + FlowNavigationDemandQueueLookup/self(0.003900) + FlowSteeringSetupWorld/self(0.003700) + FlowNavigationDemandSectorValidation/self(0.003600) + FlowSteeringSetupOccupancy/self(0.003600) + FlowCombatApproachOccupancyCandidate/self(0.003600) + FlowNavigationPathBudgetAccounting/self(0.003500) + FlowNavigationDemandReuse/self(0.003500) + listener:MAEntityLogicFrameSystem+PhaseListener/self(0.003200) + LogicEntityDamageResolve/self(0.002800) + listener:GroupMoveManager/self(0.002800) + FlowTileQueueReferenceTrim/self(0.002600) + LogicFrameListenerSnapshot/self(0.002500) + LogicMoveResolveRebuild/self(0.002400) + FlowNavigationPathBudgetEnd/self(0.002100) + FlowNavigationDemandPathValidation/self(0.002100) + FlowNavigationPathWorldRestore/self(0.002000) + FlowCombatApproachOccupancyPrepareGuard/self(0.001800) + FlowMovingTargetPolicyAnchorKeyBuild/self(0.001700) + FlowMovingTargetPolicyBatchStatePublish/self(0.001600) + FlowStableGoalPrologue/self(0.001400) + CharacterMoveSetInput/self(0.001100) + listener:TutorialManager/self(0.001000) + FlowCombatApproachCoreExpandedPhase/self(0.000900) + FlowGroupMove/self(0.000900) + FlowMovingTargetPolicyAnchorNavState/self(0.000800) + FlowMovingTargetPolicyOutputInitialization/self(0.000800) + FlowMovingTargetPolicyAnchorProjectionCacheCheck/self(0.000700) + FlowTileQueuePrune/self(0.000700) + FlowMovingTargetPolicyAnchorTargetResolve/self(0.000700) + FlowNavigationCommit/self(0.000600) + LogicEntityFrameComplete/self(0.000400) + LogicMoveResolveBookkeeping/self(0.000400) + FlowRuntimeRebuildQueue/self(0.000400) + FlowTileQueueMovingTargetProjection/self(0.000300) + FlowTileQueueSharedGoal/self(0.000200) + LogicFrameListenerAttributed/self(0.000000)
