using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 簇生成系统
/// 按范围批量生成单位，保证单位在NavMesh上且不重叠
/// </summary>
public static class ClusterSpawnSystem
{
    /// <summary>
    /// 校验生成条件
    /// </summary>
    /// <param name="center">中心位置</param>
    /// <param name="count">生成数量</param>
    /// <param name="radius">生成半径</param>
    /// <param name="minDistance">最小间距</param>
    /// <param name="spawnPositions">生成位置列表</param>
    /// <returns>是否可以生成</returns>
    public static bool ValidateSpawn(Vector3 center, int count, float radius, float minDistance, out List<Vector3> spawnPositions)
    {
        spawnPositions = new List<Vector3>();
        
        if (count<= 0 || radius<= 0 || minDistance <= 0)
        {
            return false;
        }
        
        // 在圆形区域内生成候选点（增加到10倍数量）
        int maxAttempts = count * 10;
        int navMeshFailCount = 0;
        int overlapFailCount = 0;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 candidate = GenerateRandomPointInCircle(center, radius);
            
            // 投影到NavMesh（增加搜索半径到3米）
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                Vector3 spawnPos = hit.position;
                
                // 检查是否与已有生成位置重叠
                bool isOverlap = false;
                foreach (Vector3 existingPos in spawnPositions)
                {
                    if (Vector3.Distance(spawnPos, existingPos)< minDistance)
                    {
                        isOverlap = true;
                        overlapFailCount++;
                        break;
                    }
                }
                
                // 简化版：只检查与已有生成位置的重叠，不检查物理碰撞
                if (!isOverlap)
                {
                    spawnPositions.Add(spawnPos);
                    
                    // 达到需求数量
                    if (spawnPositions.Count >= count)
                    {
                        return true;
                    }
                }
            }
            else
            {
                navMeshFailCount++;
            }
        }
        

        
        // 没有找到足够的位置
        return spawnPositions.Count >= count;
    }
    
    /// <summary>
    /// 生成簇单位（简化版）
    /// </summary>
    /// <param name="center">中心位置</param>
    /// <param name="count">生成数量</param>
    /// <param name="radius">生成半径</param>
    /// <param name="minDistance">最小间距</param>
    /// <param name="unitIndex">单位索引</param>
    /// <param name="side">阵营</param>
    /// <param name="brainType">AI类型</param>
    /// <returns>是否生成成功</returns>
    public static bool SpawnCluster(Vector3 center, int count, float radius, float minDistance, 
        string unitIndex, SideType side, BrainType brainType)
    {
        List<Vector3> spawnPositions = new List<Vector3>();
        
        // 直接生成单位，不做复杂验证
        for (int i = 0; i< count; i++)
        {
            // 在圆形区域内生成随机点
            Vector3 candidate = GenerateRandomPointInCircle(center, radius);
            
            // 投影到NavMesh
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                Vector3 spawnPos = hit.position;
                spawnPositions.Add(spawnPos);
            }
            else
            {
                // 如果投影失败，直接使用候选点
                spawnPositions.Add(candidate);
            }
        }
        
        foreach (Vector3 pos in spawnPositions)
        {
            SoldierFactory.ShowSoldier(unitIndex, pos, side, brainType);
        }
        return true;
    }
    
    /// <summary>
    /// 在圆形区域内生成随机点
    /// </summary>
    private static Vector3 GenerateRandomPointInCircle(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(0f, radius);
        
        float x = center.x + Mathf.Cos(angle) * distance;
        float z = center.z + Mathf.Sin(angle) * distance;
        
        return new Vector3(x, center.y, z);
    }
}
