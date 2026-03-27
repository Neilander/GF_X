using AAAGame.Scripts.BuffSystem;
using UnityEngine;

/// <summary>
/// [示例] 护盾 Buff 回调。
/// 纯 Debug 版本，仅输出日志验证时机和参数正确性。
/// 响应 OnCreate / OnRemove 两个时机。
/// </summary>
public class DebugVersionShieldBuff : BuffCallback
{
    private readonly float _shieldAmount;

    public DebugVersionShieldBuff(float shieldAmount)
    {
        _shieldAmount = shieldAmount;
    }

    public override void Apply(BuffRuntimeInfo info, string trigger)
    {
        switch (trigger)
        {
            case BuffConstant.OnCreate:
                Debug.Log($"[ShieldBuff] {info.Target?.ReferenceId} 获得护盾 {_shieldAmount}");
                break;
            case BuffConstant.OnRemove:
                Debug.Log($"[ShieldBuff] {info.Target?.ReferenceId} 护盾消失");
                break;
        }
    }
}
