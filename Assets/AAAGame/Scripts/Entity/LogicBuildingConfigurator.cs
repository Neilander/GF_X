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
    public static event Action<IBuildingLogicContext> BuildingConfigured;

    public static void Configure(
        LogicEntityState state,
        BuildingData buildingData,
        string buildingInstanceId,
        string strongholdId,
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

        bool tutorialLevel = TutorialManager.IsCurrentLevelTutorial();
        var properties = new CreaturePropertyManager(property => GetPropertyConfigValue(buildingData, property, tutorialLevel));
        state.Configure(null, properties, MAEntity.UnknownNavAgentTypeId, true, new BuildingAIBrain(), false, false);

        LogicCombatShape combatShape = BuildingCombatShapeCatalog.LoadRequired()
            .ResolveRequired(buildingData.PrefabPath, state.Position, logicQuarterTurns);
        IReadOnlyList<LogicCombatShape> obstacleShapes = BuildingLogicObstacleShapeCatalog.LoadRequired()
            .ResolveRequired(buildingData.PrefabPath, state.Position, logicQuarterTurns);
        bool noAttack = buildingData.Weapon == null || buildingData.Weapon.Atk <= Fix64.Zero;
        LogicInteractionOptionDescriptor[] interactionOptions = LogicInteractionOptionDescriptorFactory.Create(
            state.EntityId,
            buildingInstanceId,
            buildingData);
        int? armySupplyPerUnit = buildingData.Type == BuilType.Army
            ? ResolveRequiredUnitSupply(buildingData)
            : (int?)null;
        state.ConfigureBuilding(
            buildingData,
            buildingInstanceId,
            strongholdId,
            ownerFactionId,
            combatShape,
            obstacleShapes,
            interactionOptions,
            noAttack,
            armySupplyPerUnit);

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
        if (buildingData.Type == BuilType.Prod)
            LogicBuildingProductionService.Configure(state);

        Fix64 aggroRange = Fix64.Max(
            DistanceUnitConverter.ConvertToWorld(weaponData.Range) + (Fix64)1.5f,
            (Fix64)4);
        targetingComp.AggroRangeFixed = aggroRange;
        targetingComp.ForgetRangeFixed = aggroRange + (Fix64)2;
        targetingComp.FollowSearchRangeFixed = Fix64.Zero;
        BuildingConfigured?.Invoke(state);
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

        result.AggroRangeFixed = (Fix64)6;
        result.ForgetRangeFixed = (Fix64)8;
        result.FollowSearchRangeFixed = Fix64.Zero;
        result.AlertRadiusFixed = (Fix64)12;
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

    private static Fix64 GetPropertyConfigValue(
        BuildingData buildingData,
        CreatureMainProperty property,
        bool tutorialLevel)
    {
        switch (property)
        {
            case CreatureMainProperty.Def:
                return buildingData.Def > Fix64.Zero ? buildingData.Def : Fix64.Zero;
            case CreatureMainProperty.Health:
                Fix64 health = buildingData.HP > Fix64.Zero ? buildingData.HP : (Fix64)120;
                return tutorialLevel ? health * (Fix64)0.5f : health;
            case CreatureMainProperty.Sight:
                return buildingData.Weapon != null && buildingData.Weapon.Range > Fix64.Zero
                    ? buildingData.Weapon.Range
                    : Fix64.Zero;
            default:
                return Fix64.Zero;
        }
    }

    private static int ResolveRequiredUnitSupply(BuildingData buildingData)
    {
        if (string.IsNullOrWhiteSpace(buildingData.UnitID))
        {
            if (buildingData.Lv == 0)
                return 0;
            throw new InvalidOperationException($"Army building '{buildingData.Identifier}' has no UnitID.");
        }
        if (GF.DataTable == null)
            throw new InvalidOperationException($"Cannot configure army building '{buildingData.Identifier}': data tables are unavailable.");

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>()
                    ?? throw new InvalidOperationException("CharacterDataDetail table is unavailable.");
        CharacterDataDetail row = table.GetDataRow(candidate => candidate.CharacterKey == buildingData.UnitID)
                                  ?? throw new InvalidOperationException(
                                      $"Army building '{buildingData.Identifier}' references missing unit '{buildingData.UnitID}'.");
        if (row.Supply < 0)
            throw new InvalidOperationException($"Unit '{buildingData.UnitID}' has negative supply {row.Supply}.");
        return row.Supply;
    }
}
