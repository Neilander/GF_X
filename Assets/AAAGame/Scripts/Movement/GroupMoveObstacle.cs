using UnityEngine;

/// <summary>
/// 挂在障碍物上，自动向 GroupMoveManager 注册/注销。
/// 支持任意 Collider（BoxCollider 用 AABB，其他用球形包围）。
/// </summary>
public class GroupMoveObstacle : MonoBehaviour
{
    private int _obstacleId;

    private void Start()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning($"[GroupMoveObstacle] {name} 没有 Collider，跳过注册");
            return;
        }

        _obstacleId = col.GetInstanceID();

        if (col is BoxCollider)
        {
            GroupMoveManager.Instance.RegisterBoxObstacle(col);
        }
        else
        {
            var bounds = col.bounds;
            GroupMoveManager.Instance.RegisterCircleObstacle(
                _obstacleId, bounds.center, bounds.extents.magnitude);
        }
    }

    private void OnDestroy()
    {
        if (!GroupMoveManager.HasInstance) return;
        GroupMoveManager.Instance.UnregisterObstacle(_obstacleId);
    }
}
