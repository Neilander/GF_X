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

        var resolvedScope = m_TechScopeResolver.Resolve(techData);
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
