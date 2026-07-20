using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ActiveSkillSO", menuName = "Skills/ActiveSkillSO")]
public class ActiveSkillSO : SkillEffectSO
{
    public List<BasicAction> actions;

    public CoolDownType coolDownType;

    public float coolDownInterval;

    [Header("属性")]
    [Tooltip("不可在其他技能释放时释放，默认开启")]
    public bool banWhenOtherSkill = true;
    [Tooltip("其他技能不可插入，默认关闭")]
    public bool banOtherSkillWhenCast = false;

    //这些数值之后都会读表，根据名字获取到一组float，然后赋值
    [Header("临时数值，之后淘汰")]
    public float radius = 10f;
    public Vector3 selectRatio;

    public float ResolveCooldownInterval()
    {
        return ResolveCooldownInterval(1);
    }

    public float ResolveCooldownInterval(int level)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return coolDownInterval;

        SkillData skillData = SkillDataModel.GetSkillData(skillId);
        if (skillData == null)
            return coolDownInterval;

        Fix64 cooldown = skillData.GetCooldown(level);
        return cooldown > Fix64.Zero ? (float)cooldown : coolDownInterval;
    }

    protected float GetCastDistanceWorldOrFallback()
    {
        float tableValue = GetCastDistanceWorld();
        return tableValue > 0f ? tableValue : radius;
    }

    protected Vector3 GetSelectionScaleOrFallback()
    {
        float areaRange = GetAreaRangeWorld();
        if (areaRange <= 0f)
            return selectRatio;

        return new Vector3(
            areaRange,
            selectRatio.y > 0f ? selectRatio.y : areaRange * 2f,
            selectRatio.z);
    }

    protected virtual SkillInfo CreateSkillInfo(IEntityContext body)
    {
        return new SkillInfo()
        {
            entity = body,
            currentIndex = 0,
            isFinished = false,
        };
    }

    public virtual void StartSkill(IEntityContext body, out SkillInfo info)
    {
        info = CreateSkillInfo(body);
        info.tempInfoRecords = new Dictionary<ActionInfo, Type>();
        if (actions == null || actions.Count == 0)
            throw new InvalidOperationException($"ActiveSkillSO has no actions. skillId={skillId}, asset={name}");

        SetupNewAction(info, 0);
        /*
        actions[0].StartAction(body, out info.currentInfo);
        info.currentInfo.damageInfo = new Damage(body, 1);
        
        body.animator.SetTrigger(actions[0].relatedTriggerString);*/
    }

    public virtual void TickSkill(SkillInfo info, float deltaTime)
    {
        actions[info.currentIndex].Tick(info.currentInfo, deltaTime);
        if (info.currentInfo.isFinished)
        {
            if (info.currentIndex < actions.Count - 1)
            {
                //说明技能还没执行完，继续执行
                SwitchToNextAction(info);
            }
            else
            {
                //技能执行完毕了
                info.isFinished = true;
            }
        }
    }

    protected virtual void SwitchToNextAction(SkillInfo info)
    {
        info.currentIndex += 1;
        SetupNewAction(info, info.currentIndex);
        /*
        actions[info.currentIndex].StartAction(info.entity, out info.currentInfo);
        info.currentInfo.damageInfo = new Damage(info.entity, 1);
        info.entity.animator.SetTrigger(actions[info.currentIndex].relatedTriggerString);*/
    }

    protected virtual void SetupNewAction(SkillInfo info, int actionIndex)
    {
        //结算之前的信息
        if (info.currentIndex == 0)
        {
            //说明前置没有行为，这是第一次，不用初始化
        }
        else
        {
            switch (info.currentInfo)
            {
                case PositionSelectActionInfo posInfo:
                    info.tempInfoRecords.Add(posInfo, typeof(PositionSelectActionInfo));
                    break;

            }
        }
        //数据记录完毕，进入下个部分

        //更新信息
        var action = actions[actionIndex];
        action.StartAction(RequireLegacyActionBody(info.entity), out info.currentInfo);
        info.currentInfo.executeIndex = actionIndex;
        info.currentInfo.damageInfo = new Damage(info.entity, Fix64.One);
        if (!string.IsNullOrWhiteSpace(action.relatedTriggerString)
            && info.entity is MAEntity view
            && view.animator != null)
        {
            view.animator.SetTrigger(action.relatedTriggerString);
        }
        info.currentInfo.fatherInfo = info;

        //根据新的信息容器类型来注入
        //只注入固定的设置信息
        switch (info.currentInfo)
        {
            case PositionSelectActionInfo posSelectInfo:
                if (info.entity is not MAEntity positionSelectionView)
                {
                    throw new InvalidOperationException(
                        $"ActiveSkillSO position selection requires a bound MAEntity presenter. skillId={skillId}, entity={info.entity.LogicEntityId.Value}.");
                }
                posSelectInfo.centerTrans = positionSelectionView.transform;
                posSelectInfo.radius = GetCastDistanceWorldOrFallback();
                posSelectInfo.selectScale = GetSelectionScaleOrFallback();
                break;

        }
    }

    private static GeneralCreature RequireLegacyActionBody(IEntityContext entity)
    {
        if (entity is GeneralCreature creature)
            return creature;
        throw new InvalidOperationException(
            $"Legacy BasicAction requires a GeneralCreature presenter. entity={entity?.LogicEntityId.Value ?? 0}.");
    }



    public virtual void InterruptSkill(SkillInfo info)
    {
        if (info == null)
            return;

        if (!info.isFinished && info.currentInfo != null && info.currentIndex >= 0 && info.currentIndex < actions.Count)
            actions[info.currentIndex].Interrupt(info.currentInfo);

        info.isFinished = true;
    }

    public void InterruptSkill()
    {
        GF.LogError("尝试打断了技能，但是并没有实现。感觉是没问题的，就是因为没具体case，所以想有需求了再实现");
    }

}

public class SkillInfo
{
    public int currentIndex = 0;
    public bool isFinished = false;
    public IEntityContext entity;
    public ActionInfo currentInfo;
    public Dictionary<ActionInfo, Type> tempInfoRecords;
}



public enum CoolDownType
{
    Count
}


