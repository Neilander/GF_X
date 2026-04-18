using GameFramework;
using GameFramework.Event;
using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using UnityEngine;
using UnityGameFramework.Runtime;

public class SideTipsManager : GameFrameworkComponent
{
    [SerializeField] private float tipDuration = 2f;

    private readonly HashSet<UnitType> shownEnemyUnitTypes = new HashSet<UnitType>();
    private bool isSubscribed;
    private bool hasLoggedWaitingForEventComponent;

    private void Start()
    {
        TrySubscribeEvents();
    }

    private void OnEnable()
    {
        TrySubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
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
                Log.Warning("[SideTips] GF.Event is not ready yet, will retry subscription in Update.");
            }
            return;
        }

        hasLoggedWaitingForEventComponent = false;

        GF.Event.Subscribe(EnemyUnitVisibilityChangedEventArgs.EventId, OnEnemyUnitVisibilityChanged);
        isSubscribed = true;
        Log.Info("[SideTips] Subscribed EnemyUnitVisibilityChangedEventArgs.");
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
            GF.Event.Unsubscribe(EnemyUnitVisibilityChangedEventArgs.EventId, OnEnemyUnitVisibilityChanged);

        isSubscribed = false;
    }

    private void OnEnemyUnitVisibilityChanged(object sender, GameEventArgs e)
    {
        EnemyUnitVisibilityChangedEventArgs args = (EnemyUnitVisibilityChangedEventArgs)e;
        if (args.NewCellState != Fog3CellState.Visible || args.OldCellState == Fog3CellState.Visible)
            return;

        MAEntity entity = args.Entity;
        if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide)
            return;

        Log.Info("[SideTips] Enemy visibility visible event received. entityId={0}, old={1}, new={2}, characterKey={3}.",
            args.EntityId, args.OldCellState, args.NewCellState, entity.CharacterKey);

        if (!TryResolveTipKeys(entity, out UnitType unitType, out string nameKey, out string descKey))
        {
            Log.Warning("[SideTips] Failed to resolve unit type or tip keys. entityId={0}, characterKey={1}.", args.EntityId, entity.CharacterKey);
            return;
        }

        if (shownEnemyUnitTypes.Contains(unitType))
            return;

        string title = Localize(nameKey);
        string content = Localize(descKey);
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
            return;

        if (GF.UI == null)
            return;

        if (!shownEnemyUnitTypes.Add(unitType))
            return;

        GF.UI.ShowSideTips(title, content, tipDuration);
        Log.Info("[SideTips] First visible enemy unit type shown. unitType={0}, entityId={1}.", unitType, args.EntityId);
    }

    private static bool TryResolveTipKeys(MAEntity entity, out UnitType unitType, out string nameKey, out string descKey)
    {
        unitType = default;
        nameKey = string.Empty;
        descKey = string.Empty;

        if (entity == null || entity.CharacterData == null)
            return false;

        if (!Enum.TryParse(entity.CharacterKey, out unitType))
            return false;

        nameKey = entity.CharacterData.NameKey;
        descKey = entity.CharacterData.DescKey;
        return true;
    }

    private static string Localize(string key)
    {
        if (string.IsNullOrEmpty(key) || GF.Localization == null)
            return string.Empty;

        return GF.Localization.GetString(key);
    }

    public void ResetShownUnitTypes()
    {
        shownEnemyUnitTypes.Clear();
    }
}