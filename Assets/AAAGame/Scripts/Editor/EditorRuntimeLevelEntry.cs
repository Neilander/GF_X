using System;

internal static class EditorRuntimeLevelEntry
{
    public static bool TryEnterWithDefaultCareer(string levelIdentifier, out string errorMessage)
    {
        errorMessage = null;
        try
        {
            LevelTable level = CareerConfigRuntime.GetLevelRequired(levelIdentifier);
            Archetype archetype = level.DefaultArchetype;
            CareerRunSettings.BeginRun(levelIdentifier, false, archetype);
            LevelTagRuntime.SetActiveTagIds(Array.Empty<int>());
            if (LevelSelectionService.TryEnterPreparedCareerRun(out errorMessage))
                return true;

            CareerRunSettings.CancelRun();
            return false;
        }
        catch (Exception exception)
        {
            CareerRunSettings.CancelRun();
            errorMessage = exception.Message;
            return false;
        }
    }
}
