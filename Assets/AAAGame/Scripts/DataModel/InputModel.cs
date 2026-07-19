using System;
using GameFramework;
using UnityEngine;

public class InputModel : DataModelBase
{
    public LogicInputTimeline LogicTimeline { get; } = new LogicInputTimeline();
    public LogicInputFrame CurrentLogicFrame { get; private set; } = LogicInputFrame.Empty;

    public Fix64 MoveX { get; set; }
    public Fix64 MoveY { get; set; }

    public bool InteractionPressed { get; set; }
    public bool Interaction2Pressed { get; set; }
    public bool Interaction3Pressed { get; set; }
    public bool OpenTechTreePressed { get; set; }

    public bool PlayerAttack { get; set; }

    public bool Skill1Pressed { get; set; }
    public bool Skill2Pressed { get; set; }
    public bool Skill3Pressed { get; set; }
    public bool Skill4Pressed { get; set; }
    public bool Skill5Pressed { get; set; }

    public Vector2 SelectScreenPosition { get; set; }
    public bool SkillConfirmPressed { get; set; }

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
        OpenTechTreePressed = false;
        Skill1Pressed = false;
        Skill2Pressed = false;
        Skill3Pressed = false;
        Skill4Pressed = false;
        Skill5Pressed = false;
        PlayerAttack = false;
        SelectScreenPosition = Vector2.zero;
        SkillConfirmPressed = false;
    }

    public void RequestSkillPress(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex >= SkillInputRuntime.MaxSkillCount)
            throw new ArgumentOutOfRangeException(nameof(skillIndex), skillIndex, "Invalid skill index.");
        if (!LogicTimeline.IsStarted || (LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused))
            return;

        LogicTimeline.EnqueueButtonPulse(
            Time.realtimeSinceStartupAsDouble,
            (LogicInputButton)((int)LogicInputButton.Skill1 + skillIndex));
    }

    public void RequestSkillConfirm()
    {
        if (!LogicTimeline.IsStarted || (LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused))
            return;

        LogicTimeline.EnqueueButtonPulse(
            Time.realtimeSinceStartupAsDouble,
            LogicInputButton.SkillConfirm);
    }

    public void RequestSelectScreenPosition(Vector2 screenPosition)
    {
        if (!LogicTimeline.IsStarted || (LogicTimeControlService.IsActive && LogicTimeControlService.IsPaused))
            return;

        LogicTimeline.EnqueueSelectScreenPosition(
            Time.realtimeSinceStartupAsDouble,
            new FixVector2((Fix64)screenPosition.x, (Fix64)screenPosition.y));
    }

    public void ClearSkillRequests()
    {
        Skill1Pressed = false;
        Skill2Pressed = false;
        Skill3Pressed = false;
        Skill4Pressed = false;
        Skill5Pressed = false;
        SkillConfirmPressed = false;
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
        OpenTechTreePressed = frame.WasPressed(LogicInputButton.OpenTechTree);
        PlayerAttack = frame.WasPressed(LogicInputButton.PlayerAttack);
        Skill1Pressed = frame.WasPressed(LogicInputButton.Skill1);
        Skill2Pressed = frame.WasPressed(LogicInputButton.Skill2);
        Skill3Pressed = frame.WasPressed(LogicInputButton.Skill3);
        Skill4Pressed = frame.WasPressed(LogicInputButton.Skill4);
        Skill5Pressed = frame.WasPressed(LogicInputButton.Skill5);
        SelectScreenPosition = frame.SelectScreenPosition;
        SkillConfirmPressed = frame.WasPressed(LogicInputButton.SkillConfirm);
    }

    protected override void OnCreate(RefParams userdata)
    {
        base.OnCreate(userdata);
        Reset();
    }

    protected override void OnRelease()
    {
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
