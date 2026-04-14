public static class LevelEntityFactory
{
    public static EntityParams CreateLevelParams(LevelData levelData)
    {
        EntityParams levelParams = EntityParams.Create();
        levelParams.Set(LevelEntity.P_LevelData, levelData);
        return levelParams;
    }

    public static int ShowLevel(string prefabPath, LevelData levelData)
    {
        EntityParams levelParams = CreateLevelParams(levelData);
        return GF.Entity.ShowEntity<LevelEntity>(prefabPath, Const.EntityGroup.Level, levelParams);
    }
}
