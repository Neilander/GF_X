using System;
using System.Collections.Generic;
using System.Reflection;
using GameFramework;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicGameEndServiceTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
        LogicGameEndService.BeginTimeline();
        EnsureInGameDataModel();
        InGameDataModel.SetValue(IngameValueType.Day, 1, false);
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        if (LogicGameEndService.IsActive)
            LogicGameEndService.EndTimeline();
        if (LogicEntityLifecycleService.IsActive)
            LogicEntityLifecycleService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void ViewlessEnemyTargetCapturedOnExactLogicFrame_Wins()
    {
        LogicEntityState target = CreateBuilding("target-enemy", EntitySideHelper.EnemyFactionId, true);
        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0));
        LogicGameEndService.RegisterInitialConditionBuilding(target.BuildingInstanceId, target.OwnerFactionId);
        PublishEntities();

        target.SetOwnerFaction(EntitySideHelper.PlayerFactionId);
        Assert.IsFalse(LogicGameEndService.IsGameEnded);

        LogicTimeControlService.BeginFrame(1);
        LogicGameEndService.ApplyFrame(1);

        Assert.IsTrue(LogicGameEndService.IsGameEnded);
        Assert.IsTrue(LogicGameEndService.IsWin);
        Assert.AreEqual(1ul, LogicGameEndService.LastAppliedFrame);
    }

    [Test]
    public void ViewlessPlayerTargetDisabledOnExactLogicFrame_Fails()
    {
        LogicEntityState target = CreateBuilding("target-player", EntitySideHelper.PlayerFactionId, true);
        LogicGameEndService.Initialize(
            CreateLevel(
                Array.Empty<VictoryConditionType>(),
                0,
                new[] { FailConditionType.LoseSpecificBuildings },
                0));
        LogicGameEndService.RegisterInitialConditionBuilding(target.BuildingInstanceId, target.OwnerFactionId);
        PublishEntities();

        target.TakeDamage((Fix64)1000, HealthModifyType.empty);
        Assert.IsTrue(target.IsDisabled);
        Assert.IsFalse(LogicGameEndService.IsGameEnded);

        LogicTimeControlService.BeginFrame(1);
        LogicGameEndService.ApplyFrame(1);

        Assert.IsTrue(LogicGameEndService.IsGameEnded);
        Assert.IsFalse(LogicGameEndService.IsWin);
    }

    [Test]
    public void RegisteringTargetsDoesNotEvaluateBeforeFirstCompleteTick()
    {
        LogicEntityState target = CreateBuilding("target-late", EntitySideHelper.EnemyFactionId, true);
        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0));
        LogicGameEndService.RegisterInitialConditionBuilding(target.BuildingInstanceId, target.OwnerFactionId);
        PublishEntities();
        target.SetOwnerFaction(EntitySideHelper.PlayerFactionId);

        Assert.IsFalse(LogicGameEndService.IsGameEnded);
        LogicTimeControlService.BeginFrame(1);
        LogicGameEndService.ApplyFrame(1);
        Assert.IsTrue(LogicGameEndService.IsGameEnded);
    }

    [Test]
    public void DayConditionReadsLogicStateWithoutEventPump()
    {
        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.SurviveAmountDays },
            1,
            Array.Empty<FailConditionType>(),
            0));
        InGameDataModel.SetValue(IngameValueType.Day, 2, false);

        LogicTimeControlService.BeginFrame(1);
        LogicGameEndService.ApplyFrame(1);

        Assert.IsTrue(LogicGameEndService.IsGameEnded);
        Assert.IsTrue(LogicGameEndService.IsWin);
    }

    [Test]
    public void TargetHashIsStableForRegistrationOrder()
    {
        LogicEntityState first = CreateBuilding("target-z", EntitySideHelper.EnemyFactionId, true);
        LogicEntityState second = CreateBuilding("target-a", EntitySideHelper.EnemyFactionId, true);
        LevelData level = CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0);
        LogicGameEndService.Initialize(level);
        LogicGameEndService.RegisterInitialConditionBuilding(first.BuildingInstanceId, first.OwnerFactionId);
        LogicGameEndService.RegisterInitialConditionBuilding(second.BuildingInstanceId, second.OwnerFactionId);
        var forward = new LogicStateHasher();
        LogicGameEndService.WriteDeterministicState(forward);

        LogicGameEndService.ResetForWorldTransition();
        LogicGameEndService.Initialize(level);
        LogicGameEndService.RegisterInitialConditionBuilding(second.BuildingInstanceId, second.OwnerFactionId);
        LogicGameEndService.RegisterInitialConditionBuilding(first.BuildingInstanceId, first.OwnerFactionId);
        var reverse = new LogicStateHasher();
        LogicGameEndService.WriteDeterministicState(reverse);

        Assert.AreEqual(forward.Hash, reverse.Hash);
    }

    [Test]
    public void TargetHashIsIndependentOfViewBinding()
    {
        LogicEntityState first = CreateBuilding("target-z", EntitySideHelper.EnemyFactionId, true);
        LogicEntityState second = CreateBuilding("target-a", EntitySideHelper.EnemyFactionId, true);
        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0));
        LogicGameEndService.RegisterInitialConditionBuilding(first.BuildingInstanceId, first.OwnerFactionId);
        LogicGameEndService.RegisterInitialConditionBuilding(second.BuildingInstanceId, second.OwnerFactionId);
        PublishEntities();
        LogicTimeControlService.BeginFrame(1);
        LogicGameEndService.ApplyFrame(1);

        var withoutView = new LogicStateHasher();
        LogicGameEndService.WriteDeterministicState(withoutView);
        LogicEntityLifecycleService.BindView(first.LogicEntityId, 1001);
        var withView = new LogicStateHasher();
        LogicGameEndService.WriteDeterministicState(withView);
        Assert.AreEqual(withoutView.Hash, withView.Hash);
        LogicEntityLifecycleService.UnbindView(first.LogicEntityId, 1001);
    }

    [Test]
    public void InvalidRegistrationAndMissingTargetFailLoudly()
    {
        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0));
        Assert.Throws<ArgumentException>(() =>
            LogicGameEndService.RegisterInitialConditionBuilding("", EntitySideHelper.EnemyFactionId));
        LogicGameEndService.RegisterInitialConditionBuilding("missing", EntitySideHelper.EnemyFactionId);
        LogicTimeControlService.BeginFrame(1);
        Assert.Throws<InvalidOperationException>(() => LogicGameEndService.ApplyFrame(1));
    }

    private static LogicEntityState CreateBuilding(string instanceId, int ownerFactionId, bool gameEndCondition)
    {
        SideType side = EntitySideHelper.ToSide(ownerFactionId);
        var descriptor = new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            side,
            instanceId);
        return LogicEntityStateStore.GetRequired(
            LogicEntityLifecycleService.RequestConfiguredSpawn(
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
                        CreateBuildingData(instanceId),
                        instanceId,
                        "game-end-test",
                        ownerFactionId,
                        LogicCombatShape.AxisAlignedBox(FixVector2.Zero, new FixVector2(Fix64.One, Fix64.One)),
                        Array.Empty<LogicCombatShape>(),
                        Array.Empty<LogicInteractionOptionDescriptor>(),
                        false,
                        isGameEndConditionBuilding: gameEndCondition);
                }));
    }

    private static void PublishEntities()
    {
        LogicEntityLifecycleService.PublishPendingInitializationEntities();
    }

    private static LevelData CreateLevel(
        VictoryConditionType[] victoryConditions,
        int victoryValue,
        FailConditionType[] failConditions,
        int failValue)
    {
        var level = new LevelData();
        SetPrivate(level, nameof(LevelData.Identifier), "LogicGameEndServiceTests");
        SetPrivate(level, nameof(LevelData.VictoryConditions), victoryConditions);
        SetPrivate(level, nameof(LevelData.VictoryValue), victoryValue);
        SetPrivate(level, nameof(LevelData.LoseConditions), failConditions);
        SetPrivate(level, nameof(LevelData.LoseValue), failValue);
        return level;
    }

    private static void SetPrivate<T>(LevelData level, string propertyName, T value)
    {
        typeof(LevelData).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            .SetValue(level, value, null);
    }

    private static BuildingData CreateBuildingData(string identifier)
    {
        return new BuildingData(
            identifier,
            BuilType.Def,
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
            0,
            Array.Empty<string>());
    }

    private static void EnsureInGameDataModel()
    {
        FieldInfo dataModelField = typeof(GF).GetField(
            "<DataModel>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        var component = dataModelField?.GetValue(null) as DataModelComponent;
        if (component == null)
        {
            var gameObject = new GameObject("LogicGameEndServiceTests_DataModel");
            component = gameObject.AddComponent<DataModelComponent>();
            dataModelField?.SetValue(null, component);
        }

        FieldInfo dataModelsField = typeof(DataModelComponent).GetField(
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
            Type pairType = typeof(DataModelComponent).Assembly.GetType("TypeIdPair");
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
            ?.SetValue(model, new Dictionary<IngameValueType, int>());
    }
}
