using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 全局 Buff 管理器：监听科技解锁事件，根据科技效果给单位施加全局 Buff。
/// 挂在场景 GameEntry 上，继承 GameFrameworkComponent。
/// </summary>
public class GlobalBuffManager : GameFrameworkComponent
{
    private static GlobalBuffManager s_Current;
    [SerializeField] private bool enableDebugLogs = true;

    private sealed class GlobalUnitBuffEntry
    {
        public string TechId;
        public ITechEffectRuntime Effect;
        public TechData TechData;
    }

    private sealed class PersistentBuildingBuffRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public ITechEffectRuntime Effect;
        public TechData TechData;
        public Func<IBuildingLogicContext, bool> Matches;
        public Func<IBuildingLogicContext, string> ResolveTechId;
    }

    private sealed class RuntimeArmyForceRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public Func<IBuildingLogicContext, Fix64> ResolveBonus;
    }

    private sealed class PersistentBuildingEntityBuffRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public Func<IBuildingLogicContext, bool> Matches;
        public Func<IBuildingLogicContext, List<BuffCallback>> CreateModules;
    }

    private TechScopeIndex m_TechScopeIndex;
    private TechScopeResolver m_TechScopeResolver;
    private bool m_IsSubscribed;
    private readonly Dictionary<int, Dictionary<UnitType, List<GlobalUnitBuffEntry>>> m_UnitBuffsByFaction = new();
    private readonly List<PersistentBuildingBuffRule> m_PersistentBuildingBuffRules = new();
    private readonly List<RuntimeArmyForceRule> m_RuntimeArmyForceRules = new();
    private readonly List<PersistentBuildingEntityBuffRule> m_PersistentBuildingEntityBuffRules = new();
    private readonly List<int> m_PendingArmyCardPresentationFactions = new();
    private readonly List<int> m_DeterministicFactionIds = new();
    private readonly List<UnitType> m_DeterministicUnitTypes = new();
    private readonly List<GlobalUnitBuffEntry> m_DeterministicUnitBuffEntries = new();
    private readonly List<PersistentBuildingBuffRule> m_DeterministicBuildingBuffRules = new();
    private readonly List<RuntimeArmyForceRule> m_DeterministicArmyForceRules = new();
    private readonly List<PersistentBuildingEntityBuffRule> m_DeterministicEntityBuffRules = new();
    private static readonly Comparison<UnitType> s_UnitTypeComparison = CompareUnitTypes;
    private static readonly Comparison<int> s_IntComparison = CompareInts;
    private static readonly Comparison<GlobalUnitBuffEntry> s_GlobalUnitBuffEntryComparison = CompareGlobalUnitBuffEntries;
    private static readonly Comparison<PersistentBuildingBuffRule> s_PersistentBuildingBuffRuleComparison = ComparePersistentBuildingBuffRules;
    private static readonly Comparison<RuntimeArmyForceRule> s_RuntimeArmyForceRuleComparison = CompareRuntimeArmyForceRules;
    private static readonly Comparison<PersistentBuildingEntityBuffRule> s_PersistentBuildingEntityBuffRuleComparison = ComparePersistentBuildingEntityBuffRules;
    private BuildingTechRuntimeEffect m_BuildingTechRuntimeEffect;

    public TechScopeResolver ScopeResolver => m_TechScopeResolver;
    public static GlobalBuffManager Current => s_Current;
    internal int PendingArmyCardPresentationFactionCount => m_PendingArmyCardPresentationFactions.Count;

    public static GlobalBuffManager RequireCurrent()
    {
        return s_Current
               ?? throw new InvalidOperationException("GlobalBuffManager is required before logic runtime effects are applied.");
    }

    public static void WriteCurrentDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(0x474C4F4242554646UL);
        hasher.Add(s_Current != null);
        if (s_Current == null)
            return;

        s_Current.WriteDeterministicState(hasher);
    }

    protected void Start()
    {
        RegisterCurrent();
        SubscribeTechEffectCommands();
    }

    public void PrepareRuntimeDependencies()
    {
        RegisterCurrent();
        if (!TryInitializeScopeResolver())
            throw new InvalidOperationException("GlobalBuffManager.PrepareRuntimeDependencies failed: TechScopeResolver data tables are not ready.");
        m_BuildingTechRuntimeEffect ??= new BuildingTechRuntimeEffect();
        SubscribeTechEffectCommands();
    }

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
    }

    protected  void OnDestroy()
    {
        if (s_Current == this)
            s_Current = null;
        if (m_IsSubscribed)
        {
            LogicTechEffectCommandService.EffectApplying -= OnTechEffectApplying;
            LogicBuildingConfigurator.BuildingConfigured -= OnLogicBuildingConfigured;
        }

        m_IsSubscribed = false;
        m_BuildingTechRuntimeEffect?.ClearRuntimeState();
    }

    private void RegisterCurrent()
    {
        if (s_Current != null && s_Current != this)
            throw new InvalidOperationException("GlobalBuffManager cannot register multiple deterministic runtime owners.");

        s_Current = this;
    }

    private void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(m_IsSubscribed);
        hasher.Add(m_TechScopeResolver != null);
        hasher.Add(m_BuildingTechRuntimeEffect != null);
        if (m_BuildingTechRuntimeEffect != null)
            m_BuildingTechRuntimeEffect.WriteDeterministicState(hasher);

        m_DeterministicFactionIds.Clear();
        foreach (int factionId in m_UnitBuffsByFaction.Keys)
            m_DeterministicFactionIds.Add(factionId);
        m_DeterministicFactionIds.Sort(s_IntComparison);
        hasher.Add(m_DeterministicFactionIds.Count);
        for (int factionIndex = 0; factionIndex < m_DeterministicFactionIds.Count; factionIndex++)
        {
            int factionId = m_DeterministicFactionIds[factionIndex];
            hasher.Add(factionId);
            m_DeterministicUnitTypes.Clear();
            foreach (UnitType unitType in m_UnitBuffsByFaction[factionId].Keys)
                m_DeterministicUnitTypes.Add(unitType);
            m_DeterministicUnitTypes.Sort(s_UnitTypeComparison);
            hasher.Add(m_DeterministicUnitTypes.Count);
            for (int unitIndex = 0; unitIndex < m_DeterministicUnitTypes.Count; unitIndex++)
            {
                UnitType unitType = m_DeterministicUnitTypes[unitIndex];
                hasher.Add((int)unitType);
                List<GlobalUnitBuffEntry> entries = m_UnitBuffsByFaction[factionId][unitType];
                m_DeterministicUnitBuffEntries.Clear();
                m_DeterministicUnitBuffEntries.AddRange(entries);
                m_DeterministicUnitBuffEntries.Sort(s_GlobalUnitBuffEntryComparison);
                hasher.Add(m_DeterministicUnitBuffEntries.Count);
                for (int entryIndex = 0; entryIndex < m_DeterministicUnitBuffEntries.Count; entryIndex++)
                    AddGlobalUnitBuffEntry(hasher, m_DeterministicUnitBuffEntries[entryIndex]);
            }
        }

        m_DeterministicBuildingBuffRules.Clear();
        m_DeterministicBuildingBuffRules.AddRange(m_PersistentBuildingBuffRules);
        m_DeterministicBuildingBuffRules.Sort(s_PersistentBuildingBuffRuleComparison);
        hasher.Add(m_DeterministicBuildingBuffRules.Count);
        for (int i = 0; i < m_DeterministicBuildingBuffRules.Count; i++)
        {
            PersistentBuildingBuffRule rule = m_DeterministicBuildingBuffRules[i];
            hasher.Add(rule.OwnerFactionId);
            hasher.Add(rule.SourceBuildingInstanceId);
            hasher.Add(rule.TechId);
            hasher.Add(rule.Effect?.GetType().FullName);
        }

        m_DeterministicArmyForceRules.Clear();
        m_DeterministicArmyForceRules.AddRange(m_RuntimeArmyForceRules);
        m_DeterministicArmyForceRules.Sort(s_RuntimeArmyForceRuleComparison);
        hasher.Add(m_DeterministicArmyForceRules.Count);
        for (int i = 0; i < m_DeterministicArmyForceRules.Count; i++)
        {
            RuntimeArmyForceRule rule = m_DeterministicArmyForceRules[i];
            hasher.Add(rule.OwnerFactionId);
            hasher.Add(rule.SourceBuildingInstanceId);
            hasher.Add(rule.TechId);
            hasher.Add(rule.ResolveBonus != null);
        }

        m_DeterministicEntityBuffRules.Clear();
        m_DeterministicEntityBuffRules.AddRange(m_PersistentBuildingEntityBuffRules);
        m_DeterministicEntityBuffRules.Sort(s_PersistentBuildingEntityBuffRuleComparison);
        hasher.Add(m_DeterministicEntityBuffRules.Count);
        for (int i = 0; i < m_DeterministicEntityBuffRules.Count; i++)
        {
            PersistentBuildingEntityBuffRule rule = m_DeterministicEntityBuffRules[i];
            hasher.Add(rule.OwnerFactionId);
            hasher.Add(rule.SourceBuildingInstanceId);
            hasher.Add(rule.TechId);
            hasher.Add(rule.Matches != null);
            hasher.Add(rule.CreateModules != null);
        }
    }

    private static void AddGlobalUnitBuffEntry(LogicStateHasher hasher, GlobalUnitBuffEntry entry)
    {
        if (entry == null)
            throw new InvalidOperationException("GlobalBuffManager deterministic state contains a null unit buff entry.");
        hasher.Add(entry.TechId);
        hasher.Add(entry.Effect?.GetType().FullName);
        hasher.Add(entry.TechData?.Identifier);
    }

    private static int CompareUnitTypes(UnitType left, UnitType right)
    {
        return ((int)left).CompareTo((int)right);
    }

    private static int CompareInts(int left, int right)
    {
        return left.CompareTo(right);
    }

    private static int CompareGlobalUnitBuffEntries(GlobalUnitBuffEntry left, GlobalUnitBuffEntry right)
    {
        int result = string.CompareOrdinal(left?.TechId, right?.TechId);
        if (result != 0)
            return result;
        return string.CompareOrdinal(left?.Effect?.GetType().FullName, right?.Effect?.GetType().FullName);
    }

    private static int ComparePersistentBuildingBuffRules(PersistentBuildingBuffRule left, PersistentBuildingBuffRule right)
    {
        int result = left.OwnerFactionId.CompareTo(right.OwnerFactionId);
        if (result != 0)
            return result;
        result = string.CompareOrdinal(left.SourceBuildingInstanceId, right.SourceBuildingInstanceId);
        return result != 0 ? result : string.CompareOrdinal(left.TechId, right.TechId);
    }

    private static int CompareRuntimeArmyForceRules(RuntimeArmyForceRule left, RuntimeArmyForceRule right)
    {
        int result = left.OwnerFactionId.CompareTo(right.OwnerFactionId);
        if (result != 0)
            return result;
        result = string.CompareOrdinal(left.SourceBuildingInstanceId, right.SourceBuildingInstanceId);
        return result != 0 ? result : string.CompareOrdinal(left.TechId, right.TechId);
    }

    private static int ComparePersistentBuildingEntityBuffRules(PersistentBuildingEntityBuffRule left, PersistentBuildingEntityBuffRule right)
    {
        int result = left.OwnerFactionId.CompareTo(right.OwnerFactionId);
        if (result != 0)
            return result;
        result = string.CompareOrdinal(left.SourceBuildingInstanceId, right.SourceBuildingInstanceId);
        return result != 0 ? result : string.CompareOrdinal(left.TechId, right.TechId);
    }

    private void OnLevelLoadStarted()
    {
        ClearLevelRuntimeState();
    }

    public void ClearLevelRuntimeState()
    {
        m_UnitBuffsByFaction.Clear();
        m_PersistentBuildingBuffRules.Clear();
        m_RuntimeArmyForceRules.Clear();
        m_PersistentBuildingEntityBuffRules.Clear();
        m_PendingArmyCardPresentationFactions.Clear();
        LogicBuildingExtraPropsStore.ClearAll();
        LogicProductionConditionState.ClearAll();
        BuildingCostModifierService.Clear();
        m_BuildingTechRuntimeEffect?.ClearRuntimeState();
    }

    private void OnTechEffectApplying(LogicTechEffectCommand command)
    {
        if (!LogicTechEffectCommandService.IsApplyingFrame)
            throw new InvalidOperationException("GlobalBuffManager received a tech effect outside the logic command apply window.");
        ApplyTechEffect(command);
    }

    public void RestoreStageCheckpointTechEffects(InGameDataCheckpoint checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));
        if (!LevelSelectionService.IsLevelLoading || !LogicTimeControlService.IsPaused)
            throw new InvalidOperationException("Stage checkpoint tech effects can only be restored during a paused level load.");

        ulong sequence = 0;
        for (int i = 0; i < checkpoint.TechOwnership.Count; i++)
        {
            StageTechOwnership tech = checkpoint.TechOwnership[i];
            for (int ownerIndex = 0; ownerIndex < tech.OwnerBuildingInstanceIds.Count; ownerIndex++)
            {
                string buildingInstanceId = tech.OwnerBuildingInstanceIds[ownerIndex];
                IBuildingLogicContext owner = LogicBuildingQueryService.GetRequiredByInstanceId(buildingInstanceId);
                ApplyTechEffect(new LogicTechEffectCommand(
                    0,
                    checked(++sequence),
                    tech.TechId,
                    true,
                    owner.OwnerFactionId,
                    buildingInstanceId));
            }
        }
    }

    private void ApplyTechEffect(LogicTechEffectCommand command)
    {
        if (m_TechScopeResolver == null)
            throw new InvalidOperationException("GlobalBuffManager cannot apply a logic tech effect before runtime dependencies are prepared.");

        var techData = TechDataModel.GetTechData(command.TechId);
        if (techData == null)
        {
            if (IsSyntheticRuntimeTechId(command.TechId))
                return;

            throw new InvalidOperationException($"GlobalBuffManager cannot find TechData for '{command.TechId}'.");
        }

        BuildingLevelTechRoute levelTechRoute = BuildingLevelTechRouting.Resolve(techData);
        if (levelTechRoute == BuildingLevelTechRoute.ProductionSystem)
        {
            DebugLog($"忽略 Prod 建筑等级科技事件，效果由逻辑生产事务按等级读取。techId={command.TechId}");
            return;
        }

        if (levelTechRoute == BuildingLevelTechRoute.ArmyUnitLevelSystem)
        {
            DebugLog($"忽略 Army 建筑等级科技事件，效果由单位等级系统按建筑等级读取。techId={command.TechId}");
            return;
        }

        if (levelTechRoute == BuildingLevelTechRoute.BuildingData)
        {
            DebugLog($"忽略纯等级数据科技事件，效果已由升级后的 BuildingData 生效。techId={command.TechId}");
            return;
        }

        if (techData.ScopeType == TechScopeType.Skill)
        {
            SkillRuntimeDataModel.LearnOrUpgradeFromTech(techData);
            DebugLog($"忽略技能科技事件，效果由 SkillRuntimeDataModel 处理。techId={command.TechId}");
            return;
        }

        var resolvedScope = m_TechScopeResolver.Resolve(techData, command.SourceBuildingInstanceId);
        var runtimeEffect = GetBuildingTechRuntimeEffect();
        if (runtimeEffect.CanHandle(techData))
        {
            runtimeEffect.Activate(new TechEffectContext
            {
                TechId = command.TechId,
                OwnerFactionId = command.OwnerFactionId,
                SourceBuildingInstanceId = command.SourceBuildingInstanceId,
                TechData = techData,
                ResolvedScope = resolvedScope,
                GlobalBuffManager = this,
            });

            DebugLog(
                $"[GlobalBuffManager] 建筑科技运行时规则解锁: techId={command.TechId}, " +
                $"ownerFactionId={command.OwnerFactionId}, " +
                $"scopeType={techData.ScopeType}, " +
                $"characterKeys=[{string.Join(",", resolvedScope.CharacterKeys)}], " +
                $"unitTypes=[{string.Join(",", resolvedScope.UnitTypes)}]");
            return;
        }

        throw new InvalidOperationException(
            $"GlobalBuffManager has no runtime rule for tech '{command.TechId}', scopeType={techData.ScopeType}.");
    }

    private bool TryInitializeScopeResolver()
    {
        if (m_TechScopeResolver != null)
            return true;

        if (!LogicRuntimeDataTableCache.IsPrepared)
            return false;

        m_TechScopeIndex = TechScopeIndex.CreateFromPreparedRuntimeData();
        m_TechScopeResolver = new TechScopeResolver(m_TechScopeIndex);
        return true;
    }

    private void SubscribeTechEffectCommands()
    {
        if (m_IsSubscribed)
            return;

        LogicTechEffectCommandService.EffectApplying += OnTechEffectApplying;
        LogicBuildingConfigurator.BuildingConfigured += OnLogicBuildingConfigured;
        m_IsSubscribed = true;
    }

    public void RegisterUnitBuff(UnitType unitType, int ownerFactionId, string techId, ITechEffectRuntime effect, TechData techData)
    {
        if (string.IsNullOrWhiteSpace(techId) || effect == null || techData == null)
            return;

        if (!m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var unitBuffsByType))
        {
            unitBuffsByType = new Dictionary<UnitType, List<GlobalUnitBuffEntry>>();
            m_UnitBuffsByFaction[ownerFactionId] = unitBuffsByType;
        }

        if (!unitBuffsByType.TryGetValue(unitType, out var entries))
        {
            entries = new List<GlobalUnitBuffEntry>();
            unitBuffsByType[unitType] = entries;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].TechId, techId, StringComparison.Ordinal))
                return;
        }

        entries.Add(new GlobalUnitBuffEntry
        {
            TechId = techId,
            Effect = effect,
            TechData = techData,
        });

        DebugLog($"RegisterUnitBuff: techId={techId}, ownerFactionId={ownerFactionId}, unitType={unitType}, totalEntriesForUnit={entries.Count}");
    }

    /// <summary>
    /// 按 (unitType, faction, techId) 精确移除 per-unit 桶里的一条 entry。用于条件型开关 buff（如资金条件）。
    /// </summary>
    public bool UnregisterUnitBuff(UnitType unitType, int ownerFactionId, string techId)
    {
        if (string.IsNullOrWhiteSpace(techId)) return false;
        if (!m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var buffsByType)) return false;
        if (!buffsByType.TryGetValue(unitType, out var entries) || entries == null) return false;

        int removed = entries.RemoveAll(e => e != null && string.Equals(e.TechId, techId, StringComparison.Ordinal));
        if (removed > 0)
        {
            DebugLog($"UnregisterUnitBuff: faction={ownerFactionId}, unitType={unitType}, techId={techId}, removed={removed}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 在 ownerFactionId 桶下，按 techId 前缀批量移除所有 unitType 下的匹配 entry。
    /// 用于丢卡累加型 buff 等需要"每次挂新 id，阶段结束批量清"的场景。
    /// </summary>
    public int UnregisterUnitBuffByTechPrefix(int ownerFactionId, string techIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(techIdPrefix)) return 0;
        if (!m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var buffsByType)) return 0;

        int totalRemoved = 0;
        foreach (var kv in buffsByType)
        {
            var entries = kv.Value;
            if (entries == null) continue;
            totalRemoved += entries.RemoveAll(e => e != null && !string.IsNullOrEmpty(e.TechId) && e.TechId.StartsWith(techIdPrefix, StringComparison.Ordinal));
        }
        if (totalRemoved > 0)
            DebugLog($"UnregisterUnitBuffByTechPrefix: faction={ownerFactionId}, prefix={techIdPrefix}, removed={totalRemoved}");
        return totalRemoved;
    }

    public int UnregisterTechEffects(string techId, int ownerFactionId, string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(techId))
            return 0;

        int removed = 0;
        if (m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var buffsByType))
        {
            foreach (var pair in buffsByType)
            {
                var entries = pair.Value;
                if (entries == null)
                    continue;

                removed += entries.RemoveAll(e => e != null && string.Equals(e.TechId, techId, StringComparison.Ordinal));
            }
        }

        removed += m_PersistentBuildingBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(buildingInstanceId)
                || string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal)));

        removed += m_RuntimeArmyForceRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(buildingInstanceId)
                || string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal)));

        removed += m_PersistentBuildingEntityBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(buildingInstanceId)
                || string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal)));

        removed += BuildingCostModifierService.UnregisterTechDiscount(techId, ownerFactionId);

        RemoveBuildingUnitProviderBuffs(ownerFactionId, buildingInstanceId, techId);

        if (removed > 0)
            DebugLog($"UnregisterTechEffects: faction={ownerFactionId}, buildingInstanceId={buildingInstanceId}, techId={techId}, removed={removed}");

        return removed;
    }

    public void RegisterPersistentBuildingBuffRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        ITechEffectRuntime effect,
        TechData techData,
        Func<IBuildingLogicContext, bool> matches,
        Func<IBuildingLogicContext, string> resolveTechId = null)
    {
        if (string.IsNullOrWhiteSpace(techId) || effect == null || techData == null || matches == null)
            return;

        m_PersistentBuildingBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, sourceBuildingInstanceId, StringComparison.Ordinal)
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal));

        m_PersistentBuildingBuffRules.Add(new PersistentBuildingBuffRule
        {
            OwnerFactionId = ownerFactionId,
            SourceBuildingInstanceId = sourceBuildingInstanceId,
            TechId = techId,
            Effect = effect,
            TechData = techData,
            Matches = matches,
            ResolveTechId = resolveTechId,
        });

        ApplyPersistentBuildingUnitProviderBuffsToCurrentLogicBuildings(ownerFactionId);
        DebugLog($"RegisterPersistentBuildingBuffRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public void RegisterRuntimeArmyForceRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        Func<IBuildingLogicContext, Fix64> resolveBonus)
    {
        if (string.IsNullOrWhiteSpace(techId) || resolveBonus == null)
            return;

        m_RuntimeArmyForceRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, sourceBuildingInstanceId, StringComparison.Ordinal)
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal));

        m_RuntimeArmyForceRules.Add(new RuntimeArmyForceRule
        {
            OwnerFactionId = ownerFactionId,
            SourceBuildingInstanceId = sourceBuildingInstanceId,
            TechId = techId,
            ResolveBonus = resolveBonus,
        });

        QueueArmyCardPropertiesChangedForCurrentBuildings(ownerFactionId);
        DebugLog($"RegisterRuntimeArmyForceRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public Fix64 CalculateRuntimeArmyForceBonus(IBuildingLogicContext building)
    {
        if (building == null || building.BuildingData == null)
            return Fix64.Zero;

        Fix64 total = LevelTagRuntime.CalculateArmyForceBonus(building);
        for (int i = 0; i < m_RuntimeArmyForceRules.Count; i++)
        {
            RuntimeArmyForceRule rule = m_RuntimeArmyForceRules[i];
            if (rule == null || rule.OwnerFactionId != building.OwnerFactionId || rule.ResolveBonus == null)
                continue;

            total += rule.ResolveBonus.Invoke(building);
        }

        return total;
    }

    public void RegisterPersistentBuildingEntityBuffRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        Func<IBuildingLogicContext, bool> matches,
        Func<IBuildingLogicContext, List<BuffCallback>> createModules)
    {
        if (string.IsNullOrWhiteSpace(techId) || matches == null || createModules == null)
            return;

        m_PersistentBuildingEntityBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, sourceBuildingInstanceId, StringComparison.Ordinal)
            && string.Equals(rule.TechId, techId, StringComparison.Ordinal));

        m_PersistentBuildingEntityBuffRules.Add(new PersistentBuildingEntityBuffRule
        {
            OwnerFactionId = ownerFactionId,
            SourceBuildingInstanceId = sourceBuildingInstanceId,
            TechId = techId,
            Matches = matches,
            CreateModules = createModules,
        });

        ApplyPersistentBuildingEntityBuffsToCurrentLogicBuildings(ownerFactionId);
        DebugLog($"RegisterPersistentBuildingEntityBuffRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public List<BuffData> GetRuntimeBuffsForBuildingEntity(IBuildingLogicContext building)
    {
        if (building == null)
            return null;

        var result = LevelTagRuntime.CreateBuildingBuffs(building) ?? new List<BuffData>();
        List<BuffData> careerBuffs = CareerRuntimeEffects.CreateBuildingBuffs(building);
        if (careerBuffs != null)
            result.AddRange(careerBuffs);
        AddPersistentBuildingUnitProviderBuffs(result, building);
        return result.Count > 0 ? result : null;
    }

    private void AddPersistentBuildingUnitProviderBuffs(List<BuffData> result, IBuildingLogicContext building)
    {
        if (result == null || building == null || m_PersistentBuildingBuffRules.Count == 0)
            return;

        for (int i = 0; i < m_PersistentBuildingBuffRules.Count; i++)
        {
            PersistentBuildingBuffRule rule = m_PersistentBuildingBuffRules[i];
            if (rule == null || rule.OwnerFactionId != building.OwnerFactionId)
                continue;

            result.Add(BuffData.Create(
                id: GetBuildingUnitProviderBuffId(rule.SourceBuildingInstanceId, rule.TechId, building.BuildingInstanceId),
                duration: Fix64.Zero,
                isForever: true,
                maxStack: 1,
                modules: new List<BuffCallback>
                {
                    new SourceBuildingTechUnitBuffProvider(
                        rule.OwnerFactionId,
                        rule.TechId,
                        rule.Effect,
                        rule.TechData,
                        rule.Matches,
                        rule.ResolveTechId)
                }));
        }
    }

    private static string GetBuildingUnitProviderBuffId(string sourceBuildingInstanceId, string techId, string targetBuildingInstanceId)
    {
        return $"{GetBuildingUnitProviderBuffPrefix(sourceBuildingInstanceId)}{techId}_{targetBuildingInstanceId}";
    }

    private static string GetBuildingUnitProviderBuffPrefix(string sourceBuildingInstanceId)
    {
        return $"building_unit_provider_tech_{sourceBuildingInstanceId}_";
    }

    public List<BuffData> GetBuffsForBuilding(string buildingInstanceId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        var result = new List<BuffData>();
        IBuildingLogicContext sourceBuilding = LogicBuildingQueryService.GetRequiredByInstanceId(buildingInstanceId);
        if (sourceBuilding.OwnerFactionId != ownerFactionId)
        {
            throw new InvalidOperationException(
                $"Building buff source faction mismatch. buildingInstanceId={buildingInstanceId}, expected={ownerFactionId}, actual={sourceBuilding.OwnerFactionId}.");
        }
        List<BuffData> levelTagBuildingBuffs = LevelTagRuntime.CreateUnitBuffsFromSourceBuilding(sourceBuilding);
        if (levelTagBuildingBuffs != null && levelTagBuildingBuffs.Count > 0)
            result.AddRange(levelTagBuildingBuffs);

        DebugLog($"GetBuffsForBuilding: ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}, createdCount={result.Count}");
        return result.Count > 0 ? result : null;
    }

    public void ClearBuildingRuntimeTechState(string buildingInstanceId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        RemoveBuildingUnitProviderBuffs(ownerFactionId, buildingInstanceId, null);

        m_PersistentBuildingBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal));
        m_RuntimeArmyForceRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal));
        m_PersistentBuildingEntityBuffRules.RemoveAll(rule =>
            rule != null
            && rule.OwnerFactionId == ownerFactionId
            && string.Equals(rule.SourceBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal));
        LogicBuildingExtraPropsStore.Reset(buildingInstanceId);
        LogicBuildingProductionService.ReconfigureByBuildingInstanceId(buildingInstanceId);
        DebugLog($"ClearBuildingRuntimeTechState: buildingInstanceId={buildingInstanceId}");
    }

    private void RemoveBuildingUnitProviderBuffs(int ownerFactionId, string sourceBuildingInstanceId, string techId)
    {
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            return;

        string prefix = GetBuildingUnitProviderBuffPrefix(sourceBuildingInstanceId);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.OwnerFactionId != ownerFactionId
                || building.BuffComp == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(techId))
            {
                if (building.BuffComp is CharacterBuffComp buffComp)
                    buffComp.RemoveBuffsByPrefix(prefix);
                continue;
            }

            building.BuffComp.RemoveBuff(GetBuildingUnitProviderBuffId(sourceBuildingInstanceId, techId, building.BuildingInstanceId));
        }
    }

    private void QueueArmyCardPropertiesChangedForCurrentBuildings(int ownerFactionId)
    {
        if (!m_PendingArmyCardPresentationFactions.Contains(ownerFactionId))
            m_PendingArmyCardPresentationFactions.Add(ownerFactionId);
    }

    public void UpdatePresentation()
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("GlobalBuffManager.UpdatePresentation cannot run during a logic frame.");
        if (m_PendingArmyCardPresentationFactions.Count == 0)
            return;

        m_PendingArmyCardPresentationFactions.Sort(s_IntComparison);
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int factionIndex = 0; factionIndex < m_PendingArmyCardPresentationFactions.Count; factionIndex++)
        {
            int ownerFactionId = m_PendingArmyCardPresentationFactions[factionIndex];
            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++)
            {
                if (entities[entityIndex] is not IBuildingLogicContext building
                    || building.OwnerFactionId != ownerFactionId
                    || building.BuildingData?.Type != BuilType.Army
                    || !LogicEntityLifecycleService.TryGetBoundView(building.LogicEntityId, out MAEntity view))
                {
                    continue;
                }

                if (view is not BuildingEntity buildingView)
                {
                    throw new InvalidOperationException(
                        $"Army building {building.LogicEntityId.Value} is bound to non-building view {view.GetType().Name}.");
                }
                buildingView.RaiseArmyCardPropertyChangedEventForTech();
            }
        }

        m_PendingArmyCardPresentationFactions.Clear();
    }

    private void ApplyPersistentBuildingUnitProviderBuffsToCurrentLogicBuildings(int ownerFactionId)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || building.OwnerFactionId != ownerFactionId
                || building.BuffComp == null)
            {
                continue;
            }

            var buffs = new List<BuffData>();
            AddPersistentBuildingUnitProviderBuffs(buffs, building);
            if (buffs.Count == 0)
                continue;

            for (int buffIndex = 0; buffIndex < buffs.Count; buffIndex++)
                building.BuffComp.AddBuff(buffs[buffIndex], building);
        }
    }

    private void OnLogicBuildingConfigured(IBuildingLogicContext building)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        List<BuffData> runtimeBuffs = GetRuntimeBuffsForBuildingEntity(building);
        if (runtimeBuffs != null)
        {
            for (int i = 0; i < runtimeBuffs.Count; i++)
                building.BuffComp.AddBuff(runtimeBuffs[i], building);
        }
        ApplyPersistentBuildingEntityBuffs(building);
    }

    private void ApplyPersistentBuildingEntityBuffsToCurrentLogicBuildings(int ownerFactionId)
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is IBuildingLogicContext building
                && building.Alive
                && building.OwnerFactionId == ownerFactionId)
            {
                ApplyPersistentBuildingEntityBuffs(building);
            }
        }
    }

    private void ApplyPersistentBuildingEntityBuffs(IBuildingLogicContext building)
    {
        if (building.BuffComp == null)
        {
            throw new InvalidOperationException(
                $"Cannot apply runtime building tech buffs without BuffComp. entity={building.LogicEntityId.Value}.");
        }

        for (int i = 0; i < m_PersistentBuildingEntityBuffRules.Count; i++)
        {
            PersistentBuildingEntityBuffRule rule = m_PersistentBuildingEntityBuffRules[i];
            if (rule == null
                || rule.OwnerFactionId != building.OwnerFactionId
                || rule.Matches == null
                || !rule.Matches.Invoke(building))
            {
                continue;
            }

            List<BuffCallback> modules = rule.CreateModules?.Invoke(building);
            if (modules == null || modules.Count == 0)
                continue;

            building.BuffComp.AddBuff(
                BuffData.Create(
                    id: $"building_runtime_tech_{rule.TechId}_{building.BuildingInstanceId}",
                    duration: Fix64.Zero,
                    isForever: true,
                    maxStack: 1,
                    modules: modules),
                building);
        }
    }

    /// <summary>
    /// 取或新建建筑的 ExtraProps。BuildingInstanceId 跨 Entity 重建保持不变，
    /// 因此升级场景同 id 直接拿到同一份数据，extra 属性自然延续。
    /// </summary>
    public BuildingExtraProps GetOrCreateExtraProps(string buildingInstanceId)
    {
        return LogicBuildingExtraPropsStore.GetOrCreate(buildingInstanceId);
    }

    /// <summary>
    /// 仅查询，不会创建新对象。用于展示型查询（如 UI 实时读数）。
    /// </summary>
    public BuildingExtraProps GetExtraProps(string buildingInstanceId)
    {
        return LogicBuildingExtraPropsStore.TryGet(buildingInstanceId);
    }

    public List<BuffData> GetBuffs(UnitType unitType, int ownerFactionId)
    {
        var result = LevelTagRuntime.CreateUnitBuffs(unitType, ownerFactionId) ?? new List<BuffData>();
        List<BuffData> careerBuffs = CareerRuntimeEffects.CreateUnitBuffs(unitType, ownerFactionId);
        if (careerBuffs != null)
            result.AddRange(careerBuffs);
        if (!m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var unitBuffsByType)
            || !unitBuffsByType.TryGetValue(unitType, out var entries)
            || entries == null
            || entries.Count == 0)
        {
            return result.Count > 0 ? result : null;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var buffData = entry.Effect?.CreateUnitInitialBuff(entry.TechData, unitType, entry.TechId);
            if (buffData != null)
            {
                result.Add(buffData);
                DebugLog($"GetBuffs: created buff id={buffData.id}, techId={entry.TechId}, ownerFactionId={ownerFactionId}, unitType={unitType}");
            }
            else
            {
                DebugLog($"GetBuffs: effect returned null buff, techId={entry.TechId}, ownerFactionId={ownerFactionId}, unitType={unitType}");
            }
        }

        DebugLog($"GetBuffs: ownerFactionId={ownerFactionId}, unitType={unitType}, createdCount={result.Count}");
        return result.Count > 0 ? result : null;
    }

    private BuildingTechRuntimeEffect GetBuildingTechRuntimeEffect()
    {
        if (m_BuildingTechRuntimeEffect == null)
            throw new InvalidOperationException("Building tech runtime was not prepared before a logic tech effect was applied.");

        return m_BuildingTechRuntimeEffect;
    }

    private static bool IsSyntheticRuntimeTechId(string techId)
    {
        return !string.IsNullOrWhiteSpace(techId)
            && techId.StartsWith("Tech_BaseBuilt_", StringComparison.Ordinal);
    }

    private void DebugLog(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log($"[GlobalBuffManager] {message}");
    }
}
