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
        EnsureInGameDataModel();
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
        LogicEntityLifecycleService.BeginTimeline();
        LogicStrongholdMap.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        if (m_RewardManagerObject != null)
            UnityEngine.Object.DestroyImmediate(m_RewardManagerObject);
        EntityRegistry.Clear();
        LogicStrongholdMap.Clear();
        LogicEntityLifecycleService.EndTimeline();
        LogicPhaseCommandService.EndTimeline();
        LogicTimeControlService.EndTimeline();
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
    public void Nursery_DefaultBonusCapRemainsThree()
    {
        InGameDataModel.SetValue(IngameValueType.Day, 10, false);
        LogicEntityState nursery = CreateProductionBuilding("Buil_Nursery_Lv1", "production-nursery-1", 10);
        nursery.ProductionProps.ProductionTraitFirstDay = 1;

        LogicBuildingProductionService.Refresh(nursery);

        Assert.AreEqual((Fix64)3, nursery.ProductionProps.DynamicProduction);
        Assert.AreEqual(13, LogicBuildingProductionService.GetProduction(nursery));
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
                        Array.Empty<Fix64>(),
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
            });
    }

    private static void EnsureInGameDataModel()
    {
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

}
