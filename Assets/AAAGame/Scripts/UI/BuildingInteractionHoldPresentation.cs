using System;
using UnityEngine;

public static class BuildingInteractionHoldPresentation
{
    public const string MinimumDurationConfigKey = "BuildingInteractionHoldMinimumDuration";

    private const float PerStarMinimumSeconds = 0.1f;
    private const float PerStarMaximumSeconds = 0.4f;
    private const float AlignedDurationSeconds = 2f;

    public static float ResolveConfiguredDurationSeconds(int starCount)
    {
        if (GF.Config == null)
            throw new InvalidOperationException("Building interaction hold duration requires GF.Config.");

        float minimumDuration = GF.Config.GetFloat(MinimumDurationConfigKey);
        return ResolveDurationSeconds(starCount, minimumDuration);
    }

    public static float ResolveDurationSeconds(int starCount, float minimumDuration)
    {
        if (starCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(starCount));
        if (minimumDuration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        if (starCount == 1)
            return minimumDuration;

        float perStarSeconds = AlignedDurationSeconds / (starCount - 1f);
        perStarSeconds = Mathf.Clamp(perStarSeconds, PerStarMinimumSeconds, PerStarMaximumSeconds);
        return Mathf.Max(minimumDuration, perStarSeconds * (starCount - 1f));
    }

    public static float AdvanceConfiguredProgressStars(
        float current,
        bool pressing,
        int starCount,
        float deltaTime)
    {
        return AdvanceProgressStars(
            current,
            pressing,
            starCount,
            deltaTime,
            ResolveConfiguredDurationSeconds(starCount));
    }

    public static float AdvanceProgressStars(
        float current,
        bool pressing,
        int starCount,
        float deltaTime,
        float duration)
    {
        if (starCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(starCount));
        if (deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (duration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(duration));

        float delta = starCount / duration * deltaTime;
        return pressing
            ? Mathf.Min(starCount, current + delta)
            : Mathf.Max(0f, current - delta);
    }
}
