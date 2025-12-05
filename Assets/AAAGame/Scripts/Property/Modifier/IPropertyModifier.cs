using GameFramework;

public enum EModifierMergeType
{
    DirectAdditive, //直接加算
    FinalAdditive, //最终加算
    IrreversibleAdditive, //不可逆加算
    DirectMultiplicative, //直接乘算
    FinalMultiplicative, //最终乘算
    IrreversibleMultiplicative, //不可逆乘算
    Override, //覆盖
    IrreversibleOverride, //不可逆覆盖
    Clamp, //限制
    Vector2IntArrayAdditive, //Vector2Int数组加算
    Vector2IntArrayPreOverride, //Vector2Int数组预覆盖
    Vector2IntArrayFinalOverride, //Vector2Int数组最终覆盖
    RangeAdditive, //Vector2Int范围加算
}

public interface IPropertyModifier : IReference
{
    //Modifier的优先级，数字越小优先级越高
    int Priority { get; }
}