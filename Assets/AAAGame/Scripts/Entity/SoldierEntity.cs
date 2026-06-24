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
    /// <summary>
    /// AI类型
    /// </summary>
    public BrainType BrainType { get; private set; }
    public string SourceStrongholdId { get; private set; }
    public string SourceBuildingInstanceId { get; private set; }
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
        base.OnShow(userData);
        if (userData is EntityParams ep)
        {
            Side = ep.Side;
            BrainType = ep.BrainType; // 设置AI类型
            SourceStrongholdId = ep.GetString(EntityParams.P_SourceStrongholdId);
            SourceBuildingInstanceId = ep.GetString(EntityParams.P_SourceBuildingInstanceId);

            if (ep.position.HasValue)
            {
                ApplySpawnPosition(ep.position.Value);
                LogSpawnDiagnostics(ep.position.Value);
            }
            else if (ep.BrainType == BrainType.Player)
                Log.Error("Player SoldierEntity missing spawn position in EntityParams. CharacterKey={0}", CharacterKey);

            SetBrain(BrainFactory.Create(ep.BrainType, this, ep));
            ConfigureTargetingModeForSpawn();
            ApplyDefendPhaseSpawnParams(ep);
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

        OnPhaseChangedForDefendPhaseSpeed(args);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        TickDefendPhaseSpeedControl();

        if (m_MinimapReportComponent != null)
        {
            m_MinimapReportComponent.Tick();
        }

        // Debug: 绿线=导航方向, 红线=到目标直线
        if (targetComp?.CurrentTarget != null && targetComp.CurrentTarget.Alive)
        {
            var target = targetComp.CurrentTarget;
            Vector3 pos = Position + Vector3.up * 0.5f;
            // 红线：到目标的直线
            Debug.DrawLine(pos, target.Position + Vector3.up * 0.5f, Color.red);
            // 绿线：当前导航计算的方向
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

        // 直接创建 DirectAtkComp，不再走 Factory
        var atkComp = new DirectAtkComp();
        this.SetAtkComp(atkComp);    // 先让 Entity 持有引用
        atkComp.Init(this);          // Init 内部会创建 WeaponComp 并通过 SetWeaponComp 挂载

        FactoryHelper.CreateTargetingComp(UtilityBuiltin.AssetsPath.GetTargetingFactoryPath(targetFacPath), this);
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
        DisableDefendPhaseSpeedControl();
        UnsubscribePhaseEvents();
        base.OnHide(isShutdown, userData);
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

    private void LogSpawnDiagnostics(Vector3 requestedPosition)
    {
        bool flowHit = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            requestedPosition,
            navAgentTypeID,
            2.5f,
            0f,
            out Vector3 legalPoint);
        Log.Info(
            "[SoldierSpawn] key={0} brain={1} side={2} unitLevel={3} unitSize={4} navAgentType={5} requestedPos={6} actualPos={7} flowHit={8} flowPos={9}",
            CharacterKey,
            BrainType,
            Side,
            UnitLevel,
            CharacterData != null ? CharacterData.Size.ToString() : "null",
            navAgentTypeID,
            requestedPosition,
            transform.position,
            flowHit,
            flowHit ? legalPoint.ToString() : "none");
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
