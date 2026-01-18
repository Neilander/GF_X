using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PositionSelectAction", menuName = "Actions/PositionSelection")]
public class PositionSelectAction : BasicAction
{
    [Header("范围选择预制体")]
    [SerializeField]protected string posSelectPrefabName;
    //[SerializeField] protected Vector3 selectScale;


    protected override ActionInfo CreateInfo(GeneralCreature body)
    {
        //GF.Log("调用了新的创建");
        return new PositionSelectActionInfo
        {
            selfBody = body,
            elapsed = 0f,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            getPosAlready = false,
            showSelectorAlready = false,
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
        
        
    }

    protected override void OnUpdate(ActionInfo info, float deltaTime)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        Vector2 selectScreenPos = posInfo.inputs.SelectScreenPosition;
        Vector3 selectWorldPos = GetCurrentPos(posInfo, selectScreenPos);
    
        //GF.Log("当前选中位置：x:"+selectWorldPos.x+",y:"+selectWorldPos.y+",z:"+selectWorldPos.z);
        if (posInfo.curSelector == null)
        {
            if (posInfo.showSelectorAlready)
                return;

            posInfo.showSelectorAlready = true;
            //生成新的selector
            var selectorParams = EntityParams.Create();
            selectorParams.OnShowCallback = logic =>
            {
                CylinderTargetSelector selector = (CylinderTargetSelector)logic;
                selector.Activate(new List<ISelectable>(), info.selfBody.Side);
                selector.ChangeRange(posInfo.selectScale);
                selector.SetPosition(selectWorldPos);
                posInfo.curSelector = selector;
            };
        
            GF.Entity.ShowEntity<CylinderTargetSelector>(posSelectPrefabName, Const.EntityGroup.Default, selectorParams);
            (info.selfBody as SkillEntity).ShowCastRange((posInfo.radius+posInfo.selectScale.x/2)*1.05f);
        }
        else
        {
            posInfo.curSelector.SetPosition(selectWorldPos);
        }



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
        if (!posInfo.getPosAlready || !IsWithinRadiusXZ(posInfo.lastSelectPos, posInfo.centerTrans, posInfo.radius))
        {
            posInfo.getPosAlready = true;
            posInfo.lastSelectPos = posInfo.centerTrans.position;
        }
        
        

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

        Vector3 centerPos = posInfo.centerTrans.position;

        // 在半径内：直接用 hitPos
        if (IsWithinRadiusXZ(hitPos, posInfo.centerTrans, posInfo.radius))
        {
            posInfo.lastSelectPos = hitPos;
            return hitPos;
        }

        // —— 超出半径：沿方向 clamp 到圆周 ——
        Vector2 dirXZ = new Vector2(
            hitPos.x - centerPos.x,
            hitPos.z - centerPos.z
        );

        Vector2 clampedDir = dirXZ.normalized * posInfo.radius;

        Vector3 clampedPos = new Vector3(
            centerPos.x + clampedDir.x,
            hitPos.y, // 保留原来的高度；如果你想锁高度，用 centerPos.y
            centerPos.z + clampedDir.y
        );

        posInfo.lastSelectPos = clampedPos;
        return clampedPos;
    }
    
    private static bool IsWithinRadiusXZ(
        Vector3 worldPos,
        Transform centerTrans,
        float radius
    )
    {
        Vector2 posXZ = new Vector2(worldPos.x, worldPos.z);
        Vector2 centerXZ = new Vector2(
            centerTrans.position.x,
            centerTrans.position.z
        );

        return Vector2.Distance(posXZ, centerXZ) <= radius;
    }
}

public class PositionSelectActionInfo : ActionInfo
{
    public ISelector<ISelectable> curSelector;
    public Vector3 lastSelectPos;
    public bool getPosAlready;
    public bool showSelectorAlready;

    //需要设置的数值
    public Transform centerTrans;
    public float radius;
    public Vector3 selectScale;

}
