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
    public void Build_PlayerMoveIntoEnemyStronghold_IsRejected()
    {
        SimEntityContext player = CreateEntity(SideType.PlayerSide);
        FixVector2 start = StrongholdPoint(0);

        FixVector2 resolved = LogicMovementRegionConstraintService.ResolvePosition(
            player,
            start,
            StrongholdPoint(1),
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreEqual(start, resolved);
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
            StrongholdPoint(2),
            out LogicMovementRegionConstraintFailure failure);

        Assert.AreNotEqual(before.Hash, after.Hash);
        Assert.AreEqual(start, resolved);
        Assert.AreEqual(LogicMovementRegionConstraintFailure.TutorialStrongholdBoundary, failure);
    }

    [Test]
    public void VisibilityBoundary_UsesCurrentFixedLogicRevealers()
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

        FixVector2 visible = CellCenter(1);
        Assert.AreEqual(
            visible,
            LogicMovementRegionConstraintService.ResolvePosition(
                player,
                player.PositionFixed,
                visible,
                out LogicMovementRegionConstraintFailure visibleFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.None, visibleFailure);

        FixVector2 hidden = CellCenter(2);
        Assert.AreEqual(
            player.PositionFixed,
            LogicMovementRegionConstraintService.ResolvePosition(
                player,
                player.PositionFixed,
                hidden,
                out LogicMovementRegionConstraintFailure hiddenFailure));
        Assert.AreEqual(LogicMovementRegionConstraintFailure.NotVisible, hiddenFailure);
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
