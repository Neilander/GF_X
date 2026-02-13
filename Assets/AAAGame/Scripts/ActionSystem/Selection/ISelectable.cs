public interface ISelectable
{
    bool CanBeSelected();
    void InSelection(ISelector selector);
    void DeSelection();
}