using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BehaviorTreeTest : MonoBehaviour
{
   
    public BehaviorTreeGraph graph;

    private BasicEnemyContext context;
    private DummyTarget selfTarget;

    private class DummyTarget : ITargetable
    {
        public SideType Side { get; set; }
        public bool Alive { get; set; }
        public GameObject Gmo { get; set; }
        public string ReferenceId { get; set; }
        public float health { get; private set; } = 10f;

        public bool CanBeSelected() => Alive;
        public void InSelection(ISelector selector) { }
        public void DeSelection() { }
        public void TakeDamage(float damage, HealthModifyType modType)
        {
            health -= damage;
            if (health <= 0)
            {
                health = 0;
                Alive = false;
            }
            Debug.Log($"{ReferenceId} health: {health}");
        }
    }

    private void Start()
    {
        selfTarget = new DummyTarget { Side = SideType.EnemySide, Alive = true, Gmo = gameObject, ReferenceId = "Self" };
        var target1 = new DummyTarget { Side = SideType.PlayerSide, Alive = true, Gmo = gameObject, ReferenceId = "Target1" };
        var target2 = new DummyTarget { Side = SideType.PlayerSide, Alive = true, Gmo = gameObject, ReferenceId = "Target2" };

        context = new BasicEnemyContext
        {
            priority = 3,
            position = transform.position,
            self = selfTarget,
            targets = new List<ITargetable> { target1, target2 }
        };
    }

    private void Update()
    {
        if (graph == null || graph.root == null) return;

        if (Input.GetKeyDown(KeyCode.T))
            selfTarget.TakeDamage(1f, HealthModifyType.reduce);

        context.position = transform.position;

        NodeState result = graph.root.Execute(context);
        Debug.Log($"BT Result: {result} | Message: {context.outMessage}");
    }
}
