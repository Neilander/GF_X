public class LevelData
{
    public string Identifier { get; private set; }
    public int InitResource { get; private set; }
    public string NameKey { get; private set; }
    public string DescKey { get; private set; }
    public VictoryConditionType[] VictoryConditions { get; private set; }
    public int VictoryValue { get; private set; }
    public FailConditionType[] LoseConditions { get; private set; }
    public int LoseValue { get; private set; }

    public static LevelData FromRow(LevelTable row)
    {
        return new LevelData
        {
            Identifier = row.Identifier,
            InitResource = row.InitResource,
            NameKey = row.NameKey,
            DescKey = row.DescKey,
            VictoryConditions = row.VictoryConditions,
            VictoryValue = row.VictoryValue,

            LoseConditions = row.LoseConditions,
            LoseValue = row.LoseValue
        };
    }
}