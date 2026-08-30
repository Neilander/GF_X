using System;
using System.Collections.Generic;

public enum LevelObjectiveStatus
{
    Active = 0,
    Completed = 1,
    Failed = 2,
}

public readonly struct LevelObjectiveState
{
    public LevelObjectiveState(LevelObjectiveDefinition definition, LevelObjectiveStatus status)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Status = status;
    }

    public LevelObjectiveDefinition Definition { get; }
    public LevelObjectiveStatus Status { get; }
}

public readonly struct LogicGameEndResult
{
    private LogicGameEndResult(bool isWin, string failedObjectiveIdentifier)
    {
        IsWin = isWin;
        FailedObjectiveIdentifier = failedObjectiveIdentifier;
    }

    public bool IsWin { get; }
    public string FailedObjectiveIdentifier { get; }

    public static LogicGameEndResult CreateWin() => new LogicGameEndResult(true, null);
    public static LogicGameEndResult CreateFail(string objectiveIdentifier) =>
        new LogicGameEndResult(false, objectiveIdentifier);
}

public readonly struct ConditionTargetMarkerState
{
    public ConditionTargetMarkerState(string markerId, FixVector2 position)
    {
        if (string.IsNullOrWhiteSpace(markerId))
            throw new ArgumentException("Condition target marker id is required.", nameof(markerId));
        MarkerId = markerId;
        Position = position;
    }

    public string MarkerId { get; }
    public FixVector2 Position { get; }
}

public static class LogicGameEndService
{
    public static event System.Action PlayerConditionTargetsChanged;
    public static event System.Action TargetMarkersChanged;
    private sealed class RuntimeObjective
    {
        public LevelObjectiveDefinition Definition;
        public LevelObjectiveStatus Status;
    }

    private static readonly HashSet<string> s_EnemyTargetBuildingInstanceIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_PlayerTargetBuildingInstanceIds = new(StringComparer.Ordinal);
    private static readonly List<string> s_SortedTargetIds = new();
    private static readonly Dictionary<string, IBuildingLogicContext> s_ResolvedTargets = new(StringComparer.Ordinal);
    private static readonly List<RuntimeObjective> s_Objectives = new();
    private static readonly List<LevelObjectiveState> s_ObjectiveSnapshot = new();
    private static readonly List<ConditionTargetMarkerState> s_TargetMarkerSnapshot = new();
    private static string s_CurrentLevelIdentifier;

    public static event Action<LogicGameEndResult> GameEnded;
    public static event Action ObjectivesChanged;

    public static bool IsActive { get; private set; }
    public static bool IsInitialized { get; private set; }
    public static bool IsGameEnded { get; private set; }
    public static bool IsWin { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static string CurrentLevelIdentifier => s_CurrentLevelIdentifier;
    public static IReadOnlyCollection<string> PlayerTargetBuildingInstanceIds => s_PlayerTargetBuildingInstanceIds;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicGameEndService.BeginTimeline failed: service is already active.");
        if (!LogicTimeControlService.IsActive)
            throw new InvalidOperationException("LogicGameEndService.BeginTimeline requires active logic time control.");
        IsActive = true;
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        ClearState();
        IsActive = false;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        ClearState();
    }

    public static void Initialize(LevelData levelData)
    {
        EnsureActive();
        if (levelData == null)
            throw new ArgumentNullException(nameof(levelData));
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException($"LogicGameEndService.Initialize requires frame zero. current={LogicTimeControlService.CurrentFrame}.");
        if (string.IsNullOrWhiteSpace(levelData.Identifier))
            throw new InvalidOperationException("LogicGameEndService.Initialize requires a stable level identifier.");
        if (levelData.PrimaryObjectives == null || levelData.PrimaryObjectives.Length == 0)
            throw new InvalidOperationException($"Level '{levelData.Identifier}' requires at least one primary objective.");

        ClearState();
        s_CurrentLevelIdentifier = levelData.Identifier;
        AddObjectives(levelData.PrimaryObjectives, true);
        AddObjectives(levelData.OptionalObjectives, false);
        bool hasProgressObjective = false;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            ValidateObjective(s_Objectives[i].Definition);
            if (s_Objectives[i].Definition.IsPrimary && !IsConstraint(s_Objectives[i].Definition.ObjectiveIdentifier))
                hasProgressObjective = true;
        }
        if (!hasProgressObjective)
            throw new InvalidOperationException($"Level '{levelData.Identifier}' requires a completable primary objective.");
        IsInitialized = true;
        ObjectivesChanged?.Invoke();
    }

    public static IReadOnlyList<LevelObjectiveState> GetObjectiveSnapshot()
    {
        EnsureInitialized();
        s_ObjectiveSnapshot.Clear();
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            s_ObjectiveSnapshot.Add(new LevelObjectiveState(item.Definition, item.Status));
        }
        return s_ObjectiveSnapshot;
    }

    public static IReadOnlyList<ConditionTargetMarkerState> GetTargetMarkerSnapshot()
    {
        EnsureInitialized();
        ResolveTargets();
        s_TargetMarkerSnapshot.Clear();
        s_SortedTargetIds.Clear();
        foreach (string targetId in s_EnemyTargetBuildingInstanceIds)
            s_SortedTargetIds.Add(targetId);
        s_SortedTargetIds.Sort(StringComparer.Ordinal);

        for (int i = 0; i < s_SortedTargetIds.Count; i++)
        {
            string targetId = s_SortedTargetIds[i];
            if (!s_ResolvedTargets.TryGetValue(targetId, out IBuildingLogicContext target))
                throw new InvalidOperationException($"Cannot resolve condition target marker '{targetId}'.");
            if (target.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                continue;

            s_TargetMarkerSnapshot.Add(new ConditionTargetMarkerState(targetId, target.PositionFixed));
        }

        return s_TargetMarkerSnapshot;
    }

    public static int GetCompletedOptionalExperience()
    {
        EnsureInitialized();
        int total = 0;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (!item.Definition.IsPrimary && item.Status == LevelObjectiveStatus.Completed)
                total = checked(total + item.Definition.Experience);
        }
        return total;
    }

    public static void RegisterInitialConditionBuilding(string buildingInstanceId, int initialOwnerFactionId)
    {
        EnsureInitialized();
        if (LogicTimeControlService.CurrentFrame != 0)
            throw new InvalidOperationException($"LogicGameEndService.RegisterInitialConditionBuilding requires frame zero. current={LogicTimeControlService.CurrentFrame}.");
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is required.", nameof(buildingInstanceId));
        if (initialOwnerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(initialOwnerFactionId));
        if (s_PlayerTargetBuildingInstanceIds.Contains(buildingInstanceId) || s_EnemyTargetBuildingInstanceIds.Contains(buildingInstanceId))
            throw new InvalidOperationException($"Duplicate target building instance id '{buildingInstanceId}'.");
        if (initialOwnerFactionId == EntitySideHelper.PlayerFactionId)
        {
            s_PlayerTargetBuildingInstanceIds.Add(buildingInstanceId);
            PlayerConditionTargetsChanged?.Invoke();
        }
        else
            s_EnemyTargetBuildingInstanceIds.Add(buildingInstanceId);
        TargetMarkersChanged?.Invoke();
    }

    public static bool TryGetNearestPlayerConditionBuilding(FixVector2 origin, out IBuildingLogicContext building)
    {
        EnsureInitialized();
        return LogicBuildingQueryService.TryGetNearestAliveByInstanceIds(
            s_PlayerTargetBuildingInstanceIds,
            origin,
            out building);
    }

    public static bool TryGetNearestPlayerInitialConditionBuilding(FixVector2 origin, out IBuildingLogicContext building)
    {
        return TryGetNearestPlayerConditionBuilding(origin, out building);
    }

    public static bool IsPlayerTargetBuilding(string buildingInstanceId)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is required.", nameof(buildingInstanceId));
        return s_PlayerTargetBuildingInstanceIds.Contains(buildingInstanceId);
    }

    public static void RegisterCapturedBuildingAsPlayerTarget(IBuildingLogicContext building)
    {
        EnsureInitialized();
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.OwnerFactionId != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException("Captured building must be player owned before target registration.");
        if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            throw new InvalidOperationException("Captured building has no stable building instance id.");
        if (building.IsGameEndConditionBuilding
            || s_PlayerTargetBuildingInstanceIds.Contains(building.BuildingInstanceId)
            || s_EnemyTargetBuildingInstanceIds.Contains(building.BuildingInstanceId))
        {
            throw new InvalidOperationException($"Captured building '{building.BuildingInstanceId}' is already a game-end target.");
        }
        building.SetGameEndConditionBuilding(true);
        if (!s_PlayerTargetBuildingInstanceIds.Add(building.BuildingInstanceId))
            throw new InvalidOperationException($"Failed to register captured player target '{building.BuildingInstanceId}'.");
        PlayerConditionTargetsChanged?.Invoke();
    }

    public static void ResolveCapturedEnemyTarget(IBuildingLogicContext building)
    {
        EnsureInitialized();
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (building.OwnerFactionId != EntitySideHelper.PlayerFactionId)
            throw new InvalidOperationException("Captured enemy target must be player owned before resolution.");
        if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            throw new InvalidOperationException("Captured enemy target has no stable building instance id.");
        if (!building.IsGameEndConditionBuilding)
            throw new InvalidOperationException($"Captured enemy target '{building.BuildingInstanceId}' is not a condition building.");
        if (!s_EnemyTargetBuildingInstanceIds.Contains(building.BuildingInstanceId))
            throw new InvalidOperationException($"Condition building '{building.BuildingInstanceId}' was not registered as an enemy target.");
        building.SetGameEndConditionBuilding(false);
        TargetMarkersChanged?.Invoke();
    }

    public static void CompleteScriptedObjective(string objectiveIdentifier)
    {
        EnsureInitialized();
        if (IsGameEnded)
            throw new InvalidOperationException("Cannot complete an objective after the game has ended.");
        RuntimeObjective objective = FindActivePrimaryObjective(objectiveIdentifier);
        if (objective.Definition.ObjectiveIdentifier != LevelObjectiveIdentifiers.UpgradeCodingCoreLevel3)
            throw new InvalidOperationException($"Objective '{objectiveIdentifier}' is not scripted.");
        SetStatus(objective, LevelObjectiveStatus.Completed);
        EvaluateTerminalState();
    }

    public static void ApplyFrame(ulong frame)
    {
        EnsureInitialized();
        if (frame == 0 || frame != LogicTimeControlService.CurrentFrame)
            throw new InvalidOperationException($"LogicGameEndService.ApplyFrame frame mismatch. requested={frame}, current={LogicTimeControlService.CurrentFrame}.");
        ulong expectedFrame = checked(LastAppliedFrame + 1);
        if (frame != expectedFrame)
            throw new InvalidOperationException($"LogicGameEndService.ApplyFrame non-contiguous frame. expected={expectedFrame}, actual={frame}.");

        ResolveTargets();
        ValidateConditionTargets();
        LastAppliedFrame = frame;
        if (IsGameEnded)
            return;

        int currentDay = RequiresDay() ? GetCurrentDayRequired() : 0;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective objective = s_Objectives[i];
            if (objective.Status != LevelObjectiveStatus.Active)
                continue;
            EvaluateObjective(objective, currentDay);
        }
        EvaluateTerminalState();
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(0x47414D45454E4453UL);
        hasher.Add(IsActive);
        if (!IsActive)
            return;
        hasher.Add(IsInitialized);
        if (!IsInitialized)
            return;
        hasher.Add(s_CurrentLevelIdentifier);
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            hasher.Add(item.Definition.IsPrimary);
            hasher.Add(item.Definition.Slot);
            hasher.Add(item.Definition.ObjectiveIdentifier);
            hasher.Add((int)item.Status);
            hasher.Add(item.Definition.Experience);
            hasher.Add(item.Definition.UniqueValues.Length);
            for (int valueIndex = 0; valueIndex < item.Definition.UniqueValues.Length; valueIndex++)
                hasher.Add(item.Definition.UniqueValues[valueIndex].RawValue);
        }
        AddSortedTargets(hasher, s_PlayerTargetBuildingInstanceIds);
        AddSortedTargets(hasher, s_EnemyTargetBuildingInstanceIds);
        hasher.Add(IsGameEnded);
        hasher.Add(IsWin);
        hasher.Add(LastAppliedFrame);
    }

    private static void AddObjectives(LevelObjectiveDefinition[] definitions, bool expectedPrimary)
    {
        if (definitions == null)
            return;
        for (int i = 0; i < definitions.Length; i++)
        {
            LevelObjectiveDefinition definition = definitions[i]
                ?? throw new InvalidOperationException($"Objective definition is null at index {i}.");
            if (definition.IsPrimary != expectedPrimary)
                throw new InvalidOperationException($"Objective slot {definition.Slot} has an invalid primary flag.");
            for (int existingIndex = 0; existingIndex < s_Objectives.Count; existingIndex++)
            {
                RuntimeObjective existing = s_Objectives[existingIndex];
                if (existing.Definition.IsPrimary == definition.IsPrimary && existing.Definition.Slot == definition.Slot)
                    throw new InvalidOperationException($"Duplicate objective slot {definition.Slot}.");
            }
            s_Objectives.Add(new RuntimeObjective { Definition = definition, Status = LevelObjectiveStatus.Active });
        }
    }

    private static void ValidateObjective(LevelObjectiveDefinition definition)
    {
        switch (definition.ObjectiveIdentifier)
        {
            case LevelObjectiveIdentifiers.CaptureSpecificStrongholds:
                RequireValueCount(definition, 0);
                break;
            case LevelObjectiveIdentifiers.CaptureStrongholdCount:
                RequirePositiveIntegerValue(definition);
                break;
            case LevelObjectiveIdentifiers.SurviveDays:
                RequirePositiveIntegerValue(definition);
                break;
            case LevelObjectiveIdentifiers.DefendBase:
            case LevelObjectiveIdentifiers.ProtectStronghold:
                RequireValueCount(definition, 0);
                break;
            case LevelObjectiveIdentifiers.UpgradeCodingCoreLevel3:
                RequireValueCount(definition, 0);
                break;
            default:
                throw new InvalidOperationException($"Objective '{definition.ObjectiveIdentifier}' has no runtime evaluator.");
        }
    }

    private static void EvaluateObjective(RuntimeObjective objective, int currentDay)
    {
        switch (objective.Definition.ObjectiveIdentifier)
        {
            case LevelObjectiveIdentifiers.CaptureSpecificStrongholds:
                if (AreAllEnemyTargetsPlayerOwned())
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIdentifiers.CaptureStrongholdCount:
                if (GetPlayerOwnedEnemyTargetCount() >= GetRequiredIntValue(objective.Definition))
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIdentifiers.SurviveDays:
                if (currentDay > GetRequiredIntValue(objective.Definition))
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIdentifiers.DefendBase:
            case LevelObjectiveIdentifiers.ProtectStronghold:
                if (AreAllPlayerTargetsDisabled())
                    SetStatus(objective, LevelObjectiveStatus.Failed);
                break;
            case LevelObjectiveIdentifiers.UpgradeCodingCoreLevel3:
                break;
            default:
                throw new InvalidOperationException($"Objective '{objective.Definition.ObjectiveIdentifier}' has no runtime evaluator.");
        }
    }

    private static void EvaluateTerminalState()
    {
        RuntimeObjective failedPrimary = null;
        bool allProgressPrimaryCompleted = true;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (!item.Definition.IsPrimary)
                continue;
            if (item.Status == LevelObjectiveStatus.Failed)
            {
                failedPrimary = item;
                break;
            }
            if (!IsConstraint(item.Definition.ObjectiveIdentifier) && item.Status != LevelObjectiveStatus.Completed)
                allProgressPrimaryCompleted = false;
        }

        if (failedPrimary != null)
        {
            FailAllActiveObjectives();
            CompleteFail(failedPrimary.Definition.ObjectiveIdentifier);
            return;
        }
        if (!allProgressPrimaryCompleted)
            return;

        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (item.Status != LevelObjectiveStatus.Active)
                continue;
            if (IsConstraint(item.Definition.ObjectiveIdentifier))
                SetStatus(item, LevelObjectiveStatus.Completed);
            else if (!item.Definition.IsPrimary)
                SetStatus(item, LevelObjectiveStatus.Failed);
        }
        CompleteWin();
    }

    private static void ResolveTargets()
    {
        s_ResolvedTargets.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i] ?? throw new InvalidOperationException($"EntityRegistry contains null at index {i}.");
            if (!entity.TryGetLogicBuilding(out IBuildingLogicContext building))
                continue;
            if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
                throw new InvalidOperationException($"Logic building {building.LogicEntityId.Value} has no stable building instance id.");
            if (!s_PlayerTargetBuildingInstanceIds.Contains(building.BuildingInstanceId)
                && !s_EnemyTargetBuildingInstanceIds.Contains(building.BuildingInstanceId))
                continue;
            bool capturedEnemy = s_EnemyTargetBuildingInstanceIds.Contains(building.BuildingInstanceId)
                                 && building.OwnerFactionId == EntitySideHelper.PlayerFactionId;
            if (!building.IsGameEndConditionBuilding && !capturedEnemy)
                throw new InvalidOperationException($"Registered target building '{building.BuildingInstanceId}' has an invalid condition state.");
            if (!s_ResolvedTargets.TryAdd(building.BuildingInstanceId, building))
                throw new InvalidOperationException($"Duplicate building instance id '{building.BuildingInstanceId}'.");
        }
        ValidateResolvedTargets(s_PlayerTargetBuildingInstanceIds);
        ValidateResolvedTargets(s_EnemyTargetBuildingInstanceIds);
    }

    private static void ValidateResolvedTargets(HashSet<string> targetIds)
    {
        foreach (string targetId in targetIds)
        {
            if (!s_ResolvedTargets.ContainsKey(targetId))
                throw new InvalidOperationException($"Cannot resolve registered target building '{targetId}'.");
        }
    }

    private static void ValidateConditionTargets()
    {
        bool needsEnemy = false;
        bool needsPlayer = false;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            string objectiveIdentifier = s_Objectives[i].Definition.ObjectiveIdentifier;
            needsEnemy |= objectiveIdentifier == LevelObjectiveIdentifiers.CaptureSpecificStrongholds
                          || objectiveIdentifier == LevelObjectiveIdentifiers.CaptureStrongholdCount;
            needsPlayer |= objectiveIdentifier == LevelObjectiveIdentifiers.DefendBase
                           || objectiveIdentifier == LevelObjectiveIdentifiers.ProtectStronghold;
        }
        if (needsEnemy && s_EnemyTargetBuildingInstanceIds.Count == 0)
            throw new InvalidOperationException("Enemy condition-building objective has no registered target.");
        if (needsPlayer && s_PlayerTargetBuildingInstanceIds.Count == 0)
            throw new InvalidOperationException("Player condition-building objective has no registered target.");
    }

    private static bool AreAllEnemyTargetsPlayerOwned()
    {
        foreach (string targetId in s_EnemyTargetBuildingInstanceIds)
        {
            if (s_ResolvedTargets[targetId].OwnerFactionId != EntitySideHelper.PlayerFactionId)
                return false;
        }
        return true;
    }

    private static int GetPlayerOwnedEnemyTargetCount()
    {
        int count = 0;
        foreach (string targetId in s_EnemyTargetBuildingInstanceIds)
        {
            if (s_ResolvedTargets[targetId].OwnerFactionId == EntitySideHelper.PlayerFactionId)
                count++;
        }
        return count;
    }

    private static bool AreAllPlayerTargetsDisabled()
    {
        foreach (string targetId in s_PlayerTargetBuildingInstanceIds)
        {
            if (!s_ResolvedTargets[targetId].IsDisabled)
                return false;
        }
        return true;
    }

    private static bool RequiresDay()
    {
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            if (s_Objectives[i].Definition.ObjectiveIdentifier == LevelObjectiveIdentifiers.SurviveDays)
                return true;
        }
        return false;
    }

    private static int GetCurrentDayRequired()
    {
        if (!InGameDataModel.HasActiveModel)
            throw new InvalidOperationException("Day objective requires an active InGameDataModel.");
        return InGameDataModel.GetValue(IngameValueType.Day);
    }

    private static void CompleteWin()
    {
        StopAllEntitiesForGameEnd();
        IsGameEnded = true;
        IsWin = true;
        GameEnded?.Invoke(LogicGameEndResult.CreateWin());
    }

    private static void CompleteFail(string objectiveIdentifier)
    {
        StopAllEntitiesForGameEnd();
        IsGameEnded = true;
        IsWin = false;
        GameEnded?.Invoke(LogicGameEndResult.CreateFail(objectiveIdentifier));
    }

    private static void StopAllEntitiesForGameEnd()
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i] ?? throw new InvalidOperationException($"EntityRegistry contains null at index {i}.");
            IAtkComp attack = entity.AtkComp ?? throw new InvalidOperationException($"Game end cannot stop entity {entity.LogicEntityId.Value}: attack component is missing.");
            IMoveComp move = entity.MoveComp ?? throw new InvalidOperationException($"Game end cannot stop entity {entity.LogicEntityId.Value}: move component is missing.");
            attack.InterruptAttack(AttackInterruptReason.Forced);
            move.StopMove();
        }
    }

    private static RuntimeObjective FindActivePrimaryObjective(string objectiveIdentifier)
    {
        if (string.IsNullOrWhiteSpace(objectiveIdentifier))
            throw new ArgumentException("Objective identifier is empty.", nameof(objectiveIdentifier));
        RuntimeObjective result = null;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (!item.Definition.IsPrimary
                || !string.Equals(item.Definition.ObjectiveIdentifier, objectiveIdentifier, StringComparison.Ordinal)
                || item.Status != LevelObjectiveStatus.Active)
                continue;
            if (result != null)
                throw new InvalidOperationException($"Multiple active primary objectives use identifier '{objectiveIdentifier}'.");
            result = item;
        }
        return result ?? throw new InvalidOperationException($"Active primary objective '{objectiveIdentifier}' was not found.");
    }

    private static void SetStatus(RuntimeObjective objective, LevelObjectiveStatus status)
    {
        if (objective.Status == status)
            return;
        if (objective.Status != LevelObjectiveStatus.Active)
            throw new InvalidOperationException($"Objective slot {objective.Definition.Slot} is already terminal.");
        objective.Status = status;
        ObjectivesChanged?.Invoke();
    }

    private static void FailAllActiveObjectives()
    {
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            if (s_Objectives[i].Status == LevelObjectiveStatus.Active)
                SetStatus(s_Objectives[i], LevelObjectiveStatus.Failed);
        }
    }

    private static bool IsConstraint(string objectiveIdentifier) =>
        objectiveIdentifier == LevelObjectiveIdentifiers.DefendBase
        || objectiveIdentifier == LevelObjectiveIdentifiers.ProtectStronghold;

    private static int GetRequiredIntValue(LevelObjectiveDefinition definition) => (int)definition.UniqueValues[0];

    private static void RequireValueCount(LevelObjectiveDefinition definition, int expected)
    {
        if (definition.UniqueValues.Length != expected)
            throw new InvalidOperationException($"Objective '{definition.ObjectiveIdentifier}' requires {expected} unique value(s).");
    }

    private static void RequirePositiveIntegerValue(LevelObjectiveDefinition definition)
    {
        RequireValueCount(definition, 1);
        Fix64 value = definition.UniqueValues[0];
        if (value <= Fix64.Zero || value != Fix64.Floor(value))
            throw new InvalidOperationException($"Objective '{definition.ObjectiveIdentifier}' requires one positive integer value.");
    }

    private static void AddArray(LogicStateHasher hasher, Fix64[] values)
    {
        hasher.Add(values.Length);
        for (int i = 0; i < values.Length; i++)
            hasher.Add(values[i].RawValue);
    }

    private static void AddSortedTargets(LogicStateHasher hasher, HashSet<string> targetIds)
    {
        s_SortedTargetIds.Clear();
        foreach (string targetId in targetIds)
            s_SortedTargetIds.Add(targetId);
        s_SortedTargetIds.Sort(StringComparer.Ordinal);
        hasher.Add(s_SortedTargetIds.Count);
        for (int i = 0; i < s_SortedTargetIds.Count; i++)
            hasher.Add(s_SortedTargetIds[i]);
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicGameEndService is not active.");
    }

    private static void EnsureInitialized()
    {
        EnsureActive();
        if (!IsInitialized)
            throw new InvalidOperationException("LogicGameEndService is not initialized.");
    }

    private static void ClearState()
    {
        s_EnemyTargetBuildingInstanceIds.Clear();
        s_PlayerTargetBuildingInstanceIds.Clear();
        s_SortedTargetIds.Clear();
        s_ResolvedTargets.Clear();
        s_Objectives.Clear();
        s_ObjectiveSnapshot.Clear();
        s_TargetMarkerSnapshot.Clear();
        s_CurrentLevelIdentifier = null;
        IsInitialized = false;
        IsGameEnded = false;
        IsWin = false;
        LastAppliedFrame = 0;
        PlayerConditionTargetsChanged?.Invoke();
        TargetMarkersChanged?.Invoke();
    }
}
