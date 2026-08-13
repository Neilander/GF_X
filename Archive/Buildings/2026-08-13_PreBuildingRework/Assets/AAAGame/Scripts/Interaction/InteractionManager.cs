using System;
using UnityEngine;

public sealed class InteractionManager : MonoBehaviour
{
    private MAEntity m_Actor;
    private LogicEntityId m_PresentedTargetId;
    private InteractionHost m_CurrentPresentationTarget;

    private void Awake()
    {
        m_Actor = GetComponentInParent<MAEntity>()
                  ?? throw new InvalidOperationException("InteractionManager requires an MAEntity parent.");
    }

    private void Update()
    {
        LogicEntityId targetId = default;
        if (LogicInteractionTargetStateService.IsActive && m_Actor.LogicEntityId.IsValid)
            LogicInteractionTargetStateService.TryGetTarget(m_Actor.LogicEntityId, out targetId);

        InteractionHost presentationTarget = ResolvePresentationTarget(targetId);
        if (targetId == m_PresentedTargetId && presentationTarget == m_CurrentPresentationTarget)
            return;

        m_PresentedTargetId = targetId;
        m_CurrentPresentationTarget = presentationTarget;
        PublishFocusChanged();
    }

    private void OnDisable()
    {
        ClearFocus();
    }

    private void ClearFocus()
    {
        if (!m_PresentedTargetId.IsValid && m_CurrentPresentationTarget == null)
            return;

        m_PresentedTargetId = default;
        m_CurrentPresentationTarget = null;
        PublishFocusChanged();
    }

    private void PublishFocusChanged()
    {
        if (GF.Event == null)
            throw new InvalidOperationException("InteractionManager cannot publish focus without GF.Event.");
        GF.Event.Fire(this, InteractionFocusChangedEventArgs.Create(m_CurrentPresentationTarget));
    }

    private static InteractionHost ResolvePresentationTarget(LogicEntityId targetId)
    {
        if (!targetId.IsValid || !LogicEntityLifecycleService.TryGetBoundView(targetId, out MAEntity view))
            return null;
        BuildingEntity building = view as BuildingEntity
                                  ?? throw new InvalidOperationException($"Interaction target view {targetId.Value} is not a BuildingEntity.");
        InteractionHost host = building.GetComponent<InteractionHost>();
        if (host == null || !host.IsInteractable())
        {
            throw new InvalidOperationException(
                $"Bound interaction target view {targetId.Value} has no active InteractionHost.");
        }
        return host;
    }
}
