using UnityEngine;
using System;
using System.Collections.Generic;

public class DurationMoveEffectComp : IDurationMoveEffectComp
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

    public int StartDurationAdditionalMove(float duration, Vector3 speed, Func<Vector3, Vector3> speedModifier = null)
    {
        _additionalIndex++;
        _timedAdditionalEffects.Add(_additionalIndex, new TimedMoveEffect(duration, speed, speedModifier));
        return _additionalIndex;
    }

    public int StartDurationOverrideMove(float duration, Vector3 speed, Func<Vector3, Vector3> speedModifier = null)
    {
        _overrideIndex++;
        _timedOverrideEffects.Add(_overrideIndex, new TimedMoveEffect(duration, speed, speedModifier));
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
    }

    public void ApplyEffect(float deltaTime)
    {
        Vector3 finalAdditionalVelocity = Vector3.zero;
        Vector3 finalOverrideVelocity = Vector3.zero;
        // -------- 计算 Additional --------
        var additionalKeys = new List<int>(_timedAdditionalEffects.Keys);

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
        bool hasOverride = overrideKeys.Count > 0;
        foreach (var key in overrideKeys)
        {
            var effect = _timedOverrideEffects[key];

            finalOverrideVelocity += effect.speed;

            if (effect.UpdateAndCheck(deltaTime))
            {
                _timedOverrideEffects.Remove(key);
            }
        }

        // -------- 应用到 MoveExecutor --------

        var executor = _ctx.MoveExecutor;

        if (hasOverride)
        {
            executor.SetOverride(finalOverrideVelocity);
        }
        executor.AddExternal(finalAdditionalVelocity);
    }

    public void ShutDown()
    {
        StopAllMove();
    }

    public void Resume()
    {

    }

    class TimedMoveEffect : TimedEffect
    {
        public Vector3 speed;
        public Func<Vector3, Vector3> speedModifier;
        public TimedMoveEffect(float duration, Vector3 speed, Func<Vector3, Vector3> speedModifier) : base(duration)
        {
            this.speed = speed;
            this.speedModifier = speedModifier;
        }

        public override bool UpdateAndCheck(float deltaTime)
        {
            if (speedModifier != null)
            {
                speed = speedModifier(speed);
            }
            return base.UpdateAndCheck(deltaTime);
        }
    }
}
