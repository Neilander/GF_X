using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class SkillSlotInputProxy : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private int m_SlotIndex = -1;
    private Func<int> m_ResolveActiveSkillIndex;
    private Func<int> m_ResolveSkillSlotIndex;
    private bool m_DragStartedSkill;
    private bool m_DragStartedSwap;
    private bool m_SuppressNextClick;

    public void Initialize(int slotIndex, Func<int> resolveActiveSkillIndex, Func<int> resolveSkillSlotIndex)
    {
        m_SlotIndex = slotIndex;
        m_ResolveActiveSkillIndex = resolveActiveSkillIndex;
        m_ResolveSkillSlotIndex = resolveSkillSlotIndex;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (m_DragStartedSkill || m_DragStartedSwap)
            return;

        if (m_SuppressNextClick)
        {
            m_SuppressNextClick = false;
            return;
        }

        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        RequestSkillPress();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (SkillCastState.IsCasting)
                return;

            m_DragStartedSwap = ResolveSkillSlotIndex() >= 0;
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left && CanAcceptSkillInput())
        {
            m_DragStartedSkill = RequestSkillPress();
            RequestSelectPosition(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!m_DragStartedSkill)
            return;

        RequestSelectPosition(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (m_DragStartedSwap)
        {
            SwapWithDropTarget(eventData);
            m_DragStartedSwap = false;
            m_SuppressNextClick = true;
            return;
        }

        if (!m_DragStartedSkill)
            return;

        RequestSelectPosition(eventData);

        InputModel inputModel = GF.DataModel.GetDataModel<InputModel>();
        if (inputModel == null)
            throw new InvalidOperationException("InputModel is required for skill drag.");

        inputModel.RequestSkillConfirm();
        m_DragStartedSkill = false;
        m_SuppressNextClick = true;
    }

    private bool RequestSkillPress()
    {
        if (!CanAcceptSkillInput())
            return false;

        int activeSkillIndex = m_ResolveActiveSkillIndex != null ? m_ResolveActiveSkillIndex.Invoke() : -1;
        if (activeSkillIndex < 0 || activeSkillIndex >= PlayerSkillComp.SKILL_NUM)
            return false;

        InputModel inputModel = GF.DataModel.GetDataModel<InputModel>();
        if (inputModel == null)
            throw new InvalidOperationException("InputModel is required for skill press.");

        inputModel.RequestSkillPress(activeSkillIndex);
        return true;
    }

    private int ResolveSkillSlotIndex()
    {
        return m_ResolveSkillSlotIndex != null ? m_ResolveSkillSlotIndex.Invoke() : -1;
    }

    private void SwapWithDropTarget(PointerEventData eventData)
    {
        int fromIndex = ResolveSkillSlotIndex();
        if (fromIndex < 0)
            return;

        SkillSlotInputProxy target = GetDropTarget(eventData);
        if (target == null)
            return;

        int toIndex = target.ResolveSkillSlotIndex();
        if (toIndex < 0 || toIndex == fromIndex)
            return;

        SkillRuntimeDataModel.SwapSkillSlots(fromIndex, toIndex);
    }

    private static SkillSlotInputProxy GetDropTarget(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerCurrentRaycast.gameObject == null)
            return null;

        return eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<SkillSlotInputProxy>();
    }

    private static bool CanAcceptSkillInput()
    {
        return SkillInputRuntime.CanUseActiveSkillsInCurrentPhase() && !SkillCastState.IsCasting;
    }

    private static void RequestSelectPosition(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        InputModel inputModel = GF.DataModel.GetDataModel<InputModel>();
        if (inputModel == null)
            throw new InvalidOperationException("InputModel is required for skill drag position.");

        inputModel.RequestSelectScreenPosition(eventData.position);
    }
}
