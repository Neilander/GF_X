using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PositionSelectAction", menuName = "Actions/PositionSelection")]
public class PositionSelectAction : BasicAction
{
    [Header("范围选择预制体")]
    [SerializeField]protected GameObject positionSelectPrefab;
    [SerializeField] protected Vector3 selectScale;

    protected override void OnStart(ActionInfo info)
    {
        //检测是否有遗留的ISelector
    }

    protected override void OnUpdate(ActionInfo info, float deltaTime)
    {
    }

    protected override void OnInterrupt(ActionInfo info)
    {
    }
}
