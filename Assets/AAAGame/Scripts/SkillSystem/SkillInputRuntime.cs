using UnityEngine;

public readonly struct SkillCastPreviewDescriptor
{
    public static readonly SkillCastPreviewDescriptor Instant = new(
        false,
        Fix64.Zero,
        Fix64.Zero,
        Vector3.zero,
        null);

    public SkillCastPreviewDescriptor(
        bool requiresWorldPosition,
        Fix64 castRadius,
        Fix64 selectionRadius,
        Vector3 selectorScale,
        string selectorPrefabName)
    {
        RequiresWorldPosition = requiresWorldPosition;
        CastRadius = castRadius;
        SelectionRadius = selectionRadius;
        SelectorScale = selectorScale;
        SelectorPrefabName = selectorPrefabName;
    }

    public bool RequiresWorldPosition { get; }
    public Fix64 CastRadius { get; }
    public Fix64 SelectionRadius { get; }
    public Vector3 SelectorScale { get; }
    public string SelectorPrefabName { get; }
}

public interface ISkillCastPreviewProvider
{
    bool CanRequestSkillCast(int slotIndex);
    SkillCastPreviewDescriptor GetRequiredSkillCastPreview(int slotIndex);
}

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

    public static void BeginCast()
    {
        s_ActiveCastCount++;
    }

    public static void EndCast()
    {
        if (s_ActiveCastCount <= 0)
            throw new System.InvalidOperationException("SkillCastState.EndCast called without active cast.");

        s_ActiveCastCount--;
    }

    public static void Reset()
    {
        if (s_ActiveCastCount == 0)
            return;

        s_ActiveCastCount = 0;
    }
}
