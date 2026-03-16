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

    private float _attackTimer;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        AttackCount = 0;
        IsAttacking = false;
        _attackTimer = 0f;
    }

    public void Attack(float deltaTime)
    {
        if (_ctx == null) return;
        if (_ctx.Brain == null) return;

        if (IsAttacking)
        {
            _attackTimer += deltaTime;
            if (_attackTimer >= AttackDuration)
            {
                IsAttacking = false;
                _attackTimer = 0f;
                _ctx.ResumeComp(_ctx.MoveComp, this);
            }
            return;
        }

        if (_ctx.Brain.Attack)
        {
            IsAttacking = true;
            AttackCount++;
            _attackTimer = 0f;
            _ctx.LockComp(_ctx.MoveComp, this);
        }
    }

    public void ShutDown()
    {
        IsAttacking = false;
        _attackTimer = 0f;
    }

    public void Resume() { }
}
