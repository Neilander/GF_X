using GameFramework;
using GameFramework.Event;

/// <summary>
/// 非 Invade 阶段给敌方建筑提供“无敌/不可被索敌/不攻击”保护。
/// 通过监听阶段与归属变化事件同步状态，避免每帧轮询。
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

        bool isEnemyBuilding = building.OwnerFactionID == EntitySideHelper.EnemyFactionId;
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        bool shouldProtect = isEnemyBuilding && phase != GamePhase.Invade;

        building.SetPhaseProtectionByBuff(shouldProtect);
        if (shouldProtect)
            building.RegisterInvincibleSource(_invincibleSourceId);
        else
            building.UnregisterInvincibleSource(_invincibleSourceId);
    }
}
