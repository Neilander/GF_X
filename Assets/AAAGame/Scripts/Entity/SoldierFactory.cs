using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// Soldier factory.
/// </summary>
public static class SoldierFactory
{
    private static readonly HashSet<string> LoggedPrefabSourceCharacterKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Unified soldier remove entry via HideEntity.
    /// </summary>
    public static bool RemoveSoldier(SoldierEntity soldier)
    {
        GF.Entity.HideEntity(soldier.Entity);
        return true;
    }

    /// <summary>
    /// Remove all SoldierEntity in Creature group.
    /// </summary>
    public static void RemoveAllSoldiersInCreatureGroup()
    {
        var creatureGroup = GF.Entity.GetEntityGroup(Const.EntityGroup.Creature.ToString());
        var entities = creatureGroup.GetAllEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] is Entity entity && entity.Logic is SoldierEntity soldier)
            {
                RemoveSoldier(soldier);
            }
        }
    }

    /// <summary>
    /// Show one soldier entity.
    /// </summary>
    /// <param name="index">Unit type index.</param>
    /// <param name="position">Spawn position.</param>
    /// <param name="side">Side.</param>
    /// <param name="brainType">Brain type.</param>
    public static int ShowSoldier(UnitType unitType, Vector3 position, SideType side = SideType.PlayerSide, BrainType brainType = BrainType.SoldierAI, string sourceBuildingInstanceId = null, string sourceStrongholdId = null)
    {
        string characterKey = unitType.ToString();
        string prefabName = GetPrefabPathFromCharacterData(characterKey);
        Const.EntityGroup entityGroup = unitType == UnitType.Unit_Hero ? Const.EntityGroup.Player : Const.EntityGroup.Creature;

        // Build start buffs list.
        var startBuffs = new System.Collections.Generic.List<BuffData>();
        AddInitialBuffs(startBuffs, unitType);
        AddGlobalBuffs(startBuffs, unitType, side);
        AddBuildingBuffs(startBuffs, sourceBuildingInstanceId, side);

        // Keep OnShowCallback empty here.
        // Buff setup occurs in existing show-success chain.

        return MAEntityFactory.ShowSoldier(prefabName, characterKey, position, side, brainType, entityGroup, startBuffs, sourceStrongholdId);
    }

    public static async UniTask<bool> ShowSoldierAwait(
        UnitType unitType,
        Vector3 position,
        SideType side = SideType.PlayerSide,
        BrainType brainType = BrainType.SoldierAI,
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null)
    {
        string characterKey = unitType.ToString();
        string prefabName = GetPrefabPathFromCharacterData(characterKey);
        Const.EntityGroup entityGroup = unitType == UnitType.Unit_Hero ? Const.EntityGroup.Player : Const.EntityGroup.Creature;

        var startBuffs = new System.Collections.Generic.List<BuffData>();
        AddInitialBuffs(startBuffs, unitType);
        AddGlobalBuffs(startBuffs, unitType, side);
        AddBuildingBuffs(startBuffs, sourceBuildingInstanceId, side);

        EntityParams entityParams = MAEntityFactory.CreateMAEntityParams(position, characterKey, side, brainType, startBuffs, sourceStrongholdId);
        var logic = await GF.Entity.ShowEntityAwait<SoldierEntity>(prefabName, entityGroup, entityParams);
        return logic != null;
    }

    private static string GetPrefabPathFromCharacterData(string characterKey)
    {
        var row = GetCharacterDataRow(characterKey);

        if (string.IsNullOrWhiteSpace(row.PrefabPath))
            throw new InvalidOperationException($"SoldierFactory.ShowSoldier failed: CharacterDataDetail.PrefabPath is empty. CharacterKey={characterKey}.");

        if (LoggedPrefabSourceCharacterKeys.Add(characterKey))
            Log.Info("[SoldierFactory] Unit prefab source: CharacterDataDetail.PrefabPath. CharacterKey={0}, PrefabPath={1}.", characterKey, row.PrefabPath);

        return row.PrefabPath;
    }

    private static CharacterDataDetail GetCharacterDataRow(string characterKey)
    {
        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            throw new InvalidOperationException("SoldierFactory.ShowSoldier failed: CharacterDataDetail data table is null.");

        var row = table.GetDataRow(r => r.CharacterKey == characterKey);
        if (row == null)
            throw new InvalidOperationException($"SoldierFactory.ShowSoldier failed: CharacterDataDetail row not found. CharacterKey={characterKey}.");

        return row;
    }

    private static Fix64 GetFirstUniqueValue(UnitType unitType)
    {
        string characterKey = unitType.ToString();
        var row = GetCharacterDataRow(characterKey);
        if (row.UniqueValues == null || row.UniqueValues.Length == 0)
            throw new InvalidOperationException($"SoldierFactory.AddInitialBuffs failed: CharacterDataDetail.UniqueValues is empty. CharacterKey={characterKey}.");

        return row.UniqueValues[0];
    }

    /// <summary>
    /// Add initial buffs to list.
    /// </summary>
    private static void AddInitialBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType index)
    {
        switch (index)
        {
            case UnitType.Unit_Coder:
                buffList.Add(TimedDeathBuff.CreateTimedDeath(35f));
                break;

            case UnitType.Unit_BoneButcher:
                buffList.Add(OnKillHealBuff.CreateOnKillHeal((float)GetFirstUniqueValue(index)));
                break;

            case UnitType.Unit_Scapegoat:
                buffList.Add(TauntBuffCallback.CreateTaunt(1));
                break;
        }
    }

    private static void AddGlobalBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType unitType, SideType side)
    {
        var globalBuffManager = GameEntry.GetComponent<GlobalBuffManager>();
        if (globalBuffManager == null)
            return;

        int factionId = EntitySideHelper.ToFactionId(side);
        var globalBuffs = globalBuffManager.GetBuffs(unitType, factionId);
        if (globalBuffs == null || globalBuffs.Count == 0)
            return;

        buffList.AddRange(globalBuffs);
    }

    private static void AddBuildingBuffs(System.Collections.Generic.List<BuffData> buffList, string sourceBuildingInstanceId, SideType side)
    {
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            return;

        var globalBuffManager = GameEntry.GetComponent<GlobalBuffManager>();
        if (globalBuffManager == null)
            return;

        int factionId = EntitySideHelper.ToFactionId(side);
        var buildingBuffs = globalBuffManager.GetBuffsForBuilding(sourceBuildingInstanceId, factionId);
        if (buildingBuffs == null || buildingBuffs.Count == 0)
            return;

        buffList.AddRange(buildingBuffs);
    }
}



