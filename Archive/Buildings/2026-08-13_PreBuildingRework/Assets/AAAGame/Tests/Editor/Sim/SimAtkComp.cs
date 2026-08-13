using UnityEngine;

/// <summary>
/// 纯逻辑攻击组件：记录攻击调用，不依赖 Animator 和 BasicAction。
/// </summary>
public class SimAtkComp : IAtkComp
{
    private IEntityContext _ctx;

    public int AttackCount { get; private set; }
    public bool IsAttacking { get; private set; }
    public float AttackDuration = 0.5f;

    private Fix64 _attackTimer;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        AttackCount = 0;
        IsAttacking = false;
        _attackTimer = Fix64.Zero;
    }

    public void Attack(Fix64 deltaTime)
    {
        if (_ctx == null) return;
        if (_ctx.Brain == null) return;

        if (IsAttacking)
        {
            _attackTimer += deltaTime;
            if (_attackTimer >= (Fix64)AttackDuration)
            {
                IsAttacking = false;
                _attackTimer = Fix64.Zero;
                _ctx.ResumeComp(_ctx.MoveComp, this);
            }
            return;
        }

        if (_ctx.Brain.Attack)
        {
            IsAttacking = true;
            AttackCount++;
            _attackTimer = Fix64.Zero;
            _ctx.LockComp(_ctx.MoveComp, this);
        }
    }

    public void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced)
    {
        if (IsAttacking && _ctx != null)
        {
            _ctx.ResumeComp(_ctx.MoveComp, this);
        }

        IsAttacking = false;
        _attackTimer = Fix64.Zero;
    }

    public void ShutDown()
    {
        InterruptAttack(AttackInterruptReason.CapabilityLocked);
    }

    public void Resume() { }
}
