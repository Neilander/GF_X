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
    public void GameEnd_InterruptsAttacksAndStopsMovementBeforePublishingResult()
    {
        LogicEntityState target = CreateBuilding("target-enemy", EntitySideHelper.EnemyFactionId, true);
        LogicEntityState combatant = CreateBuilding("combatant", EntitySideHelper.EnemyFactionId, false);
        var attack = new ObservedAttackComp();
        var move = new ObservedMoveComp();
        combatant.SetAtkComp(attack);
        combatant.SetMoveComp(move);
        attack.Init(combatant);
        move.Init(combatant);

        LogicGameEndService.Initialize(CreateLevel(
            new[] { VictoryConditionType.OccupySpecificBuildings },
            0,
            Array.Empty<FailConditionType>(),
            0));
        LogicGameEndService.RegisterInitialConditionBuilding(target.BuildingInstanceId, target.OwnerFactionId);
        PublishEntities();

        bool resultObservedAfterStop = false;
        void ObserveResult(LogicGameEndResult _) =>
            resultObservedAfterStop = !attack.IsAttacking && !move.IsMoving;
        LogicGameEndService.GameEnded += ObserveResult;
        try
        {
            target.SetOwnerFaction(EntitySideHelper.PlayerFactionId);
            LogicTimeControlService.BeginFrame(1);
            LogicGameEndService.ApplyFrame(1);
        }
        finally
        {
            LogicGameEndService.GameEnded -= ObserveResult;
        }

        Assert.IsFalse(attack.IsAttacking);
        Assert.AreEqual(1, attack.InterruptCount);
        Assert.AreEqual(AttackInterruptReason.Forced, attack.LastInterruptReason);
        Assert.IsFalse(move.IsMoving);
        Assert.AreEqual(1, move.StopCount);
        Assert.IsTrue(resultObservedAfterStop);
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
    public void ScriptedTutorialCompletion_ProducesCompleteTutorialWin()
    {
        LevelData level = CreateLevel(
            new[] { VictoryConditionType.CompleteTutorial },
            0,
            Array.Empty<FailConditionType>(),
            0);
        SetPrivate(level, nameof(LevelData.Identifier), "Lv_1");
        LogicGameEndService.Initialize(level);

        LogicGameEndResult? captured = null;
        void Capture(LogicGameEndResult result) => captured = result;
        LogicGameEndService.GameEnded += Capture;
        try
        {
            LogicGameEndService.CompleteScriptedWin(VictoryConditionType.CompleteTutorial);
        }
        finally
        {
            LogicGameEndService.GameEnded -= Capture;
        }

        Assert.IsTrue(LogicGameEndService.IsGameEnded);
        Assert.IsTrue(LogicGameEndService.IsWin);
        Assert.IsTrue(captured.HasValue);
        Assert.IsTrue(captured.Value.IsWin);
        Assert.AreEqual(VictoryConditionType.CompleteTutorial, captured.Value.VictoryCondition);
    }

    [Test]
    public void RuntimeScheduler_StopsAfterGameEndWithoutPausingPresentationTime()
    {
        LevelData level = CreateLevel(
            new[] { VictoryConditionType.CompleteTutorial },
            0,
            Array.Empty<FailConditionType>(),
            0);
        SetPrivate(level, nameof(LevelData.Identifier), "Lv_1");
        LogicGameEndService.Initialize(level);
        LogicGameEndService.CompleteScriptedWin(VictoryConditionType.CompleteTutorial);

        MethodInfo prepareNextFrame = typeof(RuntimeProcedureBase).GetMethod(
            "PrepareNextLogicFrame",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(prepareNextFrame);
        double schedulerScale = (double)prepareNextFrame.Invoke(new ArenaProcedure(), null);

        Assert.AreEqual(0d, schedulerScale);
        Assert.IsFalse(LogicTimeControlService.IsPaused);
        Assert.AreEqual(1f, LogicTimeControlService.AnimationScale);
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
    public void DefendFallbackResolution_UsesLogicAuthorityWithoutGameEndManagerView()
    {
        LogicEntityState target = CreateBuilding("target-player", EntitySideHelper.PlayerFactionId, true);
        LogicGameEndService.Initialize(CreateLevel(
            Array.Empty<VictoryConditionType>(),
            0,
            new[] { FailConditionType.LoseSpecificBuildings },
            0));
        LogicGameEndService.RegisterInitialConditionBuilding(target.BuildingInstanceId, target.OwnerFactionId);
        PublishEntities();
        Assert.IsNull(GameEndManager.Current);

        MethodInfo resolve = typeof(LogicUnitConfigurator).GetMethod(
            "ResolveDefendFallbackTarget",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(resolve);
        var enemyState = new LogicEntityState(
            new LogicEntityId(10001),
            new LogicEntitySpawnDescriptor(
                new FixVector2((Fix64)5, Fix64.Zero),
                new FixVector2(Fix64.Zero, Fix64.One),
                SideType.EnemySide,
                "defend-enemy"));
        var entityParams = new EntityParams
        {
            BrainType = BrainType.DefendEnemyAI,
            Side = SideType.EnemySide,
        };

        IEntityContext resolved = (IEntityContext)resolve.Invoke(
            null,
            new object[] { enemyState, entityParams });

        Assert.AreSame(target, resolved);
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
        LogicEntityLifecycleService.CommitPendingInitializationEntities();
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
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(LogicGameEndServiceTests));
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
    }

    private sealed class ObservedAttackComp : IAtkComp
    {
        public bool IsAttacking { get; private set; } = true;
        public int InterruptCount { get; private set; }
        public AttackInterruptReason LastInterruptReason { get; private set; }

        public void Init(IEntityContext ctx) { }
        public void Attack(Fix64 deltaTime) { }

        public void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced)
        {
            IsAttacking = false;
            InterruptCount++;
            LastInterruptReason = reason;
        }

        public void ShutDown() { }
        public void Resume() { }
    }

    private sealed class ObservedMoveComp : IMoveComp
    {
        public FixVector2 NavDirectionFixed => FixVector2.Zero;
        public bool IsMoving { get; private set; } = true;
        public int StopCount { get; private set; }

        public void Init(IEntityContext ctx) { }
        public void Move(Fix64 deltaTime) { }
        public void MoveToFixed(FixVector2 destination) { }

        public void StopMove()
        {
            IsMoving = false;
            StopCount++;
        }

        public void CommitResolvedDisplacement(FixVector2 displacement) { }
        public void SetNavTargetFixed(FixVector2 destination) { }
        public void ShutDown() { }
        public void Resume() { }
    }
}
