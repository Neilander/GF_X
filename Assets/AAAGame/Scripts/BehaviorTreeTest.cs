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
        public Fix64 HealthValue { get; private set; } = (Fix64)10;

        public bool CanBeSelected() => Alive;
        public void InSelection(ISelector selector) { }
        public void DeSelection() { }
        public void TakeDamage(Fix64 damage, HealthModifyType modType,IEntityContext attacker = null)
        {
            HealthValue -= damage;
            if (HealthValue <= Fix64.Zero)
            {
                HealthValue = Fix64.Zero;
                Alive = false;
            }
            Debug.Log($"{ReferenceId} health: {(float)HealthValue}");
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
            selfTarget.TakeDamage((Fix64)1, HealthModifyType.reduce);

        context.position = transform.position;

        NodeState result = graph.root.Execute(context);
        Debug.Log($"BT Result: {result} | Message: {context.outMessage}");
    }
}
