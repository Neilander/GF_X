public abstract class InstantActiveSkillSO : ActiveSkillSO
{
    protected abstract void ApplyInstant(IEntityContext caster);

    public override void StartSkill(IEntityContext body, out SkillInfo info)
    {
        ValidateAction();
        base.StartSkill(body, out info);
        ApplyCommittedEffect(info);
    }

    public override void StartSkill(IEntityContext body, FixVector2 requestedWorldPosition, out SkillInfo info)
    {
        ValidateAction();
        base.StartSkill(body, requestedWorldPosition, out info);
        ApplyCommittedEffect(info);
    }

    public override void TickSkill(SkillInfo info, Fix64 deltaTime)
    {
        if (info == null)
            throw new System.ArgumentNullException(nameof(info));
        if (info.isFinished)
            return;

        base.TickSkill(info, deltaTime);
        ApplyCommittedEffect(info);
    }

    protected override void ConfigureActionInfo(SkillInfo skillInfo, ActionInfo actionInfo, int actionIndex)
    {
        base.ConfigureActionInfo(skillInfo, actionInfo, actionIndex);
        if (actionInfo is not InstantSkillActionInfo instantInfo)
            throw new System.InvalidOperationException(
                $"Instant active skill requires InstantSkillActionInfo. skillId={skillId}, action={actionIndex}.");

        SkillRuntimeInfo runtime = GetRuntimeInfo();
        SkillData data = runtime.Data
                         ?? throw new System.InvalidOperationException($"SkillData missing. skillId={skillId}");
        instantInfo.windUp = data.WindUp;
        instantInfo.windDown = data.WindDown;
    }

    private void ValidateAction()
    {
        if (actions == null || actions.Count != 1 || actions[0] is not InstantSkillAction)
        {
            throw new System.InvalidOperationException(
                $"Instant active skill requires exactly one InstantSkillAction. skillId={skillId}, asset={name}.");
        }
    }

    private void ApplyCommittedEffect(SkillInfo info)
    {
        if (info.currentInfo is not InstantSkillActionInfo instantInfo)
            throw new System.InvalidOperationException($"Instant active skill action state missing. skillId={skillId}.");

        if (instantInfo.effectCommitted && !instantInfo.effectApplied)
        {
            ApplyInstant(info.entity);
            instantInfo.effectApplied = true;
        }

        if (instantInfo.isFinished)
        {
            if (!instantInfo.effectApplied)
                throw new System.InvalidOperationException($"Instant active skill finished before applying its effect. skillId={skillId}.");
            info.isFinished = true;
        }
    }
}
