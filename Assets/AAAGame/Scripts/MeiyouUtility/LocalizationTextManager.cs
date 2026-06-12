using System;
using System.Collections.Generic;
using System.Text;
using GameFramework.DataTable;
using UnityEngine;
using UnityGameFramework.Runtime;

public class LocalizationTextManager : GameFrameworkComponent
{
	[Serializable]
#pragma warning disable 0649
	private sealed class KeywordGroupConfig
	{
		public Color Color = Color.white;
		public string IconSpriteName;
		public List<string> Keywords = new();
	}

	[Serializable]
	private sealed class KeywordRuleConfig
	{
		public string Keyword;
		public Color Color = Color.white;
		public string IconSpriteName;
	}
#pragma warning restore 0649

	private sealed class KeywordRule
	{
		public string Keyword;
		public string RichText;
		public int Priority;
	}

	private static LocalizationTextManager s_Instance;
	private static readonly Dictionary<string, string> s_ProcessedTextCache = new(StringComparer.Ordinal);
	[SerializeField] private bool m_AutoCollectUnitKeywords = true;
	[SerializeField] private bool m_AutoCollectBuildingKeywords = true;
	private bool m_RulesDirty = true;

	[SerializeField] private string m_NoReplaceStartMarker = "[[";
	[SerializeField] private string m_NoReplaceEndMarker = "]]";
	[SerializeField] private Color m_PositiveNumberColor = Color.green;
	[SerializeField] private Color m_NegativeNumberColor = Color.red;

	[Header("行业词")]
	[SerializeField] private KeywordGroupConfig m_IndustryKeywords = new();

	[Header("单位词")]
	[SerializeField] private KeywordGroupConfig m_UnitKeywords = new();

	[Header("建筑词")]
	[SerializeField] private KeywordGroupConfig m_BuildingKeywords = new();

	[Header("特殊名词")]
	[SerializeField] private List<KeywordRuleConfig> m_SpecialKeywords = new();

	private readonly List<KeywordRule> m_CompiledRules = new();

	protected override void Awake()
	{
		base.Awake();
		s_Instance = this;
		RebuildRules();
	}

	private void OnEnable()
	{
		s_Instance = this;
		RebuildRules();
	}

	private void OnValidate()
	{
		if (!Application.isPlaying)
		{
			RebuildRules();
		}
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
		{
			s_Instance = null;
		}
	}

	public static string ProcessText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}

		LocalizationTextManager manager = GetRuntimeManager();
		if (manager != null)
		{
			manager.EnsureRulesBuilt();
		}

		if (manager == null)
		{
			return text;
		}

		string cacheKey = BuildCacheKey(text);
		if (s_ProcessedTextCache.TryGetValue(cacheKey, out string cached))
		{
			return cached;
		}

		string processed = manager.ProcessTextInternal(text);
		s_ProcessedTextCache[cacheKey] = processed;
		return processed;
	}

	public static void ClearCache()
	{
		s_ProcessedTextCache.Clear();
	}

	public static string GetLocalizedText(string key, bool applyRichText = true)
	{
		// 仅用于“直接本地化 key”（如 BuildingTable.NameKey/DescKey）。
		// 对于 LocalizationTextTable 的 identifier（如 Archetype_*），请走 LocalizationTextDataModel.GetText。
		string text = GF.Localization != null ? GF.Localization.GetString(key) : key;
		return applyRichText ? ProcessText(text) : text;
	}

	private static LocalizationTextManager GetRuntimeManager()
	{
		if (s_Instance != null)
		{
			return s_Instance;
		}

		s_Instance = FindObjectOfType<LocalizationTextManager>();
		return s_Instance;
	}

	private void RebuildRules()
	{
		m_CompiledRules.Clear();
		HashSet<string> seenKeywords = new(StringComparer.Ordinal);
		bool unitKeywordsLoaded = !m_AutoCollectUnitKeywords;
		bool buildingKeywordsLoaded = !m_AutoCollectBuildingKeywords;

		AddGroupRules(seenKeywords, m_IndustryKeywords, 1);
		if (m_AutoCollectUnitKeywords)
		{
			unitKeywordsLoaded = AddTableKeywordRules<CharacterDataDetail>(seenKeywords, m_UnitKeywords, row => row.NameKey, 2);
		}
		else
		{
			AddGroupRules(seenKeywords, m_UnitKeywords, 2);
		}

		if (m_AutoCollectBuildingKeywords)
		{
			buildingKeywordsLoaded = AddTableKeywordRules<BuildingTable>(seenKeywords, m_BuildingKeywords, row => row.NameKey, 3);
		}
		else
		{
			AddGroupRules(seenKeywords, m_BuildingKeywords, 3);
		}

		if (m_SpecialKeywords != null)
		{
			foreach (KeywordRuleConfig config in m_SpecialKeywords)
			{
				AddRule(null, config?.Keyword, config?.Color ?? Color.white, config?.IconSpriteName, 4);
			}
		}

		m_CompiledRules.Sort((left, right) =>
		{
			int lengthCompare = right.Keyword.Length.CompareTo(left.Keyword.Length);
			return lengthCompare != 0 ? lengthCompare : right.Priority.CompareTo(left.Priority);
		});
		m_RulesDirty = !(unitKeywordsLoaded && buildingKeywordsLoaded);
		ClearCache();
	}

	private void EnsureRulesBuilt()
	{
		if (m_RulesDirty)
		{
			RebuildRules();
		}
	}

	private void AddGroupRules(HashSet<string> seenKeywords, KeywordGroupConfig config, int priority)
	{
		if (config == null || config.Keywords == null)
		{
			return;
		}

		foreach (string keyword in config.Keywords)
		{
			AddRule(seenKeywords, keyword, config.Color, config.IconSpriteName, priority);
		}
	}

	private bool AddTableKeywordRules<T>(HashSet<string> seenKeywords, KeywordGroupConfig config, Func<T, string> keywordSelector, int priority) where T : DataRowBase
	{
		AddGroupRules(seenKeywords, config, priority);

		var table = GF.DataTable?.GetDataTable<T>();
		if (table == null)
		{
			return false;
		}

		foreach (var row in table.GetAllDataRows())
		{
			if (row == null)
			{
				continue;
			}

			string nameKey = keywordSelector(row);
			if (string.IsNullOrWhiteSpace(nameKey))
			{
				continue;
			}

			AddRule(seenKeywords, GetLocalizedText(nameKey, false), config.Color, config.IconSpriteName, priority);
		}

		return true;
	}

	private void AddRule(HashSet<string> seenKeywords, string keyword, Color color, string iconSpriteName, int priority)
	{
		if (string.IsNullOrWhiteSpace(keyword) || (seenKeywords != null && !seenKeywords.Add(keyword)))
		{
			return;
		}

		m_CompiledRules.Add(new KeywordRule
		{
			Keyword = keyword,
			RichText = BuildRichText(keyword, color, iconSpriteName),
			Priority = priority
		});
	}

	private string ProcessTextInternal(string text)
	{
		if (m_CompiledRules.Count == 0)
		{
			return HighlightSignedNumbers(StripNoReplaceMarkers(text));
		}

		List<string> protectedSegments = new();
		string unescapedText = ProtectSegments(text, protectedSegments);
		string processed = HighlightSignedNumbers(unescapedText);
		processed = ReplaceKeywords(processed);
		return RestoreProtectedSegments(processed, protectedSegments);
	}

	private string HighlightSignedNumbers(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}

		StringBuilder builder = new(text.Length);
		for (int index = 0; index < text.Length;)
		{
			char current = text[index];
			if ((current == '+' || current == '-') && index + 1 < text.Length && char.IsDigit(text[index + 1]))
			{
				int startIndex = index;
				index += 2;
				while (index < text.Length && char.IsDigit(text[index]))
				{
					index++;
				}

				string token = text.Substring(startIndex, index - startIndex);
				string colorHex = ColorUtility.ToHtmlStringRGBA(current == '+' ? m_PositiveNumberColor : m_NegativeNumberColor);
				builder.Append("<color=#");
				builder.Append(colorHex);
				builder.Append('>');
				builder.Append(token);
				builder.Append("</color>");
				continue;
			}

			builder.Append(current);
			index++;
		}

		return builder.ToString();
	}

	private string ReplaceKeywords(string text)
	{
		if (string.IsNullOrEmpty(text) || m_CompiledRules.Count == 0)
		{
			return text;
		}

		StringBuilder builder = new(text.Length);
		for (int index = 0; index < text.Length;)
		{
			KeywordRule matchedRule = null;
			foreach (KeywordRule rule in m_CompiledRules)
			{
				if (MatchesAt(text, index, rule.Keyword))
				{
					matchedRule = rule;
					break;
				}
			}

			if (matchedRule != null)
			{
				builder.Append(matchedRule.RichText);
				index += matchedRule.Keyword.Length;
				continue;
			}

			builder.Append(text[index]);
			index++;
		}

		return builder.ToString();
	}

	private static bool MatchesAt(string text, int index, string keyword)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
		{
			return false;
		}

		if (index < 0 || index + keyword.Length > text.Length)
		{
			return false;
		}

		return string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.Ordinal) == 0;
	}

	private string ProtectSegments(string text, List<string> protectedSegments)
	{
		if (string.IsNullOrEmpty(text)
			|| string.IsNullOrEmpty(m_NoReplaceStartMarker)
			|| string.IsNullOrEmpty(m_NoReplaceEndMarker))
		{
			return text;
		}

		StringBuilder builder = new(text.Length);
		int cursor = 0;
		while (cursor < text.Length)
		{
			int startIndex = text.IndexOf(m_NoReplaceStartMarker, cursor, StringComparison.Ordinal);
			if (startIndex < 0)
			{
				builder.Append(text, cursor, text.Length - cursor);
				break;
			}

			int contentStart = startIndex + m_NoReplaceStartMarker.Length;
			int endIndex = text.IndexOf(m_NoReplaceEndMarker, contentStart, StringComparison.Ordinal);
			if (endIndex < 0)
			{
				builder.Append(text, cursor, text.Length - cursor);
				break;
			}

			builder.Append(text, cursor, startIndex - cursor);
			string protectedContent = text.Substring(contentStart, endIndex - contentStart);
			string token = $"__LOC_PROTECTED_{protectedSegments.Count}__";
			protectedSegments.Add(protectedContent);
			builder.Append(token);
			cursor = endIndex + m_NoReplaceEndMarker.Length;
		}

		return builder.ToString();
	}

	private static string RestoreProtectedSegments(string text, IReadOnlyList<string> protectedSegments)
	{
		if (string.IsNullOrEmpty(text) || protectedSegments == null || protectedSegments.Count == 0)
		{
			return text;
		}

		StringBuilder builder = new(text);
		for (int i = 0; i < protectedSegments.Count; i++)
		{
			builder.Replace($"__LOC_PROTECTED_{i}__", protectedSegments[i]);
		}

		return builder.ToString();
	}

	private static string StripNoReplaceMarkers(string text)
	{
		LocalizationTextManager manager = GetRuntimeManager();
		if (manager == null)
		{
			return text;
		}

		List<string> protectedSegments = new();
		string protectedText = manager.ProtectSegments(text, protectedSegments);
		return RestoreProtectedSegments(protectedText, protectedSegments);
	}

	private static string BuildRichText(string keyword, Color color, string iconSpriteName)
	{
		string colorHex = ColorUtility.ToHtmlStringRGBA(color);
		string coloredKeyword = $"<color=#{colorHex}>{keyword}</color>";
		if (string.IsNullOrWhiteSpace(iconSpriteName))
		{
			return coloredKeyword;
		}

		return $"<sprite name={iconSpriteName}>{coloredKeyword}";
	}

	private static string BuildCacheKey(string text)
	{
		string language = GF.Localization != null ? GF.Localization.Language.ToString() : string.Empty;
		return string.Concat(language, "\u001F", text);
	}
}
