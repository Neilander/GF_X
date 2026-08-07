using System;
using AAAGame.Card;
using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicMovementRegionConstraintServiceTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.BuildBeforeInvade);
        LogicMovementRegionConstraintService.BeginTimeline();
        InitializeStrongholds();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        if (LogicCardPlacementAuthority.IsActive)
            LogicCardPlacementAuthority.EndTimeline();
        if (LogicMovementRegionConstraintService.IsActive)
            LogicMovementRegionConstraintService.EndTimeline();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        LogicStrongholdMap.Clear();
    }

    [Test]
    public void Build_PlayerMoveIntoEnemyStronghold_IsClippedAtBoundary()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 start = StrongholdPoint(0);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            StrongholdPoint(1),
            out LogicMovementRegionConstraintFailure failure);

        Assert.Greater(resolved.x.RawValue, start.x.RawValue);
        Assert.Less(resolved.x.RawValue, StrongholdPoint(1).x.RawValue);
        Assert.AreEqual(LogicMovementRegionConstraintFailure.EnemyStronghold, failure);
    }

    [Test]
    public void Build_PlayerMoveInsideOwnedStronghold_IsAllowed()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 candidate = new FixVector2((Fix64)0.25f, Fix64.Zero);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            StrongholdPoint(0),
            candidate,
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(candidate, resolved);
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, failure);
    }

    [Test]
    public void Build_CircleOverlapsEnemyStrongholdBeforeCenterCrossesBoundary_IsClipped()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 start = new FixVector2((Fix64)0.2f, Fix64.Zero);
        FixVector2 candidate = new FixVector2((Fix64)0.3f, Fix64.Zero);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            candidate,
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(LogicMovementRegionConstraintFailure.EnemyStronghold, failure);
        Assert.Greater(resolved.x.RawValue, start.x.RawValue);
        Assert.Less(resolved.x.RawValue, candidate.x.RawValue);
        Assert.AreEqual(Fix64.Zero, resolved.y);
    }

    [Test]
    public void Build_HighSpeedMoveAcrossEnemyStronghold_IsSweptAndClipped()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 start = StrongholdPoint(0);
        FixVector2 candidate = StrongholdPoint(2);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            candidate,
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(LogicMovementRegionConstraintFailure.EnemyStronghold, failure);
        Assert.Greater(resolved.x.RawValue, start.x.RawValue);
        Assert.Less(resolved.x.RawValue, ((Fix64)0.5f).RawValue);
    }

    [Test]
    public void Build_DiagonalMoveAtEnemyStrongholdEdge_PreservesTangentialMotion()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 start = new FixVector2((Fix64)0.25f, (Fix64)(-1f));
        FixVector2 candidate = new FixVector2((Fix64)0.45f, (Fix64)(-0.5f));

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            candidate,
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(LogicMovementRegionConstraintFailure.EnemyStronghold, failure);
        Assert.AreEqual(candidate.x.RawValue, resolved.x.RawValue);
        Assert.Greater(resolved.y.RawValue, start.y.RawValue);
        Assert.Less(resolved.y.RawValue, candidate.y.RawValue);
    }

    [Test]
    public void AuthoredNonBinaryCellSize_DoesNotDriftAcrossDistantStrongholdBoundary()
    {
        LogicStrongholdMap.Clear();
        const float authoredCellSize = 1.4f;
        LogicStrongholdMap.InitializeFromAuthoredGrid(
            0f,
            0f,
            1f,
            0f,
            0f,
            1f,
            authoredCellSize,
            new[]
            {
                new LogicStrongholdCellDefinition("left", 46, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("right", 47, 0, EntitySideHelper.EnemyFactionId),
            });

        FixVector2 candidate = new FixVector2((Fix64)65.104f, Fix64.Zero);

        Assert.IsTrue(LogicStrongholdMap.TryResolveStrongholdId(candidate, out string strongholdId));
        Assert.AreEqual("right", strongholdId,
            "The logic boundary must match the authored 46.5 * 1.4 world boundary instead of accumulating Q12 cell-size error.");
    }

    [Test]
    public void Build_Lv3EnemyStrongholdCorner_UsesUnitCollisionRadiusWithoutWallPadding()
    {
        LogicStrongholdMap.Clear();
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.FromRaw(5735),
            new[]
            {
                new LogicStrongholdCellDefinition("enemy", 46, 8, EntitySideHelper.EnemyFactionId),
            });
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        player.SetProperty(
            CreatureMainProperty.CollisionRadius,
            DistanceUnitConverter.ConvertFromWorld(Fix64.FromRaw(1352)));
        FixVector2 start = new FixVector2(Fix64.FromRaw(259590), Fix64.FromRaw(42835));
        Fix64 collisionRadius = DistanceUnitConverter.ConvertToWorld(
            player.GetProperty(CreatureMainProperty.CollisionRadius));

        Assert.IsTrue(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
            start,
            collisionRadius,
            EntitySideHelper.PlayerFactionId));

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            start + new FixVector2(Fix64.FromRaw(128), Fix64.Zero),
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(LogicMovementRegionConstraintFailure.EnemyStronghold, failure);
        Assert.Greater(resolved.x.RawValue, start.x.RawValue);
        Assert.Less(resolved.x.RawValue, start.x.RawValue + 128);
        Assert.AreEqual(start.y.RawValue, resolved.y.RawValue);
        Assert.IsTrue(LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
            resolved,
            collisionRadius,
            EntitySideHelper.PlayerFactionId));
    }

    [TestCase(512)]
    [TestCase(1352)]
    [TestCase(2867)]
    public void Build_DifferentUnitRadii_AreBlockedOutsideEveryEdgeAndCorner(long collisionRadiusRaw)
    {
        LogicStrongholdMap.Clear();
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy", 0, 0, EntitySideHelper.EnemyFactionId),
            });
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        Fix64 collisionRadius = Fix64.FromRaw(collisionRadiusRaw);
        player.SetProperty(
            CreatureMainProperty.CollisionRadius,
            DistanceUnitConverter.ConvertFromWorld(collisionRadius));
        Fix64 boundary = Fix64.One / (Fix64)2 + collisionRadius;
        Fix64 approachDistance = Fix64.FromRaw(256);
        var directions = new[]
        {
            new FixVector2(-Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, -Fix64.One),
            new FixVector2(Fix64.Zero, Fix64.One),
            new FixVector2(-Fix64.One, -Fix64.One),
            new FixVector2(-Fix64.One, Fix64.One),
            new FixVector2(Fix64.One, -Fix64.One),
            new FixVector2(Fix64.One, Fix64.One),
        };

        for (int i = 0; i < directions.Length; i++)
        {
            FixVector2 direction = directions[i];
            FixVector2 start = direction * (boundary + approachDistance);
            FixVector2 candidate = direction * (boundary - approachDistance);
            Assert.IsTrue(
                LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                    start,
                    collisionRadius,
                    EntitySideHelper.PlayerFactionId),
                $"Start must be outside. radiusRaw={collisionRadiusRaw}, direction={direction}.");

            FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
                player,
                start,
                candidate,
                out LogicMovementRegionConstraintFailure failure);

            Assert.AreEqual(
                LogicMovementRegionConstraintFailure.EnemyStronghold,
                failure,
                $"Move must hit the enemy stronghold. radiusRaw={collisionRadiusRaw}, direction={direction}.");
            Assert.AreNotEqual(
                candidate,
                resolved,
                $"Move must be clipped. radiusRaw={collisionRadiusRaw}, direction={direction}.");
            Assert.IsTrue(
                LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                    resolved,
                    collisionRadius,
                    EntitySideHelper.PlayerFactionId),
                $"Resolved unit volume must remain outside. radiusRaw={collisionRadiusRaw}, direction={direction}, resolved={resolved}.");
        }
    }

    [Test]
    public void Invade_PlayerMoveIntoEnemyStronghold_IsAllowed()
    {
        LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        LogicTimeControlService.BeginFrame(1);
        LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 candidate = StrongholdPoint(1);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            StrongholdPoint(0),
            candidate,
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(candidate, resolved);
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, failure);
    }

    [Test]
    public void Build_EnemyUnitAndOutsideStrongholdMove_AreAllowed()
    {
        SimEntityContext enemy = CreateEntity(SideType.EnemySide);
        FixVector2 enemyCandidate = StrongholdPoint(1);
        Assert.AreEqual(
            enemyCandidate,
            LogicMovementRegionConstraintService.ResolvePosition(
                enemy,
                StrongholdPoint(0),
                enemyCandidate,
                out LogicMovementRegionConstraintFailure enemyFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, enemyFailure);

        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 outsideCandidate = StrongholdPoint(3);
        Assert.AreEqual(
            outsideCandidate,
            LogicMovementRegionConstraintService.ResolvePosition(
                player,
                StrongholdPoint(2),
                outsideCandidate,
                out LogicMovementRegionConstraintFailure outsideFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, outsideFailure);
    }

    [Test]
    public void TutorialBoundary_UsesFixedStrongholdMapAndEntersDeterministicState()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        var before = new LogicStateHasher();
        LogicMovementRegionConstraintService.WriteDeterministicState(before);

        LogicMovementRegionConstraintService.SetTutorialStrongholdBoundary("owned");
        var after = new LogicStateHasher();
        LogicMovementRegionConstraintService.WriteDeterministicState(after);
        FixVector2 start = StrongholdPoint(0);
        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            StrongholdPoint(-1),
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreNotEqual(before.Hash, after.Hash);
        Assert.AreEqual(start, resolved);
        Assert.AreEqual(LogicMovementRegionConstraintFailure.TutorialStrongholdBoundary, failure);
    }

    [Test]
    public void Invade_PlayerMoveIntoNonVisibleArea_IsRejected()
    {
        LogicStrongholdMap.Clear();
        LogicCardPlacementAuthority.BeginTimeline();
        LogicCardPlacementAuthority.BindWorldForTests(
            CreateFogMap(5, 1),
            Array.Empty<LogicCombatShape>(),
            Fix64.One,
            Fix64.One,
            Fix64.One);
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        player.PositionFixed = CellCenter(0);
        EntityRegistry.RegisterAsPlayer(player);

        LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);
        LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });

        FixVector2 visible = CellCenter(1);
        Assert.IsTrue(LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(visible));
        Assert.AreEqual(
            visible,
            LogicMovementRegionConstraintService.ResolvePosition(
                player,
                player.PositionFixed,
                visible,
                out LogicMovementRegionConstraintFailure visibleFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, visibleFailure);

        FixVector2 hidden = CellCenter(2);
        bool hiddenVisible = LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(hidden);
        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            player.PositionFixed,
            hidden,
            out LogicMovementRegionConstraintFailure hiddenFailure);

        Assert.IsFalse(hiddenVisible);
        Assert.AreEqual(
            player.PositionFixed,
            resolved,
            $"A non-visible candidate must not be committed. start={player.PositionFixed}, candidate={hidden}, resolved={resolved}, failure={hiddenFailure}.");
        Assert.AreEqual(LogicMovementRegionConstraintFailure.NotVisible, hiddenFailure);
        Assert.IsFalse(LogicMovementRegionConstraintService.IsPositionAllowed(
            player,
            hidden,
            out LogicMovementRegionConstraintFailure positionFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.NotVisible, positionFailure);

        SimEntityContext enemy = CreateEntity(SideType.EnemySide);
        Assert.AreEqual(
            hidden,
            LogicMovementRegionConstraintService.ResolvePosition(
                enemy,
                enemy.PositionFixed,
                hidden,
                out LogicMovementRegionConstraintFailure enemyFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, enemyFailure);
    }

    [Test]
    public void RunningLogicTimeline_RejectsPlayerMoveWhenFogAuthorityIsUnbound()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                LogicMovementRegionConstraintService.ResolvePosition(
                    player,
                    StrongholdPoint(0),
                    new FixVector2((Fix64)0.1f, Fix64.Zero),
                    out _));
            StringAssert.Contains("fog authority", exception.Message.ToLowerInvariant());
        }
        finally
        {
            LogicFrameRuntime.End();
        }
    }

    [Test]
    public void ResolvePosition_WhenTimelineIsInactive_Throws()
    {
        LogicMovementRegionConstraintService.EndTimeline();
        SimEntityContext player = CreateEntity(SideType.PlayerSide);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LogicMovementRegionConstraintService.ResolvePosition(
                player,
                StrongholdPoint(0),
                StrongholdPoint(1),
                out _));

        StringAssert.Contains("service is not active", exception.Message);
    }

    [Test]
    public void InvadeTutorialTrigger_IsEvaluatedOnLogicFrameAndClearsAfterPhaseExit()
    {
        LogicMovementRegionConstraintService.BindTutorialInvadeTrigger(
            StrongholdPoint(0),
            new FixVector2((Fix64)0.5f, (Fix64)0.5f));
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        EntityRegistry.RegisterAsPlayer(player);
        string activatedStrongholdId = null;
        LogicMovementRegionConstraintService.TutorialStrongholdBoundaryActivated += OnActivated;
        try
        {
            LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.Invade);
            LogicTimeControlService.BeginFrame(1);
            LogicPhaseCommandService.ApplyFrameForTests(1, _ => { });
            LogicMovementRegionConstraintService.ApplyFrame(1);

            Assert.AreEqual("owned", activatedStrongholdId);
            Assert.AreEqual("owned", LogicMovementRegionConstraintService.TutorialStrongholdId);
            Assert.IsTrue(LogicMovementRegionConstraintService.TutorialInvadeTriggerConsumed);

            LogicPhaseCommandService.ScheduleForNextFrame(GamePhase.BuildBeforeInvade);
            LogicTimeControlService.BeginFrame(2);
            LogicPhaseCommandService.ApplyFrameForTests(2, _ => { });
            LogicMovementRegionConstraintService.ApplyFrame(2);

            Assert.IsFalse(LogicMovementRegionConstraintService.HasTutorialStrongholdBoundary);
            Assert.IsTrue(LogicMovementRegionConstraintService.TutorialInvadeTriggerConsumed);
        }
        finally
        {
            LogicMovementRegionConstraintService.TutorialStrongholdBoundaryActivated -= OnActivated;
        }

        void OnActivated(string strongholdId)
        {
            activatedStrongholdId = strongholdId;
        }
    }

    private static SimEntityContext CreateEntity(SideType side)
    {
        return new SimEntityContext
        {
            Side = side,
            PositionFixed = StrongholdPoint(0),
        };
    }

    private static void InitializeStrongholds()
    {
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("owned", 0, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("enemy", 1, 0, EntitySideHelper.EnemyFactionId),
            });
    }

    private static FixVector2 CellCenter(int x)
    {
        return new FixVector2((Fix64)x + (Fix64)0.5f, (Fix64)0.5f);
    }

    private static FixVector2 StrongholdPoint(int x)
    {
        return new FixVector2((Fix64)x, Fix64.Zero);
    }

    private static Fog3MapData CreateFogMap(int width, int height)
    {
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        return new Fog3MapData(new Fog3TerrainInfo(
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            "LogicMovementRegionConstraintServiceTests"));
    }
}
