using System;
using System.Collections.Generic;
using UnityEngine;

public static class LogicBuildingConfigurator
{
    private const string Lv0InvincibleBuffId = "building_lv0_invincible";
    private const string PhaseGuardBuffId = "building_phase_guard";
    private static readonly Fix64 PlaceholderAttackInterval = (Fix64)1.6f;
    private static readonly Fix64 PlaceholderAttackRange = (Fix64)650;
    private static readonly Fix64 PlaceholderWindUp = (Fix64)0.35f;
    private static readonly Fix64 PlaceholderWindDown = (Fix64)0.35f;

    public static void Configure(
        LogicEntityState state,
        BuildingData buildingData,
        string buildingInstanceId,
        int ownerFactionId,
        int logicQuarterTurns)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (buildingData == null)
            throw new ArgumentNullException(nameof(buildingData));
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is empty.", nameof(buildingInstanceId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        if (logicQuarterTurns < 0 || logicQuarterTurns > 3)
            throw new ArgumentOutOfRangeException(nameof(logicQuarterTurns));
        if (state.IsConfigured)
            throw new InvalidOperationException($"LogicBuildingConfigurator.Configure failed: entity {state.EntityId.Value} is already configured.");
        if (!string.Equals(state.CharacterKey, buildingData.Identifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"LogicBuildingConfigurator.Configure failed: character key mismatch. entity={state.EntityId.Value}, state={state.CharacterKey}, building={buildingData.Identifier}.");
        }

        var properties = new CreaturePropertyManager(property => GetPropertyConfigValue(buildingData, property));
        state.Configure(null, properties, MAEntity.UnknownNavAgentTypeId, true, new BuildingAIBrain(), false, false);

        float yawDegrees = logicQuarterTurns * 90f;
        Vector3 worldPosition = new Vector3((float)state.Position.x, 0f, (float)state.Position.y);
        LogicCombatShape combatShape = BuildingCombatShapeCatalog.LoadRequired()
            .ResolveRequired(buildingData.PrefabPath, worldPosition, yawDegrees);
        IReadOnlyList<LogicCombatShape> obstacleShapes = BuildingLogicObstacleShapeCatalog.LoadRequired()
            .ResolveRequired(buildingData.PrefabPath, worldPosition, yawDegrees);
        bool noAttack = buildingData.Weapon == null || buildingData.Weapon.Atk <= Fix64.Zero;
        state.ConfigureBuilding(
            buildingData,
            buildingInstanceId,
            ownerFactionId,
            combatShape,
            obstacleShapes,
            noAttack);

        var moveComp = new NoMoveComp();
        state.SetMoveComp(moveComp);
        moveComp.Init(state);

        WeaponData weaponData = CreateBuildingWeaponData(buildingData);
        state.SetWeaponComp(new WeaponComp(weaponData.ToWeapon($"{state.CharacterKey}_Weapon1", properties.propertyManager)));

        ITargetingComp targetingComp = CreateTargetingComp(state, buildingData);
        state.SetTargetingComp(targetingComp);
        targetingComp.Init(state);

        var attackComp = new DirectAtkComp();
        state.SetAtkComp(attackComp);
        attackComp.Init(state);

        AddLogicInitialBuffs(state, buildingData);

        float aggroRange = Mathf.Max(DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range) + 1.5f, 4f);
        targetingComp.AggroRange = aggroRange;
        targetingComp.ForgetRange = aggroRange + 2f;
        targetingComp.FollowSearchRange = 0f;
    }

    private static void AddLogicInitialBuffs(LogicEntityState state, BuildingData buildingData)
    {
        state.BuffComp.AddBuff(
            BuffData.Create(
                Lv0InvincibleBuffId,
                float.MaxValue,
                true,
                1,
                new List<BuffCallback> { new BuildingLv0InvincibleBuff() }),
            state);
        state.BuffComp.AddBuff(
            BuffData.Create(
                PhaseGuardBuffId,
                float.MaxValue,
                true,
                1,
                new List<BuffCallback> { new BuildingPhaseGuardBuff() }),
            state);

        List<BuffData> combatBuffs = BuildingInitialBuffFactory.CreateCombatInitialBuffs(buildingData);
        if (combatBuffs == null)
            return;
        for (int i = 0; i < combatBuffs.Count; i++)
            state.BuffComp.AddBuff(combatBuffs[i], state);
    }

    private static ITargetingComp CreateTargetingComp(LogicEntityState state, BuildingData buildingData)
    {
        ITargetingComp result;
        if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Pharmacy))
            result = new HealTargetingComp();
        else if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.MeatRack))
            result = new MeatRackTargetingComp();
        else if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Monitor))
            result = new MonitorTargetingComp(MonitorWeaponEffect.ResolveFacingConeAngle(state));
        else if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Restroom))
            return new NoTargetingComp();
        else
            result = new CharacterTargetingComp { EnableAggroFallback = false };

        result.AggroRange = 6f;
        result.ForgetRange = 8f;
        result.FollowSearchRange = 0f;
        result.AlertRadius = 12f;
        return result;
    }

    private static WeaponData CreateBuildingWeaponData(BuildingData buildingData)
    {
        if (buildingData.Weapon != null && buildingData.Weapon.Atk > Fix64.Zero)
            return buildingData.Weapon;

        return new WeaponData(
            WeaponType.Melee,
            Fix64.Zero,
            PlaceholderAttackInterval,
            PlaceholderAttackRange,
            Fix64.Zero,
            PlaceholderWindUp,
            PlaceholderWindDown,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Array.Empty<Fix64>());
    }

    private static Fix64 GetPropertyConfigValue(BuildingData buildingData, CreatureMainProperty property)
    {
        switch (property)
        {
            case CreatureMainProperty.Def:
                return buildingData.Def > Fix64.Zero ? buildingData.Def : Fix64.Zero;
            case CreatureMainProperty.Health:
                return buildingData.HP > Fix64.Zero ? buildingData.HP : (Fix64)120;
            case CreatureMainProperty.Sight:
                return buildingData.Weapon != null && buildingData.Weapon.Range > Fix64.Zero
                    ? buildingData.Weapon.Range
                    : Fix64.Zero;
            default:
                return Fix64.Zero;
        }
    }
}
