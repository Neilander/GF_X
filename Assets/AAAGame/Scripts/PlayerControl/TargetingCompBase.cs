using System;

public abstract class TargetingCompBase
{
    protected static bool IsValidEnemyTarget(IEntityContext owner, IEntityContext target)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        return target != null
               && target.IsAttackTargetable()
               && EntityCombatTeamHelper.IsEnemy(owner, target);
    }

    protected static bool IsVisibleEnemyTarget(IEntityContext owner, IEntityContext target)
    {
        return IsValidEnemyTarget(owner, target)
               && LogicFactionVisionService.IsEntityVisibleToSide(owner.Side, target);
    }

    protected static Fix64 GetRequiredAttackRange(IEntityContext owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (owner.WeaponComp == null)
            throw new InvalidOperationException($"Targeting requires a weapon component. entity={owner.LogicEntityId.Value}.");
        return owner.WeaponComp.AttackRange;
    }

}
