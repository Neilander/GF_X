using GameFramework;
using UnityGameFramework.Runtime;

/// <summary>
/// 交互参数：仿 EntityParams 的使用方式，供 InteractionHost 创建交互时传入。
/// </summary>
public class InteractionParams : RefParams
{
    public new static InteractionParams Create()
    {
        var p = ReferencePool.Acquire<InteractionParams>();
        p.CreateRoot();
        return p;
    }

    protected override void ResetProperties()
    {
        base.ResetProperties();
    }
}
