using System.Collections.Generic;
using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;
using GameFramework.Event;

/// <summary>
/// 小兵实体：使用 DirectAtkComp（直接选定目标造成伤害，不走攻击盒）。
/// 适合大量小兵的战斗场景。
/// </summary>
public partial class SoldierEntity : MAEntity
{
    private const string HeroOutOfCombatSpeedBuffId = "hero_out_of_combat_speed_x2";
    private const float HeroOutOfCombatSpeedDelay = 2f;
    private const float HeroOutOfCombatSpeedRampDuration = 1f;
    private static readonly Fix64 HeroOutOfCombatSpeedBuffPercent = Fix64.One;
    private bool _heroOutOfCombatSpeedFirstApplyPending;
    private bool _heroOutOfCombatSpeedInitialGraceActive;

    /// <summary>
    /// AI类型
    /// </summary>
    public BrainType BrainType { get; private set; }
    public string SourceStrongholdId { get; private set; }
    private MinimapReportComponent m_MinimapReportComponent;

    public override void ChangeSide(SideType newSide)
    {
        base.ChangeSide(newSide);
        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.SetSide(newSide);
        }
    }

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        m_MinimapReportComponent = gameObject.AddComponent<MinimapReportComponent>();
    }

    protected override void OnShow(object userData)
    {
        _isHidingOrShuttingDown = false;
        _heroOutOfCombatSpeedFirstApplyPending = true;
        _heroOutOfCombatSpeedInitialGraceActive = false;

        base.OnShow(userData);
        if (userData is EntityParams ep)
        {
            if (ep.position.HasValue)
                ApplySpawnPosition(ep.position.Value);
            else if (ep.BrainType == BrainType.Player)
                Log.Error("Player SoldierEntity missing spawn position in EntityParams. CharacterKey={0}", CharacterKey);

            Side = ep.Side;
            BrainType = ep.BrainType; // 设置AI类型
            SourceStrongholdId = ep.GetString(EntityParams.P_SourceStrongholdId);
            SetBrain(BrainFactory.Create(ep.BrainType, this, ep));
            ConfigureTargetingModeForSpawn();
            ApplyDefendPhaseSpawnParams(ep);

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

        // 敌方入侵兵记录出生点用于"超出追击距离脱战返航"。防御阶段敌兵不启用该逻辑。
        if (Side == SideType.EnemySide && Brain is SoldierAIBrain soldierBrain && BrainType == BrainType.SoldierAI)
        {
            soldierBrain.SetBirthPosition(transform.position);
        }

        SyncHeroOutOfCombatSpeedBuff();
        RegisterToGroupMove(); // Side 已赋值，安全注册
        SubscribePhaseEvents();
    }

    private void SubscribePhaseEvents()
    {
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
    }

    private void UnsubscribePhaseEvents()
    {
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnPhaseChanged);
    }

    private void OnPhaseChanged(object sender, GameEventArgs e)
    {
        if (e is not IngamePhaseChangedEventArgs args)
            return;

        if (args.OldPhase == args.NewPhase)
            return;

        // 如果是英雄且存活，转阶段时回满血
        if (Alive && IsHeroUnit())
        {
            Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
            Fix64 delta = maxHealth - HealthValue;
            if (delta > Fix64.Zero)
            {
                CreaturePropertyManager.ModifyCurrentProperty(
                    CreatureCurrentProperty.HealthCurrent,
                    PropertyIrreversibleAdditiveModifier.Create(delta),
                    true);

                GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)maxHealth, (float)maxHealth, (float)delta));
            }
        }

        SyncHeroOutOfCombatSpeedBuff();
        OnPhaseChangedForDefendPhaseSpeed(args);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        SyncHeroOutOfCombatSpeedBuff();
        TickDefendPhaseSpeedControl();
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
        DisableDefendPhaseSpeedControl();
        UnsubscribePhaseEvents();
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

    protected override void OnOutOfCombatStateRefreshed()
    {
        base.OnOutOfCombatStateRefreshed();
        SyncHeroOutOfCombatSpeedBuff();
    }

    private void SyncHeroOutOfCombatSpeedBuff()
    {
        if (!IsHeroUnit() || BuffComp == null || CreaturePropertyManager == null)
            return;

        if (!IsOutOfCombat)
            _heroOutOfCombatSpeedInitialGraceActive = false;

        bool isFirstApply = _heroOutOfCombatSpeedFirstApplyPending;
        bool keepInitialGraceBuff = _heroOutOfCombatSpeedInitialGraceActive && IsOutOfCombat;
        bool canEnableBuff = Alive
                             && (isFirstApply
                                 || keepInitialGraceBuff
                                 || (IsOutOfCombat && OutOfCombatElapsedSeconds >= HeroOutOfCombatSpeedDelay));
        bool hasBuff = BuffComp.HasBuff(HeroOutOfCombatSpeedBuffId);

        if (canEnableBuff)
        {
            _heroOutOfCombatSpeedFirstApplyPending = false;
            if (isFirstApply)
                _heroOutOfCombatSpeedInitialGraceActive = true;

            if (hasBuff)
                return;

            BuffData buffData = BuffData.Create(
                id: HeroOutOfCombatSpeedBuffId,
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: new List<BuffCallback>
                {
                    new RampedPercentMoveSpeedBonusBuff(
                        HeroOutOfCombatSpeedBuffPercent,
                        HeroOutOfCombatSpeedRampDuration,
                        isFirstApply)
                });

            BuffComp.AddBuff(buffData, this);
            return;
        }

        RemoveHeroOutOfCombatSpeedBuff();
    }

    private void RemoveHeroOutOfCombatSpeedBuff()
    {
        _heroOutOfCombatSpeedInitialGraceActive = false;
        if (BuffComp != null && BuffComp.HasBuff(HeroOutOfCombatSpeedBuffId))
            BuffComp.RemoveBuff(HeroOutOfCombatSpeedBuffId);
    }

    private void ConfigureTargetingModeForSpawn()
    {
        if (targetComp is not CharacterTargetingComp targetingComp)
            return;

        if (BrainType != BrainType.DefendEnemyAI || Side != SideType.EnemySide)
        {
            if (Brain is SoldierAIBrain defaultBrain)
                defaultBrain.SetReturnToBirthEnabled(true);
            targetingComp.UseDefaultMode();
            return;
        }

        if (Brain is SoldierAIBrain defendBrain)
            defendBrain.SetReturnToBirthEnabled(false);

        var gameEndManager = GameEntry.GetComponent<GameEndManager>();
        if (gameEndManager != null
            && gameEndManager.TryGetNearestPlayerInitialConditionBuilding(transform.position, out BuildingEntity baseBuilding)
            && baseBuilding != null)
        {
            targetingComp.UseDefendEnemyMode(baseBuilding);
            return;
        }

        targetingComp.UseDefendEnemyMode(null);
    }

    private void ApplyDefendPhaseSpawnParams(EntityParams ep)
    {
        if (ep == null || BrainType != BrainType.DefendEnemyAI || Side != SideType.EnemySide)
            return;

        if (!ep.TryGet<VarFloat>(P_DefendAssignedSpeed, out VarFloat assignedSpeedVar))
            return;

        float assignedSpeed = assignedSpeedVar;
        if (assignedSpeed <= 0f)
            return;

        EnableDefendPhaseSpeedControl((Fix64)assignedSpeed);
    }

}
