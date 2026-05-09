public static class LevelEntityFactory
{
    public static void ShowLevel(string prefabPath)
    {
        EntityParams levelParams = EntityParams.Create();
        GF.Entity.ShowEntity<LevelEntity>(prefabPath, Const.EntityGroup.Level, levelParams);
    }
}
