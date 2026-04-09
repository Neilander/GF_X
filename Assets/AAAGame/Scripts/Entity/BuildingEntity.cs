using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildingEntity : EntityBase
{
    public const string P_BuildingData = "BuildingData";
    public const string P_BuildingInstanceId = "BuildingInstanceId";
    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public Stronghold CurrentStronghold { get; private set; }
    public bool HasUpgrade => BuildManager.HasUpgrade(this);


    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        buildingData = Params.Get(P_BuildingData) as BuildingData;
        BuildingInstanceId = Params.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;

        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            BuildingInstanceId = System.Guid.NewGuid().ToString("N");

        LevelEntity.RegisterBuildingToStronghold(this);

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        LevelEntity.UnregisterBuildingFromStronghold(this);

        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        CurrentStronghold = null;
        OwnerFactionID = 0;
        buildingData = null;
        base.OnHide(isShutdown, userData);
    }

    public void SetStronghold(Stronghold stronghold)
    {
        CurrentStronghold = stronghold;
        OwnerFactionID = stronghold != null ? stronghold.OwnerFactionId : 0;
    }

    private void EnsureInteractionHost()
    {
        EnsureInteractionCollider();

        var host = GetComponent<InteractionHost>();
        if (host == null)
            host = gameObject.AddComponent<InteractionHost>();

        // 防御：即使 OnHide 没被调用，也不让旧交互泄漏到下一次复用。
        host.ResetOptions();
        host.Init(this);

        BuildManager.ConfigureBuildInteractionOptions(this, host);
    }

    private void EnsureInteractionCollider()
    {
        if (GetComponentInChildren<Collider>() != null)
            return;

        var sphere = gameObject.AddComponent<SphereCollider>();
        sphere.isTrigger = true;

        if (TryGetVisualBounds(out var bounds))
        {
            sphere.center = transform.InverseTransformPoint(bounds.center);
            sphere.radius = Mathf.Clamp(bounds.extents.magnitude, 0.8f, 3f);
        }
        else
        {
            sphere.center = Vector3.zero;
            sphere.radius = 1.2f;
        }
    }

    private bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return false;

        bool initialized = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return initialized;
    }
}
