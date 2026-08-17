using System;
using System.Collections.Generic;
using System.IO;
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
    public void ApplyFrame_LevelZeroPlayerBuildingDoesNotProvideVision()
    {
        Fog3MapData map = CreateMap(3, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.One);
        LogicEntityState building = CreateBuilding(
            2,
            0,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)0.5f, (Fix64)0.5f),
                new FixVector2((Fix64)0.5f, (Fix64)0.5f)));
        EntityRegistry.Register(building);
        LogicTimeControlService.BeginFrame(1);

        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(0, 0));
        Assert.IsFalse(map.IsExplored(0, 0));
    }

    [Test]
    public void ApplyFrame_LevelOnePlayerBuildingStillProvidesVision()
    {
        Fog3MapData map = CreateMap(3, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), Fix64.One);
        LogicEntityState building = CreateBuilding(
            3,
            1,
            EntitySideHelper.PlayerFactionId,
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)0.5f, (Fix64)0.5f),
                new FixVector2((Fix64)0.5f, (Fix64)0.5f)));
        EntityRegistry.Register(building);
        LogicTimeControlService.BeginFrame(1);

        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.IsTrue(map.IsExplored(0, 0));
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
    public void VisibilityAcquisition_ExpandsOutwardFromTheRevealer()
    {
        Fog3MapData map = CreateMap(4, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)3, (Fix64)32);
        var player = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(player);

        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(2, 0));
        Assert.IsFalse(map.IsExplored(2, 0));

        LogicTimeControlService.BeginFrame(2);
        LogicCardPlacementAuthority.ApplyFrame(2);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(2, 0));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(3, 0));

        LogicTimeControlService.BeginFrame(3);
        LogicCardPlacementAuthority.ApplyFrame(3);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(3, 0));
    }

    [Test]
    public void VisibilityLoss_ContractsFromTheOuterEdgeTowardTheFormerRevealer()
    {
        Fog3MapData map = CreateMap(4, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)3, (Fix64)32);
        var player = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(player);

        for (ulong frame = 1; frame <= 3; frame++)
        {
            LogicTimeControlService.BeginFrame(frame);
            LogicCardPlacementAuthority.ApplyFrame(frame);
        }
        player.PositionFixed = new FixVector2((Fix64)10.5f, (Fix64)0.5f);

        LogicTimeControlService.BeginFrame(4);
        LogicCardPlacementAuthority.ApplyFrame(4);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(2, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(3, 0));

        LogicTimeControlService.BeginFrame(5);
        LogicCardPlacementAuthority.ApplyFrame(5);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));

        LogicTimeControlService.BeginFrame(6);
        LogicCardPlacementAuthority.ApplyFrame(6);
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(0, 0));
    }

    [Test]
    public void VisibilityReacquisition_CancelsAnActiveContraction()
    {
        Fog3MapData map = CreateMap(3, 1);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)2, (Fix64)32);
        var player = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(player);

        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);
        LogicTimeControlService.BeginFrame(2);
        LogicCardPlacementAuthority.ApplyFrame(2);
        player.PositionFixed = new FixVector2((Fix64)10.5f, (Fix64)0.5f);

        LogicTimeControlService.BeginFrame(3);
        LogicCardPlacementAuthority.ApplyFrame(3);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));
        player.PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f);

        LogicTimeControlService.BeginFrame(4);
        LogicCardPlacementAuthority.ApplyFrame(4);
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(0, 0));
        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
    }

    [Test]
    public void HigherPlatform_BlocksLowerViewerExplorationAndVisibility()
    {
        Fog3MapData map = CreateHeightMap(5, 1, new[] { 0, 0, 1, 0, 0 }, new bool[5]);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)5);
        LogicEntityState player = CreateUnit(
            1,
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            SideType.PlayerSide,
            false);
        EntityRegistry.RegisterAsPlayer(player);

        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);

        Assert.IsTrue(map.IsExplored(1, 0));
        Assert.IsFalse(map.IsExplored(2, 0), "A higher non-slope platform cell must be hidden from a lower viewer.");
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(2, 0));
        Assert.IsFalse(map.IsExplored(3, 0), "Cells behind higher terrain must remain hidden.");
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(3, 0));
    }

    [Test]
    public void SlopeCell_OneHeightAboveViewer_DoesNotOcclude()
    {
        Fog3MapData map = CreateHeightMap(4, 1, new[] { 0, 0, 0, 0 }, new[] { false, true, false, false });
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)4);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)4);

        Assert.IsTrue(map.IsExplored(1, 0));
        Assert.IsTrue(map.IsExplored(2, 0));
        Assert.IsTrue(map.IsExplored(3, 0));
    }

    [Test]
    public void ViewerOnSlope_IsBlockedByHigherPlatform()
    {
        Fog3MapData map = CreateHeightMap(4, 1, new[] { 0, 0, 1, 0 }, new[] { true, false, false, false });
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)4);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)4);

        Assert.IsTrue(map.IsExplored(1, 0));
        Assert.IsFalse(map.IsExplored(2, 0));
        Assert.IsFalse(map.IsExplored(3, 0));
    }

    [Test]
    public void ViewerOnSlope_UsesFlooredHeightAtItsExactPosition()
    {
        var slopeCells = new Fog3SlopeCellInfo[3];
        slopeCells[0] = new Fog3SlopeCellInfo(1, 0, 0, 1, 2);
        Fog3MapData map = CreateHeightMap(
            3,
            1,
            new[] { 0, 1, 2 },
            new[] { true, false, false },
            slopeCells);

        var lowerSlopePosition = new FixVector2((Fix64)0.25f, (Fix64)0.5f);
        var upperSlopePosition = new FixVector2((Fix64)0.75f, (Fix64)0.5f);

        Assert.AreEqual(0, map.GetVisionHeight(lowerSlopePosition));
        Assert.AreEqual(1, map.GetVisionHeight(upperSlopePosition));
        Assert.IsTrue(map.IsVisionBlockedByHigherPlatform(lowerSlopePosition, 1, 0));
        Assert.IsFalse(map.IsVisionBlockedByHigherPlatform(upperSlopePosition, 1, 0));
        Assert.IsTrue(map.IsVisionBlockedByHigherPlatform(upperSlopePosition, 2, 0));
    }

    [Test]
    public void SlopeTarget_OnlyAllowsOneHeightAboveViewer()
    {
        var slopeCells = new Fog3SlopeCellInfo[3];
        slopeCells[1] = new Fog3SlopeCellInfo(1, 0, 0, 1, 1);
        slopeCells[2] = new Fog3SlopeCellInfo(1, 0, 1, 1, 1);
        Fog3MapData map = CreateHeightMap(
            3,
            1,
            new[] { 0, 0, 1 },
            new[] { false, true, true },
            slopeCells);
        var viewer = new FixVector2((Fix64)0.5f, (Fix64)0.5f);

        Assert.IsFalse(map.IsVisionBlockedByHigherPlatform(viewer, 1, 0));
        Assert.IsTrue(map.IsVisionBlockedByHigherPlatform(viewer, 2, 0));
    }

    [Test]
    public void SameHeightPlatform_DoesNotOccludeViewer()
    {
        Fog3MapData map = CreateHeightMap(4, 1, new[] { 1, 1, 1, 1 }, new bool[4]);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)4);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)4);

        Assert.IsTrue(map.IsExplored(3, 0));
    }

    [Test]
    public void DiagonalVision_CannotLeakPastCornerTouchingHigherPlatforms()
    {
        Fog3MapData map = CreateHeightMap(
            3,
            3,
            new[]
            {
                0, 1, 0,
                1, 0, 0,
                0, 0, 0,
            },
            new bool[9]);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)4);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)4);

        Assert.IsFalse(map.IsExplored(1, 1));
        Assert.IsFalse(map.IsExplored(2, 2));
    }

    [Test]
    public void TileWorldTerrainDetection_UsesTopPlatformLayerAndMarksSlopeCells()
    {
        var configuration = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.Configuration>();
        var h0 = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        var h2 = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        var slope = ScriptableObject.CreateInstance<GiantGrey.TileWorldCreator.BlueprintLayer>();
        var slopeBuildLayer = ScriptableObject.CreateInstance<AAAGame.Tilemap.LdtkSlopeBuildLayer>();
        GameObject terrainObject = new GameObject("Fog3TerrainHeightDetectionTest");
        try
        {
            configuration.width = 4;
            configuration.height = 1;
            configuration.cellSize = 1f;
            var folder = new GiantGrey.TileWorldCreator.BlueprintLayerFolder("Root");
            configuration.blueprintLayerFolders.Add(folder);

            h0.layerName = "Plane_H0";
            h0.allPositions.UnionWith(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0), new Vector2(3, 0) });
            h2.layerName = "Plane_H2";
            h2.allPositions.Add(new Vector2(1, 0));
            slope.layerName = "Slope";
            slope.allPositions.Add(new Vector2(2, 0));
            folder.blueprintLayers.Add(h0);
            folder.blueprintLayers.Add(h2);
            folder.blueprintLayers.Add(slope);
            slopeBuildLayer.platformEndExtension = 0.2f;
            var buildFolder = new GiantGrey.TileWorldCreator.BuildLayerFolder("Build Root");
            buildFolder.buildLayers.Add(slopeBuildLayer);
            configuration.buildLayerFolders.Add(buildFolder);

            GiantGrey.TileWorldCreator.TileWorldCreatorManager manager =
                terrainObject.AddComponent<GiantGrey.TileWorldCreator.TileWorldCreatorManager>();
            manager.configuration = configuration;

            Fog3TerrainInfo terrain = Fog3TerrainDetector.Detect(new Fog3TerrainSettings
            {
                SourceMode = Fog3TerrainSourceMode.TileWorldCreator,
                RequireTileWorldCreatorManager = true,
            });

            Assert.NotNull(terrain);
            Assert.AreEqual(0, terrain.GetPlatformHeight(0, 0));
            Assert.AreEqual(2, terrain.GetPlatformHeight(1, 0));
            Assert.AreEqual(0, terrain.GetPlatformHeight(2, 0));
            Assert.IsFalse(terrain.IsSlope(1, 0));
            Assert.IsTrue(terrain.IsSlope(2, 0));
            Assert.IsTrue(terrain.SlopeCells[2].IsDefined);
            Assert.AreEqual(-1, terrain.SlopeCells[2].DirectionX);
            Assert.AreEqual(1, terrain.SlopeCells[2].Run);
            Assert.AreEqual(2, terrain.SlopeCells[2].Rise);
            Assert.AreEqual(0.2f, terrain.PlatformEdgeInset, 0.001f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
            UnityEngine.Object.DestroyImmediate(h0);
            UnityEngine.Object.DestroyImmediate(h2);
            UnityEngine.Object.DestroyImmediate(slope);
            UnityEngine.Object.DestroyImmediate(slopeBuildLayer);
            UnityEngine.Object.DestroyImmediate(configuration);
        }
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
                new FixVector2((Fix64)9.4f, (Fix64)7),
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
    public void Evaluate_EnemyBuildingStealthToggleImmediatelyChangesForbiddenArea()
    {
        Fog3MapData map = CreateMap(12, 12);
        MarkAllExplored(map);
        Bind(map, Array.Empty<LogicCombatShape>(), (Fix64)1);
        LogicEntityState enemy = CreateBuilding(
            12,
            1,
            EntitySideHelper.EnemyFactionId,
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)7, (Fix64)7),
                new FixVector2(Fix64.One, Fix64.One)),
            new[]
            {
                LogicCombatShape.AxisAlignedBox(
                    new FixVector2((Fix64)7, (Fix64)7),
                    new FixVector2(Fix64.One, Fix64.One)),
            });
        EntityRegistry.Register(enemy);
        var placementPosition = new FixVector2((Fix64)9.4f, (Fix64)7);

        Assert.AreEqual(
            LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea,
            LogicCardPlacementAuthority.Evaluate(placementPosition, (Fix64)0.5f, GamePhase.Invade));
        Assert.IsTrue(LogicCardPlacementAuthority.GeneratesEnemyBuildingForbiddenZone(enemy));

        ((IBuildingLogicContext)enemy).SetPermanentStealthByBuff(true);
        Assert.IsTrue(((IBuildingLogicContext)enemy).IsStealthed);
        Assert.IsFalse(LogicCardPlacementAuthority.GeneratesEnemyBuildingForbiddenZone(enemy));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.None,
            LogicCardPlacementAuthority.Evaluate(placementPosition, (Fix64)0.5f, GamePhase.Invade));
        Assert.IsTrue(LogicCardPlacementAuthority.IsInsideStealthedBuildingCollision(
            new FixVector2((Fix64)7, (Fix64)7)));

        ((IBuildingLogicContext)enemy).SetPermanentStealthByBuff(false);
        Assert.IsTrue(LogicCardPlacementAuthority.GeneratesEnemyBuildingForbiddenZone(enemy));
        Assert.AreEqual(
            LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea,
            LogicCardPlacementAuthority.Evaluate(placementPosition, (Fix64)0.5f, GamePhase.Invade));
        Assert.IsFalse(LogicCardPlacementAuthority.IsInsideStealthedBuildingCollision(
            new FixVector2((Fix64)7, (Fix64)7)));
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
    public void DeterministicState_ChangesWhenVisibilityTransitionChanges()
    {
        Fix64 speed = (Fix64)4;
        var first = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(first);
        Fog3MapData firstMap = CreateMap(2, 1);
        Bind(firstMap, Array.Empty<LogicCombatShape>(), (Fix64)0.49f, speed);
        LogicTimeControlService.BeginFrame(1);
        LogicCardPlacementAuthority.ApplyFrame(1);
        first.PositionFixed = new FixVector2((Fix64)1.5f, (Fix64)0.5f);
        LogicTimeControlService.BeginFrame(2);
        LogicCardPlacementAuthority.ApplyFrame(2);
        var firstState = new LogicStateHasher();
        LogicCardPlacementAuthority.WriteDeterministicState(firstState);

        LogicCardPlacementAuthority.UnbindWorld();
        EntityRegistry.Clear();
        var second = new SimEntityContext
        {
            PositionFixed = new FixVector2((Fix64)1.5f, (Fix64)0.5f),
            Side = SideType.PlayerSide,
        };
        EntityRegistry.Register(second);
        Fog3MapData secondMap = CreateMap(2, 1);
        secondMap.MarkExplored(0, 0);
        Bind(secondMap, Array.Empty<LogicCombatShape>(), (Fix64)0.49f, speed);
        var secondState = new LogicStateHasher();
        LogicCardPlacementAuthority.WriteDeterministicState(secondState);

        Assert.AreEqual(firstMap.ExplorationXorDigest, secondMap.ExplorationXorDigest);
        Assert.AreEqual(firstMap.ExplorationSumDigest, secondMap.ExplorationSumDigest);
        Assert.AreEqual(LogicCardPlacementAuthority.LastAppliedFrame, 2UL);
        Assert.AreNotEqual(firstState.Hash, secondState.Hash);
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
            (Fix64)1000,
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
            (Fix64)1000,
            false);

        LogicCardPlacementAuthority.RevealCircleForTests(
            new FixVector2((Fix64)0.5f, (Fix64)0.5f),
            (Fix64)5);

        Assert.IsTrue(map.IsExplored(3, 0));
        Assert.IsTrue(map.IsExplored(4, 0));
    }

    [Test]
    public void LaunchScene_DisablesEnemyStrongholdHiddenVisionBlock()
    {
        string launchScene = File.ReadAllText("Assets/AAAGame/Scene/Launch.unity");

        StringAssert.Contains("enableEnemyStrongholdHiddenVisionBlock: 0", launchScene);
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
        Bind(map, staticForbiddenShapes, visionRadius, (Fix64)1000);
    }

    private static void Bind(
        Fog3MapData map,
        IReadOnlyList<LogicCombatShape> staticForbiddenShapes,
        Fix64 visionRadius,
        Fix64 visionExpandSpeed)
    {
        LogicCardPlacementAuthority.BindWorldForTests(
            map,
            staticForbiddenShapes,
            visionRadius,
            visionRadius,
            visionRadius,
            visionExpandSpeed);
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

    private static Fog3MapData CreateHeightMap(
        int width,
        int height,
        int[] platformHeights,
        bool[] slopeMask,
        Fog3SlopeCellInfo[] slopeCells = null)
    {
        var walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        if (slopeCells == null)
        {
            slopeCells = new Fog3SlopeCellInfo[slopeMask.Length];
            for (int i = 0; i < slopeMask.Length; i++)
            {
                if (slopeMask[i])
                    slopeCells[i] = new Fog3SlopeCellInfo(1, 0, platformHeights[i], 1, 1);
            }
        }
        return new Fog3MapData(new Fog3TerrainInfo(
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            platformHeights,
            slopeMask,
            slopeCells,
            "LogicCardPlacementHeightTests"));
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
        LogicCombatShape shape,
        IReadOnlyList<LogicCombatShape> obstacleShapes = null)
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
            obstacleShapes ?? Array.Empty<LogicCombatShape>(),
            Array.Empty<LogicInteractionOptionDescriptor>(),
            false,
            null);
        return state;
    }
}
