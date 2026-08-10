using System;
using System.Collections.Generic;

public static class StoryTableQuery
{
    public static StoryScriptTable[] GetScriptRows(
        IEnumerable<StoryScriptTable> source,
        string scriptId)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Story script id is empty.", nameof(scriptId));

        var rows = new List<StoryScriptTable>();
        foreach (StoryScriptTable row in source)
        {
            if (string.Equals(row.ScriptID, scriptId, StringComparison.Ordinal))
                rows.Add(row);
        }

        if (rows.Count == 0)
            throw new InvalidOperationException($"Story script '{scriptId}' has no screens.");

        rows.Sort((left, right) => left.Order.CompareTo(right.Order));
        for (int i = 0; i < rows.Count; i++)
        {
            StoryScriptTable row = rows[i];
            if (row.Order <= 0)
                throw new InvalidOperationException($"Story screen {row.Id} has invalid order {row.Order}.");
            if (i > 0 && rows[i - 1].Order == row.Order)
                throw new InvalidOperationException($"Story script '{scriptId}' has duplicate order {row.Order}.");
            if (string.IsNullOrWhiteSpace(row.TextKey))
                throw new InvalidOperationException($"Story screen {row.Id} has an empty text key.");
        }

        return rows.ToArray();
    }

    public static StoryTriggerTable FindTrigger(
        IEnumerable<StoryTriggerTable> source,
        string levelIdentifier,
        StoryTiming timing)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Story level identifier is empty.", nameof(levelIdentifier));

        StoryTriggerTable match = null;
        foreach (StoryTriggerTable row in source)
        {
            if (!string.Equals(row.LevelIdentifier, levelIdentifier, StringComparison.Ordinal)
                || row.Timing != timing)
                continue;
            if (match != null)
                throw new InvalidOperationException(
                    $"Multiple story triggers match level '{levelIdentifier}' and timing '{timing}'.");
            match = row;
        }

        return match;
    }

    public static StoryCommTable[] GetCommunicationRows(
        IEnumerable<StoryCommTable> source,
        string levelIdentifier)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(levelIdentifier))
            throw new ArgumentException("Story communication level identifier is empty.", nameof(levelIdentifier));

        StoryCommTable primary = null;
        StoryCommTable secondary = null;
        foreach (StoryCommTable row in source)
        {
            if (!string.Equals(row.LevelIdentifier, levelIdentifier, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(row.SpeakerKey) || string.IsNullOrWhiteSpace(row.TextKey))
                throw new InvalidOperationException($"Story communication row {row.Id} has an empty localization key.");

            switch (row.Slot)
            {
                case StoryCommSlot.Primary:
                    if (primary != null)
                        throw new InvalidOperationException($"Level '{levelIdentifier}' has multiple primary communications.");
                    primary = row;
                    break;
                case StoryCommSlot.Secondary:
                    if (secondary != null)
                        throw new InvalidOperationException($"Level '{levelIdentifier}' has multiple secondary communications.");
                    secondary = row;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(row.Slot), row.Slot, "Unsupported story communication slot.");
            }
        }

        if (primary == null || secondary == null)
            throw new InvalidOperationException($"Level '{levelIdentifier}' requires one primary and one secondary communication.");
        return new[] { primary, secondary };
    }
}
