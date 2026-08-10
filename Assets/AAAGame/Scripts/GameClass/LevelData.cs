using System;

public static class LevelObjectiveIds
{
    public const int CaptureStrongholdCount = 3;
    public const int CaptureSpecificStrongholds = 5;
    public const int SurviveDays = 12;
    public const int ProtectStronghold = 20;
    public const int DefendBase = 25;
    public const int UpgradeCodingCoreLevel3 = 26;
}

public static class LevelObjectiveTargetIds
{
    public const string InitialEnemyConditionBuildings = "InitialEnemyConditionBuildings";
    public const string InitialPlayerConditionBuildings = "InitialPlayerConditionBuildings";
    public const string Day = "Day";
    public const string Tutorial = "Tutorial";
}

public sealed class LevelObjectiveDefinition
{
    public LevelObjectiveDefinition(
        int slot,
        bool isPrimary,
        int definitionId,
        string[] targetIds,
        Fix64[] uniqueValues,
        int experience)
    {
        if (slot <= 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (definitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(definitionId));
        if (experience < 0)
            throw new ArgumentOutOfRangeException(nameof(experience));

        Slot = slot;
        IsPrimary = isPrimary;
        DefinitionId = definitionId;
        TargetIds = targetIds == null ? Array.Empty<string>() : (string[])targetIds.Clone();
        UniqueValues = uniqueValues == null ? Array.Empty<Fix64>() : (Fix64[])uniqueValues.Clone();
        Experience = experience;
    }

    public int Slot { get; }
    public bool IsPrimary { get; }
    public int DefinitionId { get; }
    public string[] TargetIds { get; }
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
            row.PrimaryObjective1Id,
            row.PrimaryObjective2Id,
            row.PrimaryObjective3Id)];
        int index = 0;
        AddConfigured(result, ref index, 1, true, row.PrimaryObjective1Id, row.PrimaryObjective1TargetIds, row.PrimaryObjective1UniqueValues, 0);
        AddConfigured(result, ref index, 2, true, row.PrimaryObjective2Id, row.PrimaryObjective2TargetIds, row.PrimaryObjective2UniqueValues, 0);
        AddConfigured(result, ref index, 3, true, row.PrimaryObjective3Id, row.PrimaryObjective3TargetIds, row.PrimaryObjective3UniqueValues, 0);
        return result;
    }

    private static LevelObjectiveDefinition[] BuildOptionalObjectives(LevelTable row)
    {
        var result = new LevelObjectiveDefinition[CountConfigured(
            row.OptionalObjective1Id,
            row.OptionalObjective2Id,
            row.OptionalObjective3Id,
            row.OptionalObjective4Id,
            row.OptionalObjective5Id)];
        int index = 0;
        AddConfigured(result, ref index, 1, false, row.OptionalObjective1Id, row.OptionalObjective1TargetIds, row.OptionalObjective1UniqueValues, row.OptionalObjective1Experience);
        AddConfigured(result, ref index, 2, false, row.OptionalObjective2Id, row.OptionalObjective2TargetIds, row.OptionalObjective2UniqueValues, row.OptionalObjective2Experience);
        AddConfigured(result, ref index, 3, false, row.OptionalObjective3Id, row.OptionalObjective3TargetIds, row.OptionalObjective3UniqueValues, row.OptionalObjective3Experience);
        AddConfigured(result, ref index, 4, false, row.OptionalObjective4Id, row.OptionalObjective4TargetIds, row.OptionalObjective4UniqueValues, row.OptionalObjective4Experience);
        AddConfigured(result, ref index, 5, false, row.OptionalObjective5Id, row.OptionalObjective5TargetIds, row.OptionalObjective5UniqueValues, row.OptionalObjective5Experience);
        return result;
    }

    private static void AddConfigured(
        LevelObjectiveDefinition[] result,
        ref int index,
        int slot,
        bool isPrimary,
        int definitionId,
        string[] targetIds,
        Fix64[] uniqueValues,
        int experience)
    {
        if (definitionId == 0)
        {
            if ((targetIds != null && targetIds.Length > 0)
                || (uniqueValues != null && uniqueValues.Length > 0)
                || experience != 0)
            {
                throw new InvalidOperationException(
                    $"Unconfigured objective slot {slot} contains target, value, or experience data.");
            }
            return;
        }

        result[index++] = new LevelObjectiveDefinition(
            slot,
            isPrimary,
            definitionId,
            targetIds,
            uniqueValues,
            experience);
    }

    private static int CountConfigured(params int[] definitionIds)
    {
        int count = 0;
        for (int i = 0; i < definitionIds.Length; i++)
        {
            if (definitionIds[i] > 0)
                count++;
            else if (definitionIds[i] < 0)
                throw new InvalidOperationException($"Objective definition id cannot be negative: {definitionIds[i]}.");
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
                item.DefinitionId,
                item.TargetIds,
                item.UniqueValues,
                item.Experience);
        }
        return result;
    }
}
