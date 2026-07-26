using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AAAGame.MiniMap.FOG3;
using UnityGameFramework.Runtime;

public sealed class StageBuildingCheckpoint
{
    internal StageBuildingCheckpoint(IBuildingLogicContext building)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.BuildingData == null || string.IsNullOrWhiteSpace(building.BuildingData.Identifier))
            throw new InvalidOperationException($"Stage checkpoint building data is missing. entity={building.LogicEntityId.Value}.");
        if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            throw new InvalidOperationException($"Stage checkpoint building instance id is missing. entity={building.LogicEntityId.Value}.");

        BuildingInstanceId = building.BuildingInstanceId;
        BuildingIdentifier = building.BuildingData.Identifier;
        StrongholdId = building.StrongholdId;
        OwnerFactionId = building.OwnerFactionId;
        Position = building.PositionFixed;
        Forward = building.ForwardFixed;

        BuildingExtraProps props = building.ProductionProps
                                   ?? throw new InvalidOperationException($"Stage checkpoint building production state is missing. instance='{BuildingInstanceId}'.");
        ArmyForce = props.ArmyForce;
        Production = props.Production;
        DynamicProduction = props.DynamicProduction;
        ProductionCap = props.ProductionCap;
        StoredProduction = props.StoredProduction;
        ProductionTraitFirstDay = props.ProductionTraitFirstDay;
        ConditionCount = props.ConditionCount;
        ProductionType = props.ProductionType;
    }

    public string BuildingInstanceId { get; }
    public string BuildingIdentifier { get; }
    public string StrongholdId { get; }
    public int OwnerFactionId { get; }
    public FixVector2 Position { get; }
    public FixVector2 Forward { get; }
    public Fix64 ArmyForce { get; }
    public Fix64 Production { get; }
    public Fix64 DynamicProduction { get; }
    public Fix64 ProductionCap { get; }
    public int StoredProduction { get; }
    public int ProductionTraitFirstDay { get; }
    public int ConditionCount { get; }
    public ProductionType ProductionType { get; }

    internal void WriteState(LogicStateHasher hasher)
    {
        hasher.Add(BuildingInstanceId);
        hasher.Add(BuildingIdentifier);
        hasher.Add(StrongholdId);
        hasher.Add(OwnerFactionId);
        hasher.Add(Position.x.RawValue);
        hasher.Add(Position.y.RawValue);
        hasher.Add(Forward.x.RawValue);
        hasher.Add(Forward.y.RawValue);
        hasher.Add(ArmyForce.RawValue);
        hasher.Add(Production.RawValue);
        hasher.Add(DynamicProduction.RawValue);
        hasher.Add(ProductionCap.RawValue);
        hasher.Add(StoredProduction);
        hasher.Add(ProductionTraitFirstDay);
        hasher.Add(ConditionCount);
        hasher.Add((int)ProductionType);
    }
}

public sealed class StageCheckpoint
{
    internal StageCheckpoint(
        string levelId,
        string stageId,
        int phaseEpoch,
        GamePhase phase,
        InGameDataCheckpoint inGameData,
        StageBuildingCheckpoint[] buildings,
        Fog3ExplorationCheckpoint fogExploration,
        LogicPersistentIdAllocatorSnapshot persistentIdSnapshot)
    {
        ProtocolVersion = StageCheckpointService.CurrentProtocolVersion;
        ContentVersion = LogicReplayLog.CurrentContentVersion;
        LevelId = levelId;
        StageId = stageId;
        PhaseEpoch = phaseEpoch;
        Phase = phase;
        InGameData = inGameData;
        Buildings = Array.AsReadOnly((StageBuildingCheckpoint[])buildings.Clone());
        FogExploration = fogExploration;
        PersistentIdSnapshot = persistentIdSnapshot;

        var hasher = new LogicStateHasher();
        hasher.Add(0x535441474543484BUL);
        hasher.Add(ProtocolVersion);
        hasher.Add(ContentVersion);
        hasher.Add(LevelId);
        hasher.Add(StageId);
        hasher.Add(PhaseEpoch);
        hasher.Add((int)Phase);
        hasher.Add(InGameData.ContentHash);
        hasher.Add(Buildings.Count);
        for (int i = 0; i < Buildings.Count; i++)
            Buildings[i].WriteState(hasher);
        hasher.Add(FogExploration.ContentHash);
        hasher.Add(PersistentIdSnapshot.LastBuildingInstanceValue);
        ContentHash = hasher.Hash;
    }

    public int ProtocolVersion { get; }
    public string ContentVersion { get; }
    public string LevelId { get; }
    public string StageId { get; }
    public int PhaseEpoch { get; }
    public GamePhase Phase { get; }
    public InGameDataCheckpoint InGameData { get; }
    public ReadOnlyCollection<StageBuildingCheckpoint> Buildings { get; }
    public Fog3ExplorationCheckpoint FogExploration { get; }
    public LogicPersistentIdAllocatorSnapshot PersistentIdSnapshot { get; }
    public ulong ContentHash { get; }
}

public static class StageCheckpointService
{
    public const int CurrentProtocolVersion = 1;

    private static readonly List<StageCheckpoint> s_History = new List<StageCheckpoint>();
    private static readonly HashSet<Fog3ExplorationCheckpoint> s_RetainedFogSnapshots = new HashSet<Fog3ExplorationCheckpoint>();
    private static string s_LevelId;
    private static int s_NextPhaseEpoch;

    public static bool IsActive { get; private set; }
    public static ReadOnlyCollection<StageCheckpoint> History => s_History.AsReadOnly();
    public static int RetainedFogSnapshotCount => s_RetainedFogSnapshots.Count;
    public static long RetainedFogPayloadBytes { get; private set; }

    public static void BeginSession(string levelId)
    {
        if (IsActive)
            throw new InvalidOperationException("StageCheckpointService.BeginSession failed: session is already active.");
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("Stage checkpoint level id is empty.", nameof(levelId));

        s_LevelId = levelId;
        s_NextPhaseEpoch = 1;
        s_History.Clear();
        s_RetainedFogSnapshots.Clear();
        RetainedFogPayloadBytes = 0;
        IsActive = true;
    }

    public static void EndSession()
    {
        if (!IsActive)
            throw new InvalidOperationException("StageCheckpointService.EndSession failed: session is not active.");
        s_LevelId = null;
        s_NextPhaseEpoch = 0;
        s_History.Clear();
        s_RetainedFogSnapshots.Clear();
        RetainedFogPayloadBytes = 0;
        IsActive = false;
    }

    public static StageCheckpoint CaptureStageStart(string stageId, Fog3MapData fogMap)
    {
        if (!IsActive)
            throw new InvalidOperationException("StageCheckpointService.CaptureStageStart failed: session is not active.");
        if (string.IsNullOrWhiteSpace(stageId))
            throw new ArgumentException("Stage checkpoint stage id is empty.", nameof(stageId));
        if (fogMap == null)
            throw new ArgumentNullException(nameof(fogMap));
        if (!LogicPersistentIdAllocator.IsActive)
            throw new InvalidOperationException("Stage checkpoint requires an active persistent id allocator.");

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        if (!Enum.IsDefined(typeof(GamePhase), phase))
            throw new InvalidOperationException($"Stage checkpoint has invalid current phase {phase}.");

        StageBuildingCheckpoint[] buildings = CaptureBuildings();
        var checkpoint = new StageCheckpoint(
            s_LevelId,
            stageId,
            s_NextPhaseEpoch,
            phase,
            InGameDataModel.CaptureStageCheckpointState(),
            buildings,
            fogMap.CaptureExplorationCheckpoint(),
            LogicPersistentIdAllocator.CaptureSnapshot());

        s_History.Add(checkpoint);
        if (s_RetainedFogSnapshots.Add(checkpoint.FogExploration))
        {
            RetainedFogPayloadBytes = checked(
                RetainedFogPayloadBytes + checkpoint.FogExploration.StoredPayloadByteCount);
        }
        s_NextPhaseEpoch = checked(s_NextPhaseEpoch + 1);
        return checkpoint;
    }

    public static StageCheckpoint GetRequired(int phaseEpoch)
    {
        if (!IsActive)
            throw new InvalidOperationException("StageCheckpointService.GetRequired failed: session is not active.");
        if (phaseEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(phaseEpoch));
        int index = phaseEpoch - 1;
        if (index >= s_History.Count || s_History[index].PhaseEpoch != phaseEpoch)
            throw new KeyNotFoundException($"Stage checkpoint phase epoch {phaseEpoch} does not exist.");
        return s_History[index];
    }

    private static StageBuildingCheckpoint[] CaptureBuildings()
    {
        LogicEntityState[] states = LogicEntityStateStore.CaptureStageBuildingStates();
        var buildings = new List<StageBuildingCheckpoint>();
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < states.Length; i++)
        {
            IBuildingLogicContext building = states[i]
                                                ?? throw new InvalidOperationException($"Stage checkpoint encountered a null building state. index={i}.");
            if (!instanceIds.Add(building.BuildingInstanceId))
                throw new InvalidOperationException($"Stage checkpoint encountered duplicate building instance id '{building.BuildingInstanceId}'.");
            buildings.Add(new StageBuildingCheckpoint(building));
        }

        buildings.Sort((left, right) => string.CompareOrdinal(left.BuildingInstanceId, right.BuildingInstanceId));
        return buildings.ToArray();
    }
}

public static class StageCheckpointRuntimeCoordinator
{
    public static bool IsActive { get; private set; }

    public static void BeginSession(string levelId)
    {
        if (IsActive)
            throw new InvalidOperationException("Stage checkpoint runtime coordinator is already active.");

        RequireFogMap();
        StageCheckpointService.BeginSession(levelId);
        PhaseManager.PersistentStageCommitted += OnPersistentStageCommitted;
        IsActive = true;
    }

    public static void EndSession()
    {
        if (!IsActive)
            throw new InvalidOperationException("Stage checkpoint runtime coordinator is not active.");

        PhaseManager.PersistentStageCommitted -= OnPersistentStageCommitted;
        StageCheckpointService.EndSession();
        IsActive = false;
    }

    private static void OnPersistentStageCommitted(GamePhase phase)
    {
        Fog3MapData fogMap = RequireFogMap();
        StageCheckpoint checkpoint = StageCheckpointService.CaptureStageStart(phase.ToString(), fogMap);
        Log.Info(
            "[StageCheckpoint] Captured. level={0}, stage={1}, epoch={2}, phase={3}, buildings={4}, " +
            "fogRaw={5}, fogStored={6}, retainedFogSnapshots={7}, retainedFogBytes={8}, hash={9}.",
            checkpoint.LevelId,
            checkpoint.StageId,
            checkpoint.PhaseEpoch,
            checkpoint.Phase,
            checkpoint.Buildings.Count,
            checkpoint.FogExploration.RawPayloadByteCount,
            checkpoint.FogExploration.StoredPayloadByteCount,
            StageCheckpointService.RetainedFogSnapshotCount,
            StageCheckpointService.RetainedFogPayloadBytes,
            checkpoint.ContentHash);
    }

    private static Fog3MapData RequireFogMap()
    {
        Fog3Manager manager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
        if (manager == null)
            throw new InvalidOperationException("Stage checkpoint requires Fog3Manager.");
        if (!manager.IsInitialized || manager.Controller?.MapData == null)
            manager.Initialize();
        return manager.Controller?.MapData
               ?? throw new InvalidOperationException("Stage checkpoint requires initialized Fog3 map data.");
    }
}
