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

    private const string DefaultPropertyTemplateId = "Unit_Coder";
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
    public bool IsHealthBarSuppressedByBuff => _healthBarSuppressedByBuff;
    public bool HasPermanentNoAttackCapability { get; private set; }

    private BuildingAtkComp _buildingAtkComp;
    private bool _isDisabled;
    private bool _lv0InvincibleByBuff;
    private bool _phaseProtectionByBuff;
    private bool _healthBarSuppressedByBuff;
    private bool _combatLocked;
    private static readonly ICapability DisabledStateLocker = new DisabledCapabilityLocker();
    private BaseValueProperty _armyForceProperty;
    private BaseValueProperty _armySupplyPerUnitProperty;
    private MinimapReportComponent _minimapReportComponent;
    private BuildingExtraProps _extraProps; // 引用自 GlobalBuffManager 的中央字典，升级场景同 id 共享同对象

    protected override void RefreshCharacterData(object userData)
    {
        var entityParams = userData as EntityParams;
        buildingData = entityParams?.Get(P_BuildingData) as BuildingData;
        BuildingInstanceId = entityParams != null && entityParams.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;
        IsGameEndConditionBuilding = entityParams != null
            && entityParams.TryGet<VarBoolean>(P_IsGameEndConditionBuilding, out var isGameEndConditionBuilding)
            && isGameEndConditionBuilding;

        CharacterKey = ResolvePropertyTemplateId(buildingData);
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

 
        // Property init uses a fallback template; combat identity should stay on the building id.
        if (buildingData != null)
            CharacterKey = buildingData.Identifier;

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
        EnsureLv0InvincibleBuff();
        EnsurePhaseProtectionBuff();

        // 拿到 extra 属性引用（升级场景同 BuildingInstanceId 共享同一对象，extra 数据自然延续）
        _extraProps = GameEntry.GetComponent<GlobalBuffManager>()?.GetOrCreateExtraProps(BuildingInstanceId);

        // 初始化生产建筑的动态产出机制
        Debug.Log($"[BuildingEntity] 准备调用InitializeDynamicProductionMechanism，buildingData={buildingData?.Identifier}");
        InitializeDynamicProductionMechanism();
        Debug.Log($"[BuildingEntity] InitializeDynamicProductionMechanism调用完成");

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }

        // 建筑出现后请求重新烘焙 NavMesh（延迟合并，批量建造只烘焙一次）
        LevelEntity.RequestRebakeNavMesh();
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
        UnregisterOutlineRenderers();
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
        _lv0InvincibleByBuff = false;
        _phaseProtectionByBuff = false;
        _healthBarSuppressedByBuff = false;
        ClearArmyCardProperties();
        _extraProps = null; // 仅清字段引用，中央字典里的对象保留给同 id 的下次 Show
        buildingData = null;
        BuildingInstanceId = null;
        base.OnHide(isShutdown, userData);
    }

    public void SetStronghold(Stronghold stronghold)
    {
        int oldFactionId = OwnerFactionID;
        CurrentStronghold = stronghold;
        OwnerFactionID = stronghold != null ? stronghold.OwnerFactionId : 0;
        SyncSideFromFaction();
        _minimapReportComponent?.SetSide(Side);
        RefreshLv0PhaseVisibility();

        if (oldFactionId != OwnerFactionID)
        {
            RefreshInteractionHostForCurrentOwnership();
            GF.Event.Fire(this, EntityFactionChangedEventArgs.Create(Id, oldFactionId, OwnerFactionID, BuildingInstanceId));
        }
    }

    protected override void SetUpMAComp(object userData)
    {
        var noMoveComp = new NoMoveComp();
        SetMoveComp(noMoveComp);
        noMoveComp.Init(this);

        var targetingComp = new CharacterTargetingComp
        {
            AggroRange = 6f,
            ForgetRange = 8f,
            FollowSearchRange = 0f,
            EnableAggroFallback = false, // 建筑不需要"视线外仇恨"，原地反击就好
            AlertRadius = 12f            // 建筑被打/扫到敌人时，召唤 12m 内友军反击
        };
        SetTargetingComp(targetingComp);
        targetingComp.Init(this);

        _buildingAtkComp = new BuildingAtkComp(CreatePlaceholderWeaponData());
        SetAtkComp(_buildingAtkComp);
        _buildingAtkComp.Init(this);
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
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

        bool visible = !IsLv0Building()
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
        if (_buildingAtkComp == null)
            return;

        Fix64 damage = Fix64.Zero;
        if (!HasPermanentNoAttackCapability && buildingData != null)
            damage = Fix64.Max(Fix64.Zero, buildingData.Atk);

        var weaponData = new WeaponData(
            WeaponType.Melee,
            damage,
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

        _buildingAtkComp.UpdateWeaponData(weaponData);

        if (targetComp is CharacterTargetingComp targetingComp)
        {
            float aggroRange = Mathf.Max(DistanceUnitConverter.ConvertToWorldFloat(weaponData.Range) + 1.5f, 4f);
            targetingComp.AggroRange = aggroRange;
            targetingComp.ForgetRange = aggroRange + 2f;
            targetingComp.FollowSearchRange = 0f;
        }
    }

    private void ApplyBuildingPropertyOverrides()
    {
        if (CreaturePropertyManager == null)
            return;

        Fix64 hp = buildingData != null && buildingData.HP > Fix64.Zero ? buildingData.HP : (Fix64)120;
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

    private void InitializeAttackCapabilityFlags()
    {
        HasPermanentNoAttackCapability = buildingData != null && buildingData.Atk <= Fix64.Zero;
    }

    private string ResolvePropertyTemplateId(BuildingData data)
    {
        if (data != null && HasCharacterDataRow(data.Identifier))
            return data.Identifier;

        return DefaultPropertyTemplateId;
    }

    private bool HasCharacterDataRow(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId) || GF.DataTable == null)
            return false;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        if (table == null)
            return false;

        var rows = table.GetDataRows(r => r.CharacterKey == characterId);
        return rows != null && rows.Length > 0;
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
        if (!EnableDamagePopText)
            return;

        Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), ((float)damage).ToString(), endPos, DamageTextType.Normal);
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
                // 增加资源
                InGameDataModel.TryModifyValue(IngameValueType.Coin, production, true);
                Debug.Log($"Building {buildingData.Identifier} harvested {production} coins");
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
        Fix64 cap = _extraProps != null ? _extraProps.ProductionCap : (Fix64)int.MaxValue;

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

        int value = (int)_armySupplyPerUnitProperty.GetValue();
        return Mathf.Max(0, value);
    }

    public int GetArmyOccupiedSupply()
    {
        long occupied = (long)GetArmyForce() * GetArmySupplyPerUnit();
        if (occupied <= 0)
            return 0;

        return occupied >= int.MaxValue ? int.MaxValue : (int)occupied;
    }

    /// <summary>
    /// 初始化生产建筑的动态产出机制
    /// </summary>
    private void InitializeDynamicProductionMechanism()
    {
        Debug.Log($"[BuildingEntity] InitializeDynamicProductionMechanism开始执行");

        if (buildingData == null)
        {
            Debug.Log($"[BuildingEntity] 初始化失败: buildingData为null");
            return;
        }

        if (buildingData.Type != BuilType.Prod)
        {
            Debug.Log($"[BuildingEntity] 初始化失败: {buildingData.Identifier}不是生产建筑 (Type={buildingData.Type}, 期望={BuilType.Prod})");
            return;
        }

        Debug.Log($"[BuildingEntity] 初始化生产建筑动态产出机制: {buildingData.Identifier}, Arche={buildingData.Arche}");

        // 根据建筑类型应用对应的动态产出机制
        switch (buildingData.Arche)
        {
            case Archetype.Delivery:
                if (buildingData.Identifier.Contains("ParcelLocker"))
                {
                    Debug.Log($"[BuildingEntity] 检测到快递柜，准备应用动态产出Buff");
                    ApplyDynamicProductionBuff<TechBuilParcelLockerDynamicEffectSO>();
                }
                else
                {
                    Debug.Log($"[BuildingEntity] Archetype.Delivery但Identifier不包含ParcelLocker: {buildingData.Identifier}");
                }
                break;
            case Archetype.Sightseeing:
                if (buildingData.Identifier.Contains("SouvenirStand"))
                {
                    ApplyDynamicProductionBuff<TechBuilSouvenirStandDynamicEffectSO>();
                }
                break;
            case Archetype.Butchery:
                if (buildingData.Identifier.Contains("MeatStall"))
                {
                    ApplyDynamicProductionBuff<TechBuilMeatStallDynamicEffectSO>();
                }
                break;
            case Archetype.Coding:
                if (buildingData.Identifier.Contains("MiningRig"))
                {
                    ApplyDynamicProductionBuff<TechBuilMiningRigDynamicEffectSO>();
                }
                break;
        }
    }

    /// <summary>
    /// 应用动态产出Buff
    /// </summary>
    private void ApplyDynamicProductionBuff<T>() where T : TechEffectSO, new()
    {
        try
        {
            Debug.Log($"[BuildingEntity] 开始应用动态产出Buff: {buildingData.Identifier} -> {typeof(T).Name}");

            var techEffect = new T();
            var techId = $"dynamic_production_{buildingData.Identifier}";
            Debug.Log($"[BuildingEntity] 创建TechEffect和TechData: techId={techId}");

            var techData = new TechData(
                identifier: techId,
                skillID: "",
                nameKey: $"DynamicProduction_{buildingData.Identifier}",
                descKey: $"Dynamic production for {buildingData.Identifier}",
                cost: 0,
                uniqueValues: new Fix64[0],
                scopeType: TechScopeType.SelfBuil,
                unitScope: new string[0],
                tagScope: new UnitTag[0],
                archScope: new Archetype[0],
                spritePath: "",
                isStackable: true
            );

            Debug.Log($"[BuildingEntity] 调用CreateBuildingScopedBuff...");
            var buffData = techEffect.CreateBuildingScopedBuff(techData, techId);

            if (buffData != null)
            {
                Debug.Log($"[BuildingEntity] BuffData创建成功，准备注册到GlobalBuffManager");
                var globalBuffManager = GameEntry.GetComponent<GlobalBuffManager>();
                if (globalBuffManager != null)
                {
                    globalBuffManager.RegisterBuildingBuff(BuildingInstanceId, OwnerFactionID, techId, techEffect, techData);
                    Debug.Log($"[BuildingEntity] 成功应用动态产出Buff: {buildingData.Identifier} -> {typeof(T).Name}");

                    // 立即应用Buff效果到当前建筑实体
                    if (BuffComp != null)
                    {
                        BuffComp.AddBuff(buffData, this);
                        Debug.Log($"[BuildingEntity] 立即应用Buff到当前建筑实体");
                    }
                    else
                    {
                        Debug.LogError($"[BuildingEntity] BuffComp为null，无法应用Buff");
                    }
                }
                else
                {
                    Debug.LogError($"[BuildingEntity] GlobalBuffManager为null");
                }
            }
            else
            {
                Debug.LogError($"[BuildingEntity] BuffData创建失败");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BuildingEntity] 应用动态产出Buff失败: {buildingData.Identifier} -> {typeof(T).Name}: {ex.Message}");
            Debug.LogError($"[BuildingEntity] 异常堆栈: {ex.StackTrace}");
        }
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
