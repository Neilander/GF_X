using AAAGame.Scripts.BuffSystem;
using UnityEngine;

/// <summary>
/// [示例] 持续回血 Buff 回调。
/// 纯 Debug 版本，仅输出日志验证时机和参数正确性。
/// </summary>
public class DebugVersionHealOverTimeBuff : BuffCallback
{
    private readonly float _baseHeal;

    public DebugVersionHealOverTimeBuff(float baseHeal)
    {
        _baseHeal = baseHeal;
    }

    
    /*
    public override void Apply(BuffRuntimeInfo info, string trigger)
    {
        if (trigger != BuffConstant.OnTick) return;

        float heal = _baseHeal * info.CurrentStack;
        Debug.Log($"[HealBuff] {info.Target?.ReferenceId} 恢复生命 {heal}（基础 {_baseHeal} × {info.CurrentStack} 层）");
    }*/
}
