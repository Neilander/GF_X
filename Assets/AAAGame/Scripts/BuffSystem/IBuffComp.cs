using AAAGame.Scripts.BuffSystem;

/// <summary>
/// Buff 组件接口。
/// 定义 Buff 系统的标准生命周期和操作方法。
/// </summary>
public interface IBuffComp : ICapability
{
    /// <summary>初始化，绑定宿主实体</summary>
    void Init(IEntityContext ctx);

    /// <summary>每帧驱动，在 MAEntity.OnUpdate 中调用</summary>
    void UpdateBuff(float deltaTime);

    /// <summary>添加 Buff</summary>
    void AddBuff(BuffData buffData, IEntityContext creator);

    /// <summary>按 ID 移除 Buff（遵循 RemoveStrategy）</summary>
    void RemoveBuff(string buffId);

    /// <summary>按标签移除所有匹配的 Buff</summary>
    void RemoveBuffByTag(string tag);

    /// <summary>查询是否存在指定 Buff</summary>
    bool HasBuff(string buffId);

    /// <summary>获取指定 Buff 的当前层数，不存在返回 0</summary>
    int GetBuffStack(string buffId);
}
