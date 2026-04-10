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

    private void EnsureDetectorReference()
    {
        if (detector != null)
            return;

        detector = GetComponent<InteractionDetector>();
        if (detector == null)
            detector = GetComponentInChildren<InteractionDetector>();
    }

    public void ConfigureRuntime(
        InteractionDetector detectorComponent,
        float range,
        float padding,
        float distanceScoreWeight = 0.65f,
        float facingScoreWeight = 0.35f,
        float switchGate = 0.08f,
        float holdGate = 0.1f)
    {
        detector = detectorComponent;
        interactionRange = Mathf.Max(0f, range);
        triggerPadding = Mathf.Max(0f, padding);

        float weightSum = Mathf.Max(0.001f, distanceScoreWeight + facingScoreWeight);
        distanceWeight = Mathf.Clamp01(distanceScoreWeight / weightSum);
        angleWeight = 1f - distanceWeight;

        switchThreshold = Mathf.Max(0f, switchGate);
        minHoldTime = Mathf.Max(0f, holdGate);

        SyncRangeToComponents();
    }

    private void Awake()
    {
        EnsureDetectorReference();
        SyncRangeToComponents();
    }

    private void OnValidate()
    {
        EnsureDetectorReference();
        SyncRangeToComponents();
    }

    private void SyncRangeToComponents()
    {
        interactionRange = Mathf.Max(0f, interactionRange);
        triggerPadding = Mathf.Max(0f, triggerPadding);

        if (detector != null)
        {
            detector.MaxDistance = interactionRange;
            detector.TriggerPadding = triggerPadding;

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
        EnsureDetectorReference();
        if (detector == null)
            return;

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
        // 交互执行统一交给 InteractOptionTips（鼠标长按 / 可选按键长按）。
        // 这里不再做按键瞬发执行，避免绕过长按进度逻辑。
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

        var collider = target.GetComponent<Collider>();
        float dist = collider != null
            ? Vector3.Distance(actorPos, collider.ClosestPoint(actorPos))
            : Vector3.Distance(actorPos, target.Transform.position);

        float effectiveRange = interactionRange * 0.8f;
        if (dist > effectiveRange)
            return false;

        float distanceScore = 1f - Mathf.Clamp01(dist / Mathf.Max(0.001f, effectiveRange));

        Vector3 toTarget = target.Transform.position - actorPos;
        Vector3 dir = toTarget.sqrMagnitude <= 1e-8f ? actorForward : toTarget.normalized;
        float cos = Vector3.Dot(actorForward, dir);
        float angleScore = Mathf.Clamp01((cos + 1f) * 0.5f); // -1..1 -> 0..1

        score = distanceScore * distanceWeight + angleScore * angleWeight;
        return true;
    }
}
