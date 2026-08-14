using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public static class DescriptionValueFormatter
{
    private static readonly Regex PlaceholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> s_FormattedDescCache = new(StringComparer.Ordinal);

    public static void ClearCache()
    {
        s_FormattedDescCache.Clear();
    }

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

        if (data.ScopeType == TechScopeType.Skill)
        {
            if (string.IsNullOrWhiteSpace(data.SkillID))
                throw new InvalidOperationException($"Skill tech has no skill id. tech={data.Identifier}");
            int currentLevel = SkillRuntimeDataModel.GetLevel(data.SkillID);
            return data.GetSkillTechFormattedDesc(currentLevel > 0 ? currentLevel : 1);
        }

        return LocalizeAndFill(data.DescKey, data.UniqueValues);
    }

    public static string GetSkillTechFormattedDesc(this TechData data, int targetSkillLevel)
    {
        if (data == null)
            return string.Empty;
        if (data.ScopeType != TechScopeType.Skill || string.IsNullOrWhiteSpace(data.SkillID))
            throw new ArgumentException("Skill tech description requires a skill tech.", nameof(data));
        if (targetSkillLevel <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSkillLevel), targetSkillLevel, "Skill level must be positive.");

        SkillData skillData = SkillDataModel.GetSkillData(data.SkillID)
                              ?? throw new InvalidOperationException($"Skill tech references missing skill data. tech={data.Identifier}, skill={data.SkillID}");
        string skillName = LocalizationTextManager.GetLocalizedText(skillData.NameKey, false);
        string skillDesc = skillData.GetFormattedDesc(targetSkillLevel);
        return $"{skillName} Lv{targetSkillLevel}\n{skillDesc}";
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

        Fix64[] uniqueValues = null;
        if (data.Lv1UniqueValues != null)
        {
            uniqueValues = new Fix64[data.Lv1UniqueValues.Length];
            for (int i = 0; i < uniqueValues.Length; i++)
            {
                Fix64 increment = (data.UpgradeIncrementUniqueValues != null && i < data.UpgradeIncrementUniqueValues.Length)
                    ? data.UpgradeIncrementUniqueValues[i]
                    : Fix64.Zero;
                uniqueValues[i] = data.Lv1UniqueValues[i] + increment * (level - 1);
            }
        }

        return LocalizeAndFill(data.DescKey, uniqueValues);
    }

    public static string LocalizeAndFill(string descKey, Fix64[] uniqueValues)
    {
        if (string.IsNullOrWhiteSpace(descKey) || GF.Localization == null)
            return string.Empty;

        string cacheKey = BuildCacheKey(descKey, uniqueValues);
        if (s_FormattedDescCache.TryGetValue(cacheKey, out string cached))
            return cached;

        string localizedText = GF.Localization.GetString(descKey);
        string formatted = LocalizationTextManager.ProcessText(Fill(localizedText, uniqueValues));
        s_FormattedDescCache[cacheKey] = formatted;
        return formatted;
    }

    private static string BuildCacheKey(string descKey, Fix64[] uniqueValues)
    {
        string language = GF.Localization != null ? GF.Localization.Language.ToString() : string.Empty;
        StringBuilder builder = new(language.Length + descKey.Length + 16);
        builder.Append(language).Append('\u001F').Append(descKey).Append('\u001F');

        if (uniqueValues != null)
        {
            for (int i = 0; i < uniqueValues.Length; i++)
            {
                builder.Append(uniqueValues[i]).Append('\u001F');
            }
        }

        return builder.ToString();
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
