using System.Collections.Generic;
using UnityEngine;

public partial class SoldierEntity
{
    private const string HeroGhostBuffId = "hero_ghost_state";
    private const float GhostAlphaMultiplier = 0.35f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly ICapability GhostCapabilityLocker = new GhostStateCapabilityLocker();
    private const int GhostCollisionSyncIntervalFrames = 6;

    private readonly Dictionary<Renderer, Color> _ghostBaseColors = new Dictionary<Renderer, Color>();
    private readonly Dictionary<int, CharacterController> _ghostIgnoredUnitControllers = new Dictionary<int, CharacterController>();
    private bool _isHidingOrShuttingDown;
    private int _nextGhostCollisionSyncFrame;

    public bool IsGhostState { get; private set; }

    public override void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (this.HasInvincibleBuff())
            return;

        base.TakeDamage(damage, modType, attacker);
    }

    protected override bool TryHandleZeroHealth(IEntityContext attacker)
    {
        if (!IsHeroUnit())
            return false;

        EnterGhostState();
        return true;
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

        SetGhostVisual(enabled);
        SetGhostGroupMoveCollisionIgnore(enabled);
        SyncGhostUnitCollisionIgnores(force: true);
        AAAGame.MiniMap.FOG3.Fog3Manager.Instance?.SetEntityRevealerAllowRevealHidden(Id, !enabled);
    }

    public void RestoreFromGhostState()
    {
        SetGhostStateByBuff(false);
        Alive = true;

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

        TryEnsureHealthBarVisible(maxHealth);
    }

    private void TryEnsureHealthBarVisible(Fix64 maxHealth)
    {
        if (!CanRecreateHealthBarOnGhostRestore())
        {
            Debug.Log($"[SoldierEntity.Ghost] Skip health bar recreate on ghost restore. entityId={Id}, isHiding={_isHidingOrShuttingDown}, available={Available}, active={isActiveAndEnabled}, sceneLoaded={gameObject.scene.isLoaded}");
            return;
        }

        if (GameObject.Find($"HealthBar_{Id}") != null)
            return;

        bool isFriendly = Side == SideType.PlayerSide;
        Debug.Log($"[SoldierEntity.Ghost] Recreate health bar on ghost restore. entityId={Id}");
        HealthBarComp.Create(Id, transform, (float)HealthValue, (float)maxHealth, isFriendly);
    }

    private bool CanRecreateHealthBarOnGhostRestore()
    {
        if (!Application.isPlaying)
            return false;

        if (_isHidingOrShuttingDown)
            return false;

        if (!Available)
            return false;

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return false;

        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            return false;

        if (GF.Entity == null || !GF.Entity.HasEntity(Id))
            return false;

        return true;
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

    private bool IsHeroUnit()
    {
        if (CharacterData?.UnitTags != null)
        {
            for (int i = 0; i < CharacterData.UnitTags.Length; i++)
            {
                if (CharacterData.UnitTags[i] == UnitTag.Hero)
                    return true;
            }
        }

        return CharacterKey == UnitType.Unit_Hero.ToString();
    }

    private void SetGhostVisual(bool enabled)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material material = renderer.material;
            if (material == null)
                continue;

            if (!_ghostBaseColors.TryGetValue(renderer, out Color baseColor))
            {
                baseColor = GetMaterialColor(material);
                _ghostBaseColors[renderer] = baseColor;
            }

            SetMaterialColor(material, enabled ? ToGhostColor(baseColor) : baseColor);
        }
    }

    private static Color GetMaterialColor(Material material)
    {
        if (material.HasProperty(BaseColorId))
            return material.GetColor(BaseColorId);

        if (material.HasProperty(ColorId))
            return material.GetColor(ColorId);

        return Color.white;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);

        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, color);
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

        GroupMoveManager.Instance.Coordinator.SetAgentIgnoreCollision(GetInstanceID(), ignore);
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

                bool keepIgnored = otherController.TryGetComponent<SoldierEntity>(out SoldierEntity otherSoldier)
                    && otherSoldier.IsGhostState;
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
        {
            Debug.Log($"[SoldierEntity.Ghost] Sync unit collision ignores. entityId={Id}, ghost={IsGhostState}, added={added}, restored={restored}, tracked={_ghostIgnoredUnitControllers.Count}");
        }
    }

    private void ClearGhostRuntimeState()
    {
        SetGhostStateByBuff(false);
        IsGhostState = false;
        _ghostBaseColors.Clear();
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