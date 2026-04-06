using GameFramework;
using System.Collections.Generic;

public sealed class DeviceOpenPanelInteractionOption : IInteractionOption
{
    private static readonly KeyValuePair<IngameValueType, int>[] EmptyCost = System.Array.Empty<KeyValuePair<IngameValueType, int>>();
    private DeviceEntity _owner;
    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => EmptyCost;

    public bool IsVisible()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        return _owner.HasInteractionPanel;
    }

    public bool IsExecutable()
    {
        if (_owner == null || _owner.deviceData == null)
            return false;
        return _owner.HasInteractionPanel;
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as DeviceEntity;
        DisplayName = displayName;
        DisplayDesc = string.Empty;
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
        DisplayName = null;
        DisplayDesc = null;
    }
}
