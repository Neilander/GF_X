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
    public const string MeleeRangeTextId = "Building_Unit_Range_Melee";
    public const string BaseDescriptionTextId = "Building_Description_Base_Format";
    public const string ArmyDescriptionTextId = "Building_Description_Army_Format";

    public static string GetBuildingLevelTitle(int level)
    {
        switch (level)
        {
            case 1:
                return "I";
            case 2:
                return "II";
            case 3:
                return "III";
            default:
                throw new ArgumentOutOfRangeException(nameof(level), level, "Building panel level must be between 1 and 3.");
        }
    }

    public static string GetUpgradeLevelTitle(int currentLevel)
    {
        if (currentLevel < 1 || currentLevel >= 3)
            throw new ArgumentOutOfRangeException(nameof(currentLevel), currentLevel, "Upgradeable building level must be 1 or 2.");

        return GetBuildingLevelTitle(currentLevel) + "\u2192" + GetBuildingLevelTitle(currentLevel + 1);
    }

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
        results.Add(new BuildingPanelStat(HealthGlyph, FormatStatValue(health)));
        results.Add(new BuildingPanelStat(DefenseGlyph, FormatStatValue(defense)));

        if (data.Weapon == null || data.Weapon.Type == WeaponType.None || data.Weapon.Atk <= Fix64.Zero)
            return;

        Weapon runtimeWeapon = runtimeBuilding?.weaponComp?.Data;
        if (runtimeBuilding != null && runtimeWeapon == null)
            throw new InvalidOperationException($"Attacking building is missing its runtime weapon. building={data.Identifier}.");

        results.Add(new BuildingPanelStat(AttackGlyph, FormatStatValue(runtimeWeapon?.Atk ?? data.Weapon.Atk)));
        results.Add(new BuildingPanelStat(IntervalGlyph, FormatStatValue(ResolveBuildingInterval(data, runtimeWeapon))));
        results.Add(new BuildingPanelStat(RangeGlyph, FormatStatValue(runtimeWeapon?.Range ?? data.Weapon.Range)));

        if (runtimeWeapon != null)
            AddWeaponAbilityStats(runtimeWeapon, results);
        else
            AddWeaponAbilityStats(data.Weapon, results);
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
            FormatStatValue(CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Health, level))));
        results.Add(new BuildingPanelStat(
            DefenseGlyph,
            FormatStatValue(CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Def, level))));
        results.Add(new BuildingPanelStat(AttackGlyph, FormatStatValue(weapon.Atk)));
        results.Add(new BuildingPanelStat(IntervalGlyph, FormatStatValue(interval)));
        results.Add(new BuildingPanelStat(RangeGlyph, FormatUnitRange(weapon)));
        results.Add(new BuildingPanelStat(
            MoveSpeedGlyph,
            FormatStatValue(CharacterDataDetailAccessor.GetMainValue(unit, CreatureMainProperty.Speed, level))));

        AddWeaponAbilityStats(weapon, results);

        Fix64 criticalDamageBonus = SoldierFactory.ResolveArmyPresentationCriticalDamageBonusPercent(
            ParseUnitType(data.UnitID),
            level);
        if (HasIntrinsicCriticalAbility(data.UnitID))
        {
            results.Add(new BuildingPanelStat(
                CriticalGlyph,
                "+" + FormatStatValue(CriticalDamageUtility.BaseCriticalDamageRate + criticalDamageBonus) + "%"));
        }

        Fix64 healOnHit = SoldierFactory.ResolveArmyPresentationHealOnHit(ParseUnitType(data.UnitID), level);
        if (healOnHit > Fix64.Zero)
            results.Add(new BuildingPanelStat(HealOnHitGlyph, "+" + FormatStatValue(healOnHit)));
    }

    internal static string FormatUnitRange(WeaponData weapon)
    {
        if (weapon == null)
            throw new ArgumentNullException(nameof(weapon));

        return IsMeleeWeaponType(weapon.Type)
            ? LocalizationTextDataModel.GetText(MeleeRangeTextId, applyRichText: false)
            : FormatStatValue(weapon.Range);
    }

    internal static bool IsMeleeWeaponType(WeaponType type)
    {
        return type.ToString().IndexOf("Melee", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string GetDescription(BuildingData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Arche == Archetype.None)
        {
            return DescriptionValueFormatter.LocalizeAndFill(
                data.DescKey,
                ResolveBuildingDescriptionValues(data));
        }

        string archetypeName = ResolveArchetypeName(data.Arche);
        if (data.Type == BuilType.Base)
        {
            return FillBuildingDescriptionTemplate(
                LocalizationTextDataModel.GetText(BaseDescriptionTextId),
                archetypeName,
                data.Lv,
                string.Empty);
        }

        if (data.Type == BuilType.Army)
        {
            return FillBuildingDescriptionTemplate(
                LocalizationTextDataModel.GetText(ArmyDescriptionTextId),
                archetypeName,
                data.Lv,
                GetUnitName(data));
        }

        string description = DescriptionValueFormatter.LocalizeAndFill(
            data.DescKey,
            ResolveBuildingDescriptionValues(data));
        return FillBuildingDescriptionTemplate(description, archetypeName, data.Lv, string.Empty);
    }

    public static string GetBuildingName(BuildingData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        string name = LocalizationTextManager.GetLocalizedText(data.NameKey, false);
        return FormatBuildingName(name, data.Lv);
    }

    internal static string FormatBuildingName(string name, int level)
    {
        return level <= 1 ? name : $"{name}-Lv{level}";
    }

    internal static string FormatSkillName(string name, int level)
    {
        if (level <= 0)
            throw new ArgumentOutOfRangeException(nameof(level));

        return LocalizationTextManager.ProcessText($"{name}-Lv{level}");
    }

    internal static string FillBuildingDescriptionTemplate(
        string template,
        string archetypeName,
        int level,
        string unitName)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(archetypeName))
            throw new ArgumentException("Building description requires a localized archetype name.", nameof(archetypeName));

        return template
            .Replace("{Arch}", archetypeName)
            .Replace("{Lv}", level.ToString())
            .Replace("{Unit}", unitName ?? string.Empty);
    }

    public static string ResolveArchetypeName(Archetype archetype)
    {
        if (archetype == Archetype.None)
            throw new ArgumentException("Building description requires a concrete archetype.", nameof(archetype));

        return LocalizationTextDataModel.GetText($"Archetype_{archetype}");
    }

    internal static string StripUnitDescriptionSuffix(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        int fullWidthColon = description.IndexOf('：');
        int asciiColon = description.IndexOf(':');
        int separatorIndex = fullWidthColon < 0
            ? asciiColon
            : asciiColon < 0 ? fullWidthColon : Math.Min(fullWidthColon, asciiColon);
        return separatorIndex < 0
            ? description
            : description.Substring(0, separatorIndex).TrimEnd();
    }

    public static string GetUnitName(BuildingData data)
    {
        if (data == null || data.Type != BuilType.Army)
            return string.Empty;

        CharacterDataDetail unit = GetRequiredUnit(data);
        string name = LocalizationTextManager.GetLocalizedText(unit.NameKey, false);
        return LocalizationTextManager.ProcessText(FormatBuildingName(name, data.Lv));
    }

    public static string GetUnitDescription(BuildingData data)
    {
        if (data == null || data.Type != BuilType.Army)
            return string.Empty;

        CharacterDataDetail unit = GetRequiredUnit(data);
        UnitType unitType = ParseUnitType(data.UnitID);
        Fix64[] abilityValues = SoldierFactory.ResolveArmyPresentationAbilityValues(
            unitType,
            Math.Max(1, Math.Min(3, data.Lv)));
        return DescriptionValueFormatter.LocalizeAndFill(unit.DescKey, abilityValues);
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
        string name = FormatSkillName(
            LocalizationTextManager.GetLocalizedText(skill.NameKey, false),
            info.Level);
        string description = skill.GetFormattedDesc(info.Level);
        var stats = new List<BuildingPanelStat>();
        CollectSkillStats(skill, info.Level, stats);
        var parts = new List<string>(stats.Count);
        for (int i = 0; i < stats.Count; i++)
            parts.Add(stats[i].Glyph + " " + stats[i].Value);
        return parts.Count > 0
            ? $"{name}\n{description}\n{string.Join("   ", parts)}"
            : $"{name}\n{description}";
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
        List<BuildingPanelStat> results)
    {
        if (weapon.SplashRadius > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplashGlyph, FormatStatValue(weapon.SplashRadius)));
        if (weapon.AmmunitionCapacity > Fix64.Zero)
            results.Add(new BuildingPanelStat(AmmoGlyph, FormatStatValue(weapon.AmmunitionCapacity)));
        if (weapon.ProjectileCount > Fix64.One)
            results.Add(new BuildingPanelStat(ProjectileCountGlyph, FormatStatValue(weapon.ProjectileCount)));
        if (weapon.SplitAngle > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitAngleGlyph, FormatStatValue(weapon.SplitAngle)));
        if (weapon.SplitDist > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitDistanceGlyph, FormatStatValue(weapon.SplitDist)));
    }

    private static void AddWeaponAbilityStats(
        Weapon weapon,
        List<BuildingPanelStat> results)
    {
        if (weapon.SplashRadius > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplashGlyph, FormatStatValue(weapon.SplashRadius)));
        if (weapon.AmmunitionCapacity > Fix64.Zero)
            results.Add(new BuildingPanelStat(AmmoGlyph, FormatStatValue(weapon.AmmunitionCapacity)));
        if (weapon.ProjectileCount > Fix64.One)
            results.Add(new BuildingPanelStat(ProjectileCountGlyph, FormatStatValue(weapon.ProjectileCount)));
        if (weapon.SplitAngle > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitAngleGlyph, FormatStatValue(weapon.SplitAngle)));
        if (weapon.SplitDist > Fix64.Zero)
            results.Add(new BuildingPanelStat(SplitDistanceGlyph, FormatStatValue(weapon.SplitDist)));
    }

    private static string FormatStatValue(Fix64 value) => value.ToStringRound();

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
