using GameFramework;

public sealed class DeviceOpenPanelInteractionOption : IInteractionOption
{
    private DeviceEntity _owner;
    public string DisplayName { get; private set; }
    public StringIntPair[] CostMaterial => null;

    public bool IsExecutable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        return _owner.HasInteractionPanel;
    }
    public bool IsAvailable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        return _owner.HasInteractionPanel;
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as DeviceEntity;
        DisplayName = displayName;
    }

    public void Execute()
    {
        if (_owner == null || _owner.deviceData == null)
            return;

        if (!_owner.HasInteractionPanel)
            return;

        var uiParams = UIParams.Create();
        uiParams.Set(InteractionPanel.P_Owner, _owner);
        GF.UI.OpenUIForm(_owner.deviceData.InteractionPanelID.Value, uiParams);
    }

    public void Clear()
    {
        _owner = null;
    }
}
