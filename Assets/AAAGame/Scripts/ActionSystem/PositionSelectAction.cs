using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PositionSelectAction", menuName = "Actions/PositionSelection")]
public class PositionSelectAction : BasicAction
{
    [Header("范围选择预制体")]
    [SerializeField]protected string posSelectPrefabName;
    //[SerializeField] protected Vector3 selectScale;


    protected override ActionInfo CreateInfo(IEntityContext body)
    {
        if (GF.DataModel == null)
            throw new System.InvalidOperationException("PositionSelectAction requires GF.DataModel.");
        InputModel inputModel = GF.DataModel.GetDataModel<InputModel>();
        if (inputModel == null)
            throw new System.InvalidOperationException("PositionSelectAction requires InputModel.");

        return new PositionSelectActionInfo
        {
            selfBody = body,
            elapsed = Fix64.Zero,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            getPosAlready = false,
            showSelectorAlready = false,
            inputs = inputModel
        };
    }

    protected override void OnStart(ActionInfo info)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        //检测是否有遗留的ISelector
        if (posInfo.curSelector != null)
        {
            GF.Entity.HideEntity(posInfo.curSelector.GetEntityID());
            posInfo.curSelector = null;
        }

        posInfo.lastSelectPos = info.selfBody.LogicFramePositionFixed();
        posInfo.confirmedSelectPos = posInfo.lastSelectPos;
        posInfo.getPosAlready = true;
    }

    protected override void OnUpdate(ActionInfo info, Fix64 deltaTime)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        LogicInputFrame inputFrame = posInfo.inputs.CurrentLogicFrame;
        FixVector2 center = info.selfBody.LogicFramePositionFixed();
        FixVector2 requested = inputFrame.HasSelectWorldPosition
            ? inputFrame.SelectWorldPosition
            : center;
        posInfo.lastSelectPos = ClampToRadius(center, requested, posInfo.radius);

        UpdatePresentation(info, posInfo);

        if (inputFrame.WasPressed(LogicInputButton.SkillConfirm))
        {
            posInfo.selectedTargets = CollectSelectedTargets(
                posInfo.lastSelectPos,
                posInfo.selectionRadius,
                info.selfBody.Side);
            posInfo.confirmedSelectPos = posInfo.lastSelectPos;
            HidePresentation(info.selfBody, posInfo);
            FinishAction(info);
        }



    }

    protected override void OnInterrupt(ActionInfo info)
    {
        PositionSelectActionInfo posInfo = GetInfo(info);
        HidePresentation(info.selfBody, posInfo);
    }

    private PositionSelectActionInfo GetInfo(ActionInfo info)
    {
        return info as PositionSelectActionInfo
               ?? throw new System.InvalidOperationException("PositionSelectAction requires PositionSelectActionInfo.");
    }

    public static FixVector2 ClampToRadius(FixVector2 center, FixVector2 requested, Fix64 radius)
    {
        if (radius < Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(radius), radius, "Selection radius must be non-negative.");
        if (radius == Fix64.Zero)
            return center;

        FixVector2 offset = requested - center;
        if (FixVector2.SqrMagnitude(offset) <= radius * radius)
            return requested;
        return center + offset.GetNormalized() * radius;
    }

    private void UpdatePresentation(ActionInfo info, PositionSelectActionInfo posInfo)
    {
        if (info.selfBody is not ICastRangePresenter rangePresenter)
            return;

        Vector3 position = ToPresentationPosition(info.selfBody, posInfo.lastSelectPos);
        posInfo.curSelector?.SetPosition(position);
        if (posInfo.showSelectorAlready)
            return;

        posInfo.showSelectorAlready = true;
        var selectorParams = EntityParams.Create();
        selectorParams.OnShowCallback = logic =>
        {
            CylinderTargetSelector selector = logic as CylinderTargetSelector
                ?? throw new System.InvalidOperationException($"PositionSelectAction expected CylinderTargetSelector, actual={logic?.GetType().FullName ?? "null"}.");
            if (!info.isRunning || info.isFinished || info.isInterrupted)
            {
                GF.Entity.HideEntity(selector.GetEntityID());
                return;
            }

            selector.Activate(new List<ISelectable>(), info.selfBody.Side);
            selector.ChangeRange(posInfo.selectScale);
            selector.SetPosition(ToPresentationPosition(info.selfBody, posInfo.lastSelectPos));
            posInfo.curSelector = selector;
        };

        GF.Entity.ShowEntity<CylinderTargetSelector>(posSelectPrefabName, Const.EntityGroup.Default, selectorParams);
        rangePresenter.ShowCastRange((float)posInfo.radius);
    }

    private static List<ISelectable> CollectSelectedTargets(FixVector2 center, Fix64 radius, SideType side)
    {
        List<ITargetable> targets = LogicTargetSelectionQuery.CollectCurrentFrameCircle(
            center,
            radius,
            side,
            null);
        var selected = new List<ISelectable>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
            selected.Add(targets[i]);
        return selected;
    }

    private static Vector3 ToPresentationPosition(IEntityContext body, FixVector2 position)
    {
        return new Vector3((float)position.x, body.Position.y, (float)position.y);
    }

    private static void HidePresentation(IEntityContext body, PositionSelectActionInfo posInfo)
    {
        if (posInfo.curSelector != null)
        {
            GF.Entity.HideEntity(posInfo.curSelector.GetEntityID());
            posInfo.curSelector = null;
        }

        if (body is ICastRangePresenter rangePresenter)
            rangePresenter.HideCastRange();
    }
}

public class PositionSelectActionInfo : ActionInfo
{
    public ISelector<ISelectable> curSelector;
    public FixVector2 lastSelectPos;
    public bool getPosAlready;
    public bool showSelectorAlready;
    public List<ISelectable> selectedTargets;
    public FixVector2 confirmedSelectPos;

    //需要设置的数值
    public Fix64 radius;
    public Fix64 selectionRadius;
    public Vector3 selectScale;

}
