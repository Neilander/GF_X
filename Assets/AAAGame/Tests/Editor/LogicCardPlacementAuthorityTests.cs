using System;
using System.Collections.Generic;
using System.Reflection;
using AAAGame.Card;
using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using UnityEngine;

public sealed class LogicCardPlacementAuthorityTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicCardPlacementAuthority.BeginTimeline();
        LogicEntityStateStore.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
        if (LogicEntityStateStore.IsActive)
            LogicEntityStateStore.EndTimeline();
        if (LogicCardPlacementAuthority.IsActive)
            LogicCardPlacementAuthority.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void RevealCircle_UsesFixedCellCentersAndStableIncrementalDigest()
    {
        Fog3MapData map = CreateMap(5, 5);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);
        FixVector2 center = new FixVector2((Fix64)1.5f, (Fix64)1.5f);

        LogicCardPlacementAuthority.RevealCircleForTests(center, Fix64.One);

        Assert.AreEqual(5, map.ExploredCellCount);
        Assert.IsTrue(map.IsExplored(1, 1));
        Assert.IsTrue(map.IsExplored(0, 1));
        Assert.IsTrue(map.IsExplored(2, 1));
        Assert.IsTrue(map.IsExplored(1, 0));
        Assert.IsTrue(map.IsExplored(1, 2));
        Assert.IsFalse(map.IsExplored(0, 0));
        ulong xorDigest = map.ExplorationXorDigest;
        ulong sumDigest = map.ExplorationSumDigest;

        LogicCardPlacementAuthority.RevealCircleForTests(center, Fix64.One);

        Assert.AreEqual(5, map.ExploredCellCount);
        Assert.AreEqual(xorDigest, map.ExplorationXorDigest);
        Assert.AreEqual(sumDigest, map.ExplorationSumDigest);
    }

    [Test]
    public void ApplyFrame_RevealsFromLogicEntityPosition()
    {
        Fog3MapData map = CreateMap(5, 5);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);
        LogicEntityState hero = CreateUnit(
            1,
            new FixVector2((Fix64)2.5f, (Fix64)2.5f),
            SideType.PlayerSide,
            true);
        EntityRegistry.RegisterAsPlayer(hero);
        LogicTimeControlService.BeginFrame(1);

        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.AreEqual(1UL, LogicCardPlacementAuthority.LastAppliedFrame);
        Assert.IsTrue(map.IsExplored(2, 2));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.None,
            LogicCardPlacementAuthority.Evaluate(hero.PositionFixed, Fix64.Zero, GamePhase.Invade));
    }

    [Test]
    public void CurrentLogicRevealers_UseFixedVisibilityRadius()
    {
        Fog3MapData map = CreateMap(5, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.One);
        LogicEntityState player = CreateUnit(
            1,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            SideType.PlayerSide,
            false);
        EntityRegistry.RegisterAsPlayer(player);
        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.IsTrue(LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(
            new FixVector2((Fix64)1.5f, (Fix64)0.5f)));
        Assert.IsFalse(LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(
            new FixVector2((Fix64)2.5f, (Fix64)0.5f)));
    }

    [Test]
    public void GhostHero_IlluminatesExploredCellsWithoutRevealingHiddenCells()
    {
        Fog3MapData map = CreateMap(4, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)3);
        LogicEntityState hero = CreateUnit(
            1,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            SideType.PlayerSide,
            true);
        FieldInfo ghostStateField = typeof(LogicEntityState).GetField(
            "<IsGhostState>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ghostStateField);
        ghostStateField.SetValue(hero, true);
        EntityRegistry.RegisterAsPlayer(hero);
        map.MarkExplored(0, 0);
        map.MarkExplored(1, 0);

        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.IsTrue(LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(
            new FixVector2((Fix64)1.5f, (Fix64)0.5f)));
        Assert.IsFalse(LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers(
            new FixVector2((Fix64)2.5f, (Fix64)0.5f)));

        Assert.IsFalse(map.IsExplored(2, 0));
    }

    [Test]
    public void BuildPhaseExploration_DoesNotDependOnCardSystemInitialization()
    {
        var controller = new Fog3Controller();
        controller.Initialize(new Fog3TerrainInfo(
            3,
            1,
            1f,
            Vector3.zero,
            new[] { true, true, true },
            "BuildPhaseExplorationTest"));
        Fog3MapData map = controller.MapData;
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.FromRaw(2007));
        var revealer = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(revealer);
        GameObject view = new GameObject("BuildPhaseFogRevealer");
        try
        {
            Assert.IsTrue(LogicCardPlacementAuthority.IsBoundTo(map));
            controller.RegisterRevealer(view.transform, 0.49f, revealer.LogicEntityId.Value, false);

            LogicTimeControlService.BeginFrame(1);
            LogicCardPlacementAuthority.ApplyFrame(1);
            controller.PublishAuthoritativeVisibility(false);
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));

            revealer.PositionFixed = new FixVector2((Fix64)1.5f, (Fix64)0.5f);
            LogicTimeControlService.BeginFrame(2);
            LogicCardPlacementAuthority.ApplyFrame(2);
            controller.PublishAuthoritativeVisibility(false);

            Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(0, 0));
            Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(view);
        }
    }

    [Test]
    public void CardSystemShutdown_DoesNotUnbindLogicFogExploration()
    {
        Fog3MapData map = CreateMap(3, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.One);
        GameObject setupObject = new GameObject("CardSetupFogBindingTest");
        try
        {
            CardSetup cardSetup = setupObject.AddComponent<CardSetup>();

            cardSetup.CardSystemShutdown(false);

            Assert.IsTrue(LogicCardPlacementAuthority.IsWorldBound);
            Assert.IsTrue(LogicCardPlacementAuthority.IsBoundTo(map));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(setupObject);
        }
    }

    [Test]
    public void BindWorld_MidTimelineSynchronizesBindingFrameAndContinuesOnNextFrame()
    {
        Fog3MapData map = CreateMap(5, 5);
        LogicEntityState hero = CreateUnit(
            1,
            new FixVector2((Fix64)2.5f, (Fix64)2.5f),
            SideType.PlayerSide,
            true);
        EntityRegistry.RegisterAsPlayer(hero);
        for (ulong frame = 1; frame <= 17; frame++)
            LogicTimeControlService.BeginFrame(frame);

        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);

        Assert.AreEqual(17UL, LogicCardPlacementAuthority.LastAppliedFrame);
        Assert.IsTrue(map.IsExplored(2, 2));

        LogicTimeControlService.BeginFrame(18);
        LogicCardPlacementAuthority.ApplyFrame(18);

        Assert.AreEqual(18UL, LogicCardPlacementAuthority.LastAppliedFrame);
    }

    [Test]
    public void Evaluate_UsesAuthoredStaticShapeAndPlacementRadius()
    {
        Fog3MapData map = CreateMap(6, 6);
        MarkAllExplored(map);
        LogicCombatShape forbidden = LogicCombatShape.AxisAlignedBox(
            new FixVector2((Fix64)3, (Fix64)3),
            new FixVector2(Fix64.One, Fix64.One));
        Bind(map, new[] { forbidden }, (Fix64)1);

        Assert.AreEqual(
            LogicCardPlacementInvalidReason.StaticForbiddenArea,
            LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)1.5f, (Fix64)3),
                (Fix64)0.5f,
                GamePhase.Invade));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.None,
            LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)1.5f, (Fix64)3),
                Fix64.FromRaw((Fix64.One / (Fix64)2).RawValue - 1),
                GamePhase.Invade));
    }

    [Test]
    public void Evaluate_UsesEnemyLogicBuildingShapeAndSkipsLevelZeroPlaceholder()
    {
        Fog3MapData map = CreateMap(12, 12);
        MarkAllExplored(map);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);
        LogicEntityState enemy = CreateBuilding(
            10,
            1,
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)7, (Fix64)7),
                new FixVector2(Fix64.One, Fix64.One)));
        LogicEntityState placeholder = CreateBuilding(
            11,
            0,
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)1, (Fix64)1),
                new FixVector2(Fix64.One, Fix64.One)));
        EntityRegistry.Register(enemy);
        EntityRegistry.Register(placeholder);

        Assert.AreEqual(
            LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea,
            LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)10.5f, (Fix64)7),
                (Fix64)0.5f,
                GamePhase.Invade));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.None,
            LogicCardPlacementAuthority.Evaluate(
                new FixVector2((Fix64)1, (Fix64)1),
                Fix64.Zero,
                GamePhase.Invade));
    }

    [Test]
    public void DeterministicState_ChangesWhenExplorationChanges()
    {
        Fog3MapData map = CreateMap(3, 3);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);
        var before = new LogicStateHasher();
        LogicCardPlacementAuthority.WriteDeterministicState(before);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)1.5f, (Fix64)1.5f),
            Fix64.FromRaw(1));
        var after = new LogicStateHasher();
        LogicCardPlacementAuthority.WriteDeterministicState(after);

        Assert.AreNotEqual(before.Hash, after.Hash);
    }

    [Test]
    public void RevealCircle_BlocksHiddenPropagationBeyondEnemyStronghold()
    {
        Fog3MapData map = CreateMap(5, 1);
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy-stronghold", 1, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("enemy-stronghold", 2, 0, EntitySideHelper.EnemyFactionId),
            });
        LogicCardPlacementAuthority.BindWorldForTests(
            map,
            Array.Empty<LogicCombatShape>(),
            (Fix64)5,
            (Fix64)5,
            (Fix64)5,
            true);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)5);

        Assert.IsTrue(map.IsExplored(0, 0));
        Assert.IsTrue(map.IsExplored(1, 0));
        Assert.IsTrue(map.IsExplored(2, 0));
        Assert.IsFalse(map.IsExplored(3, 0));
        Assert.IsFalse(map.IsExplored(4, 0));
    }

    [Test]
    public void RevealCircle_WhenEnemyStrongholdBlockIsDisabled_RevealsBeyondEnemyStronghold()
    {
        Fog3MapData map = CreateMap(5, 1);
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy-stronghold", 1, 0, EntitySideHelper.EnemyFactionId),
                new LogicStrongholdCellDefinition("enemy-stronghold", 2, 0, EntitySideHelper.EnemyFactionId),
            });
        LogicCardPlacementAuthority.BindWorldForTests(
            map,
            Array.Empty<LogicCombatShape>(),
            (Fix64)5,
            (Fix64)5,
            (Fix64)5,
            false);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)5);

        Assert.IsTrue(map.IsExplored(3, 0));
        Assert.IsTrue(map.IsExplored(4, 0));
    }

    [Test]
    public void Evaluate_EnemyStrongholdIsForbiddenOnlyDuringDefendPhase()
    {
        Fog3MapData map = CreateMap(3, 1);
        MarkAllExplored(map);
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("enemy-stronghold", 1, 0, EntitySideHelper.EnemyFactionId),
            });
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.One);
        FixVector2 enemyStrongholdPosition = new FixVector2(Fix64.One, Fix64.Zero);

        Assert.AreEqual(
            LogicCardPlacementInvalidReason.None,
            LogicCardPlacementAuthority.Evaluate(
                enemyStrongholdPosition,
                Fix64.Zero,
                GamePhase.Invade));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.EnemyStrongholdForbiddenArea,
            LogicCardPlacementAuthority.Evaluate(
                enemyStrongholdPosition,
                Fix64.Zero,
                GamePhase.Defend));
    }

    [Test]
    public void StaticForbiddenCatalog_RequiresExplicitLevelEntry()
    {
        CardStaticForbiddenShapeCatalog catalog = CardStaticForbiddenShapeCatalog.LoadRequired();

        Assert.AreEqual(0, catalog.ResolveRequired("Lv_2").Count);
        Assert.Throws<InvalidOperationException>(() => catalog.ResolveRequired("Missing_Level"));
    }

    private static void Bind(
        Fog3MapData map,
        IReadOnlyList<LogicCombatShape> staticForbiddenShapes,
        Fix64 visionRadius)
    {
        LogicCardPlacementAuthority.BindWorldForTests(
            map,
            staticForbiddenShapes,
            visionRadius,
            visionRadius,
            visionRadius);
    }

    private static Fog3MapData CreateMap(int width, int height)
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
            "LogicCardPlacementAuthorityTests"));
    }

    private static void MarkAllExplored(Fog3MapData map)
    {
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
                map.MarkExplored(x, y);
        }
    }

    private static LogicEntityState CreateUnit(
        long id,
        FixVector2 position,
        SideType side,
        bool isHero)
    {
        LogicEntityState state = LogicEntityStateStore.Create(
            new LogicEntityId(checked((int)id)),
            new LogicEntitySpawnDescriptor(
                position,
                new FixVector2(Fix64.Zero, Fix64.One),
                side,
                $"Unit_CardPlacement_{id}"));
        state.Configure(
            null,
            new CreaturePropertyManager(_ => Fix64.One),
            0,
            false,
            null,
            false,
            isHero,
            isHero);
        return state;
    }

    private static LogicEntityState CreateBuilding(
        long id,
        int level,
        int ownerFactionId,
        LogicCombatShape shape)
    {
        LogicEntityState state = CreateUnit(
            id,
            shape.Center,
            EntitySideHelper.ToSide(ownerFactionId),
            false);
        state.ConfigureBuilding(
            new BuildingData(
                $"Building_CardPlacement_{id}",
                BuilType.Def,
                Archetype.Security,
                "Tests/CardPlacementBuilding",
                $"Building_CardPlacement_{id}",
                $"Building_CardPlacement_{id}",
                level,
                0,
                (Fix64)100,
                null,
                Fix64.Zero,
                Array.Empty<Fix64>(),
                null,
                0,
                Array.Empty<string>()),
            $"building-card-placement-{id}",
            "stronghold-card-placement",
            ownerFactionId,
            shape,
            Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false,
            null);
        return state;
    }
}
