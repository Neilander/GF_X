using GameFramework;
using System.Collections.Generic;
using UnityGameFramework.Runtime;

public sealed class TechResearchInteractionOption : IInteractionOption
{
    private BuildingEntity _owner;
    private string _techId;

    public string DisplayName { get; private set; }
    public string DisplayDesc { get; private set; }
    public KeyValuePair<IngameValueType, int>[] CostResource => GameEntry.GetComponent<TechManager>().GetTechResourceCosts(_techId);

    public bool IsVisible()
    {
        return GameEntry.GetComponent<TechManager>().IsResearchOptionVisible(_owner, _techId);
    }

    public bool IsExecutable()
    {
        return GameEntry.GetComponent<TechManager>().IsResearchOptionExecutable(_owner, _techId);
    }

    public void Init(object owner, string displayName, InteractionParams @params)
    {
        _owner = owner as BuildingEntity;
        DisplayName = displayName;
        DisplayDesc = string.Empty;

        if (@params == null)
            return;

        _techId = @params.Get<VarString>("TechId");

        var techData = TechDataModel.GetTechData(_techId);
        if (techData != null)
            DisplayDesc = techData.GetFormattedDesc();
    }

    public void Execute()
    {
        GameEntry.GetComponent<TechManager>().ResearchTech(_owner, _techId);
    }

    public void Clear()
    {
        _owner = null;
        _techId = null;
        DisplayName = null;
        DisplayDesc = null;
    }
}