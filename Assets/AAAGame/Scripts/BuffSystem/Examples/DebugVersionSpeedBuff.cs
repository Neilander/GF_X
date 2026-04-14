using AAAGame.Scripts.BuffSystem;
using UnityEngine;

/// <summary>
/// [示例] 移速修改 Buff 回调（加速/减速）。
/// 纯 Debug 版本，仅输出日志验证时机和参数正确性。
/// 响应 OnCreate / OnAddStack / OnReduceStack / OnRemove 四个时机。
/// </summary>
public class DebugVersionSpeedBuff : BuffCallback
{
    private readonly float _speedDelta;

    public DebugVersionSpeedBuff(float speedDelta)
    {
        _speedDelta = speedDelta;
    }

    /*
    public override void Apply(BuffRuntimeInfo info, string trigger)
    {
        switch (trigger)
        {
            case BuffConstant.OnCreate:
                Debug.Log($"[SpeedBuff] {info.Target?.CharacterKey} 移速 +{_speedDelta}，创建时层数 {info.CurrentStack}");
                break;
            case BuffConstant.OnAddStack:
                Debug.Log($"[SpeedBuff] {info.Target?.CharacterKey} 移速 +{_speedDelta}，叠层后 {info.CurrentStack} 层");
                break;
            case BuffConstant.OnReduceStack:
                Debug.Log($"[SpeedBuff] {info.Target?.CharacterKey} 移速 -{_speedDelta}，减层后 {info.CurrentStack} 层");
                break;
            case BuffConstant.OnRemove:
                Debug.Log($"[SpeedBuff] {info.Target?.CharacterKey} 移速 buff 完全移除");
                break;
        }
    }*/
}
