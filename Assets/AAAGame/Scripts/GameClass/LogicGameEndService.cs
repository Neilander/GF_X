using System;
using System.Collections.Generic;
using GameFramework;

public readonly struct LogicGameEndResult
{
    private LogicGameEndResult(
        bool isWin,
        VictoryConditionType victoryCondition,
        FailConditionType failCondition)
    {
        IsWin = isWin;
        VictoryCondition = victoryCondition;
        FailCondition = failCondition;
    }

    public bool IsWin { get; }
    public VictoryConditionType VictoryCondition { get; }
    public FailConditionType FailCondition { get; }

    public static LogicGameEndResult CreateWin(VictoryConditionType condition) =>
        new LogicGameEndResult(true, condition, default);

    public static LogicGameEndResult CreateFail(FailConditionType condition) =>
        new LogicGameEndResult(false, default, condition);
}

public static class LogicGameEndService
{
    private static readonly HashSet<string> s_EnemyTargetBuildingInstanceIds =
        new HashSet<string>(StringComparer.Ordinal);
    private static readonly HashSet<string> s_PlayerTargetBuildingInstanceIds =
        new HashSet<string>(StringComparer.Ordinal);
    private static readonly List<string> s_SortedTargetIds = new List<string>();
    private static readonly Dictionary<string, IBuildingLogicContext> s_ResolvedTargets =
        new Dictionary<string, IBuildingLogicContext>(StringComparer.Ordinal);

    private static bool s_EnableOccupySpecificBuildings;
    private static bool s_EnableSurviveAmountDays;
    private static bool s_EnableLoseSpecificBuildings;
    private static bool s_EnableArriveAmountDays;
    private static int s_SurviveAmountDaysValue;
    private static int s_ArriveAmountDaysValue;
    private static string s_CurrentLevelIdentifier;
    private static VictoryConditionType s_VictoryCondition;
    private static FailConditionType s_FailCondition;
    private const string TutorialLevelIdentifier = "Lv_1";

    public static event Action<LogicGameEndResult> GameEnded;

    public static bool IsActive { get; private set; }
    public static bool IsInitialized { get; private set; }
    public static bool IsGameEnded { get; private set; }
    public static bool IsWin { get; private set; }
    public static ulong LastAppliedFrame { get; private set; }
    public static bool EnableOccupySpecificBuildings => s_EnableOccupySpecificBuildings;
    public static bool EnableSurviveAmountDays => s_EnableSurviveAmountDays;
    public static bool EnableLoseSpecificBuildings => s_EnableLoseSpecificBuildings;
    public static bool EnableArriveAmountDays => s_EnableArriveAmountDays;
    public static int SurviveAmountDaysValue => s_SurviveAmountDaysValue;
    public static int ArriveAmountDaysValue => s_ArriveAmountDaysValue;
    public static string CurrentLevelIdentifier => s_CurrentLevelIdentifier;
    public static IReadOnlyCollection<string> PlayerTargetBuildingInstanceIds =>
        s_PlayerTargetBuildingInstanceIds;

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
        {
            throw new InvalidOperationException(
                $"LogicGameEndService.Initialize requires frame zero. current={LogicTimeControlService.CurrentFrame}.");
        }
        if (string.IsNullOrWhiteSpace(levelData.Identifier))
            throw new InvalidOperationException("LogicGameEndService.Initialize requires a stable level identifier.");

        ClearState();
        s_EnableOccupySpecificBuildings = !string.Equals(
                                               levelData.Identifier,
                                               TutorialLevelIdentifier,
                                               StringComparison.Ordinal)
                                           && ContainsVictoryCondition(
                                               levelData.VictoryConditions,
                                               VictoryConditionType.OccupySpecificBuildings);
        s_EnableSurviveAmountDays = ContainsVictoryCondition(
            levelData.VictoryConditions,
            VictoryConditionType.SurviveAmountDays);
        s_EnableLoseSpecificBuildings = ContainsFailCondition(
            levelData.LoseConditions,
            FailConditionType.LoseSpecificBuildings);
        s_EnableArriveAmountDays = ContainsFailCondition(
            levelData.LoseConditions,
            FailConditionType.ArriveAmountDays);
        s_SurviveAmountDaysValue = levelData.VictoryValue;
        s_ArriveAmountDaysValue = levelData.LoseValue;
        s_CurrentLevelIdentifier = levelData.Identifier;
        IsInitialized = true;
    }

    public static void RegisterInitialConditionBuilding(string buildingInstanceId, int initialOwnerFactionId)
    {
        EnsureInitialized();
        if (LogicTimeControlService.CurrentFrame != 0)
        {
            throw new InvalidOperationException(
                $"LogicGameEndService.RegisterInitialConditionBuilding requires frame zero. current={LogicTimeControlService.CurrentFrame}.");
        }
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is required.", nameof(buildingInstanceId));
        if (initialOwnerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(initialOwnerFactionId));
        if (s_PlayerTargetBuildingInstanceIds.Contains(buildingInstanceId)
            || s_EnemyTargetBuildingInstanceIds.Contains(buildingInstanceId))
        {
            throw new InvalidOperationException(
                $"LogicGameEndService encountered duplicate target building instance id '{buildingInstanceId}'.");
        }

        if (initialOwnerFactionId == EntitySideHelper.PlayerFactionId)
            s_PlayerTargetBuildingInstanceIds.Add(buildingInstanceId);
        else
            s_EnemyTargetBuildingInstanceIds.Add(buildingInstanceId);
    }

    public static bool TryGetNearestPlayerInitialConditionBuilding(
        FixVector2 origin,
        out IBuildingLogicContext building)
    {
        EnsureInitialized();
        return LogicBuildingQueryService.TryGetNearestByInstanceIds(
            s_PlayerTargetBuildingInstanceIds,
            origin,
            out building);
    }

    public static void PromoteCapturedConditionBuildingToPlayerTarget(string buildingInstanceId)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is required.", nameof(buildingInstanceId));
        if (!s_EnemyTargetBuildingInstanceIds.Contains(buildingInstanceId))
        {
            throw new InvalidOperationException(
                $"Captured condition building '{buildingInstanceId}' was not registered as an enemy target.");
        }
        if (!s_PlayerTargetBuildingInstanceIds.Add(buildingInstanceId))
            throw new InvalidOperationException($"Captured condition building '{buildingInstanceId}' is already a player target.");
    }

    public static void CompleteScriptedWin(VictoryConditionType condition)
    {
        EnsureInitialized();
        if (!string.Equals(s_CurrentLevelIdentifier, TutorialLevelIdentifier, StringComparison.Ordinal))
            throw new InvalidOperationException("Scripted tutorial victory is only valid in Level_1.");
        if (condition != VictoryConditionType.CompleteTutorial)
            throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unexpected scripted victory condition.");
        if (IsGameEnded)
            throw new InvalidOperationException("Cannot complete a scripted victory after the game has ended.");
        CompleteWin(condition);
    }

    public static void ApplyFrame(ulong frame)
    {
        EnsureInitialized();
        if (frame == 0 || frame != LogicTimeControlService.CurrentFrame)
        {
            throw new InvalidOperationException(
                $"LogicGameEndService.ApplyFrame failed: frame mismatch. requested={frame}, current={LogicTimeControlService.CurrentFrame}.");
        }
        ulong expectedFrame = checked(LastAppliedFrame + 1);
        if (frame != expectedFrame)
        {
            throw new InvalidOperationException(
                $"LogicGameEndService.ApplyFrame failed: non-contiguous frame. expected={expectedFrame}, actual={frame}.");
        }

        ResolveTargets();
        ValidateConditionTargets();
        LastAppliedFrame = frame;
        if (IsGameEnded)
            return;

        if (s_EnableLoseSpecificBuildings && AreAllPlayerTargetsDisabled())
        {
            CompleteFail(FailConditionType.LoseSpecificBuildings);
            return;
        }

        int currentDay = GetCurrentDayRequired();
        if (s_EnableArriveAmountDays && currentDay > s_ArriveAmountDaysValue)
        {
            CompleteFail(FailConditionType.ArriveAmountDays);
            return;
        }

        if (s_EnableOccupySpecificBuildings && AreAllEnemyTargetsPlayerOwned())
        {
            CompleteWin(VictoryConditionType.OccupySpecificBuildings);
            return;
        }

        if (s_EnableSurviveAmountDays && currentDay > s_SurviveAmountDaysValue)
            CompleteWin(VictoryConditionType.SurviveAmountDays);
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
        hasher.Add(s_EnableOccupySpecificBuildings);
        hasher.Add(s_EnableSurviveAmountDays);
        hasher.Add(s_EnableLoseSpecificBuildings);
        hasher.Add(s_EnableArriveAmountDays);
        hasher.Add(s_SurviveAmountDaysValue);
        hasher.Add(s_ArriveAmountDaysValue);
        AddSortedTargets(hasher, s_PlayerTargetBuildingInstanceIds);
        AddSortedTargets(hasher, s_EnemyTargetBuildingInstanceIds);
        hasher.Add(IsGameEnded);
        hasher.Add(IsWin);
        if (IsGameEnded)
            hasher.Add(IsWin ? (int)s_VictoryCondition : (int)s_FailCondition);
        hasher.Add(LastAppliedFrame);
    }

    private static void ResolveTargets()
    {
        s_ResolvedTargets.Clear();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                ?? throw new InvalidOperationException($"EntityRegistry contains null at index {i}.");
            if (!entity.TryGetLogicBuilding(out IBuildingLogicContext building))
                continue;
            if (string.IsNullOrWhiteSpace(building.BuildingInstanceId))
            {
                throw new InvalidOperationException(
                    $"Logic building {building.LogicEntityId.Value} has no stable building instance id.");
            }
            if (!s_PlayerTargetBuildingInstanceIds.Contains(building.BuildingInstanceId)
                && !s_EnemyTargetBuildingInstanceIds.Contains(building.BuildingInstanceId))
            {
                continue;
            }
            if (!building.IsGameEndConditionBuilding)
            {
                throw new InvalidOperationException(
                    $"Registered target building '{building.BuildingInstanceId}' is not a logic game-end-condition building.");
            }
            if (!s_ResolvedTargets.TryAdd(building.BuildingInstanceId, building))
            {
                throw new InvalidOperationException(
                    $"LogicGameEndService found duplicate building instance id '{building.BuildingInstanceId}'.");
            }
        }

        ValidateResolvedTargets(s_PlayerTargetBuildingInstanceIds);
        ValidateResolvedTargets(s_EnemyTargetBuildingInstanceIds);
    }

    private static void ValidateResolvedTargets(HashSet<string> targetIds)
    {
        foreach (string targetId in targetIds)
        {
            if (!s_ResolvedTargets.ContainsKey(targetId))
            {
                throw new InvalidOperationException(
                    $"LogicGameEndService cannot resolve registered target building '{targetId}'.");
            }
        }
    }

    private static void ValidateConditionTargets()
    {
        if (s_EnableOccupySpecificBuildings && s_EnemyTargetBuildingInstanceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "OccupySpecificBuildings is enabled without an initial enemy target building.");
        }
        if (s_EnableLoseSpecificBuildings && s_PlayerTargetBuildingInstanceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "LoseSpecificBuildings is enabled without an initial player target building.");
        }
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

    private static bool AreAllPlayerTargetsDisabled()
    {
        foreach (string targetId in s_PlayerTargetBuildingInstanceIds)
        {
            if (!s_ResolvedTargets[targetId].IsDisabled)
                return false;
        }

        return true;
    }

    private static int GetCurrentDayRequired()
    {
        if (!InGameDataModel.HasActiveModel)
            throw new InvalidOperationException("LogicGameEndService requires an active InGameDataModel.");
        return InGameDataModel.GetValue(IngameValueType.Day);
    }

    private static void CompleteWin(VictoryConditionType condition)
    {
        StopAllEntitiesForGameEnd();
        IsGameEnded = true;
        IsWin = true;
        s_VictoryCondition = condition;
        GameEnded?.Invoke(LogicGameEndResult.CreateWin(condition));
    }

    private static void CompleteFail(FailConditionType condition)
    {
        StopAllEntitiesForGameEnd();
        IsGameEnded = true;
        IsWin = false;
        s_FailCondition = condition;
        GameEnded?.Invoke(LogicGameEndResult.CreateFail(condition));
    }

    private static void StopAllEntitiesForGameEnd()
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                ?? throw new InvalidOperationException($"EntityRegistry contains null at index {i}.");
            IAtkComp attack = entity.AtkComp
                ?? throw new InvalidOperationException(
                    $"Game end cannot stop entity {entity.LogicEntityId.Value}: attack component is missing.");
            IMoveComp move = entity.MoveComp
                ?? throw new InvalidOperationException(
                    $"Game end cannot stop entity {entity.LogicEntityId.Value}: move component is missing.");

            attack.InterruptAttack(AttackInterruptReason.Forced);
            move.StopMove();
        }
    }

    private static bool ContainsVictoryCondition(
        IReadOnlyList<VictoryConditionType> conditions,
        VictoryConditionType expected)
    {
        if (conditions == null)
            return false;
        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i] == expected)
                return true;
        }

        return false;
    }

    private static bool ContainsFailCondition(
        IReadOnlyList<FailConditionType> conditions,
        FailConditionType expected)
    {
        if (conditions == null)
            return false;
        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i] == expected)
                return true;
        }

        return false;
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
            throw new InvalidOperationException("LogicGameEndService operation failed: service is not active.");
    }

    private static void EnsureInitialized()
    {
        EnsureActive();
        if (!IsInitialized)
            throw new InvalidOperationException("LogicGameEndService operation failed: service is not initialized.");
    }

    private static void ClearState()
    {
        IsInitialized = false;
        IsGameEnded = false;
        IsWin = false;
        LastAppliedFrame = 0;
        s_EnableOccupySpecificBuildings = false;
        s_EnableSurviveAmountDays = false;
        s_EnableLoseSpecificBuildings = false;
        s_EnableArriveAmountDays = false;
        s_SurviveAmountDaysValue = 0;
        s_ArriveAmountDaysValue = 0;
        s_CurrentLevelIdentifier = null;
        s_VictoryCondition = default;
        s_FailCondition = default;
        s_PlayerTargetBuildingInstanceIds.Clear();
        s_EnemyTargetBuildingInstanceIds.Clear();
        s_SortedTargetIds.Clear();
        s_ResolvedTargets.Clear();
    }
}
