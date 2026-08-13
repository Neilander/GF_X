public interface ILogicDeterministicStateContributor
{
    void WriteDeterministicState(LogicStateHasher hasher);
}

public static class LogicDeterministicStateWriter
{
    private static readonly System.Collections.Generic.List<int> s_SortedIds =
        new System.Collections.Generic.List<int>();

    public static void AddSortedIds(LogicStateHasher hasher, System.Collections.Generic.IEnumerable<int> values)
    {
        s_SortedIds.Clear();
        s_SortedIds.AddRange(values);
        s_SortedIds.Sort();
        hasher.Add(s_SortedIds.Count);
        for (int i = 0; i < s_SortedIds.Count; i++)
            hasher.Add(s_SortedIds[i]);
    }

    public static void AddSortedStringsById(LogicStateHasher hasher, System.Collections.Generic.IDictionary<int, string> values)
    {
        s_SortedIds.Clear();
        s_SortedIds.AddRange(values.Keys);
        s_SortedIds.Sort();
        hasher.Add(s_SortedIds.Count);
        for (int i = 0; i < s_SortedIds.Count; i++)
        {
            int key = s_SortedIds[i];
            hasher.Add(key);
            hasher.Add(values[key]);
        }
    }
}
