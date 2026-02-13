using System.Collections;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;

public class InputModel : DataModelBase
{
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
    
    public Vector2 SelectScreenPosition { get; set; }
    public bool SkillConfirmPressed { get; set; }

    public void Reset()
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
        PlayerAttack = false;

        SelectScreenPosition = Vector2.zero;
        SkillConfirmPressed = false;
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