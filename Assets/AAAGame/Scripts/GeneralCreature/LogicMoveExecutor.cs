using System;
using UnityEngine;

public sealed class LogicMoveExecutor : IMoveExecutor
{
    private FixVector2 m_InputVelocity;
    private FixVector2 m_ExternalVelocity;
    private FixVector2 m_OverrideVelocity;
    private bool m_HasOverride;
    private bool m_NavigationConstrained = true;
    private bool m_BypassConstraintForNextFrame;
    private MovementMode m_MovementMode;

    public MovementMode MovementMode => m_MovementMode;
    public bool HasPreparedLogicMove { get; private set; }
    public ulong PreparedLogicFrame { get; private set; }
    public bool PreparedCollisionMovable { get; private set; }
    public FixVector2 PreparedResolvedHorizontalDisplacement { get; private set; }

    public void SetInput(Vector3 velocity)
    {
        SetInputFixed(ToFixed(velocity));
    }

    public void SetInputFixed(FixVector2 velocity)
    {
        m_InputVelocity = velocity;
    }

    public void AddExternal(Vector3 velocity)
    {
        AddExternalFixed(ToFixed(velocity));
    }

    public void AddExternalFixed(FixVector2 velocity)
    {
        m_ExternalVelocity += velocity;
    }

    public void SetOverride(Vector3 velocity)
    {
        SetOverrideFixed(ToFixed(velocity));
    }

    public void SetOverrideFixed(FixVector2 velocity)
    {
        m_OverrideVelocity = velocity;
        m_HasOverride = true;
    }

    public void ClearOverride()
    {
        m_HasOverride = false;
        m_OverrideVelocity = FixVector2.Zero;
    }

    public void SetExternal(Vector3 velocity)
    {
        SetExternalFixed(ToFixed(velocity));
    }

    public void SetExternalFixed(FixVector2 velocity)
    {
        m_ExternalVelocity = velocity;
    }

    public void SetMovementMode(MovementMode mode)
    {
        m_MovementMode = mode;
    }

    public void SetNavigationConstrained(bool constrained)
    {
        m_NavigationConstrained = constrained;
    }

    public void SetConstraintBypassForNextFrame(bool bypass = true)
    {
        m_BypassConstraintForNextFrame = bypass;
    }

    public void EnableNavigationConstraintBypass()
    {
        m_BypassConstraintForNextFrame = true;
    }

    public void Execute()
    {
        throw new InvalidOperationException("LogicMoveExecutor.Execute failed: use the coordinated logic-frame pipeline.");
    }

    public void Execute(float deltaTime)
    {
        throw new InvalidOperationException("LogicMoveExecutor.Execute failed: use the coordinated logic-frame pipeline.");
    }

    public void PrepareLogicFrame(ulong frameId, Fix64 deltaTime, bool allowMovement)
    {
        if (frameId == 0)
            throw new ArgumentOutOfRangeException(nameof(frameId));
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("LogicMoveExecutor.PrepareLogicFrame failed: deltaTime is not fixed.");
        if (HasPreparedLogicMove)
            throw new InvalidOperationException($"LogicMoveExecutor.PrepareLogicFrame failed: frame {PreparedLogicFrame} is still pending.");

        FixVector2 velocity = FixVector2.Zero;
        if (allowMovement)
        {
            if (m_HasOverride)
                velocity = m_OverrideVelocity;
            else if (m_MovementMode == MovementMode.Normal)
                velocity = m_InputVelocity + m_ExternalVelocity;
            else if (m_MovementMode == MovementMode.Displaced)
                velocity = m_ExternalVelocity;
        }

        PreparedLogicFrame = frameId;
        PreparedResolvedHorizontalDisplacement = velocity * deltaTime;
        PreparedCollisionMovable = allowMovement && (m_NavigationConstrained || m_BypassConstraintForNextFrame);
        HasPreparedLogicMove = true;
    }

    public void CommitPreparedLogicFrame(ulong frameId)
    {
        if (!HasPreparedLogicMove || PreparedLogicFrame != frameId)
        {
            throw new InvalidOperationException(
                $"LogicMoveExecutor.CommitPreparedLogicFrame failed: prepared={PreparedLogicFrame}, requested={frameId}.");
        }

        HasPreparedLogicMove = false;
        PreparedLogicFrame = 0;
        PreparedCollisionMovable = false;
        PreparedResolvedHorizontalDisplacement = FixVector2.Zero;
        m_InputVelocity = FixVector2.Zero;
        m_ExternalVelocity = FixVector2.Zero;
        m_OverrideVelocity = FixVector2.Zero;
        m_HasOverride = false;
        m_BypassConstraintForNextFrame = false;
    }

    private static FixVector2 ToFixed(Vector3 value)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.z) || float.IsInfinity(value.z))
        {
            throw new ArgumentException("Logic move velocity must be finite.", nameof(value));
        }

        return new FixVector2((Fix64)value.x, (Fix64)value.z);
    }
}
