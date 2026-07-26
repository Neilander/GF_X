using System.Collections.Generic;
using UnityEngine;

public class HeroEntity : SoldierEntity, ICastRangePresenter
{
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
    private bool _isHidingOrShuttingDown;
    private int _nextGhostCollisionSyncFrame;
    private Transform _rangeTrans;
    private LineRenderer _rangeLineRenderer;
    private Transform _attackRangePreviewTrans;
    private LineRenderer _attackRangePreviewRenderer;
    private Fix64 _renderedAttackRange;
    private Vector3 _attackRangePreviewParentScale;
    private bool _hasRenderedAttackRange;
    private bool _presentedGhostState;

    public bool IsGhostState => LogicState != null ? LogicState.IsGhostState : _presentedGhostState;
    public bool IsHeroSoldier => true;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _rangeTrans = transform.Find("CastRange");
    }

    protected override void OnShow(object userData)
    {
        _isHidingOrShuttingDown = false;
        base.OnShow(userData);
        EnsurePlayerInteractionRuntime();
        ApplyGhostPresentation(LogicState.IsGhostState);
        SyncAttackRangePreview(true);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        _isHidingOrShuttingDown = true;
        HideCastRange();
        ClearGhostRuntimeState();
        if (_attackRangePreviewTrans != null)
            _attackRangePreviewTrans.gameObject.SetActive(false);
        _hasRenderedAttackRange = false;
        base.OnHide(isShutdown, userData);
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        TickGhostCollisionRuntime();
        if (_presentedGhostState != LogicState.IsGhostState)
            ApplyGhostPresentation(LogicState.IsGhostState);
        SyncAttackRangePreview(false);
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

    protected override void OnLogicGhostStatePresentation(bool enabled)
    {
        base.OnLogicGhostStatePresentation(enabled);
        ApplyGhostPresentation(enabled);
        if (!enabled)
            TryEnsureHealthBarVisible(LogicState.GetProperty(CreatureMainProperty.Health));
    }

    private void ApplyGhostPresentation(bool enabled)
    {
        if (_presentedGhostState == enabled)
            return;
        _presentedGhostState = enabled;
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

        InteractionManager manager = interactionObject.GetComponent<InteractionManager>();
        if (manager == null)
            manager = interactionObject.AddComponent<InteractionManager>();

        if (interactionObject.GetComponent<InteractOptionTipsPresenter>() == null)
            interactionObject.AddComponent<InteractOptionTipsPresenter>();

        manager.ConfigureRuntime(
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
        if (!LogicEntityId.IsValid)
            throw new System.InvalidOperationException("HeroEntity.SetGhostGroupMoveCollisionIgnore failed: logic entity id is invalid.");

        GroupMoveManager.Instance.SetAgentIgnoreCollision(LogicEntityId.Value, ignore);
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
                IEntityContext otherLogic = allEntities[i]
                                            ?? throw new System.InvalidOperationException(
                                                $"HeroEntity.SyncGhostUnitCollisionIgnores found a null logic entity at index {i}.");
                if (otherLogic.LogicEntityId == LogicEntityId)
                    continue;
                if (otherLogic is IBuildingLogicContext building && building.BuildingData != null)
                    continue;
                if (!LogicEntityLifecycleService.TryGetBoundView(otherLogic.LogicEntityId, out MAEntity other))
                    continue;

                CharacterController otherController = other.GetComponent<CharacterController>();
                if (otherController == null)
                    continue;

                if (!other.LogicEntityId.IsValid)
                    throw new System.InvalidOperationException("HeroEntity.SyncGhostUnitCollisionIgnores failed: other logic entity id is invalid.");
                int otherId = other.LogicEntityId.Value;
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
        ApplyGhostPresentation(false);

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
        _presentedGhostState = false;
    }
}
