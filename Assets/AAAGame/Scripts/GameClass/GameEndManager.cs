using UnityGameFramework.Runtime;
using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;

public enum VictoryConditionType { OccupySpecificBuildings, SurviveAmountDays, CollectAmountResources, KillSpecificUnits }
public enum FailConditionType { LoseSpecificBuildings, ArriveAmountDays, ConsumeAmountResources, LoseHero }

public class GameEndManager : GameFrameworkComponent
{
    public bool IsGameEnded { get; private set; }
    public bool IsWin { get; private set; }

    private bool m_ConditionInitialized;
    private bool m_EnableOccupySpecificBuildings;
    private bool m_EnableLoseSpecificBuildings;
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
        m_EnableLoseSpecificBuildings = ContainsFailCondition(levelData.LoseConditions, FailConditionType.LoseSpecificBuildings);
        m_ConditionInitialized = true;
        m_CurrentLevelIdentifier = levelIdentifier;

        Log.Info("[GameEndManager] Initialized. OccupySpecificBuildings={0}, LoseSpecificBuildings={1}",
            m_EnableOccupySpecificBuildings,
            m_EnableLoseSpecificBuildings);

        EvaluateConditions();
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
        m_EnableLoseSpecificBuildings = false;
        m_CurrentLevelIdentifier = null;
        IsGameEnded = false;
        IsWin = false;

        m_EnemyTargetBuildingInstanceIds.Clear();
        m_PlayerTargetBuildingInstanceIds.Clear();
        m_PlayerDisabledBuildingInstanceIds.Clear();
        m_TargetOwnerFactionByBuildingInstanceId.Clear();
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
            Log.Error("[GameEndManager] EntityFactionChangedEventArgs missing BuildingInstanceId.");
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

        if (m_EnableOccupySpecificBuildings && EvaluateOccupySpecificBuildings())
        {
            TriggerWin(VictoryConditionType.OccupySpecificBuildings);
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