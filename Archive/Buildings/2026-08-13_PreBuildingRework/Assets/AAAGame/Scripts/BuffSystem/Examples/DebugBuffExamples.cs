using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;


/*
/// <summary>
/// [示例] 演示如何构建 BuffData 并添加到实体上。
/// 纯 Debug 版本，可在测试 Procedure 中调用验证 buff 系统是否正常工作。
/// </summary>
public static class DebugBuffExamples
{
    /// <summary>
    /// 中毒 Buff：每秒 tick 一次，按层数造成伤害，最多叠 5 层，
    /// 重复添加时刷新时间 + 叠层，过期时逐层减少。
    /// </summary>
    public static BuffData CreatePoisonBuff()
    {
        return new BuffData(
            id: "debug_poison",
            maxStack: 5,
            isForever: false,
            duration: 3f,
            tickTime: 1f,
            updateStrategy: BuffUpdateEnum.ReplaceAndAddStack,
            removeStrategy: BuffRemoveEnum.Reduce,
            modules: new List<BuffCallback> { new DebugVersionDotBuff(2f) }
        );
    }

    /// <summary>
    /// 回血 Buff：持续 10 秒，每 2 秒恢复一次，不叠层，过期直接移除。
    /// </summary>
    public static BuffData CreateRegenBuff()
    {
        return new BuffData(
            id: "debug_regen",
            maxStack: 1,
            isForever: false,
            duration: 10f,
            tickTime: 2f,
            updateStrategy: BuffUpdateEnum.AddTime,
            removeStrategy: BuffRemoveEnum.Clear,
            modules: new List<BuffCallback> { new DebugVersionHealOverTimeBuff(5f) }
        );
    }

    /// <summary>
    /// 加速 Buff：持续 5 秒，最多叠 3 层，过期直接移除。
    /// </summary>
    public static BuffData CreateSpeedBuff()
    {
        return new BuffData(
            id: "debug_speed",
            maxStack: 3,
            isForever: false,
            duration: 5f,
            tickTime: 0f,
            updateStrategy: BuffUpdateEnum.ReplaceAndAddStack,
            removeStrategy: BuffRemoveEnum.Clear,
            modules: new List<BuffCallback> { new DebugVersionSpeedBuff(1.5f) }
        );
    }

    /// <summary>
    /// 护盾 Buff：永久存在直到手动移除，不叠层。
    /// </summary>
    public static BuffData CreateShieldBuff()
    {
        return new BuffData(
            id: "debug_shield",
            maxStack: 1,
            isForever: true,
            duration: 0f,
            tickTime: 0f,
            updateStrategy: BuffUpdateEnum.AddTime,
            removeStrategy: BuffRemoveEnum.Clear,
            modules: new List<BuffCallback> { new DebugVersionShieldBuff(50f) }
        );
    }

    /// <summary>
    /// 使用示例：在测试代码中调用
    /// <code>
    /// var poisonData = DebugBuffExamples.CreatePoisonBuff();
    /// entity.BuffComp.AddBuff(poisonData, attackerEntity);
    /// </code>
    /// 然后观察 Console 输出验证 buff 生命周期是否正确。
    /// </summary>
    public static void LogUsageHint()
    {
        Debug.Log("[DebugBuffExamples] 用法：var data = DebugBuffExamples.CreatePoisonBuff(); entity.BuffComp.AddBuff(data, attacker);");
    }
}
*/