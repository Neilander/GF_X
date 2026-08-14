using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityGameFramework.Runtime;

public sealed class SkillSlotInputProxy : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    private int m_SlotIndex = -1;
    private Func<int> m_ResolveActiveSkillIndex;
    private Func<int> m_ResolveSkillSlotIndex;
    private Action<int> m_PointerEntered;
    private Action<int> m_PointerExited;
    private bool m_DragStartedSkill;
    private bool m_DragUsesAim;
    private bool m_DragStartedSwap;
    private bool m_SuppressNextClick;

    public void Initialize(
        int slotIndex,
        Func<int> resolveActiveSkillIndex,
        Func<int> resolveSkillSlotIndex,
        Action<int> pointerEntered,
        Action<int> pointerExited)
    {
        m_SlotIndex = slotIndex;
        m_ResolveActiveSkillIndex = resolveActiveSkillIndex;
        m_ResolveSkillSlotIndex = resolveSkillSlotIndex;
        m_PointerEntered = pointerEntered;
        m_PointerExited = pointerExited;
    }

    public void OnPointerEnter(PointerEventData eventData) => m_PointerEntered?.Invoke(m_SlotIndex);
    public void OnPointerExit(PointerEventData eventData) => m_PointerExited?.Invoke(m_SlotIndex);

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

        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        int skillSlotIndex = ResolveSkillSlotIndex();
        if (skillSlotIndex < 0)
            return;
        m_DragStartedSwap = true;

        int activeSkillIndex = ResolveActiveSkillIndex();
        if (CanAcceptSkillInput(activeSkillIndex))
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
        bool swapped = m_DragStartedSwap && SwapWithDropTarget(eventData);
        if (swapped)
        {
            if (m_DragUsesAim)
                SkillCastPresentationService.Cancel();
            m_DragStartedSwap = false;
            m_DragStartedSkill = false;
            m_DragUsesAim = false;
            m_SuppressNextClick = true;
            return;
        }

        m_DragStartedSwap = false;

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

    private bool SwapWithDropTarget(PointerEventData eventData)
    {
        int fromIndex = ResolveSkillSlotIndex();
        if (fromIndex < 0)
            return false;

        SkillSlotInputProxy target = GetDropTarget(eventData);
        if (target == null)
            return false;

        int toIndex = target.ResolveSkillSlotIndex();
        if (toIndex < 0 || toIndex == fromIndex)
            return false;
        if (!SkillRuntimeDataModel.CanSwapSkillSlots(fromIndex, toIndex))
            return false;

        SkillRuntimeDataModel.RequestSwapSkillSlots(fromIndex, toIndex);
        return true;
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
