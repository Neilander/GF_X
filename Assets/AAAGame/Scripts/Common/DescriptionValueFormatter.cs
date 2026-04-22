using System;
using System.Text.RegularExpressions;

public static class DescriptionValueFormatter
{
    private static readonly Regex PlaceholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

    public static string GetFormattedDesc(this BuildingData data)
    {
        if (data == null)
            return string.Empty;

        return LocalizeAndFill(data.DescKey, data.UniqueValues);
    }

    public static string GetFormattedDesc(this TechData data)
    {
        if (data == null)
            return string.Empty;

        return LocalizeAndFill(data.DescKey, data.UniqueValues);
    }

    public static string GetFormattedDesc(this CharacterDataDetail data)
    {
        if (data == null)
            return string.Empty;

        return LocalizeAndFill(data.DescKey, data.UniqueValues);
    }

    public static string GetFormattedDesc(this SkillData data, int level = 1)
    {
        if (data == null)
            return string.Empty;

        Fix64[] uniqueValues = level >= 2 && data.Lv2UniqueValues != null && data.Lv2UniqueValues.Length > 0
            ? data.Lv2UniqueValues
            : data.Lv1UniqueValues;

        return LocalizeAndFill(data.DescKey, uniqueValues);
    }

    public static string LocalizeAndFill(string descKey, Fix64[] uniqueValues)
    {
        if (string.IsNullOrWhiteSpace(descKey) || GF.Localization == null)
            return string.Empty;

        string localizedText = GF.Localization.GetString(descKey);
        return Fill(localizedText, uniqueValues);
    }

    public static string Fill(string template, Fix64[] uniqueValues)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (uniqueValues == null || uniqueValues.Length == 0)
            return template;

        return PlaceholderRegex.Replace(template, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out int index))
                return match.Value;

            if (index < 0 || index >= uniqueValues.Length)
                return match.Value;

            return uniqueValues[index].ToString();
        });
    }
}