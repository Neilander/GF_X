using GameFramework;
using System.Collections.Generic;
using AAAGame.MiniMap;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class BuildingEntity : MAEntity
{
    public const string P_BuildingData = "BuildingData";
    public const string P_BuildingInstanceId = "BuildingInstanceId";
    public const string P_IsGameEndConditionBuilding = "IsGameEndConditionBuilding";
    public const string P_IsNavigationStaticBaked = "IsNavigationStaticBaked";
    public const string P_EnableConstructionEscape = "EnableConstructionEscape";

    private static readonly Fix64 PlaceholderAttackInterval = (Fix64)1.6f;
    private static readonly Fix64 PlaceholderAttackRange = (Fix64)650;
    private static readonly Fix64 PlaceholderWindUp = (Fix64)0.35f;
    private static readonly Fix64 PlaceholderWindDown = (Fix64)0.35f;
    private const string Lv0InvincibleBuffId = "building_lv0_invincible";
    private const string PhaseGuardBuffId = "building_phase_guard";
    private const string ArmyForcePropertyId = "Building_ArmyForce";
    private const string ArmySupplyPerUnitPropertyId = "Building_ArmySupplyPerUnit";

    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public Stronghold CurrentStronghold { get; private set; }
    public bool IsGameEndConditionBuilding { get; private set; }
    public bool IsNavigationStaticBaked { get; private set; }
    public bool EnableConstructionEscape { get; private set; }
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
    public bool IsDisabled => _isDisabled;
    public bool IsLv0Invincible => _lv0InvincibleByBuff;
    public bool IsPhaseProtected => _phaseProtectionByBuff;
    public bool IsHealthBarSuppressedByBuff => _healthBarSuppressedByBuff || _stealthHealthBarSuppressed;
    public bool IsHealthBarSuppressedByPhaseBuff => _healthBarSuppressedByBuff;
    public bool HasPermanentNoAttackCapability { get; private set; }

    private DirectAtkComp _directAtkComp;
    private bool _isDisabled;
    private bool _lv0InvincibleByBuff;
    private bool _phaseProtectionByBuff;
    private bool _healthBarSuppressedByBuff;
    private bool _stealthHealthBarSuppressed;
    private bool _stealthMinimapHidden;
    private bool _permanentStealthVisibility;
    private bool _combatLocked;
    private static readonly ICapability DisabledStateLocker = new DisabledCapabilityLocker();
    private BaseValueProperty _armyForceProperty;
    private BaseValueProperty _armySupplyPerUnitProperty;
    private MinimapReportComponent _minimapReportComponent;
    private BuildingExtraProps _extraProps; // 引用自 GlobalBuffManager 的中央字典，升级场景同 id 共享同对象
    private readonly List<int> _registeredFlowObstacleIds = new List<int>();
    protected override bool UsesFlowNavigationAgent => false;

    protected override void RefreshCharacterData(object userData)
    {
        var entityParams = userData as EntityParams;
        buildingData = entityParams?.Get(P_BuildingData) as BuildingData;
        BuildingInstanceId = entityParams != null && entityParams.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;
        IsGameEndConditionBuilding = entityParams != null
            && entityParams.TryGet<VarBoolean>(P_IsGameEndConditionBuilding, out var isGameEndConditionBuilding)
            && isGameEndConditionBuilding;
        IsNavigationStaticBaked = entityParams != null
            && entityParams.TryGet<VarBoolean>(P_IsNavigationStaticBaked, out var isNavigationStaticBaked)
            && isNavigationStaticBaked;
        EnableConstructionEscape = entityParams != null
            && entityParams.TryGet<VarBoolean>(P_EnableConstructionEscape, out var enableConstructionEscape)
            && enableConstructionEscape;

        CharacterKey = buildingData != null ? buildingData.Identifier : string.Empty;
        SetBrain(new BuildingAIBrain());

        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            BuildingInstanceId = System.Guid.NewGuid().ToString("N");
    }

    /// <summary>当前注册到 BuildingOutlineFeature 的 Renderer 缓存，OnHide 反注册用。</summary>
    private Renderer[] _outlineRenderers;

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        TauntLevel = 0; // 建筑默认嘲讽等级 0

        RegisterOutlineRenderers();

        InitializeAttackCapabilityFlags();
        ResetCombatRuntimeState();
        ApplyBuildingPropertyOverrides();
        LogBuildingCombatState("OnShow");
        InitializeArmyCardProperties();
        ConfigureCombatByBuildingData();
        SyncHurtBoxToBuildingBounds();

        InGameDataModel.RegisterBuilding(this);
        SyncSideFromFaction();
        EnsureMinimapReportComponent();
        SubscribeLv0PhaseVisibilityEvents();
        RefreshLv0PhaseVisibility();
        // 拿到 extra 属性引用（升级场景同 BuildingInstanceId 共享同一对象，extra 数据自然延续）
        _extraProps = GameEntry.GetComponent<GlobalBuffManager>()?.GetOrCreateExtraProps(BuildingInstanceId);

        EnsureLv0InvincibleBuff();
        EnsurePhaseProtectionBuff();
        ApplyBuildingInitialBuffs();

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }

        RegisterFlowFieldObstacles();
        if (EnableConstructionEscape)
            BeginConstructionEscapeForOverlappingHeroes();
    }

    protected override CreaturePropertyManager CreateCreaturePropertyManager()
    {
        return new CreaturePropertyManager(GetBuildingPropertyConfigValue);
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
        float r = targetComp.AlertRadius;
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
        UnregisterOutlineRenderers();
        UnregisterFlowFieldObstacles();
        InGameDataModel.UnregisterBuilding(this);

        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        ResetCombatRuntimeState();

        CurrentStronghold = null;
        OwnerFactionID = 0;
        HasPermanentNoAttackCapability = false;
        IsGameEndConditionBuilding = false;
        IsNavigationStaticBaked = false;
        EnableConstructionEscape = false;
        _lv0InvincibleByBuff = false;
        _phaseProtectionByBuff = false;
        _healthBarSuppressedByBuff = false;
        _stealthHealthBarSuppressed = false;
        _stealthMinimapHidden = false;
        _permanentStealthVisibility = false;
        ClearArmyCardProperties();
        _extraProps = null; // 仅清字段引用，中央字典里的对象保留给同 id 的下次 Show
        buildingData = null;
        BuildingInstanceId = null;
        base.OnHide(isShutdown, userData);
    }

    internal void RefreshFlowFieldObstacles()
    {
        UnregisterFlowFieldObstacles();
        RegisterFlowFieldObstacles();
    }

    private void RegisterFlowFieldObstacles()
    {
        if (IsNavigationStaticBaked)
        {
            return;
        }

        if (!GroupMoveManager.HasInstance)
        {
            Log.Error("BuildingEntity.RegisterFlowFieldObstacles failed: GroupMoveManager is not available. building={0} instance={1}", CharacterKey, BuildingInstanceId);
            return;
        }

        if (_registeredFlowObstacleIds.Count > 0)
            throw new System.InvalidOperationException($"BuildingEntity.RegisterFlowFieldObstacles failed: stale obstacle ids. building={CharacterKey} count={_registeredFlowObstacleIds.Count}.");

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            int obstacleId = GroupMoveManager.Instance.RegisterColliderObstacle(collider);
            _registeredFlowObstacleIds.Add(obstacleId);

            if (GameDebugSettings.IsEnabled(DebugCategory.Move))
            {
                Bounds bounds = GroupMoveManager.ResolveColliderWorldBounds(collider);
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[BuildingFlowObstacle] collider building={0} instance={1} name={2} path={3} type={4} id={5} layer={6} tag={7} enabled={8} trigger={9} active={10} center={11} size={12}",
                    CharacterKey,
                    BuildingInstanceId,
                    collider.gameObject.name,
                    BuildHierarchyPath(collider.transform),
                    collider.GetType().Name,
                    obstacleId,
                    collider.gameObject.layer,
                    collider.tag,
                    collider.enabled,
                    collider.isTrigger,
                    collider.gameObject.activeInHierarchy,
                    bounds.center,
                    bounds.size);
            }
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[BuildingFlowObstacle] register building={0} instance={1} colliders={2} registered={3} pos={4}",
                CharacterKey,
                BuildingInstanceId,
                colliders.Length,
                _registeredFlowObstacleIds.Count,
                Position);
        }
    }

    private void BeginConstructionEscapeForOverlappingHeroes()
    {
        IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
        if (allEntities == null)
            throw new System.InvalidOperationException($"BuildingEntity.BeginConstructionEscapeForOverlappingHeroes failed: EntityRegistry.AllEntities is null. building={CharacterKey} instance={BuildingInstanceId}.");

        int playerHeroCount = 0;
        int activatedCount = 0;
        for (int i = 0; i < allEntities.Count; i++)
        {
            if (allEntities[i] is not HeroEntity hero || hero.Side != SideType.PlayerSide)
                continue;

            playerHeroCount++;
            if (hero.TryBeginConstructionEscape(this))
                activatedCount++;
        }

        Debug.Log(
            $"[BuildingConstructionEscape] evaluate building={CharacterKey} instance={BuildingInstanceId} " +
            $"position={Position} playerHeroes={playerHeroCount} activated={activatedCount}");
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "null";

        Stack<string> parts = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private void UnregisterFlowFieldObstacles()
    {
        if (_registeredFlowObstacleIds.Count == 0)
            return;

        if (GroupMoveManager.HasInstance)
        {
            for (int i = 0; i < _registeredFlowObstacleIds.Count; i++)
                GroupMoveManager.Instance.UnregisterObstacle(_registeredFlowObstacleIds[i]);
        }

        if (GameDebugSettings.IsEnabled(DebugCategory.Move))
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[BuildingFlowObstacle] unregister building={0} instance={1} registered={2} pos={3}",
                CharacterKey,
                BuildingInstanceId,
                _registeredFlowObstacleIds.Count,
                Position);
        }

        _registeredFlowObstacleIds.Clear();
    }

    public void SetStronghold(Stronghold stronghold, bool triggerFactionChangedEvent = true)
    {
        int oldFactionId = OwnerFactionID;
        CurrentStronghold = stronghold;
        OwnerFactionID = stronghold != null ? stronghold.OwnerFactionId : 0;
        SyncSideFromFaction();
        _minimapReportComponent?.SetSide(Side);
        RefreshLv0PhaseVisibility();
        RefreshPermanentStealthVisibility();

        if (oldFactionId != OwnerFactionID)
        {
            RefreshInteractionHostForCurrentOwnership();
            if (triggerFactionChangedEvent)
                GF.Event.Fire(this, EntityFactionChangedEventArgs.Create(Id, oldFactionId, OwnerFactionID, BuildingInstanceId));
        }
    }

    protected override void SetUpMAComp(object userData)
    {
        var noMoveComp = new NoMoveComp();
        SetMoveComp(noMoveComp);
        noMoveComp.Init(this);

        ITargetingComp targetingComp = CreateBuildingTargetingComp();
        SetTargetingComp(targetingComp);
        targetingComp.Init(this);

        SetWeaponComp(new WeaponComp(CreateBuildingWeaponData().ToWeapon($"{CharacterKey}_Weapon1", CreaturePropertyManager?.propertyManager)));

        _directAtkComp = new DirectAtkComp();
        SetAtkComp(_directAtkComp);
        _directAtkComp.Init(this);
    }

    private ITargetingComp CreateBuildingTargetingComp()
    {
        if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Pharmacy))
        {
            return new HealTargetingComp
            {
                AggroRange = 6f,
                ForgetRange = 8f,
                FollowSearchRange = 0f,
                AlertRadius = 12f
            };
        }

        if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.MeatRack))
        {
            return new MeatRackTargetingComp
            {
                AggroRange = 6f,
                ForgetRange = 8f,
                FollowSearchRange = 0f,
                AlertRadius = 12f
            };
        }

        if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Monitor))
        {
            return new MonitorTargetingComp(MonitorWeaponEffect.ResolveFacingConeAngle(this))
            {
                AggroRange = 6f,
                ForgetRange = 8f,
                FollowSearchRange = 0f,
                AlertRadius = 12f
            };
        }

        if (BuildingAbilityIds.IsBuilding(buildingData, BuildingAbilityIds.Restroom))
            return new NoTargetingComp();

        return new CharacterTargetingComp
        {
            AggroRange = 6f,
            ForgetRange = 8f,
            FollowSearchRange = 0f,
            EnableAggroFallback = false, // 建筑不需要"视线外仇恨"，原地反击就好
            AlertRadius = 12f            // 建筑被打/扫到敌人时，召唤 12m 内友军反击
        };
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
        if (damage <= Fix64.Zero)
            return;

        if (this.HasInvincibleBuff() || _isDisabled || !Alive)
            return;

        Fix64 before = HealthValue;
        Fix64 max = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 finalDamage = CalculateIncomingDamage(damage, modType);
        if (finalDamage <= Fix64.Zero)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[BuildingDamage] {CharacterKey} raw={damage} def={GetCurrentDefense()} final=0 hp={before}/{max} attacker={attacker?.CharacterKey}");
            return;
        }

        TriggerHitAnimation();

        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(-finalDamage), true);

        Fix64 cur = HealthValue;
        NotifyDamageTakenForOutOfCombat();

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)cur, (float)max, (float)(-finalDamage)));

        ShowDamagePopText(finalDamage);

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[BuildingDamage] {CharacterKey} raw={damage} def={GetCurrentDefense()} final={finalDamage} hp={before}->{cur}/{max} attacker={attacker?.CharacterKey}");

        // 受击告警：通知 TargetingComp，让它广播 attacker 给周围友军（建筑自身 EnableAggroFallback=false 不记 _lastAttacker，但仍广播）
        if (attacker != null && targetComp != null)
            targetComp.NotifyDamageTaken(attacker);

        if (cur <= Fix64.Zero)
        {
            EnterDisabledState(attacker);
        }
    }

    private Fix64 CalculateIncomingDamage(Fix64 damage, HealthModifyType modType)
    {
        if (modType != HealthModifyType.reduce)
            return damage;

        Fix64 defense = GetCurrentDefense();
        if (defense <= Fix64.Zero)
            return damage;

        return Fix64.Max(Fix64.Zero, damage - defense);
    }

    private Fix64 GetCurrentDefense()
    {
        if (CreaturePropertyManager == null)
            return Fix64.Zero;

        Fix64 defense = CreaturePropertyManager.GetProperty(CreatureMainProperty.Def);
        return defense > Fix64.Zero ? defense : Fix64.Zero;
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

        if (buildingData != null && buildingData.Lv == 0)
            GameEntry.GetComponent<BuildManager>().ConfigureConstructInteractionOptions(this, host);
        else
            GameEntry.GetComponent<TechManager>().ConfigureTechInteractionOptions(this, host);
    }

    private void EnsureLv0InvincibleBuff()
    {
        if (BuffComp == null)
            return;

        var buffData = BuffData.Create(
            id: Lv0InvincibleBuffId,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new BuildingLv0InvincibleBuff() });

        BuffComp.AddBuff(buffData, this);
    }

    private void EnsurePhaseProtectionBuff()
    {
        if (BuffComp == null)
            return;

        var buffData = BuffData.Create(
            id: PhaseGuardBuffId,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new BuildingPhaseGuardBuff() });

        BuffComp.AddBuff(buffData, this);
    }

    private void ApplyBuildingInitialBuffs()
    {
        if (BuffComp == null)
            return;

        List<BuffData> buffs = BuildingInitialBuffFactory.CreateInitialBuffs(buildingData);
        List<BuffData> runtimeTechBuffs = GameEntry.GetComponent<GlobalBuffManager>()?.GetRuntimeBuffsForBuildingEntity(this);
        if (buffs == null && runtimeTechBuffs == null)
            return;

        if (buffs != null)
        {
            for (int i = 0; i < buffs.Count; i++)
                BuffComp.AddBuff(buffs[i], this);
        }

        if (runtimeTechBuffs != null)
        {
            for (int i = 0; i < runtimeTechBuffs.Count; i++)
                BuffComp.AddBuff(runtimeTechBuffs[i], this);
        }
    }

    public void SetPhaseProtectionByBuff(bool enabled)
    {
        if (_phaseProtectionByBuff == enabled)
            return;

        _phaseProtectionByBuff = enabled;
        if (_phaseProtectionByBuff && targetComp != null)
            targetComp.CurrentTarget = null;
    }

    public void SetLv0InvincibleByBuff(bool enabled)
    {
        if (_lv0InvincibleByBuff == enabled)
            return;

        _lv0InvincibleByBuff = enabled;
        if (_lv0InvincibleByBuff && targetComp != null)
            targetComp.CurrentTarget = null;
    }

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

    private void ConfigureCombatByBuildingData()
    {
        if (_directAtkComp == null)
            return;

        WeaponData weaponData = CreateBuildingWeaponData();

        _directAtkComp.UpdateWeaponData(weaponData);

        if (targetComp != null)
        {
            float aggroRange = Mathf.Max(DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range) + 1.5f, 4f);
            targetComp.AggroRange = aggroRange;
            targetComp.ForgetRange = aggroRange + 2f;
            targetComp.FollowSearchRange = 0f;
        }
    }

    private void ApplyBuildingPropertyOverrides()
    {
        if (CreaturePropertyManager == null)
            return;

        Fix64 hp = buildingData != null && buildingData.HP > Fix64.Zero ? buildingData.HP : (Fix64)120;
        if (TutorialManager.IsCurrentLevelTutorial())
            hp *= (Fix64)0.5f;

        Fix64 def = buildingData != null ? buildingData.Def : (Fix64)10;
        if (def < Fix64.Zero)
            def = Fix64.Zero;

        // 建筑表数值应作为“目标值”而非“叠加值”，否则会把模板属性再加一遍导致血量过高。
        Fix64 currentHpMax = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 currentDef = CreaturePropertyManager.GetProperty(CreatureMainProperty.Def);

        Fix64 hpDelta = hp - currentHpMax;
        Fix64 defDelta = def - currentDef;

        CreaturePropertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Health,
            PropertyDirectAdditiveModifier.Create(hpDelta),
            true);

        CreaturePropertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Def,
            PropertyDirectAdditiveModifier.Create(defDelta),
            true);

        Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 currentHealth = HealthValue;
        Fix64 delta = maxHealth - currentHealth;
        if (Fix64.Abs(delta) > (Fix64)0.001f)
        {
            CreaturePropertyManager.ModifyCurrentProperty(
                CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(delta),
                true);
        }
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

    private WeaponData CreatePlaceholderWeaponData()
    {
        return new WeaponData(
            WeaponType.Melee,
            Fix64.Zero,
            PlaceholderAttackInterval,
            PlaceholderAttackRange,
            Fix64.Zero,
            PlaceholderWindUp,
            PlaceholderWindDown,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            new Fix64[0]);
    }

    private WeaponData CreateBuildingWeaponData()
    {
        if (buildingData != null && buildingData.Weapon != null && buildingData.Weapon.Atk > Fix64.Zero)
            return buildingData.Weapon;

        return CreatePlaceholderWeaponData();
    }

    private void InitializeAttackCapabilityFlags()
    {
        HasPermanentNoAttackCapability = buildingData != null && (buildingData.Weapon == null || buildingData.Weapon.Atk <= Fix64.Zero);
    }

    private void EnterDisabledState(IEntityContext attacker)
    {
        if (_isDisabled)
            return;

        _isDisabled = true;
        GF.Event.Fire(this, BuildingDisabledStateChangedEventArgs.Create(Id, BuildingInstanceId, true));
        Alive = false;

        Fix64 curHealth = HealthValue;
        if (curHealth < Fix64.Zero)
        {
            CreaturePropertyManager.ModifyCurrentProperty(
                CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(-curHealth),
                true);
        }

        if (targetComp != null)
            targetComp.CurrentTarget = null;

        LockCombatCapabilities();
        OnDead();
        SetDisabledVisual(true);

        LevelEntity.NotifyBuildingDisabled(this, attacker);
    }

    public void RestoreToFullHealthAndEnable()
    {
        ResetCombatRuntimeState();
        Alive = true;

        if (CreaturePropertyManager == null)
            return;

        Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 currentHealth = HealthValue;
        Fix64 delta = maxHealth - currentHealth;
        if (Fix64.Abs(delta) > (Fix64)0.001f)
        {
            CreaturePropertyManager.ModifyCurrentProperty(
                CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(delta),
                true);
        }

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)HealthValue, (float)maxHealth, (float)delta));
    }

    private void ResetCombatRuntimeState()
    {
        bool wasDisabled = _isDisabled;
        _isDisabled = false;
        if (wasDisabled)
        {
            GF.Event.Fire(this, BuildingDisabledStateChangedEventArgs.Create(Id, BuildingInstanceId, false));
        }

        UnlockCombatCapabilities();

        if (targetComp != null)
            targetComp.CurrentTarget = null;

        SetDisabledVisual(false);
    }

    private void LockCombatCapabilities()
    {
        if (_combatLocked)
            return;

        if (atkComp != null)
            LockComp(atkComp, DisabledStateLocker);

        if (targetComp != null)
            LockComp(targetComp, DisabledStateLocker);

        _combatLocked = true;
    }

    private void UnlockCombatCapabilities()
    {
        if (!_combatLocked)
            return;

        if (atkComp != null)
            ResumeComp(atkComp, DisabledStateLocker);

        if (targetComp != null)
            ResumeComp(targetComp, DisabledStateLocker);

        _combatLocked = false;
    }

    private void SyncSideFromFaction()
    {
        int teamId = EntityCombatTeamHelper.ResolveTeamIdByFaction(OwnerFactionID);
        Side = EntitySideHelper.ToSide(teamId);
    }

    private void TriggerHitAnimation()
    {
        if (animator == null)
            return;

        foreach (var p in animator.parameters)
        {
            if (p.name == "GetHit" && p.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger("GetHit");
                break;
            }
        }
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
    /// 收获资源
    /// </summary>
    public void Harvest()
    {
        if (buildingData == null || !Alive || IsDisabled)
            return;

        // 只处理资源建筑
        if (buildingData.Type == BuilType.Prod)
        {
            int production = GetProduction();
            if (production > 0)
            {
                int actualProduction = InGameDataModel.ConsumeProductionBuildingCoinReserves(BuildingInstanceId, production);
                if (actualProduction <= 0)
                    return;

                // 增加资源（受点位存量限制）
                InGameDataModel.TryModifyValue(IngameValueType.Coin, actualProduction, true);
                Debug.Log($"Building {buildingData.Identifier} harvested {actualProduction} coins (raw={production})");
            }
        }
    }

    /// <summary>
    /// 当前建筑的日产出值（仅对 Prod 类型建筑有意义）。
    /// = BuildingData.Production (base) + BuildingExtraProps.Production (tech extra) + BuildingExtraProps.DynamicProduction (动态产出)
    /// </summary>
    public int GetProduction()
    {
        if (buildingData == null)
            return 0;

        Fix64 baseValue = (Fix64)buildingData.Production;
        Fix64 techExtra = _extraProps != null ? _extraProps.Production : Fix64.Zero;
        Fix64 dynamicExtra = _extraProps != null ? _extraProps.DynamicProduction : Fix64.Zero;
        Fix64 cap = _extraProps != null && _extraProps.ProductionCap > Fix64.Zero
            ? _extraProps.ProductionCap
            : (Fix64)int.MaxValue;

        // 应用上限限制
        Fix64 total = baseValue + techExtra + dynamicExtra;
        Fix64 cappedTotal = total > cap ? cap : total;

        return Mathf.Max(0, (int)cappedTotal);
    }

    /// <summary>
    /// 设置动态产出值
    /// </summary>
    public void SetDynamicProduction(int value)
    {
        if (_extraProps != null)
        {
            _extraProps.DynamicProduction = (Fix64)value;
        }
    }

    public int GetBaseProductionWithTechExtra()
    {
        if (buildingData == null)
            return 0;

        Fix64 baseValue = (Fix64)buildingData.Production;
        Fix64 techExtra = _extraProps != null ? _extraProps.Production : Fix64.Zero;
        return Mathf.Max(0, (int)(baseValue + techExtra));
    }

    /// <summary>
    /// 设置产出上限
    /// </summary>
    public void SetProductionCap(int value)
    {
        if (_extraProps != null)
        {
            _extraProps.ProductionCap = (Fix64)value;
        }
    }

    public int GetStoredProduction()
    {
        return _extraProps != null ? Mathf.Max(0, _extraProps.StoredProduction) : 0;
    }

    public void SetStoredProduction(int value)
    {
        if (_extraProps != null)
            _extraProps.StoredProduction = Mathf.Max(0, value);
    }

    public int GetProductionTraitFirstDay()
    {
        return _extraProps != null ? Mathf.Max(0, _extraProps.ProductionTraitFirstDay) : 0;
    }

    public void SetProductionTraitFirstDay(int day)
    {
        if (_extraProps != null)
            _extraProps.ProductionTraitFirstDay = Mathf.Max(0, day);
    }

    public void NotifyProductionGranted(int rawProduction, int actualProduction)
    {
        if (BuffComp is AAAGame.Scripts.BuffSystem.CharacterBuffComp buffComp)
        {
            foreach (BuffCallback module in buffComp.EnumerateAllModules())
                module.OnBuildingProductionGranted(this, rawProduction, actualProduction);
        }
    }

    /// <summary>
    /// 设置产出计算类型
    /// </summary>
    public void SetProductionType(ProductionType productionType)
    {
        if (_extraProps != null)
        {
            _extraProps.ProductionType = productionType;
        }
    }

    /// <summary>
    /// 设置条件计数
    /// </summary>
    public void SetConditionCount(int count)
    {
        if (_extraProps != null)
        {
            _extraProps.ConditionCount = count;
        }
    }

    public int GetArmyForce()
    {
        Fix64 baseValue = (Fix64)GetArmyForceWithoutRuntimeRules();
        Fix64 runtimeBonus = GameEntry.GetComponent<GlobalBuffManager>()?.CalculateRuntimeArmyForceBonus(this) ?? Fix64.Zero;
        return Mathf.Max(0, (int)(baseValue + runtimeBonus));
    }

    public int GetArmyForceWithoutRuntimeRules()
    {
        if (_armyForceProperty == null)
            return 0;

        Fix64 baseValue = _armyForceProperty.GetValue();
        Fix64 extra = _extraProps != null ? _extraProps.ArmyForce : Fix64.Zero;
        return Mathf.Max(0, (int)(baseValue + extra));
    }

    public int GetArmySupplyPerUnit()
    {
        if (_armySupplyPerUnitProperty == null)
            return 0;

        int value = (int)_armySupplyPerUnitProperty.GetValue() + LevelTagRuntime.CalculateArmySupplyPerUnitBonus(this);
        return Mathf.Max(0, value);
    }

    public int GetArmyOccupiedSupply()
    {
        long occupied = (long)GetArmyForce() * GetArmySupplyPerUnit();
        if (occupied <= 0)
            return 0;

        return occupied >= int.MaxValue ? int.MaxValue : (int)occupied;
    }

    public void SetArmyForceBase(int value)
    {
        if (_armyForceProperty == null)
            return;

        _armyForceProperty.SetBaseValue((Fix64)Mathf.Max(0, value));
    }

    public void SetArmySupplyPerUnitBase(int value)
    {
        if (_armySupplyPerUnitProperty == null)
            return;

        _armySupplyPerUnitProperty.SetBaseValue((Fix64)Mathf.Max(0, value));
    }

    public void ModifyArmyForce(IPropertyModifier modifier, bool ifAdd = true)
    {
        if (_armyForceProperty == null || modifier == null)
            return;

        if (ifAdd)
            _armyForceProperty.AddModifier(modifier);
        else
            _armyForceProperty.RemoveModifier(modifier);
    }

    public void ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd = true)
    {
        if (_armySupplyPerUnitProperty == null || modifier == null)
            return;

        if (ifAdd)
            _armySupplyPerUnitProperty.AddModifier(modifier);
        else
            _armySupplyPerUnitProperty.RemoveModifier(modifier);
    }

    private void InitializeArmyCardProperties()
    {
        ClearArmyCardProperties();

        if (buildingData == null || buildingData.Type != BuilType.Army)
            return;

        PropertyManager propertyManager = CreaturePropertyManager.propertyManager;
        _armyForceProperty = PropertyHelper.CreateBaseProperty(ArmyForcePropertyId, propertyManager);
        _armySupplyPerUnitProperty = PropertyHelper.CreateBaseProperty(ArmySupplyPerUnitPropertyId, propertyManager);

        int initArmyForce = Mathf.Max(0, buildingData.Production);
        int initSupplyPerUnit = ResolveUnitSupplyByCharacterKey(buildingData.UnitID);
        _armyForceProperty.SetBaseValue((Fix64)initArmyForce);
        _armySupplyPerUnitProperty.SetBaseValue((Fix64)Mathf.Max(0, initSupplyPerUnit));

        _armyForceProperty.OnDirty(RaiseArmyCardPropertyChangedEvent);
        _armySupplyPerUnitProperty.OnDirty(RaiseArmyCardPropertyChangedEvent);
    }

    private void ClearArmyCardProperties()
    {
        _armyForceProperty = null;
        _armySupplyPerUnitProperty = null;
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

    private static int ResolveUnitSupplyByCharacterKey(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || GF.DataTable == null)
            return 0;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            return 0;

        var row = table.GetDataRow(r => r.CharacterKey == characterKey);
        return row != null ? Mathf.Max(0, row.Supply) : 0;
    }

    private sealed class DisabledCapabilityLocker : ICapability
    {
        public void ShutDown()
        {
        }

        public void Resume()
        {
        }
    }
}
