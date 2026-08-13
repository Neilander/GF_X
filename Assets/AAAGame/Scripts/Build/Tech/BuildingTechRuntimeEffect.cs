using System;
using System.Collections.Generic;
using AAAGame.Card;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class BuildingTechRuntimeEffect : ITechEffectRuntime
{
    private sealed class RuntimeTechRule
    {
        public Action<BuildingTechRuntimeEffect, TechEffectContext> Activate;
        public bool RegisterUnitBuffsAfterActivate;
        public Func<TechData, string, List<BuffCallback>> CreateUnitModules;
        public Func<TechData, string, List<BuffCallback>> CreateBuildingModules;
        public string SkipReason;
    }

    private static readonly Dictionary<string, RuntimeTechRule> s_Rules = CreateRules();

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
        m_SortingCenterCardForceSpecs.Clear();
        m_FireHqDeathSupplySpecs.Clear();
        m_BattleCardCountsByFaction.Clear();
        m_DeadSupplyByFaction.Clear();
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x425452554E54494DUL);
        hasher.Add(m_EventsSubscribed);
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
        BuildingCostModifierService.WriteDeterministicState(hasher);
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

    public void Activate(TechEffectContext context)
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

    public BuffData CreateUnitInitialBuff(TechData techData, UnitType unitType, string techId)
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

    public List<BuffCallback> CreateBuildingScopedModules(TechData techData, string techId)
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

        AddActivation(rules, "Tech_Buil_ResearchCenter_Lv2_Opt2",
            (self, context) => self.RegisterStrongholdCostDiscount(context, (int)GetValue(context.TechData, 0)));
        AddActivation(rules, "Tech_Buil_ResearchCenter_Lv3_Opt2",
            (self, context) => self.ApplyArmyForceInStrongholdsWithArchetype(context, Archetype.Coding, GetValue(context.TechData, 0)));

        AddUnit(rules, "Tech_Buil_DreamPark_Lv2_Opt2",
            (techData, _) => Modules(new ConditionalCoinAttackSpeedBuff((int)GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_DreamPark_Lv3_Opt2",
            (techData, _) => Modules(new OutgoingAttackDebuffBuff(-GetValue(techData, 1), GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_SortingCenter_Lv2_Opt2",
            (techData, _) => Modules(new TimedBuffOnSpawnModule(GetValue(techData, 0), () => Modules(
                new MainPropertyPercentBuff(CreatureMainProperty.Speed, GetValue(techData, 1)),
                new MainPropertyAdditiveBuff(CreatureMainProperty.Def, GetValue(techData, 2))))));
        AddActivation(rules, "Tech_Buil_SortingCenter_Lv3_Opt2",
            (self, context) => self.RegisterBattleCardForceBonus(context, (int)GetValue(context.TechData, 0), GetValue(context.TechData, 1)));

        AddActivation(rules, "Tech_Buil_FarmBase_Lv2_Opt2",
            (self, context) => self.AddMaxSupply(context, GetValue(context.TechData, 0)), registerUnitBuffsAfterActivate: true);
        AddUnit(rules, "Tech_Buil_FarmBase_Lv2_Opt2",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_FarmBase_Lv3_Opt2",
            (techData, _) => Modules(new ConditionalLowHpDamageBonusBuff(GetValue(techData, 0), GetValue(techData, 1))));

        AddActivation(rules, "Tech_Buil_FireHQ_Lv2_Opt2",
            (self, context) => self.RegisterBattleDeathSupplyCoin(context, (int)GetValue(context.TechData, 0)));
        AddUnit(rules, "Tech_Buil_FireHQ_Lv3_Opt2",
            (techData, _) => Modules(new ExtraHitFlatDamageBuff((int)GetValue(techData, 0), GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_SecurityOffice_Lv2_Opt2",
            (techData, _) => Modules(new MeleeVsRangedDamageBonusBuff(GetValue(techData, 0))));
        AddUnit(rules, "Tech_Buil_SecurityOffice_Lv3_Opt2",
            (techData, _) => Modules(new ConsecutiveSameTargetBonusDamageBuff((int)GetValue(techData, 0), GetValue(techData, 1))));

        AddUnit(rules, "Tech_Buil_WildernessCamp_Lv2_Opt2",
            (techData, _) => Modules(new StationaryAttackPercentBuff(GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_WildernessCamp_Lv3_Opt2",
            (techData, _) => Modules(new IdleNextAttackCriticalBuff(GetValue(techData, 0))));

        AddActivation(rules, "Tech_Buil_GreenhouseGarden_Lv3_Opt2",
            (self, context) => self.RegisterArmyBuffInStrongholdsWithDifferentArmyArchetypes(context));
        AddUnit(rules, "Tech_Buil_GreenhouseGarden_Lv2_Opt2",
            (techData, _) => Modules(new LightMeleeReflectDamageBuff(GetValue(techData, 0))));
        AddBuilding(rules, "Tech_Buil_GreenhouseGarden_Lv3_Opt2",
            (techData, _) => Modules(new MainPropertyAdditiveBuff(CreatureMainProperty.Health, GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_TopHospital_Lv2_Opt2",
            (techData, _) => Modules(new NearbyMedicalDelayedDamageBuff(GetValue(techData, 0), GetValue(techData, 1), GetValue(techData, 2))));
        AddUnit(rules, "Tech_Buil_TopHospital_Lv3_Opt2",
            (techData, _) => Modules(new FatalDamageProtectionBuff(GetValue(techData, 0))));

        AddUnit(rules, "Tech_Buil_SwallowNest_Lv2_Opt2",
            (techData, _) => Modules(new MissingHealthAttackSpeedBuff(GetValue(techData, 0), GetValue(techData, 1))));
        AddUnit(rules, "Tech_Buil_SwallowNest_Lv3_Opt2",
            (techData, _) => Modules(new TimedBuffOnSpawnModule(GetValue(techData, 0), () => Modules(
                new FlatAttackBonusBuff(GetValue(techData, 1)),
                new AttackSpeedBonusBuff(GetValue(techData, 2)),
                new HealthDrainOverTimeBuff(GetValue(techData, 3))))));

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
        Action<BuildingTechRuntimeEffect, TechEffectContext> action,
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

        int supplyDelta = Mathf.Max(0, (int)amount);
        if (supplyDelta > 0
            && !InGameDataModel.TryModifyValue(IngameValueType.MaxSupply, supplyDelta, true))
        {
            throw new InvalidOperationException(
                $"Tech max-supply effect could not be committed. tech={context.TechData.Identifier}, delta={supplyDelta}.");
        }
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

        GamePhase phase = LogicPhaseCommandService.GetRequiredCurrentPhase();
        if (phase != GamePhase.Invade && phase != GamePhase.Defend)
            return;

        int victimSupply = victim.CharacterData != null ? Math.Max(0, victim.CharacterData.Supply) : 0;
        m_DeadSupplyByFaction.TryGetValue(ownerFactionId, out int current);
        m_DeadSupplyByFaction[ownerFactionId] = checked(current + victimSupply);
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        if (!LogicPhaseCommandService.IsApplyingFrame)
            throw new InvalidOperationException("Building tech received a phase transition outside the logic phase apply window.");

        if (newPhase == GamePhase.Invade || newPhase == GamePhase.Defend)
            ResetBattleCardCounts();

        if (!InGameDataModel.IsBuildPhase(newPhase))
            return;

        GrantFireHqDeathSupplyCoins();
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
                if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, coin, true))
                {
                    throw new InvalidOperationException(
                        $"Fire HQ death coin reward could not be committed. faction={spec.OwnerFactionId}, amount={coin}.");
                }
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
        if (string.IsNullOrWhiteSpace(characterKey))
            return null;
        return LogicRuntimeDataTableCache.TryGetCharacter(characterKey, out CharacterDataDetail row) ? row : null;
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
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(410);
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
        if (m_Timer < UpdateInterval)
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
        if (!BuildingTechRuntimeEffect.HasTag(attacker.CharacterData.UnitTags, UnitTag.Melee))
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




public sealed class NearbyMedicalDelayedDamageBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private static readonly Fix64 UpdateInterval = Fix64.FromRaw(615);
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
        if (m_Timer < UpdateInterval)
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
        if (attacker == null || !attacker.LogicEntityId.IsValid)
            throw new InvalidOperationException("Delayed incoming damage requires an attacker with a valid logic entity id.");

        m_DeferredDamages.Add(new DeferredDamage(attacker, delayed, m_Duration));
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
                try
                {
                    DamageHelper.DoDirectDamage(hostEntity, tick, HealthModifyType.reduce, item.Attacker);
                }
                finally
                {
                    m_ApplyingDeferred = false;
                }
            }
        }
    }

    private struct DeferredDamage
    {
        public IEntityContext Attacker;
        public LogicEntityId AttackerId;
        public Fix64 TotalDamage;
        public Fix64 RemainingDamage;
        public Fix64 TotalDuration;
        public Fix64 RemainingDuration;

        public DeferredDamage(IEntityContext attacker, Fix64 damage, Fix64 duration)
        {
            Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
            AttackerId = attacker.LogicEntityId;
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
            hasher.Add(item.AttackerId.Value);
            hasher.Add(item.TotalDamage.RawValue);
            hasher.Add(item.RemainingDamage.RawValue);
            hasher.Add(item.TotalDuration.RawValue);
            hasher.Add(item.RemainingDuration.RawValue);
        }
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
        int careerDiscount = CareerRuntimeEffects.GetCoreUpgradeCostDiscount(buildingData, ownerFactionId);
        return LevelTagRuntime.ModifyBuildingCost(
            buildingData,
            Mathf.Max(0, cost - discount - careerDiscount));
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
