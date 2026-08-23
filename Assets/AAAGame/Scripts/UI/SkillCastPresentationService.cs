using System;
using System.Collections.Generic;
using UnityEngine;

public static class SkillCastPresentationService
{
    private const int AimTimeScaleUnits = 2000;

    private static int s_Generation;
    private static int s_SlotIndex = -1;
    private static IEntityContext s_Caster;
    private static MAEntity s_CasterView;
    private static ICastRangePresenter s_RangePresenter;
    private static SkillCastPreviewDescriptor s_Descriptor;
    private static CylinderTargetSelector s_Selector;
    private static FixVector2 s_LastWorldPosition;
    private static bool s_HasWorldPosition;
    private static bool s_OwnsBulletTime;

    public static bool IsAiming { get; private set; }
    public static event Action Changed;

    public static bool CanRequestSkillCast(int slotIndex)
    {
        if (!TryResolveCaster(out IEntityContext caster, out ISkillCastPreviewProvider provider))
            return false;
        if (!LogicSkillCastCommandService.IsActive
            || LogicSkillCastCommandService.HasPendingForCaster(caster.LogicEntityId))
        {
            return false;
        }
        return provider.CanRequestSkillCast(slotIndex);
    }

    public static bool RequestCastAtScreen(int slotIndex, InputManager inputManager, Vector2 screenPosition)
    {
        if (inputManager == null)
            throw new ArgumentNullException(nameof(inputManager));
        if (!TryResolveRequest(slotIndex, out IEntityContext caster, out _, out SkillCastPreviewDescriptor descriptor))
            return false;

        if (descriptor.RequiresWorldPosition)
        {
            if (!inputManager.TryGetSelectionWorldPosition(screenPosition, out FixVector2 worldPosition))
                return false;
            Schedule(caster, slotIndex, worldPosition);
            return true;
        }

        Schedule(caster, slotIndex);
        return true;
    }

    public static bool TryBeginAim(int slotIndex, InputManager inputManager, Vector2 screenPosition)
    {
        if (inputManager == null)
            throw new ArgumentNullException(nameof(inputManager));
        if (IsAiming)
            return false;
        if (!TryResolveRequest(slotIndex, out IEntityContext caster, out _, out SkillCastPreviewDescriptor descriptor)
            || !descriptor.RequiresWorldPosition)
        {
            return false;
        }
        if (!LogicEntityLifecycleService.TryGetBoundView(caster.LogicEntityId, out MAEntity view))
            throw new InvalidOperationException($"Skill aim requires a bound caster view. caster={caster.LogicEntityId.Value}.");
        if (view is not ICastRangePresenter presenter)
        {
            throw new InvalidOperationException(
                $"Skill aim caster view does not implement ICastRangePresenter. caster={caster.LogicEntityId.Value}, view={view.GetType().FullName}.");
        }

        BeginAimPresentation(slotIndex, caster, view, presenter, descriptor);
        UpdateAim(inputManager, screenPosition);
        ShowSelector(s_Generation);
        return true;
    }

    public static void UpdateActiveAim(InputManager inputManager)
    {
        if (!IsAiming)
            return;
        if (inputManager == null)
            throw new ArgumentNullException(nameof(inputManager));
        UpdateAim(inputManager, inputManager.GetPointerScreenPosition());
    }

    public static void UpdateAim(InputManager inputManager, Vector2 screenPosition)
    {
        if (!IsAiming)
            return;
        if (inputManager == null)
            throw new ArgumentNullException(nameof(inputManager));
        if (s_Caster == null || !s_Caster.IsRegisteredInLogicWorld())
        {
            Cancel();
            return;
        }
        if (!ActiveSkillCastEligibility.CanCast(s_Caster))
        {
            Cancel();
            return;
        }
        if (!inputManager.TryGetSelectionWorldPosition(screenPosition, out FixVector2 requested))
            return;

        SetAimWorldPosition(requested);
    }

    private static void BeginAimPresentation(
        int slotIndex,
        IEntityContext caster,
        MAEntity view,
        ICastRangePresenter presenter,
        SkillCastPreviewDescriptor descriptor)
    {
        s_Generation = checked(s_Generation + 1);
        s_SlotIndex = slotIndex;
        s_Caster = caster;
        s_CasterView = view;
        s_RangePresenter = presenter;
        s_Descriptor = descriptor;
        s_Selector = null;
        s_LastWorldPosition = caster.PositionFixed;
        s_HasWorldPosition = false;
        IsAiming = true;
        LogicTimeControlService.SetBulletTimeScale(
            LogicTimeControlSources.SkillAimBulletTime,
            AimTimeScaleUnits);
        s_OwnsBulletTime = true;
        Changed?.Invoke();
        presenter.ShowCastRange((float)descriptor.CastRadius);
    }

    private static void SetAimWorldPosition(FixVector2 requested)
    {
        s_LastWorldPosition = requested;
        s_HasWorldPosition = true;
        FixVector2 previewPosition = PositionSelectAction.ClampToRadius(
            s_Caster.PositionFixed,
            requested,
            s_Descriptor.CastRadius);
        s_Selector?.SetPosition(ToPresentationPosition(previewPosition));
    }

    public static bool CommitAim()
    {
        if (!IsAiming)
            return false;
        if (!s_HasWorldPosition)
            return false;
        if (!ActiveSkillCastEligibility.CanCast(s_Caster))
        {
            Cleanup();
            return false;
        }

        IEntityContext caster = s_Caster;
        int slotIndex = s_SlotIndex;
        FixVector2 requested = s_LastWorldPosition;
        Cleanup();
        Schedule(caster, slotIndex, requested);
        return true;
    }

    public static void Cancel()
    {
        if (!IsAiming && s_Selector == null && s_RangePresenter == null && !s_OwnsBulletTime)
            return;
        Cleanup();
    }

    private static bool TryResolveRequest(
        int slotIndex,
        out IEntityContext caster,
        out ISkillCastPreviewProvider provider,
        out SkillCastPreviewDescriptor descriptor)
    {
        descriptor = SkillCastPreviewDescriptor.Instant;
        if (!TryResolveCaster(out caster, out provider)
            || !provider.CanRequestSkillCast(slotIndex)
            || !LogicSkillCastCommandService.IsActive
            || LogicSkillCastCommandService.HasPendingForCaster(caster.LogicEntityId))
        {
            return false;
        }

        descriptor = provider.GetRequiredSkillCastPreview(slotIndex);
        return true;
    }

    private static bool TryResolveCaster(
        out IEntityContext caster,
        out ISkillCastPreviewProvider provider)
    {
        caster = EntityRegistry.Player;
        if (caster == null || !caster.LogicEntityId.IsValid)
        {
            provider = null;
            return false;
        }
        if (!ActiveSkillCastEligibility.CanCast(caster))
        {
            provider = null;
            return false;
        }
        if (caster is not ISkillCompHost host || host.skillComp is not ISkillCastPreviewProvider resolved)
            throw new InvalidOperationException($"Player has no skill cast preview provider. caster={caster.LogicEntityId.Value}.");
        provider = resolved;
        return true;
    }

    private static void Schedule(IEntityContext caster, int slotIndex, FixVector2 requestedWorldPosition)
    {
        if (caster == null || !caster.LogicEntityId.IsValid)
            throw new InvalidOperationException("Skill cast scheduling requires a valid caster.");
        LogicSkillCastCommandService.Submit(
            caster.LogicEntityId,
            slotIndex,
            requestedWorldPosition);
        Changed?.Invoke();
    }

    private static void Schedule(IEntityContext caster, int slotIndex)
    {
        if (caster == null || !caster.LogicEntityId.IsValid)
            throw new InvalidOperationException("Skill cast scheduling requires a valid caster.");
        LogicSkillCastCommandService.Submit(caster.LogicEntityId, slotIndex);
        Changed?.Invoke();
    }

    private static void ShowSelector(int generation)
    {
        var selectorParams = EntityParams.Create();
        selectorParams.OnShowCallback = logic =>
        {
            CylinderTargetSelector selector = logic as CylinderTargetSelector
                ?? throw new InvalidOperationException($"Skill aim expected CylinderTargetSelector, actual={logic?.GetType().FullName ?? "null"}.");
            if (!IsAiming || generation != s_Generation)
            {
                GF.Entity.HideEntity(selector.GetEntityID());
                return;
            }

            selector.Activate(new List<ISelectable>(), s_Caster.Side);
            selector.ChangeRange(s_Descriptor.SelectorScale);
            FixVector2 previewPosition = PositionSelectAction.ClampToRadius(
                s_Caster.PositionFixed,
                s_LastWorldPosition,
                s_Descriptor.CastRadius);
            selector.SetPosition(ToPresentationPosition(previewPosition));
            s_Selector = selector;
        };

        GF.Entity.ShowEntity<CylinderTargetSelector>(
            s_Descriptor.SelectorPrefabName,
            Const.EntityGroup.Default,
            selectorParams);
    }

    private static Vector3 ToPresentationPosition(FixVector2 position)
    {
        float y = s_CasterView != null ? s_CasterView.transform.position.y : 0f;
        return new Vector3((float)position.x, y, (float)position.y);
    }

    private static void Cleanup()
    {
        IsAiming = false;
        s_Generation = checked(s_Generation + 1);
        if (s_OwnsBulletTime)
        {
            LogicTimeControlService.RemoveBulletTimeScale(LogicTimeControlSources.SkillAimBulletTime);
            s_OwnsBulletTime = false;
        }
        if (s_Selector != null)
            GF.Entity.HideEntity(s_Selector.GetEntityID());
        s_RangePresenter?.HideCastRange();
        s_SlotIndex = -1;
        s_Caster = null;
        s_CasterView = null;
        s_RangePresenter = null;
        s_Descriptor = SkillCastPreviewDescriptor.Instant;
        s_Selector = null;
        s_LastWorldPosition = FixVector2.Zero;
        s_HasWorldPosition = false;
        Changed?.Invoke();
    }
}
