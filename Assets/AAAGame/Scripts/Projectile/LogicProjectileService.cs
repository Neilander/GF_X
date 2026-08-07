using System;
using System.Collections.Generic;

public readonly struct LogicProjectileViewState
{
    public LogicProjectileViewState(FixVector2 position, bool completed, bool hit)
    {
        Position = position;
        Completed = completed;
        Hit = hit;
    }

    public FixVector2 Position { get; }
    public bool Completed { get; }
    public bool Hit { get; }
}

public readonly struct LogicProjectileDeterministicState
{
    public LogicProjectileDeterministicState(
        ulong id,
        LogicEntityId attackerId,
        LogicEntityId targetId,
        FixVector2 position,
        Fix64 speed)
    {
        Id = id;
        AttackerId = attackerId;
        TargetId = targetId;
        Position = position;
        Speed = speed;
    }

    public ulong Id { get; }
    public LogicEntityId AttackerId { get; }
    public LogicEntityId TargetId { get; }
    public FixVector2 Position { get; }
    public Fix64 Speed { get; }
}

public sealed class LogicProjectileSnapshot
{
    internal LogicProjectileSnapshot(
        ulong lastId,
        ulong lastCompletedFrame,
        LogicProjectileSnapshotEntry[] entries,
        ulong[] activeIds)
    {
        LastId = lastId;
        LastCompletedFrame = lastCompletedFrame;
        Entries = (LogicProjectileSnapshotEntry[])entries.Clone();
        ActiveIds = (ulong[])activeIds.Clone();
    }

    public ulong LastId { get; }
    public ulong LastCompletedFrame { get; }
    internal LogicProjectileSnapshotEntry[] Entries { get; }
    internal ulong[] ActiveIds { get; }
}

internal readonly struct LogicProjectileSnapshotEntry
{
    public LogicProjectileSnapshotEntry(
        ulong id,
        LogicEntityId attackerId,
        LogicEntityId targetId,
        WeaponData weaponData,
        FixVector2 position,
        Fix64 speed,
        bool viewReserved,
        bool viewBound,
        bool completed,
        bool hit)
    {
        Id = id;
        AttackerId = attackerId;
        TargetId = targetId;
        WeaponData = weaponData;
        Position = position;
        Speed = speed;
        ViewReserved = viewReserved;
        ViewBound = viewBound;
        Completed = completed;
        Hit = hit;
    }

    public ulong Id { get; }
    public LogicEntityId AttackerId { get; }
    public LogicEntityId TargetId { get; }
    public WeaponData WeaponData { get; }
    public FixVector2 Position { get; }
    public Fix64 Speed { get; }
    public bool ViewReserved { get; }
    public bool ViewBound { get; }
    public bool Completed { get; }
    public bool Hit { get; }
}

public static class LogicProjectileService
{
    private sealed class ProjectileState
    {
        public ulong Id;
        public IEntityContext Attacker;
        public IEntityContext Target;
        public WeaponData WeaponData;
        public FixVector2 Position;
        public Fix64 Speed;
        public bool ViewReserved;
        public bool ViewBound;
        public bool Completed;
        public bool Hit;
    }

    private static readonly Dictionary<ulong, ProjectileState> s_States = new Dictionary<ulong, ProjectileState>();
    private static readonly List<ulong> s_ActiveIds = new List<ulong>();
    private static ulong s_LastId;

    public static bool IsActive { get; private set; }
    public static ulong LastId => s_LastId;
    public static ulong LastCompletedFrame { get; private set; }
    public static int ActiveCount => s_ActiveIds.Count;
    public static int RetainedViewStateCount => s_States.Count;

    public static IReadOnlyList<LogicProjectileDeterministicState> CaptureActiveStates()
    {
        EnsureActive();
        var result = new LogicProjectileDeterministicState[s_ActiveIds.Count];
        for (int i = 0; i < s_ActiveIds.Count; i++)
        {
            ProjectileState state = s_States[s_ActiveIds[i]];
            result[i] = new LogicProjectileDeterministicState(
                state.Id,
                state.Attacker.LogicEntityId,
                state.Target.LogicEntityId,
                state.Position,
                state.Speed);
        }
        return result;
    }

    public static void WriteActiveDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        EnsureActive();

        hasher.Add(s_LastId);
        hasher.Add(s_ActiveIds.Count);
        for (int i = 0; i < s_ActiveIds.Count; i++)
        {
            ProjectileState state = s_States[s_ActiveIds[i]];
            if (state == null || state.Attacker == null || state.Target == null)
                throw new InvalidOperationException($"LogicProjectileService deterministic state is invalid. index={i}.");
            hasher.Add(state.Id);
            hasher.Add(state.Attacker.LogicEntityId.Value);
            hasher.Add(state.Target.LogicEntityId.Value);
            hasher.Add(state.Position.x.RawValue);
            hasher.Add(state.Position.y.RawValue);
            hasher.Add(state.Speed.RawValue);
        }
    }

    public static LogicProjectileSnapshot CaptureSnapshot()
    {
        EnsureActive();
        var ids = new List<ulong>(s_States.Keys);
        ids.Sort();
        var entries = new LogicProjectileSnapshotEntry[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            ProjectileState state = s_States[ids[i]];
            entries[i] = new LogicProjectileSnapshotEntry(
                state.Id,
                state.Attacker.LogicEntityId,
                state.Target.LogicEntityId,
                CloneWeaponData(state.WeaponData),
                state.Position,
                state.Speed,
                state.ViewReserved,
                state.ViewBound,
                state.Completed,
                state.Hit);
        }

        return new LogicProjectileSnapshot(
            s_LastId,
            LastCompletedFrame,
            entries,
            s_ActiveIds.ToArray());
    }

    public static void RestoreSnapshot(LogicProjectileSnapshot snapshot)
    {
        EnsureActive();
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicProjectileService.RestoreSnapshot failed: a logic frame is running.");

        var entities = new Dictionary<int, IEntityContext>();
        IList<IEntityContext> registered = EntityRegistry.AllEntities;
        for (int i = 0; i < registered.Count; i++)
            entities.Add(registered[i].LogicEntityId.Value, registered[i]);

        s_States.Clear();
        s_ActiveIds.Clear();
        for (int i = 0; i < snapshot.Entries.Length; i++)
        {
            LogicProjectileSnapshotEntry entry = snapshot.Entries[i];
            if (!entities.TryGetValue(entry.AttackerId.Value, out IEntityContext attacker))
                throw new InvalidOperationException($"LogicProjectileService.RestoreSnapshot failed: attacker {entry.AttackerId.Value} is not registered.");
            if (!entities.TryGetValue(entry.TargetId.Value, out IEntityContext target))
                throw new InvalidOperationException($"LogicProjectileService.RestoreSnapshot failed: target {entry.TargetId.Value} is not registered.");
            if (entry.Id == 0 || s_States.ContainsKey(entry.Id))
                throw new InvalidOperationException($"LogicProjectileService.RestoreSnapshot failed: invalid or duplicate projectile id {entry.Id}.");

            s_States.Add(entry.Id, new ProjectileState
            {
                Id = entry.Id,
                Attacker = attacker,
                Target = target,
                WeaponData = CloneWeaponData(entry.WeaponData),
                Position = entry.Position,
                Speed = entry.Speed,
                ViewReserved = entry.ViewReserved,
                ViewBound = entry.ViewBound,
                Completed = entry.Completed,
                Hit = entry.Hit,
            });
        }

        var activeSet = new HashSet<ulong>();
        for (int i = 0; i < snapshot.ActiveIds.Length; i++)
        {
            ulong id = snapshot.ActiveIds[i];
            if (!s_States.TryGetValue(id, out ProjectileState state))
                throw new InvalidOperationException($"LogicProjectileService.RestoreSnapshot failed: active projectile {id} is absent.");
            if (state.Completed || !activeSet.Add(id))
                throw new InvalidOperationException($"LogicProjectileService.RestoreSnapshot failed: active projectile {id} is completed or duplicated.");
            s_ActiveIds.Add(id);
        }

        s_LastId = snapshot.LastId;
        LastCompletedFrame = snapshot.LastCompletedFrame;
    }

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicProjectileService.BeginTimeline failed: service is already active.");
        ClearState();
        IsActive = true;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicProjectileService.ResetForWorldTransition failed: a logic frame is running.");
        ClearState();
    }

    public static void EndTimeline()
    {
        EnsureActive();
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new InvalidOperationException("LogicProjectileService.EndTimeline failed: a logic frame is running.");
        ClearState();
        IsActive = false;
    }

    public static ulong Submit(IEntityContext attacker, IEntityContext target, WeaponData weaponData)
    {
        EnsureActive();
        if (!LogicFrameRuntime.IsTicking || !LogicDamageEventService.IsCollecting)
            throw new InvalidOperationException("LogicProjectileService.Submit failed: projectile submission is only valid in the combat collection phase.");
        if (attacker == null)
            throw new ArgumentNullException(nameof(attacker));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (weaponData == null)
            throw new ArgumentNullException(nameof(weaponData));
        if (!attacker.LogicEntityId.IsValid || !target.LogicEntityId.IsValid)
            throw new InvalidOperationException("LogicProjectileService.Submit failed: attacker and target require valid logic ids.");

        Fix64 speed = DistanceUnitConverter.ConvertToWorld(weaponData.ProjectileSpeed);
        if (speed <= Fix64.Zero)
            throw new InvalidOperationException($"LogicProjectileService.Submit failed: projectile speed must be positive. attacker={attacker.LogicEntityId.Value}.");

        ulong id = checked(++s_LastId);
        var state = new ProjectileState
        {
            Id = id,
            Attacker = attacker,
            Target = target,
            WeaponData = weaponData,
            Position = LogicEntityFrameSnapshotService.GetRequiredPosition(attacker),
            Speed = speed,
        };
        s_States.Add(id, state);
        s_ActiveIds.Add(id);
        return id;
    }

    public static void AdvanceFrame(ulong frameId, Fix64 deltaTime)
    {
        EnsureActive();
        if (!LogicFrameRuntime.IsTicking || frameId != LogicFrameRuntime.CurrentFrame)
            throw new InvalidOperationException("LogicProjectileService.AdvanceFrame failed: frame mismatch.");
        if (!LogicDamageEventService.IsCollecting)
            throw new InvalidOperationException("LogicProjectileService.AdvanceFrame failed: damage collection is not active.");
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("LogicProjectileService.AdvanceFrame failed: deltaTime is not fixed.");

        LogicEntityFrameSnapshot snapshot = LogicEntityFrameSnapshotService.Current;
        for (int i = s_ActiveIds.Count - 1; i >= 0; i--)
        {
            ulong id = s_ActiveIds[i];
            ProjectileState state = s_States[id];
            if (!snapshot.TryGet(state.Target.LogicEntityId, out LogicEntityFrameState targetState)
                || !targetState.Alive)
            {
                Complete(state, false);
                s_ActiveIds.RemoveAt(i);
                continue;
            }

            FixVector2 aimPoint = targetState.CombatShape.ClosestPoint(state.Position);
            Fix64 moveDistance = state.Speed * deltaTime;
            state.Position = AdvanceToward(state.Position, aimPoint, moveDistance, out bool arrived);
            if (!arrived)
                continue;

            bool hit = WeaponTargetRules.IsValidTargetForWeapon(state.Attacker, state.Target, state.WeaponData.Type);
            if (hit)
                ResolveHit(state);
            Complete(state, hit);
            s_ActiveIds.RemoveAt(i);
        }

        LastCompletedFrame = frameId;
    }

    public static FixVector2 AdvanceToward(
        FixVector2 start,
        FixVector2 target,
        Fix64 maxDistance,
        out bool arrived)
    {
        if (maxDistance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        FixVector2 delta = target - start;
        Fix64 distance = FixVector2.Magnitude(delta);
        if (distance <= maxDistance)
        {
            arrived = true;
            return target;
        }
        if (distance == Fix64.Zero)
        {
            arrived = true;
            return start;
        }

        arrived = false;
        return start + new FixVector2(
            delta.x * maxDistance / distance,
            delta.y * maxDistance / distance);
    }

    public static void BindView(ulong projectileId)
    {
        EnsureActive();
        ProjectileState state = GetRequired(projectileId);
        if (state.ViewBound)
            throw new InvalidOperationException($"LogicProjectileService.BindView failed: projectile {projectileId} already has a view.");
        state.ViewReserved = false;
        state.ViewBound = true;
    }

    public static void ReserveView(ulong projectileId)
    {
        EnsureActive();
        ProjectileState state = GetRequired(projectileId);
        if (state.ViewReserved || state.ViewBound)
            throw new InvalidOperationException($"LogicProjectileService.ReserveView failed: projectile {projectileId} already has a view request.");
        state.ViewReserved = true;
    }

    public static void CancelViewReservation(ulong projectileId)
    {
        EnsureActive();
        ProjectileState state = GetRequired(projectileId);
        if (!state.ViewReserved || state.ViewBound)
            throw new InvalidOperationException($"LogicProjectileService.CancelViewReservation failed: projectile {projectileId} has no pending view reservation.");
        state.ViewReserved = false;
        if (state.Completed)
            s_States.Remove(projectileId);
    }

    public static bool TryGetPresentationState(ulong projectileId, out LogicProjectileViewState viewState)
    {
        EnsureActive();
        if (projectileId == 0)
            throw new ArgumentOutOfRangeException(nameof(projectileId));
        if (!s_States.TryGetValue(projectileId, out ProjectileState state))
        {
            viewState = default;
            return false;
        }

        viewState = new LogicProjectileViewState(state.Position, state.Completed, state.Hit);
        return true;
    }

    public static LogicProjectileViewState GetRequiredViewState(ulong projectileId)
    {
        EnsureActive();
        ProjectileState state = GetRequired(projectileId);
        if (!state.ViewBound)
            throw new InvalidOperationException($"LogicProjectileService.GetRequiredViewState failed: projectile {projectileId} view is not bound.");
        return new LogicProjectileViewState(state.Position, state.Completed, state.Hit);
    }

    public static void ReleaseView(ulong projectileId)
    {
        if (!IsActive)
            return;
        ProjectileState state = GetRequired(projectileId);
        if (!state.ViewBound)
            throw new InvalidOperationException($"LogicProjectileService.ReleaseView failed: projectile {projectileId} view is not bound.");
        state.ViewBound = false;
        if (state.Completed)
            s_States.Remove(projectileId);
    }

    private static void ResolveHit(ProjectileState state)
    {
        if (WeaponTargetRules.IsHealingWeapon(state.WeaponData.Type))
        {
            HealingWeaponEffect.Execute(state.Attacker, state.Target, state.WeaponData);
        }
        else if (state.WeaponData.SplashRadius > Fix64.Zero)
        {
            AreaWeaponDamage.DealSplash(state.Attacker, state.Target, state.WeaponData);
        }
        else
        {
            var damage = new Damage(state.Attacker as ITargetable, state.WeaponData.Damage, HealthModifyType.reduce);
            DamageHelper.DoDamage(state.Target as ITargetable, damage, state.Attacker);
        }
    }

    private static void Complete(ProjectileState state, bool hit)
    {
        state.Completed = true;
        state.Hit = hit;
        if (!state.ViewReserved && !state.ViewBound)
            s_States.Remove(state.Id);
    }

    private static ProjectileState GetRequired(ulong projectileId)
    {
        if (projectileId == 0 || !s_States.TryGetValue(projectileId, out ProjectileState state))
            throw new InvalidOperationException($"LogicProjectileService does not contain projectile {projectileId}.");
        return state;
    }

    private static WeaponData CloneWeaponData(WeaponData source)
    {
        if (source == null)
            throw new InvalidOperationException("LogicProjectileService snapshot contains null WeaponData.");
        return new WeaponData(
            source.Type,
            source.Atk,
            source.Interval,
            source.Range,
            source.ProjectileSpeed,
            source.WindUp,
            source.WindDown,
            source.SplashRadius,
            source.SplitAngle,
            source.SplitDist,
            source.ProjectileCount,
            source.AmmunitionCapacity,
            (Fix64[])source.UniqueValues.Clone());
    }

    private static void ClearState()
    {
        s_States.Clear();
        s_ActiveIds.Clear();
        s_LastId = 0;
        LastCompletedFrame = 0;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicProjectileService operation failed: service is not active.");
    }
}
