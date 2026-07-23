using System;
using UnityEngine;

public class InteractionManager : MonoBehaviour, ILogicFrameUpdate, ILogicFrameStableOrder
{
    [Header("Range")]
    [SerializeField] private float interactionRange = 10f;
    [SerializeField] private float triggerPadding = 3f;

    [Header("Scoring")]
    [SerializeField] private float distanceWeight = 0.6f;
    [SerializeField] private float angleWeight = 0.4f;
    [SerializeField] private float switchThreshold = 0.05f;
    [SerializeField] private float minHoldTime = 0f;

    private MAEntity m_Actor;
    private LogicEntityId m_CurrentTargetId;
    private InteractionHost m_CurrentPresentationTarget;
    private Fix64 m_EffectiveRange;
    private Fix64 m_DistanceWeight;
    private Fix64 m_AngleWeight;
    private Fix64 m_SwitchThreshold;
    private int m_MinHoldFrames;
    private ulong m_LastSwitchFrame;
    private bool m_IsRegistered;
    private bool m_IsHoldConsumerRegistered;

    public int LogicFrameOrder => -500;
    public long LogicFrameStableKey => m_Actor != null && m_Actor.LogicEntityId.IsValid
        ? m_Actor.LogicEntityId.Value
        : throw new InvalidOperationException("InteractionManager stable key requires a bound actor.");

    public void ConfigureRuntime(
        float range,
        float padding,
        float distanceScoreWeight = 0.65f,
        float facingScoreWeight = 0.35f,
        float switchGate = 0.08f,
        float holdGate = 0.1f)
    {
        interactionRange = Mathf.Max(0f, range);
        triggerPadding = Mathf.Max(0f, padding);
        distanceWeight = Mathf.Max(0f, distanceScoreWeight);
        angleWeight = Mathf.Max(0f, facingScoreWeight);
        switchThreshold = Mathf.Max(0f, switchGate);
        minHoldTime = Mathf.Max(0f, holdGate);
        QuantizeConfiguration();
        TryRegister();
    }

    private void Awake()
    {
        m_Actor = GetComponentInParent<MAEntity>()
                  ?? throw new InvalidOperationException("InteractionManager requires an MAEntity parent.");
        QuantizeConfiguration();
    }

    private void OnEnable()
    {
        TryRegister();
    }

    private void OnDisable()
    {
        Unregister();
        ClearFocus();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    private void OnValidate()
    {
        interactionRange = Mathf.Max(0f, interactionRange);
        triggerPadding = Mathf.Max(0f, triggerPadding);
        distanceWeight = Mathf.Max(0f, distanceWeight);
        angleWeight = Mathf.Max(0f, angleWeight);
        switchThreshold = Mathf.Max(0f, switchThreshold);
        minHoldTime = Mathf.Max(0f, minHoldTime);
    }

    public void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException("InteractionManager received a non-fixed logic delta.");
        if (LogicEntityFrameSnapshotService.CapturedFrame != LogicFrameRuntime.CurrentFrame)
            throw new InvalidOperationException("InteractionManager requires the current frame snapshot.");

        LogicEntityId targetId = ResolveBestTarget(m_CurrentTargetId);
        LogicInteractionTargetStateService.SetTarget(m_Actor.LogicEntityId, targetId);
        if (targetId != m_CurrentTargetId)
        {
            m_CurrentTargetId = targetId;
            m_LastSwitchFrame = LogicFrameRuntime.CurrentFrame;
        }

        InteractionHost presentationTarget = ResolvePresentationTarget(targetId);
        if (presentationTarget == m_CurrentPresentationTarget)
            return;
        m_CurrentPresentationTarget = presentationTarget;
        if (GF.Event == null)
            throw new InvalidOperationException("InteractionManager cannot publish focus without GF.Event.");

        GF.Event.Fire(this, InteractionFocusChangedEventArgs.Create(m_CurrentPresentationTarget));
    }

    private LogicEntityId ResolveBestTarget(LogicEntityId currentId)
    {
        LogicEntityFrameState actorState = LogicEntityFrameSnapshotService.GetRequiredCurrent(m_Actor);
        IBuildingLogicContext bestBuilding = null;
        Fix64 bestScore = -(Fix64)1;

        for (int i = 0; i < EntityRegistry.AllEntities.Count; i++)
        {
            if (!EntityRegistry.AllEntities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
                || !building.Alive)
                continue;
            if (!LogicInteractionOptionService.HasVisibleOptions(building))
                continue;
            if (!TryScore(actorState, building, out Fix64 score))
                continue;

            if (IsBetterCandidate(
                    score,
                    building.LogicEntityId,
                    bestBuilding != null,
                    bestScore,
                    bestBuilding != null ? bestBuilding.LogicEntityId : default))
            {
                bestBuilding = building;
                bestScore = score;
            }
        }

        LogicEntityId bestId = bestBuilding != null ? bestBuilding.LogicEntityId : default;
        if (!bestId.IsValid || !currentId.IsValid || currentId == bestId)
            return bestId;
        if (!TryResolveBuilding(currentId, out IBuildingLogicContext currentBuilding)
            || !TryScore(actorState, currentBuilding, out Fix64 currentScore))
        {
            return bestId;
        }

        ulong heldFrames = LogicFrameRuntime.CurrentFrame - m_LastSwitchFrame;
        if (heldFrames < (ulong)m_MinHoldFrames)
            return currentId;
        return bestScore > currentScore + m_SwitchThreshold ? bestId : currentId;
    }

    private bool TryScore(
        LogicEntityFrameState actorState,
        IBuildingLogicContext building,
        out Fix64 score)
    {
        LogicEntityFrameState targetState = LogicEntityFrameSnapshotService.Current.GetRequired(building.LogicEntityId);
        return TryComputeScore(
            actorState,
            targetState,
            m_EffectiveRange,
            m_DistanceWeight,
            m_AngleWeight,
            out score);
    }

    public static bool TryComputeScore(
        LogicEntityFrameState actorState,
        LogicEntityFrameState targetState,
        Fix64 effectiveRange,
        Fix64 distanceScoreWeight,
        Fix64 angleScoreWeight,
        out Fix64 score)
    {
        if (effectiveRange <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(effectiveRange));
        if (distanceScoreWeight < Fix64.Zero || angleScoreWeight < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(distanceScoreWeight));
        Fix64 scoreWeightSum = distanceScoreWeight + angleScoreWeight;
        if (scoreWeightSum <= Fix64.Zero)
            throw new ArgumentException("Interaction score weights must have a positive sum.");
        distanceScoreWeight /= scoreWeightSum;
        angleScoreWeight /= scoreWeightSum;

        Fix64 distance = targetState.CombatShape.DistanceToSurface(actorState.Position);
        if (distance > effectiveRange)
        {
            score = -(Fix64)1;
            return false;
        }

        Fix64 distanceScore = Fix64.One - Fix64.Clamp(distance / effectiveRange, Fix64.Zero, Fix64.One);
        FixVector2 toTarget = targetState.CombatShape.Center - actorState.Position;
        FixVector2 direction = FixVector2.SqrMagnitude(toTarget) > Fix64.Zero
            ? toTarget.GetNormalized()
            : actorState.Forward;
        Fix64 cosine = FixVector2.Dot(actorState.Forward, direction);
        Fix64 angleScore = Fix64.Clamp((cosine + Fix64.One) / 2, Fix64.Zero, Fix64.One);
        score = distanceScore * distanceScoreWeight + angleScore * angleScoreWeight;
        return true;
    }

    public static bool IsBetterCandidate(
        Fix64 score,
        LogicEntityId entityId,
        bool hasBest,
        Fix64 bestScore,
        LogicEntityId bestEntityId)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Candidate entity id must be valid.", nameof(entityId));
        if (!hasBest)
            return true;
        if (!bestEntityId.IsValid)
            throw new ArgumentException("Best entity id must be valid when a best candidate exists.", nameof(bestEntityId));

        return score > bestScore || (score == bestScore && entityId < bestEntityId);
    }

    private void QuantizeConfiguration()
    {
        if (interactionRange <= 0f)
            throw new InvalidOperationException("Interaction range must be positive.");

        m_EffectiveRange = (Fix64)interactionRange * (Fix64)4 / 5;
        Fix64 rawDistanceWeight = (Fix64)distanceWeight;
        Fix64 rawAngleWeight = (Fix64)angleWeight;
        Fix64 weightSum = rawDistanceWeight + rawAngleWeight;
        if (weightSum <= Fix64.Zero)
            throw new InvalidOperationException("Interaction score weights must have a positive sum.");

        m_DistanceWeight = rawDistanceWeight / weightSum;
        m_AngleWeight = rawAngleWeight / weightSum;
        m_SwitchThreshold = (Fix64)switchThreshold;
        m_MinHoldFrames = (int)Math.Ceiling(minHoldTime * LogicFrameRuntime.FrameRate);
    }

    private void TryRegister()
    {
        if (m_IsRegistered || !isActiveAndEnabled)
            return;
        if (!LogicFrameRuntime.IsActive
            || !LogicInteractionHoldService.IsActive
            || m_Actor == null
            || !m_Actor.LogicEntityId.IsValid)
            return;

        LogicInteractionHoldService.RegisterConsumer(CanExecuteLogicInteraction, ExecuteLogicInteraction);
        m_IsHoldConsumerRegistered = true;
        try
        {
            LogicFrameRuntime.Register(this);
            m_IsRegistered = true;
        }
        catch
        {
            LogicInteractionHoldService.UnregisterConsumer(CanExecuteLogicInteraction, ExecuteLogicInteraction);
            m_IsHoldConsumerRegistered = false;
            throw;
        }
    }

    private void Unregister()
    {
        if (!m_IsRegistered)
            return;
        if (!LogicFrameRuntime.IsActive)
            throw new InvalidOperationException("InteractionManager lost its logic runtime before unregistering.");

        LogicFrameRuntime.Unregister(this);
        m_IsRegistered = false;
        if (m_IsHoldConsumerRegistered)
        {
            if (!LogicInteractionHoldService.IsActive)
                throw new InvalidOperationException("InteractionManager lost its interaction hold service before unregistering.");
            LogicInteractionHoldService.UnregisterConsumer(CanExecuteLogicInteraction, ExecuteLogicInteraction);
            m_IsHoldConsumerRegistered = false;
        }
    }

    private void ClearFocus()
    {
        if (!m_CurrentTargetId.IsValid && m_CurrentPresentationTarget == null)
            return;

        m_CurrentTargetId = default;
        m_CurrentPresentationTarget = null;
        if (LogicInteractionTargetStateService.IsActive
            && m_Actor != null
            && m_Actor.LogicEntityId.IsValid)
        {
            LogicInteractionTargetStateService.RemoveActor(m_Actor.LogicEntityId);
        }
        if (GF.Event != null)
            GF.Event.Fire(this, InteractionFocusChangedEventArgs.Create(null));
    }

    private bool CanExecuteLogicInteraction(InputKey key)
    {
        return TryResolveBuilding(m_CurrentTargetId, out IBuildingLogicContext building)
               && LogicInteractionOptionService.TryGetVisibleOption(building, key, out LogicInteractionOptionDescriptor option)
               && LogicInteractionOptionService.IsExecutable(building, option);
    }

    private bool ExecuteLogicInteraction(InputKey key)
    {
        return TryResolveBuilding(m_CurrentTargetId, out IBuildingLogicContext building)
               && LogicInteractionOptionService.TryGetVisibleOption(building, key, out LogicInteractionOptionDescriptor option)
               && LogicInteractionOptionService.Execute(building, option);
    }

    private static bool TryResolveBuilding(LogicEntityId entityId, out IBuildingLogicContext building)
    {
        building = null;
        if (!entityId.IsValid || !EntityRegistry.TryGet(entityId, out IEntityContext context))
            return false;
        if (!context.TryGetLogicBuilding(out building))
            return false;
        return building != null
               && building.Alive
               && LogicInteractionOptionService.HasVisibleOptions(building);
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
