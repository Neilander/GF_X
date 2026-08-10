using System;

public static class LevelObjectiveIdentifiers
{
    public const string CaptureStrongholdCount = "CaptureStrongholdCount";
    public const string CaptureSpecificStrongholds = "CaptureSpecificStrongholds";
    public const string SurviveDays = "SurviveDays";
    public const string ProtectStronghold = "ProtectStronghold";
    public const string DefendBase = "DefendBase";
    public const string UpgradeCodingCoreLevel3 = "UpgradeCodingCoreLevel3";
}

public sealed class LevelObjectiveDefinition
{
    public LevelObjectiveDefinition(
        int slot,
        bool isPrimary,
        string objectiveIdentifier,
        Fix64[] uniqueValues,
        int experience)
    {
        if (slot <= 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (string.IsNullOrWhiteSpace(objectiveIdentifier))
            throw new ArgumentException("Objective identifier is empty.", nameof(objectiveIdentifier));
        if (experience < 0)
            throw new ArgumentOutOfRangeException(nameof(experience));

        Slot = slot;
        IsPrimary = isPrimary;
        ObjectiveIdentifier = objectiveIdentifier;
        UniqueValues = uniqueValues == null ? Array.Empty<Fix64>() : (Fix64[])uniqueValues.Clone();
        Experience = experience;
    }

    public int Slot { get; }
    public bool IsPrimary { get; }
    public string ObjectiveIdentifier { get; }
    public Fix64[] UniqueValues { get; }
    public int Experience { get; }
}

public class LevelData
{
    public string Identifier { get; private set; }
    public string NameKey { get; private set; }
    public string DescKey { get; private set; }
    public int InitResource { get; private set; }
    public GamePhase StartPhase { get; private set; }
    public LevelObjectiveDefinition[] PrimaryObjectives { get; private set; }
    public LevelObjectiveDefinition[] OptionalObjectives { get; private set; }

    public static LevelData FromRow(LevelTable row)
    {
        if (row == null)
            throw new ArgumentNullException(nameof(row));

        return new LevelData
        {
            Identifier = row.Identifier,
            NameKey = row.NameKey,
            DescKey = row.DescKey,
            InitResource = row.InitResource,
            StartPhase = row.StartPhase,
            PrimaryObjectives = BuildPrimaryObjectives(row),
            OptionalObjectives = BuildOptionalObjectives(row),
        };
    }

    public static LevelData CreateForTests(
        string identifier,
        LevelObjectiveDefinition[] primaryObjectives,
        LevelObjectiveDefinition[] optionalObjectives = null)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Level identifier is empty.", nameof(identifier));
        return new LevelData
        {
            Identifier = identifier,
            PrimaryObjectives = CloneDefinitions(primaryObjectives),
            OptionalObjectives = CloneDefinitions(optionalObjectives),
        };
    }

    private static LevelObjectiveDefinition[] BuildPrimaryObjectives(LevelTable row)
    {
        var result = new LevelObjectiveDefinition[CountConfigured(
            row.PrimaryObjective1Identifier,
            row.PrimaryObjective2Identifier,
            row.PrimaryObjective3Identifier)];
        int index = 0;
        AddConfigured(result, ref index, 1, true, row.PrimaryObjective1Identifier, row.PrimaryObjective1UniqueValues, 0);
        AddConfigured(result, ref index, 2, true, row.PrimaryObjective2Identifier, row.PrimaryObjective2UniqueValues, 0);
        AddConfigured(result, ref index, 3, true, row.PrimaryObjective3Identifier, row.PrimaryObjective3UniqueValues, 0);
        return result;
    }

    private static LevelObjectiveDefinition[] BuildOptionalObjectives(LevelTable row)
    {
        var result = new LevelObjectiveDefinition[CountConfigured(
            row.OptionalObjective1Identifier,
            row.OptionalObjective2Identifier,
            row.OptionalObjective3Identifier,
            row.OptionalObjective4Identifier,
            row.OptionalObjective5Identifier)];
        int index = 0;
        AddConfigured(result, ref index, 1, false, row.OptionalObjective1Identifier, row.OptionalObjective1UniqueValues, row.OptionalObjective1Experience);
        AddConfigured(result, ref index, 2, false, row.OptionalObjective2Identifier, row.OptionalObjective2UniqueValues, row.OptionalObjective2Experience);
        AddConfigured(result, ref index, 3, false, row.OptionalObjective3Identifier, row.OptionalObjective3UniqueValues, row.OptionalObjective3Experience);
        AddConfigured(result, ref index, 4, false, row.OptionalObjective4Identifier, row.OptionalObjective4UniqueValues, row.OptionalObjective4Experience);
        AddConfigured(result, ref index, 5, false, row.OptionalObjective5Identifier, row.OptionalObjective5UniqueValues, row.OptionalObjective5Experience);
        return result;
    }

    private static void AddConfigured(
        LevelObjectiveDefinition[] result,
        ref int index,
        int slot,
        bool isPrimary,
        string objectiveIdentifier,
        Fix64[] uniqueValues,
        int experience)
    {
        if (string.IsNullOrWhiteSpace(objectiveIdentifier))
        {
            if ((uniqueValues != null && uniqueValues.Length > 0)
                || experience != 0)
            {
                throw new InvalidOperationException(
                    $"Unconfigured objective slot {slot} contains value or experience data.");
            }
            return;
        }

        result[index++] = new LevelObjectiveDefinition(
            slot,
            isPrimary,
            objectiveIdentifier,
            uniqueValues,
            experience);
    }

    private static int CountConfigured(params string[] objectiveIdentifiers)
    {
        int count = 0;
        for (int i = 0; i < objectiveIdentifiers.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(objectiveIdentifiers[i]))
                count++;
        }
        return count;
    }

    private static LevelObjectiveDefinition[] CloneDefinitions(LevelObjectiveDefinition[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<LevelObjectiveDefinition>();
        var result = new LevelObjectiveDefinition[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            LevelObjectiveDefinition item = source[i]
                ?? throw new InvalidOperationException($"Objective definition is null at index {i}.");
            result[i] = new LevelObjectiveDefinition(
                item.Slot,
                item.IsPrimary,
                item.ObjectiveIdentifier,
                item.UniqueValues,
                item.Experience);
        }
        return result;
    }
}
