public abstract class InstantActiveSkillSO : ActiveSkillSO
{
    protected abstract void ApplyInstant(IEntityContext caster);

    public override void StartSkill(IEntityContext body, out SkillInfo info)
    {
        ApplyInstant(body);
        info = new SkillInfo
        {
            entity = body,
            currentIndex = 0,
            isFinished = true,
        };
    }

    public override void TickSkill(SkillInfo info, Fix64 deltaTime)
    {
        if (info != null)
            info.isFinished = true;
    }
}
