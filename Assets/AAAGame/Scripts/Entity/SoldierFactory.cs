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
    private const float NurseAmmoDepletedDeathDelaySeconds = 0.6f;
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
    public static int ShowSoldier(
        UnitType unitType,
        Vector3 position,
        SideType side = SideType.PlayerSide,
        BrainType brainType = BrainType.SoldierAI,
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null,
        System.Action<EntityParams> configureParams = null)
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

        return MAEntityFactory.ShowSoldier(prefabName, characterKey, position, side, brainType, entityGroup, startBuffs, sourceStrongholdId, configureParams);
    }

    public static async UniTask<bool> ShowSoldierAwait(
        UnitType unitType,
        Vector3 position,
        SideType side = SideType.PlayerSide,
        BrainType brainType = BrainType.SoldierAI,
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null,
        Func<bool> keepAlivePredicate = null)
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
        if (logic != null && keepAlivePredicate != null && !keepAlivePredicate())
        {
            GF.Entity.HideEntitySafe(logic);
            return false;
        }

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

    private static Fix64[] GetUniqueValues(UnitType unitType, int requiredCount)
    {
        string characterKey = unitType.ToString();
        var row = GetCharacterDataRow(characterKey);
        if (row.UniqueValues == null || row.UniqueValues.Length < requiredCount)
            throw new InvalidOperationException($"SoldierFactory.AddInitialBuffs failed: CharacterDataDetail.UniqueValues count is less than {requiredCount}. CharacterKey={characterKey}.");

        return row.UniqueValues;
    }

    private static BuffData CreateInitialBuff(string id, bool isForever, float duration, params BuffCallback[] modules)
    {
        if (modules == null || modules.Length == 0)
            throw new InvalidOperationException($"SoldierFactory.CreateInitialBuff failed: modules is empty. BuffId={id}.");

        return BuffData.Create(
            id: id,
            duration: duration,
            isForever: isForever,
            maxStack: 1,
            modules: new List<BuffCallback>(modules));
    }

    /// <summary>
    /// Add initial buffs to list.
    /// </summary>
    private static void AddInitialBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType index)
    {
        switch (index)
        {
            case UnitType.Unit_Intern:
                buffList.Add(TimedDeathBuff.CreateTimedDeath(35f));
                break;

            case UnitType.Unit_BoneButcher:
                buffList.Add(OnKillHealBuff.CreateOnKillHeal((float)GetFirstUniqueValue(index)));
                break;

            case UnitType.Unit_Scapegoat:
                buffList.Add(TauntBuffCallback.CreateTaunt(1));
                break;

            case UnitType.Unit_ColdCarrier:
            {
                Fix64[] values = GetUniqueValues(index, 2);
                buffList.Add(CreateInitialBuff(
                    "unit_cold_carrier_excess_damage_reduction",
                    true,
                    float.MaxValue,
                    new ExcessDamageReductionBuff(values[0], values[1])));
                break;
            }

            case UnitType.Unit_RiotGuard:
                buffList.Add(TauntBuffCallback.CreateTaunt((int)GetFirstUniqueValue(index)));
                break;

            case UnitType.Unit_Poacher:
                buffList.Add(CreateInitialBuff(
                    "unit_poacher_first_hit_critical",
                    true,
                    float.MaxValue,
                    new FirstHitPerTargetCriticalBuff()));
                break;

            case UnitType.Unit_Gardener:
                buffList.Add(CreateInitialBuff(
                    "unit_gardener_high_health_critical",
                    true,
                    float.MaxValue,
                    new HighHealthTargetCriticalBuff(GetFirstUniqueValue(index))));
                break;

            case UnitType.Unit_Surgeon:
                buffList.Add(TimedDeathBuff.CreateTimedDeath((float)GetFirstUniqueValue(index)));
                break;

            case UnitType.Unit_Nurse:
                buffList.Add(CreateInitialBuff(
                    "unit_nurse_ammo_depleted_death",
                    true,
                    float.MaxValue,
                    new AmmoDepletedDeathBuff(NurseAmmoDepletedDeathDelaySeconds)));
                break;

            case UnitType.Unit_Brat:
                buffList.Add(CreateInitialBuff(
                    "unit_brat_nearby_enemy_attack_lock",
                    true,
                    float.MaxValue,
                    new NearbyEnemyAttackLockBuff(GetFirstUniqueValue(index))));
                break;

            case UnitType.Unit_LateRider:
            {
                Fix64[] values = GetUniqueValues(index, 5);
                buffList.Add(CreateInitialBuff(
                    "unit_late_rider_charge",
                    true,
                    float.MaxValue,
                    new LateRiderChargeBuff(
                        values[0],
                        values[1],
                        values[2],
                        values[3],
                        (float)values[4])));
                break;
            }

            case UnitType.Unit_Sprinter:
            {
                Fix64[] values = GetUniqueValues(index, 5);
                buffList.Add(CreateInitialBuff(
                    "unit_sprinter_deploy_boost",
                    false,
                    (float)values[0],
                    new PercentAttackBonusBuff(values[1]),
                    new AttackSpeedBonusBuff(values[2]),
                    new RevertibleMoveSpeedBonusBuff(values[3]),
                    new PercentDamageReductionBuff(values[4])));
                break;
            }

            case UnitType.Unit_JavelinThrower:
                buffList.Add(CreateInitialBuff(
                    "unit_javelin_thrower_critical_and_drain",
                    true,
                    float.MaxValue,
                    new AlwaysCriticalDamageBuff(),
                    new HealthDrainOverTimeBuff(GetFirstUniqueValue(index))));
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



