using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildingEntity : EntityBase
{
    public const string P_BuildingData = "BuildingData";
    public const string P_InitOwnerFactionID = "InitOwnerFactionID";
    public const string P_BuildingInstanceId = "BuildingInstanceId";
    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public bool HasUpgrade => BuildManager.HasUpgrade(this);


    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        buildingData = Params.Get(P_BuildingData) as BuildingData;
        OwnerFactionID = Params.Get<VarInt32>(P_InitOwnerFactionID);
        BuildingInstanceId = Params.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;

        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            BuildingInstanceId = System.Guid.NewGuid().ToString("N");

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        buildingData = null;
        base.OnHide(isShutdown, userData);
    }

    private void EnsureInteractionHost()
    {
        var host = GetComponent<InteractionHost>();
        if (host == null)
            host = gameObject.AddComponent<InteractionHost>();

        // 防御：即使 OnHide 没被调用，也不让旧交互泄漏到下一次复用。
        host.ResetOptions();
        host.Init(this);

        BuildManager.ConfigureUpgradeInteractionOptions(this, host);
    }
}
