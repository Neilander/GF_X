using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public readonly struct StageStringIntValue
{
    public StageStringIntValue(string key, int value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public int Value { get; }
}

public sealed class StageTechOwnership
{
    internal StageTechOwnership(string techId, string[] ownerBuildingInstanceIds)
    {
        TechId = techId;
        OwnerBuildingInstanceIds = Array.AsReadOnly((string[])ownerBuildingInstanceIds.Clone());
    }

    public string TechId { get; }
    public ReadOnlyCollection<string> OwnerBuildingInstanceIds { get; }
}

public sealed class InGameDataCheckpoint
{
    internal InGameDataCheckpoint(
        int[] values,
        StageTechOwnership[] techOwnership,
        StageStringIntValue[] productionReserves,
        StageStringIntValue[] buildingCosts)
    {
        Values = Array.AsReadOnly((int[])values.Clone());
        TechOwnership = Array.AsReadOnly((StageTechOwnership[])techOwnership.Clone());
        ProductionReserves = Array.AsReadOnly((StageStringIntValue[])productionReserves.Clone());
        BuildingCosts = Array.AsReadOnly((StageStringIntValue[])buildingCosts.Clone());

        var hasher = new LogicStateHasher();
        WriteState(hasher);
        ContentHash = hasher.Hash;
    }

    public ReadOnlyCollection<int> Values { get; }
    public ReadOnlyCollection<StageTechOwnership> TechOwnership { get; }
    public ReadOnlyCollection<StageStringIntValue> ProductionReserves { get; }
    public ReadOnlyCollection<StageStringIntValue> BuildingCosts { get; }
    public ulong ContentHash { get; }

    internal void WriteState(LogicStateHasher hasher)
    {
        hasher.Add(0x535447494E47414DUL);
        hasher.Add(Values.Count);
        for (int i = 0; i < Values.Count; i++)
            hasher.Add(Values[i]);

        hasher.Add(TechOwnership.Count);
        for (int i = 0; i < TechOwnership.Count; i++)
        {
            StageTechOwnership entry = TechOwnership[i];
            hasher.Add(entry.TechId);
            hasher.Add(entry.OwnerBuildingInstanceIds.Count);
            for (int ownerIndex = 0; ownerIndex < entry.OwnerBuildingInstanceIds.Count; ownerIndex++)
                hasher.Add(entry.OwnerBuildingInstanceIds[ownerIndex]);
        }

        WriteValues(hasher, ProductionReserves);
        WriteValues(hasher, BuildingCosts);
    }

    private static void WriteValues(LogicStateHasher hasher, ReadOnlyCollection<StageStringIntValue> values)
    {
        hasher.Add(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            hasher.Add(values[i].Key);
            hasher.Add(values[i].Value);
        }
    }
}

public partial class InGameDataModel
{
    public static InGameDataCheckpoint CaptureStageCheckpointState()
    {
        InGameDataModel model = GetModel()
                                ?? throw new InvalidOperationException("Cannot capture stage checkpoint without InGameDataModel.");
        if (model.m_IngameValue == null
            || model.m_TechOwnerContextsById == null
            || model.m_ProductionBuildingCoinReservesByInstanceId == null
            || model.m_BuildingCostSpentByInstanceId == null)
        {
            throw new InvalidOperationException("Cannot capture an uninitialized InGameDataModel stage checkpoint.");
        }
        int valueCount = (int)IngameValueType.MaxSupply + 1;
        var values = new int[valueCount];
        for (int i = 0; i < valueCount; i++)
            values[i] = model.m_IngameValue.TryGetValue((IngameValueType)i, out int value) ? value : 0;

        var techIds = new List<string>(model.m_TechOwnerContextsById.Keys);
        techIds.Sort(StringComparer.Ordinal);
        var techOwnership = new StageTechOwnership[techIds.Count];
        for (int i = 0; i < techIds.Count; i++)
        {
            string techId = techIds[i];
            HashSet<string> ownerSet = model.m_TechOwnerContextsById[techId]
                                       ?? throw new InvalidOperationException($"Cannot capture null tech owner set. techId='{techId}'.");
            var owners = new List<string>(ownerSet);
            owners.Sort(StringComparer.Ordinal);
            techOwnership[i] = new StageTechOwnership(techId, owners.ToArray());
        }

        return new InGameDataCheckpoint(
            values,
            techOwnership,
            CaptureStringIntValues(model.m_ProductionBuildingCoinReservesByInstanceId),
            CaptureStringIntValues(model.m_BuildingCostSpentByInstanceId));
    }

    public static void RestoreStageCheckpointState(InGameDataCheckpoint checkpoint, bool triggerEvents = true)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));
        InGameDataModel model = GetModel()
                                ?? throw new InvalidOperationException("Cannot restore stage checkpoint without InGameDataModel.");

        Dictionary<IngameValueType, int> values = ValidateAndCreateValues(checkpoint.Values);
        Dictionary<string, HashSet<string>> techOwnership = ValidateAndCreateTechOwnership(checkpoint.TechOwnership);
        Dictionary<string, int> reserves = ValidateAndCreateStringIntValues(checkpoint.ProductionReserves, "production reserve");
        Dictionary<string, int> costs = ValidateAndCreateStringIntValues(checkpoint.BuildingCosts, "building cost");
        var oldValues = new Dictionary<IngameValueType, int>(model.m_IngameValue);

        model.m_IngameValue = values;
        model.m_TechOwnerContextsById = techOwnership;
        model.m_ProductionBuildingCoinReservesByInstanceId = reserves;
        model.m_BuildingCostSpentByInstanceId = costs;
        var unlockedTechIds = new List<string>(techOwnership.Keys);
        unlockedTechIds.Sort(StringComparer.Ordinal);
        model.UnlockedTechIds = unlockedTechIds.ToArray();

        if (!triggerEvents)
            return;
        for (int i = 0; i < checkpoint.Values.Count; i++)
        {
            var type = (IngameValueType)i;
            int oldValue = oldValues.TryGetValue(type, out int previous) ? previous : 0;
            int newValue = checkpoint.Values[i];
            if (oldValue == newValue)
                continue;
            QueueValuePresentation(type, oldValue, newValue);
        }
    }

    private static StageStringIntValue[] CaptureStringIntValues(Dictionary<string, int> source)
    {
        var keys = new List<string>(source.Keys);
        keys.Sort(StringComparer.Ordinal);
        var values = new StageStringIntValue[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            values[i] = new StageStringIntValue(keys[i], source[keys[i]]);
        return values;
    }

    private static Dictionary<IngameValueType, int> ValidateAndCreateValues(ReadOnlyCollection<int> source)
    {
        int requiredCount = (int)IngameValueType.MaxSupply + 1;
        if (source.Count != requiredCount)
            throw new InvalidOperationException($"Stage checkpoint value count mismatch. expected={requiredCount}, actual={source.Count}.");
        if (!Enum.IsDefined(typeof(GamePhase), source[(int)IngameValueType.Phase]))
            throw new InvalidOperationException($"Stage checkpoint has invalid phase {source[(int)IngameValueType.Phase]}.");

        var result = new Dictionary<IngameValueType, int>(requiredCount);
        for (int i = 0; i < requiredCount; i++)
        {
            int value = source[i];
            if (i != (int)IngameValueType.Phase && value < 0)
                throw new InvalidOperationException($"Stage checkpoint value is negative. type={(IngameValueType)i}, value={value}.");
            result.Add((IngameValueType)i, value);
        }
        if (result[IngameValueType.CurrentSupply] > result[IngameValueType.MaxSupply])
            throw new InvalidOperationException("Stage checkpoint current supply exceeds max supply.");
        return result;
    }

    private static Dictionary<string, HashSet<string>> ValidateAndCreateTechOwnership(
        ReadOnlyCollection<StageTechOwnership> source)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            StageTechOwnership entry = source[i]
                                       ?? throw new InvalidOperationException($"Stage checkpoint tech entry is null. index={i}.");
            if (string.IsNullOrWhiteSpace(entry.TechId))
                throw new InvalidOperationException($"Stage checkpoint tech id is empty. index={i}.");
            var owners = new HashSet<string>(StringComparer.Ordinal);
            for (int ownerIndex = 0; ownerIndex < entry.OwnerBuildingInstanceIds.Count; ownerIndex++)
            {
                string owner = entry.OwnerBuildingInstanceIds[ownerIndex];
                if (string.IsNullOrWhiteSpace(owner) || !owners.Add(owner))
                    throw new InvalidOperationException($"Stage checkpoint has invalid duplicate tech owner. techId='{entry.TechId}', owner='{owner}'.");
            }
            if (owners.Count == 0 || !result.TryAdd(entry.TechId, owners))
                throw new InvalidOperationException($"Stage checkpoint has invalid tech ownership. techId='{entry.TechId}'.");
        }
        return result;
    }

    private static Dictionary<string, int> ValidateAndCreateStringIntValues(
        ReadOnlyCollection<StageStringIntValue> source,
        string field)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            StageStringIntValue entry = source[i];
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0 || !result.TryAdd(entry.Key, entry.Value))
                throw new InvalidOperationException($"Stage checkpoint has invalid {field}. key='{entry.Key}', value={entry.Value}.");
        }
        return result;
    }
}
