using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class ProjectilePresentationService
{
    private const int MaxDispatchesPerRenderFrame = 8;

    private sealed class Request
    {
        public ulong ProjectileId;
        public LogicEntityId AttackerId;
        public IEntityContext Attacker;
        public IEntityContext Target;
        public WeaponData WeaponData;
        public RangedWeaponSO Weapon;
        public DirectAtkComp WeaponProvider;
        public EntityParams EntityParams;
    }

    private static readonly List<Request> s_Pending = new List<Request>();
    private static readonly Dictionary<int, Request> s_InFlight = new Dictionary<int, Request>();
    private static readonly List<int> s_InFlightViewIds = new List<int>();
    private static string s_Failure;

    public static bool IsActive { get; private set; }
    public static int PendingCount => s_Pending.Count;
    public static int InFlightCount => s_InFlight.Count;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("ProjectilePresentationService.BeginTimeline failed: service is already active.");
        if (GF.Event == null)
            throw new InvalidOperationException("ProjectilePresentationService.BeginTimeline failed: GF.Event is null.");

        s_Pending.Clear();
        s_InFlight.Clear();
        s_Failure = null;
        GF.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        GF.Event.Subscribe(ShowEntityFailureEventArgs.EventId, OnShowEntityFailure);
        IsActive = true;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        CancelAll();
        GF.Event.Unsubscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        GF.Event.Unsubscribe(ShowEntityFailureEventArgs.EventId, OnShowEntityFailure);
        IsActive = false;
        s_Failure = null;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        CancelAll();
        s_Failure = null;
    }

    public static void Enqueue(
        ulong projectileId,
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        RangedWeaponSO weapon)
    {
        if (weapon == null)
            throw new ArgumentNullException(nameof(weapon));
        EnqueueCore(projectileId, attacker, target, weaponData, weapon, null);
    }

    public static void Publish(
        ulong projectileId,
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        RangedWeaponSO weapon)
    {
        if (!IsActive)
            return;
        Enqueue(projectileId, attacker, target, weaponData, weapon);
    }

    public static void Enqueue(
        ulong projectileId,
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        DirectAtkComp weaponProvider)
    {
        if (weaponProvider == null)
            throw new ArgumentNullException(nameof(weaponProvider));
        EnqueueCore(projectileId, attacker, target, weaponData, null, weaponProvider);
    }

    public static void Publish(
        ulong projectileId,
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        DirectAtkComp weaponProvider)
    {
        if (!IsActive)
            return;
        Enqueue(projectileId, attacker, target, weaponData, weaponProvider);
    }

    private static void EnqueueCore(
        ulong projectileId,
        IEntityContext attacker,
        IEntityContext target,
        WeaponData weaponData,
        RangedWeaponSO weapon,
        DirectAtkComp weaponProvider)
    {
        EnsureActive();
        if (projectileId == 0)
            throw new ArgumentOutOfRangeException(nameof(projectileId));
        if (attacker == null)
            throw new ArgumentNullException(nameof(attacker));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (weaponData == null)
            throw new ArgumentNullException(nameof(weaponData));
        if (weapon == null && weaponProvider == null)
            throw new ArgumentException("Projectile presentation requires a weapon or provider.");
        if (!attacker.LogicEntityId.IsValid)
            throw new InvalidOperationException("Projectile presentation requires a valid attacker logic id.");
        if (!LogicProjectileService.TryGetPresentationState(projectileId, out _))
            throw new InvalidOperationException($"Projectile presentation cannot queue unknown projectile {projectileId}.");
        for (int i = 0; i < s_Pending.Count; i++)
        {
            if (s_Pending[i].ProjectileId == projectileId)
                throw new InvalidOperationException($"Projectile presentation {projectileId} is already pending.");
        }

        s_Pending.Add(new Request
        {
            ProjectileId = projectileId,
            AttackerId = attacker.LogicEntityId,
            Attacker = attacker,
            Target = target,
            WeaponData = weaponData,
            Weapon = weapon,
            WeaponProvider = weaponProvider,
        });
    }

    public static void UpdateRenderFrame()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("Projectile presentation cannot update during a logic frame.");
        if (GF.Entity == null)
            throw new InvalidOperationException("ProjectilePresentationService.UpdateRenderFrame failed: GF.Entity is null.");
        if (s_Failure != null)
            throw new InvalidOperationException(s_Failure);

        RemoveCompletedPendingRequests();
        int dispatched = 0;
        for (int i = 0; i < s_Pending.Count && dispatched < MaxDispatchesPerRenderFrame;)
        {
            Request request = s_Pending[i];
            if (!TryResolveWeapon(request))
            {
                i++;
                continue;
            }
            if (!LogicEntityLifecycleService.TryGetBoundView(request.AttackerId, out MAEntity attackerView))
            {
                i++;
                continue;
            }

            Transform origin = attackerView.PresentationBindings.RequireProjectileOrigin();
            LogicProjectileService.ReserveView(request.ProjectileId);
            request.EntityParams = request.Weapon.CreateProjectilePresentationParams(
                request.Attacker,
                request.Target,
                request.WeaponData,
                request.ProjectileId,
                origin.position);
            int expectedViewId = request.EntityParams.Id;
            s_InFlight.Add(expectedViewId, request);
            s_Pending.RemoveAt(i);
            try
            {
                int actualViewId = GF.Entity.ShowEntity<Projectile>(
                    request.Weapon.ProjectileName,
                    Const.EntityGroup.Bullet,
                    request.EntityParams);
                if (actualViewId != expectedViewId)
                {
                    throw new InvalidOperationException(
                        $"Projectile presentation returned a different view id. expected={expectedViewId}, actual={actualViewId}.");
                }
                request.Weapon.PresentLaunchFeedback(request.Attacker, origin.position);
            }
            catch
            {
                s_InFlight.Remove(expectedViewId);
                CancelInFlightRequest(expectedViewId, request);
                throw;
            }

            dispatched++;
        }
    }

    private static bool TryResolveWeapon(Request request)
    {
        if (request.Weapon != null)
            return true;
        if (request.WeaponProvider == null)
            throw new InvalidOperationException($"Projectile presentation {request.ProjectileId} has no weapon provider.");
        if (!string.IsNullOrWhiteSpace(request.WeaponProvider.WeaponLoadFailure))
        {
            throw new InvalidOperationException(
                $"Projectile presentation {request.ProjectileId} cannot resolve its weapon: {request.WeaponProvider.WeaponLoadFailure}");
        }
        request.WeaponProvider.EnsureWeaponPresentationLoaded();
        if (request.WeaponProvider.WeaponSO == null)
            return false;
        if (request.WeaponProvider.WeaponSO is not RangedWeaponSO rangedWeapon)
        {
            throw new InvalidOperationException(
                $"Projectile presentation {request.ProjectileId} requires RangedWeaponSO, actual={request.WeaponProvider.WeaponSO.GetType().FullName}.");
        }

        request.Weapon = rangedWeapon;
        return true;
    }

#if UNITY_EDITOR
    public static void BeginTimelineForTests()
    {
        if (IsActive)
            throw new InvalidOperationException("ProjectilePresentationService.BeginTimelineForTests failed: service is already active.");
        if (!LogicProjectileService.IsActive)
            throw new InvalidOperationException("ProjectilePresentationService.BeginTimelineForTests failed: logic projectile service is not active.");
        s_Pending.Clear();
        s_InFlight.Clear();
        s_Failure = null;
        IsActive = true;
    }

    public static void PruneCompletedForTests()
    {
        EnsureActive();
        RemoveCompletedPendingRequests();
    }

    public static void EndTimelineForTests()
    {
        EnsureActive();
        if (s_InFlight.Count != 0)
            throw new InvalidOperationException("ProjectilePresentationService.EndTimelineForTests failed: requests are in flight.");
        s_Pending.Clear();
        s_Failure = null;
        IsActive = false;
    }
#endif

    private static void RemoveCompletedPendingRequests()
    {
        for (int i = s_Pending.Count - 1; i >= 0; i--)
        {
            if (!LogicProjectileService.TryGetPresentationState(
                    s_Pending[i].ProjectileId,
                    out LogicProjectileViewState state)
                || state.Completed)
            {
                s_Pending.RemoveAt(i);
            }
        }
    }

    private static void CancelAll()
    {
        s_Pending.Clear();
        s_InFlightViewIds.Clear();
        foreach (int viewId in s_InFlight.Keys)
            s_InFlightViewIds.Add(viewId);
        for (int i = 0; i < s_InFlightViewIds.Count; i++)
        {
            int viewId = s_InFlightViewIds[i];
            Request request = s_InFlight[viewId];
            CancelInFlightRequest(viewId, request);
        }
        s_InFlight.Clear();
        s_InFlightViewIds.Clear();
    }

    private static void CancelInFlightRequest(int viewId, Request request)
    {
        LogicProjectileViewBindingState bindingState =
            LogicProjectileService.GetRequiredViewBindingState(request.ProjectileId);
        switch (bindingState)
        {
            case LogicProjectileViewBindingState.Reserved:
                LogicProjectileService.CancelViewReservation(request.ProjectileId);
                GF.Entity.HideEntity(viewId);
                break;
            case LogicProjectileViewBindingState.Bound:
                GF.Entity.HideEntity(viewId);
                break;
            default:
                throw new InvalidOperationException(
                    $"ProjectilePresentationService cannot cancel in-flight view {viewId}: projectile {request.ProjectileId} binding state is {bindingState}.");
        }

        ReferencePool.Release(request.EntityParams);
    }

    private static void OnShowEntitySuccess(object sender, GameEventArgs gameEventArgs)
    {
        if (gameEventArgs is not ShowEntitySuccessEventArgs args || args.Entity == null)
            throw new InvalidOperationException("ProjectilePresentationService received an invalid show-success event.");
        if (!s_InFlight.TryGetValue(args.Entity.Id, out Request request))
            return;
        if (!ReferenceEquals(args.UserData, request.EntityParams))
        {
            s_Failure = $"Projectile presentation show-success userData mismatch. view={args.Entity.Id}.";
            return;
        }

        s_InFlight.Remove(args.Entity.Id);
    }

    private static void OnShowEntityFailure(object sender, GameEventArgs gameEventArgs)
    {
        if (gameEventArgs is not ShowEntityFailureEventArgs args)
            throw new InvalidOperationException("ProjectilePresentationService received an invalid show-failure event.");
        if (!s_InFlight.Remove(args.EntityId, out Request request))
            return;

        LogicProjectileService.CancelViewReservation(request.ProjectileId);
        s_Failure =
            $"Projectile presentation failed to show entity. view={args.EntityId}, asset={args.EntityAssetName}, error={args.ErrorMessage}";
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("ProjectilePresentationService operation failed: service is not active.");
        if (!LogicProjectileService.IsActive)
            throw new InvalidOperationException("ProjectilePresentationService operation failed: logic projectile service is not active.");
    }
}
