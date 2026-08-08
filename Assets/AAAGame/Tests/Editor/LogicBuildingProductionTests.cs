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
            UnityEngine.Object.DestroyImmediate(m_RewardManagerObject);
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
    public void ServiceDesk_OccupiedStrongholdsUseConfiguredStepAndCap()
    {
        InitializeStrongholds(
            EntitySideHelper.PlayerFactionId,
            EntitySideHelper.PlayerFactionId,
            EntitySideHelper.PlayerFactionId,
            EntitySideHelper.PlayerFactionId,
            EntitySideHelper.EnemyFactionId);
        LogicEntityState serviceDesk = CreateProductionBuilding(
            "Buil_ServiceDesk_Lv1",
            "production-service-desk-1",
            10);

        LogicBuildingProductionService.Refresh(serviceDesk);

        Assert.AreEqual(ProductionType.ByOccupiedStrongholdCount, serviceDesk.ProductionProps.ProductionType);
        Assert.AreEqual(4, serviceDesk.ProductionProps.ConditionCount);
        Assert.AreEqual((Fix64)2, serviceDesk.ProductionProps.DynamicProduction);
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(serviceDesk));
    }

    [Test]
    public void TrophyRack_UsesPreviousDayHeavyKillsAndConfiguredCap()
    {
        InitializeStrongholds(EntitySideHelper.PlayerFactionId);
        LogicEntityState trophyRack = CreateProductionBuilding(
            "Buil_TrophyRack_Lv1",
            "production-trophy-rack-1",
            10);
        LogicEntityId first = CreateUnit("Unit_BoneButcher", SideType.EnemySide, "SH_0_1", FixVector2.Zero);
        LogicEntityId second = CreateUnit("Unit_BoneButcher", SideType.EnemySide, "SH_0_1", FixVector2.Zero);
        LogicEntityId third = CreateUnit("Unit_BoneButcher", SideType.EnemySide, "SH_0_1", FixVector2.Zero);
        LogicTimeControlService.BeginFrame(2);
        LogicEntityLifecycleService.ApplyFrame(2);
        LogicProductionConditionState.RecordUnitDeath(LogicEntityStateStore.GetRequired(first));
        LogicProductionConditionState.RecordUnitDeath(LogicEntityStateStore.GetRequired(second));
        LogicProductionConditionState.RecordUnitDeath(LogicEntityStateStore.GetRequired(third));
        InGameDataModel.SetValue(IngameValueType.Day, 2, false);

        LogicBuildingProductionService.Refresh(trophyRack);

        Assert.AreEqual(ProductionType.ByHeavyKillCount, trophyRack.ProductionProps.ProductionType);
        Assert.AreEqual(3, trophyRack.ProductionProps.ConditionCount);
        Assert.AreEqual((Fix64)2, trophyRack.ProductionProps.DynamicProduction);
        Assert.AreEqual(12, LogicBuildingProductionService.GetProduction(trophyRack));
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
    public void InsuranceOffice_AccumulatesEachBuildBoundaryAndReleasesAfterPreviousDayDamage()
    {
        InitializeStrongholds(EntitySideHelper.PlayerFactionId);
        LogicEntityState insurance = CreateProductionBuilding(
            "Buil_InsuranceOffice_Lv1",
            "production-insurance-1",
            10);
        InGameDataModel.SetValue(IngameValueType.Day, 2, false);

        LogicBuildingProductionService.PrepareBuildPhase(true);

        Assert.AreEqual(ProductionType.StoredUntilBuildingDamaged, insurance.ProductionProps.ProductionType);
        Assert.AreEqual(10, insurance.ProductionProps.StoredProduction);
        Assert.AreEqual(0, LogicBuildingProductionService.GetProduction(insurance));

        insurance.TakeDamage(Fix64.One, HealthModifyType.reduce);
        InGameDataModel.SetValue(IngameValueType.Day, 3, false);
        LogicBuildingProductionService.PrepareBuildPhase(true);

        Assert.AreEqual(20, insurance.ProductionProps.ConditionCount);
        Assert.AreEqual(20, insurance.ProductionProps.StoredProduction);
        Assert.AreEqual(20, LogicBuildingProductionService.GetProduction(insurance));

        InGameDataModel.EnsureProductionBuildingCoinReserves(insurance.BuildingInstanceId, 20);
        Assert.AreEqual(20, LogicBuildingProductionService.GrantProduction(insurance));
        Assert.AreEqual(0, insurance.ProductionProps.StoredProduction);
        Assert.AreEqual(0, LogicBuildingProductionService.GetProduction(insurance));
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

    private static LogicEntityState CreateProductionBuilding(
        string identifier,
        string buildingInstanceId,
        int production)
    {
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.PlayerSide,
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
                    EntitySideHelper.PlayerFactionId,
                    LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                    Array.Empty<LogicCombatShape>(),
                    Array.Empty<LogicInteractionOptionDescriptor>(),
                    true);
                LogicBuildingProductionService.Configure(state);
            });

        LogicTimeControlService.BeginFrame(1);
        LogicEntityLifecycleService.ApplyFrame(1);
        return LogicEntityStateStore.GetRequired(entityId);
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
