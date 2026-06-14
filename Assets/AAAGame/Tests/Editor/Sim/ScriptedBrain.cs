using UnityEngine;

/// <summary>
/// 脚本化 Brain：测试时逐帧设定移动和攻击输入。
/// </summary>
public class ScriptedBrain : IControlBrain, ITickBrain
{
    public Vector2 Move { get; set; }
    public bool Attack { get; set; }
    public bool Skill1 { get; set; }
    public bool Skill2 { get; set; }
    public bool Skill3 { get; set; }
    public bool Skill4 { get; set; }
    public bool Skill5 { get; set; }

    public void Tick(IEntityContext self, float dt)
    {
        // 默认什么也不做，测试代码直接设置 Move/Attack 属性
    }
}
