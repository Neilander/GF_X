using UnityEngine;

public static class EntityContextExtensions
{
    public static bool TryGetLogicBuilding(
        this IEntityContext context,
        out IBuildingLogicContext building)
    {
        building = context as IBuildingLogicContext;
        if (building == null || building.BuildingData == null)
        {
            building = null;
            return false;
        }
        return true;
    }

    public static bool IsLogicBuilding(this IEntityContext context)
    {
        return context.TryGetLogicBuilding(out _);
    }

    /// <summary>
    /// 检查 IEntityContext 背后的 Unity 对象是否已被销毁。
    /// 接口变量不走 Unity 的 == 重载，需要先转为 Object。
    /// </summary>
    public static bool IsDestroyed(this IEntityContext ctx)
    {
        return ctx == null || (ctx is Object obj && obj == null);
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
        if (ctx is IHeroLogicContext se && se.IsGhostState)
            return false;

        // 检查是否处于战斗阶段（进攻阶段）
        int currentPhase = InGameDataModel.GetValue(IngameValueType.Phase);
        if (currentPhase != (int)GamePhase.Invade && currentPhase != (int)GamePhase.Defend)
            return false;

        return true;
    }

    public static bool IsHealTargetable(this IEntityContext ctx)
    {
        if (ctx.IsDestroyed() || !ctx.Alive)
            return false;

        if (ctx is IHeroLogicContext se && se.IsGhostState)
            return false;

        int currentPhase = InGameDataModel.GetValue(IngameValueType.Phase);
        if (currentPhase != (int)GamePhase.Invade && currentPhase != (int)GamePhase.Defend)
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

        return (float)target.CombatShape.DistanceToSurface(self.PositionFixed);
    }

    public static bool TryGetTargetClosestPoint(this IEntityContext target, Vector3 origin, out Vector3 closestPoint)
    {
        closestPoint = default;

        if (target == null)
            return false;

        FixVector2 closest = target.CombatShape.ClosestPoint(
            new FixVector2((Fix64)origin.x, (Fix64)origin.z));
        closestPoint = new Vector3((float)closest.x, origin.y, (float)closest.y);
        return true;
    }

}
