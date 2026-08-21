/// <summary>
/// 以 invariant decimal 文本读取权威 Fix64 配置。
/// </summary>
public static class FixedConfigReader
{
#if UNITY_EDITOR
    private static readonly System.Collections.Generic.Dictionary<string, Fix64> s_EditorTestPositiveFixedConfigs =
        new System.Collections.Generic.Dictionary<string, Fix64>(System.StringComparer.Ordinal);
    private static readonly System.Collections.Generic.Dictionary<string, Fix64> s_EditorTestFixedConfigs =
        new System.Collections.Generic.Dictionary<string, Fix64>(System.StringComparer.Ordinal);
#endif

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

    public static Fix64 ReadRequiredFixedConfig(string configKey)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Fixed config key must not be null or empty.", nameof(configKey));
#if UNITY_EDITOR
        if (s_EditorTestFixedConfigs.TryGetValue(configKey, out Fix64 testValue))
            return testValue;
#endif
        if (GF.Config == null)
            throw new System.InvalidOperationException($"Fixed config '{configKey}' cannot be read before GF.Config is initialized.");

        return ParseFixedConfigText(configKey, GF.Config.GetString(configKey));
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

    public static bool TryGetEditorTestFixedConfig(string configKey, out Fix64 value)
    {
        return s_EditorTestFixedConfigs.TryGetValue(configKey, out value);
    }

    public static void SetEditorTestFixedConfig(string configKey, Fix64 value)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Editor test fixed config key must not be null or empty.", nameof(configKey));

        s_EditorTestFixedConfigs[configKey] = value;
    }

    public static void ClearEditorTestFixedConfig(string configKey)
    {
        if (string.IsNullOrEmpty(configKey))
            throw new System.ArgumentException("Editor test fixed config key must not be null or empty.", nameof(configKey));

        s_EditorTestFixedConfigs.Remove(configKey);
    }
#endif
}
