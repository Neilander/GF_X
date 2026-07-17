using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;

public class HeroEntity : SoldierEntity, ISkillCompHost, ICastRangePresenter
{
    private const string HeroOutOfCombatSpeedBuffId = "hero_out_of_combat_speed_x2";
    private const float HeroOutOfCombatSpeedDelay = 2f;
    private const float HeroOutOfCombatSpeedRampDuration = 1f;
    private static readonly Fix64 HeroOutOfCombatSpeedBuffPercent = Fix64.One;

    private const string HeroGhostBuffId = "hero_ghost_state";
    private const float GhostAlphaMultiplier = 0.6f;
    private const string UnitOutlineShaderName = "Hidden/AAAGame/UnitOutline";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly ICapability GhostCapabilityLocker = new GhostStateCapabilityLocker();
    private const int GhostCollisionSyncIntervalFrames = 6;
    private const int CastRangeSegments = 96;
    private const string AttackRangePreviewNodeName = "AttackRangePreview";
    private const int AttackRangePreviewSegments = 96;
    private const float AttackRangePreviewHeight = 0.04f;
    private const float AttackRangePreviewWidth = 0.04f;
    private const string PlayerInteractionNodeName = "InteractCollider";
    private const float PlayerInteractionRange = 2.7f;
    private const float PlayerInteractionPadding = 0.7f;

    private readonly Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, Material[]> _ghostMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<int, CharacterController> _ghostIgnoredUnitControllers = new Dictionary<int, CharacterController>();
    private readonly Dictionary<int, Collider> _constructionEscapeIgnoredColliders = new Dictionary<int, Collider>();
    private bool _heroOutOfCombatSpeedFirstApplyPending;
    private bool _heroOutOfCombatSpeedInitialGraceActive;
    private bool _isHidingOrShuttingDown;
    private int _nextGhostCollisionSyncFrame;
    private Transform _rangeTrans;
    private LineRenderer _rangeLineRenderer;
    private Transform _attackRangePreviewTrans;
    private LineRenderer _attackRangePreviewRenderer;
    private Fix64 _renderedAttackRange;
    private Vector3 _attackRangePreviewParentScale;
    private bool _hasRenderedAttackRange;

    public ISkillComp skillComp { get; private set; }
    public bool IsGhostState { get; private set; }
    public bool IsHeroSoldier => true;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _rangeTrans = transform.Find("CastRange");
    }

    protected override void OnShow(object userData)
    {
        _isHidingOrShuttingDown = false;
        _heroOutOfCombatSpeedFirstApplyPending = true;
        _heroOutOfCombatSpeedInitialGraceActive = false;

        base.OnShow(userData);
        GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnHeroPhaseChanged);
        GF.Event.Subscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        EnsurePlayerInteractionRuntime();
        EnsureHeroSkillRuntime();
        SyncAttackRangePreview(true);
        SyncHeroOutOfCombatSpeedBuff();
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        _isHidingOrShuttingDown = true;
        GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnHeroPhaseChanged);
        GF.Event.Unsubscribe(SkillChangedEventArgs.EventId, OnSkillChanged);
        CancelRunningSkills();
        skillComp?.ShutDown();
        skillComp = null;
        ClearConstructionEscapeRuntimeState();
        ClearGhostRuntimeState();
        if (_attackRangePreviewTrans != null)
            _attackRangePreviewTrans.gameObject.SetActive(false);
        _hasRenderedAttackRange = false;
        base.OnHide(isShutdown, userData);
    }

    protected override void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        base.OnLogicFrameUpdate(deltaTime);
        SyncHeroOutOfCombatSpeedBuff();
        TickGhostCollisionRuntime();
        TickConstructionEscapeRuntime();

        if (CanRun(skillComp))
            skillComp.Skill(deltaTime);
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        SyncAttackRangePreview(false);
    }

    protected override void OnOutOfCombatStateRefreshed()
    {
        base.OnOutOfCombatStateRefreshed();
        SyncHeroOutOfCombatSpeedBuff();
    }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (this.HasInvincibleBuff())
            return;

        Fix64 before = HealthValue;
        base.TakeDamage(damage, modType, attacker);
        if (HealthValue < before)
            RemoveHeroOutOfCombatSpeedBuff();
    }

    protected override bool TryHandleZeroHealth(IEntityContext attacker)
    {
        if (LevelTagRuntime.TryConsumeHeroRevive(this))
        {
            ReviveHeroToFullHealth();
            return true;
        }

        EnterGhostState();
        return true;
    }

    public void SetSkillComp(ISkillComp newSkillComp) => skillComp = newSkillComp;

    public void CancelRunningSkills()
    {
        skillComp?.CancelSkills();
        HideCastRange();
        SkillCastState.Reset();
    }

    public void ShowCastRange(float radius)
    {
        if (radius <= 0.01f)
        {
            if (_rangeTrans != null)
                _rangeTrans.gameObject.SetActive(false);
            return;
        }

        EnsureCastRange();
        _rangeTrans.localScale = Vector3.one;
        _rangeTrans.gameObject.SetActive(true);
        UpdateCastRangeCircle(radius);
    }

    public void HideCastRange()
    {
        ShowCastRange(0f);
    }

    private void EnsureHeroSkillRuntime()
    {
        if (skillComp == null)
            FactoryHelper.CreateSkillComp(UtilityBuiltin.AssetsPath.GetSkillFactoryPath("PlayerSkillFactory"), this);
        else
            skillComp.OnSkillChanged();
    }

    private void OnHeroPhaseChanged(object sender, GameEventArgs e)
    {
        if (e is not IngamePhaseChangedEventArgs args)
            return;

        if (args.OldPhase == args.NewPhase)
            return;

        if (InGameDataModel.IsBuildPhase(args.NewPhase))
        {
            CancelRunningSkills();
            GF.DataModel.GetDataModel<InputModel>()?.ClearSkillRequests();
        }

        if (Alive)
            RestoreHealthToFull();

        SyncHeroOutOfCombatSpeedBuff();
    }

    private void OnSkillChanged(object sender, GameEventArgs e)
    {
        skillComp?.OnSkillChanged();
    }

    private void RestoreHealthToFull()
    {
        Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        Fix64 delta = maxHealth - HealthValue;
        if (delta <= Fix64.Zero)
            return;

        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(delta),
            true);

        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, (float)maxHealth, (float)maxHealth, (float)delta));
    }

    private void ReviveHeroToFullHealth()
    {
        RestoreHealthToFull();
        Alive = true;
    }

    public void SetGhostStateByBuff(bool enabled)
    {
        if (IsGhostState == enabled)
            return;

        IsGhostState = enabled;
        Alive = true;

        if (targetComp != null)
            targetComp.CurrentTarget = null;

        if (enabled)
        {
            LockComp(atkComp, GhostCapabilityLocker);
            LockComp(targetComp, GhostCapabilityLocker);
        }
        else
        {
            ResumeComp(atkComp, GhostCapabilityLocker);
            ResumeComp(targetComp, GhostCapabilityLocker);
        }

        if (enabled)
            CancelHitFlashVisual();

        SetGhostVisual(enabled);
        SetGhostGroupMoveCollisionIgnore(enabled);
        SyncGhostUnitCollisionIgnores(force: true);
        AAAGame.MiniMap.FOG3.Fog3Manager fogManager = AAAGame.MiniMap.FOG3.Fog3Manager.Instance;
        if (fogManager == null)
            return;

        fogManager.SetEntityRevealerAllowRevealHidden(Id, !enabled);
        fogManager.RefreshRevealHiddenByHeroGhostState();
    }

    public void RestoreFromGhostState()
    {
        SetGhostStateByBuff(false);
        Alive = true;
        RestoreHealthToFull();
        TryEnsureHealthBarVisible(CreaturePropertyManager.GetProperty(CreatureMainProperty.Health));
    }

    private void EnterGhostState()
    {
        ClampHealthToZero();

        if (BuffComp.HasBuff(HeroGhostBuffId))
            return;

        BuffData buffData = BuffData.Create(
            id: HeroGhostBuffId,
            duration: float.MaxValue,
            isForever: true,
            maxStack: 1,
            modules: new List<BuffCallback> { new HeroGhostBuff() });

        BuffComp.AddBuff(buffData, this);
    }

    private void ClampHealthToZero()
    {
        Fix64 currentHealth = HealthValue;
        if (currentHealth >= Fix64.Zero)
            return;

        Fix64 delta = -currentHealth;
        CreaturePropertyManager.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(delta),
            true);

        Fix64 maxHealth = CreaturePropertyManager.GetProperty(CreatureMainProperty.Health);
        GF.Event.Fire(this, CreatureHealthChangedEventArgs.Create(Id, 0f, (float)maxHealth, (float)delta));
    }

    private void TryEnsureHealthBarVisible(Fix64 maxHealth)
    {
        if (!CanRecreateHealthBarOnGhostRestore())
        {
            Debug.Log($"[HeroEntity] Skip health bar recreate on ghost restore. entityId={Id}, isHiding={_isHidingOrShuttingDown}, available={Available}, active={isActiveAndEnabled}, sceneLoaded={gameObject.scene.isLoaded}");
            return;
        }

        if (GameObject.Find($"HealthBar_{Id}") != null)
            return;

        HealthBarComp.Create(Id, transform, (float)HealthValue, (float)maxHealth, Side == SideType.PlayerSide);
    }

    private bool CanRecreateHealthBarOnGhostRestore()
    {
        if (!Application.isPlaying || _isHidingOrShuttingDown || !Available)
            return false;

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return false;

        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            return false;

        return GF.Entity != null && GF.Entity.HasEntity(Id);
    }

    private void SyncHeroOutOfCombatSpeedBuff()
    {
        if (BuffComp == null || CreaturePropertyManager == null)
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

    private void EnsureCastRange()
    {
        if (_rangeTrans != null)
            return;

        GameObject rangeObject = new GameObject("CastRange");
        _rangeTrans = rangeObject.transform;
        _rangeTrans.SetParent(transform, false);
        _rangeTrans.localPosition = Vector3.zero;
        _rangeTrans.localRotation = Quaternion.identity;

        _rangeLineRenderer = rangeObject.AddComponent<LineRenderer>();
        _rangeLineRenderer.useWorldSpace = false;
        _rangeLineRenderer.loop = true;
        _rangeLineRenderer.positionCount = CastRangeSegments;
        _rangeLineRenderer.widthMultiplier = 0.08f;
        _rangeLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _rangeLineRenderer.startColor = new Color(0.37f, 0.5f, 1f, 0.8f);
        _rangeLineRenderer.endColor = _rangeLineRenderer.startColor;
    }

    private void SyncAttackRangePreview(bool logState)
    {
        if (weaponComp == null || weaponComp.Data == null)
            throw new System.InvalidOperationException($"Hero attack range preview requires weapon data. entityId={Id}, characterKey={CharacterKey}.");

        Fix64 attackRange = weaponComp.AttackRange;
        if (attackRange <= Fix64.Zero)
            throw new System.InvalidOperationException($"Hero attack range preview requires a positive range. entityId={Id}, characterKey={CharacterKey}, range={attackRange}.");

        EnsureAttackRangePreview();

        Vector3 parentScale = transform.lossyScale;
        if (Mathf.Abs(parentScale.x) <= 0.0001f || Mathf.Abs(parentScale.y) <= 0.0001f || Mathf.Abs(parentScale.z) <= 0.0001f)
            throw new System.InvalidOperationException($"Hero attack range preview cannot compensate a zero transform scale. entityId={Id}, characterKey={CharacterKey}, scale={parentScale}.");

        bool scaleChanged = (_attackRangePreviewParentScale - parentScale).sqrMagnitude > 0.000001f;
        if (!_hasRenderedAttackRange || _renderedAttackRange != attackRange || scaleChanged)
        {
            _attackRangePreviewTrans.localScale = new Vector3(
                1f / parentScale.x,
                1f / parentScale.y,
                1f / parentScale.z);
            UpdateAttackRangePreviewCircle((float)attackRange);
            _renderedAttackRange = attackRange;
            _attackRangePreviewParentScale = parentScale;
            _hasRenderedAttackRange = true;
        }

        if (!_attackRangePreviewTrans.gameObject.activeSelf)
            _attackRangePreviewTrans.gameObject.SetActive(true);

        if (logState)
        {
            Vector3 previewCenter = _attackRangePreviewTrans.TransformPoint(
                new Vector3(0f, AttackRangePreviewHeight, 0f));
            Vector3 previewEdge = _attackRangePreviewTrans.TransformPoint(
                _attackRangePreviewRenderer.GetPosition(0));
            float renderedRadius = Vector2.Distance(
                new Vector2(previewCenter.x, previewCenter.z),
                new Vector2(previewEdge.x, previewEdge.z));
            float centerOffset = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(previewCenter.x, previewCenter.z));
            Debug.Log(
                $"[HeroAttackRangePreview] entityId={Id}, characterKey={CharacterKey}, " +
                $"rangeWorld={(float)attackRange:F3}, renderedRadius={renderedRadius:F3}, " +
                $"centerOffset={centerOffset:F4}, center={transform.position}, parentScale={parentScale}.");
        }
    }

    private void EnsureAttackRangePreview()
    {
        if (_attackRangePreviewRenderer != null)
            return;

        GameObject previewObject = new GameObject(AttackRangePreviewNodeName);
        _attackRangePreviewTrans = previewObject.transform;
        _attackRangePreviewTrans.SetParent(transform, false);
        _attackRangePreviewTrans.localPosition = Vector3.zero;
        _attackRangePreviewTrans.localRotation = Quaternion.identity;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            throw new System.InvalidOperationException("Hero attack range preview shader 'Sprites/Default' was not found.");

        _attackRangePreviewRenderer = previewObject.AddComponent<LineRenderer>();
        _attackRangePreviewRenderer.useWorldSpace = false;
        _attackRangePreviewRenderer.loop = true;
        _attackRangePreviewRenderer.positionCount = AttackRangePreviewSegments;
        _attackRangePreviewRenderer.widthMultiplier = AttackRangePreviewWidth;
        _attackRangePreviewRenderer.alignment = LineAlignment.View;
        _attackRangePreviewRenderer.numCornerVertices = 4;
        _attackRangePreviewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _attackRangePreviewRenderer.receiveShadows = false;
        _attackRangePreviewRenderer.sortingOrder = 6;
        _attackRangePreviewRenderer.material = new Material(shader)
        {
            name = "HeroAttackRangePreview_Material"
        };
        _attackRangePreviewRenderer.startColor = new Color(1f, 1f, 1f, 0.75f);
        _attackRangePreviewRenderer.endColor = _attackRangePreviewRenderer.startColor;
    }

    private void UpdateAttackRangePreviewCircle(float radius)
    {
        for (int i = 0; i < AttackRangePreviewSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / AttackRangePreviewSegments;
            _attackRangePreviewRenderer.SetPosition(
                i,
                new Vector3(Mathf.Cos(angle) * radius, AttackRangePreviewHeight, Mathf.Sin(angle) * radius));
        }
    }

    private void UpdateCastRangeCircle(float radius)
    {
        if (_rangeLineRenderer == null)
        {
            float diameter = radius * 2f;
            _rangeTrans.localScale = new Vector3(diameter, diameter, 1f);
            return;
        }

        for (int i = 0; i < CastRangeSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / CastRangeSegments;
            _rangeLineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.03f, Mathf.Sin(angle) * radius));
        }
    }

    private void SetGhostVisual(bool enabled)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null
                || renderer is ParticleSystemRenderer
                || renderer is TrailRenderer
                || renderer == _attackRangePreviewRenderer
                || renderer == _rangeLineRenderer)
                continue;

            if (enabled)
            {
                if (!_originalMaterials.ContainsKey(renderer))
                    _originalMaterials[renderer] = renderer.sharedMaterials;

                if (!_ghostMaterials.TryGetValue(renderer, out Material[] ghostMats) || ghostMats == null)
                {
                    Material[] origMats = _originalMaterials[renderer];
                    if (origMats == null)
                        continue;

                    List<Material> ghostMatList = new List<Material>(origMats.Length);
                    for (int m = 0; m < origMats.Length; m++)
                    {
                        Material origMat = origMats[m];
                        if (origMat == null || IsUnitOutlineMaterial(origMat))
                            continue;

                        Material ghostMat = CreateGhostMaterial(origMat);
                        if (ghostMat != null)
                            ghostMatList.Add(ghostMat);
                    }

                    ghostMats = ghostMatList.ToArray();
                    _ghostMaterials[renderer] = ghostMats;
                }

                renderer.sharedMaterials = _ghostMaterials[renderer];
            }
            else if (_originalMaterials.TryGetValue(renderer, out Material[] origMats) && origMats != null)
            {
                renderer.sharedMaterials = origMats;
            }
        }
    }

    private void CancelHitFlashVisual()
    {
        AAAGame.Effect.HitFlashEffect hitFlashEffect = GetComponent<AAAGame.Effect.HitFlashEffect>();
        if (hitFlashEffect != null)
            hitFlashEffect.Cancel();
    }

    private static bool IsUnitOutlineMaterial(Material material)
    {
        return material != null
            && material.shader != null
            && material.shader.name == UnitOutlineShaderName;
    }

    private static Material CreateGhostMaterial(Material source)
    {
        Shader ghostShader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(AAAGame.Effect.EffectShaderAssetLoader.GhostShaderAssetPath);

        if (ghostShader == null)
        {
            Debug.LogError($"[HeroEntity] Shader is not ready: {AAAGame.Effect.EffectShaderAssetLoader.GhostShaderAssetPath}");
            return null;
        }

        Material ghostMat = new Material(ghostShader)
        {
            name = source.name + "_Ghost",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
        };

        Texture mainTex = GetMaterialMainTexture(source);
        if (mainTex != null)
        {
            if (ghostMat.HasProperty(BaseMapId))
                ghostMat.SetTexture(BaseMapId, mainTex);
            if (ghostMat.HasProperty(MainTexId))
                ghostMat.SetTexture(MainTexId, mainTex);
        }

        SetupTransparentMaterial(ghostMat);
        SetMaterialColor(ghostMat, ToGhostColor(GetMaterialColor(source)));
        return ghostMat;
    }

    private static Texture GetMaterialMainTexture(Material material)
    {
        if (material.HasProperty(BaseMapId))
            return material.GetTexture(BaseMapId);

        if (material.HasProperty(MainTexId))
            return material.GetTexture(MainTexId);

        return null;
    }

    private static void SetupTransparentMaterial(Material material)
    {
        if (material.HasProperty(SurfaceId))
            material.SetFloat(SurfaceId, 1f);

        if (material.HasProperty(SrcBlendId))
            material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

        if (material.HasProperty(DstBlendId))
            material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty(ZWriteId))
            material.SetFloat(ZWriteId, 0f);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty(BaseColorId))
            return material.GetColor(BaseColorId);

        if (material.HasProperty(ColorId))
            return material.GetColor(ColorId);

        if (material.HasProperty("_MainColor"))
            return material.GetColor("_MainColor");

        return Color.white;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);

        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, color);

        if (material.HasProperty("_MainColor"))
            material.SetColor("_MainColor", color);
    }

    private static Color ToGhostColor(Color source)
    {
        return new Color(
            source.r * 0.75f,
            source.g * 0.75f,
            source.b * 0.75f,
            Mathf.Clamp01(source.a * GhostAlphaMultiplier));
    }

    private void SetGhostGroupMoveCollisionIgnore(bool ignore)
    {
        if (!GroupMoveManager.HasInstance)
            return;

        GroupMoveManager.Instance.SetAgentIgnoreCollision(GetInstanceID(), ignore);
    }

    public bool TryBeginConstructionEscape(BuildingEntity building)
    {
        if (building == null)
            throw new System.InvalidOperationException("HeroEntity.TryBeginConstructionEscape failed: building is null.");

        CharacterController selfController = GetComponent<CharacterController>();
        if (selfController == null)
            throw new System.InvalidOperationException($"HeroEntity.TryBeginConstructionEscape failed: hero has no CharacterController. entityId={Id}.");

        Collider[] buildingColliders = building.GetComponentsInChildren<Collider>(true);
        int solidColliderCount = 0;
        int overlapCount = 0;
        Bounds heroBounds = selfController.bounds;
        for (int i = 0; i < buildingColliders.Length; i++)
        {
            Collider buildingCollider = buildingColliders[i];
            if (buildingCollider == null || !buildingCollider.enabled || buildingCollider.isTrigger)
                continue;

            solidColliderCount++;
            if (heroBounds.Intersects(GroupMoveManager.ResolveColliderWorldBounds(buildingCollider)))
                overlapCount++;
        }

        if (overlapCount == 0)
        {
            System.Text.StringBuilder colliderDiagnostics = new System.Text.StringBuilder();
            for (int i = 0; i < buildingColliders.Length; i++)
            {
                Collider buildingCollider = buildingColliders[i];
                if (buildingCollider == null || !buildingCollider.enabled || buildingCollider.isTrigger)
                    continue;

                colliderDiagnostics.Append(" [").Append(buildingCollider.name)
                    .Append(" bounds=").Append(GroupMoveManager.ResolveColliderWorldBounds(buildingCollider)).Append(']');
            }

            Debug.Log(
                $"[HeroConstructionEscape] skip no-overlap heroId={Id} heroPos={Position} heroBounds={heroBounds} " +
                $"buildingId={building.Id} building={building.CharacterKey} instance={building.BuildingInstanceId} " +
                $"buildingPos={building.Position} solidColliders={solidColliderCount} colliders={colliderDiagnostics}");
            return false;
        }

        int ignoredCount = 0;
        for (int i = 0; i < buildingColliders.Length; i++)
        {
            Collider buildingCollider = buildingColliders[i];
            if (buildingCollider == null || !buildingCollider.enabled || buildingCollider.isTrigger)
                continue;

            int colliderId = buildingCollider.GetInstanceID();
            if (_constructionEscapeIgnoredColliders.ContainsKey(colliderId))
                continue;

            Physics.IgnoreCollision(selfController, buildingCollider, true);
            _constructionEscapeIgnoredColliders.Add(colliderId, buildingCollider);
            ignoredCount++;
        }

        MoveExecutor executor = GetComponent<MoveExecutor>();
        if (executor == null)
            throw new System.InvalidOperationException($"HeroEntity.TryBeginConstructionEscape failed: hero has no MoveExecutor. entityId={Id}.");

        executor.EnableNavigationConstraintBypassUntilLegalPoint();
        bool legalBeforeEscape = FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
            Position,
            navAgentTypeID,
            0f,
            0f,
            out _);
        Debug.Log(
            $"[HeroConstructionEscape] begin heroId={Id} heroPos={Position} heroBounds={heroBounds} " +
            $"buildingId={building.Id} building={building.CharacterKey} instance={building.BuildingInstanceId} buildingPos={building.Position} " +
            $"solidColliders={solidColliderCount} overlaps={overlapCount} ignoredAdded={ignoredCount} ignoredTotal={_constructionEscapeIgnoredColliders.Count} " +
            $"legalBeforeEscape={legalBeforeEscape}");
        return true;
    }

    private void TickConstructionEscapeRuntime()
    {
        if (_constructionEscapeIgnoredColliders.Count == 0)
            return;

        CharacterController selfController = GetComponent<CharacterController>();
        if (selfController == null)
            throw new System.InvalidOperationException($"HeroEntity.TickConstructionEscapeRuntime failed: hero has no CharacterController. entityId={Id}.");

        Bounds heroBounds = selfController.bounds;
        foreach (Collider buildingCollider in _constructionEscapeIgnoredColliders.Values)
        {
            if (buildingCollider != null
                && buildingCollider.enabled
                && heroBounds.Intersects(GroupMoveManager.ResolveColliderWorldBounds(buildingCollider)))
                return;
        }

        int restored = RestoreConstructionEscapeCollisions(selfController);
        Debug.Log(
            $"[HeroConstructionEscape] physical escape complete heroId={Id} heroPos={Position} " +
            $"heroBounds={heroBounds} restored={restored}");
    }

    private void ClearConstructionEscapeRuntimeState()
    {
        if (_constructionEscapeIgnoredColliders.Count == 0)
            return;

        CharacterController selfController = GetComponent<CharacterController>();
        if (selfController == null)
            throw new System.InvalidOperationException($"HeroEntity.ClearConstructionEscapeRuntimeState failed: hero has no CharacterController. entityId={Id}.");

        RestoreConstructionEscapeCollisions(selfController);
    }

    private int RestoreConstructionEscapeCollisions(CharacterController selfController)
    {
        int restored = 0;
        foreach (Collider buildingCollider in _constructionEscapeIgnoredColliders.Values)
        {
            if (buildingCollider == null)
                continue;

            Physics.IgnoreCollision(selfController, buildingCollider, false);
            restored++;
        }

        _constructionEscapeIgnoredColliders.Clear();
        return restored;
    }

    private void TickGhostCollisionRuntime()
    {
        if (IsGhostState)
        {
            if (Time.frameCount < _nextGhostCollisionSyncFrame)
                return;

            _nextGhostCollisionSyncFrame = Time.frameCount + GhostCollisionSyncIntervalFrames;
            SyncGhostUnitCollisionIgnores(force: false);
            return;
        }

        if (_ghostIgnoredUnitControllers.Count == 0)
            return;

        if (Time.frameCount < _nextGhostCollisionSyncFrame)
            return;

        _nextGhostCollisionSyncFrame = Time.frameCount + GhostCollisionSyncIntervalFrames;
        SyncGhostUnitCollisionIgnores(force: false);
    }

    private void SyncGhostUnitCollisionIgnores(bool force)
    {
        CharacterController selfController = GetComponent<CharacterController>();
        if (selfController == null)
            return;

        if (_ghostIgnoredUnitControllers.Count > 0)
        {
            var staleIds = new List<int>();
            foreach (var pair in _ghostIgnoredUnitControllers)
            {
                if (pair.Value == null)
                    staleIds.Add(pair.Key);
            }

            for (int i = 0; i < staleIds.Count; i++)
                _ghostIgnoredUnitControllers.Remove(staleIds[i]);
        }

        int added = 0;
        int restored = 0;

        if (IsGhostState)
        {
            IList<IEntityContext> allEntities = EntityRegistry.AllEntities;
            for (int i = 0; i < allEntities.Count; i++)
            {
                if (allEntities[i] is not MAEntity other || other == this || other is BuildingEntity)
                    continue;

                CharacterController otherController = other.GetComponent<CharacterController>();
                if (otherController == null)
                    continue;

                int otherId = other.GetInstanceID();
                if (_ghostIgnoredUnitControllers.ContainsKey(otherId))
                    continue;

                Physics.IgnoreCollision(selfController, otherController, true);
                _ghostIgnoredUnitControllers[otherId] = otherController;
                added++;
            }
        }
        else
        {
            if (_ghostIgnoredUnitControllers.Count == 0)
                return;

            var removeIds = new List<int>();
            foreach (var pair in _ghostIgnoredUnitControllers)
            {
                CharacterController otherController = pair.Value;
                if (otherController == null)
                {
                    removeIds.Add(pair.Key);
                    continue;
                }

                bool keepIgnored = otherController.TryGetComponent<HeroEntity>(out HeroEntity otherHero)
                    && otherHero.IsGhostState;
                if (keepIgnored)
                    continue;

                Physics.IgnoreCollision(selfController, otherController, false);
                removeIds.Add(pair.Key);
                restored++;
            }

            for (int i = 0; i < removeIds.Count; i++)
                _ghostIgnoredUnitControllers.Remove(removeIds[i]);
        }

        if (added > 0 || restored > 0 || force)
            Debug.Log($"[HeroEntity] Sync unit collision ignores. entityId={Id}, ghost={IsGhostState}, added={added}, restored={restored}, tracked={_ghostIgnoredUnitControllers.Count}");
    }

    private void ClearGhostRuntimeState()
    {
        SetGhostStateByBuff(false);
        IsGhostState = false;

        foreach (var mats in _ghostMaterials.Values)
        {
            if (mats == null)
                continue;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                    Destroy(mats[i]);
            }
        }

        _ghostMaterials.Clear();
        _originalMaterials.Clear();
        _ghostIgnoredUnitControllers.Clear();
        _nextGhostCollisionSyncFrame = 0;
    }

    private sealed class GhostStateCapabilityLocker : ICapability
    {
        public void ShutDown()
        {
        }

        public void Resume()
        {
        }
    }
}
