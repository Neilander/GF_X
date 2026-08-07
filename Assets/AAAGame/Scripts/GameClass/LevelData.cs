public class LevelData
{
    public string Identifier { get; private set; }
    public string NameKey { get; private set; }
    public string DescKey { get; private set; }
    public int InitResource { get; private set; }
    public GamePhase StartPhase { get; private set; }
    public VictoryConditionType[] VictoryConditions { get; private set; }
    public int VictoryValue { get; private set; }
    public FailConditionType[] LoseConditions { get; private set; }
    public int LoseValue { get; private set; }

    public static LevelData FromRow(LevelTable row)
    {
        return new LevelData
        {
            Identifier = row.Identifier,
            NameKey = row.NameKey,
            DescKey = row.DescKey,
            InitResource = row.InitResource,
            StartPhase = row.StartPhase,
            VictoryConditions = row.VictoryConditions != null
                ? (VictoryConditionType[])row.VictoryConditions.Clone()
                : null,
            VictoryValue = row.VictoryValue,
            LoseConditions = row.LoseConditions != null
                ? (FailConditionType[])row.LoseConditions.Clone()
                : null,
            LoseValue = row.LoseValue
        };
    }
}
