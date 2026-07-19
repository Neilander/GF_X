using UnityEngine;

/// <summary>
/// 挂在障碍物上，自动向 GroupMoveManager 注册/注销。
/// 支持任意 Collider（BoxCollider 用 AABB，其他用球形包围）。
/// </summary>
public class GroupMoveObstacle : MonoBehaviour
{
    [SerializeField] private int _obstacleId;

    private void Start()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            throw new System.InvalidOperationException($"GroupMoveObstacle.Start failed: {name} has no Collider.");
        }
        if (_obstacleId <= 0)
            throw new System.InvalidOperationException($"GroupMoveObstacle.Start failed: {name} requires a positive authored obstacle id.");

        GroupMoveManager.Instance.RegisterColliderObstacle(_obstacleId, col);
    }

    private void OnDestroy()
    {
        if (!GroupMoveManager.HasInstance) return;
        GroupMoveManager.Instance.UnregisterObstacle(_obstacleId);
    }
}
