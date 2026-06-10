using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class MAEntityFactory
{
    public static EntityParams CreateMAEntityParams(
        Vector3 position,
        string characterKey,
        SideType side,
        BrainType brainType,
        List<BuffData> startBuffs = null,
        string sourceStrongholdId = null,
        int unitLevel = 1)
    {
        EntityParams entityParams = EntityParams.Create(position: position);
        entityParams.Side = side;
        entityParams.BrainType = brainType;
        entityParams.UnitLevel = Mathf.Clamp(unitLevel, 1, 3);
        entityParams.SetString(EntityParams.P_CharacterKey, characterKey);
        if (!string.IsNullOrEmpty(sourceStrongholdId))
        {
            entityParams.SetString(EntityParams.P_SourceStrongholdId, sourceStrongholdId);
        }
        entityParams.StartBuffs = startBuffs;
        return entityParams;
    }

    public static int ShowSoldier(
        string prefabName,
        string characterKey,
        Vector3 position,
        SideType side,
        BrainType brainType,
        Const.EntityGroup entityGroup,
        List<BuffData> startBuffs = null,
        string sourceStrongholdId = null,
        System.Action<EntityParams> configureParams = null,
        int unitLevel = 1)
    {
        EntityParams entityParams = CreateMAEntityParams(position, characterKey, side, brainType, startBuffs, sourceStrongholdId, unitLevel);
        configureParams?.Invoke(entityParams);
        return GF.Entity.ShowEntity<SoldierEntity>(prefabName, entityGroup, entityParams);
    }

    public static int ShowCharacter(
        string prefabName,
        string characterKey,
        Vector3 position,
        SideType side,
        BrainType brainType,
        Const.EntityGroup entityGroup,
        List<BuffData> startBuffs = null)
    {
        EntityParams entityParams = CreateMAEntityParams(position, characterKey, side, brainType, startBuffs);
        return GF.Entity.ShowEntity<CharacterEntity>(prefabName, entityGroup, entityParams);
    }

    public static int ShowBuilding(BuildingData buildingData, Vector3 position, string buildingInstanceId, bool isGameEndConditionBuilding = false)
    {
        EntityParams entityParams = EntityParams.Create(position);
        entityParams.Set(BuildingEntity.P_BuildingData, buildingData);
        entityParams.SetString(BuildingEntity.P_BuildingInstanceId, buildingInstanceId);
        if (isGameEndConditionBuilding)
        {
            entityParams.Set<VarBoolean>(BuildingEntity.P_IsGameEndConditionBuilding, true);
        }

        return GF.Entity.ShowEntity<BuildingEntity>(buildingData.PrefabPath, Const.EntityGroup.Building, entityParams);
    }
}

public static class BuildingInitialBuffFactory
{
    public static List<BuffData> CreateInitialBuffs(BuildingData buildingData)
    {
        var buffs = new List<BuffData>();
        AddInitialBuffs(buffs, buildingData);
        return buffs.Count > 0 ? buffs : null;
    }

    public static void AddInitialBuffs(List<BuffData> buffList, BuildingData buildingData)
    {
        if (buffList == null || buildingData == null)
            return;

        AddProductionBuff(buffList, buildingData);
        AddDefenseUtilityBuff(buffList, buildingData);
    }

    private static void AddProductionBuff(List<BuffData> buffList, BuildingData buildingData)
    {
        if (buildingData.Type != BuilType.Prod)
            return;

        BuffCallback module = null;
        if (IsBuilding(buildingData, "Buil_ParcelLocker"))
            module = new ParcelLockerProductionBuff();
        else if (IsBuilding(buildingData, "Buil_SouvenirStand"))
            module = new SouvenirStandProductionBuff();
        else if (IsBuilding(buildingData, "Buil_MeatStall"))
            module = new MeatStallProductionBuff();
        else if (IsBuilding(buildingData, "Buil_MiningRig"))
            module = new MiningRigProductionBuff();
        else if (IsBuilding(buildingData, "Buil_ServiceDesk"))
            module = new ServiceDeskProductionBuff();
        else if (IsBuilding(buildingData, "Buil_TrophyRack"))
            module = new TrophyRackProductionBuff();
        else if (IsBuilding(buildingData, "Buil_Nursery"))
            module = new NurseryProductionBuff();
        else if (IsBuilding(buildingData, "Buil_ReceptionDesk"))
            module = new ReceptionDeskProductionBuff();
        else if (IsBuilding(buildingData, "Buil_InsuranceOffice"))
            module = new InsuranceOfficeProductionBuff();
        else if (IsBuilding(buildingData, "Buil_TicketBooth"))
            module = new TicketBoothProductionBuff();

        if (module == null)
            return;

        buffList.Add(BuffData.Create(
            id: $"building_initial_production_{buildingData.Identifier}",
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { module }));
    }

    private static void AddDefenseUtilityBuff(List<BuffData> buffList, BuildingData buildingData)
    {
        if (buildingData.Type != BuilType.Def)
            return;

        if (IsBuilding(buildingData, "Buil_Bollard"))
        {
            int tauntValue = GetUniqueInt(buildingData, 0, 1);
            buffList.Add(TauntBuffCallback.CreateTaunt(tauntValue));
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.SortingTable))
        {
            Fix64 pushLevel = GetUniqueValue(buildingData, 0, Fix64.Zero);
            if (pushLevel > Fix64.Zero)
            {
                buffList.Add(CreateInitialBuff(
                    "building_sorting_table_knockback",
                    new KnockbackOnOutgoingDamageBuff(pushLevel)));
            }
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.MeatRack))
        {
            Fix64 pullLevel = GetUniqueValue(buildingData, 0, Fix64.Zero);
            if (pullLevel > Fix64.Zero)
            {
                buffList.Add(CreateInitialBuff(
                    "building_meat_rack_pull",
                    new PullOnOutgoingDamageBuff(pullLevel)));
            }
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.Trap))
        {
            buffList.Add(CreateInitialBuff(
                "building_trap_invincible",
                new BuildingInvincibleSourceBuff("building_trap_invincible_source")));
            buffList.Add(CreateInitialBuff(
                "building_trap_no_collision",
                new BuildingCollisionBlockingBuff()));
            buffList.Add(CreateInitialBuff(
                "building_trap_permanent_stealth",
                new BuildingPermanentStealthBuff()));
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.RoseBush))
        {
            buffList.Add(CreateInitialBuff(
                "building_rose_bush_invincible",
                new BuildingInvincibleSourceBuff("building_rose_bush_invincible_source")));
            buffList.Add(CreateInitialBuff(
                "building_rose_bush_no_collision",
                new BuildingCollisionBlockingBuff()));
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.BallLauncher))
        {
            float reloadDelay = (float)GetUniqueValue(buildingData, 0, (Fix64)16);
            buffList.Add(CreateInitialBuff(
                "building_ball_launcher_ammo_reload",
                new AmmoReloadBuff(reloadDelay)));
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.Pharmacy))
        {
            buffList.Add(CreateInitialBuff(
                "building_pharmacy_phase_ammo_reset",
                new PhaseAmmoResetBuff()));
        }
        else if (IsBuilding(buildingData, BuildingAbilityIds.Restroom))
        {
            Fix64 queueLimit = GetUniqueValue(buildingData, 0, (Fix64)4);
            Fix64 releaseInterval = GetUniqueValue(buildingData, 1, (Fix64)4);
            buffList.Add(CreateInitialBuff(
                "building_restroom_queue",
                new RestroomQueueBuff((int)queueLimit, (float)releaseInterval)));
        }
    }

    private static BuffData CreateInitialBuff(string id, params BuffCallback[] modules)
    {
        if (modules == null || modules.Length == 0)
            throw new System.InvalidOperationException($"BuildingInitialBuffFactory.CreateInitialBuff failed: modules is empty. BuffId={id}.");

        return BuffData.Create(
            id: id,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback>(modules));
    }

    private static bool IsBuilding(BuildingData buildingData, string baseIdentifier)
    {
        return buildingData?.Identifier != null && buildingData.Identifier.StartsWith(baseIdentifier);
    }

    private static int GetUniqueInt(BuildingData buildingData, int index, int fallback)
    {
        int value = (int)GetUniqueValue(buildingData, index, (Fix64)fallback);
        return value > 0 ? value : fallback;
    }

    private static Fix64 GetUniqueValue(BuildingData buildingData, int index, Fix64 fallback)
    {
        if (buildingData?.UniqueValues == null || index < 0 || index >= buildingData.UniqueValues.Length)
            return fallback;

        Fix64 value = buildingData.UniqueValues[index];
        return value > Fix64.Zero ? value : fallback;
    }
}
