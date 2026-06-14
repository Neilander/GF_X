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
    public bool Skill4Pressed { get; set; }
    public bool Skill5Pressed { get; set; }

    public Vector2 SelectScreenPosition { get; set; }
    public bool SkillConfirmPressed { get; set; }

    private readonly bool[] m_RequestedSkillPressed = new bool[SkillInputRuntime.MaxSkillCount];
    private bool m_RequestedSkillConfirmPressed;
    private bool m_HasRequestedSelectScreenPosition;
    private Vector2 m_RequestedSelectScreenPosition;

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
        Skill4Pressed = false;
        Skill5Pressed = false;
        PlayerAttack = false;

        SelectScreenPosition = Vector2.zero;
        SkillConfirmPressed = false;
        for (int i = 0; i < m_RequestedSkillPressed.Length; i++)
            m_RequestedSkillPressed[i] = false;
        m_RequestedSkillConfirmPressed = false;
        m_HasRequestedSelectScreenPosition = false;
        m_RequestedSelectScreenPosition = Vector2.zero;
    }

    public void RequestSkillPress(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex >= m_RequestedSkillPressed.Length)
            throw new System.ArgumentOutOfRangeException(nameof(skillIndex), skillIndex, "Invalid skill index.");

        m_RequestedSkillPressed[skillIndex] = true;
    }

    public void RequestSkillConfirm()
    {
        m_RequestedSkillConfirmPressed = true;
    }

    public void RequestSelectScreenPosition(Vector2 screenPosition)
    {
        m_HasRequestedSelectScreenPosition = true;
        m_RequestedSelectScreenPosition = screenPosition;
    }

    public void ClearSkillRequests()
    {
        Skill1Pressed = false;
        Skill2Pressed = false;
        Skill3Pressed = false;
        Skill4Pressed = false;
        Skill5Pressed = false;
        SkillConfirmPressed = false;
        for (int i = 0; i < m_RequestedSkillPressed.Length; i++)
            m_RequestedSkillPressed[i] = false;
        m_RequestedSkillConfirmPressed = false;
        m_HasRequestedSelectScreenPosition = false;
    }

    public bool ConsumeRequestedSkillPress(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex >= m_RequestedSkillPressed.Length)
            return false;

        return Consume(ref m_RequestedSkillPressed[skillIndex]);
    }

    public bool ConsumeRequestedSkillConfirm()
    {
        return Consume(ref m_RequestedSkillConfirmPressed);
    }

    public Vector2 ConsumeSelectScreenPosition(Vector2 fallback)
    {
        if (!m_HasRequestedSelectScreenPosition)
            return fallback;

        m_HasRequestedSelectScreenPosition = false;
        return m_RequestedSelectScreenPosition;
    }

    private static bool Consume(ref bool value)
    {
        bool result = value;
        value = false;
        return result;
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
