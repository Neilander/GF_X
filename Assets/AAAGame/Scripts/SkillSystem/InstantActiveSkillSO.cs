public abstract class InstantActiveSkillSO : ActiveSkillSO
{
    protected abstract void ApplyInstant(MAEntity caster);

    public override void StartSkill(MAEntity body, out SkillInfo info)
    {
        ApplyInstant(body);
        info = new SkillInfo
        {
            entity = body,
            currentIndex = 0,
            isFinished = true,
        };
    }

    public override void TickSkill(SkillInfo info, float deltaTime)
    {
        if (info != null)
            info.isFinished = true;
    }
}
