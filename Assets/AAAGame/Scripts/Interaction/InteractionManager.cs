using System.Collections.Generic;
using UnityEngine;

public class InteractionManager : MonoBehaviour
{
    [Header("Range")]
    [SerializeField] private float interactionRange = 10f;
    [SerializeField] private float triggerPadding = 3f;

    [SerializeField] private InteractionDetector detector;

    [Header("Scoring")]
    [SerializeField] private float distanceWeight = 0.6f;
    [SerializeField] private float angleWeight = 0.4f;
    [Tooltip("Only switch when V_best > V_current + threshold")]
    [SerializeField] private float switchThreshold = 0.05f;
    [Tooltip("Optional time gate to avoid rapid switches; set to 0 to disable")]
    [SerializeField] private float minHoldTime = 0f;
    private float _lastSwitchTime;

    private InputModel _inputModel;
    private InteractionHost _currentTarget;

    private void Awake()
    {
        SyncRangeToComponents();
    }

    private void OnValidate()
    {
        SyncRangeToComponents();
    }

    private void SyncRangeToComponents()
    {
        interactionRange = Mathf.Max(0f, interactionRange);
        triggerPadding = Mathf.Max(0f, triggerPadding);

        if (detector != null)
        {
            detector.MaxDistance = interactionRange;

            var sphere = detector.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                sphere.isTrigger = true;
                sphere.radius = interactionRange + triggerPadding;
            }
        }
    }

    private void Update()
    {
        if (!EnsureInputModel())
            return;

        detector.CleanupInvalid();
        var target = ResolveBestTarget(detector.Candidates, _currentTarget);

        bool targetChanged = target != _currentTarget;
        _currentTarget = target;

        if (targetChanged)
        {
            GF.Event.Fire(this, InteractionFocusChangedEventArgs.Create(_currentTarget));
            Debug.Log($"[Interaction] Focus changed to {_currentTarget?.name ?? "<null>"}");
        }

        HandleInput();
    }

    private void HandleInput()
    {
        if (_currentTarget == null)
            return;

        InputKey? optKey = _inputModel.InteractionPressed ? InputKey.InteractionPrimary
            : _inputModel.Interaction2Pressed ? InputKey.InteractionSecondary
            : _inputModel.Interaction3Pressed ? InputKey.InteractionTertiary
            : null;
        if (!optKey.HasValue)
            return;
        _currentTarget.TryExecute(optKey.Value);
    }

    private bool EnsureInputModel()
    {
        if (_inputModel != null)
            return true;

        if (GF.DataModel == null)
            return false;

        _inputModel = GF.DataModel.GetDataModel<InputModel>();
        return _inputModel != null;
    }

    public InteractionHost ResolveBestTarget(IReadOnlyList<InteractionHost> candidates, InteractionHost current)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        float bestScore = float.NegativeInfinity;
        InteractionHost best = null;

        Vector3 actorPos = transform.position;
        Vector3 actorForward = transform.forward;

        for (int i = 0; i < candidates.Count; i++)
        {
            var target = candidates[i];
            if (target == null)
                continue;

            if (!TryScore(actorPos, actorForward, target, out float score))
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                best = target;
            }
        }

        if (best == null)
            return null;

        if (current == null || current == best)
            return best;

        bool currentValid = TryScore(actorPos, actorForward, current, out float currentScore);
        if (!currentValid)
        {
            _lastSwitchTime = Time.time;
            return best;
        }

        if (minHoldTime > 0f && Time.time - _lastSwitchTime < minHoldTime)
            return current;

        // Va/Vb hysteresis: switch only when Vb > Va + threshold.
        if (bestScore <= currentScore + switchThreshold)
            return current;

        _lastSwitchTime = Time.time;
        return best;
    }

    private bool TryScore(Vector3 actorPos, Vector3 actorForward, InteractionHost target, out float score)
    {
        score = float.NegativeInfinity;
        if (target == null)
            return false;
        if (!target.IsInteractable())
            return false;

        float dist = Vector3.Distance(actorPos, target.Transform.position);
        if (dist > interactionRange)
            return false;

        float distanceScore = 1f - Mathf.Clamp01(dist / interactionRange);

        Vector3 toTarget = target.Transform.position - actorPos;
        Vector3 dir = toTarget.sqrMagnitude <= 1e-8f ? actorForward : toTarget.normalized;
        float cos = Vector3.Dot(actorForward, dir);
        float angleScore = Mathf.Clamp01((cos + 1f) * 0.5f); // -1..1 -> 0..1

        score = distanceScore * distanceWeight + angleScore * angleWeight;
        return true;
    }
}
