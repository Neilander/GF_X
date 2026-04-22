using GameFramework;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public enum TutorialTriggerType
{
    None = 0,
    MoveHeroByWASD = 1,
}

public class TutorialManager : GameFrameworkComponent
{
    private const string MoveHeroTipId = "tutorial.move.hero.wasd";
    private const string MoveHeroTipTitleTextId = "Tutorial_MoveHero_Title";
    private const string MoveHeroTipContentTextId = "Tutorial_MoveHero_Content";
    private const float MoveInputThreshold = 0.1f;

    private InputModel inputModel;
    private readonly HashSet<TutorialTriggerType> activeTutorials = new HashSet<TutorialTriggerType>();
    private readonly List<TutorialTriggerType> activeTutorialsBuffer = new List<TutorialTriggerType>(4);
    private readonly HashSet<TutorialTriggerType> completedTutorials = new HashSet<TutorialTriggerType>();
    private bool hasLoggedWaitingForInputModel;

    private void Update()
    {
        if (activeTutorials.Count <= 0)
            return;

        activeTutorialsBuffer.Clear();
        activeTutorialsBuffer.AddRange(activeTutorials);

        for (int i = 0; i < activeTutorialsBuffer.Count; i++)
        {
            TickTutorial(activeTutorialsBuffer[i]);
        }
    }

    public bool NotifyTriggerEntered(TutorialTriggerType triggerType, Component triggerSource = null)
    {
        switch (triggerType)
        {
            case TutorialTriggerType.MoveHeroByWASD:
                return TryStartMoveHeroTutorial(triggerSource);
            default:
                Log.Warning("[Tutorial] Unsupported trigger type: {0}.", triggerType);
                return false;
        }
    }

    public void ResetMoveTutorialForDebug()
    {
        if (activeTutorials.Remove(TutorialTriggerType.MoveHeroByWASD))
        {
            RequestCloseSideTip(MoveHeroTipId);
        }

        completedTutorials.Remove(TutorialTriggerType.MoveHeroByWASD);
        inputModel = null;
        hasLoggedWaitingForInputModel = false;
    }

    private void TickTutorial(TutorialTriggerType triggerType)
    {
        switch (triggerType)
        {
            case TutorialTriggerType.MoveHeroByWASD:
                TickMoveHeroTutorial();
                break;
            default:
                Log.Warning("[Tutorial] Unknown active tutorial trigger: {0}.", triggerType);
                activeTutorials.Remove(triggerType);
                break;
        }
    }

    private bool TryStartMoveHeroTutorial(Component triggerSource)
    {
        if (completedTutorials.Contains(TutorialTriggerType.MoveHeroByWASD))
            return false;

        if (activeTutorials.Contains(TutorialTriggerType.MoveHeroByWASD))
            return false;

        SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager == null)
        {
            Log.Warning("[Tutorial] Start move tutorial failed: SideTipsManager is missing.");
            return false;
        }

        string tipTitle = LocalizationTextDataModel.GetText(MoveHeroTipTitleTextId);
        string tipContent = LocalizationTextDataModel.GetText(MoveHeroTipContentTextId);
        if (string.IsNullOrEmpty(tipTitle) && string.IsNullOrEmpty(tipContent))
        {
            Log.Warning("[Tutorial] Move tutorial tip content is empty.");
            return false;
        }

        sideTipsManager.ShowConditionalTip(MoveHeroTipId, tipTitle, tipContent);
        activeTutorials.Add(TutorialTriggerType.MoveHeroByWASD);

        string sourceName = triggerSource != null ? triggerSource.name : "Unknown";
        Log.Info("[Tutorial] Move tutorial started. trigger={0}.", sourceName);
        return true;
    }

    private void TickMoveHeroTutorial()
    {
        if (!TryGetInputModel(out InputModel model))
            return;

        Fix64 inputThreshold = (Fix64)Mathf.Max(0f, MoveInputThreshold);
        bool hasMovementInput = Fix64.Abs(model.MoveX) > inputThreshold || Fix64.Abs(model.MoveY) > inputThreshold;
        if (!hasMovementInput)
            return;

        CompleteMoveHeroTutorial();
    }

    private void CompleteMoveHeroTutorial()
    {
        if (!activeTutorials.Remove(TutorialTriggerType.MoveHeroByWASD))
            return;

        completedTutorials.Add(TutorialTriggerType.MoveHeroByWASD);
        hasLoggedWaitingForInputModel = false;

        RequestCloseSideTip(MoveHeroTipId);

        Log.Info("[Tutorial] Move tutorial completed.");
    }

    private void RequestCloseSideTip(string tipId)
    {
        if (GF.Event != null)
        {
            GF.Event.Fire(this, CloseSideTipEventArgs.Create(tipId));
            return;
        }

        SideTipsManager sideTipsManager = GameEntry.GetComponent<SideTipsManager>();
        if (sideTipsManager != null)
        {
            sideTipsManager.CloseConditionalTip(tipId);
        }
    }

    private bool TryGetInputModel(out InputModel model)
    {
        if (inputModel == null)
        {
            if (GF.DataModel == null || !GF.DataModel.HasDataModel<InputModel>())
            {
                if (!hasLoggedWaitingForInputModel)
                {
                    hasLoggedWaitingForInputModel = true;
                    Log.Warning("[Tutorial] InputModel is not ready yet.");
                }

                model = null;
                return false;
            }

            inputModel = GF.DataModel.GetDataModel<InputModel>();
        }

        hasLoggedWaitingForInputModel = false;
        model = inputModel;
        return model != null;
    }
}