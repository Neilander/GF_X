# Flow Cold Audit Acceptance

- Fixed entry: Logs/Lv2PullChasePerformance.txt, RESULT=PASS, UTC 2026-09-05T14:19:09.4138280Z to 14:19:46.3107225Z.
- Measurement: logic=80/render=131, raw Tick=346486 at 10000000 Hz, 34.648600ms. Exclusive tree/raw/sample/sum verifier PASS.
- Focused EditMode: 20/20 passed, job 9549e495a0f74a139e6ce4dd080dc2d7 (capture, Replay, Checkpoint, capacity).
- Follow-up regression: 2/2 passed, job ebea94926d5b4f4080c865ff958ec04c (authored cost and missing NavigationSync snapshot contract).
- Full Flow fixture: FAILED, job c5095c9a63b640239c337c27b1922654, 338/338 executed. Failure list from MCP is capped; no total failed count was supplied. These failures are not waived by focused checks.
- Overall goal remains OPEN. Cold numbers are first-record minus subsequent-record estimates, not isolated equal-work JIT measurements. Full fixture failures and broader performance targets are not declared passed.

## Reported Failures

- FlowFieldCrowdMovementSystemTests.AuthoredCostField的动态障碍模糊使用同一定点墙距: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.ColliderObstacle注册使用Transform世界几何而非滞后PhysicsBounds: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.DeterministicTile方向不会指向不可穿越邻格:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.FixedSteering_同Tick同目标使用稳定预约分流:   Expected: not equal to x:4.5 y:2.5   But was:  x:4.5 y:2.5
- FlowFieldCrowdMovementSystemTests.FlowTileBuildQueue会在后续查询前预构建路径Tile链:   flow tile queue 应能在后续查询前预构建下游 tile 链，tileCount=1   Expected: greater than 1
- FlowFieldCrowdMovementSystemTests.NavigationAuthorityConfig_DoesNotReadFloatValues:   Expected: String containing "ResolveConfiguredAgentTypeRadiusFixed"   But was:  "using System;
- FlowFieldCrowdMovementSystemTests.NavigationDeterministicHash_IsStableAndTracksFutureAffectingState: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest切换MovingTargetIdentity必须立即失效旧CommittedPlan:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest动态Dirty必须清除已发布但未提交的SharedSuffix:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest同MovingTarget跨Sector局部Binding不得扩张共享Route:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest同目标Source规模必须暴露各自Merge与共享Suffix工作量:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest移动目标同Sector换格不重建高层:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest移动目标跨Sector在完成Request上原位重绑定GoalPolicy:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest移动目标连续跨Sector不得重启Partial且完成后原子替换CommittedPlan:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationPathRequest移动目标高Quota同TickFollowUp必须先提交旧PolicyAuthority:   Expected: 0   But was:  1
- FlowFieldCrowdMovementSystemTests.NavigationSync同Tick同MovingTarget不同RawGoalGroup必须分事务且保持单Policy槽:   Expected: True   But was:  False
- FlowFieldCrowdMovementSystemTests.NavigationSync同Tick多Source同目标只初始化一次目标侧Policy:   真实多 sector request 的 graph walker 必须跨越多个 pop/邻接 primitive 后才返回 managed。   Expected: greater than or equal to 8
- FlowFieldCrowdMovementSystemTests.NavigationSync按反向依赖和配额提交PortalTile链:   三 Sector corridor 必须声明 final tile 和两个上游 portal tile。   Expected: 3
- FlowFieldCrowdMovementSystemTests.PlayerCompilationBoundary_ExcludesEditorBakeApiButKeepsRuntimeFailureDiagnostics: System.InvalidOperationException : Required source line was not found: public static FlowNavigationGridAsset.DerivedNavigationData BuildDerivedNavigationDataForAsset(
- FlowFieldCrowdMovementSystemTests.RuntimeDirtyCommit不会留下旧FloatTileJob且会清理共享场Job: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.RuntimeDirtyCostField使用定点直线和对角墙距: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.RuntimeDirtyPortalTransitions_ProcessAtMostFixedSourceQuotaPerQueueTick: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.RuntimeDirtyQueue会分帧重建IslandField: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=14 world=1.
- FlowFieldCrowdMovementSystemTests.RuntimeDirtyQueue会原子提交运行时障碍重建: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.
- FlowFieldCrowdMovementSystemTests.RuntimeDirty建筑障碍重建后保留地面锚点高度: System.InvalidOperationException : Goal projection spatial index found a walkable cell without a finalized island. cell=0 world=1.

