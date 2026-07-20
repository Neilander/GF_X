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
    public static event System.Action Changed;

    public static void Register(IEntityContext entity)
    {
        if (entity == null)
            throw new System.ArgumentNullException(nameof(entity));
        if (!entity.LogicEntityId.IsValid)
            throw new System.InvalidOperationException("EntityRegistry.Register failed: entity has an invalid logic id.");

        int low = 0;
        int high = _entities.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            IEntityContext current = _entities[middle];
            int comparison = current.LogicEntityId.CompareTo(entity.LogicEntityId);
            if (comparison == 0)
            {
                if (ReferenceEquals(current, entity))
                    return;

                throw new System.InvalidOperationException($"EntityRegistry.Register failed: duplicate logic entity id {entity.LogicEntityId.Value}.");
            }

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        _entities.Insert(low, entity);
        Changed?.Invoke();
    }

    public static void RegisterAsPlayer(IEntityContext entity)
    {
        _player = entity;
        Register(entity);
    }

    public static bool TryGet(LogicEntityId entityId, out IEntityContext entity)
    {
        if (!entityId.IsValid)
            throw new System.ArgumentException("Entity id must be valid.", nameof(entityId));

        int low = 0;
        int high = _entities.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            IEntityContext current = _entities[middle];
            int comparison = current.LogicEntityId.CompareTo(entityId);
            if (comparison == 0)
            {
                entity = current;
                return true;
            }

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        entity = null;
        return false;
    }

    public static void Unregister(IEntityContext entity)
    {
        bool removed = _entities.Remove(entity);
        if (_player == entity)
            _player = null;
        if (removed)
            Changed?.Invoke();
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
        bool hadEntities = _entities.Count > 0 || _player != null;
        _entities.Clear();
        _player = null;
        if (hadEntities)
            Changed?.Invoke();
    }
}
