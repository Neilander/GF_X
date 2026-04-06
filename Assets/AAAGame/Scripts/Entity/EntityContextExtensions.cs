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
}
