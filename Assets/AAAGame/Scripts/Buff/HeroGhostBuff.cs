/// <summary>
/// 英雄幽灵状态 Buff：
/// - 生效时进入幽灵态（不可攻击、无敌、不可被选为攻击目标、半透明）
/// - 阶段切换时自动移除，并由宿主恢复为满血常态
/// </summary>
public class HeroGhostBuff : BuffCallback
{
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "hero_ghost_state::source"
            : $"{buffData.id}::source";

        if (hostEntity is IHeroLogicContext soldier)
        {
            soldier.RegisterInvincibleSource(_invincibleSourceId);
            soldier.SetGhostStateByBuff(true);
        }
    }

    public override void OnRemove()
    {
        if (hostEntity is IHeroLogicContext soldier)
        {
            soldier.UnregisterInvincibleSource(_invincibleSourceId);
            soldier.RestoreFromGhostState();
        }
    }

}
