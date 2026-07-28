using System;

public sealed class LogicMoveExecutor : IMoveExecutor, ILogicDeterministicStateContributor
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

    public void SetInputFixed(FixVector2 velocity)
    {
        m_InputVelocity = velocity;
    }

    public void AddExternalFixed(FixVector2 velocity)
    {
        m_ExternalVelocity += velocity;
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

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        hasher.Add(m_InputVelocity.x.RawValue);
        hasher.Add(m_InputVelocity.y.RawValue);
        hasher.Add(m_ExternalVelocity.x.RawValue);
        hasher.Add(m_ExternalVelocity.y.RawValue);
        hasher.Add(m_OverrideVelocity.x.RawValue);
        hasher.Add(m_OverrideVelocity.y.RawValue);
        hasher.Add(m_HasOverride);
        hasher.Add(m_NavigationConstrained);
        hasher.Add(m_BypassConstraintForNextFrame);
        hasher.Add((int)m_MovementMode);
        hasher.Add(HasPreparedLogicMove);
        hasher.Add(PreparedLogicFrame);
        hasher.Add(PreparedCollisionMovable);
        hasher.Add(PreparedResolvedHorizontalDisplacement.x.RawValue);
        hasher.Add(PreparedResolvedHorizontalDisplacement.y.RawValue);
    }

}
