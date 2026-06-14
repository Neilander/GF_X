using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class TargetPositionActiveSkillSO : ActiveSkillSO
{
    protected override SkillInfo CreateSkillInfo(MAEntity body)
    {
        return new TargetPositionSkillInfo
        {
            entity = body,
            currentIndex = 0,
            isFinished = false,
            selectTargets = new List<ISelectable>(),
            selectPos = body.transform.position
        };
    }

    protected override void SwitchToNextAction(SkillInfo info)
    {
        CapturePositionSelection(info);
        base.SwitchToNextAction(info);

        if (info is TargetPositionSkillInfo positionInfo)
        {
            info.currentInfo.selectTargets = positionInfo.selectTargets;
            info.currentInfo.selectPos = positionInfo.selectPos;
        }
    }

    protected override void SetupNewAction(SkillInfo info, int actionIndex)
    {
        base.SetupNewAction(info, actionIndex);
        if (info.currentInfo is PositionSelectActionInfo posSelectInfo)
        {
            posSelectInfo.centerTrans = info.entity.transform;
            posSelectInfo.radius = GetCastDistanceWorldOrFallback();
            posSelectInfo.selectScale = GetSelectionScaleOrFallback();
        }
    }

    public override void TickSkill(SkillInfo info, float deltaTime)
    {
        if (info == null)
            throw new ArgumentNullException(nameof(info));

        int previousIndex = info.currentIndex;
        base.TickSkill(info, deltaTime);

        if (info.isFinished)
        {
            if (previousIndex == info.currentIndex)
                CapturePositionSelection(info);

            if (info is not TargetPositionSkillInfo positionInfo)
                throw new InvalidOperationException($"{GetType().Name} requires TargetPositionSkillInfo. skillId={skillId}");

            ApplyAtPosition(info.entity, positionInfo.selectPos, positionInfo.selectTargets);
        }
    }

    protected abstract void ApplyAtPosition(MAEntity caster, Vector3 position, IReadOnlyList<ISelectable> selectedTargets);

    private static void CapturePositionSelection(SkillInfo info)
    {
        if (info.currentInfo is not PositionSelectActionInfo actionInfo)
            return;

        if (info is not TargetPositionSkillInfo positionInfo)
            throw new InvalidOperationException("TargetPositionActiveSkillSO requires TargetPositionSkillInfo.");

        positionInfo.selectPos = actionInfo.confirmedSelectPos;
        positionInfo.selectTargets = actionInfo.selectedTargets ?? new List<ISelectable>();
    }
}

public class TargetPositionSkillInfo : SkillInfo
{
    public List<ISelectable> selectTargets;
    public Vector3 selectPos;
}
