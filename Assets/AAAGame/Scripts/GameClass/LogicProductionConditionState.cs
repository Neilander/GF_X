using System;
using System.Collections.Generic;

public static class LogicProductionConditionState
{
    private static readonly Dictionary<string, int> s_KillCountByStrongholdDay = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> s_HeavyKillCountByStrongholdDay = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> s_SurvivorCountByStrongholdDay = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_BuildingDamagedByStrongholdDay = new(StringComparer.Ordinal);
    private static readonly List<string> s_DeterministicStringKeys = new List<string>();

    public static void ClearAll()
    {
        s_KillCountByStrongholdDay.Clear();
        s_HeavyKillCountByStrongholdDay.Clear();
        s_SurvivorCountByStrongholdDay.Clear();
        s_BuildingDamagedByStrongholdDay.Clear();
    }

    public static int GetKillCount(string strongholdId, int day) =>
        GetValue(s_KillCountByStrongholdDay, BuildKey(strongholdId, day));

    public static int GetHeavyKillCount(string strongholdId, int day) =>
        GetValue(s_HeavyKillCountByStrongholdDay, BuildKey(strongholdId, day));

    public static int GetSurvivorCount(string strongholdId, int day) =>
        GetValue(s_SurvivorCountByStrongholdDay, BuildKey(strongholdId, day));

    public static bool WasBuildingDamaged(string strongholdId, int day) =>
        !string.IsNullOrWhiteSpace(strongholdId)
        && s_BuildingDamagedByStrongholdDay.Contains(BuildKey(strongholdId, day));

    public static int GetPlayerOccupiedStrongholdCount()
    {
        return LogicStrongholdMap.CountOwnedStrongholds(EntitySideHelper.PlayerFactionId);
    }

    public static int GetBuildingCount(string strongholdId, string identifierPrefix)
    {
        if (string.IsNullOrWhiteSpace(strongholdId) || string.IsNullOrWhiteSpace(identifierPrefix))
            return 0;

        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || !building.Alive
                || building.IsDisabled
                || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal)
                || building.BuildingData == null)
            {
                continue;
            }

            if (building.BuildingData.Identifier.StartsWith(identifierPrefix, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    public static int GetBuildingCount(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is required.", nameof(strongholdId));

        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                && building.Alive
                && !building.IsDisabled
                && string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    public static int GetTroopCount(string strongholdId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            return 0;
        LogicStrongholdMap.EnsureInitialized();

        SideType expectedSide = ResolveStrongholdSideRequired(strongholdId);
        int count = 0;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is LogicEntityState state
                && !state.IsBuildingEntity
                && state.Alive
                && state.Side == expectedSide
                && LogicStrongholdMap.TryResolveStrongholdId(state.Position, out string currentStrongholdId)
                && string.Equals(currentStrongholdId, strongholdId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    public static void RecordUnitDeath(LogicEntityState victim)
    {
        if (victim == null || victim.Side != SideType.EnemySide)
            return;
        if (!LogicStrongholdMap.TryResolveStrongholdId(victim.Position, out string strongholdId))
            return;

        int day = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        string key = BuildKey(strongholdId, day);
        Increment(s_KillCountByStrongholdDay, key);
        if (victim.CharacterData != null
            && (victim.CharacterData.Size == UnitSize.Large || victim.CharacterData.Size == UnitSize.SuperLarge))
        {
            Increment(s_HeavyKillCountByStrongholdDay, key);
        }
    }

    public static void RecordBuildingDamaged(IBuildingLogicContext building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.StrongholdId))
            return;

        int day = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        s_BuildingDamagedByStrongholdDay.Add(BuildKey(building.StrongholdId, day));
    }

    public static void SnapshotSurvivors(int day)
    {
        LogicStrongholdMap.EnsureInitialized();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is LogicEntityState state)
                || state.IsBuildingEntity
                || !state.Alive
                || !LogicStrongholdMap.TryResolveStrongholdId(state.Position, out string strongholdId))
            {
                continue;
            }

            if (state.Side != ResolveStrongholdSideRequired(strongholdId))
                continue;

            counts.TryGetValue(strongholdId, out int current);
            counts[strongholdId] = current + 1;
        }

        IReadOnlyList<string> strongholdIds = LogicStrongholdMap.GetStrongholdIdsOrdered();
        for (int i = 0; i < strongholdIds.Count; i++)
        {
            string strongholdId = strongholdIds[i];
            counts.TryGetValue(strongholdId, out int count);
            s_SurvivorCountByStrongholdDay[BuildKey(strongholdId, day)] = count;
        }
    }

    public static void SetKillCount(string strongholdId, int day, int count)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("Stronghold id is empty.", nameof(strongholdId));
        s_KillCountByStrongholdDay[BuildKey(strongholdId, day)] = Math.Max(0, count);
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        AddSortedDictionary(hasher, s_KillCountByStrongholdDay);
        AddSortedDictionary(hasher, s_HeavyKillCountByStrongholdDay);
        AddSortedDictionary(hasher, s_SurvivorCountByStrongholdDay);
        FillSortedStringKeys(s_BuildingDamagedByStrongholdDay);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
            hasher.Add(s_DeterministicStringKeys[i]);
    }

    private static int GetValue(Dictionary<string, int> values, string key) =>
        values.TryGetValue(key, out int value) ? value : 0;

    private static void Increment(Dictionary<string, int> values, string key)
    {
        values.TryGetValue(key, out int current);
        values[key] = checked(current + 1);
    }

    private static string BuildKey(string strongholdId, int day) =>
        $"{strongholdId ?? string.Empty}_{Math.Max(1, day)}";

    private static SideType ResolveStrongholdSideRequired(string strongholdId)
    {
        int ownerFactionId = LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId);
        return EntitySideHelper.ToSide(
            EntityCombatTeamHelper.ResolveTeamIdByFaction(ownerFactionId));
    }

    private static void AddSortedDictionary(LogicStateHasher hasher, Dictionary<string, int> values)
    {
        FillSortedStringKeys(values.Keys);
        hasher.Add(s_DeterministicStringKeys.Count);
        for (int i = 0; i < s_DeterministicStringKeys.Count; i++)
        {
            string key = s_DeterministicStringKeys[i];
            hasher.Add(key);
            hasher.Add(values[key]);
        }
    }

    private static void FillSortedStringKeys(IEnumerable<string> values)
    {
        s_DeterministicStringKeys.Clear();
        s_DeterministicStringKeys.AddRange(values);
        s_DeterministicStringKeys.Sort(StringComparer.Ordinal);
    }
}
