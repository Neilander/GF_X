public interface IPropertyClampModifier<T> : IPropertyModifier
{
    T Min { get; }
    T Max { get; }
}