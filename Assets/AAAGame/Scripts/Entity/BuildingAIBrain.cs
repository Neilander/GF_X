using UnityEngine;

/// <summary>
/// 建筑 AI：不移动，仅在目标进入射程后触发攻击输入。
/// </summary>
public class BuildingAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move => Vector2.zero;
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;

    public void Tick(IEntityContext self, float dt)
    {
        Attack = false;

        if (self == null || !self.Alive)
            return;

        if (self is BuildingEntity building && (building.HasPermanentNoAttackCapability || building.IsPhaseProtected))
            return;

        if (self.GetProperty(CreatureMainProperty.PhysicalAtk) <= Fix64.Zero)
            return;

        var target = self.TargetComp?.CurrentTarget;
        if (!target.IsAttackTargetable())
        {
            if (self.TargetComp != null)
                self.TargetComp.CurrentTarget = null;
            return;
        }

        if (!EntityCombatTeamHelper.IsEnemy(self, target))
        {
            if (self.TargetComp != null)
                self.TargetComp.CurrentTarget = null;
            return;
        }

        float distance = Vector3.Distance(self.Position, target.Position);
        float attackRange = GetEffectiveAttackRange(self);
        Attack = distance <= attackRange;
    }

    private float GetEffectiveAttackRange(IEntityContext self)
    {
        float equilibriumRadius = 0f;
        if (GroupMoveManager.HasInstance)
        {
            int selfId = (self as MAEntity)?.GetInstanceID() ?? self.GetHashCode();
            equilibriumRadius = GroupMoveManager.Instance.Coordinator.GetAgentEquilibriumRadius(selfId);
        }

        float weaponRange = self.WeaponComp != null ? (float)self.WeaponComp.AttackRange : 1.5f;
        return equilibriumRadius + weaponRange;
    }
}
