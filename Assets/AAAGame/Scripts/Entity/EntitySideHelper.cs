public static class EntitySideHelper
{
    public static bool CanHit(int selfTeamId, int targetTeamId)
    {
        return EntityCombatTeamHelper.IsEnemyTeam(selfTeamId, targetTeamId);
    }
}
