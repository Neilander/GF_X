using UnityEngine;
using System.Collections;

/// <summary>
/// 敌人生成器：测试用，挂在场景物体上 Start 时生成一簇敌人
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("生成配置")]
    [Tooltip("单位类型")]
    public UnitType unitType = UnitType.Unit_Intern;

    [Tooltip("生成数量")]
    [Range(1, 100)]
    public int count = 5;

    [Tooltip("生成半径")]
    public float radius = 5f;

    [Tooltip("最小间距")]
    public float minDistance = 2f;

    [Header("阵营")]
    public SideType side = SideType.EnemySide;
    public BrainType brainType = BrainType.SoldierAI;

    private void Start()
    {
        StartCoroutine(SpawnEnemiesAfterDelay());
    }

    private IEnumerator SpawnEnemiesAfterDelay()
    {
        yield return null; // 等待一帧，确保事件已订阅
        Debug.Log($"EnemySpawner: Spawning {count} enemies at {transform.position}, unitType={unitType}, side={side}");
        ClusterSpawnSystem.SpawnCluster(transform.position, count, radius, minDistance, unitType, side, brainType);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = side == SideType.EnemySide ? Color.red : Color.green;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
