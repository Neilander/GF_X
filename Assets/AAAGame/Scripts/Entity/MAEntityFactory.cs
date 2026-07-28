using System.Collections.Generic;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class MAEntityFactory
{
    public static EntityParams CreateMAEntityParamsFixed(
        FixVector2 position,
        float viewY,
        string characterKey,
        SideType side,
        BrainType brainType,
        List<BuffData> startBuffs = null,
        string sourceStrongholdId = null,
        int unitLevel = 1,
        LogicSkillFactoryKind skillFactoryKind = LogicSkillFactoryKind.None,
        System.Action<EntityParams> configureParams = null)
    {
        var viewPosition = new Vector3((float)position.x, viewY, (float)position.y);
        return CreateMAEntityParamsCore(
            viewPosition,
            position,
            characterKey,
            side,
            brainType,
            startBuffs,
            sourceStrongholdId,
            unitLevel,
            skillFactoryKind,
            configureParams);
    }

    private static EntityParams CreateMAEntityParamsCore(
        Vector3 viewPosition,
        FixVector2 logicPosition,
        string characterKey,
        SideType side,
        BrainType brainType,
        List<BuffData> startBuffs,
        string sourceStrongholdId,
        int unitLevel,
        LogicSkillFactoryKind skillFactoryKind,
        System.Action<EntityParams> configureParams)
    {
        EntityParams entityParams = CreateLogicEntityParams(viewPosition);
        entityParams.Side = side;
        entityParams.BrainType = brainType;
        entityParams.UnitLevel = Mathf.Clamp(unitLevel, 1, 3);
        entityParams.LogicSkillFactoryKind = skillFactoryKind;
        entityParams.SetString(EntityParams.P_CharacterKey, characterKey);
        if (!string.IsNullOrEmpty(sourceStrongholdId))
        {
            entityParams.SetString(EntityParams.P_SourceStrongholdId, sourceStrongholdId);
        }
        entityParams.StartBuffs = startBuffs;
        configureParams?.Invoke(entityParams);
        AssignConfiguredLogicState(
            entityParams,
            logicPosition,
            new FixVector2(Fix64.Zero, Fix64.One),
            side,
            characterKey,
            state => LogicUnitConfigurator.Configure(state, entityParams));
        return entityParams;
    }

    public static LogicEntityId ShowSoldierFixed(
        string prefabName,
        string characterKey,
        FixVector2 position,
        float viewY,
        SideType side,
        BrainType brainType,
        Const.EntityGroup entityGroup,
        List<BuffData> startBuffs = null,
        string sourceStrongholdId = null,
        System.Action<EntityParams> configureParams = null,
        int unitLevel = 1)
    {
        EntityParams entityParams = CreateMAEntityParamsFixed(
            position,
            viewY,
            characterKey,
            side,
            brainType,
            startBuffs,
            sourceStrongholdId,
            unitLevel,
            LogicSkillFactoryKind.None,
            configureParams);
        int viewRequestId = GF.Entity.ShowEntity<SoldierEntity>(prefabName, entityGroup, entityParams);
        if (viewRequestId <= 0)
            throw new System.InvalidOperationException($"MAEntityFactory.ShowSoldierFixed failed to request view. logicEntity={entityParams.LogicEntityId.Value}, prefab={prefabName}.");
        return entityParams.LogicEntityId;
    }

    public static LogicEntityId ShowHeroFixed(
        string prefabName,
        string characterKey,
        FixVector2 position,
        float viewY,
        SideType side,
        BrainType brainType,
        Const.EntityGroup entityGroup,
        List<BuffData> startBuffs = null,
        string sourceStrongholdId = null,
        System.Action<EntityParams> configureParams = null,
        int unitLevel = 1)
    {
        EntityParams entityParams = CreateMAEntityParamsFixed(
            position,
            viewY,
            characterKey,
            side,
            brainType,
            startBuffs,
            sourceStrongholdId,
            unitLevel,
            LogicSkillFactoryKind.Player,
            configureParams);
        int viewRequestId = GF.Entity.ShowEntity<HeroEntity>(prefabName, entityGroup, entityParams);
        if (viewRequestId <= 0)
            throw new System.InvalidOperationException($"MAEntityFactory.ShowHeroFixed failed to request view. logicEntity={entityParams.LogicEntityId.Value}, prefab={prefabName}.");
        return entityParams.LogicEntityId;
    }

    public static LogicEntityId ShowBuildingFixed(
        BuildingData buildingData,
        FixVector2 position,
        float viewY,
        string buildingInstanceId,
        string strongholdId,
        int ownerFactionId,
        int logicQuarterTurns = 0,
        bool isGameEndConditionBuilding = false,
        bool isNavigationStaticBaked = false,
        bool currentInteractionFrameLifecycle = false)
    {
        var viewPosition = new Vector3((float)position.x, viewY, (float)position.y);
        EntityParams entityParams = CreateBuildingEntityParams(
            buildingData,
            viewPosition,
            position,
            buildingInstanceId,
            strongholdId,
            ownerFactionId,
            logicQuarterTurns,
            isGameEndConditionBuilding,
            isNavigationStaticBaked,
            currentInteractionFrameLifecycle);
        int viewRequestId = GF.Entity.ShowEntity<BuildingEntity>(
            buildingData.PrefabPath,
            Const.EntityGroup.Building,
            entityParams);
        if (viewRequestId <= 0)
        {
            throw new System.InvalidOperationException(
                $"MAEntityFactory.ShowBuildingFixed failed to request view. logicEntity={entityParams.LogicEntityId.Value}, building={buildingData.Identifier}.");
        }
        return entityParams.LogicEntityId;
    }

    private static EntityParams CreateBuildingEntityParams(
        BuildingData buildingData,
        Vector3 viewPosition,
        FixVector2 logicPosition,
        string buildingInstanceId,
        string strongholdId,
        int ownerFactionId,
        int logicQuarterTurns,
        bool isGameEndConditionBuilding,
        bool isNavigationStaticBaked,
        bool currentInteractionFrameLifecycle)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new System.ArgumentException("MAEntityFactory.ShowBuilding failed: buildingInstanceId is empty.", nameof(buildingInstanceId));
        if (buildingData == null)
            throw new System.ArgumentNullException(nameof(buildingData));
        if (ownerFactionId < 0)
            throw new System.ArgumentOutOfRangeException(nameof(ownerFactionId));
        if (logicQuarterTurns < 0 || logicQuarterTurns > 3)
            throw new System.ArgumentOutOfRangeException(nameof(logicQuarterTurns));

        EntityParams entityParams = CreateLogicEntityParams(viewPosition);
        entityParams.FactionId = ownerFactionId;
        entityParams.eulerAngles = new Vector3(0f, logicQuarterTurns * 90f, 0f);
        entityParams.Set(BuildingEntity.P_BuildingData, buildingData);
        entityParams.SetString(BuildingEntity.P_BuildingInstanceId, buildingInstanceId);
        if (isGameEndConditionBuilding)
        {
            entityParams.Set<VarBoolean>(BuildingEntity.P_IsGameEndConditionBuilding, true);
        }
        if (isNavigationStaticBaked)
        {
            entityParams.Set<VarBoolean>(BuildingEntity.P_IsNavigationStaticBaked, true);
        }
        SideType side = EntitySideHelper.ToSide(EntityCombatTeamHelper.ResolveTeamIdByFaction(ownerFactionId));
        AssignConfiguredLogicState(
            entityParams,
            logicPosition,
            ResolveBuildingForwardFixed(logicQuarterTurns),
            side,
            buildingData.Identifier,
            state => LogicBuildingConfigurator.Configure(
                state,
                buildingData,
                buildingInstanceId,
                strongholdId,
                ownerFactionId,
                logicQuarterTurns,
                isGameEndConditionBuilding,
                isNavigationStaticBaked),
            currentInteractionFrameLifecycle);

        return entityParams;
    }

    private static EntityParams CreateLogicEntityParams(Vector3 position)
    {
        return EntityParams.Create(position: position);
    }

    public static FixVector2 ResolveBuildingForwardFixed(int logicQuarterTurns)
    {
        switch (logicQuarterTurns)
        {
            case 0:
                return new FixVector2(Fix64.Zero, Fix64.One);
            case 1:
                return new FixVector2(Fix64.One, Fix64.Zero);
            case 2:
                return new FixVector2(Fix64.Zero, -Fix64.One);
            case 3:
                return new FixVector2(-Fix64.One, Fix64.Zero);
            default:
                throw new System.ArgumentOutOfRangeException(nameof(logicQuarterTurns));
        }
    }

    private static void AssignConfiguredLogicState(
        EntityParams entityParams,
        FixVector2 position,
        FixVector2 forward,
        SideType side,
        string characterKey,
        System.Action<LogicEntityState> configure,
        bool currentInteractionFrameLifecycle = false)
    {
        if (entityParams == null)
            throw new System.ArgumentNullException(nameof(entityParams));
        if (entityParams.LogicEntityId.IsValid || entityParams.LogicEntityState != null)
            throw new System.InvalidOperationException("MAEntityFactory.AssignConfiguredLogicState failed: params already have logic identity.");
        if (configure == null)
            throw new System.ArgumentNullException(nameof(configure));

        var descriptor = new LogicEntitySpawnDescriptor(
            position,
            forward,
            side,
            characterKey,
            entityParams.GetString(EntityParams.P_SourceStrongholdId));
        LogicEntityId entityId = currentInteractionFrameLifecycle
            ? LogicEntityLifecycleService.RequestConfiguredSpawnForCurrentInteractionFrame(descriptor, configure)
            : LogicEntityLifecycleService.RequestConfiguredSpawn(descriptor, configure);
        entityParams.LogicEntityId = entityId;
        entityParams.LogicEntityState = LogicEntityStateStore.GetRequired(entityId);
    }
}

public static class BuildingInitialBuffFactory
{
    public static List<BuffData> CreateCombatInitialBuffs(BuildingData buildingData)
    {
        var buffs = new List<BuffData>();
        AddDefenseUtilityBuff(buffs, buildingData);
        return buffs.Count > 0 ? buffs : null;
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
            Fix64 reloadDelay = GetUniqueValue(buildingData, 0, (Fix64)16);
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
                new RestroomQueueBuff((int)queueLimit, releaseInterval)));
        }
    }

    private static BuffData CreateInitialBuff(string id, params BuffCallback[] modules)
    {
        if (modules == null || modules.Length == 0)
            throw new System.InvalidOperationException($"BuildingInitialBuffFactory.CreateInitialBuff failed: modules is empty. BuffId={id}.");

        return BuffData.Create(
            id: id,
            duration: Fix64.Zero,
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
