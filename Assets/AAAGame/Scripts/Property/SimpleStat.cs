/// <summary>
/// 轻量属性容器：基础值(Base) + 加法叠加(Additive) + 乘法叠加(Multiplier)。
/// 终值 = (Base + Additive) * Multiplier。
/// Buff 通过 += / -= / *= / /= 直接操作 Additive 或 Multiplier。
/// 不走 ValueProperty/IPropertyModifier 体系，保持简单。
/// </summary>
public struct SimpleStat
{
    public Fix64 Base;
    public Fix64 Additive;
    public Fix64 Multiplier;

    public Fix64 Value => (Base + Additive) * Multiplier;

    public static SimpleStat From(Fix64 baseValue)
    {
        return new SimpleStat
        {
            Base = baseValue,
            Additive = Fix64.Zero,
            Multiplier = Fix64.One,
        };
    }
}
