public interface IPropertyAdditiveModifier<T> : IPropertyModifier
{
    T Value { get; }
}