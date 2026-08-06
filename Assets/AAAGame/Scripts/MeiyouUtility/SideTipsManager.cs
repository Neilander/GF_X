using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using UnityGameFramework.Runtime;

public class SideTipsManager : GameFrameworkComponent
{
    [SerializeField] private float tipSpacing = 10f;

    private readonly HashSet<UnitType> shownEnemyUnitTypes = new HashSet<UnitType>();
    private bool isSubscribed;
    private bool hasLoggedWaitingForEventComponent;

    public float TipSpacing => tipSpacing;

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
        TrySubscribeEvents();
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
        UnsubscribeEvents();
    }

    private void OnLevelLoadStarted()
    {
        ResetShownUnitTypes();
    }

    private void TrySubscribeEvents()
    {
        if (isSubscribed)
            return;

        if (GF.Event == null)
        {
            if (!hasLoggedWaitingForEventComponent)
            {
                hasLoggedWaitingForEventComponent = true;
                Log.Info("[SideTips] GF.Event is not ready yet, will retry subscription in Update.");
            }
            return;
        }

        hasLoggedWaitingForEventComponent = false;

        GF.Event.Subscribe(EnemyUnitVisibilityChangedEventArgs.EventId, OnEnemyUnitVisibilityChanged);
        GF.Event.Subscribe(CloseSideTipEventArgs.EventId, OnCloseSideTipRequested);
        isSubscribed = true;
        Log.Info("[SideTips] Subscribed side tip related events.");
    }

    public void BootstrapIfNeeded()
    {
        TrySubscribeEvents();
    }

    private void UnsubscribeEvents()
    {
        if (!isSubscribed)
            return;

        if (GF.Event != null)
        {
            GF.Event.Unsubscribe(EnemyUnitVisibilityChangedEventArgs.EventId, OnEnemyUnitVisibilityChanged);
            GF.Event.Unsubscribe(CloseSideTipEventArgs.EventId, OnCloseSideTipRequested);
        }

        isSubscribed = false;
    }

    private void OnCloseSideTipRequested(object sender, GameEventArgs e)
    {
        CloseSideTipEventArgs args = (CloseSideTipEventArgs)e;
        CloseTip(args.TipId);
    }

    private void OnEnemyUnitVisibilityChanged(object sender, GameEventArgs e)
    {
        EnemyUnitVisibilityChangedEventArgs args = (EnemyUnitVisibilityChangedEventArgs)e;
        if (args.NewCellState != Fog3CellState.Visible || args.OldCellState == Fog3CellState.Visible)
            return;

        MAEntity entity = args.Entity;
        if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide)
            return;

        if (!TryResolveTipKeys(entity, out UnitType unitType, out string nameKey))
            return;

        if (shownEnemyUnitTypes.Contains(unitType))
            return;

        string title = LocalizationTextManager.GetLocalizedText(nameKey, false);
        string content = entity.CharacterData.GetFormattedDesc();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
            return;

        if (!shownEnemyUnitTypes.Add(unitType))
            return;

        ShowDynamicTip("EnemyUnitDiscovered", title, content);
        Log.Info("[SideTips] First visible enemy unit type shown. unitType={0}, entityId={1}.", unitType, args.EntityId);
    }

    private static bool TryResolveTipKeys(MAEntity entity, out UnitType unitType, out string nameKey)
    {
        unitType = default;
        nameKey = string.Empty;

        if (entity == null || entity.CharacterData == null)
            return false;

        if (!Enum.TryParse(entity.CharacterKey, out unitType))
            return false;

        nameKey = entity.CharacterData.NameKey;
        return true;
    }

    public void ShowTip(
        string identifier,
        float? durationOverride = null,
        string conditionTipId = null,
        params object[] contentFormatArgs)
    {
        if (GF.UI == null)
            throw new InvalidOperationException("SideTipsManager requires GF.UI before showing a tip.");

        TipPresentation presentation = TipsDataModel.GetPresentation(
            identifier,
            contentFormatArgs: contentFormatArgs);
        ShowResolvedTip(identifier, presentation, durationOverride, conditionTipId);
    }

    public void ShowDynamicTip(
        string identifier,
        string title,
        string content,
        float? durationOverride = null,
        string conditionTipId = null)
    {
        if (GF.UI == null)
            throw new InvalidOperationException("SideTipsManager requires GF.UI before showing a tip.");

        TipPresentation presentation = TipsDataModel.GetPresentation(identifier, title, content);
        ShowResolvedTip(identifier, presentation, durationOverride, conditionTipId);
    }

    public void CloseTip(string tipId)
    {
        if (GF.UI == null)
            throw new InvalidOperationException("SideTipsManager requires GF.UI before closing a tip.");

        GF.UI.CloseConditionalSideTip(tipId);
    }

    private static void ShowResolvedTip(
        string identifier,
        TipPresentation presentation,
        float? durationOverride,
        string conditionTipId)
    {
        float duration = presentation.ResolveDuration(durationOverride);
        string resolvedTipId = ResolveConditionalTipId(identifier, duration, conditionTipId);

        if (!string.IsNullOrWhiteSpace(resolvedTipId))
        {
            GF.UI.ShowConditionalSideTip(
                resolvedTipId,
                presentation.Title,
                presentation.Content,
                duration,
                presentation.Icon);
            return;
        }

        GF.UI.ShowSideTips(
            presentation.Title,
            presentation.Content,
            duration,
            presentation.Icon);
    }

    public static string ResolveConditionalTipId(string identifier, float duration, string conditionTipId)
    {
        if (!string.IsNullOrWhiteSpace(conditionTipId))
            return conditionTipId;
        return duration < 0f ? identifier : null;
    }

    public void ResetShownUnitTypes()
    {
        shownEnemyUnitTypes.Clear();
    }
}
