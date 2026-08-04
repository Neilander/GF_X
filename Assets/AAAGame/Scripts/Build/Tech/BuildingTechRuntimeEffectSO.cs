using System;
using System.Collections.Generic;
using AAAGame.Card;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class BuildingTechRuntimeEffectSO : TechEffectSO
{
    private const string DiscardFutureBuffPrefix = "base_tech_discard_future_";
    private const string DiscardFieldBuffPrefix = "base_tech_discard_field_";

    private sealed class RuntimeTechRule
    {
        public Action<BuildingTechRuntimeEffectSO, TechEffectContext> Activate;
        public bool RegisterUnitBuffsAfterActivate;
        public Func<TechData, string, List<BuffCallback>> CreateUnitModules;
        public Func<TechData, string, List<BuffCallback>> CreateBuildingModules;
        public string SkipReason;
    }

    private static readonly Dictionary<string, RuntimeTechRule> s_Rules = CreateRules();

    private readonly Dictionary<string, DiscardBuffSpec> m_DiscardBuffs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> m_PendingBuildPhaseCoinsByFaction = new();
    private readonly Dictionary<string, SortingCenterCardForceSpec> m_SortingCenterCardForceSpecs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FireHqDeathSupplySpec> m_FireHqDeathSupplySpecs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> m_BattleCardCountsByFaction = new();
    private readonly Dictionary<int, int> m_DeadSupplyByFaction = new();
    private readonly List<string> m_DeterministicStringKeys = new();
    private readonly List<int> m_DeterministicIntKeys = new();
    private static readonly Comparison<int> s_IntComparison =
        (left, right) => left.CompareTo(right);
    private static readonly Comparison<string> s_StringComparison = string.CompareOrdinal;
    private bool m_EventsSubscribed;
    private int m_DiscardCounter;

    private struct DiscardBuffSpec
    {
        public string TechId;
        public int OwnerFactionId;
        public Fix64 HealthBonus;
        public Fix64 AttackSpeedPercent;
    }

    private struct SortingCenterCardForceSpec
    {
        public string TechId;
        public int OwnerFactionId;
        public int CardLimit;
        public Fix64 BonusForce;
    }

    private struct FireHqDeathSupplySpec
    {
        public string TechId;
        public int OwnerFactionId;
        public int SupplyPerCoin;
    }

    public bool CanHandle(TechData techData)
    {
        return techData != null
               && IsRuntimeDrivenTechId(techData.Identifier);
    }

    public static bool IsRuntimeDrivenTechId(string techId)
    {
        return !string.IsNullOrWhiteSpace(techId) && s_Rules.ContainsKey(GetRootTechId(techId));
    }

    public void ClearRuntimeState()
    {
        UnsubscribeRuntimeEvents();
        m_DiscardBuffs.Clear();
        m_PendingBuildPhaseCoinsByFaction.Clear();
        m_SortingCenterCardForceSpecs.Clear();
        m_FireHqDeathSupplySpecs.Clear();
        m_BattleCardCountsByFaction.Clear();
        m_DeadSupplyByFaction.Clear();
        m_DiscardCounter = 0;
        DiscardRewardModifierService.Clear();
        EnemyArmyForceModifierService.Clear();
        HealingTargetFilterService.Clear();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x425452554E54494DUL);
        hasher.Add(m_EventsSubscribed);
        hasher.Add(m_DiscardCounter);
        AddDiscardBuffs(hasher);
        AddPendingBuildPhaseCoins(hasher);
        AddSortingCenterSpecs(hasher);
        AddFireHqSpecs(hasher);
        AddIntDictionary(hasher, m_BattleCardCountsByFaction);
        AddIntDictionary(hasher, m_DeadSupplyByFaction);
    }

    public static void WriteStaticModifierDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x4254535441544943UL);
        DiscardRewardModifierService.WriteDeterministicState(hasher);
        EnemyArmyForceModifierService.WriteDeterministicState(hasher);
        HealingTargetFilterService.WriteDeterministicState(hasher);
        BuildingCostModifierService.WriteDeterministicState(hasher);
        SettlementOffsetRateService.WriteDeterministicState(hasher);
    }

    private void AddDiscardBuffs(LogicStateHasher hasher)
    {
        FillSortedStringKeys(m_DiscardBuffs);
        hasher.Add(m_DeterministicStringKeys.Count);
        for (int i = 0; i < m_DeterministicStringKeys.Count; i++)
        {
            string key = m_DeterministicStringKeys[i];
            DiscardBuffSpec spec = m_DiscardBuffs[key];
            hasher.Add(key);
            hasher.Add(spec.OwnerFactionId);
            hasher.Add(spec.HealthBonus.RawValue);
            hasher.Add(spec.AttackSpeedPercent.RawValue);
        }
    }

    private void AddPendingBuildPhaseCoins(LogicStateHasher hasher)
    {
        AddIntDictionary(hasher, m_PendingBuildPhaseCoinsByFaction);
    }

    private void AddSortingCenterSpecs(LogicStateHasher hasher)
    {
        FillSortedStringKeys(m_SortingCenterCardForceSpecs);
        hasher.Add(m_DeterministicStringKeys.Count);
        for (int i = 0; i < m_DeterministicStringKeys.Count; i++)
        {
            string key = m_DeterministicStringKeys[i];
            SortingCenterCardForceSpec spec = m_SortingCenterCardForceSpecs[key];
            hasher.Add(key);
            hasher.Add(spec.OwnerFactionId);
            hasher.Add(spec.CardLimit);
            hasher.Add(spec.BonusForce.RawValue);
        }
    }

    private void AddFireHqSpecs(LogicStateHasher hasher)
    {
        FillSortedStringKeys(m_FireHqDeathSupplySpecs);
        hasher.Add(m_DeterministicStringKeys.Count);
        for (int i = 0; i < m_DeterministicStringKeys.Count; i++)
        {
            string key = m_DeterministicStringKeys[i];
            FireHqDeathSupplySpec spec = m_FireHqDeathSupplySpecs[key];
            hasher.Add(key);
            hasher.Add(spec.OwnerFactionId);
            hasher.Add(spec.SupplyPerCoin);
        }
    }

    private void AddIntDictionary(LogicStateHasher hasher, Dictionary<int, int> values)
    {
        m_DeterministicIntKeys.Clear();
        foreach (int key in values.Keys)
            m_DeterministicIntKeys.Add(key);
        m_DeterministicIntKeys.Sort(s_IntComparison);
        hasher.Add(m_DeterministicIntKeys.Count);
        for (int i = 0; i < m_DeterministicIntKeys.Count; i++)
        {
            int key = m_DeterministicIntKeys[i];
            hasher.Add(key);
            hasher.Add(values[key]);
        }
    }

    private void FillSortedStringKeys<T>(Dictionary<string, T> values)
    {
        m_DeterministicStringKeys.Clear();
        foreach (string key in values.Keys)
            m_DeterministicStringKeys.Add(key);
        m_DeterministicStringKeys.Sort(s_StringComparison);
    }

    public override void Activate(TechEffectContext context)
    {
        if (context?.TechData == null || context.GlobalBuffManager == null)
            return;

        TechData techData = context.TechData;
        if (techData.ScopeType == TechScopeType.Skill)
        {
            Debug.Log($"[BuildingTechRuntimeEffect] 技能科技暂不实现，跳过 techId={techData.Identifier}");
            return;
        }

        if (!TryGetRule(techData.Identifier, out RuntimeTechRule rule))
        {
            Debug.LogWarning($"[BuildingTechRuntimeEffect] 缺少建筑科技规则，techId={techData.Identifier}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(rule.SkipReason))
        {
            Debug.LogWarning($"[BuildingTechRuntimeEffect] 科技机制暂未实现，已跳过 techId={techData.Identifier}, reason={rule.SkipReason}");
            return;
        }

        rule.Activate?.Invoke(this, context);

        if (rule.CreateUnitModules != null && (rule.Activate == null || rule.RegisterUnitBuffsAfterActivate))
            RegisterUnitBuffs(context);
    }

    public override BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
    {
        List<BuffCallback> modules = CreateUnitModules(techData, techId);
        if (modules == null || modules.Count == 0)
            return null;

        return BuffData.Create(
            id: $"base_tech_{techId}_{unitType}",
            duration: Fix64.Zero,
            isForever: true,
            maxStack: 1,
            modules: modules);
    }

    public override List<BuffCallback> CreateBuildingScopedModules(TechData techData, string techId)
    {
        if (techData == null || string.IsNullOrWhiteSpace(techId))
            return null;

        string rootTechId = GetRootTechId(techId);
        if (!TryGetRule(rootTechId, out RuntimeTechRule rule) || rule.CreateBuildingModules == null)
            return null;

        return rule.CreateBuildingModules.Invoke(techData, techId);
    }

    private void RegisterUnitBuffs(TechEffectContext context)
    {
        if (context.ResolvedScope == null || context.ResolvedScope.UnitTypes.Count == 0)
        {
            Debug.LogWarning($"[BuildingTechRuntimeEffect] ResolvedScope 无 UnitTypes, techId={context.TechId}");
            return;
        }

        foreach (UnitType unitType in context.ResolvedScope.UnitTypes)
        {
            context.GlobalBuffManager.RegisterUnitBuff(unitType, context.OwnerFactionId, context.TechId, this, context.TechData);
        }
    }

    private static List<BuffCallback> CreateUnitModules(TechData techData, string techId)
    {
        if (techData == null || string.IsNullOrWhiteSpace(techData.Identifier))
            return null;

        if (TryGetRule(techData.Identifier, out RuntimeTechRule rule) && rule.CreateUnitModules != null)
            return rule.CreateUnitModules.Invoke(techData, techId);

        Debug.LogWarning($"[BuildingTechRuntimeEffect] 缺少单位 buff 规则，techId={techData.Identifier}");
        return null;
    }

    private static Dictionary<string, RuntimeTechRule> CreateRules()
    {
        var rules = new Dictionary<string, RuntimeTechRule>(StringComparer.Ordinal);

        AddActivation(rules, "Tech_Buil_ServerRoom_Opt1",
            (self, context) => self.ApplyArmyForceIfAtLeast(context, minForce: (int)GetValue(context.TechData, 0), bonusForce: GetValue(context.TechData, 1)));
        AddUnit(rules, "Tech_Buil_ServerRoom_Opt2",
            (techData, _) => Modules(new LifetimePercentBuff(GetValue(techData, 0) / (Fix64)100)));
        AddActivation(rules, "Tech_Buil_ServerRoom_Opt3",
            (self, context) => self.AddMaxSupply(context, GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_ServerRoom_Opt4",
            (self, context) => self.RegisterDiscardBuff(context, healthBonus: GetValue(context.TechData, 0), attackSpeedPercent: Fix64.Zero));
        AddUnit(rules, "Tech_Buil_ServerRoom_Opt4",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Health, GetValue(techData, 0))));

        AddActivation(rules, "Tech_Buil_ResearchCenter_Lv2_Opt2",
            (self, context) => self.RegisterStrongholdCostDiscount(context, (int)GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_ResearchCenter_Lv3_Opt2",
            (self, context) => self.ApplyArmyForceInStrongholdsWithArchetype(context, Archetype.Coding, GetValue(context.TechData, 0)));

        AddUnit(rules, "Tech_Buil_DreamPark_Lv2_Opt2",
            (techData, _) => Modules(new ConditionalCoinAttackSpeedBuff((int)GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_DreamPark_Lv3_Opt2",
            (techData, _) => Modules(new OutgoingAttackDebuffBuff(-GetValue(techData, 1), GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_GiantMascot_Opt1",
            (techData, _) => Modules(
                new AttackSpeedBonusBuff(GetValue(techData, 0)),
                new MainPropertyAdditiveBuff(CreatureMainProperty.Health, -GetValue(techData, 1))));
        AddActivation(rules, "Tech_Buil_GiantMascot_Opt2",
            (self, context) => self.RegisterCurrentDayCreatureArmyForcePercent(context, GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_GiantMascot_Opt3",
            (self, context) => self.RegisterDiscardBuff(context, healthBonus: Fix64.Zero, attackSpeedPercent: GetValue(context.TechData, 0)));
        AddUnit(rules, "Tech_Buil_GiantMascot_Opt3",
            (techData, _) => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddActivation(rules, "Tech_Buil_GiantMascot_Opt4",
            (self, context) => self.RegisterEnemyArmyForceReduction(context, GetValue(context.TechData, 0)));

        AddUnit(rules, "Tech_Buil_SortingCenter_Lv2_Opt2",
            (techData, _) => Modules(new TimedBuffOnSpawnModule(GetValue(techData, 0), () => Modules(
                new MainPropertyPercentBuff(CreatureMainProperty.Speed, GetValue(techData, 1)),
                new MainPropertyAdditiveBuff(CreatureMainProperty.Def, GetValue(techData, 2))))));
        AddActivation(rules, "Tech_Buil_SortingCenter_Lv3_Opt2",
            (self, context) => self.RegisterBattleCardForceBonus(context, (int)GetValue(context.TechData, 0), GetValue(context.TechData, 1)));

        AddUnit(rules, "Tech_Buil_NavStation_Opt1",
            (techData, _) => Modules(
                new MainPropertyPercentBuff(CreatureMainProperty.Speed, GetValue(techData, 0)),
                new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -GetValue(techData, 1))));
        AddActivation(rules, "Tech_Buil_NavStation_Opt2",
            (self, context) => self.RegisterDiscardConversionRateReduction(context, (int)GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_NavStation_Opt3",
            (self, context) => self.RegisterNextBuildPhaseCoin(context, (int)GetValue(context.TechData, 0)));

        AddActivation(rules, "Tech_Buil_FarmBase_Lv2_Opt2",
            (self, context) => self.AddMaxSupply(context, GetValue(context.TechData, 0)), registerUnitBuffsAfterActivate: true);
        AddUnit(rules, "Tech_Buil_FarmBase_Lv2_Opt2",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_FarmBase_Lv3_Opt2",
            (techData, _) => Modules(new ConditionalLowHpDamageBonusBuff(GetValue(techData, 0), GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_QualityCheck_Opt1",
            (techData, _) => Modules(new EnemySizeAttackSpeedAuraBuff(GetValue(techData, 0), UnitSize.Small, -GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_QualityCheck_Opt2",
            (techData, _) => Modules(
                new MainPropertyPercentBuff(CreatureMainProperty.Health, GetValue(techData, 0)),
                new MainPropertyPercentBuff(CreatureMainProperty.Speed, -GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_QualityCheck_Opt3",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.WeightLevel, GetValue(techData, 0))));
        AddActivation(rules, "Tech_Buil_QualityCheck_Opt4",
            (self, context) => self.RegisterBuildingBuffForArmyForceAtMost(context, maxForce: (int)GetValue(context.TechData, 0)));
        AddBuilding(rules, "Tech_Buil_QualityCheck_Opt4",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Health, GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_FireAcademy_Opt1",
            (techData, _) => Modules(new FirstIncomingDamageReductionBuff(GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_FireAcademy_Opt2",
            (techData, _) => Modules(new OutgoingAttackDebuffBuff(-GetValue(techData, 1), GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_FireAcademy_Opt3",
            (techData, _) => Modules(new PercentDamageReductionBuff(GetValue(techData, 0))));
        AddActivation(rules, "Tech_Buil_FireAcademy_Opt4",
            (self, context) => self.RegisterBuildingEntityPropertyBuff(context, CreatureMainProperty.Def, GetValue(context.TechData, 0)));

        AddActivation(rules, "Tech_Buil_FireHQ_Lv2_Opt2",
            (self, context) => self.RegisterBattleDeathSupplyCoin(context, (int)GetValue(context.TechData, 0)));
        AddUnit(rules, "Tech_Buil_FireHQ_Lv3_Opt2",
            (techData, _) => Modules(new ExtraHitFlatDamageBuff((int)GetValue(techData, 0), GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_SecurityOffice_Lv2_Opt2",
            (techData, _) => Modules(new MeleeVsRangedDamageBonusBuff(GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_SecurityOffice_Lv3_Opt2",
            (techData, _) => Modules(new ConsecutiveSameTargetBonusDamageBuff((int)GetValue(techData, 0), GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_SurveillanceRoom_Opt1",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_SurveillanceRoom_Opt2",
            (techData, _) => Modules(new BehindSecurityRangedAttackAuraBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddActivation(rules, "Tech_Buil_SurveillanceRoom_Opt3",
            (self, context) => self.RegisterBuildingEntityPropertyBuff(context, CreatureMainProperty.Sight, GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_SurveillanceRoom_Opt4",
            (self, context) => self.RegisterSourceBuildingWatcher(context,
                _ => Modules(new EnemyEnterFriendlyStrongholdDamageWatcherBuff(GetValue(context.TechData, 0)))));

        AddUnit(rules, "Tech_Buil_WildernessCamp_Lv2_Opt2",
            (techData, _) => Modules(new StationaryAttackPercentBuff(GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_WildernessCamp_Lv3_Opt2",
            (techData, _) => Modules(new IdleNextAttackCriticalBuff(GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_Watchtower_Opt1",
            (techData, _) => Modules(new RangeBonusBuff(GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_Watchtower_Opt2",
            (techData, _) => Modules(new FlatAttackBonusBuff(GetValue(techData, 0)), new AttackSpeedBonusBuff(-GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_Watchtower_Opt3",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Sight, GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_Watchtower_Opt4",
            (techData, _) => Modules(new CriticalDamageBonusBuff(GetValue(techData, 0))));

        AddActivation(rules, "Tech_Buil_GreenhouseGarden_Lv3_Opt2",
            (self, context) => self.RegisterArmyBuffInStrongholdsWithDifferentArmyArchetypes(context));
        AddUnit(rules, "Tech_Buil_GreenhouseGarden_Lv2_Opt2",
            (techData, _) => Modules(new LightMeleeReflectDamageBuff(GetValue(techData, 0))));
        AddBuilding(rules, "Tech_Buil_GreenhouseGarden_Lv3_Opt2",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Health, GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_BreedingRoom_Opt1",
            (techData, _) => Modules(new NearbyFriendlyCountDefBuff(GetValue(techData, 0), (int)GetValue(techData, 1), GetValue(techData, 2))));
        AddActivation(rules, "Tech_Buil_BreedingRoom_Opt2",
            (self, context) => self.RegisterSourceBuildingWatcher(context,
                _ => Modules(new EnemyInFriendlyStrongholdDefAuraWatcherBuff(GetValue(context.TechData, 0)))));
        AddUnit(rules, "Tech_Buil_BreedingRoom_Opt3",
            (techData, _) => Modules(new OnDeathHealNearbyAlliesBuff(GetValue(techData, 0), GetValue(techData, 1))));
        AddActivation(rules, "Tech_Buil_BreedingRoom_Opt4",
            (self, context) => self.RegisterArmyBuffInStrongholdsWithArchetypeBuilding(context, Archetype.Gardening));
        AddBuilding(rules, "Tech_Buil_BreedingRoom_Opt4",
            (techData, _) => Modules(new MainPropertyPercentBuff(CreatureMainProperty.Health, GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_TopHospital_Lv2_Opt2",
            (techData, _) => Modules(new NearbyMedicalDelayedDamageBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_TopHospital_Lv3_Opt2",
            (techData, _) => Modules(new FatalDamageProtectionBuff(GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_Radiology_Opt1",
            (techData, _) => Modules(new OnHealedTimedStatsBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_Radiology_Opt3",
            (techData, _) => Modules(new OnDeathEnemyAttackDebuffBuff(GetValue(techData, 0), -GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_Radiology_Opt2",
            (techData, _) => Modules(new OutOfCombatHealToThresholdOnceBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddActivation(rules, "Tech_Buil_Radiology_Opt4",
            (self, context) => self.RegisterNurseHealTargetThreshold(context, GetValue(context.TechData, 0)));

        AddUnit(rules, "Tech_Buil_SwallowNest_Lv2_Opt2",
            (techData, _) => Modules(new MissingHealthAttackSpeedBuff(GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_SwallowNest_Lv3_Opt2",
            (techData, _) => Modules(new TimedBuffOnSpawnModule(GetValue(techData, 0), () => Modules(
                new FlatAttackBonusBuff(GetValue(techData, 1)),
                new AttackSpeedBonusBuff(GetValue(techData, 2)),
                new HealthDrainOverTimeBuff(GetValue(techData, 3))))));

        AddActivation(rules, "Tech_Buil_TrainingRoom_Opt1",
            (self, context) => self.RegisterTrainingRoomBuildingBuff(context));
        AddBuilding(rules, "Tech_Buil_TrainingRoom_Opt1",
            (_, techId) => Modules(new PercentAttackBonusBuff(ParseEncodedValue(techId))));
        AddUnit(rules, "Tech_Buil_TrainingRoom_Opt2",
            (techData, _) => Modules(new LoneUnitBonusBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_TrainingRoom_Opt3",
            (techData, _) => Modules(new OnKillFlatGrowthBuff((int)GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_TrainingRoom_Opt4",
            (techData, _) => Modules(new OutOfCombatStickyMoveSpeedBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));

        AddSelfBuilding(rules, "Tech_Buil_Monitor_Lv2",
            techData => Modules(new BlindChanceBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_Restroom_Lv2",
            techData => Modules(new RestroomQueueModifierBuff((int)GetValue(techData, 0), GetValue(techData, 1))));
        AddSelfBuilding(rules, "Tech_Buil_Restroom_Lv3",
            techData => Modules(new RestroomQueueModifierBuff((int)GetValue(techData, 0), GetValue(techData, 1))));
        AddSelfBuilding(rules, "Tech_Buil_SortingTable_Lv2",
            techData => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_SortingTable_Lv3",
            techData => Modules(new KnockbackOnOutgoingDamageBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_MeatRack_Lv3",
            techData => Modules(new PullOnOutgoingDamageBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_SprinklerHead_Lv2",
            techData => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_SprinklerHead_Lv3",
            techData => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_Bollard_Lv3",
            techData => Modules(CreateTauntModule((int)GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_RoseBush_Lv2",
            techData => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_RoseBush_Lv3",
            techData => Modules(new AttackSpeedBonusBuff(GetValue(techData, 0))));
        AddSelfBuilding(rules, "Tech_Buil_BallLauncher_Lv3",
            techData => Modules(
                new AttackSpeedBonusBuff(GetValue(techData, 0)),
                new AmmoReloadDelayModifierBuff(-GetValue(techData, 1))));

        AddActivation(rules, "Tech_Buil_NavStation_Opt4",
            (self, context) => self.RegisterSettlementOffsetRate(context, (int)GetValue(context.TechData, 0)));
        AddSkipped(rules, "Tech_Buil_ResearchCenter_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_ResearchCenter_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_DreamPark_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_DreamPark_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SortingCenter_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SortingCenter_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_FarmBase_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_FarmBase_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_FireHQ_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_FireHQ_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SecurityOffice_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SecurityOffice_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_WildernessCamp_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_WildernessCamp_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_GreenhouseGarden_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_GreenhouseGarden_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_TopHospital_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_TopHospital_Lv3_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SwallowNest_Lv2_Opt1", "技能系统未接入");
        AddSkipped(rules, "Tech_Buil_SwallowNest_Lv3_Opt1", "技能系统未接入");

        return rules;
    }

    private static void AddActivation(
        Dictionary<string, RuntimeTechRule> rules,
        string techId,
        Action<BuildingTechRuntimeEffectSO, TechEffectContext> action,
        bool registerUnitBuffsAfterActivate = false)
    {
        RuntimeTechRule rule = GetOrCreateRule(rules, techId);
        rule.Activate = action;
        rule.RegisterUnitBuffsAfterActivate = registerUnitBuffsAfterActivate;
    }

    private static void AddUnit(
        Dictionary<string, RuntimeTechRule> rules,
        string techId,
        Func<TechData, string, List<BuffCallback>> factory)
    {
        GetOrCreateRule(rules, techId).CreateUnitModules = factory;
    }

    private static void AddBuilding(
        Dictionary<string, RuntimeTechRule> rules,
        string techId,
        Func<TechData, string, List<BuffCallback>> factory)
    {
        GetOrCreateRule(rules, techId).CreateBuildingModules = factory;
    }

    private static void AddSelfBuilding(
        Dictionary<string, RuntimeTechRule> rules,
        string techId,
        Func<TechData, List<BuffCallback>> factory)
    {
        AddActivation(rules, techId,
            (self, context) => self.RegisterSourceBuildingWatcher(context, _ => factory?.Invoke(context.TechData)));
    }

    private static void AddSkipped(Dictionary<string, RuntimeTechRule> rules, string techId, string reason)
    {
        GetOrCreateRule(rules, techId).SkipReason = reason;
    }

    private static RuntimeTechRule GetOrCreateRule(Dictionary<string, RuntimeTechRule> rules, string techId)
    {
        if (!rules.TryGetValue(techId, out RuntimeTechRule rule))
        {
            rule = new RuntimeTechRule();
            rules.Add(techId, rule);
        }

        return rule;
    }

    private static bool TryGetRule(string techId, out RuntimeTechRule rule)
    {
        return s_Rules.TryGetValue(GetRootTechId(techId), out rule);
    }

    private static List<BuffCallback> Modules(params BuffCallback[] modules)
    {
        return new List<BuffCallback>(modules);
    }

    private static BuffCallback CreateTauntModule(int tauntValue)
    {
        var module = new TauntBuffCallback();
        module.SetTauntValue(tauntValue);
        return module;
    }

    private void AddMaxSupply(TechEffectContext context, Fix64 amount)
    {
        if (context.OwnerFactionId != EntitySideHelper.PlayerFactionId || amount == Fix64.Zero)
            return;

        InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, Mathf.Max(0, (int)amount), true);
    }

    private void RegisterDiscardBuff(TechEffectContext context, Fix64 healthBonus, Fix64 attackSpeedPercent)
    {
        EnsureEventSubscriptions();
        m_DiscardBuffs[context.TechData.Identifier] = new DiscardBuffSpec
        {
            TechId = context.TechData.Identifier,
            OwnerFactionId = context.OwnerFactionId,
            HealthBonus = healthBonus,
            AttackSpeedPercent = attackSpeedPercent,
        };
    }

    private void EnsureEventSubscriptions()
    {
        if (m_EventsSubscribed)
            return;

        LogicCardCommandService.CardResolved += OnLogicCardResolved;
        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;
        LogicUnitDeathEventService.UnitDied += OnLogicUnitDied;
        m_EventsSubscribed = true;
    }

    private void UnsubscribeRuntimeEvents()
    {
        if (!m_EventsSubscribed)
            return;

        LogicCardCommandService.CardResolved -= OnLogicCardResolved;
        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        LogicUnitDeathEventService.UnitDied -= OnLogicUnitDied;
        m_EventsSubscribed = false;
    }

    private void OnDisable()
    {
        UnsubscribeRuntimeEvents();
    }

    private void OnLogicCardResolved(LogicCardResolution resolution)
    {
        if (!LogicCardCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Building tech received a card resolution outside the logic card apply window.");

        if (resolution.Kind == LogicCardCommandKind.Discard)
        {
            ApplyAllDiscardBuffs();
            return;
        }

        if (resolution.Kind != LogicCardCommandKind.Play || m_SortingCenterCardForceSpecs.Count == 0)
            return;

        string sourceBuildingInstanceId = resolution.SourceBuildingInstanceId;
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            return;
        IBuildingLogicContext sourceBuilding = LogicBuildingQueryService.GetRequiredByInstanceId(sourceBuildingInstanceId);
        if (!IsArmyBuilding(sourceBuilding))
            throw new InvalidOperationException(
                $"Played card source '{sourceBuildingInstanceId}' is not an army building.");

        m_BattleCardCountsByFaction.TryGetValue(sourceBuilding.OwnerFactionId, out int currentCount);
        m_BattleCardCountsByFaction[sourceBuilding.OwnerFactionId] = checked(currentCount + 1);
    }

    private void ApplyAllDiscardBuffs()
    {
        if (m_DiscardBuffs.Count == 0)
            return;

        foreach (DiscardBuffSpec spec in m_DiscardBuffs.Values)
            ApplyDiscardBuff(spec);
    }

    private void OnLogicUnitDied(IEntityContext victim)
    {
        if (m_FireHqDeathSupplySpecs.Count == 0)
            return;
        if (!LogicDamageEventService.IsApplying)
            throw new InvalidOperationException("Building tech received a unit death outside the logic damage apply window.");
        if (victim == null)
            throw new ArgumentNullException(nameof(victim));

        int ownerFactionId = EntitySideHelper.ToFactionId(victim.Side);
        if (ownerFactionId < 0)
            return;

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        if (phase != GamePhase.Invade && phase != GamePhase.Defend)
            return;

        int victimSupply = victim.CharacterData != null ? Math.Max(0, victim.CharacterData.Supply) : 0;
        m_DeadSupplyByFaction.TryGetValue(ownerFactionId, out int current);
        m_DeadSupplyByFaction[ownerFactionId] = checked(current + victimSupply);
    }

    private void ApplyDiscardBuff(DiscardBuffSpec spec)
    {
        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (manager == null)
            throw new InvalidOperationException("Building tech discard resolution requires GlobalBuffManager.");

        string uniqueTechId = $"{DiscardFutureBuffPrefix}{spec.TechId}_{m_DiscardCounter++}";
        TechData techData = TechDataModel.GetTechData(spec.TechId);
        foreach (UnitType unitType in Enum.GetValues(typeof(UnitType)))
        {
            manager.RegisterUnitBuff(unitType, spec.OwnerFactionId, uniqueTechId, this, techData);
        }

        SideType targetSide = EntitySideHelper.ToSide(spec.OwnerFactionId);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext ma = all[i];
            if (ma == null || !ma.Alive || ma.Side != targetSide)
                continue;

            var comp = ma.BuffComp as CharacterBuffComp;
            comp?.AddBuff(BuffData.Create(
                id: $"{DiscardFieldBuffPrefix}{uniqueTechId}_{ma.LogicEntityId.Value}",
                duration: Fix64.Zero,
                isForever: true,
                maxStack: 1,
                modules: CreateDiscardModules(spec)), ma);
        }
    }

    private List<BuffCallback> CreateDiscardModules(DiscardBuffSpec spec)
    {
        var modules = new List<BuffCallback>();
        if (spec.HealthBonus != Fix64.Zero)
            modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Health, spec.HealthBonus));
        if (spec.AttackSpeedPercent != Fix64.Zero)
            modules.Add(new AttackSpeedBonusBuff(spec.AttackSpeedPercent));
        return modules;
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        if (!LogicPhaseCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Building tech received a phase transition outside the logic phase apply window.");

        if (newPhase == GamePhase.Invade || newPhase == GamePhase.Defend)
            ResetBattleCardCounts();

        if (!InGameDataModel.IsBuildPhase(newPhase))
            return;

        GrantPendingBuildPhaseCoins();
        GrantFireHqDeathSupplyCoins();

        var manager = GameEntry.GetComponent<GlobalBuffManager>();
        if (m_DiscardBuffs.Count > 0 && manager == null)
            throw new InvalidOperationException("Building tech phase transition requires GlobalBuffManager for discard buffs.");
        foreach (DiscardBuffSpec spec in m_DiscardBuffs.Values)
        {
            manager.UnregisterUnitBuffByTechPrefix(spec.OwnerFactionId, DiscardFutureBuffPrefix);
        }

        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i]?.BuffComp is CharacterBuffComp comp)
                comp.RemoveBuffsByPrefix(DiscardFieldBuffPrefix);
        }

        m_DiscardCounter = 0;
    }

    private void RegisterNextBuildPhaseCoin(TechEffectContext context, int amount)
    {
        if (amount <= 0)
            return;

        EnsureEventSubscriptions();
        m_PendingBuildPhaseCoinsByFaction.TryGetValue(context.OwnerFactionId, out int current);
        m_PendingBuildPhaseCoinsByFaction[context.OwnerFactionId] = Mathf.Max(0, current + amount);
    }

    private void GrantPendingBuildPhaseCoins()
    {
        if (m_PendingBuildPhaseCoinsByFaction.Count == 0)
            return;

        foreach (var pair in m_PendingBuildPhaseCoinsByFaction)
        {
            if (pair.Value <= 0 || pair.Key != EntitySideHelper.PlayerFactionId)
                continue;

            InGameDataModel.TryModifyValue(IngameValueType.Coin, pair.Value, true);
        }

        m_PendingBuildPhaseCoinsByFaction.Clear();
    }

    private void RegisterCurrentDayCreatureArmyForcePercent(TechEffectContext context, Fix64 percent)
    {
        if (percent == Fix64.Zero)
            return;

        int unlockDay = InGameDataModel.GetValue(IngameValueType.Day);
        context.GlobalBuffManager.RegisterRuntimeArmyForceRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building =>
            {
                if (InGameDataModel.GetValue(IngameValueType.Day) != unlockDay || !IsArmyBuilding(building) || !ArmyBuildingUnitHasTag(building, UnitTag.Creature))
                    return Fix64.Zero;

                return Fix64.Floor(building.GetArmyForceWithoutRuntimeRules() * percent / (Fix64)100);
            });
    }

    private void RegisterEnemyArmyForceReduction(TechEffectContext context, Fix64 reductionPercent)
    {
        EnemyArmyForceModifierService.RegisterReduction(context.TechId, context.OwnerFactionId, reductionPercent);
    }

    private void RegisterBattleCardForceBonus(TechEffectContext context, int cardLimit, Fix64 bonusForce)
    {
        if (cardLimit <= 0 || bonusForce == Fix64.Zero)
            return;

        EnsureEventSubscriptions();
        m_SortingCenterCardForceSpecs[context.TechId] = new SortingCenterCardForceSpec
        {
            TechId = context.TechId,
            OwnerFactionId = context.OwnerFactionId,
            CardLimit = cardLimit,
            BonusForce = bonusForce,
        };

        context.GlobalBuffManager.RegisterRuntimeArmyForceRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building =>
            {
                if (!IsArmyBuilding(building))
                    return Fix64.Zero;

                m_BattleCardCountsByFaction.TryGetValue(building.OwnerFactionId, out int playedCount);
                return playedCount < cardLimit ? bonusForce : Fix64.Zero;
            });
    }

    private void RegisterDiscardConversionRateReduction(TechEffectContext context, int reduction)
    {
        DiscardRewardModifierService.RegisterRateReduction(context.TechId, context.OwnerFactionId, reduction);
    }

    private void RegisterSettlementOffsetRate(TechEffectContext context, int offsetRate)
    {
        SettlementOffsetRateService.RegisterOffsetRate(context.TechId, context.OwnerFactionId, offsetRate);
    }

    private void RegisterBattleDeathSupplyCoin(TechEffectContext context, int supplyPerCoin)
    {
        if (supplyPerCoin <= 0)
            return;

        EnsureEventSubscriptions();
        m_FireHqDeathSupplySpecs[context.TechId] = new FireHqDeathSupplySpec
        {
            TechId = context.TechId,
            OwnerFactionId = context.OwnerFactionId,
            SupplyPerCoin = supplyPerCoin,
        };
    }

    private void GrantFireHqDeathSupplyCoins()
    {
        if (m_FireHqDeathSupplySpecs.Count == 0 || m_DeadSupplyByFaction.Count == 0)
        {
            m_DeadSupplyByFaction.Clear();
            return;
        }

        foreach (FireHqDeathSupplySpec spec in m_FireHqDeathSupplySpecs.Values)
        {
            if (spec.OwnerFactionId != EntitySideHelper.PlayerFactionId || spec.SupplyPerCoin <= 0)
                continue;

            m_DeadSupplyByFaction.TryGetValue(spec.OwnerFactionId, out int deadSupply);
            int coin = deadSupply / spec.SupplyPerCoin;
            if (coin > 0)
                InGameDataModel.TryModifyValue(IngameValueType.Coin, coin, true);
        }

        m_DeadSupplyByFaction.Clear();
    }

    private void ResetBattleCardCounts()
    {
        m_BattleCardCountsByFaction.Clear();
    }

    private void RegisterSourceBuildingWatcher(TechEffectContext context, Func<IBuildingLogicContext, List<BuffCallback>> createModules)
    {
        if (createModules == null)
            return;

        context.GlobalBuffManager.RegisterPersistentBuildingEntityBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building => building != null && string.Equals(building.BuildingInstanceId, context.SourceBuildingInstanceId, StringComparison.Ordinal),
            createModules);
    }

    private void RegisterNurseHealTargetThreshold(TechEffectContext context, Fix64 healthPercentThreshold)
    {
        HealingTargetFilterService.RegisterNurseHealthThreshold(context.TechId, context.OwnerFactionId, healthPercentThreshold);
    }

    private void ApplyArmyForceIfAtLeast(TechEffectContext context, int minForce, Fix64 bonusForce)
    {
        if (bonusForce == Fix64.Zero)
            return;

        context.GlobalBuffManager.RegisterRuntimeArmyForceRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building => IsArmyBuilding(building) && building.GetArmyForceWithoutRuntimeRules() >= minForce
                ? bonusForce
                : Fix64.Zero);
    }

    private void ApplyArmyForceInStrongholdsWithArchetype(TechEffectContext context, Archetype archetype, Fix64 bonusForce)
    {
        if (bonusForce == Fix64.Zero)
            return;

        context.GlobalBuffManager.RegisterRuntimeArmyForceRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building => IsArmyBuilding(building)
                        && LogicBuildingQueryService.HasBuildingArchetype(building, archetype)
                ? bonusForce
                : Fix64.Zero);
    }

    private void RegisterStrongholdCostDiscount(TechEffectContext context, int discountPerArchetype)
    {
        if (discountPerArchetype <= 0)
            return;

        IBuildingLogicContext sourceBuilding = LogicBuildingQueryService.GetRequiredByInstanceId(
            context.SourceBuildingInstanceId);
        if (string.IsNullOrWhiteSpace(sourceBuilding.StrongholdId))
            throw new InvalidOperationException(
                $"Research center cost discount source has no stronghold. techId={context.TechId}, buildingInstanceId={context.SourceBuildingInstanceId}.");

        BuildingCostModifierService.RegisterStrongholdArchetypeDiscount(
            sourceBuilding.StrongholdId,
            context.TechId,
            context.OwnerFactionId,
            discountPerArchetype);
    }

    private void RegisterBuildingBuffForArmyForceAtMost(TechEffectContext context, int maxForce)
    {
        context.GlobalBuffManager.RegisterPersistentBuildingBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            this,
            context.TechData,
            building => IsArmyBuilding(building) && building.GetArmyForce() <= maxForce);
    }

    private void RegisterTrainingRoomBuildingBuff(TechEffectContext context)
    {
        Fix64 forcePerStep = GetValue(context.TechData, 0);
        Fix64 attackPercentPerStep = GetValue(context.TechData, 1);
        if (forcePerStep <= Fix64.Zero || attackPercentPerStep == Fix64.Zero)
            return;

        context.GlobalBuffManager.RegisterPersistentBuildingBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            this,
            context.TechData,
            building => IsArmyBuilding(building) && CalculateTrainingRoomSteps(building, forcePerStep) > 0,
            building =>
            {
                int steps = CalculateTrainingRoomSteps(building, forcePerStep);
                if (steps <= 0)
                    return null;

                Fix64 percent = attackPercentPerStep * (Fix64)steps;
                return $"{context.TechId}|{percent}";
            });
    }

    private void RegisterArmyBuffInStrongholdsWithDifferentArmyArchetypes(TechEffectContext context)
    {
        context.GlobalBuffManager.RegisterPersistentBuildingBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            this,
            context.TechData,
            building => IsArmyBuilding(building)
                        && LogicBuildingQueryService.HasDifferentArmyArchetype(building));
    }

    private void RegisterArmyBuffInStrongholdsWithArchetypeBuilding(TechEffectContext context, Archetype archetype)
    {
        context.GlobalBuffManager.RegisterPersistentBuildingBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            this,
            context.TechData,
            building => IsArmyBuilding(building)
                        && LogicBuildingQueryService.HasBuildingArchetype(building, archetype));
    }

    private void RegisterBuildingEntityPropertyBuff(TechEffectContext context, CreatureMainProperty property, Fix64 amount)
    {
        if (amount == Fix64.Zero)
            return;

        context.GlobalBuffManager.RegisterPersistentBuildingEntityBuffRule(
            context.OwnerFactionId,
            context.SourceBuildingInstanceId,
            context.TechId,
            building => building != null && building.BuildingData != null,
            _ => Modules(new MainPropertyAdditiveBuff(property, amount)));
    }

    private static int CalculateTrainingRoomSteps(IBuildingLogicContext building, Fix64 forcePerStep)
    {
        if (!IsArmyBuilding(building) || forcePerStep <= Fix64.Zero)
            return 0;

        return (int)Fix64.Floor((Fix64)building.GetArmyForce() / forcePerStep);
    }

    private static bool IsArmyBuilding(IBuildingLogicContext building)
    {
        return building?.BuildingData != null && building.BuildingData.Type == BuilType.Army;
    }

    private static bool ArmyBuildingUnitHasTag(IBuildingLogicContext building, UnitTag tag)
    {
        if (!IsArmyBuilding(building) || string.IsNullOrWhiteSpace(building.BuildingData.UnitID))
            return false;

        CharacterDataDetail row = FindCharacterData(building.BuildingData.UnitID);
        return HasTag(row?.UnitTags, tag);
    }

    private static CharacterDataDetail FindCharacterData(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || GF.DataTable == null)
            return null;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        return table?.GetDataRow(row => row.CharacterKey == characterKey);
    }

    internal static bool HasTag(UnitTag[] tags, UnitTag tag)
    {
        if (tags == null)
            return false;

        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag)
                return true;
        }

        return false;
    }

    private static Fix64 GetValue(TechData techData, int index)
    {
        if (techData?.UniqueValues == null || index < 0 || index >= techData.UniqueValues.Length)
            return Fix64.Zero;

        return techData.UniqueValues[index];
    }

    private static Fix64 ParseEncodedValue(string techId)
    {
        int index = techId.LastIndexOf('|');
        if (index < 0 || index >= techId.Length - 1)
            return Fix64.Zero;

        return Fix64.Parse(techId.Substring(index + 1));
    }

    private static string GetRootTechId(string techId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return techId;

        int index = techId.IndexOf('|');
        return index >= 0 ? techId.Substring(0, index) : techId;
    }
}

public sealed class ConditionalCoinAttackSpeedBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.1f;
    private readonly int m_CoinThreshold;
    private readonly Fix64 m_AttackSpeedPercent;
    private Fix64 m_Timer;
    private bool m_Applied;
    private Fix64 m_Factor = Fix64.One;

    public ConditionalCoinAttackSpeedBuff(int coinThreshold, Fix64 attackSpeedPercent)
    {
        m_CoinThreshold = coinThreshold;
        m_AttackSpeedPercent = attackSpeedPercent;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        if (InGameDataModel.GetValue(IngameValueType.Coin) <= m_CoinThreshold)
            Apply();
        else
            Remove();
    }

    public override void OnRemove()
    {
        Remove();
    }

    private void Apply()
    {
        if (m_Applied || m_AttackSpeedPercent == Fix64.Zero)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            return;

        m_Factor = Fix64.One / (Fix64.One + m_AttackSpeedPercent / (Fix64)100);
        weapon.ApplyMultiplier(WeaponStatId.Interval, m_Factor);
        m_Applied = true;
    }

    private void Remove()
    {
        if (!m_Applied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null && m_Factor != Fix64.Zero)
            weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_Factor);

        m_Applied = false;
        m_Factor = Fix64.One;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_Applied);
        hasher.Add(m_Factor.RawValue);
    }
}

public sealed class EnemySizeAttackSpeedAuraBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.1f;
    private readonly Fix64 m_Radius;
    private readonly UnitSize m_TargetSize;
    private readonly Fix64 m_AttackSpeedPercent;
    private readonly HashSet<int> m_Affected = new();
    private Fix64 m_Timer;

    public EnemySizeAttackSpeedAuraBuff(Fix64 radius, UnitSize targetSize, Fix64 attackSpeedPercent)
    {
        m_Radius = radius;
        m_TargetSize = targetSize;
        m_AttackSpeedPercent = attackSpeedPercent;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval || hostEntity == null)
            return;

        m_Timer = Fix64.Zero;
        Refresh();
    }

    public override void OnRemove()
    {
        ClearAffected();
    }

    private void Refresh()
    {
        var current = new HashSet<int>();
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext enemy = all[i];
            if (enemy == null || !enemy.Alive || !EntityCombatTeamHelper.IsEnemy(hostEntity, enemy))
                continue;
            if (enemy.CharacterData == null || enemy.CharacterData.Size != m_TargetSize)
                continue;
            if (hostEntity.LogicFrameDistanceToTargetSurfaceFixed(enemy) > radius)
                continue;

            int enemyId = enemy.LogicEntityId.Value;
            current.Add(enemyId);
            if (!m_Affected.Contains(enemyId))
                AddDebuff(enemy);
        }

        var removeIds = new List<int>();
        foreach (int id in m_Affected)
        {
            if (!current.Contains(id))
                removeIds.Add(id);
        }

        for (int i = 0; i < removeIds.Count; i++)
            RemoveDebuff(removeIds[i]);
    }

    private void AddDebuff(IEntityContext enemy)
    {
        var comp = enemy.BuffComp as CharacterBuffComp;
        if (comp == null)
            return;

        comp.AddBuff(BuffData.Create(
            id: GetBuffId(enemy.LogicEntityId.Value),
            duration: Fix64.Zero,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new AttackSpeedBonusBuff(m_AttackSpeedPercent) }), enemy);
        m_Affected.Add(enemy.LogicEntityId.Value);
    }

    private void RemoveDebuff(int entityId)
    {
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext entity = all[i];
            if (entity != null && entity.LogicEntityId.Value == entityId && entity.BuffComp is CharacterBuffComp comp)
            {
                comp.RemoveBuff(GetBuffId(entityId));
                break;
            }
        }

        m_Affected.Remove(entityId);
    }

    private void ClearAffected()
    {
        var ids = new List<int>(m_Affected);
        for (int i = 0; i < ids.Count; i++)
            RemoveDebuff(ids[i]);
    }

    private string GetBuffId(int entityId) => $"tech_enemy_size_aura_{buffData?.id}_{entityId}";

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        LogicDeterministicStateWriter.AddSortedIds(hasher, m_Affected);
    }
}

public sealed class MeleeVsRangedDamageBonusBuff : BuffCallback
{
    private readonly Fix64 m_BonusPercent;

    public MeleeVsRangedDamageBonusBuff(Fix64 bonusPercent)
    {
        m_BonusPercent = bonusPercent;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        if (m_BonusPercent == Fix64.Zero || target is not IEntityContext targetEntity)
            return baseDamage;
        if (targetEntity.CharacterData?.UnitTags == null || !HasTag(targetEntity.CharacterData.UnitTags, UnitTag.Ranged))
            return baseDamage;
        if (hostEntity?.CharacterData?.UnitTags == null || !HasTag(hostEntity.CharacterData.UnitTags, UnitTag.Melee))
            return baseDamage;

        return baseDamage * (Fix64.One + m_BonusPercent / (Fix64)100);
    }

    private static bool HasTag(UnitTag[] tags, UnitTag tag)
    {
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag)
                return true;
        }

        return false;
    }
}

public sealed class OutOfCombatStickyMoveSpeedBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.1f;
    private readonly Fix64 m_RequiredOutOfCombatSeconds;
    private readonly Fix64 m_MoveSpeedPercent;
    private readonly Fix64 m_StickySeconds;
    private Fix64 m_Timer;
    private Fix64 m_StickyTimer;
    private bool m_Applied;
    private IPropertyModifier m_Modifier;

    public OutOfCombatStickyMoveSpeedBuff(Fix64 requiredOutOfCombatSeconds, Fix64 moveSpeedPercent, Fix64 stickySeconds)
    {
        m_RequiredOutOfCombatSeconds = Fix64.Max(Fix64.Zero, requiredOutOfCombatSeconds);
        m_MoveSpeedPercent = moveSpeedPercent;
        m_StickySeconds = Fix64.Max(Fix64.Zero, stickySeconds);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        Fix64 elapsed = m_Timer;
        m_Timer = Fix64.Zero;
        var ma = hostEntity;
        if (ma == null)
            return;

        if (ma.IsOutOfCombat && ma.OutOfCombatElapsedLogicTime >= m_RequiredOutOfCombatSeconds)
        {
            m_StickyTimer = m_StickySeconds;
            Apply();
            return;
        }

        if (m_StickyTimer > Fix64.Zero)
        {
            m_StickyTimer = Fix64.Max(Fix64.Zero, m_StickyTimer - elapsed);
            Apply();
        }
        else
        {
            Remove();
        }
    }

    public override void OnRemove()
    {
        Remove();
    }

    private void Apply()
    {
        if (m_Applied || m_MoveSpeedPercent == Fix64.Zero)
            return;

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager == null)
            return;

        m_Modifier = PropertyDirectAdditiveModifier.Create(m_MoveSpeedPercent / (Fix64)100);
        propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, true);
        m_Applied = true;
    }

    private void Remove()
    {
        if (!m_Applied)
            return;

        var propertyManager = hostEntity?.CreatureProperties;
        if (propertyManager != null && m_Modifier != null)
            propertyManager.ModifyMainPropertyMul(CreatureMainProperty.Speed, NormalBaseValueTp.Buff, m_Modifier, false);

        m_Modifier = null;
        m_Applied = false;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_StickyTimer.RawValue);
        hasher.Add(m_Applied);
    }
}

public sealed class OutOfCombatHealToThresholdOnceBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.1f;
    private readonly Fix64 m_RequiredOutOfCombatSeconds;
    private readonly Fix64 m_HealthThresholdPercent;
    private readonly Fix64 m_AttackSpeedPenaltyPercent;
    private Fix64 m_Timer;
    private bool m_Triggered;
    private bool m_PenaltyApplied;
    private Fix64 m_AppliedAttackIntervalFactor;

    public OutOfCombatHealToThresholdOnceBuff(Fix64 requiredOutOfCombatSeconds, Fix64 healthThresholdPercent, Fix64 attackSpeedPenaltyPercent)
    {
        m_RequiredOutOfCombatSeconds = Fix64.Max(Fix64.Zero, requiredOutOfCombatSeconds);
        m_HealthThresholdPercent = healthThresholdPercent;
        m_AttackSpeedPenaltyPercent = attackSpeedPenaltyPercent;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (m_Triggered)
            return;

        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        var ma = hostEntity;
        if (ma == null || !ma.Alive || !ma.IsOutOfCombat || ma.OutOfCombatElapsedLogicTime < m_RequiredOutOfCombatSeconds)
            return;

        Trigger(ma);
    }

    public override void OnRemove()
    {
        RemoveAttackSpeedPenalty();
    }

    private void Trigger(IEntityContext ma)
    {
        m_Triggered = true;

        if (ma.CreatureProperties != null && m_HealthThresholdPercent > Fix64.Zero)
        {
            Fix64 maxHealth = ma.CreatureProperties.GetProperty(CreatureMainProperty.Health);
            Fix64 targetHealth = maxHealth * m_HealthThresholdPercent / (Fix64)100;
            Fix64 healAmount = targetHealth - ma.HealthValue;
            if (healAmount > Fix64.Zero)
                ma.Heal(healAmount);
        }

        ApplyAttackSpeedPenalty();
    }

    private void ApplyAttackSpeedPenalty()
    {
        if (m_PenaltyApplied || m_AttackSpeedPenaltyPercent <= Fix64.Zero)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon == null)
            return;

        Fix64 denom = Fix64.One - m_AttackSpeedPenaltyPercent / (Fix64)100;
        if (denom <= Fix64.Zero)
            return;

        m_AppliedAttackIntervalFactor = Fix64.One / denom;
        weapon.ApplyMultiplier(WeaponStatId.Interval, m_AppliedAttackIntervalFactor);
        m_PenaltyApplied = true;
    }

    private void RemoveAttackSpeedPenalty()
    {
        if (!m_PenaltyApplied)
            return;

        var weapon = hostEntity?.WeaponComp?.Data;
        if (weapon != null && m_AppliedAttackIntervalFactor != Fix64.Zero)
            weapon.ApplyMultiplier(WeaponStatId.Interval, Fix64.One / m_AppliedAttackIntervalFactor);

        m_PenaltyApplied = false;
        m_AppliedAttackIntervalFactor = Fix64.Zero;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_Triggered);
        hasher.Add(m_PenaltyApplied);
        hasher.Add(m_AppliedAttackIntervalFactor.RawValue);
    }
}

public sealed class ExtraHitFlatDamageBuff : BuffCallback
{
    private readonly int m_Threshold;
    private readonly Fix64 m_BonusPerExtraHit;

    public ExtraHitFlatDamageBuff(int threshold, Fix64 bonusPerExtraHit)
    {
        m_Threshold = Mathf.Max(0, threshold);
        m_BonusPerExtraHit = bonusPerExtraHit;
    }

    public override Fix64 ModifyOutgoingDamage(ITargetable target, Fix64 baseDamage)
    {
        int totalHits = DamageHelper.GetCurrentAttackTotalHits(hostEntity);
        int extraHits = Mathf.Max(0, totalHits - m_Threshold);
        return extraHits > 0 ? baseDamage + m_BonusPerExtraHit * (Fix64)extraHits : baseDamage;
    }
}

public sealed class LightMeleeReflectDamageBuff : BuffCallback
{
    private readonly Fix64 m_ReflectPercent;
    private bool m_Reflecting;

    public LightMeleeReflectDamageBuff(Fix64 reflectPercent)
    {
        m_ReflectPercent = reflectPercent;
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (m_Reflecting || modType != HealthModifyType.reduce || attacker == null || baseDamage <= Fix64.Zero || m_ReflectPercent <= Fix64.Zero)
            return baseDamage;
        if (attacker.CharacterData == null || attacker.CharacterData.Size != UnitSize.Small)
            return baseDamage;
        if (!BuildingTechRuntimeEffectSO.HasTag(attacker.CharacterData.UnitTags, UnitTag.Melee))
            return baseDamage;

        Fix64 reflect = baseDamage * m_ReflectPercent / (Fix64)100;
        if (reflect <= Fix64.Zero)
            return baseDamage;

        m_Reflecting = true;
        DamageHelper.DoDirectDamage(attacker, reflect, HealthModifyType.reduce, hostEntity);
        m_Reflecting = false;
        return baseDamage;
    }
}

public sealed class BehindSecurityRangedAttackAuraBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.15f;
    private readonly Fix64 m_AttackBonus;
    private readonly Fix64 m_ConeAngle;
    private readonly Fix64 m_Distance;
    private Fix64 m_Timer;
    private bool m_Applied;
    private string BuffId => $"tech_security_behind_attack_{buffData?.id}_{hostEntity?.LogicEntityId.Value}";

    public BehindSecurityRangedAttackAuraBuff(Fix64 attackBonus, Fix64 coneAngle, Fix64 distance)
    {
        m_AttackBonus = attackBonus;
        m_ConeAngle = Fix64.Max(Fix64.Zero, coneAngle);
        m_Distance = distance;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        if (ShouldApply())
            Apply();
        else
            Remove();
    }

    public override void OnRemove()
    {
        Remove();
    }

    private bool ShouldApply()
    {
        if (hostEntity == null || hostEntity.CharacterData == null || !BuildingTechRuntimeEffectSO.HasTag(hostEntity.CharacterData.UnitTags, UnitTag.Ranged))
            return false;

        Fix64 maxDistance = DistanceUnitConverter.ConvertToWorld(m_Distance);
        Fix64 coneAngle = m_ConeAngle;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext security = all[i];
            if (security == null || ReferenceEquals(security, hostEntity) || !security.Alive)
                continue;
            if (!EntityCombatTeamHelper.IsAlly(hostEntity, security))
                continue;
            if (security.CharacterData == null || security.CharacterData.Archetype != Archetype.Security)
                continue;

            FixVector2 offset = LogicEntityFrameSnapshotService.GetRequiredPosition(hostEntity)
                                - LogicEntityFrameSnapshotService.GetRequiredPosition(security);
            if (FixVector2.SqrMagnitude(offset) > maxDistance * maxDistance)
                continue;

            FixVector2 back = -LogicEntityFrameSnapshotService.GetRequiredForward(security);
            if (MonitorFacingUtility.IsDirectionWithinCone(back, offset, coneAngle))
                return true;
        }

        return false;
    }

    private void Apply()
    {
        if (m_Applied || m_AttackBonus == Fix64.Zero || hostEntity?.BuffComp is not CharacterBuffComp comp)
            return;

        comp.AddBuff(BuffData.Create(BuffId, Fix64.Zero, true, 1, new List<BuffCallback> { new FlatAttackBonusBuff(m_AttackBonus) }), hostEntity);
        m_Applied = true;
    }

    private void Remove()
    {
        if (!m_Applied)
            return;

        hostEntity?.BuffComp?.RemoveBuff(BuffId);
        m_Applied = false;
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        hasher.Add(m_Applied);
    }
}

public sealed class EnemyEnterFriendlyStrongholdDamageWatcherBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.2f;
    private readonly Fix64 m_Damage;
    private readonly Dictionary<int, string> m_LastStrongholdIdByEntity = new();
    private Fix64 m_Timer;

    public EnemyEnterFriendlyStrongholdDamageWatcherBuff(Fix64 damage)
    {
        m_Damage = damage;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval || m_Damage <= Fix64.Zero)
            return;

        m_Timer = Fix64.Zero;
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext enemy = all[i];
            if (enemy == null || !enemy.Alive || !EntityCombatTeamHelper.IsEnemy(hostEntity, enemy))
                continue;

            int enemyId = enemy.LogicEntityId.Value;
            m_LastStrongholdIdByEntity.TryGetValue(enemyId, out string previousId);
            int ownerFactionId = EntitySideHelper.ToFactionId(hostEntity.Side);
            if (!LogicBuildingQueryService.TryResolveOwnedStrongholdAtPosition(
                    enemy.LogicFramePositionFixed(),
                    ownerFactionId,
                    out string strongholdId))
            {
                m_LastStrongholdIdByEntity[enemyId] = null;
                continue;
            }

            if (!string.Equals(previousId, strongholdId, StringComparison.Ordinal))
                DamageHelper.DoDirectDamage(enemy, m_Damage, HealthModifyType.reduce, hostEntity);

            m_LastStrongholdIdByEntity[enemyId] = strongholdId;
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        LogicDeterministicStateWriter.AddSortedStringsById(hasher, m_LastStrongholdIdByEntity);
    }
}

public sealed class EnemyInFriendlyStrongholdDefAuraWatcherBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.2f;
    private readonly Fix64 m_DefPenalty;
    private readonly HashSet<int> m_Affected = new();
    private Fix64 m_Timer;

    public EnemyInFriendlyStrongholdDefAuraWatcherBuff(Fix64 defPenalty)
    {
        m_DefPenalty = defPenalty;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        Refresh();
    }

    public override void OnRemove()
    {
        foreach (int id in new List<int>(m_Affected))
            RemoveDebuff(id);
    }

    private void Refresh()
    {
        var current = new HashSet<int>();
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext enemy = all[i];
            if (enemy == null || !enemy.Alive || !EntityCombatTeamHelper.IsEnemy(hostEntity, enemy))
                continue;

            int ownerFactionId = EntitySideHelper.ToFactionId(hostEntity.Side);
            if (!LogicBuildingQueryService.TryResolveOwnedStrongholdAtPosition(
                    enemy.LogicFramePositionFixed(),
                    ownerFactionId,
                    out _))
                continue;

            int enemyId = enemy.LogicEntityId.Value;
            current.Add(enemyId);
            if (!m_Affected.Contains(enemyId))
                AddDebuff(enemy);
        }

        foreach (int id in new List<int>(m_Affected))
        {
            if (!current.Contains(id))
                RemoveDebuff(id);
        }
    }

    private void AddDebuff(IEntityContext enemy)
    {
        if (m_DefPenalty == Fix64.Zero || enemy.BuffComp is not CharacterBuffComp comp)
            return;

        int enemyId = enemy.LogicEntityId.Value;
        comp.AddBuff(BuffData.Create(GetBuffId(enemyId), Fix64.Zero, true, 1,
            new List<BuffCallback> { new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -m_DefPenalty) }), enemy);
        m_Affected.Add(enemyId);
    }

    private void RemoveDebuff(int entityId)
    {
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext entity = all[i];
            if (entity != null && entity.LogicEntityId.Value == entityId)
            {
                entity.BuffComp?.RemoveBuff(GetBuffId(entityId));
                break;
            }
        }

        m_Affected.Remove(entityId);
    }

    private string GetBuffId(int entityId) => $"tech_enemy_stronghold_def_{buffData?.id}_{entityId}";

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        LogicDeterministicStateWriter.AddSortedIds(hasher, m_Affected);
    }
}

public sealed class NearbyMedicalDelayedDamageBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private const float UpdateInterval = 0.15f;
    private readonly Fix64 m_Radius;
    private readonly Fix64 m_DelayPercent;
    private readonly Fix64 m_Duration;
    private readonly HashSet<int> m_Affected = new();
    private Fix64 m_Timer;

    public NearbyMedicalDelayedDamageBuff(Fix64 radius, Fix64 delayPercent, Fix64 duration)
    {
        m_Radius = radius;
        m_DelayPercent = delayPercent;
        m_Duration = Fix64.Max(Fix64.FromRaw(41), duration);
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        m_Timer += (Fix64)deltaTime;
        if (m_Timer < (Fix64)UpdateInterval)
            return;

        m_Timer = Fix64.Zero;
        Refresh();
    }

    public override void OnRemove()
    {
        foreach (int id in new List<int>(m_Affected))
            RemoveReceiver(id);
    }

    private void Refresh()
    {
        var current = new HashSet<int>();
        Fix64 radius = DistanceUnitConverter.ConvertToWorld(m_Radius);
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext ally = all[i];
            if (ally == null || !ally.Alive || !EntityCombatTeamHelper.IsAlly(hostEntity, ally))
                continue;
            if (hostEntity.LogicFrameDistanceToTargetSurfaceFixed(ally) > radius)
                continue;

            int allyId = ally.LogicEntityId.Value;
            current.Add(allyId);
            if (!m_Affected.Contains(allyId))
                AddReceiver(ally);
        }

        foreach (int id in new List<int>(m_Affected))
        {
            if (!current.Contains(id))
                RemoveReceiver(id);
        }
    }

    private void AddReceiver(IEntityContext ally)
    {
        if (ally.BuffComp is not CharacterBuffComp comp)
            return;

        int allyId = ally.LogicEntityId.Value;
        comp.AddBuff(BuffData.Create(GetBuffId(allyId), Fix64.Zero, true, 1,
            new List<BuffCallback> { new DelayedIncomingDamageReceiverBuff(m_DelayPercent, m_Duration) }), ally);
        m_Affected.Add(allyId);
    }

    private void RemoveReceiver(int entityId)
    {
        var all = EntityRegistry.AllEntities;
        for (int i = 0; i < all.Count; i++)
        {
            IEntityContext entity = all[i];
            if (entity != null && entity.LogicEntityId.Value == entityId)
            {
                entity.BuffComp?.RemoveBuff(GetBuffId(entityId));
                break;
            }
        }

        m_Affected.Remove(entityId);
    }

    private string GetBuffId(int entityId) => $"tech_medical_delayed_damage_{buffData?.id}_{entityId}";

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_Timer.RawValue);
        LogicDeterministicStateWriter.AddSortedIds(hasher, m_Affected);
    }
}

public sealed class DelayedIncomingDamageReceiverBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private readonly Fix64 m_DelayPercent;
    private readonly Fix64 m_Duration;
    private readonly List<DeferredDamage> m_DeferredDamages = new();
    private bool m_ApplyingDeferred;

    public DelayedIncomingDamageReceiverBuff(Fix64 delayPercent, Fix64 duration)
    {
        m_DelayPercent = delayPercent;
        m_Duration = Fix64.Max(Fix64.FromRaw(41), duration);
    }

    public override Fix64 ModifyIncomingDamage(IEntityContext attacker, Fix64 baseDamage, HealthModifyType modType)
    {
        if (m_ApplyingDeferred || modType != HealthModifyType.reduce || baseDamage <= Fix64.Zero || m_DelayPercent <= Fix64.Zero)
            return baseDamage;

        Fix64 delayed = baseDamage * m_DelayPercent / (Fix64)100;
        if (delayed <= Fix64.Zero)
            return baseDamage;

        m_DeferredDamages.Add(new DeferredDamage(delayed, m_Duration));
        return baseDamage - delayed;
    }

    public override void OnUpdate(Fix64 deltaTime)
    {
        if (hostEntity == null || !hostEntity.Alive || deltaTime <= Fix64.Zero)
            return;

        for (int i = m_DeferredDamages.Count - 1; i >= 0; i--)
        {
            DeferredDamage item = m_DeferredDamages[i];
            Fix64 tick = item.TotalDamage * deltaTime / item.TotalDuration;
            item.RemainingDamage -= tick;
            item.RemainingDuration -= deltaTime;
            if (item.RemainingDuration <= Fix64.Zero || item.RemainingDamage <= Fix64.Zero)
            {
                tick += item.RemainingDamage > Fix64.Zero ? item.RemainingDamage : Fix64.Zero;
                m_DeferredDamages.RemoveAt(i);
            }
            else
            {
                m_DeferredDamages[i] = item;
            }

            if (tick > Fix64.Zero)
            {
                m_ApplyingDeferred = true;
                DamageHelper.DoDirectDamage(hostEntity, tick, HealthModifyType.reduce);
                m_ApplyingDeferred = false;
            }
        }
    }

    private struct DeferredDamage
    {
        public Fix64 TotalDamage;
        public Fix64 RemainingDamage;
        public Fix64 TotalDuration;
        public Fix64 RemainingDuration;

        public DeferredDamage(Fix64 damage, Fix64 duration)
        {
            TotalDamage = damage;
            RemainingDamage = damage;
            TotalDuration = duration;
            RemainingDuration = duration;
        }
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_ApplyingDeferred);
        hasher.Add(m_DeferredDamages.Count);
        for (int i = 0; i < m_DeferredDamages.Count; i++)
        {
            DeferredDamage item = m_DeferredDamages[i];
            hasher.Add(item.TotalDamage.RawValue);
            hasher.Add(item.RemainingDamage.RawValue);
            hasher.Add(item.TotalDuration.RawValue);
            hasher.Add(item.RemainingDuration.RawValue);
        }
    }
}

public sealed class OnHealedTimedStatsBuff : BuffCallback
{
    private readonly Fix64 m_Duration;
    private readonly Fix64 m_AttackBonus;
    private readonly Fix64 m_DefBonus;

    public OnHealedTimedStatsBuff(Fix64 duration, Fix64 attackBonus, Fix64 defBonus)
    {
        m_Duration = Fix64.Max(Fix64.FromRaw(41), duration);
        m_AttackBonus = attackBonus;
        m_DefBonus = defBonus;
    }

    public override void OnHealed(Fix64 amount)
    {
        if (amount <= Fix64.Zero)
            return;

        if (hostEntity.BuffComp is not CharacterBuffComp comp)
            return;

        comp.AddBuff(BuffData.Create(
            $"tech_on_healed_stats_{buffData?.id}_{hostEntity.LogicEntityId.Value}",
            m_Duration,
            false,
            1,
            new List<BuffCallback>
            {
                new FlatAttackBonusBuff(m_AttackBonus),
                new MainPropertyAdditiveBuff(CreatureMainProperty.Def, m_DefBonus),
            }), hostEntity);
    }
}

public static class DiscardRewardModifierService
{
    private sealed class RateReduction
    {
        public string TechId;
        public int OwnerFactionId;
        public int Reduction;
    }

    private static readonly List<RateReduction> s_Reductions = new();
    private static readonly List<RateReduction> s_DeterministicReductions = new();
    private static readonly Comparison<RateReduction> s_ReductionComparison = CompareReductions;

    public static void Clear()
    {
        s_Reductions.Clear();
    }

    public static void RegisterRateReduction(string techId, int ownerFactionId, int reduction)
    {
        if (string.IsNullOrWhiteSpace(techId) || reduction <= 0)
            return;

        s_Reductions.RemoveAll(item => item.TechId == techId && item.OwnerFactionId == ownerFactionId);
        s_Reductions.Add(new RateReduction { TechId = techId, OwnerFactionId = ownerFactionId, Reduction = reduction });
    }

    public static int CalculateConversionRate(int baseRate)
    {
        int result = Mathf.Max(1, baseRate);
        for (int i = 0; i < s_Reductions.Count; i++)
        {
            RateReduction item = s_Reductions[i];
            if (item.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                result -= Mathf.Max(0, item.Reduction);
        }

        return LevelTagRuntime.ModifyDiscardRewardConversionRate(Mathf.Max(1, result));
    }

    internal static void WriteDeterministicState(LogicStateHasher hasher)
    {
        s_DeterministicReductions.Clear();
        s_DeterministicReductions.AddRange(s_Reductions);
        s_DeterministicReductions.Sort(s_ReductionComparison);
        hasher.Add(s_DeterministicReductions.Count);
        for (int i = 0; i < s_DeterministicReductions.Count; i++)
        {
            RateReduction item = s_DeterministicReductions[i];
            if (item == null || string.IsNullOrWhiteSpace(item.TechId) || item.Reduction <= 0)
                throw new InvalidOperationException("Discard reward modifier deterministic state contains an invalid reduction.");
            hasher.Add(item.TechId);
            hasher.Add(item.OwnerFactionId);
            hasher.Add(item.Reduction);
        }
    }

    private static int CompareReductions(RateReduction left, RateReduction right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        return result != 0 ? result : left.OwnerFactionId.CompareTo(right.OwnerFactionId);
    }
}

public static class EnemyArmyForceModifierService
{
    private sealed class Reduction
    {
        public string TechId;
        public int OwnerFactionId;
        public Fix64 Percent;
    }

    private static readonly List<Reduction> s_Reductions = new();
    private static readonly List<Reduction> s_DeterministicReductions = new();
    private static readonly Comparison<Reduction> s_ReductionComparison = CompareReductions;

    public static void Clear()
    {
        s_Reductions.Clear();
    }

    public static void RegisterReduction(string techId, int ownerFactionId, Fix64 percent)
    {
        if (string.IsNullOrWhiteSpace(techId) || percent <= Fix64.Zero)
            return;

        s_Reductions.RemoveAll(item => item.TechId == techId && item.OwnerFactionId == ownerFactionId);
        s_Reductions.Add(new Reduction { TechId = techId, OwnerFactionId = ownerFactionId, Percent = percent });
    }

    public static int CalculateSpawnCount(int baseCount)
    {
        Fix64 result = (Fix64)Mathf.Max(0, baseCount);
        for (int i = 0; i < s_Reductions.Count; i++)
        {
            Reduction item = s_Reductions[i];
            if (item.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                result *= Fix64.One - item.Percent / (Fix64)100;
        }

        return LevelTagRuntime.ModifyEnemySpawnCount(Mathf.Max(0, (int)Fix64.Ceiling(result)));
    }

    internal static void WriteDeterministicState(LogicStateHasher hasher)
    {
        s_DeterministicReductions.Clear();
        s_DeterministicReductions.AddRange(s_Reductions);
        s_DeterministicReductions.Sort(s_ReductionComparison);
        hasher.Add(s_DeterministicReductions.Count);
        for (int i = 0; i < s_DeterministicReductions.Count; i++)
        {
            Reduction item = s_DeterministicReductions[i];
            if (item == null || string.IsNullOrWhiteSpace(item.TechId) || item.Percent <= Fix64.Zero)
                throw new InvalidOperationException("Enemy army force modifier deterministic state contains an invalid reduction.");
            hasher.Add(item.TechId);
            hasher.Add(item.OwnerFactionId);
            hasher.Add(item.Percent.RawValue);
        }
    }

    private static int CompareReductions(Reduction left, Reduction right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        return result != 0 ? result : left.OwnerFactionId.CompareTo(right.OwnerFactionId);
    }
}

public static class HealingTargetFilterService
{
    private sealed class NurseThreshold
    {
        public string TechId;
        public int OwnerFactionId;
        public Fix64 HealthPercentThreshold;
    }

    private static readonly List<NurseThreshold> s_NurseThresholds = new();
    private static readonly List<NurseThreshold> s_DeterministicThresholds = new();
    private static readonly Comparison<NurseThreshold> s_ThresholdComparison = CompareThresholds;

    public static void Clear()
    {
        s_NurseThresholds.Clear();
    }

    public static void RegisterNurseHealthThreshold(string techId, int ownerFactionId, Fix64 healthPercentThreshold)
    {
        if (string.IsNullOrWhiteSpace(techId) || healthPercentThreshold <= Fix64.Zero)
            return;

        s_NurseThresholds.RemoveAll(item => item.TechId == techId && item.OwnerFactionId == ownerFactionId);
        s_NurseThresholds.Add(new NurseThreshold { TechId = techId, OwnerFactionId = ownerFactionId, HealthPercentThreshold = healthPercentThreshold });
    }

    public static bool IsValidHealTargetForHealer(IEntityContext healer, IEntityContext target)
    {
        if (healer == null || target == null || !string.Equals(healer.CharacterKey, UnitType.Unit_Nurse.ToString(), StringComparison.Ordinal))
            return true;

        int factionId = EntitySideHelper.ToFactionId(healer.Side);
        Fix64 threshold = Fix64.Zero;
        for (int i = 0; i < s_NurseThresholds.Count; i++)
        {
            NurseThreshold item = s_NurseThresholds[i];
            if (item.OwnerFactionId == factionId && item.HealthPercentThreshold > threshold)
                threshold = item.HealthPercentThreshold;
        }

        if (threshold <= Fix64.Zero)
            return true;

        return (Fix64)target.HealthRatio() * (Fix64)100 < threshold;
    }

    internal static void WriteDeterministicState(LogicStateHasher hasher)
    {
        s_DeterministicThresholds.Clear();
        s_DeterministicThresholds.AddRange(s_NurseThresholds);
        s_DeterministicThresholds.Sort(s_ThresholdComparison);
        hasher.Add(s_DeterministicThresholds.Count);
        for (int i = 0; i < s_DeterministicThresholds.Count; i++)
        {
            NurseThreshold item = s_DeterministicThresholds[i];
            if (item == null || string.IsNullOrWhiteSpace(item.TechId) || item.HealthPercentThreshold <= Fix64.Zero)
                throw new InvalidOperationException("Healing target filter deterministic state contains an invalid threshold.");
            hasher.Add(item.TechId);
            hasher.Add(item.OwnerFactionId);
            hasher.Add(item.HealthPercentThreshold.RawValue);
        }
    }

    private static int CompareThresholds(NurseThreshold left, NurseThreshold right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        return result != 0 ? result : left.OwnerFactionId.CompareTo(right.OwnerFactionId);
    }
}

public static class BuildingCostModifierService
{
    private sealed class StrongholdArchetypeDiscount
    {
        public string TechId;
        public int OwnerFactionId;
        public int DiscountPerArchetype;
    }

    private static readonly Dictionary<string, List<StrongholdArchetypeDiscount>> s_DiscountsByStrongholdId = new(StringComparer.Ordinal);
    private static readonly List<string> s_DeterministicStrongholdIds = new();
    private static readonly List<StrongholdArchetypeDiscount> s_DeterministicDiscounts = new();
    private static readonly Comparison<StrongholdArchetypeDiscount> s_DiscountComparison = CompareDiscounts;

    public static void Clear()
    {
        s_DiscountsByStrongholdId.Clear();
    }

    public static void RegisterStrongholdArchetypeDiscount(
        string strongholdId,
        string techId,
        int ownerFactionId,
        int discountPerArchetype)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            throw new ArgumentException("strongholdId is required.", nameof(strongholdId));
        if (string.IsNullOrWhiteSpace(techId))
            throw new ArgumentException("techId is required.", nameof(techId));
        if (discountPerArchetype <= 0)
            return;

        if (!s_DiscountsByStrongholdId.TryGetValue(strongholdId, out var discounts))
        {
            discounts = new List<StrongholdArchetypeDiscount>();
            s_DiscountsByStrongholdId[strongholdId] = discounts;
        }

        for (int i = 0; i < discounts.Count; i++)
        {
            var existing = discounts[i];
            if (existing != null
                && string.Equals(existing.TechId, techId, StringComparison.Ordinal)
                && existing.OwnerFactionId == ownerFactionId)
            {
                existing.DiscountPerArchetype = discountPerArchetype;
                return;
            }
        }

        discounts.Add(new StrongholdArchetypeDiscount
        {
            TechId = techId,
            OwnerFactionId = ownerFactionId,
            DiscountPerArchetype = discountPerArchetype,
        });
    }

    public static int CalculateBuildingCost(
        BuildingData buildingData,
        string strongholdId,
        int ownerFactionId)
    {
        if (buildingData == null)
            return 0;

        int cost = Mathf.Max(0, buildingData.Cost);
        int discount = CalculateDiscount(strongholdId, ownerFactionId);
        return LevelTagRuntime.ModifyBuildingCost(buildingData, Mathf.Max(0, cost - discount));
    }

    private static int CalculateDiscount(string strongholdId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(strongholdId))
            return 0;
        if (!s_DiscountsByStrongholdId.TryGetValue(strongholdId, out var discounts) || discounts == null)
            return 0;

        int archetypeCount = CountDistinctArchetypes(strongholdId, ownerFactionId);
        int total = 0;
        for (int i = 0; i < discounts.Count; i++)
        {
            var discount = discounts[i];
            if (discount == null || discount.OwnerFactionId != ownerFactionId)
                continue;

            total += Mathf.Max(0, discount.DiscountPerArchetype) * archetypeCount;
        }

        return total;
    }

    private static int CountDistinctArchetypes(string strongholdId, int ownerFactionId)
    {
        var archetypes = new HashSet<Archetype>();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!(entities[i] is IBuildingLogicContext building)
                || !building.Alive
                || building.BuildingData == null
                || building.OwnerFactionId != ownerFactionId
                || !string.Equals(building.StrongholdId, strongholdId, StringComparison.Ordinal))
            {
                continue;
            }

            Archetype archetype = building.BuildingData.Arche;
            if (archetype != Archetype.None)
                archetypes.Add(archetype);
        }

        return archetypes.Count;
    }

    internal static void WriteDeterministicState(LogicStateHasher hasher)
    {
        s_DeterministicStrongholdIds.Clear();
        foreach (string strongholdId in s_DiscountsByStrongholdId.Keys)
            s_DeterministicStrongholdIds.Add(strongholdId);
        s_DeterministicStrongholdIds.Sort(StringComparer.Ordinal);
        hasher.Add(s_DeterministicStrongholdIds.Count);
        for (int strongholdIndex = 0; strongholdIndex < s_DeterministicStrongholdIds.Count; strongholdIndex++)
        {
            string strongholdId = s_DeterministicStrongholdIds[strongholdIndex];
            if (string.IsNullOrWhiteSpace(strongholdId))
                throw new InvalidOperationException("Building cost modifier deterministic state contains an invalid stronghold id.");
            hasher.Add(strongholdId);

            List<StrongholdArchetypeDiscount> source = s_DiscountsByStrongholdId[strongholdId];
            if (source == null)
                throw new InvalidOperationException($"Building cost modifier deterministic state has a null discount list for '{strongholdId}'.");
            s_DeterministicDiscounts.Clear();
            s_DeterministicDiscounts.AddRange(source);
            s_DeterministicDiscounts.Sort(s_DiscountComparison);
            hasher.Add(s_DeterministicDiscounts.Count);
            for (int discountIndex = 0; discountIndex < s_DeterministicDiscounts.Count; discountIndex++)
            {
                StrongholdArchetypeDiscount item = s_DeterministicDiscounts[discountIndex];
                if (item == null || string.IsNullOrWhiteSpace(item.TechId) || item.DiscountPerArchetype <= 0)
                    throw new InvalidOperationException($"Building cost modifier deterministic state contains an invalid discount for '{strongholdId}'.");
                hasher.Add(item.TechId);
                hasher.Add(item.OwnerFactionId);
                hasher.Add(item.DiscountPerArchetype);
            }
        }
    }

    private static int CompareDiscounts(StrongholdArchetypeDiscount left, StrongholdArchetypeDiscount right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        return result != 0 ? result : left.OwnerFactionId.CompareTo(right.OwnerFactionId);
    }
}

public static class SettlementOffsetRateService
{
    private sealed class OffsetRate
    {
        public string TechId;
        public int OwnerFactionId;
        public int Value;
    }

    private static readonly List<OffsetRate> s_Values = new();
    private static readonly List<OffsetRate> s_DeterministicValues = new();
    private static readonly Comparison<OffsetRate> s_ValueComparison = CompareValues;

    public static void Clear()
    {
        s_Values.Clear();
    }

    public static void RegisterOffsetRate(string techId, int ownerFactionId, int value)
    {
        if (string.IsNullOrWhiteSpace(techId) || value == 0)
            return;

        s_Values.RemoveAll(item => item.TechId == techId && item.OwnerFactionId == ownerFactionId);
        s_Values.Add(new OffsetRate { TechId = techId, OwnerFactionId = ownerFactionId, Value = value });
    }

    public static int GetCurrentOffsetRateDelta()
    {
        int total = 0;
        for (int i = 0; i < s_Values.Count; i++)
        {
            OffsetRate item = s_Values[i];
            if (item.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                total += item.Value;
        }

        return total;
    }

    internal static void WriteDeterministicState(LogicStateHasher hasher)
    {
        s_DeterministicValues.Clear();
        s_DeterministicValues.AddRange(s_Values);
        s_DeterministicValues.Sort(s_ValueComparison);
        hasher.Add(s_DeterministicValues.Count);
        for (int i = 0; i < s_DeterministicValues.Count; i++)
        {
            OffsetRate item = s_DeterministicValues[i];
            if (item == null || string.IsNullOrWhiteSpace(item.TechId) || item.Value == 0)
                throw new InvalidOperationException("Settlement offset deterministic state contains an invalid value.");
            hasher.Add(item.TechId);
            hasher.Add(item.OwnerFactionId);
            hasher.Add(item.Value);
        }
    }

    private static int CompareValues(OffsetRate left, OffsetRate right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        return result != 0 ? result : left.OwnerFactionId.CompareTo(right.OwnerFactionId);
    }
}
