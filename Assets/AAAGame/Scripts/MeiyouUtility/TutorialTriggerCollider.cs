using AAAGame.Scripts.Entity;
using UnityEngine;
using UnityGameFramework.Runtime;

[RequireComponent(typeof(Collider))]
public class TutorialTriggerCollider : MonoBehaviour
{
    [SerializeField] private TutorialTriggerType triggerType = TutorialTriggerType.MoveHeroByWASD;
    [SerializeField] private bool triggerOnce = true;

    private TutorialManager tutorialManager;
    private bool hasTriggered;

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && hasTriggered)
            return;

        if (!IsPlayerHeroCollider(other))
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
