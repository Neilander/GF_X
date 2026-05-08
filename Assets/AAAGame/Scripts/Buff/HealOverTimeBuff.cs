using UnityEngine;

/// <summary>
/// 持续回血 Buff：每帧按"最大血量 × percentPerSec × dt"加到 HealthCurrent。
/// 用于脱战返航等需要快速回血的场景。
/// </summary>
public sealed class HealOverTimeBuff : BuffCallback
{
    private readonly Fix64 m_PercentPerSec;
    private float m_LogAccum;
    private bool m_LoggedFirstTick;

    public HealOverTimeBuff(Fix64 percentPerSec)
    {
        m_PercentPerSec = percentPerSec;
    }

    public override void OnAdd()
    {
        base.OnAdd();
        m_LogAccum = 0f;
        m_LoggedFirstTick = false;
        Debug.Log($"[HoT.OnAdd] host={(hostEntity != null ? hostEntity.CharacterKey : "null")} " +
                  $"buffComp={hostEntity?.BuffComp?.GetType().Name ?? "null"} " +
                  $"percentPerSec={(float)m_PercentPerSec}");
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);

        m_LogAccum += deltaTime;
        bool shouldLog = !m_LoggedFirstTick || m_LogAccum >= 1f;
        if (shouldLog)
        {
            m_LogAccum = 0f;
            m_LoggedFirstTick = true;
        }

        if (deltaTime <= 0f || m_PercentPerSec == Fix64.Zero)
        {
            if (shouldLog) Debug.LogWarning($"[HoT] early-exit dt={deltaTime} pct={(float)m_PercentPerSec}");
            return;
        }

        var creature = hostEntity as GeneralCreature;
        var pm = creature?.CreaturePropertyManager;
        if (pm == null || !creature.Alive)
        {
            if (shouldLog)
                Debug.LogWarning($"[HoT] early-exit pm={(pm != null)} alive={creature?.Alive} " +
                                 $"hostType={hostEntity?.GetType().Name ?? "null"}");
            return;
        }

        Fix64 maxHp = pm.GetProperty(CreatureMainProperty.Health);
        Fix64 curHp = creature.HealthValue;
        if (curHp >= maxHp)
        {
            if (shouldLog)
                Debug.Log($"[HoT] {creature.CharacterKey} full hp cur={(float)curHp:F1}/max={(float)maxHp:F1}");
            return;
        }

        Fix64 healThisFrame = maxHp * m_PercentPerSec * (Fix64)deltaTime;
        if (healThisFrame <= Fix64.Zero)
        {
            if (shouldLog)
                Debug.LogWarning($"[HoT] healThisFrame<=0 maxHp={(float)maxHp} pct={(float)m_PercentPerSec} dt={deltaTime}");
            return;
        }

        creature.Heal(healThisFrame);

        if (shouldLog)
        {
            Fix64 afterHp = creature.HealthValue;
            Debug.Log($"[HoT] {creature.CharacterKey} cur={(float)curHp:F1}->{(float)afterHp:F1}/max={(float)maxHp:F1} " +
                      $"+{(float)healThisFrame:F3}/frame (≈{(float)maxHp * (float)m_PercentPerSec:F1}/s)");
        }
    }
}
