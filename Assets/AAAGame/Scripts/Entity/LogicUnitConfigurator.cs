using System;
using System.Collections.Generic;
using AAAGame.Scripts.Entity;
using UnityGameFramework.Runtime;

public static class LogicUnitConfigurator
{
    public const string DefendSpeedBuffId = "defend_phase_speed_override";
    public const float DefaultAggroRange = 10f;
    public const float DefaultForgetRange = 20f;
    public const float DefaultFollowRange = 30f;
    public const float DefaultAlertRadius = 5f;

    public static void Configure(LogicEntityState state, EntityParams entityParams)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (entityParams == null)
            throw new ArgumentNullException(nameof(entityParams));
        if (state.IsConfigured)
            throw new InvalidOperationException($"LogicUnitConfigurator.Configure failed: entity {state.EntityId.Value} is already configured.");

        string characterKey = entityParams.GetString(EntityParams.P_CharacterKey);
        if (!string.Equals(characterKey, state.CharacterKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.Configure failed: character key mismatch. entity={state.EntityId.Value}, state={state.CharacterKey}, params={characterKey}.");
        }
        if (GF.DataTable == null)
            throw new InvalidOperationException("LogicUnitConfigurator.Configure failed: GF.DataTable is null.");

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            throw new InvalidOperationException("LogicUnitConfigurator.Configure failed: CharacterDataDetail table is null.");
        CharacterDataDetail characterData = table.GetDataRow(row => row.CharacterKey == characterKey);
        if (characterData == null)
            throw new InvalidOperationException($"LogicUnitConfigurator.Configure failed: CharacterDataDetail row is missing. character={characterKey}.");

        int unitLevel = Math.Max(1, Math.Min(3, entityParams.UnitLevel));
        var properties = new CreaturePropertyManager(characterKey, unitLevel);
        int navigationAgentTypeId = AgentTypeHelper.ResolveNavAgentTypeId(characterData.Size);
        IControlBrain brain = BrainFactory.Create(entityParams.BrainType, state, entityParams);
        state.Configure(
            characterData,
            properties,
            navigationAgentTypeId,
            false,
            brain,
            true,
            entityParams.BrainType == BrainType.Player,
            entityParams.LogicSkillFactoryKind == LogicSkillFactoryKind.Player);

        ConfigureDefendEnemySpawnSpeed(state, entityParams);

        var moveComp = new CharacterMoveComp();
        state.SetMoveComp(moveComp);
        moveComp.Init(state, navigationAgentTypeId);

        IAtkComp attackComp = entityParams.BrainType == BrainType.Player
            ? new MoveAtkComp()
            : new DirectAtkComp();
        state.SetAtkComp(attackComp);
        attackComp.Init(state);

        ITargetingComp targetingComp = WeaponTargetRules.IsHealingWeapon(state.WeaponComp.Data.Type)
            ? new HealTargetingComp()
            : new CharacterTargetingComp();
        targetingComp.Init(state);
        targetingComp.AggroRangeFixed = (Fix64)10;
        targetingComp.ForgetRangeFixed = (Fix64)20;
        targetingComp.FollowSearchRangeFixed = (Fix64)30;
        targetingComp.AlertRadiusFixed = (Fix64)5;
        state.SetTargetingComp(targetingComp);
        ConfigureTargetingModeForSpawn(
            state,
            entityParams,
            targetingComp,
            ResolveDefendFallbackTarget(state, entityParams));

        if (brain is SoldierAIBrain soldierBrain)
        {
            soldierBrain.SetBirthPositionFixed(state.Position);
            soldierBrain.SetReturnToBirthEnabled(entityParams.BrainType != BrainType.DefendEnemyAI);
        }

        if (entityParams.LogicSkillFactoryKind != LogicSkillFactoryKind.None)
        {
            string factoryName = entityParams.LogicSkillFactoryKind == LogicSkillFactoryKind.Player
                ? "PlayerSkillFactory"
                : "CharacterSkillFactory";
            FactoryHelper.CreatePreloadedSkillComp(
                UtilityBuiltin.AssetsPath.GetSkillFactoryPath(factoryName),
                state);
        }

        if (state.IsHeroEntity)
        {
            state.BuffComp.AddBuff(
                BuffData.Create(
                    "hero_out_of_combat_speed_x2",
                    float.MaxValue,
                    true,
                    1,
                    new List<BuffCallback> { new HeroOutOfCombatMoveSpeedBuff() }),
                state);
        }

        if (entityParams.StartBuffs != null)
        {
            for (int i = 0; i < entityParams.StartBuffs.Count; i++)
            {
                BuffData buff = entityParams.StartBuffs[i]
                    ?? throw new InvalidOperationException($"LogicUnitConfigurator.Configure failed: start buff {i} is null. entity={state.EntityId.Value}.");
                state.BuffComp.AddBuff(buff, state);
            }
        }
    }

    public static void ConfigureDefendEnemySpawnSpeed(LogicEntityState state, EntityParams entityParams)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (entityParams == null)
            throw new ArgumentNullException(nameof(entityParams));
        if (entityParams.BrainType != BrainType.DefendEnemyAI)
            return;
        if (entityParams.Side != SideType.EnemySide || state.Side != SideType.EnemySide)
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: DefendEnemyAI must use EnemySide. entity={state.EntityId.Value}, paramsSide={entityParams.Side}, stateSide={state.Side}.");
        }
        if (!entityParams.DefendAssignedSpeed.HasValue)
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: assigned speed is missing. entity={state.EntityId.Value}.");
        }

        Fix64 assignedSpeed = entityParams.DefendAssignedSpeed.Value;
        if (assignedSpeed <= Fix64.Zero)
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: assigned speed must be positive. entity={state.EntityId.Value}, raw={assignedSpeed.RawValue}.");
        }
        if (state.BuffComp == null)
            throw new InvalidOperationException($"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: entity {state.EntityId.Value} has no BuffComp.");
        if (state.BuffComp.HasBuff(DefendSpeedBuffId))
            throw new InvalidOperationException($"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: duplicate speed buff. entity={state.EntityId.Value}.");

        BuffData buffData = BuffData.Create(
            DefendSpeedBuffId,
            Fix64.Zero,
            true,
            1,
            new List<BuffCallback> { new FixedMoveSpeedOverrideBuff(assignedSpeed) });
        if (!state.BuffComp.AddBuff(buffData, state))
            throw new InvalidOperationException($"LogicUnitConfigurator.ConfigureDefendEnemySpawnSpeed failed: speed buff was rejected. entity={state.EntityId.Value}.");
    }

    public static void ConfigureTargetingModeForSpawn(
        LogicEntityState state,
        EntityParams entityParams,
        ITargetingComp targetingComp,
        IEntityContext defendFallbackTarget)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (entityParams == null)
            throw new ArgumentNullException(nameof(entityParams));
        if (targetingComp == null)
            throw new ArgumentNullException(nameof(targetingComp));

        if (entityParams.BrainType != BrainType.DefendEnemyAI)
        {
            if (defendFallbackTarget != null)
                throw new InvalidOperationException("LogicUnitConfigurator.ConfigureTargetingModeForSpawn failed: non-defend unit received a defend fallback target.");
            if (targetingComp is CharacterTargetingComp defaultTargeting)
                defaultTargeting.UseDefaultMode();
            return;
        }

        if (entityParams.Side != SideType.EnemySide || state.Side != SideType.EnemySide)
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.ConfigureTargetingModeForSpawn failed: DefendEnemyAI must use EnemySide. entity={state.EntityId.Value}, paramsSide={entityParams.Side}, stateSide={state.Side}.");
        }
        if (targetingComp is not CharacterTargetingComp defendTargeting)
        {
            throw new InvalidOperationException(
                $"LogicUnitConfigurator.ConfigureTargetingModeForSpawn failed: DefendEnemyAI requires CharacterTargetingComp. entity={state.EntityId.Value}, actual={targetingComp.GetType().FullName}.");
        }
        if (defendFallbackTarget != null)
        {
            bool hasValidId = defendFallbackTarget.LogicEntityId.IsValid;
            bool isBuilding = defendFallbackTarget.TryGetLogicBuilding(out _);
            bool isAlive = defendFallbackTarget.Alive;
            bool isEnemy = EntityCombatTeamHelper.IsEnemy(state, defendFallbackTarget);
            if (!hasValidId || !isBuilding || !isAlive || !isEnemy)
            {
                throw new InvalidOperationException(
                    $"LogicUnitConfigurator.ConfigureTargetingModeForSpawn failed: invalid defend fallback target. entity={state.EntityId.Value}, fallback={defendFallbackTarget.LogicEntityId.Value}, validId={hasValidId}, building={isBuilding}, alive={isAlive}, enemy={isEnemy}.");
            }
        }

        defendTargeting.UseDefendEnemyMode(defendFallbackTarget);
    }

    private static IEntityContext ResolveDefendFallbackTarget(LogicEntityState state, EntityParams entityParams)
    {
        if (entityParams.BrainType != BrainType.DefendEnemyAI)
            return null;

        GameEndManager gameEndManager = GameEntry.GetComponent<GameEndManager>();
        if (gameEndManager == null)
            throw new InvalidOperationException("LogicUnitConfigurator.ResolveDefendFallbackTarget failed: GameEndManager is missing.");

        gameEndManager.TryGetNearestPlayerInitialConditionBuilding(
            state.Position,
            out IBuildingLogicContext fallbackTarget);
        return fallbackTarget;
    }
}

public enum LogicSkillFactoryKind
{
    None = 0,
    Player = 1,
    Character = 2,
}
