/// <summary>
/// 统一管理“配表距离/速度数值 -> Unity 世界单位”的换算倍率。
/// 配置键：DistanceConversionRate（GameConfig）。
/// </summary>
public static class DistanceUnitConverter
{
    public const string DistanceConversionRateKey = "DistanceConversionRate";
    public const float DefaultDistanceConversionRate = 0.05f;
    public const string DefaultDistanceConversionRateText = "0.05";

    private static object s_CachedDistanceConversionConfigOwner;
    private static decimal s_CachedDistanceConversionRate;
    private static bool s_HasCachedDistanceConversionRate;

#if UNITY_EDITOR
    private static readonly System.Collections.Generic.Dictionary<string, Fix64> s_EditorTestPositiveFixedConfigs =
        new System.Collections.Generic.Dictionary<string, Fix64>(System.StringComparer.Ordinal);
    private static bool s_HasEditorTestDistanceConversionRateText;
    private static string s_EditorTestDistanceConversionRateText;
    private static decimal s_EditorTestDistanceConversionRate;
#endif

    public static Fix64 DistanceConversionRateFixed
    {
        get
        {
            decimal rate = ResolveDistanceConversionRateDecimal();
            return QuantizeRawOutward(
                DistanceConversionRateKey,
                rate * (1L << Fix64.FRACTIONAL_PLACES));
        }
    }

    public static float DistanceConversionRate => (float)DistanceConversionRateFixed;

    public static Fix64 ConvertToWorld(Fix64 tableValue)
    {
        decimal scaledRaw = tableValue.RawValue * ResolveDistanceConversionRateDecimal();
        return QuantizeRawOutward(DistanceConversionRateKey, scaledRaw);
    }

    public static Fix64 ConvertFromWorld(Fix64 worldValue)
    {
        decimal scaledRaw = worldValue.RawValue / ResolveDistanceConversionRateDecimal();
        return QuantizeRawOutward(DistanceConversionRateKey, scaledRaw);
    }

    public static float ConvertToWorldFloat(Fix64 tableValue)
    {
        return (float)ConvertToWorld(tableValue);
    }

    public static Fix64 ReadRequiredPositiveFixedConfig(string configKey)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Fixed config key must not be null or empty.", nameof(configKey));
#if UNITY_EDITOR
        if (s_EditorTestPositiveFixedConfigs.TryGetValue(configKey, out Fix64 testValue))
            return testValue;
#endif
        if (GF.Config == null)
            throw new System.InvalidOperationException($"Fixed config '{configKey}' cannot be read before GF.Config is initialized.");

        Fix64 value = ParseFixedConfigText(configKey, GF.Config.GetString(configKey));
        if (value <= Fix64.Zero)
            throw new System.InvalidOperationException($"Fixed config '{configKey}' must be positive. raw={value.RawValue}.");
        return value;
    }

    public static Fix64 ParseFixedConfigText(string configKey, string configText)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Fixed config key must not be null or empty.", nameof(configKey));
        if (string.IsNullOrEmpty(configText))
            throw new System.InvalidOperationException($"Fixed config '{configKey}' must not be null or empty.");
        decimal value = ParseInvariantDecimal(configKey, configText);
        return QuantizeRawOutward(configKey, value * (1L << Fix64.FRACTIONAL_PLACES));
    }

    private static decimal ResolveDistanceConversionRateDecimal()
    {
#if UNITY_EDITOR
        if (s_HasEditorTestDistanceConversionRateText)
            return s_EditorTestDistanceConversionRate;
#endif
        if (GF.Config == null)
            throw new System.InvalidOperationException($"Fixed config '{DistanceConversionRateKey}' cannot be read before GF.Config is initialized.");
        if (s_HasCachedDistanceConversionRate && ReferenceEquals(s_CachedDistanceConversionConfigOwner, GF.Config))
            return s_CachedDistanceConversionRate;

        string configText = GF.Config.GetString(DistanceConversionRateKey);
        decimal rate = ParseInvariantDecimal(DistanceConversionRateKey, configText);
        if (rate <= decimal.Zero)
            throw new System.InvalidOperationException($"Fixed config '{DistanceConversionRateKey}' must be positive. actual='{configText}'.");

        s_CachedDistanceConversionConfigOwner = GF.Config;
        s_CachedDistanceConversionRate = rate;
        s_HasCachedDistanceConversionRate = true;
        return rate;
    }

    private static decimal ParseInvariantDecimal(string configKey, string configText)
    {
        if (!decimal.TryParse(
                configText,
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal value))
        {
            throw new System.InvalidOperationException($"Fixed config '{configKey}' is not an invariant decimal. actual='{configText}'.");
        }

        return value;
    }

    private static Fix64 QuantizeRawOutward(string configKey, decimal scaledRaw)
    {
        try
        {
            decimal quantizedRaw = scaledRaw >= decimal.Zero
                ? decimal.Ceiling(scaledRaw)
                : decimal.Floor(scaledRaw);
            return Fix64.FromRaw(decimal.ToInt64(quantizedRaw));
        }
        catch (System.OverflowException exception)
        {
            throw new System.InvalidOperationException($"Fixed conversion for '{configKey}' exceeds Fix64 raw range. scaledRaw={scaledRaw}.", exception);
        }
    }

#if UNITY_EDITOR
    public static bool TryGetEditorTestDistanceConversionRate(out Fix64 conversionRate)
    {
        return TryGetEditorTestPositiveFixedConfig(DistanceConversionRateKey, out conversionRate);
    }

    public static void SetEditorTestDistanceConversionRate(Fix64 conversionRate)
    {
        if (conversionRate <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(conversionRate), conversionRate.RawValue, "Editor test distance conversion rate must be positive.");

        SetEditorTestDistanceConversionRateText(
            ((decimal)conversionRate).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void ClearEditorTestDistanceConversionRate()
    {
        s_EditorTestDistanceConversionRateText = null;
        s_EditorTestDistanceConversionRate = decimal.Zero;
        s_HasEditorTestDistanceConversionRateText = false;
        ClearEditorTestPositiveFixedConfig(DistanceConversionRateKey);
    }

    public static bool TryGetEditorTestDistanceConversionRateText(out string configText)
    {
        configText = s_EditorTestDistanceConversionRateText;
        return s_HasEditorTestDistanceConversionRateText;
    }

    public static void SetEditorTestDistanceConversionRateText(string configText)
    {
        decimal rate = ParseInvariantDecimal(DistanceConversionRateKey, configText);
        if (rate <= decimal.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(configText), configText, "Editor test distance conversion rate must be positive.");

        s_EditorTestDistanceConversionRateText = configText;
        s_EditorTestDistanceConversionRate = rate;
        s_HasEditorTestDistanceConversionRateText = true;
        SetEditorTestPositiveFixedConfig(
            DistanceConversionRateKey,
            ParseFixedConfigText(DistanceConversionRateKey, configText));
    }

    public static bool TryGetEditorTestPositiveFixedConfig(string configKey, out Fix64 value)
    {
        return s_EditorTestPositiveFixedConfigs.TryGetValue(configKey, out value);
    }

    public static void SetEditorTestPositiveFixedConfig(string configKey, Fix64 value)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Editor test fixed config key must not be null or empty.", nameof(configKey));
        if (value <= Fix64.Zero)
            throw new System.ArgumentOutOfRangeException(nameof(value), value.RawValue, "Editor test fixed config value must be positive.");

        s_EditorTestPositiveFixedConfigs[configKey] = value;
    }

    public static void ClearEditorTestPositiveFixedConfig(string configKey)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Editor test fixed config key must not be null or empty.", nameof(configKey));

        s_EditorTestPositiveFixedConfigs.Remove(configKey);
    }
#endif
}
