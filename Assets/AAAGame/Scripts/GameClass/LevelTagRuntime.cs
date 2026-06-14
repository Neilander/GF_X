using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class LevelTagRuntime
{
    private static readonly HashSet<int> s_ActiveTagIds = new();
    private static readonly HashSet<string> s_ActiveTagIdentifiers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_WarnedUnsupportedTags = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, HeroReviveState> s_HeroReviveStatesByEntityId = new();

    private struct HeroReviveState
    {
        public int Day;
        public int Count;
    }

    public static void SetActiveTagIds(IEnumerable<int> tagIds)
    {
        s_ActiveTagIds.Clear();
        s_ActiveTagIdentifiers.Clear();
        if (tagIds == null)
            return;

        foreach (int tagId in tagIds)
        {
            if (tagId > 0)
                s_ActiveTagIds.Add(tagId);
        }
    }

    public static void SetActiveTagIdentifiers(IEnumerable<string> identifiers)
    {
        s_ActiveTagIds.Clear();
        s_ActiveTagIdentifiers.Clear();
        if (identifiers == null)
            return;

        foreach (string identifier in identifiers)
        {
            if (!string.IsNullOrWhiteSpace(identifier))
                s_ActiveTagIdentifiers.Add(identifier);
        }
    }

    public static void ClearActiveTags()
    {
        s_ActiveTagIds.Clear();
        s_ActiveTagIdentifiers.Clear();
        s_WarnedUnsupportedTags.Clear();
        s_HeroReviveStatesByEntityId.Clear();
    }

    public static int GetHeroSkillLevelBonus()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_Gifted")
                total += IntValue(tag, 0);
        }

        return Math.Max(0, total);
    }

    public static int GetCurrentSettlementOffsetRateDelta()
    {
        return SettlementOffsetRateService.GetCurrentOffsetRateDelta();
    }

    public static IReadOnlyList<LevelTagTable> GetActiveTags()
    {
        return ResolveActiveTags();
    }

    public static List<BuffData> CreateUnitBuffs(UnitType unitType, int ownerFactionId)
    {
        CharacterDataDetail row = FindCharacterData(unitType.ToString());
        if (row == null)
            return null;

        var modules = new List<BuffCallback>();
        foreach (LevelTagTable tag in ResolveActiveTags())
            AddUnitModules(tag, row, unitType, ownerFactionId, modules);

        if (modules.Count == 0)
            return null;

        return new List<BuffData>
        {
            BuffData.Create(
                id: $"level_tag_unit_{ownerFactionId}_{unitType}",
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: modules)
        };
    }

    public static List<BuffData> CreateBuildingBuffs(BuildingEntity building)
    {
        if (building?.buildingData == null)
            return null;

        var modules = new List<BuffCallback>();
        foreach (LevelTagTable tag in ResolveActiveTags())
            AddBuildingModules(tag, building, modules);

        var result = new List<BuffData>();
        if (modules.Count > 0)
        {
            result.Add(BuffData.Create(
                id: $"level_tag_building_{building.OwnerFactionID}_{building.BuildingInstanceId}",
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: modules));
        }

        return result.Count > 0 ? result : null;
    }

    public static List<BuffData> CreateUnitBuffsFromSourceBuilding(BuildingEntity sourceBuilding)
    {
        if (sourceBuilding?.BuffComp is not CharacterBuffComp buffComp)
            return null;

        var modules = new List<BuffCallback>();
        foreach (BuffCallback module in buffComp.EnumerateAllModules())
        {
            if (module is not ISourceBuildingUnitBuffProvider provider || !provider.CanProvideUnitBuffs())
                continue;

            provider.CreateUnitBuffModules(modules);
        }

        if (modules.Count == 0)
            return null;

        return new List<BuffData>
        {
            BuffData.Create(
                id: $"level_tag_source_building_unit_{sourceBuilding.BuildingInstanceId}",
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: modules)
        };
    }

    public static void ApplyCapturedStrongholdTrainingProvider(BuildingEntity building, int captureDay)
    {
        BuffData buff = CreateCapturedStrongholdTrainingProviderBuff(building, captureDay);
        if (buff != null)
            building.BuffComp?.AddBuff(buff, building);
    }

    public static bool TryConsumeHeroRevive(SoldierEntity hero)
    {
        if (hero == null || EntitySideHelper.ToFactionId(hero.Side) != EntitySideHelper.PlayerFactionId)
            return false;

        int limit = GetHeroReviveLimitPerDay();
        if (limit <= 0)
            return false;

        int day = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
        s_HeroReviveStatesByEntityId.TryGetValue(hero.Id, out HeroReviveState state);
        if (state.Day != day)
            state = new HeroReviveState { Day = day, Count = 0 };

        if (state.Count >= limit)
            return false;

        state.Count++;
        s_HeroReviveStatesByEntityId[hero.Id] = state;
        return true;
    }

    public static int GetInitialCoinDelta()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_MortgagePlan":
                    total += IntValue(tag, 0);
                    break;
                case "LvTag_ExclusiveChannel":
                    total -= IntValue(tag, 1);
                    break;
                case "LvTag_ShoestringBudgetI":
                case "LvTag_ShoestringBudgetII":
                case "LvTag_ShoestringBudgetIII":
                    total -= IntValue(tag, 0);
                    break;
            }
        }

        return total;
    }

    public static int GetInitialMaxSupplyDelta()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_ExclusiveChannel":
                    total += IntValue(tag, 0);
                    break;
                case "LvTag_IntranetFailureI":
                case "LvTag_IntranetFailureII":
                case "LvTag_IntranetFailureIII":
                    total -= IntValue(tag, 0);
                    break;
            }
        }

        return total;
    }

    public static int GetBaseProvideSupplyPerLevelDelta()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_BaseStationExpansion":
                    total += IntValue(tag, 0);
                    break;
                case "LvTag_InferiorBaseStationI":
                case "LvTag_InferiorBaseStationII":
                case "LvTag_InferiorBaseStationIII":
                    total -= IntValue(tag, 0);
                    break;
            }
        }

        return total;
    }

    public static int GetDailyBaseIncomeDelta()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_MortgagePlan":
                    total -= IntValue(tag, 1);
                    break;
                case "LvTag_SteadyGrowth":
                    total += IntValue(tag, 0);
                    break;
                case "LvTag_SupplyShortage":
                    total -= IntValue(tag, 0);
                    break;
            }
        }

        return total;
    }

    public static int GetCapturedOutpostIncomeDelta()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_Raider")
                total += IntValue(tag, 0);
        }

        return total;
    }

    public static int GetCapturedStrongholdDailyCost()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_StabilityCost")
                total += IntValue(tag, 0);
        }

        return total;
    }

    public static int ModifyBuildingCost(BuildingData buildingData, int baseCost)
    {
        if (buildingData == null)
            return Mathf.Max(0, baseCost);

        int result = Mathf.Max(0, baseCost);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_ConscriptionOrder":
                    if (buildingData.Type == BuilType.Army)
                        result += IntValue(tag, 1);
                    break;
                case "LvTag_IndustrialSupport":
                    if (buildingData.Type == BuilType.Prod && buildingData.Lv == 1)
                        result -= IntValue(tag, 0);
                    break;
                case "LvTag_ConstructionKickbackI":
                    if (buildingData.Lv == 3)
                        result += IntValue(tag, 0);
                    break;
                case "LvTag_ConstructionKickbackII":
                    if (buildingData.Lv >= 2)
                        result += IntValue(tag, 0);
                    break;
                case "LvTag_ConstructionKickbackIII":
                    if (buildingData.Lv >= 1)
                        result += IntValue(tag, 0);
                    break;
            }
        }

        return Mathf.Max(0, result);
    }

    public static int ModifyRequiredBaseLevel(int requiredBaseLevel)
    {
        int result = Mathf.Max(0, requiredBaseLevel);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_FastTrackApproval")
                result -= IntValue(tag, 0);
        }

        return Mathf.Max(0, result);
    }

    public static int ModifyRecycleRefundRate(int baseRate)
    {
        int result = Mathf.Max(0, baseRate);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_ScrapRecycling":
                    result += IntValue(tag, 0);
                    break;
                case "LvTag_DiscountedRecyclingI":
                case "LvTag_DiscountedRecyclingII":
                    result -= IntValue(tag, 0);
                    break;
            }
        }

        return Mathf.Max(0, result);
    }

    public static int ModifyTechCost(int baseCost)
    {
        int result = Mathf.Max(0, baseCost);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_TechBreakthrough":
                    result += IntValue(tag, 1);
                    break;
                case "LvTag_TechEmbargoI":
                case "LvTag_TechEmbargoII":
                    result += IntValue(tag, 0);
                    break;
            }
        }

        return Mathf.Max(0, result);
    }

    public static int GetExtraTechResearchCountPerBuilding()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_TechBreakthrough")
                total += IntValue(tag, 0);
        }

        return Mathf.Max(0, total);
    }

    public static int ModifyKillRewardConversionRate(int baseRate)
    {
        int result = Mathf.Max(1, baseRate);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_LootDeterioration")
                result += IntValue(tag, 0);
        }

        return Mathf.Max(1, result);
    }

    public static int ModifyDiscardRewardConversionRate(int baseRate)
    {
        int result = Mathf.Max(1, baseRate);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_CommunicationLoss")
                result += IntValue(tag, 0);
        }

        return Mathf.Max(1, result);
    }

    public static int ModifyMaxHandCards(int baseMax)
    {
        int result = Mathf.Max(0, baseMax);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_ForceReduction")
                result -= IntValue(tag, 0);
        }

        return Mathf.Max(1, result);
    }

    public static Fix64 GetEnemyBuildingForbiddenZonePaddingMultiplier()
    {
        Fix64 percent = Fix64.Zero;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_TightSecurity")
                percent += Value(tag, 0);
        }

        return Fix64.One + percent / (Fix64)100;
    }

    public static int ModifyResourcePointInitialAmount(int baseAmount)
    {
        Fix64 result = (Fix64)Mathf.Max(0, baseAmount);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_PoorDeposit")
                result *= Fix64.One - Value(tag, 0) / (Fix64)100;
        }

        return Mathf.Max(0, (int)Fix64.Floor(result));
    }

    public static int ModifyEnemyProductionDailyResourceCost(int baseCost)
    {
        Fix64 result = (Fix64)Mathf.Max(0, baseCost);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_ResourceDepletion")
                result *= Fix64.One + Value(tag, 0) / (Fix64)100;
        }

        return Mathf.Max(0, (int)Fix64.Ceiling(result));
    }

    public static Fix64 CalculateArmyForceBonus(BuildingEntity building)
    {
        if (building?.buildingData == null || building.buildingData.Type != BuilType.Army)
            return Fix64.Zero;

        Fix64 total = Fix64.Zero;
        int baseForce = building.GetArmyForceWithoutRuntimeRules();
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_ConscriptionOrder":
                    total += Fix64.Floor((Fix64)baseForce * Value(tag, 0) / (Fix64)100);
                    break;
                case "LvTag_RecruitShortage":
                    if (baseForce >= IntValue(tag, 0))
                        total -= Value(tag, 1);
                    break;
            }
        }

        return total;
    }

    public static int CalculateArmySupplyPerUnitBonus(BuildingEntity building)
    {
        if (building?.buildingData == null || building.buildingData.Type != BuilType.Army)
            return 0;

        CharacterDataDetail row = FindCharacterData(building.buildingData.UnitID);
        if (row == null || !IsHeavy(row))
            return 0;

        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_BandwidthCongestion")
                total += IntValue(tag, 0);
        }

        return total;
    }

    public static int ModifyEnemySpawnCount(int baseCount)
    {
        Fix64 result = (Fix64)Mathf.Max(0, baseCount);
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            switch (tag.Identifier)
            {
                case "LvTag_OverwhelmingForceI":
                case "LvTag_OverwhelmingForceII":
                    result *= Fix64.One + Value(tag, 0) / (Fix64)100;
                    break;
            }
        }

        return Mathf.Max(0, (int)Fix64.Ceiling(result));
    }

    private static void AddUnitModules(LevelTagTable tag, CharacterDataDetail row, UnitType unitType, int ownerFactionId, List<BuffCallback> modules)
    {
        if (tag == null || row == null || modules == null)
            return;

        bool player = ownerFactionId == EntitySideHelper.PlayerFactionId;
        bool enemy = ownerFactionId != EntitySideHelper.PlayerFactionId;
        bool hero = IsHero(row, unitType);
        bool ranged = HasTag(row.UnitTags, UnitTag.Ranged);
        bool melee = HasTag(row.UnitTags, UnitTag.Melee);

        switch (tag.Identifier)
        {
            case "LvTag_ProjectileEvasion":
                if (player) modules.Add(new WeaponTypeIncomingDamageReductionBuff(Value(tag, 0), true));
                break;
            case "LvTag_BlockingTechnique":
                if (player) modules.Add(new WeaponTypeIncomingDamageReductionBuff(Value(tag, 0), false));
                break;
            case "LvTag_DarkMatterFrenzy":
                if (player) modules.Add(new AttackLifeStealPercentBuff(Value(tag, 0)));
                break;
            case "LvTag_AssaultSquad":
                if (player && row.Size == UnitSize.Small) AddAttackMove(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_TitanBody":
                if (player && IsHeavy(row)) AddDefHealth(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_SightSuppression":
                if (player && ranged) AddAttackRange(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_FearlessVanguard":
                if (player && melee) AddAttackHealth(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_CoreFormation":
                if (player && row.Size == UnitSize.Medium) AddAttackSpeedHealth(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_StandardArmor":
                if (player) AddDefMove(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_PromotionPath":
                if (player && hero) modules.Add(new DayScalingHeroStatsBuff(Value(tag, 0), Value(tag, 1)));
                break;
            case "LvTag_HiddenCommander":
                if (player && !hero) AddDefHealth(modules, Value(tag, 0), Value(tag, 1));
                if (player && hero) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, -Value(tag, 2)));
                break;
            case "LvTag_Conductor":
                if (player) modules.Add(new PercentAttackBonusBuff(hero ? -Value(tag, 1) : Value(tag, 0)));
                break;
            case "LvTag_OneAgainstThousand":
                if (player && hero) AddDefHealth(modules, Value(tag, 0), Value(tag, 1));
                if (player && !hero) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, -Value(tag, 2)));
                break;
            case "LvTag_AbandonDefense":
                if (player && hero) modules.Add(new PercentAttackBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_Desperado":
                if (player && hero) AddAttackHealth(modules, Value(tag, 0), -Value(tag, 1));
                break;
            case "LvTag_HeavyShackles":
                if (player && IsHeavy(row)) modules.Add(new RevertibleMoveSpeedBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_Frail":
                if (player && row.Size == UnitSize.Small) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, -Value(tag, 0)));
                break;
            case "LvTag_HollowStrength":
                if (player && row.Size == UnitSize.Medium) modules.Add(new PercentAttackBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_RustedBladeI":
            case "LvTag_RustedBladeII":
            case "LvTag_RustedBladeIII":
                if (player && melee) modules.Add(new PercentAttackBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_WornRiflingI":
            case "LvTag_WornRiflingII":
            case "LvTag_WornRiflingIII":
                if (player && ranged) modules.Add(new PercentAttackBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_HeadwindFire":
                if (player && ranged) modules.Add(new PercentRangeBonusBuff(-Value(tag, 0) / (Fix64)100));
                break;
            case "LvTag_LegacyArmor":
                if (player) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -Value(tag, 0)));
                break;
            case "LvTag_MalnutritionI":
            case "LvTag_MalnutritionII":
            case "LvTag_MalnutritionIII":
                if (player) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, -Value(tag, 0)));
                break;
            case "LvTag_LowMoraleI":
            case "LvTag_LowMoraleII":
            case "LvTag_LowMoraleIII":
                if (player) modules.Add(new AttackSpeedBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_MuddyMarch":
                if (player) modules.Add(new RevertibleMoveSpeedBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_DenseFog":
                if (player) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Sight, -Value(tag, 0)));
                break;
            case "LvTag_RuthlessMindI":
            case "LvTag_RuthlessMindII":
            case "LvTag_RuthlessMindIII":
                if (enemy) modules.Add(new PercentAttackBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_LongRangeSuppression":
                if (enemy && ranged) modules.Add(new PercentRangeBonusBuff(Value(tag, 0) / (Fix64)100));
                break;
            case "LvTag_HardenedShellI":
            case "LvTag_HardenedShellII":
                if (enemy) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, Value(tag, 0)));
                break;
            case "LvTag_UnyieldingWillI":
            case "LvTag_UnyieldingWillII":
            case "LvTag_UnyieldingWillIII":
                if (enemy) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, Value(tag, 0)));
                break;
            case "LvTag_Stormfront":
                if (enemy) modules.Add(new AttackSpeedBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_BlindCharge":
                if (enemy) modules.Add(new RevertibleMoveSpeedBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_BlindSpotEvasionI":
            case "LvTag_BlindSpotEvasionII":
                if (enemy) modules.Add(new WeaponTypeIncomingDamageReductionBuff(Value(tag, 0), true));
                break;
            case "LvTag_DulledSensesI":
            case "LvTag_DulledSensesII":
                if (enemy) modules.Add(new WeaponTypeIncomingDamageReductionBuff(Value(tag, 0), false));
                break;
            case "LvTag_Gifted":
                break;
            case "LvTag_Neurasthenia":
                if (player) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.StatusResistance, -Value(tag, 0)));
                break;
            case "LvTag_SingularObsession":
                if (enemy) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.StatusResistance, Value(tag, 0)));
                break;
            case "LvTag_HeavyStride":
                if (enemy) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.WeightLevel, Value(tag, 0)));
                break;
        }
    }

    private static void AddBuildingModules(LevelTagTable tag, BuildingEntity building, List<BuffCallback> modules)
    {
        if (tag == null || building?.buildingData == null || modules == null)
            return;

        bool player = building.OwnerFactionID == EntitySideHelper.PlayerFactionId;
        bool enemy = building.OwnerFactionID != EntitySideHelper.PlayerFactionId;
        switch (tag.Identifier)
        {
            case "LvTag_EntrenchedFirepower":
                if (player) modules.Add(new PercentAttackBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_IllegalModification":
                if (player)
                {
                    modules.Add(new PercentAttackBonusBuff(Value(tag, 0)));
                    modules.Add(new PercentRangeBonusBuff(Value(tag, 1) / (Fix64)100));
                    modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -Value(tag, 2)));
                }
                break;
            case "LvTag_Holdout":
                if (player) AddDefHealth(modules, Value(tag, 0), Value(tag, 1));
                break;
            case "LvTag_AbandonDefense":
                if (player) modules.Add(new PercentAttackBonusBuff(-Value(tag, 1)));
                break;
            case "LvTag_PowerRationing":
                if (player) modules.Add(new PercentAttackBonusBuff(-Value(tag, 0)));
                break;
            case "LvTag_PeelingWalls":
                if (player) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, -Value(tag, 0)));
                break;
            case "LvTag_ShoddyConstructionI":
            case "LvTag_ShoddyConstructionII":
                if (player) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, -Value(tag, 0)));
                break;
            case "LvTag_DeathOutpostI":
            case "LvTag_DeathOutpostII":
                if (enemy) modules.Add(new PercentAttackBonusBuff(Value(tag, 0)));
                break;
            case "LvTag_GrievingBulwarkI":
            case "LvTag_GrievingBulwarkII":
                if (enemy) modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, Value(tag, 0)));
                break;
            case "LvTag_DeepRooted":
                if (enemy) modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, Value(tag, 0)));
                break;
        }
    }

    private static List<LevelTagTable> ResolveActiveTags()
    {
        var result = new List<LevelTagTable>();
        if (s_ActiveTagIds.Count == 0 && s_ActiveTagIdentifiers.Count == 0)
            return result;

        var table = GF.DataTable != null ? GF.DataTable.GetDataTable<LevelTagTable>() : null;
        if (table == null)
            return result;

        foreach (LevelTagTable row in table.GetAllDataRows())
        {
            if (row == null)
                continue;

            if (s_ActiveTagIds.Contains(row.Id) || s_ActiveTagIdentifiers.Contains(row.Identifier))
                result.Add(row);
        }

        return result;
    }

    private static BuffData CreateCapturedStrongholdTrainingProviderBuff(BuildingEntity building, int captureDay)
    {
        if (building?.buildingData == null || building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
            return null;

        if (captureDay <= 0)
            return null;

        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier != "LvTag_PledgeOfLoyalty")
                continue;

            int durationDays = IntValue(tag, 0);
            if (durationDays <= 0)
                return null;

            return BuffData.Create(
                id: $"level_tag_captured_training_provider_{building.BuildingInstanceId}",
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: new List<BuffCallback>
                {
                    new CapturedStrongholdTrainingProviderBuff(captureDay, durationDays, Value(tag, 1), Value(tag, 2))
                });
        }

        return null;
    }

    private static int GetHeroReviveLimitPerDay()
    {
        int total = 0;
        foreach (LevelTagTable tag in ResolveActiveTags())
        {
            if (tag.Identifier == "LvTag_PhoenixRebirth")
                total += IntValue(tag, 0);
        }

        return Math.Max(0, total);
    }

    private static CharacterDataDetail FindCharacterData(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || GF.DataTable == null)
            return null;

        var table = GF.DataTable.GetDataTable<CharacterDataDetail>();
        return table?.GetDataRow(row => row.CharacterKey == characterKey);
    }

    private static void AddAttackMove(List<BuffCallback> modules, Fix64 attackPercent, Fix64 move)
    {
        modules.Add(new PercentAttackBonusBuff(attackPercent));
        modules.Add(new RevertibleMoveSpeedBonusBuff(move));
    }

    private static void AddDefHealth(List<BuffCallback> modules, Fix64 def, Fix64 healthPercent)
    {
        modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, def));
        modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, healthPercent));
    }

    private static void AddAttackHealth(List<BuffCallback> modules, Fix64 attackPercent, Fix64 healthPercent)
    {
        modules.Add(new PercentAttackBonusBuff(attackPercent));
        modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, healthPercent));
    }

    private static void AddAttackRange(List<BuffCallback> modules, Fix64 attackPercent, Fix64 rangePercent)
    {
        modules.Add(new PercentAttackBonusBuff(attackPercent));
        modules.Add(new PercentRangeBonusBuff(rangePercent / (Fix64)100));
    }

    private static void AddAttackSpeedHealth(List<BuffCallback> modules, Fix64 attackSpeedPercent, Fix64 healthPercent)
    {
        modules.Add(new AttackSpeedBonusBuff(attackSpeedPercent));
        modules.Add(new MainPropertyPercentBuff(CreatureMainProperty.Health, healthPercent));
    }

    private static void AddDefMove(List<BuffCallback> modules, Fix64 def, Fix64 move)
    {
        modules.Add(new MainPropertyAdditiveBuff(CreatureMainProperty.Def, def));
        modules.Add(new RevertibleMoveSpeedBonusBuff(move));
    }

    private static bool IsHero(CharacterDataDetail row, UnitType unitType)
    {
        return unitType == UnitType.Unit_Hero || HasTag(row?.UnitTags, UnitTag.Hero);
    }

    private static bool IsHeavy(CharacterDataDetail row)
    {
        return row != null && (row.Size == UnitSize.Large || row.Size == UnitSize.SuperLarge);
    }

    private static bool HasTag(UnitTag[] tags, UnitTag tag)
    {
        if (tags == null)
            return false;

        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag)
                return true;
        }

        return false;
    }

    private static Fix64 Value(LevelTagTable tag, int index)
    {
        if (tag?.UniqueValues == null || index < 0 || index >= tag.UniqueValues.Length)
            return Fix64.Zero;

        return tag.UniqueValues[index];
    }

    private static int IntValue(LevelTagTable tag, int index)
    {
        return Mathf.RoundToInt((float)Value(tag, index));
    }

    private static void WarnUnsupported(LevelTagTable tag, string reason)
    {
        if (tag == null || string.IsNullOrWhiteSpace(tag.Identifier))
            return;

        if (!s_WarnedUnsupportedTags.Add(tag.Identifier))
            return;

        Log.Warning("[LevelTagRuntime] 关卡 tag 暂未实现: id={0}, identifier={1}, reason={2}", tag.Id, tag.Identifier, reason);
    }
}
