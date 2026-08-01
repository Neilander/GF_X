using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Scripts.BuffSystem;

/// <summary>
/// Soldier factory.
/// </summary>
public static class SoldierFactory
{
    private static readonly Fix64 NurseAmmoDepletedDeathDelaySeconds = Fix64.FromRaw(2458);
    private static readonly HashSet<string> LoggedPrefabSourceCharacterKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Unified soldier remove entry via the authoritative lifecycle timeline.
    /// </summary>
    public static bool RemoveSoldier(SoldierEntity soldier)
    {
        if (soldier == null)
            throw new ArgumentNullException(nameof(soldier));

        soldier.RequestDespawn();
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

    public static LogicEntityId ShowSoldierFixed(
        UnitType unitType,
        FixVector2 position,
        float viewY,
        SideType side = SideType.PlayerSide,
        BrainType brainType = BrainType.SoldierAI,
        string sourceBuildingInstanceId = null,
        string sourceStrongholdId = null,
        System.Action<EntityParams> configureParams = null,
        int unitLevel = 1)
    {
        unitLevel = NormalizeUnitLevel(unitLevel);
        string characterKey = unitType.ToString();
        long stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        string prefabName = GetPrefabPathFromCharacterData(characterKey);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.SoldierResolvePrefab,
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);
        Const.EntityGroup entityGroup = unitType == UnitType.Unit_Hero ? Const.EntityGroup.Player : Const.EntityGroup.Creature;
        stageStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        List<BuffData> startBuffs = CreateStartBuffs(unitType, unitLevel, side, sourceBuildingInstanceId);
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.SoldierCreateBuffs,
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartTicks);

        if (unitType == UnitType.Unit_Hero)
        {
            return MAEntityFactory.ShowHeroFixed(
                prefabName,
                characterKey,
                position,
                viewY,
                side,
                brainType,
                entityGroup,
                startBuffs,
                sourceStrongholdId,
                configureParams,
                unitLevel);
        }

        return MAEntityFactory.ShowSoldierFixed(
            prefabName,
            characterKey,
            position,
            viewY,
            side,
            brainType,
            entityGroup,
            startBuffs,
            sourceStrongholdId,
            configureParams,
            unitLevel);
    }

    private static List<BuffData> CreateStartBuffs(
        UnitType unitType,
        int unitLevel,
        SideType side,
        string sourceBuildingInstanceId)
    {
        var startBuffs = new List<BuffData>();
        AddInitialBuffs(startBuffs, unitType, unitLevel);
        AddGlobalBuffs(startBuffs, unitType, side);
        AddBuildingBuffs(startBuffs, sourceBuildingInstanceId, side);
        return startBuffs;
    }

    private static string GetPrefabPathFromCharacterData(string characterKey)
    {
        var row = GetCharacterDataRow(characterKey);

        if (string.IsNullOrWhiteSpace(row.PrefabPath))
            throw new InvalidOperationException($"SoldierFactory.ShowSoldier failed: CharacterDataDetail.PrefabPath is empty. CharacterKey={characterKey}.");

        LoggedPrefabSourceCharacterKeys.Add(characterKey);

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

    private static BuffData CreateInitialBuff(string id, bool isForever, Fix64 duration, params BuffCallback[] modules)
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
    private static void AddInitialBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType index, int unitLevel)
    {
        unitLevel = NormalizeUnitLevel(unitLevel);
        ArmyLevelTechModifiers tech = ResolveArmyLevelTechModifiers(index, unitLevel);

        switch (index)
        {
            case UnitType.Unit_Intern:
                buffList.Add(TimedDeathBuff.CreateTimedDeath((Fix64)35 + tech.LifetimeSecondsDelta));
                break;

            case UnitType.Unit_BoneButcher:
                buffList.Add(OnKillHealBuff.CreateOnKillHeal(GetFirstUniqueValue(index) + tech.OnKillHealPercentDelta));
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
                    Fix64.Zero,
                    new ExcessDamageReductionBuff(values[0] + tech.ExcessDamageThresholdDelta, values[1] + tech.ExcessDamageReductionPercentDelta)));
                break;
            }

            case UnitType.Unit_RiotGuard:
                buffList.Add(TauntBuffCallback.CreateTaunt((int)(GetFirstUniqueValue(index) + tech.TauntLevelDelta)));
                break;

            case UnitType.Unit_Poacher:
                buffList.Add(CreateInitialBuff(
                    "unit_poacher_first_hit_critical",
                    true,
                    Fix64.Zero,
                    new FirstHitPerTargetCriticalBuff()));
                break;

            case UnitType.Unit_Gardener:
                buffList.Add(CreateInitialBuff(
                    "unit_gardener_high_health_critical",
                    true,
                    Fix64.Zero,
                    new HighHealthTargetCriticalBuff(GetFirstUniqueValue(index) + tech.GardenerThresholdPercentDelta)));
                break;

            case UnitType.Unit_Surgeon:
                buffList.Add(TimedDeathBuff.CreateTimedDeath(GetFirstUniqueValue(index) + tech.LifetimeSecondsDelta));
                break;

            case UnitType.Unit_Nurse:
                buffList.Add(CreateInitialBuff(
                    "unit_nurse_ammo_depleted_death",
                    true,
                    Fix64.Zero,
                    new AmmoDepletedDeathBuff(NurseAmmoDepletedDeathDelaySeconds)));
                break;

            case UnitType.Unit_Brat:
                buffList.Add(CreateInitialBuff(
                    "unit_brat_nearby_enemy_attack_lock",
                    true,
                    Fix64.Zero,
                    new NearbyEnemyAttackLockBuff(GetFirstUniqueValue(index))));
                break;

            case UnitType.Unit_LateRider:
            {
                Fix64[] values = GetUniqueValues(index, 5);
                buffList.Add(CreateInitialBuff(
                    "unit_late_rider_charge",
                    true,
                    Fix64.Zero,
                    new LateRiderChargeBuff(
                        values[0],
                        values[1] + tech.LateRiderMaxDistanceDelta,
                        values[2] + tech.LateRiderMoveSpeedDelta,
                        values[3] + tech.LateRiderAttackDelta,
                        values[4])));
                break;
            }

            case UnitType.Unit_Sprinter:
            {
                Fix64[] values = GetUniqueValues(index, 5);
                buffList.Add(CreateInitialBuff(
                    "unit_sprinter_deploy_boost",
                    false,
                    values[0] + tech.SprinterDurationDelta,
                    new PercentAttackBonusBuff(values[1] + tech.SprinterAttackPercentDelta),
                    new AttackSpeedBonusBuff(values[2] + tech.SprinterAttackSpeedPercentDelta),
                    new RevertibleMoveSpeedBonusBuff(values[3] + tech.SprinterMoveSpeedDelta),
                    new PercentDamageReductionBuff(values[4] + tech.SprinterDamageReductionPercentDelta)));
                break;
            }

            case UnitType.Unit_JavelinThrower:
                buffList.Add(CreateInitialBuff(
                    "unit_javelin_thrower_critical_and_drain",
                    true,
                    Fix64.Zero,
                    new AlwaysCriticalDamageBuff(),
                    new HealthDrainOverTimeBuff(GetFirstUniqueValue(index) + tech.HealthDrainPerSecondDelta)));
                break;

            case UnitType.Unit_HydroGunner:
                buffList.Add(CreateInitialBuff(
                    "unit_hydro_gunner_knockback",
                    true,
                    Fix64.Zero,
                    new KnockbackOnOutgoingDamageBuff(GetFirstUniqueValue(index) + tech.KnockbackLevel)));
                break;
        }

        AddArmyLevelTechBuffs(buffList, index, tech);
    }

    private static void AddArmyLevelTechBuffs(System.Collections.Generic.List<BuffData> buffList, UnitType unitType, ArmyLevelTechModifiers tech)
    {
        if (tech.AttackSpeedPercent != Fix64.Zero)
        {
            buffList.Add(CreateInitialBuff(
                $"army_level_tech_attack_speed_{unitType}",
                true,
                Fix64.Zero,
                new AttackSpeedBonusBuff(tech.AttackSpeedPercent)));
        }

        if (tech.CriticalDamageBonusPercent != Fix64.Zero)
        {
            buffList.Add(CreateInitialBuff(
                $"army_level_tech_critical_damage_{unitType}",
                true,
                Fix64.Zero,
                new CriticalDamageBonusBuff(tech.CriticalDamageBonusPercent)));
        }

        if (tech.HealOnHit != Fix64.Zero)
        {
            buffList.Add(CreateInitialBuff(
                $"army_level_tech_heal_on_hit_{unitType}",
                true,
                Fix64.Zero,
                new HealOnOutgoingDamageBuff(tech.HealOnHit)));
        }

        if (tech.KnockbackLevel != Fix64.Zero && unitType != UnitType.Unit_HydroGunner)
        {
            buffList.Add(CreateInitialBuff(
                $"army_level_tech_knockback_{unitType}",
                true,
                Fix64.Zero,
                new KnockbackOnOutgoingDamageBuff(tech.KnockbackLevel)));
        }
    }

    private static ArmyLevelTechModifiers ResolveArmyLevelTechModifiers(UnitType unitType, int unitLevel)
    {
        var modifiers = new ArmyLevelTechModifiers();
        if (unitLevel <= 1)
            return modifiers;

        BuildingTable row = FindArmyBuildingRow(unitType);
        if (row == null)
            return modifiers;

        Fix64[] lv2 = unitLevel >= 2 ? row.Tech1UniqueValues : null;
        Fix64[] lv3 = unitLevel >= 3 ? row.Tech2UniqueValues : null;

        switch (unitType)
        {
            case UnitType.Unit_Intern:
                modifiers.AttackSpeedPercent += TechValue(lv2, 0, row.Tech1ID);
                modifiers.LifetimeSecondsDelta -= TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_CanMaker:
                modifiers.AttackSpeedPercent += TechValue(lv2, 0, row.Tech1ID);
                modifiers.AttackSpeedPercent += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_Brat:
                modifiers.AttackSpeedPercent += TechValue(lv2, 0, row.Tech1ID);
                modifiers.AttackSpeedPercent += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_LateRider:
                modifiers.LateRiderMaxDistanceDelta += TechValue(lv2, 0, row.Tech1ID);
                modifiers.LateRiderMaxDistanceDelta += TechValue(lv3, 0, row.Tech2ID);
                modifiers.LateRiderMoveSpeedDelta += TechValue(lv3, 1, row.Tech2ID);
                modifiers.LateRiderAttackDelta += TechValue(lv3, 2, row.Tech2ID);
                break;

            case UnitType.Unit_BoneButcher:
                modifiers.OnKillHealPercentDelta += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_ColdCarrier:
                modifiers.ExcessDamageThresholdDelta -= TechValue(lv2, 0, row.Tech1ID);
                modifiers.ExcessDamageReductionPercentDelta += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_HydroGunner:
                modifiers.AttackSpeedPercent += TechValue(lv2, 0, row.Tech1ID);
                modifiers.KnockbackLevel += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_RiotGuard:
                modifiers.TauntLevelDelta += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_LongbowHunter:
                modifiers.AttackSpeedPercent -= TechValue(lv2, 0, row.Tech1ID);
                modifiers.AttackSpeedPercent -= TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_Poacher:
                modifiers.AttackSpeedPercent -= TechValue(lv2, 0, row.Tech1ID);
                modifiers.CriticalDamageBonusPercent += TechValue(lv3, 0, row.Tech2ID);
                modifiers.AttackSpeedPercent -= TechValue(lv3, 1, row.Tech2ID);
                break;

            case UnitType.Unit_Gardener:
                modifiers.GardenerThresholdPercentDelta -= TechValue(lv2, 0, row.Tech1ID);
                modifiers.CriticalDamageBonusPercent += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_Harvester:
                modifiers.HealOnHit += TechValue(lv3, 0, row.Tech2ID);
                break;

            case UnitType.Unit_Surgeon:
                modifiers.LifetimeSecondsDelta += TechValue(lv2, 0, row.Tech1ID);
                modifiers.AttackSpeedPercent += TechValue(lv3, 0, row.Tech2ID);
                modifiers.LifetimeSecondsDelta += TechValue(lv3, 1, row.Tech2ID);
                break;

            case UnitType.Unit_Sprinter:
                modifiers.SprinterAttackPercentDelta += TechValue(lv2, 0, row.Tech1ID);
                modifiers.SprinterAttackSpeedPercentDelta += TechValue(lv2, 1, row.Tech1ID);
                modifiers.SprinterMoveSpeedDelta += TechValue(lv2, 2, row.Tech1ID);
                modifiers.SprinterDamageReductionPercentDelta += TechValue(lv3, 0, row.Tech2ID);
                modifiers.SprinterDurationDelta += TechValue(lv3, 1, row.Tech2ID);
                break;

            case UnitType.Unit_JavelinThrower:
                modifiers.AttackSpeedPercent += TechValue(lv3, 0, row.Tech2ID);
                modifiers.CriticalDamageBonusPercent += TechValue(lv3, 1, row.Tech2ID);
                modifiers.HealthDrainPerSecondDelta -= TechValue(lv3, 2, row.Tech2ID);
                break;
        }

        return modifiers;
    }

    private static BuildingTable FindArmyBuildingRow(UnitType unitType)
    {
        var table = GF.DataTable.GetDataTable<BuildingTable>();
        if (table == null)
            throw new InvalidOperationException("SoldierFactory.ResolveArmyLevelTechModifiers failed: BuildingTable data table is null.");

        string unitId = unitType.ToString();
        return table.GetDataRow(r => r.Type == BuilType.Army && string.Equals(r.UnitID, unitId, StringComparison.Ordinal));
    }

    private static Fix64 TechValue(Fix64[] values, int index, string techId)
    {
        if (values == null || values.Length == 0)
            return Fix64.Zero;

        if (index < 0 || index >= values.Length)
            throw new InvalidOperationException($"SoldierFactory.ResolveArmyLevelTechModifiers failed: Tech value index out of range. TechId={techId}, Index={index}, Count={values.Length}.");

        return values[index];
    }

    private static int NormalizeUnitLevel(int unitLevel)
    {
        if (unitLevel < 1)
            return 1;
        if (unitLevel > 3)
            return 3;
        return unitLevel;
    }

    private struct ArmyLevelTechModifiers
    {
        public Fix64 AttackSpeedPercent;
        public Fix64 CriticalDamageBonusPercent;
        public Fix64 LifetimeSecondsDelta;
        public Fix64 OnKillHealPercentDelta;
        public Fix64 ExcessDamageThresholdDelta;
        public Fix64 ExcessDamageReductionPercentDelta;
        public Fix64 TauntLevelDelta;
        public Fix64 GardenerThresholdPercentDelta;
        public Fix64 LateRiderMaxDistanceDelta;
        public Fix64 LateRiderMoveSpeedDelta;
        public Fix64 LateRiderAttackDelta;
        public Fix64 SprinterDurationDelta;
        public Fix64 SprinterAttackPercentDelta;
        public Fix64 SprinterAttackSpeedPercentDelta;
        public Fix64 SprinterMoveSpeedDelta;
        public Fix64 SprinterDamageReductionPercentDelta;
        public Fix64 HealthDrainPerSecondDelta;
        public Fix64 HealOnHit;
        public Fix64 KnockbackLevel;
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



