public readonly struct LogicGameplayStateDigest
{
    public LogicGameplayStateDigest(
        ulong frameId,
        ulong economyHash,
        ulong commandsHash,
        ulong worldRulesHash,
        ulong entitiesHash,
        ulong lifecycleHash,
        ulong obstaclesHash,
        ulong damageEventsHash,
        ulong projectilesHash,
        LogicNavigationAuthorityDigest navigation,
        ulong allocatorsHash,
        ulong gameplayStateHash)
    {
        HasDetails = true;
        FrameId = frameId;
        EconomyHash = economyHash;
        CommandsHash = commandsHash;
        WorldRulesHash = worldRulesHash;
        EntitiesHash = entitiesHash;
        LifecycleHash = lifecycleHash;
        ObstaclesHash = obstaclesHash;
        DamageEventsHash = damageEventsHash;
        ProjectilesHash = projectilesHash;
        Navigation = navigation;
        AllocatorsHash = allocatorsHash;
        GameplayStateHash = gameplayStateHash;
    }

    private LogicGameplayStateDigest(ulong gameplayStateHash)
    {
        HasDetails = false;
        FrameId = 0;
        EconomyHash = 0;
        CommandsHash = 0;
        WorldRulesHash = 0;
        EntitiesHash = 0;
        LifecycleHash = 0;
        ObstaclesHash = 0;
        DamageEventsHash = 0;
        ProjectilesHash = 0;
        Navigation = default;
        AllocatorsHash = 0;
        GameplayStateHash = gameplayStateHash;
    }

    public bool HasDetails { get; }
    public ulong FrameId { get; }
    public ulong EconomyHash { get; }
    public ulong CommandsHash { get; }
    public ulong WorldRulesHash { get; }
    public ulong EntitiesHash { get; }
    public ulong LifecycleHash { get; }
    public ulong ObstaclesHash { get; }
    public ulong DamageEventsHash { get; }
    public ulong ProjectilesHash { get; }
    public LogicNavigationAuthorityDigest Navigation { get; }
    public ulong AllocatorsHash { get; }
    public ulong GameplayStateHash { get; }

    public static LogicGameplayStateDigest FromOpaqueHash(ulong gameplayStateHash)
    {
        return new LogicGameplayStateDigest(gameplayStateHash);
    }
}
