using System.Collections.Generic;

/// <summary>
/// 种族到单位类型的集中映射。
/// 当前项目里没有现成表结构承载这层关系，先集中收口到这里，后续调整只改这一处。
/// </summary>
public class ArchetypeUnitTypeMapper
{
    private readonly Dictionary<Archetype, UnitType[]> m_UnitTypesByArchetype = new()
    {
        [Archetype.Coding] = new[] { UnitType.Unit_Intern, UnitType.Unit_Coder,UnitType.Unit_Scapegoat },
        [Archetype.Sightseeing] = new[] {  UnitType.Unit_CanMaker, UnitType.Unit_Brat },
        [Archetype.Butchery] = new[] { UnitType.Unit_BoneButcher, UnitType.Unit_ColdCarrier },
        [Archetype.Delivery] = new[] { UnitType.Unit_Courier, UnitType.Unit_LateRider },
        [Archetype.Firefighting] = new[] { UnitType.Unit_HydroGunner, UnitType.Unit_Firefighter },
    };

    public IReadOnlyCollection<UnitType> GetUnitTypes(Archetype archetype)
    {
        if (archetype == Archetype.None)
            return System.Array.Empty<UnitType>();

        return m_UnitTypesByArchetype.TryGetValue(archetype, out var unitTypes)
            ? unitTypes
            : System.Array.Empty<UnitType>();
    }
}
