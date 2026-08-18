/// <summary>
/// 英雄幽灵状态 Buff：
/// - 生效时进入幽灵态（不可攻击、无敌、不可被选为攻击目标、半透明）
/// - 阶段切换时自动移除，并由宿主恢复为满血常态
/// </summary>
public class HeroGhostBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "hero_ghost_state::source"
            : $"{buffData.id}::source";

        if (!hostEntity.TryGetLogicHero(out IHeroLogicContext hero))
            throw new System.InvalidOperationException("HeroGhostBuff requires a hero logic context.");
        hero.RegisterInvincibleSource(_invincibleSourceId);
        hero.SetGhostStateByBuff(true);
    }

    public override void OnRemove()
    {
        if (!hostEntity.TryGetLogicHero(out IHeroLogicContext hero))
            throw new System.InvalidOperationException("HeroGhostBuff requires a hero logic context.");
        hero.UnregisterInvincibleSource(_invincibleSourceId);
        hero.RestoreFromGhostState();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_invincibleSourceId);
    }
}
