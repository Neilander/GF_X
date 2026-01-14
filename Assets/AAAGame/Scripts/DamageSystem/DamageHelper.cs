using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class DamageHelper
{
   public static void DoDamage(ITargetable target, Damage damage)
   {
      // 1. 基础合法性检查
      if (target == null)
         return;

      if (!target.Alive)
         return;

      // 2. 造成伤害
      target.TakeDamage(damage.amount, damage.modType);

      // 3. 后续扩展点（现在不做）
      // - 护盾扣减
      // - Buff 触发
      // - OnDamaged 事件
      // - 反伤 / 吸血（如果你以后放在这里）
   }
}
