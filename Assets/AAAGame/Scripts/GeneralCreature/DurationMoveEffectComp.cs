using UnityEngine;
using System.Collections.Generic;

public class DurationMoveEffectComp : IDurationMoveEffectComp, ILogicDeterministicStateContributor
{
    private IEntityContext _ctx;
    //用于计数返回
    private int _additionalIndex;
    private int _overrideIndex;
    private Dictionary<int, TimedMoveEffect> _timedAdditionalEffects;
    private Dictionary<int, TimedMoveEffect> _timedOverrideEffects;

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _additionalIndex = 0;
        _overrideIndex = 0;
        _timedAdditionalEffects = new Dictionary<int, TimedMoveEffect>();
        _timedOverrideEffects = new Dictionary<int, TimedMoveEffect>();
    }

    public int StartDurationAdditionalMove(float duration, Vector3 speed)
    {
        return StartDurationAdditionalMove(
            (Fix64)duration,
            new FixVector2((Fix64)speed.x, (Fix64)speed.z));
    }

    public int StartDurationAdditionalMove(Fix64 duration, FixVector2 speed)
    {
        _additionalIndex++;
        _timedAdditionalEffects.Add(_additionalIndex, new TimedMoveEffect(duration, speed));
        return _additionalIndex;
    }

    public int StartDurationOverrideMove(float duration, Vector3 speed)
    {
        return StartDurationOverrideMove(
            (Fix64)duration,
            new FixVector2((Fix64)speed.x, (Fix64)speed.z));
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
        _ctx?.MoveExecutor?.SetMovementMode(MovementMode.Normal);
    }

    public void ApplyEffect(Fix64 deltaTime)
    {
        FixVector2 finalAdditionalVelocity = FixVector2.Zero;
        FixVector2 finalOverrideVelocity = FixVector2.Zero;
        // -------- 计算 Additional --------
        var additionalKeys = new List<int>(_timedAdditionalEffects.Keys);
        additionalKeys.Sort();
        bool hadAdditionalEffectThisFrame = additionalKeys.Count > 0;

        foreach (var key in additionalKeys)
        {
            var effect = _timedAdditionalEffects[key];

            // 先累加当前速度
            finalAdditionalVelocity += effect.speed;

            // 更新时间，若结束则移除
            if (effect.UpdateAndCheck(deltaTime))
            {
                _timedAdditionalEffects.Remove(key);
            }
        }

        // -------- 计算 Override --------
        var overrideKeys = new List<int>(_timedOverrideEffects.Keys);
        overrideKeys.Sort();
        bool hasOverride = overrideKeys.Count > 0;
        bool hadOverrideEffectThisFrame = hasOverride;
        foreach (var key in overrideKeys)
        {
            var effect = _timedOverrideEffects[key];

            finalOverrideVelocity += effect.speed;

            if (effect.UpdateAndCheck(deltaTime))
            {
                _timedOverrideEffects.Remove(key);
            }
        }

        var executor = _ctx.MoveExecutor;

        if (hasOverride)
        {
            executor.SetOverrideFixed(finalOverrideVelocity);
        }
        executor.AddExternalFixed(finalAdditionalVelocity);

        bool hasMotionEffect = hadAdditionalEffectThisFrame
                               || hadOverrideEffectThisFrame
                               || _timedAdditionalEffects.Count > 0
                               || _timedOverrideEffects.Count > 0;
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
    }

    private static void WriteEffects(LogicStateHasher hasher, Dictionary<int, TimedMoveEffect> effects)
    {
        var keys = new List<int>(effects.Keys);
        keys.Sort();
        hasher.Add(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            int key = keys[i];
            TimedMoveEffect effect = effects[key];
            hasher.Add(key);
            hasher.Add(effect.duration.RawValue);
            hasher.Add(effect.speed.x.RawValue);
            hasher.Add(effect.speed.y.RawValue);
        }
    }

    class TimedMoveEffect : TimedEffect
    {
        public FixVector2 speed;

        public TimedMoveEffect(Fix64 duration, FixVector2 speed) : base(duration)
        {
            this.speed = speed;
        }
    }
}
