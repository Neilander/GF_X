using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InteractionDetector : MonoBehaviour
{
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private float triggerPadding = 3f;
    [SerializeField] private LayerMask interactableLayerMask = ~0;

    public float MaxDistance
    {
        get => maxDistance;
        set => maxDistance = Mathf.Max(0f, value);
    }

    public float TriggerPadding
    {
        get => triggerPadding;
        set => triggerPadding = Mathf.Max(0f, value);
    }

    private readonly List<InteractionHost> _candidates = new List<InteractionHost>();
    private readonly Dictionary<InteractionHost, int> _overlapCount = new Dictionary<InteractionHost, int>();

    public IReadOnlyList<InteractionHost> Candidates => _candidates;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsInLayerMask(other.gameObject.layer))
            return;

        if (TryGetTarget(other, out var target))
        {
            if (_overlapCount.TryGetValue(target, out int count))
                _overlapCount[target] = count + 1;
            else
                _overlapCount[target] = 1;

            // 进入触发器时就记录 overlap；是否可交互由 IsValid 决定。
            if (IsValid(target) && !_candidates.Contains(target))
                _candidates.Add(target);
        }
    }
    private void OnTriggerStay(Collider other)
    {
        if (TryGetTarget(other, out var target))
        {
            // 如果此前因为距离等原因被 CleanupInvalid 移除了 overlapCount，这里兜底恢复。
            if (!_overlapCount.ContainsKey(target))
                _overlapCount[target] = 1;

            // 在同一个触发器体积内移动（比如走出 maxDistance 又走回来）时，需要能重新加入候选。
            if (IsValid(target) && !_candidates.Contains(target))
                _candidates.Add(target);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (TryGetTarget(other, out var target))
        {
            if (_overlapCount.TryGetValue(target, out int count))
            {
                count--;
                if (count <= 0)
                {
                    _overlapCount.Remove(target);
                    _candidates.Remove(target);
                }
                else
                {
                    _overlapCount[target] = count;
                }
            }
        }
    }

    public void CleanupInvalid()
    {
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            var target = _candidates[i];
            if (target == null)
            {
                _candidates.RemoveAt(i);
                _overlapCount.Remove(target);
                continue;
            }

            // 距离/状态暂时不可交互时，只从候选移除，保留 overlapCount，
            // 这样在仍处于触发器内时可以通过 OnTriggerStay 重新加入。
            if (!IsValid(target))
            {
                _candidates.RemoveAt(i);
            }
        }
    }

    private bool IsValid(InteractionHost target)
    {
        if (target == null)
            return false;

        if (!target.IsInteractable())
            return false;

        float effectiveMaxDistance = maxDistance * 0.85f;

        var collider = target.GetComponent<Collider>();
        if (collider == null)
            return Vector3.Distance(transform.position, target.Transform.position) <= effectiveMaxDistance;

        Vector3 closestPoint = collider.ClosestPoint(transform.position);
        float distance = Vector3.Distance(transform.position, closestPoint);
        return distance <= effectiveMaxDistance;
    }

    private bool IsInLayerMask(int layer)
    {
        return (interactableLayerMask.value & (1 << layer)) != 0;
    }

    private static bool TryGetTarget(Collider collider, out InteractionHost target)
    {
        target = collider.GetComponent<InteractionHost>();
        return target != null;
    }
}
