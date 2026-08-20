/// <summary>
/// 统一无敌状态 Buff（被动标记型）。
/// 真实的授予/移除由 MAEntity 的来源注册机制维护。
/// </summary>
public sealed class InvincibleStateBuff : BuffCallback
{
    public const string BuffId = "state_invincible";
}

/// <summary>
/// 通用缴械状态：停止攻击并暂停主动索敌。
/// 多个缴械模块通过能力锁独立叠加，移除单一来源不会提前恢复能力。
/// </summary>
public sealed class DisarmedStateBuff : BuffCallback, ICapability, ILogicDeterministicStateContributor
{
    private bool m_Applied;

    public override bool IsNegativeStatus => true;

    public override void OnAdd()
    {
        if (m_Applied)
            throw new System.InvalidOperationException("DisarmedStateBuff.OnAdd was called twice.");
        if (hostEntity == null || hostEntity.AtkComp == null || hostEntity.TargetComp == null)
        {
            throw new System.InvalidOperationException(
                $"DisarmedStateBuff requires attack and targeting components. host={hostEntity?.CharacterKey}.");
        }

        hostEntity.LockComp(hostEntity.AtkComp, this);
        hostEntity.LockComp(hostEntity.TargetComp, this);
        m_Applied = true;
    }

    public override void OnRemove()
    {
        if (!m_Applied)
            throw new System.InvalidOperationException("DisarmedStateBuff.OnRemove was called before OnAdd.");
        if (hostEntity == null || hostEntity.AtkComp == null || hostEntity.TargetComp == null)
            throw new System.InvalidOperationException("DisarmedStateBuff lost its host capabilities before removal.");

        hostEntity.ResumeComp(hostEntity.TargetComp, this);
        hostEntity.ResumeComp(hostEntity.AtkComp, this);
        m_Applied = false;
    }

    public void ShutDown() { }
    public void Resume() { }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Applied);
    }
}
