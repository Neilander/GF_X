using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class BuildingConstructInteractionOption : IInteractionOption
{
    private BuildingEntity _owner;
    private string _buildBuildingId;

    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => GameEntry.GetComponent<BuildManager>().GetBuildingResourceCosts(_buildBuildingId, _owner);

    public bool IsVisible()
    {
        return GameEntry.GetComponent<BuildManager>().IsConstructOptionVisible(_owner, _buildBuildingId);
    }

    public bool IsExecutable()
    {
        return GameEntry.GetComponent<BuildManager>().IsConstructOptionExecutable(_owner, _buildBuildingId);
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as BuildingEntity;
        DisplayName = displayName;
        DisplayDesc = string.Empty;

        if (@params == null)
            return;

        _buildBuildingId = @params.Get<VarString>("BuildBuildingId");

        var buildingData = BuildingDataModel.GetBuildingData(_buildBuildingId);
        if (buildingData != null)
            DisplayDesc = buildingData.GetFormattedDesc();
    }

    public void Execute()
    {
        GameEntry.GetComponent<BuildManager>().ConstructBuilding(_owner, _buildBuildingId);
    }

    public void Clear()
    {
        _owner = null;
        _buildBuildingId = null;
        DisplayName = null;
        DisplayDesc = null;
    }
}
