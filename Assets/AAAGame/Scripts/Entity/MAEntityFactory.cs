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
        string sourceStrongholdId = null)
    {
        EntityParams entityParams = EntityParams.Create(position: position);
        entityParams.Side = side;
        entityParams.BrainType = brainType;
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
        System.Action<EntityParams> configureParams = null)
    {
        EntityParams entityParams = CreateMAEntityParams(position, characterKey, side, brainType, startBuffs, sourceStrongholdId);
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
        entityParams.StartBuffs = BuildingInitialBuffFactory.CreateInitialBuffs(buildingData);
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
    }

    private static bool IsBuilding(BuildingData buildingData, string baseIdentifier)
    {
        return buildingData?.Identifier != null && buildingData.Identifier.StartsWith(baseIdentifier);
    }

    private static int GetUniqueInt(BuildingData buildingData, int index, int fallback)
    {
        if (buildingData?.UniqueValues == null || index < 0 || index >= buildingData.UniqueValues.Length)
            return fallback;

        int value = (int)buildingData.UniqueValues[index];
        return value > 0 ? value : fallback;
    }
}
