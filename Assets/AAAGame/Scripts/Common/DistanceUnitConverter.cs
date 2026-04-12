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
        return tableValue * (Fix64)DistanceConversionRate;
    }

    public static float ConvertToWorldFloat(Fix64 tableValue)
    {
        return (float)ConvertToWorld(tableValue);
    }
}