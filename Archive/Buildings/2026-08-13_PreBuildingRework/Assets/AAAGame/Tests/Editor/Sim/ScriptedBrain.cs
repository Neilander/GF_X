using UnityEngine;

/// <summary>
/// 脚本化 Brain：测试时逐帧设定移动和攻击输入。
/// </summary>
public class ScriptedBrain : IControlBrain, ITickBrain
{
    private Vector2 _move;
    private FixVector2? _moveFixedOverride;

    public Vector2 Move
    {
        get => _move;
        set
        {
            _move = value;
            _moveFixedOverride = null;
        }
    }

    public FixVector2 MoveFixed
    {
        get => _moveFixedOverride ?? new FixVector2((Fix64)_move.x, (Fix64)_move.y);
        set
        {
            _moveFixedOverride = value;
            _move = new Vector2((float)value.x, (float)value.y);
        }
    }
    public bool Attack { get; set; }
    public bool Skill1 { get; set; }
    public bool Skill2 { get; set; }
    public bool Skill3 { get; set; }
    public bool Skill4 { get; set; }
    public bool Skill5 { get; set; }

    public void Tick(IEntityContext self, Fix64 dt)
    {
        // 默认什么也不做，测试代码直接设置 Move/Attack 属性
    }
}
