using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SelectPositionSkill", 
    menuName = "Skills/(ZObsolete)SelectPositionSkill")]
public class SelectPositionSkill : BasicSkill
{
    //[Header("选择相关数据")]
    //public float radius = 10f;
    //public Vector3 selectRatio;
    
    protected override SkillInfo CreateSkillInfo(SkillEntity body)
    {
        return new SelectPosSkillInfo()
        {
            entity = body,
            currentIndex = 0,
            isFinished = false,
            selectTargets = new List<ISelectable>(),
            selectPos = body.transform.position
        };
    }

    public override void StartSkill(SkillEntity body, out SkillInfo info)
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
             posInfo.selectPos = actionInfo.lastSelectPos;
             actionInfo.curSelector.GetSelected(out posInfo.selectTargets);
         }
         //如果不是，也不清空，如果有需要再清空
 
         base.SwitchToNextAction(info);
         
         //设置继承信息
         info.currentInfo.selectTargets = posInfo.selectTargets;
         info.currentInfo.selectPos = posInfo.selectPos;
    }

    protected override void SetupNewAction(SkillInfo info, int actionIndex)
    {
        base.SetupNewAction(info, actionIndex);
        //检测新的currentInfo是否为某某某
        //GF.Log("我是1");
        if (info.currentInfo is not PositionSelectActionInfo posSelectInfo)
            return;
        //GF.Log("我是2");
        posSelectInfo.centerTrans = info.entity.transform;
        posSelectInfo.radius = radius;
        posSelectInfo.selectScale = selectRatio;
    }
}

public class SelectPosSkillInfo :SkillInfo
{
    public List<ISelectable> selectTargets;
    public Vector3 selectPos;
}