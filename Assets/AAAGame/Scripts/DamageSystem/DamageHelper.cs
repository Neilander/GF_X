using System.Collections.Generic;
using UnityEngine;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// 伤害统一入口。所有攻击出口应走这里，而不是直接调 target.TakeDamage。
/// 好处：在这里可以集中插入 buff 钩子、护盾、反伤、吸血、日志等。
/// </summary>
public static class DamageHelper
{
    private sealed class AttackHitSequence
    {
        public IEntityContext Attacker;
        public int TotalHits;
        public int CurrentHitIndex;
    }

    [System.ThreadStatic] private static Stack<AttackHitSequence> s_HitSequences;

    public static System.IDisposable BeginAttackHitSequence(IEntityContext attacker, int totalHits)
    {
        if (attacker == null || totalHits <= 1)
            return NullDisposable.Instance;

        s_HitSequences ??= new Stack<AttackHitSequence>();
        s_HitSequences.Push(new AttackHitSequence
        {
            Attacker = attacker,
            TotalHits = totalHits,
            CurrentHitIndex = 0,
        });
        return new AttackHitSequenceScope();
    }

    public static int GetCurrentAttackHitIndex(IEntityContext attacker)
    {
        AttackHitSequence sequence = GetCurrentSequence(attacker);
        return sequence != null ? sequence.CurrentHitIndex : 1;
    }

    public static int GetCurrentAttackTotalHits(IEntityContext attacker)
    {
        AttackHitSequence sequence = GetCurrentSequence(attacker);
        return sequence != null ? sequence.TotalHits : 1;
    }

    /// <summary>
    /// 对 target 造成伤害。attacker 为可空（比如环境伤害），若有则允许其身上的 Buff 修改最终伤害。
    /// </summary>
    public static void DoDamage(ITargetable target, Damage damage, IEntityContext attacker = null)
    {
        if (target == null || !target.Alive)
            return;

        AdvanceHitIndex(attacker);

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

        if (target is IEntityContext targetCtx)
        {
            var buffComp = targetCtx.BuffComp as CharacterBuffComp;
            if (buffComp != null)
            {
                foreach (var module in buffComp.EnumerateAllModules())
                {
                    finalAmount = module.ModifyIncomingDamage(attacker, finalAmount, modType);
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

    private static void AdvanceHitIndex(IEntityContext attacker)
    {
        AttackHitSequence sequence = GetCurrentSequence(attacker);
        if (sequence != null)
            sequence.CurrentHitIndex++;
    }

    private static AttackHitSequence GetCurrentSequence(IEntityContext attacker)
    {
        if (attacker == null || s_HitSequences == null || s_HitSequences.Count == 0)
            return null;

        AttackHitSequence sequence = s_HitSequences.Peek();
        return ReferenceEquals(sequence.Attacker, attacker) ? sequence : null;
    }

    private sealed class AttackHitSequenceScope : System.IDisposable
    {
        public void Dispose()
        {
            if (s_HitSequences == null || s_HitSequences.Count == 0)
                return;

            s_HitSequences.Pop();
        }
    }

    private sealed class NullDisposable : System.IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

public static class AreaWeaponDamage
{
    public static void DealSplash(IEntityContext attacker, IEntityContext mainTarget, WeaponData weaponData)
    {
        if (attacker == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.DealSplash failed: attacker is null.");
        if (mainTarget == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealSplash failed: mainTarget is null. attacker={attacker.CharacterKey}.");
        if (weaponData == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealSplash failed: weaponData is null. attacker={attacker.CharacterKey}.");

        if (weaponData.SplashRadius <= Fix64.Zero)
        {
            DealSingle(attacker, mainTarget, weaponData);
            return;
        }

        var targets = new List<IEntityContext> { mainTarget };
        float radius = DistanceUnitConverter.ConvertToWorldFloat(weaponData.SplashRadius);
        foreach (var target in CollectEnemiesInCircle(attacker, mainTarget.Position, radius, mainTarget))
            targets.Add(target);

        using (DamageHelper.BeginAttackHitSequence(attacker, targets.Count))
        {
            for (int i = 0; i < targets.Count; i++)
                DealSingle(attacker, targets[i], weaponData);
        }
    }

    public static void DealSelfAoE(IEntityContext attacker, IEntityContext mainTarget, WeaponData weaponData)
    {
        if (attacker == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.DealSelfAoE failed: attacker is null.");
        if (mainTarget == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealSelfAoE failed: mainTarget is null. attacker={attacker.CharacterKey}.");
        if (weaponData == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealSelfAoE failed: weaponData is null. attacker={attacker.CharacterKey}.");

        var targets = new List<IEntityContext> { mainTarget };
        float radius = DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range);
        foreach (var target in CollectEnemiesInCircle(attacker, attacker.Position, radius, mainTarget))
            targets.Add(target);

        using (DamageHelper.BeginAttackHitSequence(attacker, targets.Count))
        {
            for (int i = 0; i < targets.Count; i++)
                DealSingle(attacker, targets[i], weaponData);
        }
    }

    public static void DealCleave(IEntityContext attacker, IEntityContext mainTarget, WeaponData weaponData)
    {
        if (attacker == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.DealCleave failed: attacker is null.");
        if (mainTarget == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealCleave failed: mainTarget is null. attacker={attacker.CharacterKey}.");
        if (weaponData == null)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealCleave failed: weaponData is null. attacker={attacker.CharacterKey}.");

        Vector3 forward = mainTarget.Position - attacker.Position;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = attacker.Rotation * Vector3.forward;
        forward.y = 0f;
        forward.Normalize();

        float range = DistanceUnitConverter.ConvertToWorldFloat(weaponData.SplitDist > Fix64.Zero ? weaponData.SplitDist : weaponData.Range);
        float halfAngle = Mathf.Max(0f, (float)weaponData.SplitAngle) * 0.5f * Mathf.Deg2Rad;
        float tanHalfAngle = Mathf.Tan(halfAngle);
        float baseRadius = GetCollisionRadiusWorld(attacker);

        var targets = new List<IEntityContext> { mainTarget };
        foreach (var target in CollectEnemiesInRoundedCone(attacker, forward, range, tanHalfAngle, baseRadius, mainTarget))
            targets.Add(target);

        using (DamageHelper.BeginAttackHitSequence(attacker, targets.Count))
        {
            for (int i = 0; i < targets.Count; i++)
                DealSingle(attacker, targets[i], weaponData);
        }
    }

    private static void DealSingle(IEntityContext attacker, IEntityContext target, WeaponData weaponData)
    {
        if (!target.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(attacker, target))
            return;

        var damage = new Damage(attacker as ITargetable, weaponData.Damage, HealthModifyType.reduce);
        DamageHelper.DoDamage(target as ITargetable, damage, attacker);
    }

    private static IEnumerable<IEntityContext> CollectEnemiesInCircle(IEntityContext attacker, Vector3 center, float radius, IEntityContext excludedTarget)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.CollectEnemiesInCircle failed: EntityRegistry.AllEntities is null.");

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || candidate == excludedTarget)
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(attacker, candidate))
                continue;

            float reach = radius + GetCollisionRadiusWorld(candidate);
            if (HorizontalDistance(center, candidate.Position) <= reach)
                yield return candidate;
        }
    }

    private static IEnumerable<IEntityContext> CollectEnemiesInRoundedCone(
        IEntityContext attacker,
        Vector3 forward,
        float range,
        float tanHalfAngle,
        float baseRadius,
        IEntityContext excludedTarget)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.CollectEnemiesInRoundedCone failed: EntityRegistry.AllEntities is null.");

        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        Vector3 origin = attacker.Position;

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || candidate == excludedTarget)
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(attacker, candidate))
                continue;

            Vector3 offset = candidate.Position - origin;
            offset.y = 0f;
            float candidateRadius = GetCollisionRadiusWorld(candidate);
            float forwardDist = Vector3.Dot(offset, forward);
            if (forwardDist + candidateRadius < 0f || forwardDist - candidateRadius > range)
                continue;

            float sideDist = Mathf.Abs(Vector3.Dot(offset, right));
            float halfWidth = baseRadius + Mathf.Max(0f, forwardDist) * tanHalfAngle + candidateRadius;
            if (sideDist <= halfWidth)
                yield return candidate;
        }
    }

    private static float GetCollisionRadiusWorld(IEntityContext entity)
    {
        if (entity == null)
            return 0f;

        Fix64 tableRadius = entity.GetProperty(CreatureMainProperty.CollisionRadius);
        if (tableRadius > Fix64.Zero)
            return DistanceUnitConverter.ConvertToWorldFloat(tableRadius);

        if (entity is Component component && TryGetBoundsRadius(component, out float boundsRadius))
            return boundsRadius;

        return 0f;
    }

    private static bool TryGetBoundsRadius(Component component, out float radius)
    {
        radius = 0f;
        var colliders = component.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (!hasBounds)
            return false;

        radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return radius > 0f;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}

public static class HealingWeaponEffect
{
    public static void Execute(IEntityContext healer, IEntityContext target, WeaponData weaponData)
    {
        if (healer == null)
            throw new System.InvalidOperationException("HealingWeaponEffect.Execute failed: healer is null.");
        if (target == null)
            throw new System.InvalidOperationException($"HealingWeaponEffect.Execute failed: target is null. healer={healer.CharacterKey}.");
        if (weaponData == null)
            throw new System.InvalidOperationException($"HealingWeaponEffect.Execute failed: weaponData is null. healer={healer.CharacterKey}.");

        if (weaponData.Type == WeaponType.HealProjectile && weaponData.SplashRadius > Fix64.Zero)
        {
            HealSingle(healer, target, weaponData.Atk);
            float radius = DistanceUnitConverter.ConvertToWorldFloat(weaponData.SplashRadius);
            foreach (var ally in CollectAlliesInCircle(healer, target.Position, radius, target))
            {
                HealSingle(healer, ally, weaponData.Atk);
            }

            return;
        }

        HealSingle(healer, target, weaponData.Atk);
    }

    private static void HealSingle(IEntityContext healer, IEntityContext target, Fix64 amount)
    {
        if (amount <= Fix64.Zero)
            return;
        if (!WeaponTargetRules.IsValidHealTarget(healer, target, requireDamaged: false))
            return;
        if (!(target is GeneralCreature creature))
            throw new System.InvalidOperationException($"HealingWeaponEffect.HealSingle failed: target is not GeneralCreature. healer={healer.CharacterKey}, target={target.CharacterKey}.");

        creature.Heal(amount);
    }

    private static IEnumerable<IEntityContext> CollectAlliesInCircle(IEntityContext healer, Vector3 center, float radius, IEntityContext excludedTarget)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("HealingWeaponEffect.CollectAlliesInCircle failed: EntityRegistry.AllEntities is null.");

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || candidate == excludedTarget)
                continue;
            if (!WeaponTargetRules.IsValidHealTarget(healer, candidate, requireDamaged: false))
                continue;

            float reach = radius + AreaWeaponDamageQuery.GetCollisionRadiusWorld(candidate);
            if (AreaWeaponDamageQuery.HorizontalDistance(center, candidate.Position) <= reach)
                yield return candidate;
        }
    }
}

public static class WeaponTargetRules
{
    public static bool IsHealingWeapon(WeaponType weaponType)
    {
        return weaponType == WeaponType.HealMelee || weaponType == WeaponType.HealProjectile;
    }

    public static bool IsProjectileLikeWeapon(WeaponType weaponType)
    {
        return weaponType == WeaponType.Projectile
               || weaponType == WeaponType.HealProjectile
               || weaponType == WeaponType.InstantRanged
               || weaponType == WeaponType.Special;
    }

    public static bool IsValidTargetForCurrentWeapon(IEntityContext attacker, IEntityContext target)
    {
        WeaponType weaponType = attacker?.WeaponComp?.Data != null ? attacker.WeaponComp.Data.Type : WeaponType.None;
        return IsValidTargetForWeapon(attacker, target, weaponType);
    }

    public static bool IsValidTargetForWeapon(IEntityContext attacker, IEntityContext target, WeaponType weaponType)
    {
        if (IsHealingWeapon(weaponType))
            return IsValidHealTarget(attacker, target, requireDamaged: true);

        return target != null
               && target.IsAttackTargetable()
               && EntityCombatTeamHelper.IsEnemy(attacker, target);
    }

    public static bool IsValidHealTarget(IEntityContext healer, IEntityContext target, bool requireDamaged)
    {
        if (healer == null || target == null)
            return false;
        if (!target.IsHealTargetable() && requireDamaged)
            return false;
        if (!requireDamaged)
        {
            if (target.IsDestroyed() || !target.Alive)
                return false;
            if (target is SoldierEntity se && se.IsGhostState)
                return false;
        }

        return EntityCombatTeamHelper.IsAlly(healer, target);
    }
}

public static class AreaWeaponDamageQuery
{
    public static float GetCollisionRadiusWorld(IEntityContext entity)
    {
        if (entity == null)
            return 0f;

        Fix64 tableRadius = entity.GetProperty(CreatureMainProperty.CollisionRadius);
        if (tableRadius > Fix64.Zero)
            return DistanceUnitConverter.ConvertToWorldFloat(tableRadius);

        if (entity is Component component && TryGetBoundsRadius(component, out float boundsRadius))
            return boundsRadius;

        return 0f;
    }

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static bool TryGetBoundsRadius(Component component, out float radius)
    {
        radius = 0f;
        var colliders = component.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (!hasBounds)
            return false;

        radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return radius > 0f;
    }
}
