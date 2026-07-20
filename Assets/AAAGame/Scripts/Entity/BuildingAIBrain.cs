using UnityEngine;

/// <summary>
/// 建筑 AI：不移动，仅在目标进入射程后触发攻击输入。
/// </summary>
public class BuildingAIBrain : IControlBrain, ITickBrain
{
    public Vector2 Move => Vector2.zero;
    public FixVector2 MoveFixed => FixVector2.Zero;
    public bool Attack { get; private set; }
    public bool Skill1 => false;
    public bool Skill2 => false;
    public bool Skill3 => false;
    public bool Skill4 => false;
    public bool Skill5 => false;

    public void Tick(IEntityContext self, Fix64 dt)
    {
        Attack = false;

        if (self == null || !self.Alive)
            return;

        if (self is IBuildingLogicContext building && (building.HasPermanentNoAttackCapability || building.IsPhaseProtected))
            return;

        if (self.WeaponComp == null || self.WeaponComp.Data == null || self.WeaponComp.Data.Atk <= Fix64.Zero)
            return;

        var target = self.TargetComp?.CurrentTarget;
        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(self, target))
        {
            if (self.TargetComp != null)
                self.TargetComp.CurrentTarget = null;
            return;
        }

        Fix64 distance = LogicEntityFrameSnapshotService.GetRequiredTargetSurfaceDistance(self, target);
        Fix64 attackRange = GetEffectiveAttackRange(self);
        Attack = distance <= attackRange;
    }

    private Fix64 GetEffectiveAttackRange(IEntityContext self)
    {
        Fix64 weaponRange = self.WeaponComp != null ? self.WeaponComp.AttackRange : (Fix64)1.5f;
        return weaponRange;
    }
}
