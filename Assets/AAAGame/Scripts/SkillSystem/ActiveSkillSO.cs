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

    internal ActiveSkillSO CreateRuntimeSnapshot()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Active skill assets cannot be snapshotted during a logic frame.");

        SkillRuntimeSnapshotValidation.ValidateReferenceFields(this, nameof(actions));
        ActiveSkillSO snapshot = Instantiate(this);
        snapshot.hideFlags = HideFlags.HideAndDontSave;
        if (actions == null)
        {
            snapshot.actions = null;
            return snapshot;
        }

        snapshot.actions = new List<BasicAction>(actions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            BasicAction action = actions[i]
                                 ?? throw new InvalidOperationException(
                                     $"Active skill contains a null action. skillId={skillId}, index={i}.");
            snapshot.actions.Add(action.CreateRuntimeSnapshot());
        }
        return snapshot;
    }

    public Fix64 ResolveCooldownIntervalFixed()
    {
        return ResolveCooldownIntervalFixed(1);
    }

    public Fix64 ResolveCooldownIntervalFixed(int level)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new InvalidOperationException($"ActiveSkillSO missing skillId. asset={name}");
        if (level <= 0)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Skill level must be positive.");

        SkillData skillData = SkillDataModel.GetSkillData(skillId);
        if (skillData == null)
            throw new InvalidOperationException($"SkillData missing. skillId={skillId}, asset={name}");

        Fix64 cooldown = skillData.GetCooldown(level);
        if (cooldown <= Fix64.Zero)
            throw new InvalidOperationException(
                $"Active skill cooldown must be positive. skillId={skillId}, level={level}, raw={cooldown.RawValue}");
        return cooldown;
    }

    protected Fix64 GetRequiredCastDistanceWorldFixed()
    {
        Fix64 castDistance = GetCastDistanceWorldFixed();
        if (castDistance <= Fix64.Zero)
            throw new InvalidOperationException(
                $"Position skill cast distance must be positive. skillId={skillId}, raw={castDistance.RawValue}");
        return castDistance;
    }

    protected Vector3 GetSelectionScale()
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
        StartSkillInternal(body, false, FixVector2.Zero, out info);
    }

    public virtual void StartSkill(IEntityContext body, FixVector2 requestedWorldPosition, out SkillInfo info)
    {
        StartSkillInternal(body, true, requestedWorldPosition, out info);
    }

    private void StartSkillInternal(
        IEntityContext body,
        bool hasRequestedWorldPosition,
        FixVector2 requestedWorldPosition,
        out SkillInfo info)
    {
        info = CreateSkillInfo(body);
        info.tempInfoRecords = new Dictionary<ActionInfo, Type>();
        info.hasRequestedWorldPosition = hasRequestedWorldPosition;
        info.requestedWorldPosition = requestedWorldPosition;
        if (actions == null || actions.Count == 0)
            throw new InvalidOperationException($"ActiveSkillSO has no actions. skillId={skillId}, asset={name}");

        SetupNewAction(info, 0);
    }

    public virtual void TickSkill(SkillInfo info, Fix64 deltaTime)
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
        action.StartAction(
            info.entity,
            info,
            actionInfo => ConfigureActionInfo(info, actionInfo, actionIndex),
            out info.currentInfo);
    }

    protected virtual void ConfigureActionInfo(SkillInfo skillInfo, ActionInfo actionInfo, int actionIndex)
    {
        actionInfo.executeIndex = actionIndex;
        actionInfo.damageInfo = new Damage(skillInfo.entity, Fix64.One);

        //根据新的信息容器类型来注入
        //只注入固定的设置信息
        switch (actionInfo)
        {
            case PositionSelectActionInfo posSelectInfo:
                posSelectInfo.radius = GetRequiredCastDistanceWorldFixed();
                posSelectInfo.selectionRadius = GetAreaRangeWorldFixed();
                break;

        }
    }

    public bool TryGetPositionSelectionDescriptor(out SkillCastPreviewDescriptor descriptor)
    {
        if (actions != null)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] is not PositionSelectAction positionAction)
                    continue;

                Fix64 selectionRadius = GetAreaRangeWorldFixed();
                Vector3 selectionScale = GetSelectionScale();
                descriptor = new SkillCastPreviewDescriptor(
                    true,
                    GetRequiredCastDistanceWorldFixed(),
                    selectionRadius,
                    selectionScale,
                    positionAction.SelectorPrefabName);
                return true;
            }
        }

        descriptor = SkillCastPreviewDescriptor.Instant;
        return false;
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

internal static class SkillRuntimeSnapshotValidation
{
    public static void ValidateReferenceFields(object source, params string[] explicitlyHandledFields)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var handled = new HashSet<string>(explicitlyHandledFields ?? Array.Empty<string>(), StringComparer.Ordinal);
        for (Type type = source.GetType(); type != null && type != typeof(ScriptableObject); type = type.BaseType)
        {
            System.Reflection.FieldInfo[] fields = type.GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                System.Reflection.FieldInfo field = fields[i];
                Type fieldType = field.FieldType;
                if (fieldType.IsValueType || fieldType == typeof(string) || handled.Contains(field.Name))
                    continue;

                throw new InvalidOperationException(
                    $"Skill runtime snapshot contains an unsupported reference field. asset={source.GetType().Name}, field={field.Name}, type={fieldType.FullName}.");
            }
        }
    }
}

public class SkillInfo
{
    public int currentIndex = 0;
    public bool isFinished = false;
    public IEntityContext entity;
    public ActionInfo currentInfo;
    public Dictionary<ActionInfo, Type> tempInfoRecords;
    public bool hasRequestedWorldPosition;
    public FixVector2 requestedWorldPosition;
}



public enum CoolDownType
{
    Count
}


