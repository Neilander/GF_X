using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class ClusterCalculator
{
    private static Dictionary<Transform, List<MAEntity>> _clusterMap = new Dictionary<Transform, List<MAEntity>>();

    public static Vector3 GetClusteredPosition(Transform targetTrans, MAEntity self, float baseRadius)
    {
        if (targetTrans == null || self == null) return self.transform.position;

        if (!_clusterMap.ContainsKey(targetTrans))
            _clusterMap[targetTrans] = new List<MAEntity>();

        var list = _clusterMap[targetTrans];
        list.RemoveAll(x => x == null || !x.gameObject.activeInHierarchy);
        if (!list.Contains(self)) list.Add(self);

        int index = list.IndexOf(self);
        
        int maxPerRow = 5;
        int row = index / maxPerRow;          
        int indexInRow = index % maxPerRow;   

        float currentRadius = baseRadius + row * 1.5f;

        float spacingAngle = 40f; 
        int step = (indexInRow + 1) / 2;
        float sign = (indexInRow % 2 == 0) ? -1f : 1f; 
        
        float angleOffset = (indexInRow == 0) ? 0f : step * spacingAngle * sign;

        Vector3 baseDir = -targetTrans.forward;
        Vector3 offset = Quaternion.Euler(0, angleOffset, 0) * baseDir * currentRadius;
        Vector3 rawTargetPos = targetTrans.position + offset;

        // ==========================================
        // 【核心新增：防隔墙射线检测】
        // 如果玩家和算出来的槽位之间有一堵墙阻挡，
        // 就强行把槽位拉回到墙壁边缘，绝对不允许把目标定在墙对面！
        // ==========================================
        if (NavMesh.Raycast(targetTrans.position, rawTargetPos, out NavMeshHit rayHit, NavMesh.AllAreas))
        {
            // hit.position 是碰到墙壁的交点，我们稍微往回缩一点作为目标点
            rawTargetPos = rayHit.position;
        }

        if (NavMesh.SamplePosition(rawTargetPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
        {
            return hit.position; 
        }

        return rawTargetPos; 
    }
}