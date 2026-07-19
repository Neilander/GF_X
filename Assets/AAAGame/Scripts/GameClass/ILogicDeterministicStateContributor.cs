public interface ILogicDeterministicStateContributor
{
    void WriteDeterministicState(LogicStateHasher hasher);
}

public static class LogicDeterministicStateWriter
{
    public static void AddSortedIds(LogicStateHasher hasher, System.Collections.Generic.IEnumerable<int> values)
    {
        var sorted = new System.Collections.Generic.List<int>(values);
        sorted.Sort();
        hasher.Add(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
            hasher.Add(sorted[i]);
    }

    public static void AddSortedStringsById(LogicStateHasher hasher, System.Collections.Generic.IDictionary<int, string> values)
    {
        var keys = new System.Collections.Generic.List<int>(values.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            hasher.Add(keys[i]);
            hasher.Add(values[keys[i]]);
        }
    }
}
