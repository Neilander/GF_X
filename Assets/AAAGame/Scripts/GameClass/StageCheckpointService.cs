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
        IsGameEndConditionBuilding = building.IsGameEndConditionBuilding;
        IsNavigationStaticBaked = building.IsNavigationStaticBaked;

        if (LogicWallRuntime.IsWallBuilding(building.BuildingData))
        {
            if (building.BuildingData.Lv == 0)
            {
                if (!LogicWallRuntime.TryGetPreviewCell(building.LogicEntityId, out WallGridCell previewCell))
                    throw new InvalidOperationException($"Wall preview {building.LogicEntityId.Value} has no registered cell.");
                WallCells = Array.AsReadOnly(new[] { previewCell });
                WallGateCells = Array.AsReadOnly(Array.Empty<WallGridCell>());
            }
            else
            {
                LogicWallBranchDefinition branch = LogicWallRuntime.GetRequiredBranch(building.LogicEntityId);
                var cells = new WallGridCell[branch.Cells.Count];
                var gates = new List<WallGridCell>();
                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i] = branch.Cells[i];
                    if (branch.IsGateCell(cells[i]))
                        gates.Add(cells[i]);
                }
                WallCells = Array.AsReadOnly(cells);
                WallGateCells = Array.AsReadOnly(gates.ToArray());
            }
        }
        else
        {
            WallCells = Array.AsReadOnly(Array.Empty<WallGridCell>());
            WallGateCells = Array.AsReadOnly(Array.Empty<WallGridCell>());
        }

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
    public bool IsGameEndConditionBuilding { get; }
    public bool IsNavigationStaticBaked { get; }
    public ReadOnlyCollection<WallGridCell> WallCells { get; }
    public ReadOnlyCollection<WallGridCell> WallGateCells { get; }
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
        hasher.Add(IsGameEndConditionBuilding);
        hasher.Add(IsNavigationStaticBaked);
        hasher.Add(WallCells.Count);
        for (int i = 0; i < WallCells.Count; i++)
        {
            hasher.Add(WallCells[i].X);
            hasher.Add(WallCells[i].Y);
        }
        hasher.Add(WallGateCells.Count);
        for (int i = 0; i < WallGateCells.Count; i++)
        {
            hasher.Add(WallGateCells[i].X);
            hasher.Add(WallGateCells[i].Y);
        }
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

public readonly struct StageStrongholdCheckpoint
{
    public StageStrongholdCheckpoint(string strongholdId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        StrongholdId = strongholdId;
        OwnerFactionId = ownerFactionId;
    }

    public string StrongholdId { get; }
    public int OwnerFactionId { get; }
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
        StageStrongholdCheckpoint[] strongholds,
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
        Strongholds = Array.AsReadOnly((StageStrongholdCheckpoint[])strongholds.Clone());
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
        hasher.Add(Strongholds.Count);
        for (int i = 0; i < Strongholds.Count; i++)
        {
            hasher.Add(Strongholds[i].StrongholdId);
            hasher.Add(Strongholds[i].OwnerFactionId);
        }
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
    public ReadOnlyCollection<StageStrongholdCheckpoint> Strongholds { get; }
    public Fog3ExplorationCheckpoint FogExploration { get; }
    public LogicPersistentIdAllocatorSnapshot PersistentIdSnapshot { get; }
    public ulong ContentHash { get; }
}

public sealed class StageCheckpointRestoreRequest
{
    internal StageCheckpointRestoreRequest(StageCheckpoint checkpoint, StageCheckpoint[] retainedHistory)
    {
        Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        RetainedHistory = Array.AsReadOnly((StageCheckpoint[])retainedHistory.Clone());
    }

    public StageCheckpoint Checkpoint { get; }
    internal ReadOnlyCollection<StageCheckpoint> RetainedHistory { get; }
}

public static class StageCheckpointService
{
    public const int CurrentProtocolVersion = 2;

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
        BeginSession(levelId, null);
    }

    internal static void BeginSession(string levelId, IReadOnlyList<StageCheckpoint> retainedHistory)
    {
        if (IsActive)
            throw new InvalidOperationException("StageCheckpointService.BeginSession failed: session is already active.");
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("Stage checkpoint level id is empty.", nameof(levelId));

        s_LevelId = levelId;
        s_History.Clear();
        if (retainedHistory != null)
        {
            for (int i = 0; i < retainedHistory.Count; i++)
            {
                StageCheckpoint checkpoint = retainedHistory[i]
                                             ?? throw new InvalidOperationException($"Retained stage checkpoint {i} is null.");
                if (!string.Equals(checkpoint.LevelId, levelId, StringComparison.Ordinal)
                    || checkpoint.PhaseEpoch != i + 1)
                {
                    throw new InvalidOperationException(
                        $"Retained stage checkpoint history is invalid. index={i}, level='{checkpoint.LevelId}', epoch={checkpoint.PhaseEpoch}.");
                }
                s_History.Add(checkpoint);
            }
        }
        s_NextPhaseEpoch = checked(s_History.Count + 1);
        s_RetainedFogSnapshots.Clear();
        RetainedFogPayloadBytes = 0;
        for (int i = 0; i < s_History.Count; i++)
        {
            Fog3ExplorationCheckpoint fog = s_History[i].FogExploration;
            if (s_RetainedFogSnapshots.Add(fog))
                RetainedFogPayloadBytes = checked(RetainedFogPayloadBytes + fog.StoredPayloadByteCount);
        }
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

        GamePhase phase = LogicPhaseCommandService.GetRequiredCurrentPhase();
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
            CaptureStrongholds(),
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

    public static StageCheckpointRestoreRequest PrepareRestore(int phaseEpoch, Fog3MapData currentFogMap)
    {
        if (currentFogMap == null)
            throw new ArgumentNullException(nameof(currentFogMap));
        StageCheckpoint checkpoint = GetRequired(phaseEpoch);
        ValidateRestoreCheckpoint(checkpoint, currentFogMap);
        var retained = new StageCheckpoint[phaseEpoch];
        for (int i = 0; i < retained.Length; i++)
            retained[i] = s_History[i];
        return new StageCheckpointRestoreRequest(checkpoint, retained);
    }

    private static void ValidateRestoreCheckpoint(StageCheckpoint checkpoint, Fog3MapData fogMap)
    {
        if (checkpoint.ProtocolVersion != CurrentProtocolVersion)
            throw new InvalidOperationException(
                $"Stage checkpoint protocol mismatch. expected={CurrentProtocolVersion}, actual={checkpoint.ProtocolVersion}.");
        if (!string.Equals(checkpoint.ContentVersion, LogicReplayLog.CurrentContentVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Stage checkpoint content mismatch. expected='{LogicReplayLog.CurrentContentVersion}', actual='{checkpoint.ContentVersion}'.");
        if (!string.Equals(checkpoint.LevelId, s_LevelId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Stage checkpoint level mismatch. expected='{s_LevelId}', actual='{checkpoint.LevelId}'.");
        if (checkpoint.InGameData == null
            || checkpoint.InGameData.Values.Count <= (int)IngameValueType.Phase
            || checkpoint.InGameData.Values[(int)IngameValueType.Phase] != (int)checkpoint.Phase)
            throw new InvalidOperationException("Stage checkpoint phase does not match its persistent data.");
        if (checkpoint.FogExploration == null
            || checkpoint.FogExploration.Width != fogMap.Width
            || checkpoint.FogExploration.Height != fogMap.Height
            || checkpoint.FogExploration.TerrainHash != fogMap.TerrainHash)
            throw new InvalidOperationException("Stage checkpoint fog topology does not match the active level.");

        var buildingIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < checkpoint.Buildings.Count; i++)
        {
            StageBuildingCheckpoint building = checkpoint.Buildings[i]
                                                 ?? throw new InvalidOperationException($"Stage checkpoint building {i} is null.");
            if (!buildingIds.Add(building.BuildingInstanceId))
                throw new InvalidOperationException($"Stage checkpoint has duplicate building '{building.BuildingInstanceId}'.");
        }
        for (int i = 0; i < checkpoint.InGameData.TechOwnership.Count; i++)
        {
            StageTechOwnership tech = checkpoint.InGameData.TechOwnership[i];
            for (int ownerIndex = 0; ownerIndex < tech.OwnerBuildingInstanceIds.Count; ownerIndex++)
            {
                if (!buildingIds.Contains(tech.OwnerBuildingInstanceIds[ownerIndex]))
                {
                    throw new InvalidOperationException(
                        $"Stage checkpoint tech '{tech.TechId}' references missing building '{tech.OwnerBuildingInstanceIds[ownerIndex]}'.");
                }
            }
        }
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

    private static StageStrongholdCheckpoint[] CaptureStrongholds()
    {
        IReadOnlyList<string> ids = LogicStrongholdMap.GetStrongholdIdsOrdered();
        var result = new StageStrongholdCheckpoint[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            result[i] = new StageStrongholdCheckpoint(id, LogicStrongholdMap.GetOwnerFactionIdRequired(id));
        }
        return result;
    }
}

public static class StageCheckpointRuntimeCoordinator
{
    private static StageCheckpointRestoreRequest s_PendingRestore;
    private static bool s_PersistentDataRestored;
    private static Fog3MapData s_BoundFogMap;

    public static bool IsActive { get; private set; }
    public static bool HasPendingRestore => s_PendingRestore != null;

    public static StageCheckpointRestoreRequest PrepareRestore(int phaseEpoch)
    {
        if (!IsActive)
            throw new InvalidOperationException("Cannot restore a stage checkpoint without an active session.");
        if (s_PendingRestore != null)
            throw new InvalidOperationException("A stage checkpoint restore is already pending.");
        s_PendingRestore = StageCheckpointService.PrepareRestore(phaseEpoch, RequireBoundFogMap());
        s_PersistentDataRestored = false;
        Log.Info(
            "[StageCheckpoint] Restore prepared. level={0}, stage={1}, epoch={2}, phase={3}, hash={4}.",
            s_PendingRestore.Checkpoint.LevelId,
            s_PendingRestore.Checkpoint.StageId,
            s_PendingRestore.Checkpoint.PhaseEpoch,
            s_PendingRestore.Checkpoint.Phase,
            s_PendingRestore.Checkpoint.ContentHash);
        return s_PendingRestore;
    }

    public static void CancelPendingRestore(StageCheckpointRestoreRequest request)
    {
        if (request == null || !ReferenceEquals(request, s_PendingRestore))
            throw new InvalidOperationException("Stage checkpoint restore cancellation does not match the pending request.");
        s_PendingRestore = null;
        s_PersistentDataRestored = false;
    }

    public static void AbortPendingRestore()
    {
        if (s_PendingRestore == null)
            return;
        if (IsActive)
            EndSession();
        s_PendingRestore = null;
        s_PersistentDataRestored = false;
    }

    public static bool RestorePersistentDataBeforeLevelSpawn(string levelId)
    {
        if (s_PendingRestore == null)
            return false;
        RequirePendingLevel(levelId);
        if (s_PersistentDataRestored)
            throw new InvalidOperationException("Stage checkpoint persistent data was already restored.");

        StageCheckpoint checkpoint = s_PendingRestore.Checkpoint;
        InGameDataModel.RestoreStageCheckpointState(checkpoint.InGameData, false);
        LogicPersistentIdAllocator.RestoreSnapshot(checkpoint.PersistentIdSnapshot);
        s_PersistentDataRestored = true;
        return true;
    }

    public static StageCheckpoint GetPendingRestoreForLevelSpawn(string levelId)
    {
        if (s_PendingRestore == null)
            return null;
        RequirePendingLevel(levelId);
        if (!s_PersistentDataRestored)
            throw new InvalidOperationException("Stage checkpoint persistent data must be restored before level entities spawn.");
        return s_PendingRestore.Checkpoint;
    }

    public static void RestoreStrongholdOwners(IReadOnlyList<Stronghold> strongholds, string levelId)
    {
        StageCheckpoint checkpoint = GetPendingRestoreForLevelSpawn(levelId);
        if (checkpoint == null)
            return;
        if (strongholds == null)
            throw new ArgumentNullException(nameof(strongholds));

        var viewsById = new Dictionary<string, Stronghold>(StringComparer.Ordinal);
        for (int i = 0; i < strongholds.Count; i++)
        {
            Stronghold stronghold = strongholds[i]
                                    ?? throw new InvalidOperationException($"Stronghold {i} is null during checkpoint restore.");
            string id = stronghold.strongholdData?.StrongholdId;
            if (string.IsNullOrWhiteSpace(id) || !viewsById.TryAdd(id, stronghold))
                throw new InvalidOperationException($"Stronghold identity is invalid during checkpoint restore. index={i}, id='{id}'.");
        }
        if (viewsById.Count != checkpoint.Strongholds.Count)
            throw new InvalidOperationException(
                $"Stage checkpoint stronghold count mismatch. expected={checkpoint.Strongholds.Count}, actual={viewsById.Count}.");
        for (int i = 0; i < checkpoint.Strongholds.Count; i++)
        {
            StageStrongholdCheckpoint entry = checkpoint.Strongholds[i];
            if (!viewsById.TryGetValue(entry.StrongholdId, out Stronghold stronghold))
                throw new InvalidOperationException($"Stage checkpoint references unknown stronghold '{entry.StrongholdId}'.");
            LogicStrongholdMap.SetOwnerFactionId(entry.StrongholdId, entry.OwnerFactionId);
            stronghold.OwnerFactionId = entry.OwnerFactionId;
        }
    }

    public static StageCheckpoint BeginRestoredSession(string levelId)
    {
        if (IsActive)
            throw new InvalidOperationException("Stage checkpoint runtime coordinator is already active.");
        RequirePendingLevel(levelId);
        if (!s_PersistentDataRestored)
            throw new InvalidOperationException("Stage checkpoint persistent data has not been restored.");

        StageCheckpoint checkpoint = s_PendingRestore.Checkpoint;
        Fog3MapData fogMap = BindFogMap();
        fogMap.RestoreExplorationCheckpoint(checkpoint.FogExploration);
        StageCheckpointService.BeginSession(levelId, s_PendingRestore.RetainedHistory);
        PhaseManager.PersistentStageCommitted += OnPersistentStageCommitted;
        IsActive = true;
        return checkpoint;
    }

    public static void CompleteRestore()
    {
        if (!IsActive || s_PendingRestore == null || !s_PersistentDataRestored)
            throw new InvalidOperationException("Stage checkpoint restore cannot complete from the current state.");
        Log.Info(
            "[StageCheckpoint] Restore committed. level={0}, stage={1}, epoch={2}, phase={3}, hash={4}.",
            s_PendingRestore.Checkpoint.LevelId,
            s_PendingRestore.Checkpoint.StageId,
            s_PendingRestore.Checkpoint.PhaseEpoch,
            s_PendingRestore.Checkpoint.Phase,
            s_PendingRestore.Checkpoint.ContentHash);
        s_PendingRestore = null;
        s_PersistentDataRestored = false;
    }

    public static void BeginSession(string levelId)
    {
        if (IsActive)
            throw new InvalidOperationException("Stage checkpoint runtime coordinator is already active.");

        if (s_PendingRestore != null)
            throw new InvalidOperationException("Use BeginRestoredSession while a stage checkpoint restore is pending.");
        BindFogMap();
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
        s_BoundFogMap = null;
    }

    private static void RequirePendingLevel(string levelId)
    {
        if (s_PendingRestore == null)
            throw new InvalidOperationException("No stage checkpoint restore is pending.");
        if (!string.Equals(s_PendingRestore.Checkpoint.LevelId, levelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stage checkpoint restore level mismatch. expected='{s_PendingRestore.Checkpoint.LevelId}', actual='{levelId}'.");
        }
    }

    private static void OnPersistentStageCommitted(GamePhase phase)
    {
        Fog3MapData fogMap = RequireBoundFogMap();
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

    private static Fog3MapData BindFogMap()
    {
        if (s_BoundFogMap != null)
            throw new InvalidOperationException("Stage checkpoint Fog3 map is already bound.");
        Fog3Manager manager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
        if (manager == null)
            throw new InvalidOperationException("Stage checkpoint requires Fog3Manager.");
        if (!manager.IsInitialized || manager.Controller?.MapData == null)
            manager.Initialize();
        s_BoundFogMap = manager.Controller?.MapData
                        ?? throw new InvalidOperationException("Stage checkpoint requires initialized Fog3 map data.");
        return s_BoundFogMap;
    }

    private static Fog3MapData RequireBoundFogMap()
    {
        return s_BoundFogMap
               ?? throw new InvalidOperationException("Stage checkpoint Fog3 map was not bound before the runtime session began.");
    }
}
