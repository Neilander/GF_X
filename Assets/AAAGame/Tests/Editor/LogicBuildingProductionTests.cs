using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicBuildingProductionTests
{
    private GameObject m_RewardManagerObject;

    [SetUp]
    public void SetUp()
    {
        LogicRuntimeDataTableCache.PrepareForEditorTests(
            LoadCharacterRows(),
            LoadBuildingRows(),
            Array.Empty<LevelTagTable>());
        EnsureInGameDataModel();
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicStrongholdMap.Clear();
        LogicProductionConditionState.ClearAll();
    }

    [TearDown]
    public void TearDown()
    {
        if (m_RewardManagerObject != null)
        {
            RewardManager manager = m_RewardManagerObject.GetComponent<RewardManager>();
            if (manager != null)
                UnsubscribeRewardManagerForTest(manager);
            UnityEngine.Object.DestroyImmediate(m_RewardManagerObject);
            m_RewardManagerObject = null;
        }
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
        LogicProductionConditionState.ClearAll();
        LogicEntityLifecycleService.EndTimeline();
        LogicPhaseCommandService.EndTimeline();
        LogicTimeControlService.EndTimeline();
        LogicRuntimeDataTableCache.ResetForEditorTests();
    }

    [Test]
    public void GrantProduction_UsesViewlessLogicBuildingAndConsumesStableReserve()
    {
        LogicEntityState building = CreateProductionBuilding("Buil_Prod_Test_Lv1", "production-grant-1", 10);
        InGameDataModel.EnsureProductionBuildingCoinReserves(building.BuildingInstanceId, 7);

        int granted = LogicBuildingProductionService.GrantProduction(building);

        Assert.IsFalse(building.HasBoundView);
        Assert.AreEqual(7, granted);
        Assert.AreEqual(0, InGameDataModel.GetProductionBuildingCoinReserves(building.BuildingInstanceId));
    }

    [Test]
    public void RewardPresentation_CommitsCoinBeforeAnyFlyEffectCallback()
    {
        m_RewardManagerObject = new GameObject("LogicBuildingProductionTests_RewardManager");
        RewardManager manager = m_RewardManagerObject.AddComponent<RewardManager>();
        MethodInfo grant = typeof(RewardManager).GetMethod(
            "GrantCoin",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(grant);

        grant.Invoke(manager, new object[] { FixVector2.Zero, 5, "production-test" });

        Assert.AreEqual(5, InGameDataModel.GetValue(IngameValueType.Coin));

        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/MeiyouUtility/RewardManager.cs"));
        StringAssert.DoesNotContain("discardScreenPosition", source);
        StringAssert.DoesNotContain("ScreenPointToRay", source);
        StringAssert.DoesNotContain("Physics.Raycast", source);
        StringAssert.Contains("m_PendingCoinFlyPresentation.Enqueue", source);
    }

    [TestCase(3, 50, 2)]
    [TestCase(1, 50, 1)]
    [TestCase(10, 100, 0)]
    public void DemolishedProductionLoss_RoundsRemainingProductionUp(
        int production,
        int lossPercent,
        int expected)
    {
        Assert.AreEqual(
            expected,
            RewardManager.CalculateProductionAfterDemolitionLoss(production, lossPercent));
    }

    [Test]
    public void PlayerProductionBuildingDisable_RecordsLossAndNeverGrantsDestructionReward()
    {
        m_RewardManagerObject = new GameObject("PlayerProductionDisable_RewardManager");
        RewardManager manager = m_RewardManagerObject.AddComponent<RewardManager>();
        SetPrivateField(manager, "m_RuntimeDependenciesPrepared", true);
        SubscribeRewardManagerForTest(manager);
        LogicEntityState building = CreateProductionBuilding(
            "Buil_Prod_Test_Lv1",
            "production-player-disabled",
            10);

        building.TakeDamage((Fix64)150, HealthModifyType.empty);

        Assert.IsTrue(building.IsDisabled);
        Assert.AreEqual(0, InGameDataModel.GetValue(IngameValueType.Coin));
        Assert.IsTrue(InGameDataModel.ConsumeDemolishedPlayerProductionBuilding(
            building.BuildingInstanceId));
    }

    [Test]
    public void EnemyProductionBuildingDisable_GrantsRewardOnlyOnFirstDisable()
    {
        m_RewardManagerObject = new GameObject("EnemyProductionDisable_RewardManager");
        RewardManager manager = m_RewardManagerObject.AddComponent<RewardManager>();
        SetPrivateField(manager, "m_RuntimeDependenciesPrepared", true);
        SetPrivateField(manager, "m_DemolishEnemyProdReward", 2);
        SubscribeRewardManagerForTest(manager);
        LogicEntityState building = CreateProductionBuilding(
            "Buil_Prod_Test_Lv1",
            "production-enemy-disabled",
            10,
            EntitySideHelper.EnemyFactionId);

        building.TakeDamage((Fix64)150, HealthModifyType.empty);
        building.TakeDamage((Fix64)150, HealthModifyType.empty);

        Assert.IsTrue(building.IsDisabled);
        Assert.AreEqual(2, InGameDataModel.GetValue(IngameValueType.Coin));
        Assert.IsFalse(InGameDataModel.ConsumeDemolishedPlayerProductionBuilding(
            building.BuildingInstanceId));
    }

    [Test]
    public void BuildingPhaseUndo_AccumulatesCostsAndKeepsFirstPhaseForm()
    {
        const string buildingInstanceId = "building-phase-undo";
        InGameDataModel.RecordBuildingPhaseModification(buildingInstanceId, "Buil_Test_Lv0", 3);
        InGameDataModel.RecordBuildingPhaseModification(buildingInstanceId, "Buil_Test_Lv1", 4);
        InGameDataModel.RecordBuildingPhaseTech(buildingInstanceId, "Tech_Test_Lv1");
        InGameDataModel.RecordBuildingPhaseTech(buildingInstanceId, "Tech_Test_Lv2");

        Assert.IsTrue(InGameDataModel.TryGetBuildingPhaseUndo(
            buildingInstanceId,
            out string originalBuildingId,
            out int refund));
        Assert.AreEqual("Buil_Test_Lv0", originalBuildingId);
        Assert.AreEqual(7, refund);
        CollectionAssert.AreEqual(
            new[] { "Tech_Test_Lv1", "Tech_Test_Lv2" },
            InGameDataModel.GetBuildingPhaseTechIds(buildingInstanceId));

        InGameDataModel.ClearBuildingPhaseUndoRecords();
        Assert.IsFalse(InGameDataModel.TryGetBuildingPhaseUndo(buildingInstanceId, out _, out _));
    }

    [Test]
    public void BuildingPhaseUndo_RemainsAllowedForTechThatDoesNotChangeBuildingCosts()
    {
        const string buildingInstanceId = "building-normal-tech-undo";
        InGameDataModel.RecordBuildingPhaseModification(buildingInstanceId, "Buil_ResearchCenter_Lv2", 16);
        InGameDataModel.RecordBuildingPhaseTech(
            buildingInstanceId,
            "Tech_Buil_ResearchCenter_Lv3_Opt2");

        Assert.IsFalse(BuildingTechRuntimeEffect.ChangesBuildingCost(
            "Tech_Buil_ResearchCenter_Lv3_Opt2"));
    }

    [Test]
    public void TroopConditions_UseCurrentFixedPositionAndStrongholdOwnerSide()
    {
        InGameDataModel.SetStrongholds(new List<Stronghold>
        {
            new Stronghold
            {
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                strongholdData = new StrongholdData { StrongholdId = "SH_0_1" },
            },
            new Stronghold
            {
                OwnerFactionId = 1,
                strongholdData = new StrongholdData { StrongholdId = "SH_1_1" },
            },
        });
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_0_1", 0, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("SH_1_1", 1, 0, EntitySideHelper.EnemyFactionId),
            });
        CreateUnit("Unit_Player", SideType.PlayerSide, "SH_1_1", FixVector2.Zero);
        LogicEntityId enemyId = CreateUnit(
            "Unit_Enemy",
            SideType.EnemySide,
            "SH_0_1",
            new FixVector2(Fix64.One, Fix64.Zero));
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        Assert.AreEqual(1, LogicProductionConditionState.GetTroopCount("SH_0_1"));
        Assert.AreEqual(1, LogicProductionConditionState.GetTroopCount("SH_1_1"));
        LogicProductionConditionState.SnapshotSurvivors(1);
        Assert.AreEqual(1, LogicProductionConditionState.GetSurvivorCount("SH_0_1", 1));
        Assert.AreEqual(1, LogicProductionConditionState.GetSurvivorCount("SH_1_1", 1));

        LogicEntityState enemy = LogicEntityStateStore.GetRequired(enemyId);
        LogicProductionConditionState.RecordUnitDeath(enemy);
        Assert.AreEqual(0, LogicProductionConditionState.GetKillCount("SH_0_1", 1));
        Assert.AreEqual(1, LogicProductionConditionState.GetKillCount("SH_1_1", 1));
    }

    [Test]
    public void ProductionConditions_PreserveFixedPositionAcrossLargeCoordinateCellBoundary()
    {
        Fix64 originX = (Fix64)1000000;
        FixVector2 origin = new FixVector2(originX, Fix64.Zero);
        FixVector2 position = new FixVector2(
            originX + Fix64.FromRaw((1L << (Fix64.FRACTIONAL_PLACES - 1)) + 1),
            Fix64.Zero);
        LogicStrongholdMap.Initialize(
            origin,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            new[]
            {
                new LogicStrongholdCellDefinition("SH_HIGH_0", 0, 0, EntitySideHelper.PlayerFactionId),
                new LogicStrongholdCellDefinition("SH_HIGH_1", 1, 0, EntitySideHelper.EnemyFactionId),
            });
        LogicEntityId enemyId = CreateUnit(
            "Unit_Enemy_HighCoordinate",
            SideType.EnemySide,
            "SH_HIGH_1",
            position);
        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);

        Assert.AreEqual(1, LogicProductionConditionState.GetTroopCount("SH_HIGH_1"));
        LogicProductionConditionState.SnapshotSurvivors(1);
        Assert.AreEqual(1, LogicProductionConditionState.GetSurvivorCount("SH_HIGH_1", 1));

        LogicProductionConditionState.RecordUnitDeath(LogicEntityStateStore.GetRequired(enemyId));
        Assert.AreEqual(0, LogicProductionConditionState.GetKillCount("SH_HIGH_0", 1));
        Assert.AreEqual(1, LogicProductionConditionState.GetKillCount("SH_HIGH_1", 1));
    }

    [Test]
    public void Nursery_DefaultBonusCapRemainsThree()
    {
        InGameDataModel.SetValue(IngameValueType.Day, 10, false);
        LogicEntityState nursery = CreateProductionBuilding("Buil_Nursery_Lv1", "production-nursery-1", 10);
        nursery.ProductionProps.ProductionTraitFirstDay = 1;

        LogicBuildingProductionService.Refresh(nursery);

        Assert.AreEqual((Fix64)3, nursery.ProductionProps.DynamicProduction);
        Assert.AreEqual(13, LogicBuildingProductionService.GetProduction(nursery));
    }

    [Test]
    public void FreshMarket_DecreasesForEveryTwoOtherBuildingsAndStopsAtFloor()
    {
        LogicEntityState market = CreateProductionBuilding("Buil_FreshMarket_Lv1", "fresh-market", 5);
        for (int i = 0; i < 10; i++)
            CreateProductionBuilding($"Buil_Prod_Test_{i}_Lv1", $"fresh-market-neighbor-{i}", 1);

        LogicBuildingProductionService.Refresh(market);

        Assert.AreEqual(ProductionType.DecreasingByOtherBuildingCount, market.ProductionProps.ProductionType);
        Assert.AreEqual(10, market.ProductionProps.ConditionCount);
        Assert.AreEqual(1, LogicBuildingProductionService.GetProduction(market));
    }

    [Test]
    public void CampfireGrill_IncreasesForEveryTwoOtherBuildingsAndStopsAtBonusCap()
    {
        LogicEntityState grill = CreateProductionBuilding("Buil_CampfireGrill_Lv1", "campfire-grill", 1);
        for (int i = 0; i < 6; i++)
            CreateProductionBuilding($"Buil_Prod_Test_{i}_Lv1", $"campfire-grill-neighbor-{i}", 1);

        LogicBuildingProductionService.Refresh(grill);

        Assert.AreEqual(ProductionType.IncreasingByOtherBuildingCount, grill.ProductionProps.ProductionType);
        Assert.AreEqual(6, grill.ProductionProps.ConditionCount);
        Assert.AreEqual((Fix64)2, grill.ProductionProps.DynamicProduction);
        Assert.AreEqual(3, LogicBuildingProductionService.GetProduction(grill));
    }

    [Test]
    public void ReceptionDesk_SnapshotsPreviousDaySurvivorsAtBuildBoundary()
    {
        InitializeStrongholds(EntitySideHelper.PlayerFactionId);
        LogicEntityState receptionDesk = CreateProductionBuilding(
            "Buil_ReceptionDesk_Lv1",
            "production-reception-desk-1",
            10);
        for (int i = 0; i < 7; i++)
            CreateUnit($"Unit_Test_{i}", SideType.PlayerSide, "SH_0_1", FixVector2.Zero);
        LogicTimeControlService.BeginFrame(2);
        LogicEntityLifecycleService.ApplyFrame(2);
        InGameDataModel.SetValue(IngameValueType.Day, 2, false);

        LogicBuildingProductionService.PrepareBuildPhase(true);

        Assert.AreEqual(ProductionType.BySurvivorCount, receptionDesk.ProductionProps.ProductionType);
        Assert.AreEqual(7, receptionDesk.ProductionProps.ConditionCount);
        Assert.AreEqual((Fix64)2, receptionDesk.ProductionProps.DynamicProduction);
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(receptionDesk));
    }

    [Test]
    public void PackageRack_AccumulatesAtIncomeBoundaryAndReleasesImmediatelyWhenDestroyed()
    {
        InitializeStrongholds(EntitySideHelper.PlayerFactionId);
        m_RewardManagerObject = new GameObject("PackageRack_RewardManager");
        RewardManager manager = m_RewardManagerObject.AddComponent<RewardManager>();
        SetPrivateField(manager, "m_RuntimeDependenciesPrepared", true);
        SubscribeRewardManagerForTest(manager);
        LogicEntityState rack = CreateProductionBuilding("Buil_PackageRack_Lv1", "package-rack", 2);
        InGameDataModel.EnsureProductionBuildingCoinReserves(rack.BuildingInstanceId, 100);
        InGameDataModel.SetValue(IngameValueType.Day, 2, false);

        LogicBuildingProductionService.PrepareBuildPhase(true);

        Assert.AreEqual(ProductionType.StoredUntilDestroyed, rack.ProductionProps.ProductionType);
        Assert.AreEqual(2, rack.ProductionProps.StoredProduction);
        Assert.AreEqual(0, LogicBuildingProductionService.GetProduction(rack));

        rack.TakeDamage((Fix64)150, HealthModifyType.reduce);

        Assert.AreEqual(2, InGameDataModel.GetValue(IngameValueType.Coin));
        Assert.AreEqual(0, rack.ProductionProps.StoredProduction);
        Assert.IsFalse(InGameDataModel.ConsumeDemolishedPlayerProductionBuilding(rack.BuildingInstanceId));
    }

    [Test]
    public void Residence_DestroyedIncomeIsZeroForOneBoundaryOnly()
    {
        m_RewardManagerObject = new GameObject("Residence_RewardManager");
        RewardManager manager = m_RewardManagerObject.AddComponent<RewardManager>();
        SetPrivateField(manager, "m_RuntimeDependenciesPrepared", true);
        SubscribeRewardManagerForTest(manager);
        LogicEntityState residence = CreateProductionBuilding("Buil_Residence_Lv1", "residence", 3);
        InGameDataModel.EnsureProductionBuildingCoinReserves(residence.BuildingInstanceId, 100);

        residence.TakeDamage((Fix64)150, HealthModifyType.reduce);
        residence.RestoreBuildingToFullHealth();
        InvokeGrantProductionIncome(manager);
        Assert.AreEqual(0, InGameDataModel.GetValue(IngameValueType.Coin));

        InvokeGrantProductionIncome(manager);
        Assert.AreEqual(3, InGameDataModel.GetValue(IngameValueType.Coin));
    }

    [Test]
    public void TicketBooth_ProducesOnlyOnConfiguredDayIntervalFromConstructionDay()
    {
        LogicEntityState ticketBooth = CreateProductionBuilding(
            "Buil_TicketBooth_Lv1",
            "production-ticket-booth-1",
            10);

        InGameDataModel.SetValue(IngameValueType.Day, 4, false);
        LogicBuildingProductionService.Refresh(ticketBooth);
        Assert.AreEqual(ProductionType.PeriodicOutput, ticketBooth.ProductionProps.ProductionType);
        Assert.AreEqual(3, ticketBooth.ProductionProps.ConditionCount);
        Assert.AreEqual(0, LogicBuildingProductionService.GetProduction(ticketBooth));

        InGameDataModel.SetValue(IngameValueType.Day, 5, false);
        LogicBuildingProductionService.Refresh(ticketBooth);
        Assert.AreEqual(4, ticketBooth.ProductionProps.ConditionCount);
        Assert.AreEqual(10, LogicBuildingProductionService.GetProduction(ticketBooth));
    }

    [Test]
    public void MiningRig_DecreasesFromFirstSuccessfulProductionAndStopsAtConfiguredFloor()
    {
        InGameDataModel.SetValue(IngameValueType.Day, 3, false);
        LogicEntityState miningRig = CreateProductionBuilding(
            "Buil_MiningRig_Lv1",
            "production-mining-rig-1",
            10);
        InGameDataModel.EnsureProductionBuildingCoinReserves(miningRig.BuildingInstanceId, 100);

        Assert.AreEqual(10, LogicBuildingProductionService.GrantProduction(miningRig));
        Assert.AreEqual(3, miningRig.ProductionProps.ProductionTraitFirstDay);
        Assert.AreEqual(ProductionType.DecreasingOutput, miningRig.ProductionProps.ProductionType);

        InGameDataModel.SetValue(IngameValueType.Day, 4, false);
        LogicBuildingProductionService.Refresh(miningRig);
        Assert.AreEqual(9, LogicBuildingProductionService.GetProduction(miningRig));

        InGameDataModel.SetValue(IngameValueType.Day, 20, false);
        LogicBuildingProductionService.Refresh(miningRig);
        Assert.AreEqual(17, miningRig.ProductionProps.ConditionCount);
        Assert.AreEqual(1, LogicBuildingProductionService.GetProduction(miningRig));
    }

    [Test]
    public void Nursery_IncreasesOnlyAfterFirstSuccessfulProduction()
    {
        InGameDataModel.SetValue(IngameValueType.Day, 3, false);
        LogicEntityState nursery = CreateProductionBuilding(
            "Buil_Nursery_Lv1",
            "production-nursery-first-grant",
            10);
        InGameDataModel.SetValue(IngameValueType.Day, 8, false);
        LogicBuildingProductionService.Refresh(nursery);
        Assert.AreEqual(10, LogicBuildingProductionService.GetProduction(nursery));

        InGameDataModel.EnsureProductionBuildingCoinReserves(nursery.BuildingInstanceId, 100);
        Assert.AreEqual(10, LogicBuildingProductionService.GrantProduction(nursery));
        Assert.AreEqual(8, nursery.ProductionProps.ProductionTraitFirstDay);

        InGameDataModel.SetValue(IngameValueType.Day, 10, false);
        LogicBuildingProductionService.Refresh(nursery);
        Assert.AreEqual(2, nursery.ProductionProps.ConditionCount);
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(nursery));
    }

    [Test]
    public void ArmyLevelTechs_ApplyNewLifetimeHealAndCriticalEffectsWithoutRemovedAttackSpeed()
    {
        List<BuffData> internLv2 = CreateArmyInitialBuffs(UnitType.Unit_Intern, 2);
        List<BuffData> internLv3 = CreateArmyInitialBuffs(UnitType.Unit_Intern, 3);
        Assert.AreEqual((Fix64)34, FindBuff(internLv2, "timed_death").duration);
        Assert.AreEqual((Fix64)33, FindBuff(internLv3, "timed_death").duration);

        Fix64 butcherLv1Heal = ReadOnKillHealPercent(CreateArmyInitialBuffs(UnitType.Unit_BoneButcher, 1));
        Fix64 butcherLv2Heal = ReadOnKillHealPercent(CreateArmyInitialBuffs(UnitType.Unit_BoneButcher, 2));
        Fix64 butcherLv3Heal = ReadOnKillHealPercent(CreateArmyInitialBuffs(UnitType.Unit_BoneButcher, 3));
        Assert.AreEqual((Fix64)2, butcherLv2Heal - butcherLv1Heal);
        Assert.AreEqual((Fix64)5, butcherLv3Heal - butcherLv1Heal);

        List<BuffData> poacherLv3 = CreateArmyInitialBuffs(UnitType.Unit_Poacher, 3);
        CriticalDamageBonusBuff critical = FindModule<CriticalDamageBonusBuff>(poacherLv3);
        Assert.NotNull(critical);
        Assert.AreEqual((Fix64)50, critical.GetCriticalDamageBonusPercent());

        foreach (UnitType unitType in new[]
                 {
                     UnitType.Unit_CanMaker,
                     UnitType.Unit_Brat,
                     UnitType.Unit_LongbowHunter,
                     UnitType.Unit_Poacher,
                 })
        {
            Assert.IsNull(
                FindModule<AttackSpeedBonusBuff>(CreateArmyInitialBuffs(unitType, 3)),
                $"{unitType} retained a removed army-level attack-speed effect.");
        }
    }

    [Test]
    public void ResearchCenterArmyForceTechs_TargetArmyBuildingsWithoutResolvedUnitScope()
    {
        LogicEntityState internBuilding = CreateArmyBuilding(
            "Buil_InterviewRoom_Lv3",
            "army-force-intern",
            UnitType.Unit_Intern);
        LogicEntityState hunterBuilding = CreateArmyBuilding(
            "Buil_Fletcher_Lv3",
            "army-force-hunter",
            UnitType.Unit_LongbowHunter);
        var managerObject = new GameObject("GlobalBuffManager_ArmyForceScope_Test");
        var manager = managerObject.AddComponent<GlobalBuffManager>();
        var effect = new BuildingTechRuntimeEffect();
        try
        {
            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_ResearchCenter_Lv2_Opt2",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = "research-center-source",
                TechData = CreateTechData(
                    "Tech_Buil_ResearchCenter_Lv2_Opt2",
                    new[] { (Fix64)2, Fix64.One },
                    TechScopeType.Special),
                ResolvedScope = null,
                GlobalBuffManager = manager,
            });
            effect.Activate(new TechEffectContext
            {
                TechId = "Tech_Buil_ResearchCenter_Lv3_Opt2",
                OwnerFactionId = EntitySideHelper.PlayerFactionId,
                SourceBuildingInstanceId = "research-center-source",
                TechData = CreateTechData(
                    "Tech_Buil_ResearchCenter_Lv3_Opt2",
                    new[] { (Fix64)2 },
                    TechScopeType.AllBuil),
                ResolvedScope = null,
                GlobalBuffManager = manager,
            });

            Assert.AreEqual((Fix64)3, manager.CalculateRuntimeArmyForceBonus(internBuilding));
            Assert.AreEqual(Fix64.One, manager.CalculateRuntimeArmyForceBonus(hunterBuilding));
        }
        finally
        {
            effect.ClearRuntimeState();
            manager.ClearLevelRuntimeState();
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    private static LogicEntityState CreateProductionBuilding(
        string identifier,
        string buildingInstanceId,
        int production,
        int ownerFactionId = EntitySideHelper.PlayerFactionId)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            EntitySideHelper.ToSide(ownerFactionId),
            identifier);
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                state.Configure(
                    null,
                    new CreaturePropertyManager(property =>
                        property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                    0,
                    true,
                    null,
                    false);
                var move = new NoMoveComp();
                state.SetMoveComp(move);
                move.Init(state);
                var attack = new NoAtkComp();
                state.SetAtkComp(attack);
                attack.Init(state);
                var targeting = new NoTargetingComp();
                state.SetTargetingComp(targeting);
                targeting.Init(state);
                state.ConfigureBuilding(
                    new BuildingData(
                        identifier,
                        BuilType.Prod,
                        Archetype.None,
                        "Tests/Building",
                        identifier,
                        identifier,
                        1,
                        0,
                        (Fix64)100,
                        null,
                        Fix64.Zero,
                        ResolveUniqueValues(identifier),
                        null,
                        production,
                        Array.Empty<string>()),
                    buildingInstanceId,
                    "SH_0_1",
                    ownerFactionId,
                    LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                    Array.Empty<LogicCombatShape>(),
                    Array.Empty<LogicInteractionOptionDescriptor>(),
                    true);
                LogicBuildingProductionService.Configure(state);
            });

        ulong spawnFrame = checked(LogicTimeControlService.CurrentFrame + 1);
        LogicTimeControlService.BeginFrame(spawnFrame);
        LogicEntityLifecycleService.ApplyFrame(spawnFrame);
        return LogicEntityStateStore.GetRequired(entityId);
    }

    private static LogicEntityState CreateArmyBuilding(
        string identifier,
        string buildingInstanceId,
        UnitType unitType)
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestConfiguredSpawn(
            new LogicEntitySpawnDescriptor(
                FixVector2.Zero,
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.PlayerSide,
                identifier),
            state =>
            {
                state.Configure(
                    null,
                    new CreaturePropertyManager(property =>
                        property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                    0,
                    true,
                    null,
                    false);
                var move = new NoMoveComp();
                state.SetMoveComp(move);
                move.Init(state);
                var attack = new NoAtkComp();
                state.SetAtkComp(attack);
                attack.Init(state);
                var targeting = new NoTargetingComp();
                state.SetTargetingComp(targeting);
                targeting.Init(state);
                state.ConfigureBuilding(
                    new BuildingData(
                        identifier,
                        BuilType.Army,
                        Archetype.None,
                        "Tests/ArmyBuilding",
                        identifier,
                        identifier,
                        3,
                        0,
                        (Fix64)100,
                        null,
                        Fix64.Zero,
                        Array.Empty<Fix64>(),
                        unitType.ToString(),
                        1,
                        Array.Empty<string>()),
                    buildingInstanceId,
                    "SH_0_1",
                    EntitySideHelper.PlayerFactionId,
                    LogicCombatShape.AxisAlignedBox(
                        FixVector2.Zero,
                        new FixVector2(Fix64.One, Fix64.One)),
                    Array.Empty<LogicCombatShape>(),
                    Array.Empty<LogicInteractionOptionDescriptor>(),
                    true,
                    1);
            });

        ulong spawnFrame = checked(LogicTimeControlService.CurrentFrame + 1);
        LogicTimeControlService.BeginFrame(spawnFrame);
        LogicEntityLifecycleService.ApplyFrame(spawnFrame);
        return LogicEntityStateStore.GetRequired(entityId);
    }

    private static List<BuffData> CreateArmyInitialBuffs(UnitType unitType, int level)
    {
        var buffs = new List<BuffData>();
        MethodInfo method = typeof(SoldierFactory).GetMethod(
            "AddInitialBuffs",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { buffs, unitType, level });
        return buffs;
    }

    private static BuffData FindBuff(List<BuffData> buffs, string id)
    {
        BuffData result = buffs.Find(buff => string.Equals(buff.id, id, StringComparison.Ordinal));
        Assert.NotNull(result, $"Buff '{id}' was not created.");
        return result;
    }

    private static T FindModule<T>(List<BuffData> buffs) where T : BuffCallback
    {
        for (int i = 0; i < buffs.Count; i++)
        {
            List<BuffCallback> modules = buffs[i].modules;
            if (modules == null)
                continue;
            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                if (modules[moduleIndex] is T result)
                    return result;
            }
        }

        return null;
    }

    private static Fix64 ReadOnKillHealPercent(List<BuffData> buffs)
    {
        OnKillHealBuff module = FindModule<OnKillHealBuff>(buffs);
        Assert.NotNull(module);
        FieldInfo field = typeof(OnKillHealBuff).GetField(
            "_curHpPercent",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (Fix64)field.GetValue(module);
    }

    private static TechData CreateTechData(
        string identifier,
        Fix64[] uniqueValues,
        TechScopeType scopeType)
    {
        return new TechData(
            identifier,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            uniqueValues,
            scopeType,
            Array.Empty<string>(),
            Array.Empty<UnitSize>(),
            Array.Empty<UnitTag>(),
            Array.Empty<Archetype>(),
            string.Empty,
            false);
    }

    private static LogicEntityId CreateUnit(
        string characterKey,
        SideType side,
        string sourceStrongholdId,
        FixVector2 position)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            position,
            new FixVector2(Fix64.Zero, Fix64.One),
            side,
            characterKey,
            sourceStrongholdId);
        return LogicEntityLifecycleService.RequestConfiguredSpawn(
            descriptor,
            state =>
            {
                LogicRuntimeDataTableCache.TryGetCharacter(characterKey, out CharacterDataDetail characterData);
                state.Configure(
                    characterData,
                    new CreaturePropertyManager(property =>
                        property == CreatureMainProperty.Health ? (Fix64)100 : Fix64.Zero),
                    0,
                    true,
                    null,
                    false);
                var move = new NoMoveComp();
                state.SetMoveComp(move);
                move.Init(state);
                var attack = new NoAtkComp();
                state.SetAtkComp(attack);
                attack.Init(state);
                var targeting = new NoTargetingComp();
                state.SetTargetingComp(targeting);
                targeting.Init(state);
            });
    }

    private static void InitializeStrongholds(params int[] owners)
    {
        var cells = new LogicStrongholdCellDefinition[owners.Length];
        for (int i = 0; i < owners.Length; i++)
            cells[i] = new LogicStrongholdCellDefinition($"SH_{i}_1", i, 0, owners[i]);
        LogicStrongholdMap.Initialize(
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            new FixVector2(Fix64.Zero, Fix64.One),
            Fix64.One,
            cells);
    }

    private static Fix64[] ResolveUniqueValues(string levelIdentifier)
    {
        int levelIndex = levelIdentifier.LastIndexOf("_Lv", StringComparison.Ordinal);
        string baseIdentifier = levelIndex > 0
            ? levelIdentifier.Substring(0, levelIndex)
            : levelIdentifier;
        return LogicRuntimeDataTableCache.TryGetBuilding(baseIdentifier, out BuildingTable row)
            ? row.UniqueValues
            : Array.Empty<Fix64>();
    }

    private static void EnsureInGameDataModel()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(LogicBuildingProductionTests));
        FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        var component = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject("LogicBuildingProductionTests_DataModel");
            component = gameObject.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, component);
        }

        FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField(
            "m_DataModels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(component);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(component, dataModels);
        }

        InGameDataModel model = component.GetDataModel<InGameDataModel>();
        if (model == null)
        {
            model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
            Type pairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
            object pair = Activator.CreateInstance(
                pairType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { typeof(InGameDataModel), 0 },
                null);
            object dictionary = dataModelsField.GetValue(component);
            dictionary.GetType().GetMethod("Add")?.Invoke(dictionary, new[] { pair, model });
        }

        typeof(InGameDataModel)
            .GetField("m_IngameValue", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(
                model,
                new Dictionary<IngameValueType, int>
                {
                    [IngameValueType.Phase] = (int)GamePhase.Defend,
                    [IngameValueType.Day] = 1,
                    [IngameValueType.Coin] = 0,
                    [IngameValueType.CurrentSupply] = 0,
                    [IngameValueType.MaxSupply] = 0,
                });
        var reserves = typeof(InGameDataModel)
            .GetField("m_ProductionBuildingCoinReservesByInstanceId", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(model) as Dictionary<string, int>;
        reserves?.Clear();
        InGameDataModel.SetStrongholds(new List<Stronghold>());
    }

    private static void SetPrivateField<T>(RewardManager manager, string fieldName, T value)
    {
        FieldInfo field = typeof(RewardManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field, $"RewardManager field '{fieldName}' was not found.");
        field.SetValue(manager, value);
    }

    private static void SubscribeRewardManagerForTest(RewardManager manager)
    {
        MethodInfo subscribe = typeof(RewardManager).GetMethod(
            "TrySubscribeEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(subscribe);
        subscribe.Invoke(manager, null);

        FieldInfo subscribed = typeof(RewardManager).GetField(
            "m_LogicEventsSubscribed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(subscribed);
        Assert.IsTrue((bool)subscribed.GetValue(manager));
    }

    private static void UnsubscribeRewardManagerForTest(RewardManager manager)
    {
        MethodInfo unsubscribe = typeof(RewardManager).GetMethod(
            "TryUnsubscribeEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(unsubscribe);
        unsubscribe.Invoke(manager, null);

        FieldInfo subscribed = typeof(RewardManager).GetField(
            "m_LogicEventsSubscribed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(subscribed);
        Assert.IsFalse((bool)subscribed.GetValue(manager));
    }

    private static void InvokeGrantProductionIncome(RewardManager manager)
    {
        MethodInfo method = typeof(RewardManager).GetMethod(
            "GrantBuildPhaseIncomeFromPlayerProdBuildings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(manager, null);
    }

    private static BuildingTable[] LoadBuildingRows()
    {
        string path = System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/DataTable/Build/BuildingTable.txt");
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException("Building table was not found.", path);

        var rows = new List<BuildingTable>();
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var row = new BuildingTable();
            if (!row.ParseDataRow(line, null))
                throw new InvalidOperationException("Building table row could not be parsed for editor tests.");
            rows.Add(row);
        }

        return rows.ToArray();
    }

    private static CharacterDataDetail[] LoadCharacterRows()
    {
        string path = System.IO.Path.Combine(
            Application.dataPath,
            "AAAGame/DataTable/CharacterDataDetail.txt");
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException("Character data table was not found.", path);

        var rows = new List<CharacterDataDetail>();
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var row = new CharacterDataDetail();
            if (!row.ParseDataRow(line, null))
                throw new InvalidOperationException("Character data row could not be parsed for editor tests.");
            rows.Add(row);
        }

        return rows.ToArray();
    }

}
