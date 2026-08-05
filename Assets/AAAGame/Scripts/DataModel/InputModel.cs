using System;
using GameFramework;

public class InputModel : DataModelBase
{
    private static InputModel s_ActiveModel;

    public InputModel()
    {
        if (s_ActiveModel != null
            && !ReferenceEquals(s_ActiveModel, this)
            && GF.DataModel != null
            && ReferenceEquals(GF.DataModel.GetDataModel<InputModel>(), s_ActiveModel))
            throw new InvalidOperationException("InputModel active runtime model is already bound.");
        s_ActiveModel = this;
    }

    public static InputModel RequireActive()
    {
        return s_ActiveModel
               ?? throw new InvalidOperationException("InputModel is required before logic input consumers are created.");
    }

    public LogicInputTimeline LogicTimeline { get; } = new LogicInputTimeline();
    public LogicInputFrame CurrentLogicFrame { get; private set; } = LogicInputFrame.Empty;

    public Fix64 MoveX { get; set; }
    public Fix64 MoveY { get; set; }

    public bool InteractionPressed { get; set; }
    public bool Interaction2Pressed { get; set; }
    public bool Interaction3Pressed { get; set; }
    public bool PlayerAttack { get; set; }

    public void Reset()
    {
        LogicTimeline.Clear();
        CurrentLogicFrame = LogicInputFrame.Empty;
        ClearCompatibilityState();
    }

    public void ClearCompatibilityState()
    {
        MoveX = Fix64.Zero;
        MoveY = Fix64.Zero;
        InteractionPressed = false;
        Interaction2Pressed = false;
        Interaction3Pressed = false;
        PlayerAttack = false;
    }

    public void ApplyLogicInputFrame(LogicInputFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        if (frame != LogicTimeline.CurrentFrame)
            throw new InvalidOperationException(
                $"InputModel.ApplyLogicInputFrame failed: frame is not the timeline's current sealed frame. frame={frame.FrameId}.");

        CurrentLogicFrame = frame;
        MoveX = frame.WorldMove.x;
        MoveY = frame.WorldMove.y;
        InteractionPressed = frame.WasPressed(LogicInputButton.InteractionPrimary);
        Interaction2Pressed = frame.WasPressed(LogicInputButton.InteractionSecondary);
        Interaction3Pressed = frame.WasPressed(LogicInputButton.InteractionTertiary);
        PlayerAttack = frame.WasPressed(LogicInputButton.PlayerAttack);
    }

    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        if (s_ActiveModel != null && !ReferenceEquals(s_ActiveModel, this))
            throw new InvalidOperationException("InputModel active runtime model is already bound.");
        s_ActiveModel = this;
        Reset();
    }

    protected override void OnRelease()
    {
        if (!ReferenceEquals(s_ActiveModel, this))
            throw new InvalidOperationException("InputModel release does not match the active runtime model.");
        s_ActiveModel = null;
        base.OnRelease();
        Reset();
    }
}

public enum InputKey
{
    InteractionPrimary = 0,
    InteractionSecondary = 1,
    InteractionTertiary = 2
}
