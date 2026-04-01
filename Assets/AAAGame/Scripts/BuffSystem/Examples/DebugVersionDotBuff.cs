using AAAGame.Scripts.BuffSystem;
using UnityEngine;

/// <summary>
/// [示例] 持续伤害 Buff 回调（毒、燃烧等）。
/// 纯 Debug 版本，仅输出日志验证时机和参数正确性。
/// </summary>
public class DebugVersionDotBuff : BuffCallback
{
    private readonly float _baseDamage;

    public DebugVersionDotBuff(float baseDamage)
    {
        _baseDamage = baseDamage;
    }

    public override void Apply(BuffRuntimeInfo info, string trigger)
    {
        if (trigger != BuffConstant.OnTick) return;

        float damage = _baseDamage * info.CurrentStack;
        Debug.Log($"[DotBuff] {info.Target?.ReferenceId} 受到持续伤害 {damage}（基础 {_baseDamage} × {info.CurrentStack} 层）");
    }
}
