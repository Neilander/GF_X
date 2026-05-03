using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class BuildingInfoInteractionOption : IInteractionOption
{
    private BuildingEntity _owner;

    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => null;

    public bool IsVisible()
    {
        return GameEntry.GetComponent<TechManager>().IsInfoOptionVisible(_owner);
    }

    public bool IsExecutable()
    {
        return IsVisible();
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as BuildingEntity;
        DisplayName = displayName;
        DisplayDesc = string.Empty;
    }

    public void Execute()
    {
        // Info display is driven by focus + BuildingInfoTips; no action required on execute.
    }

    public void Clear()
    {
        _owner = null;
        DisplayName = null;
        DisplayDesc = null;
    }
}
