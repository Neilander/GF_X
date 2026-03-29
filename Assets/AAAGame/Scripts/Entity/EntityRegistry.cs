using System.Collections.Generic;

/// <summary>
/// 全局实体注册表：所有活着的 IEntityContext 实体在这里注册/注销。
/// SoldierAIBrain 等需要查找玩家和邻居的组件从这里获取数据。
/// 遵循拆解+调用原则：注册表只存数据，不做逻辑。
/// </summary>
public static class EntityRegistry
{
    private static readonly List<IEntityContext> _entities = new List<IEntityContext>();
    private static IEntityContext _player;

    public static IList<IEntityContext> AllEntities => _entities;
    public static IEntityContext Player => _player;

    public static void Register(IEntityContext entity)
    {
        if (!_entities.Contains(entity))
            _entities.Add(entity);
    }

    public static void RegisterAsPlayer(IEntityContext entity)
    {
        _player = entity;
        Register(entity);
    }

    public static void Unregister(IEntityContext entity)
    {
        _entities.Remove(entity);
        if (_player == entity)
            _player = null;
    }

    /// <summary>
    /// 获取离指定位置最近的领袖。目前只返回 Player，后续扩展多领袖时改这里。
    /// </summary>
    public static IEntityContext GetClosestLeader(UnityEngine.Vector3 position)
    {
        return _player;
    }

    public static void Clear()
    {
        _entities.Clear();
        _player = null;
    }
}
