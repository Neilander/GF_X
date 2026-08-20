using UnityEngine;

public static class EntityContextExtensions
{
    public static bool TryGetLogicBuilding(
        this IEntityContext context,
        out IBuildingLogicContext building)
    {
        building = null;
        if (context == null || !context.IsBuildingEntity)
            return false;
        building = context as IBuildingLogicContext
            ?? throw new System.InvalidOperationException(
                $"Entity {context.LogicEntityId.Value} declares building identity without IBuildingLogicContext.");
        if (building.BuildingData == null)
        {
            throw new System.InvalidOperationException(
                $"Building entity {context.LogicEntityId.Value} has no BuildingData.");
        }
        return true;
    }

    public static bool IsLogicBuilding(this IEntityContext context)
    {
        return context.TryGetLogicBuilding(out _);
    }

    public static bool TryGetLogicHero(
        this IEntityContext context,
        out IHeroLogicContext hero)
    {
        hero = null;
        if (context == null || !context.IsHeroEntity)
            return false;
        hero = context as IHeroLogicContext
            ?? throw new System.InvalidOperationException(
                $"Entity {context.LogicEntityId.Value} declares hero identity without IHeroLogicContext.");
        return true;
    }

    public static bool IsDestroyed(this IEntityContext ctx)
    {
        if (ctx == null)
            return true;
        if (ctx is Object)
        {
            throw new System.InvalidOperationException(
                "Logic entity lifetime cannot be resolved from a Unity Object view.");
        }
        if (!ctx.LogicEntityId.IsValid)
            throw new System.InvalidOperationException("Logic-world entity has an invalid logic id.");

        return !EntityRegistry.TryGet(ctx.LogicEntityId, out IEntityContext registered)
               || !ReferenceEquals(registered, ctx);
    }

    public static bool IsRegisteredInLogicWorld(this IEntityContext ctx)
    {
        if (ctx.IsDestroyed())
            return false;

        return true;
    }

    /// <summary>
    /// 统一攻击目标判定：死亡、销毁、无敌、幽灵态都不可作为攻击目标。
    /// </summary>
    public static bool IsAttackTargetable(this IEntityContext ctx)
    {
        if (ctx.IsDestroyed() || !ctx.Alive)
            return false;

        if (ctx.HasInvincibleBuff())
            return false;

        // 幽灵态（玩家死亡进入的复活等待状态）：Alive=true 但不可被攻击，避免敌人一直锁着它打
        if (ctx.TryGetLogicHero(out IHeroLogicContext hero) && hero.IsGhostState)
            return false;

        // 检查是否处于战斗阶段（进攻阶段）
        return true;
    }

    public static bool IsHealTargetable(this IEntityContext ctx)
    {
        if (ctx.IsDestroyed() || !ctx.Alive)
            return false;

        if (ctx.TryGetLogicHero(out IHeroLogicContext hero) && hero.IsGhostState)
            return false;

        GamePhase currentPhase = LogicPhaseCommandService.GetRequiredCurrentPhase();
        if (currentPhase != GamePhase.Invade && currentPhase != GamePhase.Defend)
            return false;

        return ctx.NeedsHealing();
    }

    public static bool NeedsHealing(this IEntityContext ctx)
    {
        if (ctx?.CreatureProperties == null)
            return false;

        Fix64 max = ctx.CreatureProperties.GetProperty(CreatureMainProperty.Health);
        if (max <= Fix64.Zero)
            return false;

        return ctx.HealthValue < max;
    }

    public static float HealthRatio(this IEntityContext ctx)
    {
        return (float)HealthRatioFixed(ctx);
    }

    public static Fix64 HealthRatioFixed(this IEntityContext ctx)
    {
        if (ctx?.CreatureProperties == null)
            return Fix64.One;

        Fix64 max = ctx.CreatureProperties.GetProperty(CreatureMainProperty.Health);
        if (max <= Fix64.Zero)
            return Fix64.One;

        return Fix64.Clamp(ctx.HealthValue / max, Fix64.Zero, Fix64.One);
    }

    public static bool HasInvincibleBuff(this IEntityContext ctx)
    {
        return ctx?.BuffComp != null && ctx.BuffComp.HasBuff(InvincibleStateBuff.BuffId);
    }

    /// <summary>
    /// 计算到目标逻辑战斗形状外轮廓的 XZ 平面距离。
    /// </summary>
    public static float DistanceToTargetSurface(this IEntityContext self, IEntityContext target)
    {
        if (self == null || target == null)
            return float.PositiveInfinity;

        return (float)LogicTargetGeometry.DistanceToSurface(target, self.PositionFixed);
    }

    public static bool TryGetTargetClosestPoint(this IEntityContext target, Vector3 origin, out Vector3 closestPoint)
    {
        closestPoint = default;

        if (target == null)
            return false;

        FixVector2 closest = LogicTargetGeometry.ClosestPoint(target,
            new FixVector2((Fix64)origin.x, (Fix64)origin.z));
        closestPoint = new Vector3((float)closest.x, origin.y, (float)closest.y);
        return true;
    }

}
