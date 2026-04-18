using UnityEngine;

/// <summary>
/// 混合型 Tech Effect 基类：同时支持
///   1) 修改建筑级 ExtraProps（如兵力、日产出）
///   2) 注册 per-building 单位 Buff（如单位出生时加血/加攻）
/// 派生类只需按需 override：
///   - ApplyExtraProps：纯建筑属性加成
///   - HasUnitBuff + CreateBuildingScopedBuff：单位 Buff 部分
/// 纯建筑属性 tech 只填前者；混合 tech 两者都填。
/// </summary>
public abstract class HybridBuildingTechEffectSO : TechEffectSO
{
    /// <summary>
    /// 是否注册单位 Buff 通道（OnUnit spawn 时从 ExtraProps 相关 pipeline 挂 BuffData）。
    /// 默认 false；带单位 Buff 的派生类 override 为 true。
    /// </summary>
    protected virtual bool HasUnitBuff => false;

    public override void Activate(TechEffectContext context)
    {
        if (context?.GlobalBuffManager == null || context.TechData == null)
            return;

        if (context.ResolvedScope == null || context.ResolvedScope.BuildingInstanceIds.Count == 0)
        {
            Debug.LogWarning($"[{GetType().Name}] ResolvedScope 无 BuildingInstanceIds, techId={context.TechId}");
            return;
        }

        foreach (var buildingInstanceId in context.ResolvedScope.BuildingInstanceIds)
        {
            // 1. 建筑属性增量（直接写入中央字典的 ExtraProps，升级场景自动延续）
            var extra = context.GlobalBuffManager.GetOrCreateExtraProps(buildingInstanceId);
            if (extra != null)
                ApplyExtraProps(extra, context.TechData);

            // 2. 单位 Buff 注册（只有混合类型 tech 走这条）
            if (HasUnitBuff)
            {
                context.GlobalBuffManager.RegisterBuildingBuff(
                    buildingInstanceId,
                    context.OwnerFactionId,
                    context.TechId,
                    this,
                    context.TechData);
            }
        }
    }

    /// <summary>
    /// 修改建筑的 ExtraProps。默认不做任何事，派生类按需 override。
    /// </summary>
    protected virtual void ApplyExtraProps(BuildingExtraProps extra, TechData techData)
    {
    }

    // CreateBuildingScopedBuff 保持父类默认实现返回 null；有单位 Buff 的派生类按需 override。
}
