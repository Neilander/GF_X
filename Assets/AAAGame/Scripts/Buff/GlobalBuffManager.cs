using System;
using System.Collections.Generic;
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
    [SerializeField] private List<TechEffectBinding> techEffectBindings = new();

    private sealed class GlobalUnitBuffEntry
    {
        public string TechId;
        public TechEffectSO Effect;
        public TechData TechData;
    }

    private TechScopeIndex m_TechScopeIndex;
    private TechScopeResolver m_TechScopeResolver;
    private bool m_IsSubscribed;
    private readonly Dictionary<int, Dictionary<UnitType, List<GlobalUnitBuffEntry>>> m_UnitBuffsByFaction = new();
    private readonly Dictionary<int, Dictionary<string, List<GlobalUnitBuffEntry>>> m_BuildingScopedBuffs = new();
    // 第三个桶：按 BuildingInstanceId 存建筑额外属性（独立于 BuildingEntity 生命周期，升级时同 id 共享同对象）
    private readonly Dictionary<string, BuildingExtraProps> m_BuildingExtraProps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TechEffectSO> m_TechEffectLookup = new(StringComparer.Ordinal);
    private bool m_IsTechEffectLookupDirty = true;

    [SerializeField] private TechEffectSO defaultTechEffect;

    public TechScopeResolver ScopeResolver => m_TechScopeResolver;
    public List<TechEffectBinding> TechEffectBindings => techEffectBindings;

    protected void Start()
    {
        TryInitializeScopeResolver();
        TrySubscribeTechUnlockedEvent();
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

    private void OnValidate()
    {
        m_IsTechEffectLookupDirty = true;
    }

    private void OnTechUnlocked(object sender, GameEventArgs e)
    {
        if (!TryInitializeScopeResolver())
            return;

        var args = (TechUnlockedEventArgs)e;
        var techData = TechDataModel.GetTechData(args.TechId);
        if (techData == null)
        {
            Debug.LogWarning($"[GlobalBuffManager] 找不到 TechData, techId={args.TechId}");
            return;
        }

        var resolvedScope = m_TechScopeResolver.Resolve(techData, args.SourceBuildingInstanceId);
        var effect = ResolveEffect(techData);
        if (effect == null)
        {
            Debug.LogWarning($"[GlobalBuffManager] 找不到可用的 TechEffect, techId={args.TechId}");
            return;
        }

        effect.Activate(new TechEffectContext
        {
            TechId = args.TechId,
            OwnerFactionId = args.OwnerFactionId,
            SourceBuildingInstanceId = args.SourceBuildingInstanceId,
            TechData = techData,
            ResolvedScope = resolvedScope,
            GlobalBuffManager = this,
        });

        DebugLog(
            $"[GlobalBuffManager] 科技解锁: techId={args.TechId}, " +
            $"ownerFactionId={args.OwnerFactionId}, " +
            $"scopeType={techData.ScopeType}, " +
            $"characterKeys=[{string.Join(",", resolvedScope.CharacterKeys)}], " +
            $"unitTypes=[{string.Join(",", resolvedScope.UnitTypes)}]");
    }

    private bool TryInitializeScopeResolver()
    {
        if (m_TechScopeResolver != null)
            return true;

        try
        {
            m_TechScopeIndex = TechScopeIndex.CreateFromCurrentDataTables();
            m_TechScopeResolver = new TechScopeResolver(m_TechScopeIndex);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GlobalBuffManager] 初始化 TechScopeResolver 失败: {exception.Message}");
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

    public void RegisterBuildingBuff(string buildingInstanceId, int ownerFactionId, string techId, TechEffectSO effect, TechData techData)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId) || string.IsNullOrWhiteSpace(techId) || effect == null || techData == null)
            return;

        if (!m_BuildingScopedBuffs.TryGetValue(ownerFactionId, out var buffsByBuilding))
        {
            buffsByBuilding = new Dictionary<string, List<GlobalUnitBuffEntry>>(StringComparer.Ordinal);
            m_BuildingScopedBuffs[ownerFactionId] = buffsByBuilding;
        }

        if (!buffsByBuilding.TryGetValue(buildingInstanceId, out var entries))
        {
            entries = new List<GlobalUnitBuffEntry>();
            buffsByBuilding[buildingInstanceId] = entries;
        }

        // stackable 暂不处理；允许同 techId 重复注册。
        entries.Add(new GlobalUnitBuffEntry
        {
            TechId = techId,
            Effect = effect,
            TechData = techData,
        });

        DebugLog($"RegisterBuildingBuff: techId={techId}, ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}, totalEntriesForBuilding={entries.Count}");
    }

    public List<BuffData> GetBuffsForBuilding(string buildingInstanceId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return null;

        if (!m_BuildingScopedBuffs.TryGetValue(ownerFactionId, out var buffsByBuilding)
            || !buffsByBuilding.TryGetValue(buildingInstanceId, out var entries)
            || entries == null
            || entries.Count == 0)
        {
            DebugLog($"GetBuffsForBuilding: ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}, entries=0");
            return null;
        }

        var result = new List<BuffData>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var buffData = entry.Effect?.CreateBuildingScopedBuff(entry.TechData, entry.TechId);
            if (buffData != null)
            {
                result.Add(buffData);
                DebugLog($"GetBuffsForBuilding: created buff id={buffData.id}, techId={entry.TechId}, ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}");
            }
            else
            {
                DebugLog($"GetBuffsForBuilding: effect returned null buff, techId={entry.TechId}, ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}");
            }
        }

        DebugLog($"GetBuffsForBuilding: ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}, createdCount={result.Count}");
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 预留清理接口。当前实现不会自动调用（建筑拆除/升级不清，允许条目保留）。
    /// </summary>
    public void UnregisterBuilding(string buildingInstanceId, int ownerFactionId)
    {
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            return;

        if (m_BuildingScopedBuffs.TryGetValue(ownerFactionId, out var buffsByBuilding))
        {
            if (buffsByBuilding.Remove(buildingInstanceId))
                DebugLog($"UnregisterBuilding: ownerFactionId={ownerFactionId}, buildingInstanceId={buildingInstanceId}");
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
        if (!m_UnitBuffsByFaction.TryGetValue(ownerFactionId, out var unitBuffsByType)
            || !unitBuffsByType.TryGetValue(unitType, out var entries)
            || entries == null
            || entries.Count == 0)
        {
            DebugLog($"GetBuffs: ownerFactionId={ownerFactionId}, unitType={unitType}, entries=0");
            return null;
        }

        var result = new List<BuffData>(entries.Count);
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

    private TechEffectSO ResolveEffect(TechData techData)
    {
        RebuildTechEffectLookupIfNeeded();

        if (techData != null
            && !string.IsNullOrWhiteSpace(techData.Identifier)
            && m_TechEffectLookup.TryGetValue(techData.Identifier, out var mappedEffect)
            && mappedEffect != null)
        {
            return mappedEffect;
        }

        return defaultTechEffect;
    }

    private void RebuildTechEffectLookupIfNeeded()
    {
        if (!m_IsTechEffectLookupDirty)
            return;

        m_TechEffectLookup.Clear();
        if (techEffectBindings != null)
        {
            for (int i = 0; i < techEffectBindings.Count; i++)
            {
                var binding = techEffectBindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.TechId))
                    continue;

                m_TechEffectLookup[binding.TechId] = binding.Effect;
            }
        }

        m_IsTechEffectLookupDirty = false;
    }

    public void MarkTechEffectBindingsDirty()
    {
        m_IsTechEffectLookupDirty = true;
    }

    private void DebugLog(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log($"[GlobalBuffManager] {message}");
    }
}
