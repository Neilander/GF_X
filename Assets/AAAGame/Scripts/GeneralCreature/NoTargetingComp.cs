using UnityEngine;

public class NoTargetingComp : ITargetingComp
{
    public IEntityContext CurrentTarget { get; set; }
    public IEntityContext AggroTarget => null;
    public void Init(IEntityContext ctx) { }

    public void UpdateTargeting(Fix64 deltaTime) { }

    public void ShutDown() { }

    public void Resume() { }

    public void ClearAggro() { CurrentTarget = null; }
}
