using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PositionSelectAction", menuName = "Actions/PositionSelection")]
public class PositionSelectAction : BasicAction
{
    [Header("范围选择预制体")]
    [SerializeField] protected string posSelectPrefabName;
    //[SerializeField] protected Vector3 selectScale;

    public string SelectorPrefabName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(posSelectPrefabName))
                throw new System.InvalidOperationException($"PositionSelectAction selector prefab is empty. asset={name}.");
            return posSelectPrefabName;
        }
    }


    protected override ActionInfo CreateInfo(IEntityContext body)
    {
        return new PositionSelectActionInfo
        {
            selfBody = body,
            elapsed = Fix64.Zero,
            isRunning = false,
            isInterrupted = false,
            isFinished = false,
            getPosAlready = false,
        };
    }

    protected override void OnStart(ActionInfo info)
    {
        if (!LogicFrameRuntime.IsTicking && !LogicPausedOperationService.IsExecuting)
            throw new System.InvalidOperationException("PositionSelectAction must start inside a logic frame or paused-operation settlement.");
        if (info.fatherInfo == null || !info.fatherInfo.hasRequestedWorldPosition)
            throw new System.InvalidOperationException("PositionSelectAction requires a final world position from a skill cast command.");

        PositionSelectActionInfo posInfo = GetInfo(info);
        FixVector2 center = LogicEntityFrameSnapshotService.GetRequiredPosition(info.selfBody);
        posInfo.lastSelectPos = ClampToRadius(
            center,
            info.fatherInfo.requestedWorldPosition,
            posInfo.radius);
        posInfo.confirmedSelectPos = posInfo.lastSelectPos;
        posInfo.getPosAlready = true;
        posInfo.selectedTargets = CollectSelectedTargets(
            posInfo.confirmedSelectPos,
            posInfo.selectionRadius,
            info.selfBody.Side);
        FinishAction(info);
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

}

public class PositionSelectActionInfo : ActionInfo
{
    public FixVector2 lastSelectPos;
    public bool getPosAlready;
    public List<ISelectable> selectedTargets;
    public FixVector2 confirmedSelectPos;

    //需要设置的数值
    public Fix64 radius;
    public Fix64 selectionRadius;
}
