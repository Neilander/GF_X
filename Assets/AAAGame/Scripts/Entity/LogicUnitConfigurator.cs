using System;
using System.Collections.Generic;
using AAAGame.Scripts.Entity;

public static class LogicUnitConfigurator
{
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
        targetingComp.AggroRange = DefaultAggroRange;
        targetingComp.ForgetRange = DefaultForgetRange;
        targetingComp.FollowSearchRange = DefaultFollowRange;
        targetingComp.AlertRadius = DefaultAlertRadius;
        state.SetTargetingComp(targetingComp);

        if (brain is SoldierAIBrain soldierBrain)
        {
            soldierBrain.SetBirthPosition(new UnityEngine.Vector3(
                (float)state.Position.x,
                0f,
                (float)state.Position.y));
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
}

public enum LogicSkillFactoryKind
{
    None = 0,
    Player = 1,
    Character = 2,
}
