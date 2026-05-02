using UnityEngine;

/// <summary>
/// 持续回血 Buff：每帧按"最大血量 × percentPerSec × dt"加到 HealthCurrent。
/// 用于脱战返航等需要快速回血的场景。
/// </summary>
public sealed class HealOverTimeBuff : BuffCallback
{
    private readonly Fix64 m_PercentPerSec;

    public HealOverTimeBuff(Fix64 percentPerSec)
    {
        m_PercentPerSec = percentPerSec;
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (deltaTime <= 0f || m_PercentPerSec == Fix64.Zero) return;

        var creature = hostEntity as GeneralCreature;
        var pm = creature?.CreaturePropertyManager;
        if (pm == null || !creature.Alive) return;

        Fix64 maxHp = pm.GetProperty(CreatureMainProperty.Health);
        Fix64 curHp = creature.HealthValue;
        if (curHp >= maxHp) return;

        Fix64 healThisFrame = maxHp * m_PercentPerSec * (Fix64)deltaTime;
        if (healThisFrame <= Fix64.Zero) return;

        pm.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(healThisFrame),
            true);
    }
}
