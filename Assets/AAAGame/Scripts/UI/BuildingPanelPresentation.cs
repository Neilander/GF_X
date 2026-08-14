using System;
using System.Collections.Generic;

public readonly struct BuildingPanelStat
{
    public BuildingPanelStat(string glyph, string value)
    {
        Glyph = glyph;
        Value = value;
    }

    public string Glyph { get; }
    public string Value { get; }
}

public static class BuildingPanelPresentation
{
    public const string HealthGlyph = "HP";
    public const string DefenseGlyph = "DEF";
    public const string AttackGlyph = "ATK";
    public const string IntervalGlyph = "INT";
    public const string RangeGlyph = "RNG";
    public const string MoveSpeedGlyph = "SPD";
    public const string SplashGlyph = "AOE";
    public const string AmmoGlyph = "AMMO";
    public const string ProjectileCountGlyph = "SHOT";
    public const string SplitAngleGlyph = "ANG";
    public const string SplitDistanceGlyph = "DIST";
    public const string CriticalGlyph = "CRIT";
    public const string HealOnHitGlyph = "HEAL";
    public const string CastDistanceGlyph = "CAST";
    public const string DurationGlyph = "DUR";
    public const string UsageGlyph = "USE";
    public const string CooldownGlyph = "CD";
    public const string StackGlyph = "STACK";

    public static void CollectBuildingCombatStats(
        BuildingData data,
        BuildingEntity runtimeBuilding,
        List<BuildingPanelStat> results)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        Fix64 health = runtimeBuilding != null
            ? RequireRuntimeProperties(runtimeBuilding).GetProperty(CreatureMainProperty.Health)
            : data.HP;
        Fix64 defense = runtimeBuilding != null
            ? RequireRuntimeProperties(runtimeBuilding).GetProperty(CreatureMainProperty.Def)
            : data.Def;
        results.Add(new BuildingPanelStat(HealthGlyph, "B " + health));
        results.Add(new BuildingPanelStat(DefenseGlyph, "B " + defense));

        if (data.Weapon == null || data.Weapon.Type == WeaponType.None || data.Weapon.Atk <= Fix64.Zero)
            return;

        Weapon runtimeWeapon = runtimeBuilding?.weaponComp?.Data;
        if (runtimeBuilding != null && runtimeWeapon == null)
            throw new InvalidOperationException($"Attacking building is missing its runtime weapon. building={data.Identifier}.");

        results.Add(new BuildingPanelStat(AttackGlyph, "B " + (runtimeWeapon?.Atk ?? data.Weapon.Atk)));
        results.Add(new BuildingPanelStat(IntervalGlyph, "B " + ResolveBuildingInterval(data, runtimeWeapon)));
        results.Add(new BuildingPanelStat(RangeGlyph, "B " + (runtimeWeapon?.Range ?? data.Weapon.Range)));

        if (runtimeWeapon != null)
            AddWeaponAbilityStats(runtimeWeapon, "B ", results);
        else
            AddWeaponAbilityStats(data.Weapon, "B ", results);
    }

    public static void CollectUnitStats(BuildingData data, List<BuildingPanelStat> results)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (data.Type != BuilType.Army)
            return;

        CharacterDataDetail unit = GetRequiredUnit(data);
        int level = Math.Max(1, Math.Min(3, data.Lv));
        WeaponData weapon = CharacterDataDetailAccessor.GetWeaponDataByIndex(unit, 0, level);
        Fix64 attackSpeedPercent = SoldierFactory.ResolveArmyPresentationAttackSpeedPercent(
            ParseUnitType(data.UnitID),
            level);
        Fix64 interval = attackSpeedPercent == Fix64.Zero
            ? weapon.Interval
            : weapon.Interval / (Fix64.One + attackSpeedPercent / (Fix64)100);

        results.Add(new BuildingPanelStat(
            HealthGlyph,
            "U " + CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Health, level)));
        results.Add(new BuildingPanelStat(
            DefenseGlyph,
            "U " + CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Def, level)));
        results.Add(new BuildingPanelStat(AttackGlyph, "U " + weapon.Atk));
        results.Add(new BuildingPanelStat(IntervalGlyph, "U " + interval));
        results.Add(new BuildingPanelStat(RangeGlyph, "U " + weapon.Range));
        results.Add(new BuildingPanelStat(
            MoveSpeedGlyph,
            "U " + CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Speed, level)));

        AddWeaponAbilityStats(weapon, "U ", results);

        Fix64 criticalDamageBonus = SoldierFactory.ResolveArmyPresentationCriticalDamageBonusPercent(
            ParseUnitType(data.UnitID),
            level);
        if (HasIntrinsicCriticalAbility(data.UnitID))
        {
            results.Add(new BuildingPanelStat(
                CriticalGlyph,
                "U +" + (CriticalDamageUtility.BaseCriticalDamageRate + criticalDamageBonus) + "%"));
        }

        Fix64 healOnHit = SoldierFactory.ResolveArmyPresentationHealOnHit(ParseUnitType(data.UnitID), level);
        if (healOnHit > Fix64.Zero)
            results.Add(new BuildingPanelStat(HealOnHitGlyph, "U +" + healOnHit));
    }

    public static string GetDescription(BuildingData data)
    {
        if (data == null)
            return string.Empty;

        string description = DescriptionValueFormatter.LocalizeAndFill(
            data.DescKey,
            ResolveBuildingDescriptionValues(data));
        if (data.Type != BuilType.Army)
            return description;

        CharacterDataDetail unit = GetRequiredUnit(data);
        UnitType unitType = ParseUnitType(data.UnitID);
        Fix64[] abilityValues = SoldierFactory.ResolveArmyPresentationAbilityValues(
            unitType,
            Math.Max(1, Math.Min(3, data.Lv)));
        string unitName = LocalizationTextManager.GetLocalizedText(unit.NameKey, false);
        string unitDescription = DescriptionValueFormatter.LocalizeAndFill(unit.DescKey, abilityValues);
        if (string.IsNullOrWhiteSpace(unitDescription))
            return description;

        return string.IsNullOrWhiteSpace(description)
            ? unitName + ": " + unitDescription
            : description + "\n" + unitName + ": " + unitDescription;
    }

    public static void CollectSkillStats(SkillData skill, int level, List<BuildingPanelStat> results)
    {
        if (skill == null)
            throw new ArgumentNullException(nameof(skill));
        if (level <= 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        Fix64 castDistance = skill.GetCastDistance(level);
        Fix64 areaRange = skill.GetAreaRange(level);
        Fix64 duration = skill.GetDuration(level);
        Fix64 cooldown = skill.GetCooldown(level);
        if (castDistance > Fix64.Zero)
            results.Add(new BuildingPanelStat(CastDistanceGlyph, castDistance.ToString()));
        if (areaRange > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplashGlyph, areaRange.ToString()));
        if (duration > Fix64.Zero)
            results.Add(new BuildingPanelStat(DurationGlyph, duration.ToString()));
        if (skill.Type == SkillType.Active)
        {
            int usage = skill.Lv1UsageCount + skill.UpgradeIncrementUsageCount * (level - 1);
            if (usage <= 0)
                throw new InvalidOperationException($"Active skill has invalid usage count. skill={skill.Identifier}, level={level}");
            results.Add(new BuildingPanelStat(UsageGlyph, usage.ToString()));
            if (cooldown <= Fix64.Zero)
                throw new InvalidOperationException($"Active skill has invalid cooldown. skill={skill.Identifier}, level={level}");
            results.Add(new BuildingPanelStat(CooldownGlyph, cooldown.ToString()));
        }

        if (skill.Identifier == "Skill_ForgedInFire")
            results.Add(new BuildingPanelStat(StackGlyph, skill.GetUniqueValue(1, level).ToString()));
    }

    public static string GetSkillTooltip(SkillRuntimeInfo info)
    {
        SkillData skill = info.Data ?? throw new ArgumentException("Skill runtime info has no data.", nameof(info));
        string name = LocalizationTextManager.GetLocalizedText(skill.NameKey, false);
        string description = skill.GetFormattedDesc(info.Level);
        var stats = new List<BuildingPanelStat>();
        CollectSkillStats(skill, info.Level, stats);
        var parts = new List<string>(stats.Count);
        for (int i = 0; i < stats.Count; i++)
            parts.Add(stats[i].Glyph + " " + stats[i].Value);
        return parts.Count > 0
            ? $"{name} Lv{info.Level}\n{description}\n{string.Join("   ", parts)}"
            : $"{name} Lv{info.Level}\n{description}";
    }

    public static string FormatBaseWithTotalModifier(int baseValue, int actualValue)
    {
        int modifier = actualValue - baseValue;
        return modifier == 0
            ? baseValue.ToString()
            : baseValue + (modifier > 0 ? "+" : string.Empty) + modifier;
    }

    public static string GetBaseSupplyText(BuildingData data)
    {
        if (data == null || data.Type != BuilType.Base)
            throw new ArgumentException("Base supply display requires a base building.", nameof(data));

        int modifierPerLevel = LevelTagRuntime.GetBaseProvideSupplyPerLevelDelta();
        int actualPerLevel = InGameDataModel.GetBaseProvideSupplyPerLevel();
        int baseValue = (actualPerLevel - modifierPerLevel) * data.Lv;
        int actualValue = actualPerLevel * data.Lv;
        return FormatBaseWithTotalModifier(baseValue, actualValue);
    }

    public static string GetProductionText(BuildingData data, BuildingEntity runtimeBuilding)
    {
        if (data == null || data.Type != BuilType.Prod)
            throw new ArgumentException("Production display requires a production building.", nameof(data));

        int actual = runtimeBuilding != null ? runtimeBuilding.GetProduction() : data.Production;
        return FormatBaseWithTotalModifier(data.Production, actual);
    }

    public static string GetArmyForceText(BuildingData data, BuildingEntity runtimeBuilding)
    {
        if (data == null || data.Type != BuilType.Army)
            throw new ArgumentException("Army force display requires an army building.", nameof(data));

        int actual = runtimeBuilding != null ? runtimeBuilding.GetArmyForce() : data.Production;
        return FormatBaseWithTotalModifier(data.Production, actual);
    }

    public static string GetArmySupplyText(BuildingData data, BuildingEntity runtimeBuilding)
    {
        if (data == null || data.Type != BuilType.Army)
            throw new ArgumentException("Army supply display requires an army building.", nameof(data));

        CharacterDataDetail unit = GetRequiredUnit(data);
        int actual = runtimeBuilding != null ? runtimeBuilding.GetArmySupplyPerUnit() : unit.Supply;
        return FormatBaseWithTotalModifier(unit.Supply, actual);
    }

    private static CreaturePropertyManager RequireRuntimeProperties(BuildingEntity building)
    {
        return building.CreaturePropertyManager
               ?? throw new InvalidOperationException($"Building panel requires runtime properties. building={building.BuildingInstanceId}.");
    }

    private static CharacterDataDetail GetRequiredUnit(BuildingData data)
    {
        if (string.IsNullOrWhiteSpace(data.UnitID))
            throw new InvalidOperationException($"Army building is missing UnitID. building={data.Identifier}.");
        if (!LogicRuntimeDataTableCache.TryGetCharacter(data.UnitID, out CharacterDataDetail unit))
            throw new InvalidOperationException($"Army building unit data is missing. building={data.Identifier}, unit={data.UnitID}.");
        return unit;
    }

    private static UnitType ParseUnitType(string unitId)
    {
        if (!Enum.TryParse(unitId, out UnitType unitType))
            throw new InvalidOperationException($"Army building UnitID is not a UnitType. unit={unitId}.");
        return unitType;
    }

    internal static Fix64[] ResolveBuildingDescriptionValues(BuildingData data)
    {
        Fix64[] values = data.UniqueValues != null ? (Fix64[])data.UniqueValues.Clone() : Array.Empty<Fix64>();
        if (data.Lv <= 1 || values.Length == 0)
            return values;

        string baseIdentifier = StripLevel(data.Identifier);
        if (!LogicRuntimeDataTableCache.TryGetBuilding(baseIdentifier, out BuildingTable row))
            throw new InvalidOperationException($"Building panel cannot resolve source row. building={data.Identifier}.");

        Fix64 lv2 = data.Lv >= 2 ? FirstValue(row.Tech1UniqueValues) : Fix64.Zero;
        Fix64 lv3 = data.Lv >= 3 ? FirstValue(row.Tech2UniqueValues) : Fix64.Zero;
        Fix64 cumulative = lv2 + lv3;

        if (baseIdentifier == "Buil_MiningRig"
            || baseIdentifier == "Buil_SouvenirStand"
            || baseIdentifier == "Buil_CampfireGrill"
            || baseIdentifier == "Buil_Nursery"
            || baseIdentifier == "Buil_ReceptionDesk")
        {
            RequireValueIndex(values, 2, data.Identifier);
            values[2] += cumulative;
        }
        else if (baseIdentifier == "Buil_FreshMarket")
        {
            RequireValueIndex(values, 1, data.Identifier);
            values[1] += cumulative;
        }
        else if (baseIdentifier == "Buil_TicketBooth")
        {
            values[0] -= cumulative;
        }
        else if (baseIdentifier == "Buil_SupplyStation")
        {
            Fix64 remaining = Fix64.One - values[0] / (Fix64)100;
            if (lv2 > Fix64.Zero)
                remaining *= Fix64.One - lv2 / (Fix64)100;
            if (lv3 > Fix64.Zero)
                remaining *= Fix64.One - lv3 / (Fix64)100;
            values[0] = (Fix64.One - remaining) * (Fix64)100;
        }
        else if (baseIdentifier == "Buil_ThemeStatue")
        {
            values[0] -= lv3;
        }
        else if (baseIdentifier == "Buil_BallLauncher")
        {
            values[0] -= data.Lv >= 3 ? ValueAt(row.Tech2UniqueValues, 1) : Fix64.Zero;
        }

        return values;
    }

    private static Fix64 ResolveBuildingInterval(BuildingData data, Weapon runtimeWeapon)
    {
        if (runtimeWeapon != null)
            return runtimeWeapon.Interval;

        Fix64 interval = data.Weapon.Interval;
        if (data.Lv <= 1)
            return interval;

        string baseIdentifier = StripLevel(data.Identifier);
        if (!LogicRuntimeDataTableCache.TryGetBuilding(baseIdentifier, out BuildingTable row))
            throw new InvalidOperationException($"Building panel cannot resolve source row. building={data.Identifier}.");

        if (baseIdentifier == "Buil_RoseBush")
        {
            if (data.Lv >= 2)
                interval /= Fix64.One + FirstValue(row.Tech1UniqueValues) / (Fix64)100;
            if (data.Lv >= 3)
                interval /= Fix64.One + FirstValue(row.Tech2UniqueValues) / (Fix64)100;
        }
        else if (baseIdentifier == "Buil_BallLauncher" && data.Lv >= 3)
        {
            interval /= Fix64.One + FirstValue(row.Tech2UniqueValues) / (Fix64)100;
        }

        return interval;
    }

    private static void AddWeaponAbilityStats(
        WeaponData weapon,
        string prefix,
        List<BuildingPanelStat> results)
    {
        if (weapon.SplashRadius > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplashGlyph, prefix + weapon.SplashRadius));
        if (weapon.AmmunitionCapacity > Fix64.Zero)
            results.Add(new BuildingPanelStat(AmmoGlyph, prefix + weapon.AmmunitionCapacity));
        if (weapon.ProjectileCount > Fix64.One)
            results.Add(new BuildingPanelStat(ProjectileCountGlyph, prefix + weapon.ProjectileCount));
        if (weapon.SplitAngle > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitAngleGlyph, prefix + weapon.SplitAngle));
        if (weapon.SplitDist > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitDistanceGlyph, prefix + weapon.SplitDist));
    }

    private static void AddWeaponAbilityStats(
        Weapon weapon,
        string prefix,
        List<BuildingPanelStat> results)
    {
        if (weapon.SplashRadius > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplashGlyph, prefix + weapon.SplashRadius));
        if (weapon.AmmunitionCapacity > Fix64.Zero)
            results.Add(new BuildingPanelStat(AmmoGlyph, prefix + weapon.AmmunitionCapacity));
        if (weapon.ProjectileCount > Fix64.One)
            results.Add(new BuildingPanelStat(ProjectileCountGlyph, prefix + weapon.ProjectileCount));
        if (weapon.SplitAngle > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitAngleGlyph, prefix + weapon.SplitAngle));
        if (weapon.SplitDist > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitDistanceGlyph, prefix + weapon.SplitDist));
    }

    private static bool HasIntrinsicCriticalAbility(string unitId)
    {
        return unitId == UnitType.Unit_Poacher.ToString()
               || unitId == UnitType.Unit_Gardener.ToString()
               || unitId == UnitType.Unit_JavelinThrower.ToString();
    }

    private static string StripLevel(string identifier)
    {
        int index = identifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        if (index <= 0)
            throw new InvalidOperationException($"Building identifier has no level suffix. building={identifier}.");
        return identifier.Substring(0, index);
    }

    private static Fix64 FirstValue(Fix64[] values) =>
        values != null && values.Length > 0 ? values[0] : Fix64.Zero;

    private static Fix64 ValueAt(Fix64[] values, int index) =>
        values != null && index >= 0 && index < values.Length ? values[index] : Fix64.Zero;

    private static void RequireValueIndex(Fix64[] values, int index, string identifier)
    {
        if (values == null || index < 0 || index >= values.Length)
            throw new InvalidOperationException($"Building description value is missing. building={identifier}, index={index}.");
    }
}
