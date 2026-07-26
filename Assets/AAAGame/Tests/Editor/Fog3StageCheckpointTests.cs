using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class Fog3StageCheckpointTests
{
    [Test]
    public void PerformanceDiagnostics_AreDisabledByDefault()
    {
        GameObject managerObject = new GameObject("Fog3PerformanceDiagnosticsDefaultManager");
        managerObject.SetActive(false);
        try
        {
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            FieldInfo diagnosticsField = typeof(Fog3Manager).GetField(
                "logPerformanceDiagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(diagnosticsField);
            Assert.IsFalse((bool)diagnosticsField.GetValue(manager));
        }
        finally
        {
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void ExplorationCheckpoint_RestoresExploredBitsButNotTransientVisibility()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
        map.MarkExplored(1, 0);
        map.MarkExplored(2, 1);
        map.AddVisibility(1, 0, 1f);
        map.AddVisibility(2, 1, 0.5f);
        Fog3ExplorationCheckpoint checkpoint = map.CaptureExplorationCheckpoint();

        map.ResetExploration();
        map.MarkExplored(0, 1);
        map.AddVisibility(0, 1, 1f);
        map.RestoreExplorationCheckpoint(checkpoint);

        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(2, 1));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(0, 1));
        Assert.AreEqual(0f, map.GetVisibility(1, 0));
        Assert.IsTrue(map.IsDirty);
    }

    [Test]
    public void RenderVisibility_DoesNotMutateLogicExploration()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });

        map.AddVisibility(1, 0, 1f);

        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.IsFalse(map.IsExplored(1, 0));
        Assert.AreEqual(0, map.ExploredCellCount);
    }

    [Test]
    public void ExplorationCheckpoint_RejectsDifferentTerrainTopology()
    {
        Fog3ExplorationCheckpoint checkpoint = CreateMap(
            new[] { true, true, true, true, true, true }).CaptureExplorationCheckpoint();
        Fog3MapData different = CreateMap(new[] { true, true, false, true, true, true });

        Assert.Throws<System.InvalidOperationException>(() =>
            different.RestoreExplorationCheckpoint(checkpoint));
    }

    [Test]
    public void ExplorationCheckpoint_CompressesSparsePayloadAndReusesUnchangedSnapshot()
    {
        const int width = 1024;
        const int height = 1024;
        var walkable = new bool[width * height];
        System.Array.Fill(walkable, true);
        var map = new Fog3MapData(new Fog3TerrainInfo(
            width,
            height,
            1f,
            Vector3.zero,
            walkable,
            "SparseCheckpointTest"));
        map.MarkExplored(1, 1);
        map.MarkExplored(width - 2, height - 2);

        Fog3ExplorationCheckpoint first = map.CaptureExplorationCheckpoint();
        Fog3ExplorationCheckpoint unchanged = map.CaptureExplorationCheckpoint();

        Assert.AreSame(first, unchanged);
        Assert.AreEqual((width * height + 7) / 8, first.RawPayloadByteCount);
        Assert.Less(first.StoredPayloadByteCount, first.RawPayloadByteCount / 10);

        map.MarkExplored(2, 2);
        Fog3ExplorationCheckpoint changed = map.CaptureExplorationCheckpoint();
        Assert.AreNotSame(first, changed);
        map.ResetExploration();
        map.RestoreExplorationCheckpoint(changed);
        Assert.IsTrue(map.IsExplored(2, 2));
    }

    [Test]
    public void EnemyVisibility_ResolvesBoundViewFromLogicRegistry()
    {
        GameObject managerObject = null;
        GameObject viewObject = null;
        LogicEntityId entityId = default;
        bool viewBound = false;

        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        try
        {
            entityId = LogicEntityLifecycleService.RequestSpawn(new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "Fog3EnemyVisibilityTest"));
            LogicEntityState logicState = LogicEntityStateStore.GetRequired(entityId);
            EntityRegistry.Register(logicState);

            viewObject = new GameObject("Fog3EnemyVisibilityView");
            Entity entityComponent = viewObject.AddComponent<Entity>();
            MAEntity view = viewObject.AddComponent<MAEntity>();
            MeshRenderer renderer = viewObject.AddComponent<MeshRenderer>();
            FieldInfo entityIdField = typeof(Entity).GetField("m_Id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo entityField = typeof(EntityLogic).GetField("m_Entity", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo cachedEntityIdProperty = typeof(EntityBase).GetProperty(nameof(EntityBase.Id));
            PropertyInfo logicEntityIdProperty = typeof(MAEntity).GetProperty(nameof(MAEntity.LogicEntityId));
            Assert.NotNull(entityIdField);
            Assert.NotNull(entityField);
            Assert.NotNull(cachedEntityIdProperty);
            Assert.NotNull(logicEntityIdProperty);
            entityIdField.SetValue(entityComponent, 404);
            entityField.SetValue(view, entityComponent);
            cachedEntityIdProperty.SetValue(view, 404);
            logicEntityIdProperty.SetValue(view, entityId);
            Assert.AreEqual(404, view.Id);

            LogicEntityLifecycleService.BindView(entityId, view.Id, view);
            viewBound = true;

            managerObject = new GameObject("Fog3EnemyVisibilityManager");
            managerObject.SetActive(false);
            Fog3Manager manager = managerObject.AddComponent<Fog3Manager>();
            MethodInfo updateEnemyVisibility = typeof(Fog3Manager).GetMethod(
                "UpdateEnemyVisibilityByFog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateEnemyVisibility);

            Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsFalse(renderer.enabled);

            map.AddVisibility(0, 0, 1f);
            updateEnemyVisibility.Invoke(manager, new object[] { map });
            Assert.IsTrue(renderer.enabled);

            manager.GetEnemyUnitVisibilityDiagnostics(
                out int aliveLogicCount,
                out int boundViewCount,
                out int trackedViewCount,
                out int renderableViewCount,
                out int fogVisibleViewCount,
                out int rendererMismatchViewCount);
            Assert.AreEqual(1, aliveLogicCount);
            Assert.AreEqual(1, boundViewCount);
            Assert.AreEqual(1, trackedViewCount);
            Assert.AreEqual(1, renderableViewCount);
            Assert.AreEqual(1, fogVisibleViewCount);
            Assert.AreEqual(0, rendererMismatchViewCount);
        }
        finally
        {
            if (managerObject != null)
                Object.DestroyImmediate(managerObject);
            if (viewBound)
                LogicEntityLifecycleService.UnbindView(entityId, 404);
            EntityRegistry.Clear();
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            LogicEntityLifecycleService.EndTimeline();
            LogicTimeControlService.EndTimeline();
        }
    }

    private static Fog3MapData CreateMap(bool[] walkable)
    {
        return new Fog3MapData(new Fog3TerrainInfo(
            3,
            2,
            1f,
            Vector3.zero,
            walkable,
            "StageCheckpointTest"));
    }
}
