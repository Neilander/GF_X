using System;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public class GameEndManager : GameFrameworkComponent
{
    private static GameEndManager s_Current;

    private bool m_EndEventSubscribed;
    private bool m_ObjectivesPresentationDirty;
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
        m_ObjectivesPresentationDirty = false;
        if (ReferenceEquals(s_Current, this))
            s_Current = null;
    }

    public void Init(LevelData levelData)
    {
        m_PendingEndPresentation.Clear();
        m_ObjectivesPresentationDirty = false;
        SubscribeEndEvent();
        LogicGameEndService.Initialize(levelData);
        Log.Info("[GameEndManager] Initialized. level={0}, objectives={1}.",
            LogicGameEndService.CurrentLevelIdentifier,
            LogicGameEndService.GetObjectiveSnapshot().Count);
    }

    public bool TryGetLevelObjectives(out IReadOnlyList<LevelObjectiveState> objectives)
    {
        objectives = null;
        if (!LogicGameEndService.IsInitialized)
            return false;
        objectives = LogicGameEndService.GetObjectiveSnapshot();
        if (objectives.Count == 0)
            return false;
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
        LogicGameEndService.ObjectivesChanged += OnLogicObjectivesChanged;
        m_EndEventSubscribed = true;
    }

    private void UnsubscribeEndEvent()
    {
        if (!m_EndEventSubscribed)
            return;
        LogicGameEndService.GameEnded -= OnLogicGameEnded;
        LogicGameEndService.ObjectivesChanged -= OnLogicObjectivesChanged;
        m_EndEventSubscribed = false;
    }

    private void OnLogicGameEnded(LogicGameEndResult result)
    {
        m_PendingEndPresentation.Enqueue(result);
    }

    private void OnLogicObjectivesChanged()
    {
        m_ObjectivesPresentationDirty = true;
    }

    public void UpdatePresentation()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("GameEndManager.UpdatePresentation cannot run during a logic frame.");

        if (m_ObjectivesPresentationDirty)
        {
            m_ObjectivesPresentationDirty = false;
            GF.Event.Fire(this, LevelObjectivesChangedEventArgs.Create());
        }
        while (m_PendingEndPresentation.Count > 0)
            PresentGameEnd(m_PendingEndPresentation.Dequeue());
    }

    private void PresentGameEnd(LogicGameEndResult result)
    {
        CareerWinRecordResult? careerResult = result.IsWin ? RecordCareerWin() : null;
        CareerSettlementPresentationService.SetLatest(careerResult);
        HandleGameEndPresentation(result.IsWin);
        if (result.IsWin)
        {
            GF.Event.Fire(this, GameEndResultEventArgs.CreateWin());
            Log.Info("[GameEndManager] GameEnd WIN, logicFrame={0}.", LogicGameEndService.LastAppliedFrame);
        }
        else
        {
            GF.Event.Fire(this, GameEndResultEventArgs.CreateFail(result.FailedObjectiveIdentifier));
            Log.Info("[GameEndManager] GameEnd FAIL by objective={0}, logicFrame={1}.", result.FailedObjectiveIdentifier, LogicGameEndService.LastAppliedFrame);
        }
    }

    private static CareerWinRecordResult? RecordCareerWin()
    {
        if (!CareerRunSettings.HasActiveRun)
        {
            Log.Info("[Career] Win was not recorded because the level was entered without career run settings.");
            return null;
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
            offsetRate,
            LogicGameEndService.GetCompletedOptionalExperience());
        Log.Info(
            "[Career] Win recorded. level={0}, experiment={1}, firstClear={2}, offset={3}, experience={4}, totalExperience={5}, grade={6}->{7}, unlockedIndustries={8}.",
            record.LevelIdentifier,
            record.IsExperiment,
            record.FirstClear,
            record.OffsetRate,
            record.TotalExperienceGained,
            progress.Experience,
            record.PreviousGrade,
            record.CurrentGrade,
            string.Join(",", record.UnlockedArchetypes));

        for (int i = 0; i < record.UnlockedArchetypes.Count; i++)
        {
            Archetype unlockedArchetype = record.UnlockedArchetypes[i];
            string industryName = LocalizationTextDataModel.GetText($"Archetype_{unlockedArchetype}");
            UnlockPresentationService.Enqueue(new UnlockPayload(
                UnlockPayloadType.Industry,
                industryName,
                "\u65b0\u7684\u521d\u59cb\u884c\u4e1a\u5df2\u52a0\u5165\u5173\u5361\u9009\u9879\u3002"));
        }
        for (int i = 0; i < record.UnlockedLevelTags.Count; i++)
        {
            LevelTagTable tag = record.UnlockedLevelTags[i];
            UnlockPresentationService.Enqueue(new UnlockPayload(
                UnlockPayloadType.LevelTag,
                LocalizationTextDataModel.GetText(tag.NameKey),
                DescriptionValueFormatter.LocalizeAndFill(tag.DescKey, tag.UniqueValues)));
        }
        return record;
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
