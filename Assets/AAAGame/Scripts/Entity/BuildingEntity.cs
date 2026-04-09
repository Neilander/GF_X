using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildingEntity : BattleEntity
{
    public const string P_BuildingData = "BuildingData";
    public const string P_InitOwnerFactionID = "InitOwnerFactionID";
    public const string P_BuildingInstanceId = "BuildingInstanceId";
    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public bool HasUpgrade => BuildManager.HasUpgrade(this);

    // 建筑不需要 Animator
    protected override void SetUpAnimator() { }

    protected override void InitBattleData(object userData)
    {
        buildingData = Params.Get(P_BuildingData) as BuildingData;
        OwnerFactionID = Params.Get<VarInt32>(P_InitOwnerFactionID);
        BuildingInstanceId = Params.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;

        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            BuildingInstanceId = System.Guid.NewGuid().ToString("N");

        ReferenceId = buildingData?.Identifier ?? "Building";
    }

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }
    }

    public override void TakeDamage(float damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        // TODO: 建筑受伤逻辑，用 buildingData 的血量
        if (!Alive) return;
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
