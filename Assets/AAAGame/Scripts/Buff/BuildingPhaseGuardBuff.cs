using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑阶段保护 Buff：
/// - 战斗保护：非 Invade 阶段给敌方建筑提供“无敌/不可被索敌/不攻击”保护。
/// - 血条效果：进入 Build 阶段后为双方建筑施加“禁用血条”效果，离开 Build 后恢复。
/// </summary>
public class BuildingPhaseGuardBuff : BuffCallback
{
    private bool _subscribed;
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "building_phase_guard::source"
            : $"{buffData.id}::source";

        SubscribeEvents();
        RefreshProtectionState();
    }

    public override void OnRemove()
    {
        UnsubscribeEvents();

        if (hostEntity is BuildingEntity building)
        {
            building.SetHealthBarSuppressedByBuff(false);
            building.SetPhaseProtectionByBuff(false);
            building.UnregisterInvincibleSource(_invincibleSourceId);
        }
    }

    private void SubscribeEvents()
    {
        if (_subscribed)
            return;

        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        _subscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_subscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
            GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
        }
        catch (GameFrameworkException)
        {
            // 生命周期收尾时 EventPool 可能先释放，忽略退订异常。
        }
        finally
        {
            _subscribed = false;
        }
    }

    private void OnPhaseChanged(object sender, GameEventArgs e)
    {
        if (e is not IngamePhaseChangedEventArgs)
            return;

        RefreshProtectionState();
    }

    private void OnEntityFactionChanged(object sender, GameEventArgs e)
    {
        if (!(hostEntity is BuildingEntity building))
            return;

        if (e is not EntityFactionChangedEventArgs args)
            return;

        if (args.EntityId != building.Id)
            return;

        RefreshProtectionState();
    }

    private void RefreshProtectionState()
    {
        if (!(hostEntity is BuildingEntity building))
            return;

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        int day = InGameDataModel.GetValue(IngameValueType.Day);

        bool isEnemyBuilding = building.OwnerFactionID == EntitySideHelper.EnemyFactionId;
        bool shouldProtect = isEnemyBuilding && phase != GamePhase.Invade;

        building.SetPhaseProtectionByBuff(shouldProtect);
        if (shouldProtect)
            building.RegisterInvincibleSource(_invincibleSourceId);
        else
            building.UnregisterInvincibleSource(_invincibleSourceId);

        bool isLv0Building = building.buildingData != null && building.buildingData.Lv == 0;
        bool shouldSuppressHealthBar = InGameDataModel.IsBuildPhase(phase) || isLv0Building;
        if (building.IsHealthBarSuppressedByPhaseBuff == shouldSuppressHealthBar)
            return;

        building.SetHealthBarSuppressedByBuff(shouldSuppressHealthBar);
        if (shouldSuppressHealthBar)
        {
            HealthBarComp.Remove(building.Id);
            return;
        }

        if (building.IsHealthBarSuppressedByBuff)
            return;

        if (GameObject.Find($"HealthBar_{building.Id}") != null)
            return;

        Fix64 max = building.CreaturePropertyManager != null
            ? building.CreaturePropertyManager.GetProperty(CreatureMainProperty.Health)
            : building.HealthValue;
        bool isFriendly = building.OwnerFactionID == EntitySideHelper.PlayerFactionId;
        var created = HealthBarComp.Create(building.Id, building.transform, (float)building.HealthValue, (float)max, isFriendly);
        if (created == null)
        {
            Log.Warning(
                "[BuildingPhaseGuardBuff] Failed to restore building health bar. id={0}, phase={1}, day={2}, ownerFaction={3}, lv0Invincible={4}",
                building.Id,
                phase,
                day,
                building.OwnerFactionID,
                building.IsLv0Invincible);
        }
    }
}
