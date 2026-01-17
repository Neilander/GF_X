using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PositionSelectAction", menuName = "Actions/PositionSelection")]
public class PositionSelectAction : BasicAction
{
    [Header("范围选择预制体")]
    [SerializeField]protected GameObject positionSelectPrefab;
    [SerializeField] protected Vector3 selectScale;


    protected override ActionInfo CreateInfo(GeneralCreature body)
    {
        return new PositionSelectActionInfo
        {
            selfBody = body,
            elapsed = 0f,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            inputs = GF.DataModel.GetDataModel<InputModel>()
        };   
    }

    protected override void OnStart(ActionInfo info)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        //检测是否有遗留的ISelector
        if (posInfo.curSelector != null)
        {
            GF.Entity.HideEntity(posInfo.curSelector.GetEntityID());
        }

        posInfo.lastSelectPos = posInfo.center;
    }

    protected override void OnUpdate(ActionInfo info, float deltaTime)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        Vector2 selectScreenPos = posInfo.inputs.SelectScreenPosition;
        Vector3 selectWorldPos = GetCurrentPos(posInfo, selectScreenPos);
        GF.Log("当前选中位置：x:"+selectWorldPos.x+",y:"+selectWorldPos.y+",z:"+selectWorldPos.z);

    }

    protected override void OnInterrupt(ActionInfo info)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
    }

    private PositionSelectActionInfo GetInfo(ActionInfo info)
    {
        PositionSelectActionInfo posInfo = info as PositionSelectActionInfo;
        if(posInfo == null)
            GF.LogError("正在使用非法的ActionInfo，应该使用PositionSelectActionInfo");
        return posInfo;
    }
    
    private Vector3 GetCurrentPos(
        PositionSelectActionInfo posInfo,
        Vector2 mouseScreenPos
    )
    {
        Camera cam = Camera.main;
        if (cam == null)
            return posInfo.lastSelectPos;

        Ray ray = cam.ScreenPointToRay(mouseScreenPos);

        int groundMask = LayerMask.GetMask("Ground");

        if (!Physics.Raycast(ray, out RaycastHit hit, cam.farClipPlane, groundMask))
        {
            // 没有碰到地面，直接用上一次的位置
            return posInfo.lastSelectPos;
        }

        Vector3 hitPos = hit.point;

        // —— 只判断 XY 平面 ——
        Vector2 hitXY    = new Vector2(hitPos.x, hitPos.y);
        Vector2 centerXY = new Vector2(posInfo.center.x, posInfo.center.y);

        float dist = Vector2.Distance(hitXY, centerXY);

        if (dist <= posInfo.radius)
        {
            // 在范围内，更新 lastSelectPos
            posInfo.lastSelectPos = hitPos;
            return hitPos;
        }

        // 超出范围，保持上一次
        return posInfo.lastSelectPos;
    }
}

public class PositionSelectActionInfo : ActionInfo
{
    public ISelector<ISelectable> curSelector;
    public Vector3 lastSelectPos;

    //需要设置的数值
    public Vector3 center;
    public float radius;
    
}
