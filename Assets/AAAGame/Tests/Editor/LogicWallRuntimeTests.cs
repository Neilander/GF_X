using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicWallRuntimeTests
{
    private GameObject m_AuthoringObject;

    [SetUp]
    public void SetUp()
    {
        LogicWallRuntime.Clear();
        LogicStrongholdMap.Clear();
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            CreateStrongholdCells(9, 9));

        var walkable = new List<WallGridHeightCell>();
        for (int y = 0; y < 9; y++)
        {
            for (int x = 0; x < 9; x++)
                walkable.Add(new WallGridHeightCell(x, y, 0));
        }
        InitializeWallGrid(walkable);
    }

    [TearDown]
    public void TearDown()
    {
        LogicWallRuntime.Clear();
        LogicStrongholdMap.Clear();
        if (m_AuthoringObject != null)
            UnityEngine.Object.DestroyImmediate(m_AuthoringObject);
    }

    [Test]
    public void SplitConnectedComponents_UsesOnlyCardinalConnections()
    {
        IReadOnlyList<IReadOnlyList<WallGridCell>> result = LogicWallRuntime.SplitConnectedComponents(
            new[]
            {
                new WallGridCell(1, 1),
                new WallGridCell(2, 1),
                new WallGridCell(2, 2),
                new WallGridCell(5, 5),
                new WallGridCell(6, 6),
            });

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(3, result[0].Count);
        Assert.AreEqual(1, result[1].Count);
        Assert.AreEqual(1, result[2].Count);
    }

    [Test]
    public void CreateBranchBuildingData_AddsHealthPerCellButKeepsDefence()
    {
        var source = new BuildingData(
            "Buil_Wall",
            BuilType.Wall,
            Archetype.None,
            "Building/Buil_Wall_Lv1",
            string.Empty,
            string.Empty,
            1,
            12,
            (Fix64)100,
            null,
            (Fix64)7,
            Array.Empty<Fix64>(),
            string.Empty,
            0,
            null);

        BuildingData branch = LogicWallRuntime.CreateBranchBuildingData(source, 4);

        Assert.AreEqual((Fix64)400, branch.HP);
        Assert.AreEqual((Fix64)7, branch.Def);
        Assert.AreEqual(12, branch.Cost);
    }

    [Test]
    public void ConstructionTargetRule_AllowsArchetypeNoneOnlyForWall()
    {
        BuildingData wallPreview = CreateBuildingData("Buil_Wall_Lv0", BuilType.Wall, Archetype.None, 0);
        BuildingData wall = CreateBuildingData("Buil_Wall_Lv1", BuilType.Wall, Archetype.None, 1);
        BuildingData ordinaryWithoutArchetype = CreateBuildingData("Buil_Def_Lv1", BuilType.Def, Archetype.None, 1);

        Assert.IsTrue(BuildingDataModel.CanConstructAt(wallPreview, wall));
        Assert.IsFalse(BuildingDataModel.IsLv1ConstructionTarget(ordinaryWithoutArchetype));
        Assert.IsFalse(BuildingDataModel.CanConstructAt(wallPreview, ordinaryWithoutArchetype));
    }

    [Test]
    public void OpenTerrainBranch_DoesNotCreateUnneededGate()
    {
        var cells = new[]
        {
            new WallGridCell(2, 4),
            new WallGridCell(3, 4),
            new WallGridCell(4, 4),
            new WallGridCell(5, 4),
            new WallGridCell(6, 4),
        };

        IReadOnlyCollection<WallGridCell> gates = LogicWallRuntime.ResolveGateCells(cells);

        Assert.IsEmpty(gates);
    }

    [Test]
    public void CorridorBlockingBranch_ChoosesItsCentralGateCell()
    {
        var corridor = new List<WallGridHeightCell>();
        for (int y = 2; y <= 6; y++)
        {
            for (int x = 0; x < 9; x++)
                corridor.Add(new WallGridHeightCell(x, y, 0));
        }
        InitializeWallGrid(corridor);
        var cells = new[]
        {
            new WallGridCell(4, 2),
            new WallGridCell(4, 3),
            new WallGridCell(4, 4),
            new WallGridCell(4, 5),
            new WallGridCell(4, 6),
        };

        IReadOnlyCollection<WallGridCell> gates = LogicWallRuntime.ResolveGateCells(cells);

        CollectionAssert.AreEquivalent(new[] { new WallGridCell(4, 4) }, gates);
    }

    [Test]
    public void InactiveBranch_IsRemovedFromCollisionUntilRestored()
    {
        LogicEntityId entityId = new LogicEntityId(9001);
        var cells = new[] { new WallGridCell(4, 4) };
        LogicWallRuntime.RegisterBranch(
            entityId,
            new LogicWallBranchDefinition(cells, Array.Empty<WallGridCell>(), EntitySideHelper.PlayerFactionId));

        FixVector2 center = LogicWallRuntime.GetCellWorldCenter(cells[0]);
        Assert.IsTrue(LogicWallRuntime.HasBuiltWalls);
        Assert.IsTrue(LogicWallRuntime.IsPointBlockedForFaction(
            center,
            Fix64.Zero,
            EntitySideHelper.EnemyFactionId));

        LogicWallRuntime.SetBranchActive(entityId, false);

        Assert.IsFalse(LogicWallRuntime.HasBuiltWalls);
        Assert.IsFalse(LogicWallRuntime.IsPointBlockedForFaction(
            center,
            Fix64.Zero,
            EntitySideHelper.EnemyFactionId));

        LogicWallRuntime.SetBranchActive(entityId, true);
        Assert.IsTrue(LogicWallRuntime.IsPointBlockedForFaction(
            center,
            Fix64.Zero,
            EntitySideHelper.EnemyFactionId));
    }

    [Test]
    public void PreviewPathConnections_FollowStrongholdEdgeAndTurnAtCorner()
    {
        var previews = new[]
        {
            new WallGridCell(0, 0),
            new WallGridCell(1, 0),
            new WallGridCell(3, 0),
            new WallGridCell(4, 0),
            new WallGridCell(5, 0),
            new WallGridCell(0, 1),
            new WallGridCell(0, 3),
            new WallGridCell(0, 4),
            new WallGridCell(0, 5),
        };
        InitializeWallGrid(CreateWalkableCells(9, 9), previews);

        Assert.AreEqual(
            WallConnectionDirection.Right | WallConnectionDirection.Up,
            LogicWallRuntime.ResolveWallPathConnections(new WallGridCell(0, 0)));
        Assert.AreEqual(
            WallConnectionDirection.Left | WallConnectionDirection.Right,
            LogicWallRuntime.ResolveWallPathConnections(new WallGridCell(4, 0)));
        Assert.AreEqual(
            WallConnectionDirection.Down | WallConnectionDirection.Up,
            LogicWallRuntime.ResolveWallPathConnections(new WallGridCell(0, 4)));
    }

    [Test]
    public void WallGeometry_UsesStraightBlocksAndCenterWithArmsAtCorner()
    {
        IReadOnlyList<WallGeometryBlock> horizontal = WallBranchGeometry.BuildConnectedBlocks(
            WallConnectionDirection.Left | WallConnectionDirection.Right);
        IReadOnlyList<WallGeometryBlock> vertical = WallBranchGeometry.BuildConnectedBlocks(
            WallConnectionDirection.Down | WallConnectionDirection.Up);
        IReadOnlyList<WallGeometryBlock> corner = WallBranchGeometry.BuildConnectedBlocks(
            WallConnectionDirection.Right | WallConnectionDirection.Up);

        Assert.AreEqual(1, horizontal.Count);
        Assert.Greater(horizontal[0].Scale.x, horizontal[0].Scale.z);
        Assert.AreEqual(1, vertical.Count);
        Assert.Greater(vertical[0].Scale.z, vertical[0].Scale.x);
        Assert.AreEqual(3, corner.Count);
        Assert.AreEqual(Vector3.zero, corner[0].CenterOffset);
        Assert.IsTrue(ContainsBlockOffset(corner, Vector3.right * 0.33f));
        Assert.IsTrue(ContainsBlockOffset(corner, Vector3.forward * 0.33f));
    }

    [Test]
    public void WallGeometry_ScalesWithAuthoredCellSize()
    {
        const float cellSize = 2.5f;
        IReadOnlyList<WallGeometryBlock> horizontal = WallBranchGeometry.BuildConnectedBlocks(
            WallConnectionDirection.Left | WallConnectionDirection.Right,
            cellSize);
        IReadOnlyList<WallGeometryBlock> corner = WallBranchGeometry.BuildConnectedBlocks(
            WallConnectionDirection.Right | WallConnectionDirection.Up,
            cellSize);

        Assert.AreEqual(cellSize, horizontal[0].Scale.x, 1e-5f);
        Assert.IsTrue(ContainsBlockOffset(corner, Vector3.right * (0.33f * cellSize)));
        Assert.IsTrue(ContainsBlockOffset(corner, Vector3.forward * (0.33f * cellSize)));
    }

    private static bool ContainsBlockOffset(IReadOnlyList<WallGeometryBlock> blocks, Vector3 expected)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if ((blocks[i].CenterOffset - expected).sqrMagnitude < 0.000001f)
                return true;
        }
        return false;
    }

    private static BuildingData CreateBuildingData(
        string identifier,
        BuilType type,
        Archetype archetype,
        int level)
    {
        return new BuildingData(
            identifier,
            type,
            archetype,
            string.Empty,
            string.Empty,
            string.Empty,
            level,
            0,
            Fix64.One,
            null,
            Fix64.Zero,
            Array.Empty<Fix64>(),
            string.Empty,
            0,
            null);
    }

    private static IReadOnlyList<LogicStrongholdCellDefinition> CreateStrongholdCells(int width, int height)
    {
        var result = new List<LogicStrongholdCellDefinition>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                result.Add(new LogicStrongholdCellDefinition("TestStronghold", x, y, EntitySideHelper.PlayerFactionId));
        }
        return result;
    }

    private static IReadOnlyCollection<WallGridHeightCell> CreateWalkableCells(int width, int height)
    {
        var result = new List<WallGridHeightCell>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                result.Add(new WallGridHeightCell(x, y, 0));
        }
        return result;
    }

    private void InitializeWallGrid(
        IReadOnlyCollection<WallGridHeightCell> walkable,
        WallGridCell[] previews = null)
    {
        LogicWallRuntime.Clear();
        if (m_AuthoringObject != null)
            UnityEngine.Object.DestroyImmediate(m_AuthoringObject);
        m_AuthoringObject = new GameObject("WallRuntimeTestAuthoring");
        var authoring = m_AuthoringObject.AddComponent<WallGridAuthoring>();
        var cells = new WallGridHeightCell[walkable.Count];
        int cellIndex = 0;
        foreach (WallGridHeightCell cell in walkable)
            cells[cellIndex++] = cell;
        authoring.SetData(
            9,
            9,
            1f,
            cells,
            previews ?? Array.Empty<WallGridCell>(),
            Array.Empty<WallGridCell>());
        LogicWallRuntime.Initialize(authoring);
    }
}
