using System;

public readonly struct TargetPriority : IComparable<TargetPriority>
{
    public TargetPriority(int taunt, bool inRange, int special, bool currentAttack, bool alert, Fix64 distance, int id)
    {
        DoubledTauntLevel = taunt;
        InsideAttackRange = inRange;
        SpecialTargetingPriority = special;
        CurrentAttackTarget = currentAttack;
        AlertTarget = alert;
        Distance = distance;
        TargetId = id;
    }

    public int DoubledTauntLevel { get; }
    public bool InsideAttackRange { get; }
    public int SpecialTargetingPriority { get; }
    public bool CurrentAttackTarget { get; }
    public bool AlertTarget { get; }
    public Fix64 Distance { get; }
    public int TargetId { get; }

    public int CompareTo(TargetPriority other)
    {
        int result = DoubledTauntLevel.CompareTo(other.DoubledTauntLevel);
        if (result != 0) return result;
        result = InsideAttackRange.CompareTo(other.InsideAttackRange);
        if (result != 0) return result;
        result = SpecialTargetingPriority.CompareTo(other.SpecialTargetingPriority);
        if (result != 0) return result;
        result = CurrentAttackTarget.CompareTo(other.CurrentAttackTarget);
        if (result != 0) return result;
        result = AlertTarget.CompareTo(other.AlertTarget);
        if (result != 0) return result;
        result = other.Distance.CompareTo(Distance);
        return result != 0 ? result : other.TargetId.CompareTo(TargetId);
    }
}

public static class TargetPriorityUtility
{
    public static TargetPriority Create(
        IEntityContext attacker,
        IEntityContext target,
        Fix64 distance,
        Fix64 attackRange,
        IEntityContext currentTarget,
        bool alertTarget)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (target == null) throw new ArgumentNullException(nameof(target));
        bool inRange = distance <= attackRange;
        int doubledTaunt = checked(target.TauntLevel * 2 - (target.IsLogicBuilding() ? 1 : 0));
        return new TargetPriority(
            doubledTaunt,
            inRange,
            GetSpecialTargetingPriority(attacker, target),
            inRange && ReferenceEquals(target, currentTarget),
            alertTarget,
            distance,
            target.LogicEntityId.Value);
    }

    private static int GetSpecialTargetingPriority(IEntityContext attacker, IEntityContext target)
    {
        if (!attacker.TryGetLogicBuilding(out IBuildingLogicContext building)
            || !BuildingAbilityIds.IsBuilding(building.BuildingData, BuildingAbilityIds.ComplaintsDepartment)
            || !BuildingTechRuntimeEffect.HasTag(target.CharacterData?.UnitTags, UnitTag.Ranged))
            return 0;

        Fix64[] values = building.BuildingData.UniqueValues;
        if (values == null || values.Length == 0 || values[0] <= Fix64.Zero || values[0] != Fix64.Floor(values[0]))
            throw new InvalidOperationException($"Complaints department ranged targeting priority is invalid. building={building.BuildingData.Identifier}.");
        return checked((int)values[0]);
    }
}
