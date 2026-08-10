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
    private LogicGameEndResult(bool isWin, int failedObjectiveDefinitionId)
    {
        IsWin = isWin;
        FailedObjectiveDefinitionId = failedObjectiveDefinitionId;
    }

    public bool IsWin { get; }
    public int FailedObjectiveDefinitionId { get; }

    public static LogicGameEndResult CreateWin() => new LogicGameEndResult(true, 0);
    public static LogicGameEndResult CreateFail(int definitionId) =>
        new LogicGameEndResult(false, definitionId);
}

public static class LogicGameEndService
{
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
            if (s_Objectives[i].Definition.IsPrimary && !IsConstraint(s_Objectives[i].Definition.DefinitionId))
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
            s_PlayerTargetBuildingInstanceIds.Add(buildingInstanceId);
        else
            s_EnemyTargetBuildingInstanceIds.Add(buildingInstanceId);
    }

    public static bool TryGetNearestPlayerInitialConditionBuilding(FixVector2 origin, out IBuildingLogicContext building)
    {
        EnsureInitialized();
        return LogicBuildingQueryService.TryGetNearestByInstanceIds(s_PlayerTargetBuildingInstanceIds, origin, out building);
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
    }

    public static void CompleteScriptedObjective(int definitionId)
    {
        EnsureInitialized();
        if (IsGameEnded)
            throw new InvalidOperationException("Cannot complete an objective after the game has ended.");
        RuntimeObjective objective = FindActivePrimaryObjective(definitionId);
        if (objective.Definition.DefinitionId != LevelObjectiveIds.UpgradeCodingCoreLevel3)
            throw new InvalidOperationException($"Objective {definitionId} is not scripted.");
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
            hasher.Add(item.Definition.DefinitionId);
            hasher.Add((int)item.Status);
            hasher.Add(item.Definition.Experience);
            AddArray(hasher, item.Definition.TargetIds);
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
        switch (definition.DefinitionId)
        {
            case LevelObjectiveIds.CaptureSpecificStrongholds:
                RequireTarget(definition, LevelObjectiveTargetIds.InitialEnemyConditionBuildings);
                RequireValueCount(definition, 0);
                break;
            case LevelObjectiveIds.CaptureStrongholdCount:
                RequireTarget(definition, LevelObjectiveTargetIds.InitialEnemyConditionBuildings);
                RequirePositiveIntegerValue(definition);
                break;
            case LevelObjectiveIds.SurviveDays:
                RequireTarget(definition, LevelObjectiveTargetIds.Day);
                RequirePositiveIntegerValue(definition);
                break;
            case LevelObjectiveIds.DefendBase:
            case LevelObjectiveIds.ProtectStronghold:
                RequireTarget(definition, LevelObjectiveTargetIds.InitialPlayerConditionBuildings);
                RequireValueCount(definition, 0);
                break;
            case LevelObjectiveIds.UpgradeCodingCoreLevel3:
                RequireTarget(definition, LevelObjectiveTargetIds.Tutorial);
                RequireValueCount(definition, 0);
                break;
            default:
                throw new InvalidOperationException($"Objective definition {definition.DefinitionId} has no runtime evaluator.");
        }
    }

    private static void EvaluateObjective(RuntimeObjective objective, int currentDay)
    {
        switch (objective.Definition.DefinitionId)
        {
            case LevelObjectiveIds.CaptureSpecificStrongholds:
                if (AreAllEnemyTargetsPlayerOwned())
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIds.CaptureStrongholdCount:
                if (GetPlayerOwnedEnemyTargetCount() >= GetRequiredIntValue(objective.Definition))
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIds.SurviveDays:
                if (currentDay > GetRequiredIntValue(objective.Definition))
                    SetStatus(objective, LevelObjectiveStatus.Completed);
                break;
            case LevelObjectiveIds.DefendBase:
            case LevelObjectiveIds.ProtectStronghold:
                if (AreAllPlayerTargetsDisabled())
                    SetStatus(objective, LevelObjectiveStatus.Failed);
                break;
            case LevelObjectiveIds.UpgradeCodingCoreLevel3:
                break;
            default:
                throw new InvalidOperationException($"Objective definition {objective.Definition.DefinitionId} has no runtime evaluator.");
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
            if (!IsConstraint(item.Definition.DefinitionId) && item.Status != LevelObjectiveStatus.Completed)
                allProgressPrimaryCompleted = false;
        }

        if (failedPrimary != null)
        {
            FailAllActiveObjectives();
            CompleteFail(failedPrimary.Definition.DefinitionId);
            return;
        }
        if (!allProgressPrimaryCompleted)
            return;

        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (item.Status != LevelObjectiveStatus.Active)
                continue;
            if (IsConstraint(item.Definition.DefinitionId))
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
            int definitionId = s_Objectives[i].Definition.DefinitionId;
            needsEnemy |= definitionId == LevelObjectiveIds.CaptureSpecificStrongholds
                          || definitionId == LevelObjectiveIds.CaptureStrongholdCount;
            needsPlayer |= definitionId == LevelObjectiveIds.DefendBase
                           || definitionId == LevelObjectiveIds.ProtectStronghold;
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
            if (s_Objectives[i].Definition.DefinitionId == LevelObjectiveIds.SurviveDays)
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

    private static void CompleteFail(int definitionId)
    {
        StopAllEntitiesForGameEnd();
        IsGameEnded = true;
        IsWin = false;
        GameEnded?.Invoke(LogicGameEndResult.CreateFail(definitionId));
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

    private static RuntimeObjective FindActivePrimaryObjective(int definitionId)
    {
        RuntimeObjective result = null;
        for (int i = 0; i < s_Objectives.Count; i++)
        {
            RuntimeObjective item = s_Objectives[i];
            if (!item.Definition.IsPrimary || item.Definition.DefinitionId != definitionId || item.Status != LevelObjectiveStatus.Active)
                continue;
            if (result != null)
                throw new InvalidOperationException($"Multiple active primary objectives use definition {definitionId}.");
            result = item;
        }
        return result ?? throw new InvalidOperationException($"Active primary objective {definitionId} was not found.");
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

    private static bool IsConstraint(int definitionId) =>
        definitionId == LevelObjectiveIds.DefendBase || definitionId == LevelObjectiveIds.ProtectStronghold;

    private static int GetRequiredIntValue(LevelObjectiveDefinition definition) => (int)definition.UniqueValues[0];

    private static void RequireTarget(LevelObjectiveDefinition definition, string expected)
    {
        if (definition.TargetIds.Length != 1 || !string.Equals(definition.TargetIds[0], expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Objective {definition.DefinitionId} requires target id '{expected}'.");
    }

    private static void RequireValueCount(LevelObjectiveDefinition definition, int expected)
    {
        if (definition.UniqueValues.Length != expected)
            throw new InvalidOperationException($"Objective {definition.DefinitionId} requires {expected} unique value(s).");
    }

    private static void RequirePositiveIntegerValue(LevelObjectiveDefinition definition)
    {
        RequireValueCount(definition, 1);
        Fix64 value = definition.UniqueValues[0];
        if (value <= Fix64.Zero || value != Fix64.Floor(value))
            throw new InvalidOperationException($"Objective {definition.DefinitionId} requires one positive integer value.");
    }

    private static void AddArray(LogicStateHasher hasher, string[] values)
    {
        hasher.Add(values.Length);
        for (int i = 0; i < values.Length; i++)
            hasher.Add(values[i]);
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
        s_CurrentLevelIdentifier = null;
        IsInitialized = false;
        IsGameEnded = false;
        IsWin = false;
        LastAppliedFrame = 0;
    }
}
