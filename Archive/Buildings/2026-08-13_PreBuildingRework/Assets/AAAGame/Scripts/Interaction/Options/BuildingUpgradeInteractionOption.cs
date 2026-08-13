using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class BuildingUpgradeInteractionOption : IInteractionOption
{
    private BuildingEntity _owner;
    private string _upgradeBuildingId;
    private string _techId;

    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => GameEntry.GetComponent<BuildManager>().GetBuildingResourceCosts(_upgradeBuildingId, _owner);

    public bool IsVisible()
    {
        return GameEntry.GetComponent<TechManager>().IsUpgradeOptionVisible(_owner, _upgradeBuildingId, _techId);
    }

    public bool IsExecutable()
    {
        return GameEntry.GetComponent<TechManager>().IsUpgradeOptionExecutable(_owner, _upgradeBuildingId, _techId);
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as BuildingEntity;
        DisplayName = displayName;
        DisplayDesc = string.Empty;

        if (@params == null)
            return;

        _upgradeBuildingId = @params.Get<VarString>("UpgradeBuildingId");
        _techId = @params.Get<VarString>("TechId");

        var techData = TechDataModel.GetTechData(_techId);
        if (techData != null)
            DisplayDesc = techData.GetFormattedDesc();
    }

    public void Execute()
    {
        GameEntry.GetComponent<TechManager>().UpgradeBuilding(_owner, _upgradeBuildingId, _techId);
    }

    public void Clear()
    {
        _owner = null;
        _upgradeBuildingId = null;
        _techId = null;
        DisplayName = null;
        DisplayDesc = null;
    }
}
