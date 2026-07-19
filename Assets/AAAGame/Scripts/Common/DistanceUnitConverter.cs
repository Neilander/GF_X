/// <summary>
/// 统一管理“配表距离/速度数值 -> Unity 世界单位”的换算倍率。
/// 配置键：DistanceConversionRate（GameConfig）。
/// </summary>
public static class DistanceUnitConverter
{
    public const string DistanceConversionRateKey = "DistanceConversionRate";
    public const float DefaultDistanceConversionRate = 0.05f;

    public static float DistanceConversionRate
    {
        get
        {
            if (GF.Config == null)
            {
                return DefaultDistanceConversionRate;
            }

            return GF.Config.GetFloat(DistanceConversionRateKey, DefaultDistanceConversionRate);
        }
    }

    public static Fix64 ConvertToWorld(Fix64 tableValue)
    {
        float conversionRate = DistanceConversionRate;
        if (float.IsNaN(conversionRate) || float.IsInfinity(conversionRate))
            throw new System.InvalidOperationException($"Distance conversion rate must be finite, actual={conversionRate}.");

        decimal scaledRaw = tableValue.RawValue * (decimal)conversionRate;
        decimal quantizedRaw = scaledRaw >= decimal.Zero
            ? decimal.Ceiling(scaledRaw)
            : decimal.Floor(scaledRaw);
        return Fix64.FromRaw(decimal.ToInt64(quantizedRaw));
    }

    public static float ConvertToWorldFloat(Fix64 tableValue)
    {
        return (float)ConvertToWorld(tableValue);
    }
}
