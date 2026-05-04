using System.Collections.Generic;
using UnityEngine;

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
        string sourceStrongholdId = null)
    {
        EntityParams entityParams = CreateMAEntityParams(position, characterKey, side, brainType, startBuffs, sourceStrongholdId);
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

    public static int ShowBuilding(BuildingData buildingData, Vector3 position, string buildingInstanceId)
    {
        EntityParams entityParams = EntityParams.Create(position);
        entityParams.Set(BuildingEntity.P_BuildingData, buildingData);
        entityParams.SetString(BuildingEntity.P_BuildingInstanceId, buildingInstanceId);
        return GF.Entity.ShowEntity<BuildingEntity>(buildingData.PrefabPath, Const.EntityGroup.Building, entityParams);
    }
}
