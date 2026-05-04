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

        if (data.ScopeType == TechScopeType.Skill && !string.IsNullOrEmpty(data.SkillID))
        {
            var skillData = SkillDataModel.GetSkillData(data.SkillID);
            if (skillData != null)
            {
                bool isLearned = InGameDataModel.IsSkillUnlocked(data.SkillID);
                string skillName = GF.Localization.GetString(skillData.NameKey);
                string skillDesc = skillData.GetFormattedDesc();

                string fmtKey;
                if (skillData.Type == SkillType.Active && isLearned)
                {
                    fmtKey = "Tech.Desc.SkillAlreadyLearned";
                    string template = GF.Localization.GetString(fmtKey);
                    return LocalizationTextManager.ProcessText(template.Replace("{0}", skillName));
                }
                else
                {
                    fmtKey = (skillData.Type == SkillType.Active && !isLearned) 
                        ? "Tech.Desc.LearnActiveSkill" 
                        : "Tech.Desc.LearnPassiveSkill";
                    string template = GF.Localization.GetString(fmtKey);
                    // 提前对template和skillName格式化，不将skillDesc卷入二次ProcessText，避免“橙髓”等关键字添加重复富文本
                    string processedTemplate = LocalizationTextManager.ProcessText(template.Replace("{0}", skillName));
                    return processedTemplate.Replace("{1}", skillDesc);
                }
            }
        }

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
