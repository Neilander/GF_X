public interface ISkillCompHost
{
    ISkillComp skillComp { get; }
    void SetSkillComp(ISkillComp newSkillComp);
    void CancelRunningSkills();
}

public interface ISkillFacingContext
{
    void SetSkillFacingDirectionFixed(FixVector2 direction);
}

public interface ICastRangePresenter
{
    void ShowCastRange(float ratio);
    void HideCastRange();
}
