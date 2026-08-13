using System.Collections.Generic;
using UnityEngine;

public class BasicEnemyContext : IBTContext
{
    public int priority { get; set; }
    
    public Vector3 position { get; set; }
    public ITargetable self { get; set; }
    public List<ITargetable> targets { get; set; } = new List<ITargetable>();
    
    public string outMessage { get; set; }
}

public class DefendEnemyContext : BasicEnemyContext
{
    public Transform defendBase { get; set; }
}