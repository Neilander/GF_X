public static class SkillInputRuntime
{
    public const int MaxSkillCount = 5;

    private static readonly string[] s_KeyLabels = { "Q", "E", "R", "F", "T" };

    public static string GetKeyLabel(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex >= s_KeyLabels.Length)
            throw new System.ArgumentOutOfRangeException(nameof(skillIndex), skillIndex, "Invalid skill index.");

        return s_KeyLabels[skillIndex];
    }

    public static bool CanUseActiveSkillsInCurrentPhase()
    {
        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        return !InGameDataModel.IsBuildPhase(phase);
    }
}

public static class SkillCastState
{
    private static int s_ActiveCastCount;

    public static bool IsCasting => s_ActiveCastCount > 0;

    public static event System.Action Changed;

    public static void BeginCast()
    {
        s_ActiveCastCount++;
        if (s_ActiveCastCount == 1)
            Changed?.Invoke();
    }

    public static void EndCast()
    {
        if (s_ActiveCastCount <= 0)
            throw new System.InvalidOperationException("SkillCastState.EndCast called without active cast.");

        s_ActiveCastCount--;
        if (s_ActiveCastCount == 0)
            Changed?.Invoke();
    }

    public static void Reset()
    {
        if (s_ActiveCastCount == 0)
            return;

        s_ActiveCastCount = 0;
        Changed?.Invoke();
    }
}
