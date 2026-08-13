using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityGameFramework.Runtime;

public sealed class SkillSlotInputProxy : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private int m_SlotIndex = -1;
    private Func<int> m_ResolveActiveSkillIndex;
    private Func<int> m_ResolveSkillSlotIndex;
    private bool m_DragStartedSkill;
    private bool m_DragUsesAim;
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

        RequestSkillCast(eventData);
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

        int activeSkillIndex = ResolveActiveSkillIndex();
        if (eventData.button == PointerEventData.InputButton.Left && CanAcceptSkillInput(activeSkillIndex))
        {
            InputManager inputManager = GetRequiredInputManager();
            m_DragStartedSkill = true;
            m_DragUsesAim = SkillCastPresentationService.TryBeginAim(
                activeSkillIndex,
                inputManager,
                eventData.position);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!m_DragStartedSkill)
            return;

        if (m_DragUsesAim && eventData != null)
            SkillCastPresentationService.UpdateAim(GetRequiredInputManager(), eventData.position);
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

        if (m_DragUsesAim)
        {
            if (eventData != null)
                SkillCastPresentationService.UpdateAim(GetRequiredInputManager(), eventData.position);
            SkillCastPresentationService.CommitAim();
        }
        else
        {
            RequestSkillCast(eventData);
        }
        m_DragStartedSkill = false;
        m_DragUsesAim = false;
        m_SuppressNextClick = true;
    }

    private bool RequestSkillCast(PointerEventData eventData)
    {
        int activeSkillIndex = ResolveActiveSkillIndex();
        if (!CanAcceptSkillInput(activeSkillIndex))
            return false;

        InputManager inputManager = GetRequiredInputManager();
        Vector2 screenPosition = eventData != null
            ? eventData.position
            : inputManager.GetPointerScreenPosition();
        return SkillCastPresentationService.RequestCastAtScreen(
            activeSkillIndex,
            inputManager,
            screenPosition);
    }

    private int ResolveActiveSkillIndex()
    {
        return m_ResolveActiveSkillIndex != null ? m_ResolveActiveSkillIndex.Invoke() : -1;
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

        SkillRuntimeDataModel.RequestSwapSkillSlots(fromIndex, toIndex);
    }

    private static SkillSlotInputProxy GetDropTarget(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerCurrentRaycast.gameObject == null)
            return null;

        return eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<SkillSlotInputProxy>();
    }

    private static bool CanAcceptSkillInput(int activeSkillIndex)
    {
        return activeSkillIndex >= 0
               && activeSkillIndex < PlayerSkillComp.SKILL_NUM
               && SkillInputRuntime.CanUseActiveSkillsInCurrentPhase()
               && !SkillCastState.IsCasting
               && !SkillCastPresentationService.IsAiming
               && SkillCastPresentationService.CanRequestSkillCast(activeSkillIndex);
    }

    private static InputManager GetRequiredInputManager()
    {
        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager == null)
            throw new InvalidOperationException("InputManager is required for skill input.");
        return inputManager;
    }
}
