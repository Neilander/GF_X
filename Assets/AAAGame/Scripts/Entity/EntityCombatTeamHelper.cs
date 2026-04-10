using UnityGameFramework.Runtime;

/// <summary>
/// 战斗阵营判定：统一使用 team（优先由 faction 解析）。
/// </summary>
public static class EntityCombatTeamHelper
{
    public const int UnknownTeamId = int.MinValue;

    public static bool IsEnemy(IEntityContext self, IEntityContext other)
    {
        if (self == null || other == null)
            return false;

        int selfTeam = ResolveTeamId(self);
        int otherTeam = ResolveTeamId(other);

        if (selfTeam != UnknownTeamId && otherTeam != UnknownTeamId)
            return selfTeam != otherTeam;

        return false;
    }

    public static bool IsAlly(IEntityContext self, IEntityContext other)
    {
        if (self == null || other == null)
            return false;

        int selfTeam = ResolveTeamId(self);
        int otherTeam = ResolveTeamId(other);

        if (selfTeam != UnknownTeamId && otherTeam != UnknownTeamId)
            return selfTeam == otherTeam;

        return false;
    }

    public static int ResolveTeamId(IEntityContext entity)
    {
        if (entity == null)
            return UnknownTeamId;

        if (entity.TeamId != UnknownTeamId)
            return entity.TeamId;

        if (entity.FactionId >= 0)
            return ResolveTeamIdByFaction(entity.FactionId);

        return UnknownTeamId;
    }

    public static int ResolveTeamIdByFaction(int factionId)
    {
        if (factionId < 0)
            return UnknownTeamId;

        var model = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
        if (model != null && model.Factions != null && model.Factions.TryGetValue(factionId, out var faction) && faction != null)
            return faction.TeamID;

        // 数据尚未加载完全时，回退用 factionId 本身作为 teamId，保持战斗可进行。
        return factionId;
    }

    public static bool IsEnemyTeam(int selfTeamId, int otherTeamId)
    {
        if (selfTeamId == UnknownTeamId || otherTeamId == UnknownTeamId)
            return false;

        return selfTeamId != otherTeamId;
    }

    public static bool IsAllyTeam(int selfTeamId, int otherTeamId)
    {
        if (selfTeamId == UnknownTeamId || otherTeamId == UnknownTeamId)
            return false;

        return selfTeamId == otherTeamId;
    }
}
