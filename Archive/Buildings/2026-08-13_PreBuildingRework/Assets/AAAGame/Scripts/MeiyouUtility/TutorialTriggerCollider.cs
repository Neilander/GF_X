using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TutorialTriggerCollider : MonoBehaviour
{
    [SerializeField] private TutorialType triggerType = TutorialType.FriendlyStronghold;
    [SerializeField] private bool triggerOnce = true;

    public TutorialType TriggerType => triggerType;
    public bool TriggerOnce => triggerOnce;

    public void GetFixedHorizontalBounds(out FixVector2 center, out FixVector2 halfExtents)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null || !box.enabled || !box.isTrigger)
            throw new System.InvalidOperationException($"Tutorial trigger '{name}' requires an enabled trigger BoxCollider.");

        Bounds bounds = box.bounds;
        if (bounds.extents.x <= 0f || bounds.extents.z <= 0f)
            throw new System.InvalidOperationException($"Tutorial trigger '{name}' has invalid horizontal bounds {bounds}.");

        center = new FixVector2((Fix64)bounds.center.x, (Fix64)bounds.center.z);
        halfExtents = new FixVector2((Fix64)bounds.extents.x, (Fix64)bounds.extents.z);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Collider collider = GetComponent<Collider>();
        if (collider != null && !collider.isTrigger)
        {
            collider.isTrigger = true;
        }
    }
#endif
}
