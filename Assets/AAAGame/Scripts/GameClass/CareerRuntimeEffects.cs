using System;
using System.Collections.Generic;

public static class CareerRuntimeEffects
{
    public static List<BuffData> CreateUnitBuffs(UnitType unitType, int ownerFactionId)
    {
        if (!ShouldApplyGrowth(ownerFactionId))
            return null;

        var modules = new List<BuffCallback>();
        bool isHero = unitType == UnitType.Unit_Hero;
        Fix64 attack = GetEffectValue(isHero
            ? MetaGrowthEffectType.HeroAttackPercent
            : MetaGrowthEffectType.UnitAttackPercent);
        Fix64 health = GetEffectValue(isHero
            ? MetaGrowthEffectType.HeroHealthPercent
            : MetaGrowthEffectType.UnitHealthPercent);
        if (attack != Fix64.Zero)
            modules.Add(new PercentAttackBonusBuff(attack));
        if (health != Fix64.Zero)
            modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, health));
        return CreatePermanentBuff("career_growth_unit", unitType.ToString(), modules);
    }

    public static List<BuffData> CreateBuildingBuffs(IBuildingLogicContext building)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (!ShouldApplyGrowth(building.OwnerFactionId))
            return null;

        var modules = new List<BuffCallback>();
        Fix64 attack = GetEffectValue(MetaGrowthEffectType.BuildingAttackPercent);
        Fix64 armor = GetEffectValue(MetaGrowthEffectType.BuildingArmor);
        Fix64 health = GetEffectValue(MetaGrowthEffectType.BuildingHealthPercent);
        if (attack != Fix64.Zero)
            modules.Add(new PercentAttackBonusBuff(attack));
        if (armor != Fix64.Zero)
            modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, armor));
        if (health != Fix64.Zero)
            modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, health));
        return CreatePermanentBuff("career_growth_building", building.BuildingData.Identifier, modules);
    }

    public static Fix64 GetVariableUnitMaxHealth()
    {
        if (!CareerRunSettings.HasActiveRun || !CareerRunSettings.IsVariableExperiment)
            return Fix64.Zero;
        VariableExperimentRuleTable rule = CareerRunSettings.ActiveRule
            ?? throw new InvalidOperationException("Active variable experiment has no rule config.");
        return rule.Identifier switch
        {
            "VariableRule_Default" => Fix64.Zero,
            "VariableRule_VariantTerrain" => Fix64.Zero,
            "VariableRule_OneHealthCoding" => GetRequiredRuleValue(rule, 0),
            _ => throw new InvalidOperationException(
                $"Variable experiment rule '{rule.Identifier}' has no runtime implementation.")
        };
    }

    private static Fix64 GetRequiredRuleValue(VariableExperimentRuleTable rule, int index)
    {
        if (rule.UniqueValues == null || index < 0 || index >= rule.UniqueValues.Length)
        {
            throw new InvalidOperationException(
                $"Variable experiment rule '{rule.Identifier}' requires UniqueValues[{index}].");
        }
        return rule.UniqueValues[index];
    }

    public static int GetInitialOrangeBonus()
    {
        return ToNonNegativeInt(GetEffectValue(MetaGrowthEffectType.InitialOrange));
    }

    public static int GetInitialFrequencyBonus()
    {
        return ToNonNegativeInt(GetEffectValue(MetaGrowthEffectType.InitialFrequency));
    }

    public static int GetCoreUpgradeCostDiscount(BuildingData buildingData, int ownerFactionId)
    {
        if (buildingData == null
            || buildingData.Type != BuilType.Base
            || buildingData.Lv <= 1
            || !ShouldApplyGrowth(ownerFactionId))
        {
            return 0;
        }
        return ToNonNegativeInt(GetEffectValue(MetaGrowthEffectType.CoreUpgradeCostDiscount));
    }

    public static bool ShouldGrantPeriodicOrange(int currentDay)
    {
        int interval = ToNonNegativeInt(GetEffectValue(MetaGrowthEffectType.PeriodicOrangeDays));
        return interval > 0 && currentDay > 0 && currentDay % interval == 0;
    }

    public static Fix64 GetEffectValue(MetaGrowthEffectType effectType)
    {
        if (!CareerRunSettings.HasActiveRun || CareerRunSettings.IsVariableExperiment)
            return Fix64.Zero;
        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        IReadOnlyList<MetaGrowthTable> rows = CareerConfigRuntime.GrowthRows;
        for (int i = 0; i < rows.Count; i++)
        {
            MetaGrowthTable row = rows[i];
            if (row.EffectType != effectType)
                continue;
            int level = progress.GetGrowthLevel(row.Identifier);
            if (level < 0 || level >= row.UniqueValues.Length)
                throw new InvalidOperationException($"Growth '{row.Identifier}' has invalid stored level {level}.");
            return row.UniqueValues[level];
        }
        throw new InvalidOperationException($"Meta growth effect '{effectType}' is not configured.");
    }

    private static bool ShouldApplyGrowth(int ownerFactionId)
    {
        return ownerFactionId == EntitySideHelper.PlayerFactionId
               && CareerRunSettings.HasActiveRun
               && !CareerRunSettings.IsVariableExperiment;
    }

    private static List<BuffData> CreatePermanentBuff(string prefix, string suffix, List<BuffCallback> modules)
    {
        if (modules.Count == 0)
            return null;
        return new List<BuffData>
        {
            BuffData.Create(
                $"{prefix}_{suffix}",
                Fix64.Zero,
                true,
                1,
                modules)
        };
    }

    private static int ToNonNegativeInt(Fix64 value)
    {
        int result = (int)value;
        if (result < 0 || (Fix64)result != value)
            throw new InvalidOperationException($"Career integer effect must be a non-negative integer. raw={value.RawValue}.");
        return result;
    }
}
