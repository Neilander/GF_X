using UnityEngine;
using UnityGameFramework.Runtime;

[RequireComponent(typeof(Collider))]
public class TutorialTriggerCollider : MonoBehaviour, ILogicFrameUpdate, ILogicFrameStableOrder
{
    [SerializeField] private TutorialType triggerType = TutorialType.FriendlyStronghold;
    [SerializeField] private bool triggerOnce = true;

    private TutorialManager tutorialManager;
    private bool hasTriggered;
    private bool logicFrameRegistered;
    private bool fixedBoundsReady;
    private FixVector2 fixedCenter;
    private FixVector2 fixedHalfExtents;

    public TutorialType TriggerType => triggerType;
    public bool TriggerOnce => triggerOnce;
    public int LogicFrameOrder => 999;
    public long LogicFrameStableKey => (long)triggerType;

    private void OnEnable()
    {
        LogicFrameRuntime.Began += HandleLogicRuntimeBegan;
        LogicFrameRuntime.Ending += HandleLogicRuntimeEnding;
        if (LogicFrameRuntime.IsActive)
            RegisterLogicFrame();
    }

    private void OnDisable()
    {
        LogicFrameRuntime.Began -= HandleLogicRuntimeBegan;
        LogicFrameRuntime.Ending -= HandleLogicRuntimeEnding;
        if (logicFrameRegistered)
            UnregisterLogicFrame();
        fixedBoundsReady = false;
    }

    public void OnLogicFrameUpdate(Fix64 deltaTime)
    {
        if (deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new System.InvalidOperationException("TutorialTriggerCollider received a non-fixed logic delta.");
        if (LogicFrameRuntime.CurrentFrame == 0)
            throw new System.InvalidOperationException("TutorialTriggerCollider cannot update on logic frame zero.");
        if (triggerOnce && hasTriggered)
            return;
        if (triggerType == TutorialType.EnemyStronghold)
            return;

        IEntityContext player = EntityRegistry.Player;
        if (player == null || !player.Alive)
            return;

        EnsureFixedBounds();
        FixVector2 offset = player.PositionFixed - fixedCenter;
        if (Fix64.Abs(offset.x) > fixedHalfExtents.x
            || Fix64.Abs(offset.y) > fixedHalfExtents.y)
        {
            return;
        }

        TutorialManager manager = ResolveTutorialManagerRequired();

        if (!manager.NotifyTriggerEntered(triggerType, this))
            return;

        hasTriggered = true;
    }

    private void EnsureFixedBounds()
    {
        if (fixedBoundsReady)
            return;

        GetFixedHorizontalBounds(out fixedCenter, out fixedHalfExtents);
        fixedBoundsReady = true;
    }

    private void HandleLogicRuntimeBegan()
    {
        RegisterLogicFrame();
    }

    private void HandleLogicRuntimeEnding()
    {
        if (logicFrameRegistered)
            UnregisterLogicFrame();
    }

    private void RegisterLogicFrame()
    {
        if (logicFrameRegistered || triggerType == TutorialType.EnemyStronghold)
            return;

        LogicFrameRuntime.Register(this);
        logicFrameRegistered = true;
    }

    private void UnregisterLogicFrame()
    {
        LogicFrameRuntime.Unregister(this);
        logicFrameRegistered = false;
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

    private TutorialManager ResolveTutorialManagerRequired()
    {
        if (tutorialManager != null)
            return tutorialManager;

        tutorialManager = GameEntry.GetComponent<TutorialManager>();
        return tutorialManager
               ?? throw new System.InvalidOperationException(
                   $"Tutorial trigger '{name}' requires the registered TutorialManager component.");
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
