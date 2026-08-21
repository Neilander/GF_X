using UnityGameFramework.Runtime;

/// <summary>
/// 战斗阵营判定：统一走 faction/team 链路，Side 仅作为 faction(0/1) 的映射入口。
/// </summary>
public static class EntityCombatTeamHelper
{
    private const int UnknownTeamId = int.MinValue;

    public static bool IsEnemy(IEntityContext self, IEntityContext other)
    {
        if (self == null || other == null)
            return false;

        int selfTeam = ResolveTeamId(self);
        int otherTeam = ResolveTeamId(other);

        if (selfTeam == UnknownTeamId || otherTeam == UnknownTeamId)
            return false;

        return selfTeam != otherTeam;
    }

    public static bool IsEnemyUnit(IEntityContext self, IEntityContext other)
    {
        return IsEnemy(self, other) && !other.IsBuildingEntity;
    }

    public static bool IsAlly(IEntityContext self, IEntityContext other)
    {
        if (self == null || other == null)
            return false;

        int selfTeam = ResolveTeamId(self);
        int otherTeam = ResolveTeamId(other);

        if (selfTeam == UnknownTeamId || otherTeam == UnknownTeamId)
            return false;

        return selfTeam == otherTeam;
    }

    public static int ResolveTeamId(IEntityContext entity)
    {
        if (entity == null)
            return UnknownTeamId;

        if (entity.TryGetLogicBuilding(out IBuildingLogicContext building))
            return ResolveTeamIdByFaction(building.OwnerFactionId);

        return ResolveTeamIdByFaction(EntitySideHelper.ToFactionId(entity.Side));
    }

    public static int ResolveTeamIdByFaction(int factionId)
    {
        if (factionId < 0)
            return UnknownTeamId;
        return InGameDataModel.GetTeamIdByFactionRequired(factionId);
    }
}
