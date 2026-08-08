using System;
using System.Collections.Generic;

public static class CareerRunSettings
{
    private static readonly Dictionary<string, Archetype> s_LastSelections = new(StringComparer.Ordinal);

    public static bool HasActiveRun { get; private set; }
    public static string CareerLevelIdentifier { get; private set; }
    public static string RuntimeLevelIdentifier { get; private set; }
    public static bool IsVariableExperiment { get; private set; }
    public static Archetype StartingArchetype { get; private set; }
    public static VariableExperimentRuleTable ActiveRule { get; private set; }

    public static Archetype ResolveRememberedSelection(string levelIdentifier, IReadOnlyList<Archetype> available)
    {
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Level identifier is empty.", nameof(levelIdentifier));
        if (available == null || available.Count == 0)
            throw new InvalidOperationException($"Level '{levelIdentifier}' has no available starting industry.");

        if (s_LastSelections.TryGetValue(levelIdentifier, out Archetype remembered))
        {
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i] == remembered)
                    return remembered;
            }
        }
        return available[0];
    }

    public static string BeginRun(string levelIdentifier, bool isVariableExperiment, Archetype startingArchetype)
    {
        LevelTable sourceLevel = CareerConfigRuntime.GetLevelRequired(levelIdentifier);
        if (startingArchetype == Archetype.None || startingArchetype == Archetype.Common)
            throw new InvalidOperationException($"Starting industry '{startingArchetype}' is not selectable.");
        if (CareerConfigRuntime.IsTutorialLevel(levelIdentifier) && startingArchetype != Archetype.Coding)
            throw new InvalidOperationException("Tutorial level requires the Coding starting industry.");

        VariableExperimentRuleTable rule = null;
        string runtimeLevelIdentifier = sourceLevel.Identifier;
        if (isVariableExperiment)
        {
            if (!CareerConfigRuntime.TryGetExperiment(levelIdentifier, out VariableExperimentTable experiment))
                throw new InvalidOperationException($"Level '{levelIdentifier}' has no variable experiment config.");
            rule = CareerConfigRuntime.GetRuleRequired(experiment.RuleIdentifier);
            if (rule.ForcedArchetype != Archetype.None && startingArchetype != rule.ForcedArchetype)
            {
                throw new InvalidOperationException(
                    $"Variable experiment '{levelIdentifier}' requires starting industry '{rule.ForcedArchetype}'.");
            }
            if (!string.IsNullOrWhiteSpace(experiment.LevelConfigIdentifier))
                runtimeLevelIdentifier = CareerConfigRuntime.GetLevelRequired(experiment.LevelConfigIdentifier).Identifier;
        }

        s_LastSelections[levelIdentifier] = startingArchetype;
        CareerLevelIdentifier = levelIdentifier;
        RuntimeLevelIdentifier = runtimeLevelIdentifier;
        IsVariableExperiment = isVariableExperiment;
        StartingArchetype = startingArchetype;
        ActiveRule = rule;
        HasActiveRun = true;
        return runtimeLevelIdentifier;
    }

    public static void RememberSelection(string levelIdentifier, Archetype archetype)
    {
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Level identifier is empty.", nameof(levelIdentifier));
        if (archetype == Archetype.None || archetype == Archetype.Common)
            throw new ArgumentOutOfRangeException(nameof(archetype), archetype, "Starting industry is not selectable.");
        s_LastSelections[levelIdentifier] = archetype;
    }

    public static void CancelRun()
    {
        HasActiveRun = false;
        CareerLevelIdentifier = null;
        RuntimeLevelIdentifier = null;
        IsVariableExperiment = false;
        StartingArchetype = Archetype.None;
        ActiveRule = null;
    }
}
