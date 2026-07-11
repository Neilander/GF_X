using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 全局 Buff 管理器：监听科技解锁事件，根据科技效果给单位施加全局 Buff。
/// 挂在场景 GameEntry 上，继承 GameFrameworkComponent。
/// </summary>
public class GlobalBuffManager : GameFrameworkComponent
{
    [SerializeField] private bool enableDebugLogs = true;
    private bool m_HasLoggedScopeResolverDataNotReady;

    private sealed class GlobalUnitBuffEntry
    {
        public string TechId;
        public TechEffectSO Effect;
        public TechData TechData;
    }

    private sealed class PersistentBuildingBuffRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public TechEffectSO Effect;
        public TechData TechData;
        public Func<BuildingEntity, bool> Matches;
        public Func<BuildingEntity, string> ResolveTechId;
    }

    private sealed class RuntimeArmyForceRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public Func<BuildingEntity, Fix64> ResolveBonus;
    }

    private sealed class PersistentBuildingEntityBuffRule
    {
        public int OwnerFactionId;
        public string SourceBuildingInstanceId;
        public string TechId;
        public Func<BuildingEntity, bool> Matches;
        public Func<BuildingEntity, List<BuffCallback>> CreateModules;
    }

    private TechScopeIndex m_TechScopeIndex;
    private TechScopeResolver m_TechScopeResolver;
    private bool m_IsSubscribed;
    private readonly Dictionary<int, Dictionary<UnitType, List<GlobalUnitBuffEntry>>> m_UnitBuffsByFaction = new();
    private readonly List<PersistentBuildingBuffRule> m_PersistentBuildingBuffRules = new();
    private readonly List<RuntimeArmyForceRule> m_RuntimeArmyForceRules = new();
    private readonly List<PersistentBuildingEntityBuffRule> m_PersistentBuildingEntityBuffRules = new();
    // 第三个桶：按 BuildingInstanceId 存建筑额外属性（独立于 BuildingEntity 生命周期，升级时同 id 共享同对象）
    private readonly Dictionary<string, BuildingExtraProps> m_BuildingExtraProps = new(StringComparer.Ordinal);
    private BuildingTechRuntimeEffectSO m_BuildingTechRuntimeEffect;

    public TechScopeResolver ScopeResolver => m_TechScopeResolver;

    protected void Start()
    {
        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
    }

    public void PrepareRuntimeDependencies()
    {
        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
    }

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
    }

    private void Update()
    {
        if (m_IsSubscribed)
            return;

        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
    }

    protected  void OnDestroy()
    {
        if (m_IsSubscribed && GF.Event != null)
            GF.Event.Unsubscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);

        m_IsSubscribed = false;
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
        m_BuildingExtraProps.Clear();
        BuildingCostModifierService.Clear();
        SettlementOffsetRateService.Clear();
        m_BuildingTechRuntimeEffect?.ClearRuntimeState();
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        if (!TryInitializeScopeResolver())
            return;

        var args = (TechUnlockedEventArgs)e;
        var techData = TechDataModel.GetTechData(args.TechId);
        if (techData == null)
        {
            if (IsSyntheticRuntimeTechId(args.TechId))
                return;

            Debug.LogWarning($"[GlobalBuffManager] 找不到 TechData, techId={args.TechId}");
            return;
        }

        if (IsProductionBuildingLevelTech(techData))
        {
            DebugLog($"忽略 Prod 建筑等级科技事件，效果由建筑生产 buff 按等级读取。techId={args.TechId}");
            return;
        }

        if (techData.ScopeType == TechScopeType.Skill)
        {
            DebugLog($"忽略技能科技事件，效果由 SkillRuntimeDataModel 处理。techId={args.TechId}");
            return;
        }

        var resolvedScope = m_TechScopeResolver.Resolve(techData, args.SourceBuildingInstanceId);
        var runtimeEffect = GetBuildingTechRuntimeEffect();
        if (runtimeEffect.CanHandle(techData))
        {
            runtimeEffect.Activate(new TechEffectContext
            {
                TechId = args.TechId,
                OwnerFactionId = args.OwnerFactionId,
                SourceBuildingInstanceId = args.SourceBuildingInstanceId,
                TechData = techData,
                ResolvedScope = resolvedScope,
                GlobalBuffManager = this,
            });

            DebugLog(
                $"[GlobalBuffManager] 建筑科技运行时规则解锁: techId={args.TechId}, " +
                $"ownerFactionId={args.OwnerFactionId}, " +
                $"scopeType={techData.ScopeType}, " +
                $"characterKeys=[{string.Join(",", resolvedScope.CharacterKeys)}], " +
                $"unitTypes=[{string.Join(",", resolvedScope.UnitTypes)}]");
            return;
        }

        Debug.LogWarning($"[GlobalBuffManager] 缺少 BuildingTechRuntimeEffectSO 运行时规则，techId={args.TechId}, scopeType={techData.ScopeType}");
    }

    private bool TryInitializeScopeResolver()
    {
        if (m_TechScopeResolver != null)
            return true;

        if (GF.DataTable?.GetDataTable<CharacterDataDetail>() == null)
        {
            m_HasLoggedScopeResolverDataNotReady = true;
            return false;
        }

        try
        {
            m_TechScopeIndex = TechScopeIndex.CreateFromCurrentDataTables();
            m_TechScopeResolver = new TechScopeResolver(m_TechScopeIndex);
            m_HasLoggedScopeResolverDataNotReady = false;
            return true;
        }
        catch (Exception exception)
        {
            if (!m_HasLoggedScopeResolverDataNotReady)
            {
                Debug.LogWarning($"[GlobalBuffManager] 初始化 TechScopeResolver 失败: {exception.Message}");
                m_HasLoggedScopeResolverDataNotReady = true;
            }
            return false;
        }
    }

    private bool TrySubscribeTechUnlockedEvent()
    {
        if (m_IsSubscribed)
            return true;

        if (GF.Event == null)
            return false;

        GF.Event.Subscribe(TechUnlockedEventArgs.EventId, OnTechUnlocked);
        m_IsSubscribed = true;
        return true;
    }

    public void RegisterUnitBuff(UnitType unitType, int ownerFactionId, string techId, TechEffectSO effect, TechData techData)
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

        RemoveBuildingUnitProviderBuffs(ownerFactionId, buildingInstanceId, techId);

        if (removed > 0)
            DebugLog($"UnregisterTechEffects: faction={ownerFactionId}, buildingInstanceId={buildingInstanceId}, techId={techId}, removed={removed}");

        return removed;
    }

    public void RegisterPersistentBuildingBuffRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        TechEffectSO effect,
        TechData techData,
        Func<BuildingEntity, bool> matches,
        Func<BuildingEntity, string> resolveTechId = null)
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

        ApplyPersistentBuildingEntityBuffsToCurrentBuildings(ownerFactionId);
        DebugLog($"RegisterPersistentBuildingBuffRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public void RegisterRuntimeArmyForceRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        Func<BuildingEntity, Fix64> resolveBonus)
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

        NotifyArmyCardPropertiesChangedForCurrentBuildings(ownerFactionId);
        DebugLog($"RegisterRuntimeArmyForceRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public Fix64 CalculateRuntimeArmyForceBonus(BuildingEntity building)
    {
        if (building == null || building.buildingData == null)
            return Fix64.Zero;

        Fix64 total = LevelTagRuntime.CalculateArmyForceBonus(building);
        for (int i = 0; i < m_RuntimeArmyForceRules.Count; i++)
        {
            RuntimeArmyForceRule rule = m_RuntimeArmyForceRules[i];
            if (rule == null || rule.OwnerFactionId != building.OwnerFactionID || rule.ResolveBonus == null)
                continue;

            total += rule.ResolveBonus.Invoke(building);
        }

        return total;
    }

    public void RegisterPersistentBuildingEntityBuffRule(
        int ownerFactionId,
        string sourceBuildingInstanceId,
        string techId,
        Func<BuildingEntity, bool> matches,
        Func<BuildingEntity, List<BuffCallback>> createModules)
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

        ApplyPersistentBuildingEntityBuffsToCurrentBuildings(ownerFactionId);
        DebugLog($"RegisterPersistentBuildingEntityBuffRule: techId={techId}, ownerFactionId={ownerFactionId}, sourceBuildingInstanceId={sourceBuildingInstanceId}");
    }

    public List<BuffData> GetRuntimeBuffsForBuildingEntity(BuildingEntity building)
    {
        if (building == null)
            return null;

        var result = LevelTagRuntime.CreateBuildingBuffs(building) ?? new List<BuffData>();
        AddPersistentBuildingUnitProviderBuffs(result, building);
        for (int i = 0; i < m_PersistentBuildingEntityBuffRules.Count; i++)
        {
            PersistentBuildingEntityBuffRule rule = m_PersistentBuildingEntityBuffRules[i];
            if (rule == null || rule.OwnerFactionId != building.OwnerFactionID || rule.Matches == null || !rule.Matches.Invoke(building))
                continue;

            List<BuffCallback> modules = rule.CreateModules?.Invoke(building);
            if (modules == null || modules.Count == 0)
                continue;

            result.Add(BuffData.Create(
                id: $"building_runtime_tech_{rule.TechId}_{building.BuildingInstanceId}",
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: modules));
        }

        return result.Count > 0 ? result : null;
    }

    private void AddPersistentBuildingUnitProviderBuffs(List<BuffData> result, BuildingEntity building)
    {
        if (result == null || building == null || m_PersistentBuildingBuffRules.Count == 0)
            return;

        for (int i = 0; i < m_PersistentBuildingBuffRules.Count; i++)
        {
            PersistentBuildingBuffRule rule = m_PersistentBuildingBuffRules[i];
            if (rule == null || rule.OwnerFactionId != building.OwnerFactionID)
                continue;

            result.Add(BuffData.Create(
                id: GetBuildingUnitProviderBuffId(rule.SourceBuildingInstanceId, rule.TechId, building.BuildingInstanceId),
                duration: float.MaxValue,
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
        BuildingEntity sourceBuilding = FindBuildingByInstanceId(buildingInstanceId);
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
        if (m_BuildingExtraProps.Remove(buildingInstanceId))
            DebugLog($"ClearBuildingRuntimeTechState: buildingInstanceId={buildingInstanceId}");
    }

    private void RemoveBuildingUnitProviderBuffs(int ownerFactionId, string sourceBuildingInstanceId, string techId)
    {
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
            return;

        var dataModel = GF.DataModel?.GetDataModel<InGameDataModel>();
        if (dataModel?.Buildings == null)
            return;

        string prefix = GetBuildingUnitProviderBuffPrefix(sourceBuildingInstanceId);
        foreach (BuildingEntity building in dataModel.Buildings)
        {
            if (building == null || building.OwnerFactionID != ownerFactionId || building.BuffComp == null)
                continue;

            if (string.IsNullOrWhiteSpace(techId))
            {
                if (building.BuffComp is CharacterBuffComp buffComp)
                    buffComp.RemoveBuffsByPrefix(prefix);
                continue;
            }

            building.BuffComp.RemoveBuff(GetBuildingUnitProviderBuffId(sourceBuildingInstanceId, techId, building.BuildingInstanceId));
        }
    }

    private static BuildingEntity FindBuildingByInstanceId(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        var dataModel = GF.DataModel?.GetDataModel<InGameDataModel>();
        if (dataModel?.Buildings == null)
            return null;

        foreach (BuildingEntity building in dataModel.Buildings)
        {
            if (building != null && string.Equals(building.BuildingInstanceId, buildingInstanceId, StringComparison.Ordinal))
                return building;
        }

        return null;
    }

    private static void NotifyArmyCardPropertiesChangedForCurrentBuildings(int ownerFactionId)
    {
        var dataModel = GF.DataModel?.GetDataModel<InGameDataModel>();
        if (dataModel?.Buildings == null)
            return;

        foreach (BuildingEntity building in dataModel.Buildings)
        {
            if (building != null && building.OwnerFactionID == ownerFactionId && building.buildingData?.Type == BuilType.Army)
                building.RaiseArmyCardPropertyChangedEventForTech();
        }
    }

    private void ApplyPersistentBuildingEntityBuffsToCurrentBuildings(int ownerFactionId)
    {
        var dataModel = GF.DataModel?.GetDataModel<InGameDataModel>();
        if (dataModel?.Buildings == null)
            return;

        foreach (BuildingEntity building in dataModel.Buildings)
        {
            if (building == null || building.OwnerFactionID != ownerFactionId || building.BuffComp == null)
                continue;

            List<BuffData> buffs = GetRuntimeBuffsForBuildingEntity(building);
            if (buffs == null)
                continue;

            for (int i = 0; i < buffs.Count; i++)
                building.BuffComp.AddBuff(buffs[i], building);
        }
    }

    /// <summary>
    /// 取或新建建筑的 ExtraProps。BuildingInstanceId 跨 Entity 重建保持不变，
    /// 因此升级场景同 id 直接拿到同一份数据，extra 属性自然延续。
    /// </summary>
    public BuildingExtraProps GetOrCreateExtraProps(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        if (!m_BuildingExtraProps.TryGetValue(buildingInstanceId, out var props))
        {
            props = new BuildingExtraProps();
            m_BuildingExtraProps[buildingInstanceId] = props;
            DebugLog($"GetOrCreateExtraProps: new entry, buildingInstanceId={buildingInstanceId}");
        }
        return props;
    }

    /// <summary>
    /// 仅查询，不会创建新对象。用于展示型查询（如 UI 实时读数）。
    /// </summary>
    public BuildingExtraProps GetExtraProps(string buildingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        m_BuildingExtraProps.TryGetValue(buildingInstanceId, out var props);
        return props;
    }

    public List<BuffData> GetBuffs(UnitType unitType, int ownerFactionId)
    {
        var result = LevelTagRuntime.CreateUnitBuffs(unitType, ownerFactionId) ?? new List<BuffData>();
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

    private BuildingTechRuntimeEffectSO GetBuildingTechRuntimeEffect()
    {
        if (m_BuildingTechRuntimeEffect == null)
            m_BuildingTechRuntimeEffect = ScriptableObject.CreateInstance<BuildingTechRuntimeEffectSO>();

        return m_BuildingTechRuntimeEffect;
    }

    private static bool IsProductionBuildingLevelTech(TechData techData)
    {
        if (techData == null || string.IsNullOrWhiteSpace(techData.Identifier))
            return false;

        var table = GF.DataTable?.GetDataTable<BuildingTable>();
        if (table == null)
            return false;

        BuildingTable row = table.GetDataRow(r =>
            r.Type == BuilType.Prod
            && (string.Equals(r.Tech1ID, techData.Identifier, StringComparison.Ordinal)
                || string.Equals(r.Tech2ID, techData.Identifier, StringComparison.Ordinal)));

        return row != null;
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
