using GameFramework;
using System.Collections.Generic;
using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class BuildingEntity : MAEntity, IBuildingLogicContext
{
    protected override bool RequireUnitPresentationRuntime => false;
    protected override bool UsesUnitOutlinePresentation => false;

    public const string P_BuildingData = "BuildingData";
    public const string P_BuildingInstanceId = "BuildingInstanceId";
    public const string P_IsGameEndConditionBuilding = "IsGameEndConditionBuilding";
    public const string P_IsNavigationStaticBaked = "IsNavigationStaticBaked";

    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public Stronghold CurrentStronghold { get; private set; }
    public bool IsGameEndConditionBuilding { get; private set; }
    public bool IsNavigationStaticBaked { get; private set; }
    public bool HasUpgrade
    {
        get
        {
            if (buildingData == null)
                return false;

            if (buildingData.Lv == 0)
                return GameEntry.GetComponent<BuildManager>().HasConstructOption(this);

            var techManager = GameEntry.GetComponent<TechManager>();
            return techManager != null && techManager.HasTechInteraction(this);
        }
    }
    public bool IsDisabled => LogicState != null ? LogicState.IsDisabled : _isDisabled;
    public bool IsLv0Invincible => LogicState != null
        ? LogicState.BuildingData != null && LogicState.BuildingData.Lv == 0
        : throw new System.InvalidOperationException("BuildingEntity.IsLv0Invincible requires a bound logic state.");
    public bool IsPhaseProtected => LogicState != null
        ? LogicState.IsPhaseProtected
        : throw new System.InvalidOperationException("BuildingEntity.IsPhaseProtected requires a bound logic state.");
    public bool IsHealthBarSuppressedByBuff => _healthBarSuppressedByBuff || _stealthHealthBarSuppressed;
    public bool IsHealthBarSuppressedByPhaseBuff => _healthBarSuppressedByBuff;
    public bool HasPermanentNoAttackCapability => LogicState != null
        ? LogicState.HasPermanentNoAttackCapability
        : throw new System.InvalidOperationException("BuildingEntity.HasPermanentNoAttackCapability requires a bound logic state.");
    BuildingData IBuildingLogicContext.BuildingData => LogicState.BuildingData;
    BuildingExtraProps IBuildingLogicContext.ProductionProps => LogicState.ProductionProps;
    string IBuildingLogicContext.StrongholdId => LogicState.StrongholdId;
    int IBuildingLogicContext.OwnerFactionId => LogicState.OwnerFactionId;
    IReadOnlyList<LogicInteractionOptionDescriptor> IBuildingLogicContext.InteractionOptions => LogicState.InteractionOptions;
    bool IBuildingLogicContext.BlocksLogicMovement => LogicState.BlocksLogicMovement;
    bool IBuildingLogicContext.IsGameEndConditionBuilding => LogicState.IsGameEndConditionBuilding;
    bool IBuildingLogicContext.IsNavigationStaticBaked => LogicState.IsNavigationStaticBaked;
    event System.Action<int, int> IBuildingLogicContext.OwnerFactionChanged
    {
        add => LogicState.OwnerFactionChanged += value;
        remove => LogicState.OwnerFactionChanged -= value;
    }

    private bool _isDisabled;
    private bool _healthBarSuppressedByBuff;
    private bool _stealthHealthBarSuppressed;
    private bool _stealthMinimapHidden;
    private bool _permanentStealthVisibility;
    private MinimapReportComponent _minimapReportComponent;
    private BuildingExtraProps _extraProps; // 引用逻辑层按 BuildingInstanceId 管理的状态
    private readonly List<ColliderState> _collisionBlockingColliderStates = new List<ColliderState>();
    private bool _blocksLogicMovement = true;

    public LogicCombatShape GetRequiredWorldCombatShape()
    {
        LogicEntityState state = LogicState
                                 ?? throw new System.InvalidOperationException("BuildingEntity combat shape requires a bound logic state.");
        if (!state.IsBuildingEntity)
            throw new System.InvalidOperationException($"BuildingEntity combat shape requires building logic state. entity={state.EntityId.Value}.");
        return state.CombatShape;
    }

    public override LogicCombatShape CombatShape => GetRequiredWorldCombatShape();

    protected override void RefreshCharacterData(object userData)
    {
        var entityParams = userData as EntityParams;
        buildingData = entityParams?.Get(P_BuildingData) as BuildingData;
        BuildingInstanceId = entityParams != null && entityParams.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;
        IsGameEndConditionBuilding = LogicState.IsGameEndConditionBuilding;
        IsNavigationStaticBaked = LogicState.IsNavigationStaticBaked;
        CharacterKey = buildingData != null ? buildingData.Identifier : string.Empty;
        if (buildingData == null)
            throw new System.InvalidOperationException("BuildingEntity.RefreshCharacterData failed: BuildingData is missing.");
        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            throw new System.InvalidOperationException($"BuildingEntity.RefreshCharacterData failed: BuildingInstanceId is empty. character={CharacterKey}.");
    }

    /// <summary>当前注册到 BuildingOutlineFeature 的 Renderer 缓存，OnHide 反注册用。</summary>
    private Renderer[] _outlineRenderers;

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        OwnerFactionID = LogicState.OwnerFactionId;

        RegisterOutlineRenderers();

        ResetCombatPresentationState();
        LogBuildingCombatState("OnShow");
        SyncHurtBoxToBuildingBounds();

        InGameDataModel.RegisterBuilding(this);
        SyncSideFromFaction();
        EnsureMinimapReportComponent();
        SubscribeLv0PhaseVisibilityEvents();
        RefreshLv0PhaseVisibility();
        // 拿到 extra 属性引用（升级场景同 BuildingInstanceId 共享同一对象，extra 数据自然延续）
        _extraProps = LogicState.ProductionProps
                      ?? throw new System.InvalidOperationException(
                          $"BuildingEntity.OnShow failed: logic production props are missing. buildingInstanceId={BuildingInstanceId}.");
        ApplyDisabledPresentation(LogicState.IsDisabled, null, false);
        ApplyCollisionBlockingPresentation(LogicState.BlocksLogicMovement);
        SetPermanentStealthVisibility(LogicState.IsPermanentStealth);

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }

    }

    protected override void OnLogicDeactivating()
    {
        base.OnLogicDeactivating();
    }

    protected override CreaturePropertyManager CreateCreaturePropertyManager()
    {
        return LogicState.CreatureProperties
               ?? throw new System.InvalidOperationException($"BuildingEntity.CreateCreaturePropertyManager failed: logic properties are missing. entity={LogicEntityId.Value}.");
    }

    private Fix64 GetBuildingPropertyConfigValue(CreatureMainProperty property)
    {
        switch (property)
        {
            case CreatureMainProperty.Def:
                return buildingData != null && buildingData.Def > Fix64.Zero ? buildingData.Def : Fix64.Zero;
            case CreatureMainProperty.Health:
                return buildingData != null && buildingData.HP > Fix64.Zero ? buildingData.HP : (Fix64)120;
            case CreatureMainProperty.Sight:
                return buildingData != null && buildingData.Weapon != null && buildingData.Weapon.Range > Fix64.Zero
                    ? buildingData.Weapon.Range
                    : Fix64.Zero;
            case CreatureMainProperty.Speed:
            case CreatureMainProperty.CollisionRadius:
            case CreatureMainProperty.TurnRate:
            default:
                return Fix64.Zero;
        }
    }

    /// <summary>建筑死亡：无视阵营，统一播 "buildDeath" cue key。</summary>
    protected override void PlayDeathSound()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play("buildDeath");
    }

    /// <summary>调试：在场景里画建筑的告警广播范围（AlertRadius），便于核对召唤友军距离。</summary>
    private void OnDrawGizmos()
    {
        if (targetComp == null) return; // 未播放或未初始化：跳过
        float r = (float)targetComp.AlertRadiusFixed;
        if (r <= 0f) return;

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f); // 半透明黄
        Vector3 center = transform.position;
        const int segments = 36;
        float step = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    /// <summary>
    /// 把所有子 Renderer 注册到 BuildingOutlineFeature 全局列表，让屏幕空间描边 Pass 拾取。
    /// 配套 UnregisterOutlineRenderers 在 OnHide 调用，避免对象池复用时残留死引用。
    /// 建筑根 GameObject 打 "SkipOutline" tag 可跳过描边（tag 需先在 Project Settings → Tags 里添加）。
    /// </summary>
    private const string SkipOutlineTag = "SkipOutline";

    private void RegisterOutlineRenderers()
    {
        UnregisterOutlineRenderers(); // 防御：复用前残留先清掉
        if (HasSkipOutlineTag()) return;
        _outlineRenderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < _outlineRenderers.Length; i++)
            BuildingOutlineFeature.Register(_outlineRenderers[i]);
    }

    /// <summary>tag 未定义时 CompareTag 抛 UnityException，这里吞掉返回 false（默认全部描边）。</summary>
    private bool HasSkipOutlineTag()
    {
        try { return gameObject.CompareTag(SkipOutlineTag); }
        catch (UnityException) { return false; }
    }

    private void UnregisterOutlineRenderers()
    {
        if (_outlineRenderers == null) return;
        for (int i = 0; i < _outlineRenderers.Length; i++)
            BuildingOutlineFeature.Unregister(_outlineRenderers[i]);
        _outlineRenderers = null;
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        UnsubscribeLv0PhaseVisibilityEvents();
        RestorePhaseVisibility();
        SetStealthVisualState(false, false, 1f);
        ApplyCollisionBlockingPresentation(true);
        UnregisterOutlineRenderers();
        InGameDataModel.UnregisterBuilding(this);

        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        ResetCombatPresentationState();

        CurrentStronghold = null;
        OwnerFactionID = 0;
        IsGameEndConditionBuilding = false;
        IsNavigationStaticBaked = false;
        _healthBarSuppressedByBuff = false;
        _stealthHealthBarSuppressed = false;
        _stealthMinimapHidden = false;
        _permanentStealthVisibility = false;
        _extraProps = null; // 仅清 View 引用，逻辑状态按同一 BuildingInstanceId 延续
        buildingData = null;
        BuildingInstanceId = null;
        base.OnHide(isShutdown, userData);
    }

    void IBuildingLogicContext.SetCollisionBlockingByBuff(bool blocksMovement) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetCollisionBlockingByBuff));

    protected override void OnLogicCollisionBlockingPresentation(bool blocksMovement)
    {
        base.OnLogicCollisionBlockingPresentation(blocksMovement);
        ApplyCollisionBlockingPresentation(blocksMovement);
    }

    private void ApplyCollisionBlockingPresentation(bool blocksMovement)
    {
        if (_blocksLogicMovement == blocksMovement)
            return;

        _blocksLogicMovement = blocksMovement;
        if (!blocksMovement)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger)
                    continue;
                _collisionBlockingColliderStates.Add(new ColliderState(collider, collider.enabled));
                collider.enabled = false;
            }
        }
        else
        {
            for (int i = 0; i < _collisionBlockingColliderStates.Count; i++)
            {
                ColliderState state = _collisionBlockingColliderStates[i];
                if (state.Collider != null)
                    state.Collider.enabled = state.Enabled;
            }
            _collisionBlockingColliderStates.Clear();
        }

    }

    void IBuildingLogicContext.SetPermanentStealthByBuff(bool enabled)
    {
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetPermanentStealthByBuff));
    }

    protected override void OnLogicPermanentStealthPresentation(bool enabled)
    {
        base.OnLogicPermanentStealthPresentation(enabled);
        SetPermanentStealthVisibility(enabled);
    }

    private readonly struct ColliderState
    {
        public readonly Collider Collider;
        public readonly bool Enabled;

        public ColliderState(Collider collider, bool enabled)
        {
            Collider = collider;
            Enabled = enabled;
        }
    }

    internal IReadOnlyList<LogicCombatShape> GetRequiredWorldObstacleShapes()
    {
        LogicEntityState state = LogicState
                                 ?? throw new System.InvalidOperationException("BuildingEntity obstacle shapes require a bound logic state.");
        if (!state.IsBuildingEntity)
            throw new System.InvalidOperationException($"BuildingEntity obstacle shapes require building logic state. entity={state.EntityId.Value}.");
        return state.LogicObstacleShapes;
    }

    internal void BindStrongholdView(
        Stronghold stronghold,
        int previousOwnerFactionId,
        bool publishFactionChangedEvent)
    {
        string expectedStrongholdId = LogicState.StrongholdId;
        string actualStrongholdId = stronghold?.strongholdData?.StrongholdId;
        if (!string.Equals(expectedStrongholdId, actualStrongholdId, System.StringComparison.Ordinal))
        {
            throw new System.InvalidOperationException(
                $"Building stronghold view mismatch. entity={LogicEntityId.Value}, logic='{expectedStrongholdId}', view='{actualStrongholdId}'.");
        }

        int logicOwnerFactionId = LogicState.OwnerFactionId;
        if (stronghold != null && stronghold.OwnerFactionId != logicOwnerFactionId)
        {
            throw new System.InvalidOperationException(
                $"Building owner view mismatch. entity={LogicEntityId.Value}, logic={logicOwnerFactionId}, view={stronghold.OwnerFactionId}.");
        }

        CurrentStronghold = stronghold;
        ApplyOwnerFactionPresentation(
            previousOwnerFactionId,
            logicOwnerFactionId,
            publishFactionChangedEvent);
    }

    internal void UnbindStrongholdView()
    {
        CurrentStronghold = null;
    }

    void IBuildingLogicContext.SetOwnerFaction(int ownerFactionId)
    {
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetOwnerFaction));
    }

    private void ApplyOwnerFactionPresentation(
        int oldFactionId,
        int ownerFactionId,
        bool publishFactionChangedEvent)
    {
        if (ownerFactionId < 0)
            throw new System.ArgumentOutOfRangeException(nameof(ownerFactionId));

        OwnerFactionID = ownerFactionId;
        SyncSideFromFaction();
        _minimapReportComponent?.SetSide(Side);
        RefreshLv0PhaseVisibility();
        RefreshPermanentStealthVisibility();

        if (oldFactionId != OwnerFactionID)
        {
            RefreshInteractionHostForCurrentOwnership();
            if (publishFactionChangedEvent)
                GF.Event.Fire(this, EntityFactionChangedEventArgs.Create(Id, oldFactionId, OwnerFactionID, BuildingInstanceId));
        }
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        _minimapReportComponent?.Tick();
    }

    private void EnsureMinimapReportComponent()
    {
        if (_minimapReportComponent == null)
        {
            _minimapReportComponent = gameObject.GetComponent<MinimapReportComponent>();
            if (_minimapReportComponent == null)
            {
                _minimapReportComponent = gameObject.AddComponent<MinimapReportComponent>();
            }
        }

        string iconPrefabName = IsGameEndConditionBuilding ? MinimapUnitData.TargetLocationIconName : null;
        _minimapReportComponent.Initialize(Side, MinimapUnitType.Building, iconPrefabName);
        UpdateMinimapReportVisibility();
    }

    private void UpdateMinimapReportVisibility()
    {
        if (_minimapReportComponent == null)
        {
            return;
        }

        bool visible = !_stealthMinimapHidden
            && !IsLv0Building()
            && (!IsGameEndConditionBuilding || OwnerFactionID != EntitySideHelper.PlayerFactionId);
        _minimapReportComponent.SetVisible(visible);
    }

    protected override void SetUpHurtBox()
    {
        AttachHurtBoxToExistingColliders();
        //lv0建筑不创建碰撞体，避免影响卡牌禁区
        // // 兜底：如果建筑上没有可用碰撞体，沿用旧逻辑创建独立 HurtBox。
        // base.SetUpHurtBox();
        // SyncHurtBoxToBuildingBounds();
    }

    private bool AttachHurtBoxToExistingColliders()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        bool attached = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            var hurtBox = collider.GetComponent<HurtBox>();
            if (hurtBox == null)
                hurtBox = collider.gameObject.AddComponent<HurtBox>();

            hurtBox.Activate(this);
            attached = true;
        }

        return attached;
    }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        base.TakeDamage(damage, modType, attacker);
    }

    private void EnsureInteractionHost()
    {
        EnsureInteractionCollider();

        var host = GetComponent<InteractionHost>();
        if (host == null)
            host = gameObject.AddComponent<InteractionHost>();

        host.enabled = true;

        // 防御：即使 OnHide 没被调用，也不让旧交互泄漏到下一次复用。
        host.ResetOptions();
        host.Init(this);
        host.ConfigureFromLogicState(this);
    }

    void IBuildingLogicContext.SetPhaseProtectionByBuff(bool enabled) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetPhaseProtectionByBuff));

    public void SetHealthBarSuppressedByBuff(bool enabled)
    {
        if (_healthBarSuppressedByBuff == enabled)
            return;

        _healthBarSuppressedByBuff = enabled;
    }

    public void SetPermanentStealthVisibility(bool enabled)
    {
        if (_permanentStealthVisibility == enabled)
            return;

        _permanentStealthVisibility = enabled;
        RefreshPermanentStealthVisibility();
    }

    private void RefreshPermanentStealthVisibility()
    {
        bool hiddenFromPlayer = _permanentStealthVisibility && OwnerFactionID != EntitySideHelper.PlayerFactionId;
        _stealthHealthBarSuppressed = hiddenFromPlayer;
        _stealthMinimapHidden = hiddenFromPlayer;

        if (hiddenFromPlayer)
            HealthBarComp.Remove(Id);

        SetStealthVisualState(_permanentStealthVisibility, hiddenFromPlayer, 0.55f);
        UpdateMinimapReportVisibility();
    }

    private void RefreshInteractionHostForCurrentOwnership()
    {
        var host = GetComponent<InteractionHost>();
        bool canInteractAsOwner = OwnerFactionID == EntitySideHelper.PlayerFactionId && HasUpgrade;

        if (!canInteractAsOwner)
        {
            if (host != null)
            {
                host.ResetOptions();
                host.Init(this);
                host.enabled = false;
            }
            return;
        }

        EnsureInteractionHost();
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

    private void SyncHurtBoxToBuildingBounds()
    {
        var hurtBoxTransform = transform.Find("HurtBox");
        if (hurtBoxTransform == null)
            return;

        var hurtBoxCollider = hurtBoxTransform.GetComponent<BoxCollider>();
        if (hurtBoxCollider == null)
            return;

        if (!TryGetCombatBounds(out var bounds))
            return;

        hurtBoxCollider.center = hurtBoxTransform.InverseTransformPoint(bounds.center);

        Vector3 size = bounds.size;
        size.x = Mathf.Max(0.6f, size.x);
        size.y = Mathf.Max(1.2f, size.y);
        size.z = Mathf.Max(0.6f, size.z);
        hurtBoxCollider.size = size;
    }

    private bool TryGetCombatBounds(out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;

        var colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (initialized)
            return true;

        return TryGetVisualBounds(out bounds);
    }

    private void LogBuildingCombatState(string stage)
    {
        if (CreaturePropertyManager == null)
            return;

        Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 currentHealth = HealthValue;
        Fix64 defense = CreaturePropertyManager.GetProperty(CreatureMainProperty.Def);
        GameDebugSettings.Log(DebugCategory.Attack,
            $"[BuildingCombat] {stage} {buildingData?.Identifier} tableHp={buildingData?.HP} hp={currentHealth}/{maxHealth} def={defense}");
    }

    protected override void OnLogicHealthChangedPresentation(LogicEntityHealthChange change)
    {
        base.OnLogicHealthChangedPresentation(change);
        if (change.Delta >= Fix64.Zero)
            return;
        ShowDamagePopText(-change.Delta);
        GameDebugSettings.Log(DebugCategory.Attack,
            $"[BuildingDamage] {CharacterKey} final={-change.Delta} hp={change.Current}/{change.Max}");
    }

    protected override void OnLogicBuildingDisabledPresentation(bool disabled, IEntityContext attacker)
    {
        base.OnLogicBuildingDisabledPresentation(disabled, attacker);
        ApplyDisabledPresentation(disabled, attacker, true);
    }

    void IBuildingLogicContext.RestoreBuildingToFullHealth() =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.RestoreBuildingToFullHealth));

    private void ResetCombatPresentationState()
    {
        _isDisabled = false;
        SetDisabledVisual(false);
    }

    private void ApplyDisabledPresentation(bool disabled, IEntityContext attacker, bool publishEvent)
    {
        if (_isDisabled == disabled)
            return;

        _isDisabled = disabled;
        Alive = LogicState.Alive;
        SetDisabledVisual(disabled);
        if (!publishEvent)
            return;

        GF.Event.Fire(this, BuildingDisabledStateChangedEventArgs.Create(Id, BuildingInstanceId, disabled));
        if (disabled)
        {
            PlayDeathSound();
        }
    }

    private void SyncSideFromFaction()
    {
        int teamId = EntityCombatTeamHelper.ResolveTeamIdByFaction(OwnerFactionID);
        Side = EntitySideHelper.ToSide(teamId);
    }

    // 是否显示建筑受伤跳字。false=不显示。改回 true 即可恢复
    private const bool EnableDamagePopText = false;

    private void ShowDamagePopText(Fix64 damage)
    {
#pragma warning disable 0162
        if (!EnableDamagePopText)
            return;

        Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), ((float)damage).ToString(), endPos, DamageTextType.Normal);
#pragma warning restore 0162
    }

    /// <summary>
    /// 当前建筑的日产出值（仅对 Prod 类型建筑有意义）。
    /// = BuildingData.Production (base) + BuildingExtraProps.Production (tech extra) + BuildingExtraProps.DynamicProduction (动态产出)
    /// </summary>
    public int GetProduction()
    {
        return LogicBuildingProductionService.GetProduction(this);
    }

    public int GetArmyForce()
    {
        return LogicState.GetArmyForce();
    }

    public int GetArmyForceWithoutRuntimeRules()
    {
        return LogicState.GetArmyForceWithoutRuntimeRules();
    }

    public int GetArmySupplyPerUnit()
    {
        return LogicState.GetArmySupplyPerUnit();
    }

    public int GetArmyOccupiedSupply()
    {
        return LogicState.GetArmyOccupiedSupply();
    }

    void IBuildingLogicContext.SetArmyForceBase(int value) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetArmyForceBase));

    void IBuildingLogicContext.SetArmySupplyPerUnitBase(int value) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.SetArmySupplyPerUnitBase));

    void IBuildingLogicContext.ModifyArmyForce(IPropertyModifier modifier, bool ifAdd) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.ModifyArmyForce));

    void IBuildingLogicContext.ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd) =>
        throw ViewLogicMutationException(nameof(IBuildingLogicContext.ModifyArmySupplyPerUnit));

    private static System.InvalidOperationException ViewLogicMutationException(string memberName)
    {
        return new System.InvalidOperationException(
            $"BuildingEntity View cannot execute {memberName}; update the registered LogicEntityState instead.");
    }

    private void RaiseArmyCardPropertyChangedEvent()
    {
        int armyForce = GetArmyForce();
        int supplyPerUnit = GetArmySupplyPerUnit();
        int occupiedSupply = GetArmyOccupiedSupply();

        GF.Event.Fire(
            this,
            ArmyBuildingCardPropertyChangedEventArgs.Create(
                Id,
                BuildingInstanceId,
                armyForce,
                supplyPerUnit,
                occupiedSupply));
    }

    public void RaiseArmyCardPropertyChangedEventForTech()
    {
        RaiseArmyCardPropertyChangedEvent();
    }

}
