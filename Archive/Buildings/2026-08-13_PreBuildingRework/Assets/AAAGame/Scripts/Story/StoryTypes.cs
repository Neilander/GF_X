public enum StoryTiming
{
    GameStart,
    BeforeLevel,
    AfterLevelWin,
    AfterLevelFail
}

public enum StoryStyle
{
    Normal,
    Document,
    Terminal
}

public enum StoryDynamicRef
{
    None,
    LastCapturedFactionVoice
}

public enum StoryCommSlot
{
    Primary,
    Secondary
}

public enum StorySignalState
{
    Normal,
    Corrupted,
    Weak,
    Fragment
}
