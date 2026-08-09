using System;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum VictoryConditionType { OccupySpecificBuildings, SurviveAmountDays, CollectAmountResources, KillSpecificUnits, CompleteTutorial }
public enum FailConditionType { LoseSpecificBuildings, ArriveAmountDays, ConsumeAmountResources, LoseHero }

public class GameEndManager : GameFrameworkComponent
{
    private static GameEndManager s_Current;
    private const string ObjectiveOccupyBeforeDayTextId = "GameEnd_Cond_OccupyBeforeDay";
    private const string ObjectiveOccupyTextId = "GameEnd_Cond_Occupy";
    private const string ObjectiveSurviveToDayTextId = "GameEnd_Cond_SurviveToDay";
    private const string ObjectiveDefendBaseTextId = "GameEnd_Cond_DefendBase";

    private bool m_EndEventSubscribed;
    private readonly Queue<LogicGameEndResult> m_PendingEndPresentation = new Queue<LogicGameEndResult>();

    public bool IsGameEnded => LogicGameEndService.IsGameEnded;
    public bool IsWin => LogicGameEndService.IsWin;

    public static GameEndManager Current => s_Current;

    protected override void Awake()
    {
        base.Awake();
        if (s_Current != null && !ReferenceEquals(s_Current, this))
            throw new InvalidOperationException("GameEndManager active runtime component is already bound.");
        s_Current = this;
    }

    private void OnDestroy()
    {
        UnsubscribeEndEvent();
        m_PendingEndPresentation.Clear();
        if (ReferenceEquals(s_Current, this))
            s_Current = null;
    }

    public void Init(LevelData levelData)
    {
        m_PendingEndPresentation.Clear();
        SubscribeEndEvent();
        LogicGameEndService.Initialize(levelData);
        Log.Info(
            "[GameEndManager] Initialized. level={0}, OccupySpecificBuildings={1}, SurviveAmountDays={2}(>{3}), LoseSpecificBuildings={4}, ArriveAmountDays={5}(>{6})",
            LogicGameEndService.CurrentLevelIdentifier,
            LogicGameEndService.EnableOccupySpecificBuildings,
            LogicGameEndService.EnableSurviveAmountDays,
            LogicGameEndService.SurviveAmountDaysValue,
            LogicGameEndService.EnableLoseSpecificBuildings,
            LogicGameEndService.EnableArriveAmountDays,
            LogicGameEndService.ArriveAmountDaysValue);
    }

    public bool TryGetLevelObjectiveLines(out List<string> objectiveLines)
    {
        objectiveLines = null;
        if (!LogicGameEndService.IsInitialized)
            return false;

        var lines = new List<string>(4);
        int currentDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));

        if (LogicGameEndService.EnableOccupySpecificBuildings)
        {
            if (LogicGameEndService.EnableArriveAmountDays)
            {
                lines.Add(string.Format(
                    LocalizationTextDataModel.GetText(ObjectiveOccupyBeforeDayTextId),
                    LogicGameEndService.ArriveAmountDaysValue,
                    GetRemainingDays(LogicGameEndService.ArriveAmountDaysValue, currentDay)));
            }
            else
            {
                lines.Add(LocalizationTextDataModel.GetText(ObjectiveOccupyTextId));
            }
        }

        if (LogicGameEndService.EnableSurviveAmountDays)
        {
            lines.Add(string.Format(
                LocalizationTextDataModel.GetText(ObjectiveSurviveToDayTextId),
                LogicGameEndService.SurviveAmountDaysValue,
                GetRemainingDays(LogicGameEndService.SurviveAmountDaysValue, currentDay)));
        }

        if (LogicGameEndService.EnableLoseSpecificBuildings)
            lines.Add(LocalizationTextDataModel.GetText(ObjectiveDefendBaseTextId));

        if (lines.Count == 0)
            return false;

        objectiveLines = lines;
        return true;
    }

    public void RegisterInitialConditionBuilding(string buildingInstanceId, int initialOwnerFactionId)
    {
        LogicGameEndService.RegisterInitialConditionBuilding(buildingInstanceId, initialOwnerFactionId);
    }

    public bool TryGetAnyPlayerInitialConditionBuilding(out BuildingEntity building)
    {
        building = null;
        IReadOnlyCollection<string> targetIds = LogicGameEndService.PlayerTargetBuildingInstanceIds;
        if (targetIds.Count == 0)
            return false;

        InGameDataModel inGameData = GF.DataModel?.GetDataModel<InGameDataModel>();
        if (inGameData == null)
            throw new InvalidOperationException("GameEndManager requires an active InGameDataModel.");

        foreach (string instanceId in targetIds)
        {
            foreach (BuildingEntity candidate in inGameData.Buildings)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.BuildingInstanceId))
                    continue;
                if (!string.Equals(candidate.BuildingInstanceId, instanceId, StringComparison.Ordinal))
                    continue;

                building = candidate;
                return true;
            }
        }

        return false;
    }

    private void SubscribeEndEvent()
    {
        if (m_EndEventSubscribed)
            return;
        LogicGameEndService.GameEnded += OnLogicGameEnded;
        m_EndEventSubscribed = true;
    }

    private void UnsubscribeEndEvent()
    {
        if (!m_EndEventSubscribed)
            return;
        LogicGameEndService.GameEnded -= OnLogicGameEnded;
        m_EndEventSubscribed = false;
    }

    private void OnLogicGameEnded(LogicGameEndResult result)
    {
        m_PendingEndPresentation.Enqueue(result);
    }

    public void UpdatePresentation()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("GameEndManager.UpdatePresentation cannot run during a logic frame.");

        while (m_PendingEndPresentation.Count > 0)
            PresentGameEnd(m_PendingEndPresentation.Dequeue());
    }

    private void PresentGameEnd(LogicGameEndResult result)
    {
        if (result.IsWin)
            RecordCareerWin();
        HandleGameEndPresentation(result.IsWin);
        if (result.IsWin)
        {
            GF.Event.Fire(this, GameEndResultEventArgs.CreateWin(result.VictoryCondition));
            Log.Info("[GameEndManager] GameEnd WIN by {0}, logicFrame={1}.", result.VictoryCondition, LogicGameEndService.LastAppliedFrame);
        }
        else
        {
            GF.Event.Fire(this, GameEndResultEventArgs.CreateFail(result.FailCondition));
            Log.Info("[GameEndManager] GameEnd FAIL by {0}, logicFrame={1}.", result.FailCondition, LogicGameEndService.LastAppliedFrame);
        }
    }

    private static void RecordCareerWin()
    {
        if (!CareerRunSettings.HasActiveRun)
        {
            Log.Info("[Career] Win was not recorded because the level was entered without career run settings.");
            return;
        }

        IReadOnlyList<LevelTagTable> activeTags = LevelTagRuntime.GetActiveTags();
        if (CareerRunSettings.IsVariableExperiment && activeTags.Count > 0)
            throw new InvalidOperationException("Variable experiments cannot finish with active level tags.");

        int offsetRate = LevelTagRuntime.GetCurrentSettlementOffsetRateDelta();
        for (int i = 0; i < activeTags.Count; i++)
        {
            LevelTagTable tag = activeTags[i];
            if (!tag.IsPositiveTag)
                offsetRate = checked(offsetRate + Math.Max(0, tag.Score));
        }
        offsetRate = Math.Max(0, offsetRate);

        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        CareerWinRecordResult record = progress.RecordWin(
            CareerRunSettings.CareerLevelIdentifier,
            CareerRunSettings.IsVariableExperiment,
            offsetRate);
        Log.Info(
            "[Career] Win recorded. level={0}, experiment={1}, firstClear={2}, offset={3}, firstOffsetReward={4}, unlockedIndustries={5}, earned={6}, spent={7}, available={8}.",
            record.LevelIdentifier,
            record.IsExperiment,
            record.FirstClear,
            record.OffsetRate,
            record.FirstOffsetReward,
            string.Join(",", record.UnlockedArchetypes),
            progress.GetEarnedPointCount(),
            progress.GetSpentPointCount(),
            progress.GetAvailablePointCount());

        for (int i = 0; i < record.UnlockedArchetypes.Count; i++)
        {
            Archetype unlockedArchetype = record.UnlockedArchetypes[i];
            string industryName = LocalizationTextDataModel.GetText($"Archetype_{unlockedArchetype}");
            UnlockPresentationService.Enqueue(new UnlockPayload(
                UnlockPayloadType.Industry,
                industryName,
                "\u65b0\u7684\u521d\u59cb\u884c\u4e1a\u5df2\u52a0\u5165\u5173\u5361\u9009\u9879\u3002"));
        }
    }

    private static int GetRemainingDays(int targetDay, int currentDay)
    {
        return Math.Max(1, targetDay - currentDay + 1);
    }

    private void HandleGameEndPresentation(bool isWin)
    {
        CloseAllOpenedUIForms();
        DisablePlayerMoveInputOnGameEnd();

        if (AudioManager.Instance != null)
            AudioManager.Instance.Play(isWin ? "levelSuccess" : "levelFail");

        StoryTiming timing = isWin ? StoryTiming.AfterLevelWin : StoryTiming.AfterLevelFail;
        StoryManager.TryPlayTriggeredStory(
            LogicGameEndService.CurrentLevelIdentifier,
            timing,
            () => OpenGameOverUI(isWin));
    }

    private void CloseAllOpenedUIForms()
    {
        UIForm[] loadedForms = GF.UI.GetAllLoadedUIForms();
        for (int i = 0; i < loadedForms.Length; i++)
        {
            UIForm form = loadedForms[i];
            if (form != null)
                GF.UI.CloseUIForm(form.SerialId);
        }
    }

    private void OpenGameOverUI(bool isWin)
    {
        UIParams uiParams = UIParams.Create();
        uiParams.Set<VarBoolean>(GameOverUIForm.P_IsWin, isWin);
        GF.UI.OpenUIForm(UIViews.GameOverUIForm, uiParams);
    }

    private static void DisablePlayerMoveInputOnGameEnd()
    {
        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager != null && inputManager.CurState != InputState.UIForm)
            inputManager.ChangeState(InputState.UIForm);
    }
}
