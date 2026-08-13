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
        if (target == null)
            throw new System.ArgumentNullException(nameof(target));
        if (damage == null)
            throw new System.ArgumentNullException(nameof(damage));
        if (!target.Alive)
            return;

        AdvanceHitIndex(attacker);

        int hitIndex = GetCurrentAttackHitIndex(attacker);
        int totalHits = GetCurrentAttackTotalHits(attacker);
        if (LogicDamageEventService.IsCollecting)
        {
            LogicDamageEventService.SubmitDamage(target, damage, attacker, hitIndex, totalHits);
            return;
        }

        ApplyDamage(target, damage.amount, damage.modType, attacker);
    }

    public static void DoDirectDamage(
        IEntityContext target,
        Fix64 amount,
        HealthModifyType modifyType,
        IEntityContext attacker = null)
    {
        if (target == null)
            throw new System.ArgumentNullException(nameof(target));
        if (amount < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(amount));
        if (!target.Alive || amount == Fix64.Zero)
            return;

        if (LogicDamageEventService.IsCollecting)
        {
            LogicDamageEventService.SubmitDirectDamage(target, amount, modifyType, attacker);
            return;
        }

        target.TakeDamage(amount, modifyType, attacker);
    }

    internal static void ApplyQueuedDamage(
        ITargetable target,
        Fix64 amount,
        HealthModifyType modType,
        IEntityContext attacker,
        int hitIndex,
        int totalHits)
    {
        if (target == null)
            throw new System.ArgumentNullException(nameof(target));
        if (hitIndex <= 0 || totalHits <= 0 || hitIndex > totalHits)
            throw new System.ArgumentOutOfRangeException(nameof(hitIndex));
        if (!target.Alive)
            return;

        if (totalHits <= 1)
        {
            ApplyDamage(target, amount, modType, attacker);
            return;
        }

        s_HitSequences ??= new Stack<AttackHitSequence>();
        s_HitSequences.Push(new AttackHitSequence
        {
            Attacker = attacker,
            TotalHits = totalHits,
            CurrentHitIndex = hitIndex,
        });
        try
        {
            ApplyDamage(target, amount, modType, attacker);
        }
        finally
        {
            s_HitSequences.Pop();
        }
    }

    private static void ApplyDamage(
        ITargetable target,
        Fix64 amount,
        HealthModifyType modType,
        IEntityContext attacker)
    {
        if (!target.Alive)
            return;

        Fix64 finalAmount = amount;

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

        target.TakeDamage(finalAmount, modType, attacker);
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
                throw new System.InvalidOperationException("DamageHelper attack hit sequence stack is empty.");

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
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(weaponData.SplashRadius);
        FixVector2 center = LogicEntityFrameSnapshotService.GetRequiredPosition(mainTarget);
        foreach (var target in CollectEnemiesInCircle(attacker, center, radius, mainTarget))
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
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(weaponData.Range);
        FixVector2 center = LogicEntityFrameSnapshotService.GetRequiredPosition(attacker);
        foreach (var target in CollectEnemiesInCircle(attacker, center, radius, mainTarget))
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

        FixVector2 origin = LogicEntityFrameSnapshotService.GetRequiredPosition(attacker);
        FixVector2 forward = LogicEntityFrameSnapshotService.GetRequiredPosition(mainTarget) - origin;
        if (FixVector2.SqrMagnitude(forward) == Fix64.Zero)
            forward = LogicEntityFrameSnapshotService.GetRequiredForward(attacker);
        forward = forward.GetNormalized();

        Fix64 range = DistanceUnitConverter.ConvertToWorld(
            weaponData.SplitDist > Fix64.Zero ? weaponData.SplitDist : weaponData.Range);
        if (weaponData.SplitAngle < Fix64.Zero || weaponData.SplitAngle >= (Fix64)180)
            throw new System.InvalidOperationException($"AreaWeaponDamage.DealCleave requires SplitAngle in [0, 180). actual={weaponData.SplitAngle}.");
        Fix64 halfAngle = weaponData.SplitAngle * Fix64.FromRaw(2048) * Fix64.PIOver180;
        Fix64 tanHalfAngle = Fix64.Tan(halfAngle);
        Fix64 baseRadius = AreaWeaponDamageQuery.GetRequiredRadialExtent(attacker);

        var targets = new List<IEntityContext> { mainTarget };
        foreach (var target in CollectEnemiesInRoundedCone(attacker, origin, forward, range, tanHalfAngle, baseRadius, mainTarget))
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

    private static IEnumerable<IEntityContext> CollectEnemiesInCircle(IEntityContext attacker, FixVector2 center, Fix64 radius, IEntityContext excludedTarget)
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

            if (AreaWeaponDamageQuery.IsWithinCircle(center, candidate, radius))
                yield return candidate;
        }
    }

    private static IEnumerable<IEntityContext> CollectEnemiesInRoundedCone(
        IEntityContext attacker,
        FixVector2 origin,
        FixVector2 forward,
        Fix64 range,
        Fix64 tanHalfAngle,
        Fix64 baseRadius,
        IEntityContext excludedTarget)
    {
        var all = EntityRegistry.AllEntities;
        if (all == null)
            throw new System.InvalidOperationException("AreaWeaponDamage.CollectEnemiesInRoundedCone failed: EntityRegistry.AllEntities is null.");

        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext candidate = all[i];
            if (candidate == null || candidate == excludedTarget)
                continue;
            if (!candidate.IsAttackTargetable())
                continue;
            if (!EntityCombatTeamHelper.IsEnemy(attacker, candidate))
                continue;

            if (AreaWeaponDamageQuery.IsWithinRoundedCone(
                    origin,
                    forward,
                    range,
                    tanHalfAngle,
                    baseRadius,
                    candidate))
                yield return candidate;
        }
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
            Fix64 radius = DistanceUnitConverter.ConvertToWorld(weaponData.SplashRadius);
            FixVector2 center = LogicEntityFrameSnapshotService.GetRequiredPosition(target);
            foreach (var ally in CollectAlliesInCircle(healer, center, radius, target))
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
        if (LogicDamageEventService.IsCollecting)
            LogicDamageEventService.SubmitHeal(healer, target, amount);
        else
            target.Heal(amount);
    }

    private static IEnumerable<IEntityContext> CollectAlliesInCircle(IEntityContext healer, FixVector2 center, Fix64 radius, IEntityContext excludedTarget)
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

            if (AreaWeaponDamageQuery.IsWithinCircle(center, candidate, radius))
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

    public static bool UsesProjectileSimulation(WeaponType weaponType)
    {
        return weaponType == WeaponType.Projectile
               || weaponType == WeaponType.HealProjectile;
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
            if (target is IHeroLogicContext se && se.IsGhostState)
                return false;
        }

        return EntityCombatTeamHelper.IsAlly(healer, target);
    }
}

public static class AreaWeaponDamageQuery
{
    public static bool IsWithinCircle(IEntityContext centerEntity, IEntityContext candidate, Fix64 radius)
    {
        if (centerEntity == null)
            throw new System.ArgumentNullException(nameof(centerEntity));
        return IsWithinCircle(LogicEntityFrameSnapshotService.GetRequiredPosition(centerEntity), candidate, radius);
    }

    public static bool IsWithinCircle(FixVector2 center, IEntityContext candidate, Fix64 radius)
    {
        if (candidate == null)
            throw new System.ArgumentNullException(nameof(candidate));
        if (radius < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(radius));
        LogicCombatShape shape = LogicEntityFrameSnapshotService.GetRequiredCurrent(candidate).CombatShape;
        return IsWithinCircle(center, shape, radius);
    }

    public static bool IsWithinCircle(FixVector2 center, LogicCombatShape shape, Fix64 radius)
    {
        if (radius < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(radius));
        return shape.DistanceToSurface(center) <= radius;
    }

    public static Fix64 GetRequiredRadialExtent(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        LogicCombatShape shape = LogicEntityFrameSnapshotService.GetRequiredCurrent(entity).CombatShape;
        switch (shape.Kind)
        {
            case LogicCombatShapeKind.Circle:
                return shape.Radius;
            case LogicCombatShapeKind.AxisAlignedBox:
                return Fix64.Max(shape.HalfExtents.x, shape.HalfExtents.y);
            default:
                throw new System.ArgumentOutOfRangeException(nameof(shape.Kind), shape.Kind, "Unknown combat shape kind.");
        }
    }

    public static bool IsWithinRoundedCone(
        FixVector2 origin,
        FixVector2 forward,
        Fix64 range,
        Fix64 tanHalfAngle,
        Fix64 baseRadius,
        IEntityContext candidate)
    {
        if (candidate == null)
            throw new System.ArgumentNullException(nameof(candidate));
        LogicCombatShape shape = LogicEntityFrameSnapshotService.GetRequiredCurrent(candidate).CombatShape;
        return IsWithinRoundedCone(origin, forward, range, tanHalfAngle, baseRadius, shape);
    }

    public static bool IsWithinRoundedCone(
        FixVector2 origin,
        FixVector2 forward,
        Fix64 range,
        Fix64 tanHalfAngle,
        Fix64 baseRadius,
        LogicCombatShape shape)
    {
        if (range < Fix64.Zero || tanHalfAngle < Fix64.Zero || baseRadius < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(range), "Rounded cone values must be non-negative.");
        FixVector2 normalizedForward = forward.GetNormalized();
        if (FixVector2.SqrMagnitude(normalizedForward) == Fix64.Zero)
            throw new System.ArgumentException("Rounded cone forward must be non-zero.", nameof(forward));
        FixVector2 right = new FixVector2(normalizedForward.y, -normalizedForward.x);
        FixVector2 offset = shape.Center - origin;
        Fix64 forwardDistance = FixVector2.Dot(offset, normalizedForward);
        Fix64 forwardExtent = GetSupportExtent(shape, normalizedForward);
        if (forwardDistance + forwardExtent < Fix64.Zero || forwardDistance - forwardExtent > range)
            return false;

        Fix64 sideDistance = Fix64.Abs(FixVector2.Dot(offset, right));
        Fix64 sideExtent = GetSupportExtent(shape, right);
        Fix64 coneDistance = Fix64.Max(Fix64.Zero, forwardDistance);
        Fix64 halfWidth = baseRadius
                          + coneDistance * tanHalfAngle
                          + sideExtent
                          + forwardExtent * tanHalfAngle;
        return sideDistance <= halfWidth;
    }

    private static Fix64 GetSupportExtent(LogicCombatShape shape, FixVector2 axis)
    {
        switch (shape.Kind)
        {
            case LogicCombatShapeKind.Circle:
                return shape.Radius;
            case LogicCombatShapeKind.AxisAlignedBox:
                return Fix64.Abs(axis.x) * shape.HalfExtents.x
                       + Fix64.Abs(axis.y) * shape.HalfExtents.y;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(shape.Kind), shape.Kind, "Unknown combat shape kind.");
        }
    }
}
