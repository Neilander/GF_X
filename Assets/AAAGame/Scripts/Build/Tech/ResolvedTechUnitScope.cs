using System.Collections.Generic;

/// <summary>
/// 科技范围解析结果。
/// 内部主键统一使用 CharacterKey，同时保留 UnitType 视图，便于运行时筛选和调试。
/// </summary>
public sealed class ResolvedTechUnitScope
{
    public TechScopeType ScopeType { get; }
    public HashSet<string> CharacterKeys { get; } = new();
    public HashSet<UnitType> UnitTypes { get; } = new();

    public ResolvedTechUnitScope(TechScopeType scopeType)
    {
        ScopeType = scopeType;
    }

    public bool IsEmpty => CharacterKeys.Count == 0 && UnitTypes.Count == 0;
}
