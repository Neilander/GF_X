using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 科技范围解析器。
/// 当前只处理 Unit / Tag / Arch / AllUnit，其余 ScopeType 先保留到后续实现。
/// </summary>
public class TechScopeResolver
{
    private readonly TechScopeIndex m_Index;
    private readonly ArchetypeUnitTypeMapper m_ArchetypeMapper;

    public TechScopeResolver(TechScopeIndex index, ArchetypeUnitTypeMapper archetypeMapper = null)
    {
        m_Index = index ?? throw new ArgumentNullException(nameof(index));
        m_ArchetypeMapper = archetypeMapper ?? new ArchetypeUnitTypeMapper();
    }

    public ResolvedTechUnitScope Resolve(TechData techData, string sourceBuildingInstanceId = null)
    {
        if (techData == null)
            throw new ArgumentNullException(nameof(techData));

        var resolved = new ResolvedTechUnitScope(techData.ScopeType);

        switch (techData.ScopeType)
        {
            case TechScopeType.SpecificUnit:
                ResolveUnitScope(techData, resolved);
                break;
            case TechScopeType.UnitTag:
                ResolveTagScope(techData, resolved);
                break;
            case TechScopeType.UnitArch:
                ResolveArchScope(techData, resolved);
                break;
            case TechScopeType.AllUnit:
                ResolveAllUnitScope(resolved);
                break;
            case TechScopeType.SelfBuil:
                ResolveSelfBuildingScope(techData, sourceBuildingInstanceId, resolved);
                break;
            default:
                Debug.LogWarning($"[TechScopeResolver] ScopeType={techData.ScopeType} 暂未实现, techId={techData.Identifier}");
                break;
        }

        return resolved;
    }

    private void ResolveSelfBuildingScope(TechData techData, string sourceBuildingInstanceId, ResolvedTechUnitScope resolved)
    {
        if (string.IsNullOrWhiteSpace(sourceBuildingInstanceId))
        {
            Debug.LogWarning($"[TechScopeResolver] SelfBuil 需要 sourceBuildingInstanceId, techId={techData.Identifier}");
            return;
        }

        resolved.BuildingInstanceIds.Add(sourceBuildingInstanceId);
    }

    private void ResolveUnitScope(TechData techData, ResolvedTechUnitScope resolved)
    {
        if (techData.UnitScope == null || techData.UnitScope.Length == 0)
            return;

        for (int i = 0; i < techData.UnitScope.Length; i++)
        {
            string unitScopeEntry = techData.UnitScope[i];
            if (string.IsNullOrWhiteSpace(unitScopeEntry))
                continue;

            if (!Enum.TryParse(unitScopeEntry, out UnitType unitType))
            {
                Debug.LogWarning($"[TechScopeResolver] 无法将 UnitScope 解析为 UnitType, techId={techData.Identifier}, value={unitScopeEntry}");
                continue;
            }

            AddUnitType(unitType, resolved);
        }
    }

    private void ResolveTagScope(TechData techData, ResolvedTechUnitScope resolved)
    {
        if (techData.TagScope == null || techData.TagScope.Length == 0)
            return;

        for (int i = 0; i < techData.TagScope.Length; i++)
        {
            var characterKeys = m_Index.GetCharacterKeysByTag(techData.TagScope[i]);
            foreach (var characterKey in characterKeys)
            {
                AddCharacterKey(characterKey, resolved);
            }
        }
    }

    private void ResolveArchScope(TechData techData, ResolvedTechUnitScope resolved)
    {
        if (techData.ArchScope == null || techData.ArchScope.Length == 0)
            return;

        for (int i = 0; i < techData.ArchScope.Length; i++)
        {
            var unitTypes = m_ArchetypeMapper.GetUnitTypes(techData.ArchScope[i]);
            foreach (var unitType in unitTypes)
            {
                AddUnitType(unitType, resolved);
            }
        }
    }

    private void ResolveAllUnitScope(ResolvedTechUnitScope resolved)
    {
        var characterKeys = m_Index.GetAllCharacterKeys();
        foreach (var characterKey in characterKeys)
        {
            AddCharacterKey(characterKey, resolved);
        }
    }

    private void AddUnitType(UnitType unitType, ResolvedTechUnitScope resolved)
    {
        resolved.UnitTypes.Add(unitType);

        if (!m_Index.TryGetCharacterKey(unitType, out var characterKey))
        {
            Debug.LogWarning($"[TechScopeResolver] UnitType={unitType} 未能映射到 CharacterKey。");
            return;
        }

        resolved.CharacterKeys.Add(characterKey);
    }

    private void AddCharacterKey(string characterKey, ResolvedTechUnitScope resolved)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
            return;

        resolved.CharacterKeys.Add(characterKey);
        if (m_Index.TryGetUnitType(characterKey, out var unitType))
            resolved.UnitTypes.Add(unitType);
    }
}
