using AAAGame.Scripts.Entity;
using UnityEngine;
using UnityGameFramework.Runtime;

[RequireComponent(typeof(Collider))]
public class TutorialTriggerCollider : MonoBehaviour
{
    [SerializeField] private TutorialType triggerType = TutorialType.MoveHeroByWASD;
    [SerializeField] private bool triggerOnce = true;

    private TutorialManager tutorialManager;
    private bool hasTriggered;

    public TutorialType TriggerType => triggerType;
    public bool TriggerOnce => triggerOnce;

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && hasTriggered)
            return;

        if (!IsPlayerHeroCollider(other))
            return;

        if (triggerType == TutorialType.InvadeSH)
            return;

        TutorialManager manager = ResolveTutorialManager();
        if (manager == null)
        {
            Log.Warning("[TutorialTrigger] TutorialManager is missing. trigger={0}.", name);
            return;
        }

        if (!manager.NotifyTriggerEntered(triggerType, this))
            return;

        hasTriggered = true;
    }

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

    private TutorialManager ResolveTutorialManager()
    {
        if (tutorialManager == null)
        {
            tutorialManager = GameEntry.GetComponent<TutorialManager>();
            if (tutorialManager == null)
            {
                tutorialManager = FindObjectOfType<TutorialManager>();
            }
        }

        return tutorialManager;
    }

    private static bool IsPlayerHeroCollider(Collider other)
    {
        if (other == null)
            return false;

        MAEntity entity = other.GetComponentInParent<MAEntity>();
        if (entity == null || !entity.Alive)
            return false;

        if (entity.Brain is PlayerBrain)
            return true;

        return object.ReferenceEquals(EntityRegistry.Player, entity);
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
