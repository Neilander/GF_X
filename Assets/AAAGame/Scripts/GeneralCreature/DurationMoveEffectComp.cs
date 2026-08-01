using UnityEngine;
using System.Collections.Generic;
using System;

public class DurationMoveEffectComp : IDurationMoveEffectComp, ILogicDeterministicStateContributor
{
    private IEntityContext _ctx;
    //用于计数返回
    private int _additionalIndex;
    private int _overrideIndex;
    private Dictionary<int, TimedMoveEffect> _timedAdditionalEffects;
    private Dictionary<int, TimedMoveEffect> _timedOverrideEffects;
    private readonly List<int> _sortedEffectKeys = new List<int>();
    private readonly List<int> _sortedPullKeys = new List<int>();
    private Dictionary<int, PullEffect> _pullEffects;
    private int _pullIndex;
    private FixVector2 _displacementVelocity;
    private Fix64 _lossOfBalanceElapsed;
    private Fix64 _worldFriction;
    private bool _isInLossOfBalance;
    private bool _attackLocked;

    private static readonly Fix64 MinimumLossOfBalanceDuration = Fix64.FromRaw(410);
    private static readonly Fix64 ExitSpeedGameUnits = Fix64.FromRaw(410);
    private static readonly Fix64 SinglePullStopRatio = Fix64.FromRaw(2748);

    public bool IsInLossOfBalance => _isInLossOfBalance;
    public FixVector2 DisplacementVelocity => _displacementVelocity;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _additionalIndex = 0;
        _overrideIndex = 0;
        _timedAdditionalEffects = new Dictionary<int, TimedMoveEffect>();
        _timedOverrideEffects = new Dictionary<int, TimedMoveEffect>();
        _pullEffects = new Dictionary<int, PullEffect>();
        _pullIndex = 0;
        _displacementVelocity = FixVector2.Zero;
        _lossOfBalanceElapsed = Fix64.Zero;
        _worldFriction = Fix64.Zero;
        _isInLossOfBalance = false;
        _attackLocked = false;
    }

    public int StartDurationAdditionalMove(Fix64 duration, FixVector2 speed)
    {
        _additionalIndex++;
        _timedAdditionalEffects.Add(_additionalIndex, new TimedMoveEffect(duration, speed));
        return _additionalIndex;
    }

    public int StartDurationOverrideMove(Fix64 duration, FixVector2 speed)
    {
        _overrideIndex++;
        _timedOverrideEffects.Add(_overrideIndex, new TimedMoveEffect(duration, speed));
        return _overrideIndex;
    }

    public void StopAddtionalMove(int index)
    {
        if (_timedAdditionalEffects.ContainsKey(index))
        {
            _timedAdditionalEffects.Remove(index);
        }
    }

    public void StopOverrideMove(int index)
    {
        if (_timedOverrideEffects.ContainsKey(index))
        {
            _timedOverrideEffects.Remove(index);
        }
    }

    public void StopAllMove()
    {
        _timedAdditionalEffects.Clear();
        _timedOverrideEffects.Clear();
        _pullEffects.Clear();
        ExitLossOfBalance();
        _ctx?.MoveExecutor?.SetMovementMode(MovementMode.Normal);
    }

    public bool TryApplyKnockback(FixVector2 direction, Fix64 strengthLevel)
    {
        RequireInitialized();
        if (IsBuildingTarget())
            return false;

        FixVector2 normalizedDirection = direction.GetNormalized();
        if (FixVector2.SqrMagnitude(normalizedDirection) == Fix64.Zero)
            throw new ArgumentException("Knockback direction must be non-zero.", nameof(direction));

        if (!DisplacementForceUtility.TryResolveKnockbackVelocity(
                strengthLevel,
                _ctx.GetProperty(CreatureMainProperty.WeightLevel),
                out Fix64 velocity))
        {
            return false;
        }

        EnterLossOfBalance();
        _displacementVelocity += normalizedDirection * velocity;
        return true;
    }

    public bool TryStartPull(LogicEntityId sourceEntityId, Fix64 strengthLevel)
    {
        RequireInitialized();
        if (!sourceEntityId.IsValid)
            throw new ArgumentException("Pull source entity id must be valid.", nameof(sourceEntityId));
        if (IsBuildingTarget())
            return false;
        if (!EntityRegistry.TryGet(sourceEntityId, out IEntityContext source) || !source.Alive)
            throw new InvalidOperationException($"Pull source entity {sourceEntityId.Value} is not active.");

        if (!DisplacementForceUtility.TryResolvePull(
                strengthLevel,
                _ctx.GetProperty(CreatureMainProperty.WeightLevel),
                out Fix64 acceleration,
                out Fix64 duration))
        {
            return false;
        }

        FixVector2 targetPosition = GetFramePosition(_ctx);
        LogicCombatShape sourceShape = GetFrameCombatShape(source);
        FixVector2 sourceEdge = sourceShape.ClosestPoint(targetPosition);
        Fix64 initialDistance = FixVector2.Distance(targetPosition, sourceEdge);
        if (initialDistance <= Fix64.Zero)
            return false;

        EnterLossOfBalance();
        _pullIndex = checked(_pullIndex + 1);
        _pullEffects.Add(
            _pullIndex,
            new PullEffect(sourceEntityId, duration, acceleration, initialDistance));
        return true;
    }

    public void CommitStaticCollision(FixVector2 firstHitNormal)
    {
        if (!_isInLossOfBalance || firstHitNormal == FixVector2.Zero)
            return;

        Fix64 inwardVelocity = FixVector2.Dot(_displacementVelocity, firstHitNormal);
        if (inwardVelocity < Fix64.Zero)
            _displacementVelocity -= firstHitNormal * inwardVelocity;
    }

    public void CopyActivePullTethers(List<DisplacementPullTetherState> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (_pullEffects == null)
            throw new InvalidOperationException("DurationMoveEffectComp pull tethers requested before initialization.");

        results.Clear();
        FillSortedPullKeys();
        for (int i = 0; i < _sortedPullKeys.Count; i++)
        {
            int effectId = _sortedPullKeys[i];
            results.Add(new DisplacementPullTetherState(effectId, _pullEffects[effectId].SourceEntityId));
        }
    }

    public void ApplyEffect(Fix64 deltaTime)
    {
        FixVector2 finalAdditionalVelocity = FixVector2.Zero;
        FixVector2 finalOverrideVelocity = FixVector2.Zero;
        // -------- 计算 Additional --------
        FillSortedEffectKeys(_timedAdditionalEffects);
        bool hadAdditionalEffectThisFrame = _sortedEffectKeys.Count > 0;

        for (int i = 0; i < _sortedEffectKeys.Count; i++)
        {
            int key = _sortedEffectKeys[i];
            TimedMoveEffect effect = _timedAdditionalEffects[key];

            // 先累加当前速度
            finalAdditionalVelocity += effect.speed;

            // 更新时间，若结束则移除
            if (effect.UpdateAndCheck(deltaTime))
            {
                _timedAdditionalEffects.Remove(key);
            }
        }

        // -------- 计算 Override --------
        FillSortedEffectKeys(_timedOverrideEffects);
        bool hasOverride = _sortedEffectKeys.Count > 0;
        bool hadOverrideEffectThisFrame = hasOverride;
        for (int i = 0; i < _sortedEffectKeys.Count; i++)
        {
            int key = _sortedEffectKeys[i];
            TimedMoveEffect effect = _timedOverrideEffects[key];

            finalOverrideVelocity += effect.speed;

            if (effect.UpdateAndCheck(deltaTime))
            {
                _timedOverrideEffects.Remove(key);
            }
        }

        ApplyPhysicalDisplacement(deltaTime);

        var executor = _ctx.MoveExecutor;
        if (executor == null)
            throw new InvalidOperationException("DurationMoveEffectComp requires a move executor.");

        if (hasOverride)
        {
            executor.SetOverrideFixed(finalOverrideVelocity);
        }
        executor.AddExternalFixed(finalAdditionalVelocity + _displacementVelocity);

        bool hasMotionEffect = hadAdditionalEffectThisFrame
                               || hadOverrideEffectThisFrame
                               || _timedAdditionalEffects.Count > 0
                               || _timedOverrideEffects.Count > 0
                               || _isInLossOfBalance;
        if (hasMotionEffect)
        {
            _ctx.MoveExecutor.SetMovementMode(MovementMode.Displaced);
        }
        else
        {
            _ctx.MoveExecutor.SetMovementMode(MovementMode.Normal);
        }
    }

    public void ShutDown()
    {
        StopAllMove();
    }

    public void Resume()
    {
        _ctx?.MoveExecutor?.SetMovementMode(MovementMode.Normal);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new System.ArgumentNullException(nameof(hasher));
        if (_timedAdditionalEffects == null || _timedOverrideEffects == null)
            throw new System.InvalidOperationException("DurationMoveEffectComp deterministic state requested before initialization.");

        hasher.Add(_additionalIndex);
        WriteEffects(hasher, _timedAdditionalEffects);
        hasher.Add(_overrideIndex);
        WriteEffects(hasher, _timedOverrideEffects);
        hasher.Add(_pullIndex);
        hasher.Add(_displacementVelocity.x.RawValue);
        hasher.Add(_displacementVelocity.y.RawValue);
        hasher.Add(_lossOfBalanceElapsed.RawValue);
        hasher.Add(_worldFriction.RawValue);
        hasher.Add(_isInLossOfBalance);
        hasher.Add(_attackLocked);
        FillSortedPullKeys();
        hasher.Add(_sortedPullKeys.Count);
        for (int i = 0; i < _sortedPullKeys.Count; i++)
        {
            int effectId = _sortedPullKeys[i];
            PullEffect effect = _pullEffects[effectId];
            hasher.Add(effectId);
            hasher.Add(effect.SourceEntityId.Value);
            hasher.Add(effect.RemainingDuration.RawValue);
            hasher.Add(effect.BaseAcceleration.RawValue);
            hasher.Add(effect.InitialDistance.RawValue);
        }
    }

    private void WriteEffects(LogicStateHasher hasher, Dictionary<int, TimedMoveEffect> effects)
    {
        FillSortedEffectKeys(effects);
        hasher.Add(_sortedEffectKeys.Count);
        for (int i = 0; i < _sortedEffectKeys.Count; i++)
        {
            int key = _sortedEffectKeys[i];
            TimedMoveEffect effect = effects[key];
            hasher.Add(key);
            hasher.Add(effect.duration.RawValue);
            hasher.Add(effect.speed.x.RawValue);
            hasher.Add(effect.speed.y.RawValue);
        }
    }

    private void FillSortedEffectKeys(Dictionary<int, TimedMoveEffect> effects)
    {
        _sortedEffectKeys.Clear();
        _sortedEffectKeys.AddRange(effects.Keys);
        _sortedEffectKeys.Sort();
    }

    private void ApplyPhysicalDisplacement(Fix64 deltaTime)
    {
        if (!_isInLossOfBalance)
            return;

        _lossOfBalanceElapsed += deltaTime;
        FixVector2 pullAcceleration = FixVector2.Zero;
        FillSortedPullKeys();
        int activePullCount = 0;
        for (int i = 0; i < _sortedPullKeys.Count; i++)
        {
            int effectId = _sortedPullKeys[i];
            PullEffect effect = _pullEffects[effectId];
            if (!EntityRegistry.TryGet(effect.SourceEntityId, out IEntityContext source) || !source.Alive)
            {
                _pullEffects.Remove(effectId);
                continue;
            }

            FixVector2 targetPosition = GetFramePosition(_ctx);
            FixVector2 sourceEdge = GetFrameCombatShape(source).ClosestPoint(targetPosition);
            FixVector2 toSource = sourceEdge - targetPosition;
            Fix64 currentDistance = FixVector2.Magnitude(toSource);
            if (currentDistance > Fix64.Zero)
            {
                Fix64 ratio = currentDistance / effect.InitialDistance;
                Fix64 ratioSquared = ratio * ratio;
                pullAcceleration += toSource / currentDistance
                                    * (effect.BaseAcceleration * ratioSquared * ratioSquared);
            }

            activePullCount++;
            effect.RemainingDuration -= deltaTime;
            if (effect.RemainingDuration <= Fix64.Zero)
                _pullEffects.Remove(effectId);
        }

        _displacementVelocity += pullAcceleration * deltaTime;
        if (activePullCount == 1)
            ApplySinglePullStop();
        ApplyFriction(deltaTime);

        Fix64 exitSpeed = DistanceUnitConverter.ConvertToWorld(ExitSpeedGameUnits);
        if (_lossOfBalanceElapsed >= MinimumLossOfBalanceDuration
            && _pullEffects.Count == 0
            && FixVector2.Magnitude(_displacementVelocity) < exitSpeed)
        {
            ExitLossOfBalance();
        }
    }

    private void ApplySinglePullStop()
    {
        FillSortedPullKeys();
        if (_sortedPullKeys.Count != 1)
            return;
        PullEffect effect = _pullEffects[_sortedPullKeys[0]];
        if (!EntityRegistry.TryGet(effect.SourceEntityId, out IEntityContext source) || !source.Alive)
            return;

        FixVector2 targetPosition = GetFramePosition(_ctx);
        FixVector2 sourceEdge = GetFrameCombatShape(source).ClosestPoint(targetPosition);
        Fix64 currentDistance = FixVector2.Distance(targetPosition, sourceEdge);
        if (currentDistance <= effect.InitialDistance * SinglePullStopRatio)
            _displacementVelocity = FixVector2.Zero;
    }

    private void ApplyFriction(Fix64 deltaTime)
    {
        Fix64 speed = FixVector2.Magnitude(_displacementVelocity);
        if (speed <= Fix64.Zero)
            return;
        Fix64 nextSpeed = Fix64.Max(Fix64.Zero, speed - _worldFriction * deltaTime);
        _displacementVelocity = nextSpeed == Fix64.Zero
            ? FixVector2.Zero
            : _displacementVelocity * (nextSpeed / speed);
    }

    private void EnterLossOfBalance()
    {
        if (_isInLossOfBalance)
            return;
        if (_ctx.AtkComp == null)
            throw new InvalidOperationException("Displacement target has no attack component.");
        if (_ctx.MoveExecutor == null)
            throw new InvalidOperationException("Displacement target has no move executor.");

        _isInLossOfBalance = true;
        _lossOfBalanceElapsed = Fix64.Zero;
        _worldFriction = DisplacementForceUtility.ReadWorldFriction();
        _ctx.LockComp(_ctx.AtkComp, this);
        _attackLocked = true;
        _ctx.MoveExecutor.SetMovementMode(MovementMode.Displaced);
    }

    private void ExitLossOfBalance()
    {
        _pullEffects?.Clear();
        _displacementVelocity = FixVector2.Zero;
        _lossOfBalanceElapsed = Fix64.Zero;
        _worldFriction = Fix64.Zero;
        _isInLossOfBalance = false;
        if (_attackLocked)
        {
            _ctx.ResumeComp(_ctx.AtkComp, this);
            _attackLocked = false;
        }
    }

    private bool IsBuildingTarget()
    {
        return _ctx is IBuildingLogicContext building && building.BuildingData != null;
    }

    private void RequireInitialized()
    {
        if (_ctx == null || _pullEffects == null)
            throw new InvalidOperationException("DurationMoveEffectComp is not initialized.");
    }

    private void FillSortedPullKeys()
    {
        _sortedPullKeys.Clear();
        _sortedPullKeys.AddRange(_pullEffects.Keys);
        _sortedPullKeys.Sort();
    }

    private static FixVector2 GetFramePosition(IEntityContext entity)
    {
        return LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(entity).Position
            : entity.PositionFixed;
    }

    private static LogicCombatShape GetFrameCombatShape(IEntityContext entity)
    {
        return LogicFrameRuntime.IsTicking
            ? LogicEntityFrameSnapshotService.GetRequiredCurrent(entity).CombatShape
            : entity.CombatShape;
    }

    class TimedMoveEffect : TimedEffect
    {
        public FixVector2 speed;

        public TimedMoveEffect(Fix64 duration, FixVector2 speed) : base(duration)
        {
            this.speed = speed;
        }
    }

    private sealed class PullEffect
    {
        public PullEffect(
            LogicEntityId sourceEntityId,
            Fix64 remainingDuration,
            Fix64 baseAcceleration,
            Fix64 initialDistance)
        {
            SourceEntityId = sourceEntityId;
            RemainingDuration = remainingDuration;
            BaseAcceleration = baseAcceleration;
            InitialDistance = initialDistance;
        }

        public LogicEntityId SourceEntityId { get; }
        public Fix64 RemainingDuration;
        public Fix64 BaseAcceleration { get; }
        public Fix64 InitialDistance { get; }
    }
}
