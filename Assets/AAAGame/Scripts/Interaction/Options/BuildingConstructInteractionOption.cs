using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class BuildingConstructInteractionOption : IInteractionOption
{
    private BuildingEntity _owner;
    private string _buildBuildingId;

    public string DisplayName { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => BuildManager.GetBuildingResourceCosts(_buildBuildingId);

    public bool IsVisible()
    {
        return BuildManager.IsConstructOptionVisible(_owner, _buildBuildingId);
    }

    public bool IsExecutable()
    {
        return BuildManager.IsConstructOptionExecutable(_owner, _buildBuildingId);
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as BuildingEntity;
        DisplayName = displayName;

        if (@params == null)
            return;

        _buildBuildingId = @params.Get<VarString>("BuildBuildingId");
    }

    public void Execute()
    {
        BuildManager.ConstructBuilding(_owner, _buildBuildingId);
    }

    public void Clear()
    {
        _owner = null;
        _buildBuildingId = null;
        DisplayName = null;
    }
}
