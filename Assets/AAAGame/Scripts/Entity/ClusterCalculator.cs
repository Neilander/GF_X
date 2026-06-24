using System.Collections.Generic;
using UnityEngine;

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

        int agentTypeId = self.navAgentTypeID == MAEntity.UnknownNavAgentTypeId ? 0 : self.navAgentTypeID;
        Vector3 desiredDisplacement = rawTargetPos - targetTrans.position;
        if (FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                targetTrans.position,
                desiredDisplacement,
                agentTypeId,
                out Vector3 constrainedDisplacement)
            && constrainedDisplacement.sqrMagnitude < desiredDisplacement.sqrMagnitude)
        {
            rawTargetPos = targetTrans.position + constrainedDisplacement;
        }

        if (FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                rawTargetPos,
                agentTypeId,
                2.0f,
                0f,
                out Vector3 legalPoint))
        {
            return legalPoint;
        }

        return rawTargetPos; 
    }
}
