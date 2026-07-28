using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using UnityEngine;

public sealed class StageCheckpointServiceTests
{
    private InGameDataCheckpoint m_OriginalInGameData;
    private LogicPersistentIdAllocatorSnapshot m_OriginalPersistentIds;
    private bool m_StartedPersistentAllocator;
    private bool m_StartedEntityStateStore;
    private bool m_InitializedStrongholdMap;

    [SetUp]
    public void SetUp()
    {
        EnsureInGameDataModel();
        for (int i = 0; i <= (int)IngameValueType.MaxSupply; i++)
        {
            var type = (IngameValueType)i;
            InGameDataModel.SetValue(type, InGameDataModel.GetValue(type), false);
        }
        m_OriginalInGameData = InGameDataModel.CaptureStageCheckpointState();

        if (StageCheckpointService.IsActive)
            StageCheckpointService.EndSession();
        if (!LogicPersistentIdAllocator.IsActive)
        {
            LogicPersistentIdAllocator.BeginTimeline();
            m_StartedPersistentAllocator = true;
        }
        m_OriginalPersistentIds = LogicPersistentIdAllocator.CaptureSnapshot();
        if (!LogicEntityStateStore.IsActive)
        {
            LogicEntityStateStore.BeginTimeline();
            m_StartedEntityStateStore = true;
        }
        EntityRegistry.Clear();
        if (!LogicStrongholdMap.IsInitialized)
        {
            LogicStrongholdMap.Initialize(
                FixVector2.Zero,
                new FixVector2(Fix64.One, Fix64.Zero),
                new FixVector2(Fix64.Zero, Fix64.One),
                Fix64.One,
                new[] { new LogicStrongholdCellDefinition("stronghold-a", 3, 4) });
            m_InitializedStrongholdMap = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (StageCheckpointService.IsActive)
            StageCheckpointService.EndSession();
        InGameDataModel.RestoreStageCheckpointState(m_OriginalInGameData, false);
        if (m_StartedPersistentAllocator && LogicPersistentIdAllocator.IsActive)
            LogicPersistentIdAllocator.EndTimeline();
        else if (LogicPersistentIdAllocator.IsActive)
            LogicPersistentIdAllocator.RestoreSnapshot(m_OriginalPersistentIds);
        if (m_StartedEntityStateStore && LogicEntityStateStore.IsActive)
            LogicEntityStateStore.EndTimeline();
        EntityRegistry.Clear();
        if (m_InitializedStrongholdMap && LogicStrongholdMap.IsInitialized)
            LogicStrongholdMap.Clear();
    }

    [Test]
    public void CaptureStageStart_AppendsImmutableNormalizedHistory()
    {
        var fog = new Fog3MapData(new Fog3TerrainInfo(
            2,
            2,
            1f,
            Vector3.zero,
            new[] { true, true, true, true },
            "StageCheckpointServiceTests"));
        fog.MarkExplored(0, 0);
        fog.AddVisibility(0, 0, 1f);
        InGameDataModel.SetPhase(GamePhase.BuildBeforeInvade, false);
        InGameDataModel.SetValue(IngameValueType.Coin, 100, false);
        LogicEntityState building = CreatePendingBuilding(
            "building-checkpoint-1",
            "Buil_Prod_Lv2",
            new FixVector2((Fix64)3, (Fix64)4),
            "stronghold-a",
            0);
        building.ProductionProps.ArmyForce = Fix64.FromRaw(12);
        building.ProductionProps.StoredProduction = 8;

        StageCheckpointService.BeginSession("Lv_Test");
        StageCheckpoint first = StageCheckpointService.CaptureStageStart("BuildBeforeInvade", fog);

        LogicPersistentIdAllocator.AllocateBuildingInstanceId();
        InGameDataModel.SetPhase(GamePhase.Invade, false);
        InGameDataModel.SetValue(IngameValueType.Coin, 75, false);
        fog.MarkExplored(1, 1);
        fog.AddVisibility(1, 1, 1f);
        StageCheckpoint second = StageCheckpointService.CaptureStageStart("Invade", fog);

        Assert.AreEqual(2, StageCheckpointService.History.Count);
        Assert.AreSame(first, StageCheckpointService.GetRequired(1));
        Assert.AreSame(second, StageCheckpointService.GetRequired(2));
        Assert.AreEqual(1, first.PhaseEpoch);
        Assert.AreEqual(2, second.PhaseEpoch);
        Assert.AreEqual(GamePhase.BuildBeforeInvade, first.Phase);
        Assert.AreEqual(GamePhase.Invade, second.Phase);
        Assert.AreEqual(1, first.Buildings.Count);
        Assert.AreEqual("building-checkpoint-1", first.Buildings[0].BuildingInstanceId);
        Assert.AreEqual("Buil_Prod_Lv2", first.Buildings[0].BuildingIdentifier);
        Assert.AreEqual(building.PositionFixed, first.Buildings[0].Position);
        Assert.AreEqual("stronghold-a", first.Buildings[0].StrongholdId);
        Assert.AreEqual(Fix64.FromRaw(12), first.Buildings[0].ArmyForce);
        Assert.AreEqual(8, first.Buildings[0].StoredProduction);
        Assert.IsTrue(first.Buildings[0].IsGameEndConditionBuilding);
        Assert.IsTrue(first.Buildings[0].IsNavigationStaticBaked);
        Assert.AreEqual(1, first.Strongholds.Count);
        Assert.AreEqual("stronghold-a", first.Strongholds[0].StrongholdId);
        Assert.AreEqual(m_OriginalPersistentIds.LastBuildingInstanceValue, first.PersistentIdSnapshot.LastBuildingInstanceValue);
        Assert.AreEqual(m_OriginalPersistentIds.LastBuildingInstanceValue + 1, second.PersistentIdSnapshot.LastBuildingInstanceValue);
        Assert.AreNotEqual(first.InGameData.ContentHash, second.InGameData.ContentHash);
        Assert.AreNotEqual(first.FogExploration.ContentHash, second.FogExploration.ContentHash);
        Assert.AreNotEqual(first.ContentHash, second.ContentHash);
    }

    [Test]
    public void PrepareRestore_ValidatesTopologyAndRetainsOnlySelectedHistoryPrefix()
    {
        var fog = new Fog3MapData(new Fog3TerrainInfo(
            2,
            2,
            1f,
            Vector3.zero,
            new[] { true, true, true, true },
            "StageCheckpointServiceTests"));
        InGameDataModel.SetPhase(GamePhase.BuildBeforeInvade, false);
        CreatePendingBuilding(
            "building-checkpoint-restore",
            "Buil_Prod_Lv2",
            new FixVector2((Fix64)3, (Fix64)4),
            "stronghold-a",
            0);

        StageCheckpointService.BeginSession("Lv_Test");
        StageCheckpoint selected = StageCheckpointService.CaptureStageStart("BuildBeforeInvade", fog);
        InGameDataModel.SetPhase(GamePhase.Invade, false);
        StageCheckpointService.CaptureStageStart("Invade", fog);

        StageCheckpointRestoreRequest request = StageCheckpointService.PrepareRestore(1, fog);
        Assert.AreSame(selected, request.Checkpoint);
        Assert.AreEqual(1, request.RetainedHistory.Count);
        Assert.AreSame(selected, request.RetainedHistory[0]);

        var incompatibleFog = new Fog3MapData(new Fog3TerrainInfo(
            2,
            2,
            1f,
            Vector3.zero,
            new[] { true, true, false, true },
            "StageCheckpointServiceTests.Incompatible"));
        Assert.Throws<System.InvalidOperationException>(() => StageCheckpointService.PrepareRestore(1, incompatibleFog));

        StageCheckpointService.EndSession();
        StageCheckpointService.BeginSession("Lv_Test", request.RetainedHistory);
        Assert.AreEqual(1, StageCheckpointService.History.Count);
        Assert.AreEqual(1, StageCheckpointService.RetainedFogSnapshotCount);
        InGameDataModel.SetPhase(GamePhase.Invade, false);
        StageCheckpoint next = StageCheckpointService.CaptureStageStart("Invade", fog);
        Assert.AreEqual(2, next.PhaseEpoch);
    }

    private static void EnsureInGameDataModel()
    {
        System.Reflection.FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(dataModelField);
        var component = dataModelField.GetValue(null) as GameFramework.DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject("StageCheckpointServiceTests_DataModel");
            component = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField.SetValue(null, component);
        }

        System.Reflection.FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(dataModelsField);
        object dataModels = dataModelsField.GetValue(component);
        if (dataModels == null || dataModels.GetType() != dataModelsField.FieldType)
        {
            dataModels = System.Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(component, dataModels);
        }
        if (component.GetDataModel<InGameDataModel>() != null)
            return;

        var model = (InGameDataModel)System.Activator.CreateInstance(typeof(InGameDataModel), true);
        System.Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
        Assert.NotNull(pairType);
        object pair = System.Activator.CreateInstance(
            pairType,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { typeof(InGameDataModel), 0 },
            null);
        System.Reflection.MethodInfo addMethod = dataModels.GetType().GetMethod("Add");
        Assert.NotNull(addMethod);
        addMethod.Invoke(dataModels, new[] { pair, model });
    }

    private static LogicEntityState CreatePendingBuilding(
        string instanceId,
        string identifier,
        FixVector2 position,
        string strongholdId,
        int ownerFactionId)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            position,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
            identifier);
        LogicEntityState state = LogicEntityStateStore.Create(new LogicEntityId(900001), descriptor);
        state.Configure(
            null,
            new CreaturePropertyManager(property =>
                property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
            0,
            true,
            null,
            false);
        var move = new NoMoveComp();
        state.SetMoveComp(move);
        move.Init(state);
        var attack = new NoAtkComp();
        state.SetAtkComp(attack);
        attack.Init(state);
        var targeting = new NoTargetingComp();
        state.SetTargetingComp(targeting);
        targeting.Init(state);
        var buildingData = new BuildingData(
            identifier,
            BuilType.Prod,
            Archetype.Coding,
            string.Empty,
            string.Empty,
            string.Empty,
            2,
            0,
            (Fix64)100,
            null,
            Fix64.Zero,
            System.Array.Empty<Fix64>(),
            string.Empty,
            0,
            System.Array.Empty<string>());
        state.ConfigureBuilding(
            buildingData,
            instanceId,
            strongholdId,
            ownerFactionId,
            LogicCombatShape.AxisAlignedBox(position, new FixVector2(Fix64.One, Fix64.One)),
            System.Array.Empty<LogicCombatShape>(),
            System.Array.Empty<LogicInteractionOptionDescriptor>(),
            false,
            null,
            true,
            true);
        return state;
    }
}
