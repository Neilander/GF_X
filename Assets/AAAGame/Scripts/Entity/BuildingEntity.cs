using GameFramework;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public partial class BuildingEntity : MAEntity
{
    public const string P_BuildingData = "BuildingData";
    public const string P_BuildingInstanceId = "BuildingInstanceId";

    private const string DefaultPropertyTemplateId = "Unit_Coder";
    private static readonly Fix64 PlaceholderAttackInterval = (Fix64)1.6f;
    private static readonly Fix64 PlaceholderAttackRange = (Fix64)650;
    private static readonly Fix64 PlaceholderWindUp = (Fix64)0.35f;
    private static readonly Fix64 PlaceholderWindDown = (Fix64)0.35f;
    private const string Lv0InvincibleBuffId = "building_lv0_invincible";
    private const string PhaseGuardBuffId = "building_phase_guard";

    public BuildingData buildingData;
    public int OwnerFactionID { get; set; }
    public string BuildingInstanceId { get; private set; }
    public Stronghold CurrentStronghold { get; private set; }
    public bool HasUpgrade => BuildManager.HasUpgrade(this);
    public bool IsDisabled => _isDisabled;
    public bool IsLv0Invincible => _lv0InvincibleByBuff;
    public bool IsPhaseProtected => _phaseProtectionByBuff;
    public bool HasPermanentNoAttackCapability { get; private set; }

    private BuildingAtkComp _buildingAtkComp;
    private bool _isDisabled;
    private bool _lv0InvincibleByBuff;
    private bool _phaseProtectionByBuff;
    private bool _combatLocked;
    private static readonly ICapability DisabledStateLocker = new DisabledCapabilityLocker();

    private void SetupBuildingData(object userData)
    {
        var entityParams = userData as EntityParams;
        buildingData = entityParams?.Get(P_BuildingData) as BuildingData;
        BuildingInstanceId = entityParams != null && entityParams.TryGet<VarString>(P_BuildingInstanceId, out var instanceId) ? instanceId : null;

        ReferenceId = ResolvePropertyTemplateId(buildingData);
        SetBrain(new BuildingAIBrain());

        if (string.IsNullOrWhiteSpace(BuildingInstanceId))
            BuildingInstanceId = System.Guid.NewGuid().ToString("N");
    }

    protected override void OnShow(object userData)
    {
        SetupBuildingData(userData);
        base.OnShow(userData);

        InitializeAttackCapabilityFlags();
        ResetCombatRuntimeState();
        ApplyBuildingPropertyOverrides();
        ConfigureCombatByBuildingData();
        SyncHurtBoxToBuildingBounds();

        LevelEntity.RegisterBuildingToStronghold(this);
        SyncSideFromFaction();
        EnsureLv0InvincibleBuff();
        EnsurePhaseProtectionBuff();

        if (HasUpgrade)
        {
            EnsureInteractionHost();
        }

        // 建筑出现后请求重新烘焙 NavMesh（延迟合并，批量建造只烘焙一次）
        LevelEntity.RequestRebakeNavMesh();
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        LevelEntity.UnregisterBuildingFromStronghold(this);

        // 对象池安全：清理运行时引用，避免下次复用时指向旧数据
        var host = GetComponent<InteractionHost>();
        if (host != null)
            host.ResetOptions();

        ResetCombatRuntimeState();

        CurrentStronghold = null;
        OwnerFactionID = 0;
        HasPermanentNoAttackCapability = false;
        _lv0InvincibleByBuff = false;
        _phaseProtectionByBuff = false;
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

        if (oldFactionId != OwnerFactionID)
        {
            RefreshInteractionHostForCurrentOwnership();
            GF.Event.Fire(this, EntityFactionChangedEventArgs.Create(Id, oldFactionId, OwnerFactionID));
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
            FollowSearchRange = 0f
        };
        SetTargetingComp(targetingComp);
        targetingComp.Init(this);

        _buildingAtkComp = new BuildingAtkComp(CreatePlaceholderWeaponData());
        SetAtkComp(_buildingAtkComp);
        _buildingAtkComp.Init(this);
    }

    protected override void SetUpHurtBox()
    {
        base.SetUpHurtBox();
        SyncHurtBoxToBuildingBounds();
    }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (damage <= Fix64.Zero)
            return;

        if (IsLv0Invincible || IsPhaseProtected || _isDisabled || !Alive)
            return;

        TriggerHitAnimation();

        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(-damage), true);

        Fix64 cur = HealthValue;
        Fix64 max = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)cur, (float)max, (float)(-damage)));

        ShowDamagePopText(damage);

        if (cur <= Fix64.Zero)
        {
            EnterDisabledState(attacker);
        }
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

        BuildManager.ConfigureBuildInteractionOptions(this, host);
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
            PropertyAdditiveModifier.Create(hpDelta),
            true);

        CreaturePropertyManager.ModifyMainPropertyValueBuff(
            CreatureMainProperty.Def,
            PropertyAdditiveModifier.Create(defDelta),
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
        _isDisabled = false;
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

    private void ShowDamagePopText(Fix64 damage)
    {
        Vector3 startPos = transform.position + new Vector3(0, 1.0f, 0);
        Vector3 endPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, UnityEngine.Random.Range(-0.5f, 0.5f));
        GF.Entity.ShowPopText(EntityParams.Create(startPos, Vector3.zero, Vector3.one), ((float)damage).ToString(), endPos, DamageTextType.Normal);
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
