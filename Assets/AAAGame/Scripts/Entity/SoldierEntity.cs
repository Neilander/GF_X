using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 小兵实体：使用 DirectAtkComp（直接选定目标造成伤害，不走攻击盒）。
/// 适合大量小兵的战斗场景。
/// </summary>
public partial class SoldierEntity : MAEntity
{
    /// <summary>
    /// AI类型
    /// </summary>
    public BrainType BrainType { get; private set; }
    private MinimapReportComponent m_MinimapReportComponent;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        m_MinimapReportComponent = gameObject.AddComponent<MinimapReportComponent>();
    }

    protected override void OnShow(object userData)
    {
        _isHidingOrShuttingDown = false;

        base.OnShow(userData);
        if (userData is EntityParams ep)
        {
            if (ep.position.HasValue)
                ApplySpawnPosition(ep.position.Value);
            else if (ep.BrainType == BrainType.Player)
                Log.Error("Player SoldierEntity missing spawn position in EntityParams. CharacterKey={0}", CharacterKey);

            Side = ep.Side;
            BrainType = ep.BrainType; // 设置AI类型
            SetBrain(BrainFactory.Create(ep.BrainType, this, ep));

        }


        if (Brain is AAAGame.Scripts.Entity.PlayerBrain)
        {
            EnsurePlayerInteractionRuntime();
        }

        //Debug.LogError("什么玩意");

        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.Initialize(Side);
        }


        RegisterToGroupMove(); // Side 已赋值，安全注册
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        TickGhostCollisionRuntime();

        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.Tick();
        }

        // Debug: 绿线=NavMesh方向, 红线=到目标直线
        if (targetComp?.CurrentTarget != null && targetComp.CurrentTarget.Alive)
        {
            var target = targetComp.CurrentTarget;
            Vector3 pos = Position + Vector3.up * 0.5f;
            // 红线：到目标的直线
            Debug.DrawLine(pos, target.Position + Vector3.up * 0.5f, Color.red);
            // 绿线：NavMesh 计算的方向
            if (moveComp != null)
            {
                Vector3 navDir = moveComp.GetNavDirection();
                Debug.DrawRay(pos, navDir * 3f, Color.green);
            }
        }

    }

    protected override void SetUpMAComp(object userData)
    {
        string moveFacPath = "CharacterMoveFactory";
        string targetFacPath = "CharacterTargetingFactory";

        FactoryHelper.CreateMoveComp(UtilityBuiltin.AssetsPath.GetMoveFactoryPath(moveFacPath), this);
        FactoryHelper.CreateTargetingComp(UtilityBuiltin.AssetsPath.GetTargetingFactoryPath(targetFacPath), this);

        // 直接创建 DirectAtkComp，不再走 Factory
        var atkComp = new DirectAtkComp();
        this.SetAtkComp(atkComp);    // 先让 Entity 持有引用
        atkComp.Init(this);          // Init 内部会创建 WeaponComp 并通过 SetWeaponComp 挂载
    }


    /// <summary>
    /// 获取单位类型（重写基类方法）
    /// </summary>
    /*
    protected override string GetUnitType()
    {
        return _unitIndex;
    }*/

    protected override void OnHide(bool isShutdown, object userData)
    {
        _isHidingOrShuttingDown = true;
        ClearGhostRuntimeState();
        base.OnHide(isShutdown, userData);
    }

    private const string PlayerInteractionNodeName = "InteractCollider";
    private const float PlayerInteractionRange = 2.7f;
    private const float PlayerInteractionPadding = 0.7f;

    private void EnsurePlayerInteractionRuntime()
    {
        Transform interactionNode = transform.Find(PlayerInteractionNodeName);
        GameObject interactionObject;

        if (interactionNode == null)
        {
            interactionObject = new GameObject(PlayerInteractionNodeName);
            interactionObject.transform.SetParent(transform);
            interactionObject.transform.localPosition = Vector3.zero;
            interactionObject.transform.localRotation = Quaternion.identity;
            interactionObject.transform.localScale = Vector3.one;
        }
        else
        {
            interactionObject = interactionNode.gameObject;
        }

        SphereCollider triggerSphere = interactionObject.GetComponent<SphereCollider>();
        if (triggerSphere == null)
            triggerSphere = interactionObject.AddComponent<SphereCollider>();
        triggerSphere.isTrigger = true;

        Rigidbody triggerBody = interactionObject.GetComponent<Rigidbody>();
        if (triggerBody == null)
            triggerBody = interactionObject.AddComponent<Rigidbody>();
        triggerBody.isKinematic = true;
        triggerBody.useGravity = false;
        triggerBody.constraints = RigidbodyConstraints.FreezeAll;

        InteractionDetector detector = interactionObject.GetComponent<InteractionDetector>();
        if (detector == null)
            detector = interactionObject.AddComponent<InteractionDetector>();

        InteractionManager manager = interactionObject.GetComponent<InteractionManager>();
        if (manager == null)
            manager = interactionObject.AddComponent<InteractionManager>();

        if (interactionObject.GetComponent<InteractOptionTipsPresenter>() == null)
            interactionObject.AddComponent<InteractOptionTipsPresenter>();

        manager.ConfigureRuntime(
            detector,
            PlayerInteractionRange,
            PlayerInteractionPadding,
            0.65f,
            0.35f,
            0.08f,
            0.1f);
    }

    private void ApplySpawnPosition(Vector3 worldPosition)
    {
        CharacterController controller = GetComponent<CharacterController>();
        if (controller != null)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = worldPosition;
            controller.enabled = wasEnabled;
            return;
        }

        transform.position = worldPosition;
    }

}
