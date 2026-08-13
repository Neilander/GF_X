public interface IPropertyOverrideModifier<T> : IPropertyModifier
{
    T Value { get; }
}
