using UnityEngine;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 伤害统一入口。所有攻击出口应走这里，而不是直接调 target.TakeDamage。
/// 好处：在这里可以集中插入 buff 钩子、护盾、反伤、吸血、日志等。
/// </summary>
public static class DamageHelper
{
    /// <summary>
    /// 对 target 造成伤害。attacker 为可空（比如环境伤害），若有则允许其身上的 Buff 修改最终伤害。
    /// </summary>
    public static void DoDamage(ITargetable target, Damage damage, IEntityContext attacker = null)
    {
        if (target == null || !target.Alive)
            return;

        Fix64 finalAmount = damage != null ? damage.amount : Fix64.Zero;
        HealthModifyType modType = damage != null ? damage.modType : HealthModifyType.reduce;

        // Buff 钩子：允许 attacker 身上的 buff 修改最终伤害
        if (attacker != null)
        {
            var buffComp = attacker.BuffComp as CharacterBuffComp;
            if (buffComp != null)
            {
                foreach (var module in buffComp.EnumerateAllModules())
                {
                    finalAmount = module.ModifyOutgoingDamage(target, finalAmount);
                }
            }
        }

        if (finalAmount < Fix64.Zero)
            finalAmount = Fix64.Zero;

        // 适配 target 的 TakeDamage 签名。ITargetable 未暴露 TakeDamage，需要转成 IEntityContext / GeneralCreature。
        if (target is IEntityContext ctx)
        {
            ctx.TakeDamage(finalAmount, modType, attacker);
        }
        else if (target is GeneralCreature gc)
        {
            gc.TakeDamage(finalAmount, modType, attacker);
        }
    }
}
