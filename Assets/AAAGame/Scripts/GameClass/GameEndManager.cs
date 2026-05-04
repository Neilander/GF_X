using UnityGameFramework.Runtime;
using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;

public enum VictoryConditionType { OccupySpecificBuildings, SurviveAmountDays, CollectAmountResources, KillSpecificUnits }
public enum FailConditionType { LoseSpecificBuildings, ArriveAmountDays, ConsumeAmountResources, LoseHero }

public class GameEndManager : GameFrameworkComponent
{
    private const string ObjectiveOccupyBeforeDayTextId = "GameEnd_Cond_OccupyBeforeDay";
    private const string ObjectiveOccupyTextId = "GameEnd_Cond_Occupy";
    private const string ObjectiveSurviveToDayTextId = "GameEnd_Cond_SurviveToDay";
    private const string ObjectiveDefendBaseTextId = "GameEnd_Cond_DefendBase";

    public bool IsGameEnded { get; private set; }
    public bool IsWin { get; private set; }

    private bool m_ConditionInitialized;
    private bool m_EnableOccupySpecificBuildings;
    private bool m_EnableSurviveAmountDays;
    private bool m_EnableLoseSpecificBuildings;
    private bool m_EnableArriveAmountDays;
    private int m_SurviveAmountDaysValue;
    private int m_ArriveAmountDaysValue;
    private string m_CurrentLevelIdentifier;
    private bool m_EventsSubscribed;

    private readonly HashSet<string> m_EnemyTargetBuildingInstanceIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_PlayerTargetBuildingInstanceIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_PlayerDisabledBuildingInstanceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_TargetOwnerFactionByBuildingInstanceId = new(StringComparer.Ordinal);

    private void OnDestroy()
    {
        UnsubscribeEvents();
        ClearRuntimeState();
    }

    public void Init(LevelData levelData)
    {
        SubscribeEvents();
        InitializeLevelConditions(levelData);
    }

    private void InitializeLevelConditions(LevelData levelData)
    {
        if (levelData == null)
        {
            Log.Error("[GameEndManager] InitializeLevelConditions failed: levelData is null.");
            return;
        }

        string levelIdentifier = levelData.Identifier;
        bool isSameLevel = m_ConditionInitialized
            && string.Equals(m_CurrentLevelIdentifier, levelIdentifier, StringComparison.Ordinal);
        if (isSameLevel)
        {
            return;
        }

        if (!string.IsNullOrEmpty(m_CurrentLevelIdentifier)
            && !string.Equals(m_CurrentLevelIdentifier, levelIdentifier, StringComparison.Ordinal))
        {
            ResetTargetsForNewLevel();
        }

        m_EnableOccupySpecificBuildings = ContainsVictoryCondition(levelData.VictoryConditions, VictoryConditionType.OccupySpecificBuildings);
        m_EnableSurviveAmountDays = ContainsVictoryCondition(levelData.VictoryConditions, VictoryConditionType.SurviveAmountDays);
        m_EnableLoseSpecificBuildings = ContainsFailCondition(levelData.LoseConditions, FailConditionType.LoseSpecificBuildings);
        m_EnableArriveAmountDays = ContainsFailCondition(levelData.LoseConditions, FailConditionType.ArriveAmountDays);
        m_SurviveAmountDaysValue = levelData.VictoryValue;
        m_ArriveAmountDaysValue = levelData.LoseValue;
        m_ConditionInitialized = true;
        m_CurrentLevelIdentifier = levelIdentifier;

        Log.Info("[GameEndManager] Initialized. OccupySpecificBuildings={0}, SurviveAmountDays={1}(>{2}), LoseSpecificBuildings={3}, ArriveAmountDays={4}(>{5})",
            m_EnableOccupySpecificBuildings,
            m_EnableSurviveAmountDays,
            m_SurviveAmountDaysValue,
            m_EnableLoseSpecificBuildings,
            m_EnableArriveAmountDays,
            m_ArriveAmountDaysValue);

        EvaluateConditions();
    }

    public bool TryGetLevelObjectiveLines(out List<string> objectiveLines)
    {
        objectiveLines = null;

        if (!m_ConditionInitialized)
        {
            return false;
        }

        var lines = new List<string>(4);
        int currentDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));

        if (m_EnableOccupySpecificBuildings)
        {
            if (m_EnableArriveAmountDays)
            {
                lines.Add(string.Format(LocalizationTextDataModel.GetText(ObjectiveOccupyBeforeDayTextId), m_ArriveAmountDaysValue, GetRemainingDays(m_ArriveAmountDaysValue, currentDay)));
            }
            else
            {
                lines.Add(LocalizationTextDataModel.GetText(ObjectiveOccupyTextId));
            }
        }

        if (m_EnableSurviveAmountDays)
        {
            lines.Add(string.Format(LocalizationTextDataModel.GetText(ObjectiveSurviveToDayTextId), m_SurviveAmountDaysValue, GetRemainingDays(m_SurviveAmountDaysValue, currentDay)));
        }

        if (m_EnableLoseSpecificBuildings)
        {
            lines.Add(LocalizationTextDataModel.GetText(ObjectiveDefendBaseTextId));
        }

        if (lines.Count == 0)
        {
            return false;
        }

        objectiveLines = lines;
        return true;
    }

    public void RegisterInitialConditionBuilding(string buildingInstanceId, int initialOwnerFactionId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
        {
            Log.Error("[GameEndManager] RegisterInitialConditionBuilding failed: invalid buildingInstanceId.");
            return;
        }

        if (!m_ConditionInitialized)
        {
            Log.Error("[GameEndManager] RegisterInitialConditionBuilding failed: call InitializeLevelConditions first.");
            return;
        }

        m_TargetOwnerFactionByBuildingInstanceId[buildingInstanceId] = initialOwnerFactionId;

        if (initialOwnerFactionId == EntitySideHelper.PlayerFactionId)
        {
            m_PlayerTargetBuildingInstanceIds.Add(buildingInstanceId);
            m_PlayerDisabledBuildingInstanceIds.Remove(buildingInstanceId);
        }
        else
        {
            m_EnemyTargetBuildingInstanceIds.Add(buildingInstanceId);
        }

        EvaluateConditions();
    }

    private void SubscribeEvents()
    {
        if (m_EventsSubscribed)
        {
            return;
        }

        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        GF.Event.Subscribe(BuildingDisabledStateChangedEventArgs.EventId, OnBuildingDisabledStateChanged);
        GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        m_EventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!m_EventsSubscribed)
        {
            return;
        }

        try
        {
            GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
            GF.Event.Unsubscribe(BuildingDisabledStateChangedEventArgs.EventId, OnBuildingDisabledStateChanged);
            GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
        }
        catch (GameFrameworkException)
        {
            // 生命周期销毁顺序可能先清空 EventPool，这里忽略退订异常。
        }
        finally
        {
            m_EventsSubscribed = false;
        }
    }

    private void ClearRuntimeState()
    {
        m_ConditionInitialized = false;
        m_EnableOccupySpecificBuildings = false;
        m_EnableSurviveAmountDays = false;
        m_EnableLoseSpecificBuildings = false;
        m_EnableArriveAmountDays = false;
        m_SurviveAmountDaysValue = 0;
        m_ArriveAmountDaysValue = 0;
        m_CurrentLevelIdentifier = null;
        IsGameEnded = false;
        IsWin = false;

        m_EnemyTargetBuildingInstanceIds.Clear();
        m_PlayerTargetBuildingInstanceIds.Clear();
        m_PlayerDisabledBuildingInstanceIds.Clear();
        m_TargetOwnerFactionByBuildingInstanceId.Clear();
    }

    private void OnIngameValueChanged(object sender, GameEventArgs e)
    {
        if (IsGameEnded)
        {
            return;
        }

        var args = e as IngameValueChangedEventArgs;
        if (args == null || args.DataType != IngameValueType.Day)
        {
            return;
        }

        if (!m_EnableSurviveAmountDays && !m_EnableArriveAmountDays)
        {
            return;
        }

        Log.Info("[GameEndManager] Day changed from {0} to {1}.", args.OldValue, args.Value);
        EvaluateConditions();
    }

    private void ResetTargetsForNewLevel()
    {
        IsGameEnded = false;
        IsWin = false;

        m_EnemyTargetBuildingInstanceIds.Clear();
        m_PlayerTargetBuildingInstanceIds.Clear();
        m_PlayerDisabledBuildingInstanceIds.Clear();
        m_TargetOwnerFactionByBuildingInstanceId.Clear();
    }

    private static bool ContainsVictoryCondition(IReadOnlyList<VictoryConditionType> conditions, VictoryConditionType expected)
    {
        if (conditions == null)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i] == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFailCondition(IReadOnlyList<FailConditionType> conditions, FailConditionType expected)
    {
        if (conditions == null)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i] == expected)
            {
                return true;
            }
        }

        return false;
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        if (IsGameEnded)
        {
            return;
        }

        var args = e as EntityFactionChangedEventArgs;
        if (args == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(args.BuildingInstanceId))
        {
            // Soldier entities or other entities might not have BuildingInstanceId
            return;
        }

        if (!m_TargetOwnerFactionByBuildingInstanceId.ContainsKey(args.BuildingInstanceId))
        {
            return;
        }

        m_TargetOwnerFactionByBuildingInstanceId[args.BuildingInstanceId] = args.NewFactionId;
        EvaluateConditions();
    }

    private void OnBuildingDisabledStateChanged(object sender, GameEventArgs e)
    {
        if (IsGameEnded)
        {
            return;
        }

        var args = e as BuildingDisabledStateChangedEventArgs;
        if (args == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(args.BuildingInstanceId))
        {
            Log.Error("[GameEndManager] BuildingDisabledStateChangedEventArgs missing BuildingInstanceId.");
            return;
        }

        if (!m_PlayerTargetBuildingInstanceIds.Contains(args.BuildingInstanceId))
        {
            return;
        }

        if (args.IsDisabled)
        {
            m_PlayerDisabledBuildingInstanceIds.Add(args.BuildingInstanceId);
        }
        else
        {
            m_PlayerDisabledBuildingInstanceIds.Remove(args.BuildingInstanceId);
        }

        EvaluateConditions();
    }

    private void EvaluateConditions()
    {
        if (IsGameEnded || !m_ConditionInitialized)
        {
            return;
        }

        if (m_EnableLoseSpecificBuildings && EvaluateLoseSpecificBuildings())
        {
            TriggerFail(FailConditionType.LoseSpecificBuildings);
            return;
        }

        if (m_EnableArriveAmountDays && EvaluateArriveAmountDays())
        {
            TriggerFail(FailConditionType.ArriveAmountDays);
            return;
        }

        if (m_EnableOccupySpecificBuildings && EvaluateOccupySpecificBuildings())
        {
            TriggerWin(VictoryConditionType.OccupySpecificBuildings);
            return;
        }

        if (m_EnableSurviveAmountDays && EvaluateSurviveAmountDays())
        {
            TriggerWin(VictoryConditionType.SurviveAmountDays);
        }
    }

    private bool EvaluateOccupySpecificBuildings()
    {
        if (m_EnemyTargetBuildingInstanceIds.Count == 0)
        {
            return false;
        }

        foreach (var buildingInstanceId in m_EnemyTargetBuildingInstanceIds)
        {
            if (!m_TargetOwnerFactionByBuildingInstanceId.TryGetValue(buildingInstanceId, out var ownerFactionId))
            {
                return false;
            }

            if (ownerFactionId != EntitySideHelper.PlayerFactionId)
            {
                return false;
            }
        }

        return true;
    }

    private bool EvaluateSurviveAmountDays()
    {
        int currentDay = InGameDataModel.GetValue(IngameValueType.Day);
        return currentDay > m_SurviveAmountDaysValue;
    }

    private static int GetRemainingDays(int targetDay, int currentDay)
    {
        return Math.Max(1, targetDay - currentDay + 1);
    }

    private bool EvaluateArriveAmountDays()
    {
        int currentDay = InGameDataModel.GetValue(IngameValueType.Day);
        return currentDay > m_ArriveAmountDaysValue;
    }

    private bool EvaluateLoseSpecificBuildings()
    {
        if (m_PlayerTargetBuildingInstanceIds.Count == 0)
        {
            return false;
        }

        foreach (var buildingInstanceId in m_PlayerTargetBuildingInstanceIds)
        {
            if (!m_PlayerDisabledBuildingInstanceIds.Contains(buildingInstanceId))
            {
                return false;
            }
        }

        return true;
    }

    private void TriggerWin(VictoryConditionType condition)
    {
        if (IsGameEnded)
        {
            return;
        }

        IsGameEnded = true;
        IsWin = true;
        HandleGameEndPresentation(isWin: true);
        GF.Event.Fire(this, GameEndResultEventArgs.CreateWin(condition));
        Log.Info("[GameEndManager] GameEnd WIN by {0}.", condition);
    }

    private void TriggerFail(FailConditionType condition)
    {
        if (IsGameEnded)
        {
            return;
        }

        IsGameEnded = true;
        IsWin = false;
        HandleGameEndPresentation(isWin: false);
        GF.Event.Fire(this, GameEndResultEventArgs.CreateFail(condition));
        Log.Info("[GameEndManager] GameEnd FAIL by {0}.", condition);
    }

    private void HandleGameEndPresentation(bool isWin)
    {
        CloseAllOpenedUIForms();
        OpenGameOverUI(isWin);
        DisablePlayerMoveInputOnGameEnd();
    }

    private void CloseAllOpenedUIForms()
    {
        var loadedForms = GF.UI.GetAllLoadedUIForms();
        for (int i = 0; i < loadedForms.Length; i++)
        {
            var form = loadedForms[i];
            if (form == null)
            {
                continue;
            }

            GF.UI.CloseUIForm(form.SerialId);
        }
    }

    private void OpenGameOverUI(bool isWin)
    {
        var uiParams = UIParams.Create();
        uiParams.Set<VarBoolean>(GameOverUIForm.P_IsWin, isWin);
        GF.UI.OpenUIForm(UIViews.GameOverUIForm, uiParams);
    }

    private static void DisablePlayerMoveInputOnGameEnd()
    {
        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager == null)
        {
            return;
        }

        if (inputManager.CurState != InputState.UIForm)
        {
            inputManager.ChangeState(InputState.UIForm);
        }
    }
}