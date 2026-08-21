using System;
using System.Collections.Generic;

public sealed class SimBuildingEntityContext : SimEntityContext, IBuildingLogicContext
{
    public SimBuildingEntityContext(int ownerFactionId)
    {
        OwnerFactionId = ownerFactionId;
        BuildingData = new BuildingData(
            "TestBuilding", BuilType.Def, Archetype.None, string.Empty, string.Empty, string.Empty,
            1, 0, (Fix64)100, null, Fix64.Zero, Array.Empty<Fix64>(), null, 0, Array.Empty<string>());
    }

    public override bool IsBuildingEntity => true;
    public BuildingData BuildingData { get; }
    public BuildingExtraProps ProductionProps => null;
    public string BuildingInstanceId => "test-building";
    public string StrongholdId => "test-stronghold";
    public int OwnerFactionId { get; private set; }
    public int GetArmyForce() => 0;
    public int GetArmyForceWithoutRuntimeRules() => 0;
    public int GetArmySupplyPerUnit() => 0;
    public int GetArmyOccupiedSupply() => 0;
    public void SetArmyForceBase(int value) { }
    public void SetArmySupplyPerUnitBase(int value) { }
    public void ModifyArmyForce(IPropertyModifier modifier, bool ifAdd = true) { }
    public void ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd = true) { }
    public IReadOnlyList<LogicInteractionOptionDescriptor> InteractionOptions => Array.Empty<LogicInteractionOptionDescriptor>();
    public bool IsDisabled => false;
    public bool IsPhaseProtected => false;
    public bool IsPermanentlyInvincible => false;
    public bool HasPermanentNoAttackCapability => false;
    public bool BlocksLogicMovement => false;
    public IReadOnlyList<LogicCombatShape> LogicObstacleShapes => Array.Empty<LogicCombatShape>();
    public bool IsGameEndConditionBuilding => false;
    public bool IsNavigationStaticBaked => false;
    public event Action<int, int> OwnerFactionChanged;

    public void SetOwnerFaction(int ownerFactionId)
    {
        int previous = OwnerFactionId;
        OwnerFactionId = ownerFactionId;
        OwnerFactionChanged?.Invoke(previous, ownerFactionId);
    }

    public void SetGameEndConditionBuilding(bool enabled) { }
    public void RestoreBuildingToFullHealth() { }
    public void SetCollisionBlockingByBuff(bool blocksMovement) { }
    public void SetPermanentInvincibilityByBuff(bool enabled) { }
    public void SetPhaseProtectionByBuff(bool enabled) { }
}
