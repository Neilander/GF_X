using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SelectPositionSkill",
    menuName = "Skills/(ZObsolete)SelectPositionSkill")]
public class SelectPositionSkill : ActiveSkillSO
{
    //[Header("选择相关数据")]
    //public float radius = 10f;
    //public Vector3 selectRatio;

    protected override SkillInfo CreateSkillInfo(IEntityContext body)
    {
        return new SelectPosSkillInfo()
        {
            entity = body,
            currentIndex = 0,
            isFinished = false,
            selectTargets = new List<ISelectable>(),
            selectPos = body.PositionFixed
        };
    }

    public override void StartSkill(IEntityContext body, out SkillInfo info)
    {
        base.StartSkill(body, out info);
    }

    protected override void SwitchToNextAction(SkillInfo info)
    {
         SelectPosSkillInfo posInfo = info as SelectPosSkillInfo;
         if (info.currentInfo is PositionSelectActionInfo)
         {
             PositionSelectActionInfo actionInfo =
                 info.currentInfo as PositionSelectActionInfo;
             //这是一个选择行为，要记录选择内容
             posInfo.selectPos = actionInfo.confirmedSelectPos;
             posInfo.selectTargets = actionInfo.selectedTargets ?? new List<ISelectable>();
         }
         //如果不是，也不清空，如果有需要再清空

         base.SwitchToNextAction(info);

         //设置继承信息
         info.currentInfo.selectTargets = posInfo.selectTargets;
         info.currentInfo.selectPos = posInfo.selectPos;
    }

}

public class SelectPosSkillInfo :SkillInfo
{
    public List<ISelectable> selectTargets;
    public FixVector2 selectPos;
}
