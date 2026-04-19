/// <summary>
/// 轻量属性容器：基础值(Base) + 加法叠加(Additive) + 百分比加法栈(PercentSum) + 连乘(Multiplier)。
/// 终值 = (Base + Additive) * (1 + PercentSum) * Multiplier
/// - Additive：固定数值累加（+5 武器攻击力）
/// - PercentSum：百分比加法栈（两个 +25% → 0.5 → ×1.5，顺序无关）
/// - Multiplier：连乘（两个 factor=0.8 → ×0.64，用于攻速这种 Interval 缩放语义）
/// Buff 通过 += / -= / *= / /= 直接操作对应字段。
/// 不走 ValueProperty/IPropertyModifier 体系，保持简单。
/// </summary>
public struct SimpleStat
{
    public Fix64 Base;
    public Fix64 Additive;
    public Fix64 PercentSum;
    public Fix64 Multiplier;

    public Fix64 Value => (Base + Additive) * (Fix64.One + PercentSum) * Multiplier;

    public static SimpleStat From(Fix64 baseValue)
    {
        return new SimpleStat
        {
            Base = baseValue,
            Additive = Fix64.Zero,
            PercentSum = Fix64.Zero,
            Multiplier = Fix64.One,
        };
    }
}
