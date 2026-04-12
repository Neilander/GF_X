using UnityEngine;

public static class EntityContextExtensions
{
    /// <summary>
    /// 检查 IEntityContext 背后的 Unity 对象是否已被销毁。
    /// 接口变量不走 Unity 的 == 重载，需要先转为 Object。
    /// </summary>
    public static bool IsDestroyed(this IEntityContext ctx)
    {
        return ctx == null || (ctx is Object obj && obj == null);
    }

    /// <summary>
    /// 统一攻击目标判定：死亡、销毁、以及无敌建筑都不可作为攻击目标。
    /// </summary>
    public static bool IsAttackTargetable(this IEntityContext ctx)
    {
        if (ctx.IsDestroyed() || !ctx.Alive)
            return false;

        if (ctx is BuildingEntity building && (building.IsLv0Invincible || building.IsPhaseProtected || building.IsDisabled))
            return false;

        return true;
    }

    /// <summary>
    /// 计算到目标可命中外轮廓的距离，优先 HurtBox/Collider，回退到中心点距离。
    /// </summary>
    public static float DistanceToTargetSurface(this IEntityContext self, IEntityContext target)
    {
        if (self == null || target == null)
            return float.PositiveInfinity;

        Vector3 from = self.Position;
        if (TryGetTargetClosestPoint(target, from, out Vector3 closestPoint))
            return Vector3.Distance(from, closestPoint);

        return Vector3.Distance(from, target.Position);
    }

    private static bool TryGetTargetClosestPoint(IEntityContext target, Vector3 origin, out Vector3 closestPoint)
    {
        closestPoint = default;

        if (!(target is Component targetComponent) || targetComponent == null)
            return false;

        if (target is BuildingEntity && TryGetClosestPointFromNonTriggerCollider(targetComponent, origin, out closestPoint))
            return true;

        var hurtBox = targetComponent.GetComponentInChildren<HurtBox>();
        if (hurtBox != null && hurtBox.TryGetComponent<Collider>(out var hurtCollider) && hurtCollider.enabled)
        {
            closestPoint = hurtCollider.ClosestPoint(origin);
            return true;
        }

        var rootCollider = targetComponent.GetComponent<Collider>();
        if (rootCollider != null && rootCollider.enabled)
        {
            closestPoint = rootCollider.ClosestPoint(origin);
            return true;
        }

        var childCollider = targetComponent.GetComponentInChildren<Collider>();
        if (childCollider != null && childCollider.enabled)
        {
            closestPoint = childCollider.ClosestPoint(origin);
            return true;
        }

        var renderer = targetComponent.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            closestPoint = renderer.bounds.ClosestPoint(origin);
            return true;
        }

        return false;
    }

    private static bool TryGetClosestPointFromNonTriggerCollider(Component targetComponent, Vector3 origin, out Vector3 closestPoint)
    {
        closestPoint = default;

        var colliders = targetComponent.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return false;

        float bestDistanceSqr = float.PositiveInfinity;
        bool hasResult = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            Vector3 point = collider.ClosestPoint(origin);
            float d2 = (point - origin).sqrMagnitude;
            if (d2 < bestDistanceSqr)
            {
                bestDistanceSqr = d2;
                closestPoint = point;
                hasResult = true;
            }
        }

        return hasResult;
    }
}
