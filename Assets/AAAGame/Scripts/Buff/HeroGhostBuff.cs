using GameFramework;
using GameFramework.Event;

/// <summary>
/// 英雄幽灵状态 Buff：
/// - 生效时进入幽灵态（不可攻击、无敌、不可被选为攻击目标、半透明）
/// - 阶段切换时自动移除，并由宿主恢复为满血常态
/// </summary>
public class HeroGhostBuff : BuffCallback
{
    private bool _subscribed;
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "hero_ghost_state::source"
            : $"{buffData.id}::source";

        SubscribeEvents();
        if (hostEntity is HeroEntity soldier)
        {
            soldier.RegisterInvincibleSource(_invincibleSourceId);
            soldier.SetGhostStateByBuff(true);
        }
    }

    public override void OnRemove()
    {
        UnsubscribeEvents();
        if (hostEntity is HeroEntity soldier)
        {
            soldier.UnregisterInvincibleSource(_invincibleSourceId);
            soldier.RestoreFromGhostState();
        }
    }

    private void SubscribeEvents()
    {
        if (_subscribed)
            return;

        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
        _subscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_subscribed)
            return;

        try
        {
            GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
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
        if (e is not IngamePhaseChangedEventArgs args)
            return;

        if (args.OldPhase == args.NewPhase)
            return;

        hostEntity?.BuffComp?.RemoveBuff(buffData?.id);
    }
}