public interface ISkillCompHost
{
    ISkillComp skillComp { get; }
    void SetSkillComp(ISkillComp newSkillComp);
    void CancelRunningSkills();
}

public interface ICastRangePresenter
{
    void ShowCastRange(float ratio);
    void HideCastRange();
}
