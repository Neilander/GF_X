using System;
using System.Collections.Generic;
using System.Linq;

public static class KeepsakeConfigRuntime
{
    private static readonly Dictionary<string, KeepsakeTable> s_Rows = new(StringComparer.Ordinal);
    private static readonly List<KeepsakeTable> s_SortedRows = new();

    public static bool IsPrepared { get; private set; }
    public static IReadOnlyList<KeepsakeTable> Rows
    {
        get
        {
            EnsurePrepared();
            return s_SortedRows;
        }
    }

    public static void Prepare()
    {
        if (IsPrepared)
            return;
        if (GF.DataTable == null)
            throw new InvalidOperationException("Keepsake configuration requires GF.DataTable.");

        var table = GF.DataTable.GetDataTable<KeepsakeTable>()
                    ?? throw new InvalidOperationException("KeepsakeTable is required for keepsake configuration.");
        Build(table.GetAllDataRows(), true);
    }

    public static KeepsakeTable GetRequired(string identifier)
    {
        EnsurePrepared();
        if (string.IsNullOrWhiteSpace(identifier) || !s_Rows.TryGetValue(identifier, out KeepsakeTable row))
            throw new InvalidOperationException($"Keepsake configuration is missing. identifier={identifier ?? "<null>"}.");
        return row;
    }

    public static KeepsakeTable GetDefaultRequired()
    {
        EnsurePrepared();
        for (int i = 0; i < s_SortedRows.Count; i++)
        {
            if (s_SortedRows[i].InitialUnlocked)
                return s_SortedRows[i];
        }
        throw new InvalidOperationException("KeepsakeTable requires at least one initially unlocked row.");
    }

    public static KeepsakeTable GetActiveOrDefaultRequired()
    {
        return CareerRunSettings.HasActiveRun
            ? GetRequired(CareerRunSettings.KeepsakeIdentifier)
            : GetDefaultRequired();
    }

    public static string ResolveCharacterKey(UnitType unitType)
    {
        return unitType == UnitType.Unit_Hero
            ? GetActiveOrDefaultRequired().HeroCharacterKey
            : unitType.ToString();
    }

#if UNITY_EDITOR
    public static void PrepareForEditorTests(IEnumerable<KeepsakeTable> rows)
    {
        Build(rows, false);
    }

    public static void ResetForEditorTests()
    {
        s_Rows.Clear();
        s_SortedRows.Clear();
        IsPrepared = false;
    }
#endif

    private static void Build(IEnumerable<KeepsakeTable> rows, bool validateRuntimeReferences)
    {
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));

        s_Rows.Clear();
        s_SortedRows.Clear();
        foreach (KeepsakeTable row in rows)
        {
            if (row == null)
                throw new InvalidOperationException("KeepsakeTable contains a null row.");
            ValidateRow(row, validateRuntimeReferences);
            if (!s_Rows.TryAdd(row.Identifier, row))
                throw new InvalidOperationException($"Duplicate keepsake identifier '{row.Identifier}'.");
            s_SortedRows.Add(row);
        }
        s_SortedRows.Sort((left, right) => left.Id.CompareTo(right.Id));
        if (s_SortedRows.Count == 0)
            throw new InvalidOperationException("KeepsakeTable contains no rows.");
        if (!s_SortedRows.Any(row => row.InitialUnlocked))
            throw new InvalidOperationException("KeepsakeTable requires at least one initially unlocked row.");
        IsPrepared = true;
    }

    private static void ValidateRow(KeepsakeTable row, bool validateRuntimeReferences)
    {
        if (string.IsNullOrWhiteSpace(row.Identifier))
            throw new InvalidOperationException($"KeepsakeTable row {row.Id} has an empty identifier.");
        if (string.IsNullOrWhiteSpace(row.NameKey) || string.IsNullOrWhiteSpace(row.DescKey))
            throw new InvalidOperationException($"Keepsake '{row.Identifier}' requires localized name and description keys.");
        if (string.IsNullOrWhiteSpace(row.HeroCharacterKey))
            throw new InvalidOperationException($"Keepsake '{row.Identifier}' has an empty hero character key.");
        if (!validateRuntimeReferences)
            return;
        if (!LogicRuntimeDataTableCache.IsPrepared)
            throw new InvalidOperationException("Keepsake validation requires prepared logic data tables.");

        CharacterDataDetail character = LogicRuntimeDataTableCache.GetCharacterRequired(row.HeroCharacterKey);
        if (character.UnitTags == null || !character.UnitTags.Contains(UnitTag.Hero))
            throw new InvalidOperationException(
                $"Keepsake '{row.Identifier}' character '{row.HeroCharacterKey}' is not tagged as a hero.");

        var knownSkills = new HashSet<string>(
            LogicRuntimeDataTableCache.SkillRows.Select(skill => skill.Identifier),
            StringComparer.Ordinal);
        var seenSkills = new HashSet<string>(StringComparer.Ordinal);
        string[] initialSkills = row.InitialSkillIdentifiers ?? Array.Empty<string>();
        for (int i = 0; i < initialSkills.Length; i++)
        {
            string skill = initialSkills[i];
            if (string.IsNullOrWhiteSpace(skill))
                throw new InvalidOperationException($"Keepsake '{row.Identifier}' has an empty initial skill at index {i}.");
            if (!seenSkills.Add(skill))
                throw new InvalidOperationException($"Keepsake '{row.Identifier}' repeats initial skill '{skill}'.");
            if (!knownSkills.Contains(skill))
                throw new InvalidOperationException($"Keepsake '{row.Identifier}' references missing skill '{skill}'.");
        }
    }

    private static void EnsurePrepared()
    {
        if (!IsPrepared)
            throw new InvalidOperationException("Keepsake configuration has not been prepared.");
    }
}

public static class KeepsakeSettlementUnlockService
{
    private static readonly HashSet<string> s_PendingIdentifiers = new(StringComparer.Ordinal);

    public static bool HasActiveRun { get; private set; }

    public static void BeginRun()
    {
        s_PendingIdentifiers.Clear();
        HasActiveRun = true;
    }

    public static void RequestUnlock(string identifier)
    {
        if (!HasActiveRun)
            throw new InvalidOperationException("Keepsake unlock can only be requested during an active career run.");
        KeepsakeConfigRuntime.GetRequired(identifier);
        s_PendingIdentifiers.Add(identifier);
    }

    public static IReadOnlyList<KeepsakeTable> Commit(CareerProgressDataModel progress)
    {
        if (!HasActiveRun)
            throw new InvalidOperationException("Keepsake unlock settlement has no active career run.");
        if (progress == null)
            throw new ArgumentNullException(nameof(progress));

        var result = new List<KeepsakeTable>();
        foreach (string identifier in s_PendingIdentifiers.OrderBy(value => KeepsakeConfigRuntime.GetRequired(value).Id))
        {
            if (progress.UnlockKeepsake(identifier))
                result.Add(KeepsakeConfigRuntime.GetRequired(identifier));
        }
        DiscardPending();
        return result;
    }

    public static void DiscardPending()
    {
        s_PendingIdentifiers.Clear();
        HasActiveRun = false;
    }
}
