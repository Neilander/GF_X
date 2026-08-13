using System;
using UnityEngine;

public sealed class AnimationRatePresenter : IDisposable
{
    private readonly Animator m_Animator;
    private readonly float m_BaseSpeed;
    private int m_PerEntityScaleUnits = LogicTimeControlService.NormalScaleUnits;
    private bool m_IsDisposed;

    public AnimationRatePresenter(Animator animator)
    {
        m_Animator = animator != null ? animator : throw new ArgumentNullException(nameof(animator));
        m_BaseSpeed = animator.speed;
        if (float.IsNaN(m_BaseSpeed) || float.IsInfinity(m_BaseSpeed) || m_BaseSpeed < 0f)
            throw new InvalidOperationException($"AnimationRatePresenter failed: invalid base animator speed {m_BaseSpeed}.");

        ApplyCurrentScale();
    }

    public void SetPerEntityScale(int scaleUnits)
    {
        ThrowIfDisposed();
        if (scaleUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(scaleUnits), scaleUnits, "Per-entity animation scale must be non-negative.");

        m_PerEntityScaleUnits = scaleUnits;
        ApplyCurrentScale();
    }

    public void ApplyCurrentScale()
    {
        ThrowIfDisposed();
        if (m_Animator == null)
            throw new InvalidOperationException("AnimationRatePresenter failed: bound Animator was destroyed.");

        float globalScale = LogicTimeControlService.IsActive
            ? LogicTimeControlService.AnimationScale
            : 1f;
        float perEntityScale = m_PerEntityScaleUnits / (float)LogicTimeControlService.ScaleUnitsPerOne;
        m_Animator.speed = m_BaseSpeed * perEntityScale * globalScale;
    }

    public void Dispose()
    {
        if (m_IsDisposed)
            throw new InvalidOperationException("AnimationRatePresenter.Dispose failed: presenter is already disposed.");
        if (m_Animator == null)
            throw new InvalidOperationException("AnimationRatePresenter.Dispose failed: bound Animator was destroyed.");

        m_Animator.speed = m_BaseSpeed;
        m_IsDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (m_IsDisposed)
            throw new InvalidOperationException("AnimationRatePresenter is disposed.");
    }
}
