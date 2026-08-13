using NUnit.Framework;
using System;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// SteeringMovement 纯逻辑测试。
/// 验证 Seek、Separation、AvoidEntity 和 SoldierAIBrain 状态机。
/// </summary>
[TestFixture]
public class SteeringMovementTests
{
    private LogicTestGroupMoveManagerAuthority m_GroupMoveAuthority;

    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        bool[] walkable = new bool[64 * 64];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(64, 64, 1f, new Vector3(-32f, 0f, -32f), walkable);
        FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        FlowFieldNavigationConfig config = ScriptableObject.CreateInstance<FlowFieldNavigationConfig>();
        config.SectorSizeInCells = 4;
        config.PortalNarrowWidthCells = 1;
        config.FlowTileCacheLimit = 32;
        config.WorldBuildOperationQuota = 1_000_000;
        config.RuntimeRebuildOperationQuota = 1_000_000;
        config.DeterministicFlowTileCommitQuota = 1_000_000;
        config.SharedGoalBuildOperationQuota = 1_000_000;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        m_GroupMoveAuthority = LogicTestGroupMoveManagerAuthority.Create(nameof(SteeringMovementTests));
        SetupCombatPhaseForTests();
        if (LogicTimeControlService.IsActive || LogicPhaseCommandService.IsActive)
            throw new InvalidOperationException("SteeringMovementTests requires inactive logic phase services at setup.");
        LogicTimeControlService.BeginTimeline();
        LogicPhaseCommandService.BeginTimeline();
        LogicPhaseCommandService.SetInitialPhase(GamePhase.Defend);
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.EndTimeline();
        if (LogicTimeControlService.IsActive)
            LogicTimeControlService.EndTimeline();
        m_GroupMoveAuthority?.Dispose();
        m_GroupMoveAuthority = null;
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
    }

    private static void SetupCombatPhaseForTests()
    {
        LogicTestInGameDataModelAuthority.Ensure(GamePhase.Defend, nameof(SteeringMovementTests));
        var dataModelField = typeof(GF).GetField("<DataModel>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        var current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            var go = new GameObject("TestGF_DataModel");
            current = go.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        var dataModelsField = typeof(GameFramework.DataModelComponent).GetField("m_DataModels", BindingFlags.Instance | BindingFlags.NonPublic);
        var dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = System.Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }

        var model = current.GetDataModel<InGameDataModel>();
        if (model == null)
        {
            model = (InGameDataModel)System.Activator.CreateInstance(typeof(InGameDataModel), true);
            var typeIdPairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
            var pair = System.Activator.CreateInstance(typeIdPairType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { typeof(InGameDataModel), 0 }, null);
            var dict = dataModelsField.GetValue(current);
            dict.GetType().GetMethod("Add").Invoke(dict, new[] { pair, model });
        }

        var phaseField = typeof(InGameDataModel).GetField("m_IngameValue", BindingFlags.Instance | BindingFlags.NonPublic);
        var values = new Dictionary<IngameValueType, int>
        {
            [IngameValueType.Phase] = (int)GamePhase.Defend,
            [IngameValueType.Day] = 1,
            [IngameValueType.Coin] = 0,
            [IngameValueType.CurrentSupply] = 0,
            [IngameValueType.MaxSupply] = 0,
        };
        phaseField?.SetValue(model, values);
    }

    [Test]
    public void BrainAuthorityConstants_UseRawFixedValues()
    {
        string scriptsRoot = System.IO.Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string[] brainFiles =
        {
            System.IO.Path.Combine(scriptsRoot, "Entity", "EnemyAIBrain.cs"),
            System.IO.Path.Combine(scriptsRoot, "Entity", "FriendlyAIBrain.cs"),
            System.IO.Path.Combine(scriptsRoot, "Movement", "SoldierAIBrain.cs"),
        };
        var floatToFixedPattern = new System.Text.RegularExpressions.Regex(
            @"\(Fix64\)\s*\(?-?\d+(?:\.\d+)?f\)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        for (int i = 0; i < brainFiles.Length; i++)
        {
            string source = System.IO.File.ReadAllText(brainFiles[i]);
            Assert.That(
                floatToFixedPattern.Matches(source).Count,
                Is.Zero,
                $"Brain authority source still converts float literals to Fix64: {brainFiles[i]}.");
        }

        var enemy = new EnemyAIBrain();
        AssertRaw(6554, enemy.AttackRange, nameof(enemy.AttackRange));
        AssertRaw(6144, enemy.SeparationRadius, nameof(enemy.SeparationRadius));
        AssertRaw(4916, enemy.SeparationWeight, nameof(enemy.SeparationWeight));

        var friendly = new FriendlyAIBrain();
        AssertRaw(6554, friendly.AttackRange, nameof(friendly.AttackRange));
        AssertRaw(1229, friendly.FollowUpdateInterval, nameof(friendly.FollowUpdateInterval));

        var soldier = new SoldierAIBrain();
        AssertRaw(40960, soldier.DetectEnemyRange, nameof(soldier.DetectEnemyRange));
        AssertRaw(94208, soldier.ChaseRange, nameof(soldier.ChaseRange));
        AssertRaw(6144, soldier.HomeArrivedRadius, nameof(soldier.HomeArrivedRadius));
        AssertRaw(2048, soldier.ReturnSpeedBonusPercent, nameof(soldier.ReturnSpeedBonusPercent));
        AssertRaw(820, soldier.ReturnHpRegenPercentPerSec, nameof(soldier.ReturnHpRegenPercentPerSec));
        Assert.IsNull(typeof(SoldierAIBrain).GetField("WeaponRange"));
        Assert.IsNull(typeof(SoldierAIBrain).GetField("SoftReturnRatio"));

        AssertStaticRaw<SoldierAIBrain>("CombatApproachRangeSlackFixed", 328);
        AssertStaticRaw<SoldierAIBrain>("CombatApproachRingSpacingFixed", 2253);
        AssertStaticRaw<SoldierAIBrain>("CombatApproachOccupancyPaddingFixed", 1434);
        Assert.IsNull(typeof(SoldierAIBrain).GetField(
            "FallbackDeadZoneRange",
            BindingFlags.Static | BindingFlags.NonPublic));
        Assert.IsNull(typeof(SoldierAIBrain).GetField(
            "FallbackInnerDeadZoneRange",
            BindingFlags.Static | BindingFlags.NonPublic));

        var directions = (FixVector2[])typeof(SoldierAIBrain)
            .GetField("StableDeadZoneDirections", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null);
        Assert.NotNull(directions);
        long[,] expectedDirectionRaw =
        {
            { 4096, 0 }, { 3785, 1568 }, { 2897, 2897 }, { 1568, 3785 },
            { 0, 4096 }, { -1568, 3785 }, { -2897, 2897 }, { -3785, 1568 },
            { -4096, 0 }, { -3785, -1568 }, { -2897, -2897 }, { -1568, -3785 },
            { 0, -4096 }, { 1568, -3785 }, { 2897, -2897 }, { 3785, -1568 },
        };
        Assert.AreEqual(expectedDirectionRaw.GetLength(0), directions.Length);
        for (int i = 0; i < directions.Length; i++)
        {
            Assert.AreEqual(expectedDirectionRaw[i, 0], directions[i].x.RawValue, $"Stable direction {i} X raw mismatch.");
            Assert.AreEqual(expectedDirectionRaw[i, 1], directions[i].y.RawValue, $"Stable direction {i} Y raw mismatch.");
        }

        Fix64[] legacyValues =
        {
            (Fix64)1.6f,
            (Fix64)1.5f,
            (Fix64)1.2f,
            (Fix64)0.9238795f,
            (Fix64)0.7071068f,
            (Fix64)0.3826834f,
            (Fix64)0.6f,
            (Fix64)0.5f,
            (Fix64)0.45f,
            (Fix64)0.3f,
            (Fix64)0.25f,
            (Fix64)0.2f,
            (Fix64)0.15f,
            (Fix64)0.12f,
            (Fix64)0.08f,
            (Fix64)0.05f,
            (Fix64)0.01f,
            (Fix64)0.0001f,
        };
        long[] expectedRaw =
        {
            6554,
            6144,
            4916,
            3785,
            2897,
            1568,
            2458,
            2048,
            1844,
            1229,
            1024,
            820,
            615,
            492,
            328,
            205,
            41,
            1,
        };
        Assert.AreEqual(expectedRaw.Length, legacyValues.Length);
        for (int i = 0; i < expectedRaw.Length; i++)
            Assert.AreEqual(expectedRaw[i], legacyValues[i].RawValue, $"Legacy Q12 raw mismatch at index {i}.");
    }

    private static void AssertRaw(long expected, Fix64 actual, string fieldName)
    {
        Assert.AreEqual(expected, actual.RawValue, $"{fieldName} raw mismatch.");
    }

    private static void AssertStaticRaw<T>(string fieldName, long expected)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field, $"Missing fixed authority field {typeof(T).Name}.{fieldName}.");
        AssertRaw(expected, (Fix64)field.GetValue(null), $"{typeof(T).Name}.{fieldName}");
    }

    #region Seek

    [Test]
    public void Seek_朝目标方向返回归一化向量()
    {
        var result = SteeringMovement.Seek(Vector3.zero, new Vector3(10, 0, 0));
        Assert.AreEqual(1f, result.x, 0.01f);
        Assert.AreEqual(0f, result.z, 0.01f);
    }

    [Test]
    public void Seek_在到达半径内减速()
    {
        // arriveRadius=2, 距离=1, 应该返回 0.5 强度
        var result = SteeringMovement.Seek(new Vector3(9, 0, 0), new Vector3(10, 0, 0), arriveRadius: 2f);
        Assert.Less(result.magnitude, 1f, "在 arrive 半径内应减速");
        Assert.Greater(result.magnitude, 0f, "还没到目标应有力");
    }

    [Test]
    public void Seek_到达目标返回零向量()
    {
        var result = SteeringMovement.Seek(new Vector3(5, 0, 3), new Vector3(5, 0, 3));
        Assert.AreEqual(Vector3.zero, result);
    }

    #endregion

    #region Separation

    [Test]
    public void Separation_无邻居返回零()
    {
        var result = SteeringMovement.Separation(Vector3.zero, new List<Vector3>(), 2f);
        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void Separation_推离近邻()
    {
        var neighbors = new List<Vector3> { new Vector3(0.5f, 0, 0) };
        var result = SteeringMovement.Separation(Vector3.zero, neighbors, 2f);
        // 邻居在右边，分离力应向左（负X）
        Assert.Less(result.x, 0f, "应被推离邻居方向");
    }

    [Test]
    public void Separation_越近力越大()
    {
        var nearNeighbor = new List<Vector3> { new Vector3(0.3f, 0, 0) };
        var farNeighbor = new List<Vector3> { new Vector3(1.5f, 0, 0) };

        var forceNear = SteeringMovement.Separation(Vector3.zero, nearNeighbor, 2f);
        var forceFar = SteeringMovement.Separation(Vector3.zero, farNeighbor, 2f);

        Assert.Greater(forceNear.magnitude, forceFar.magnitude, "近处的分离力应更大");
    }

    [Test]
    public void Separation_超出半径无力()
    {
        var neighbors = new List<Vector3> { new Vector3(5f, 0, 0) };
        var result = SteeringMovement.Separation(Vector3.zero, neighbors, 2f);
        Assert.AreEqual(Vector3.zero, result);
    }

    #endregion

    #region AvoidEntity

    [Test]
    public void AvoidEntity_玩家在附近时产生排斥力()
    {
        var result = SteeringMovement.AvoidEntity(Vector3.zero, new Vector3(1f, 0, 0), avoidRadius: 3f, strength: 2f);
        Assert.Less(result.x, 0f, "应远离玩家（负X方向）");
        Assert.Greater(result.magnitude, 0f);
    }

    [Test]
    public void AvoidEntity_玩家超出范围无力()
    {
        var result = SteeringMovement.AvoidEntity(Vector3.zero, new Vector3(10f, 0, 0), avoidRadius: 3f);
        Assert.AreEqual(Vector3.zero, result);
    }

    #endregion

    #region SoldierAIBrain 状态机

    [Test]
    public void Follow状态_缺少GroupMoveManager时明确报错()
    {
        m_GroupMoveAuthority.Detach();
        Assert.IsFalse(GroupMoveManager.HasInstance, "测试前提：未装配 GroupMoveManager。");
        var player = MakeSoldier(Vector3.zero);
        var soldier = MakeSoldier(new Vector3(5f, 0f, 0f));
        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        soldier.Brain = brain;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime));

        StringAssert.Contains("GroupMoveManager", exception.Message);
    }

    [Test]
    public void Combat状态_缺少WeaponComp时明确报错()
    {
        var soldier = MakeSoldier(Vector3.zero);
        soldier.WeaponComp = null;
        var enemy = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);
        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { soldier, enemy });
        targeting.Init(soldier);
        targeting.CurrentTarget = enemy;
        soldier.TargetComp = targeting;

        var brain = new SoldierAIBrain();
        soldier.Brain = brain;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime));

        StringAssert.Contains("WeaponComp", exception.Message);
    }

    private SimEntityContext MakeSoldier(Vector3 pos, SideType side = SideType.PlayerSide)
    {
        var ctx = new SimEntityContext
        {
            Position = pos,
            Side = side,
            Alive = true
        };
        ctx.SetProperty(CreatureMainProperty.Speed, (Fix64)5f);
        ctx.WeaponComp = CreateTestWeaponComp((Fix64)1.5f);

        var exec = new SimMoveExecutor();
        exec.Position = pos;
        ctx.MoveExecutor = exec;

        var moveComp = new SimMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        var atkComp = new SimAtkComp();
        atkComp.Init(ctx);
        ctx.AtkComp = atkComp;

        var targetingComp = new NoTargetingComp();
        targetingComp.Init(ctx);
        ctx.TargetComp = targetingComp;

        var buffComp = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        buffComp.Init(ctx);
        ctx.BuffComp = buffComp;

        return ctx;
    }

    private static WeaponComp CreateTestWeaponComp(Fix64 worldRange)
    {
        var data = new WeaponData(
            WeaponType.Melee,
            Fix64.One,
            Fix64.One,
            DistanceUnitConverter.ConvertFromWorld(worldRange),
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.One,
            Fix64.Zero,
            Array.Empty<Fix64>());
        return new WeaponComp(data.ToWeapon("SteeringMovementTests"));
    }

    [Test]
    public void 小兵Idle状态_玩家靠近后转为Follow()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(5, 0, 0));

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        brain.Inject();
        soldier.Brain = brain;

        Assert.AreEqual(SoldierAIBrain.SoldierState.Idle, brain.State);

        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "玩家在 8 范围内应转为 Follow");
    }

    [Test]
    public void 等距索敌使用较小LogicEntityId打破平局()
    {
        var self = MakeSoldier(Vector3.zero);
        self.LogicEntityId = new LogicEntityId(100);
        self.Side = SideType.PlayerSide;

        var higherIdEnemy = MakeSoldier(new Vector3(1f, 0f, 0f));
        higherIdEnemy.LogicEntityId = new LogicEntityId(20);
        higherIdEnemy.Side = SideType.EnemySide;

        var lowerIdEnemy = MakeSoldier(new Vector3(-1f, 0f, 0f));
        lowerIdEnemy.LogicEntityId = new LogicEntityId(10);
        lowerIdEnemy.Side = SideType.EnemySide;

        EntityRegistry.Register(higherIdEnemy);
        EntityRegistry.Register(self);
        EntityRegistry.Register(lowerIdEnemy);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)2f };
        targeting.Init(self);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(lowerIdEnemy, targeting.CurrentTarget);
    }

    [Test]
    public void Targeting_SelectsHighestConfiguredThreatAcrossAggroRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var closeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var distantTauntingTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        closeTarget.TauntLevel = 1;
        distantTauntingTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(closeTarget);
        EntityRegistry.Register(distantTauntingTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)5 };
        targeting.Init(self);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(distantTauntingTarget, targeting.CurrentTarget,
            "Initial targeting must maximize tauntLevel * configuredThreat - gameDistance.");
    }

    [Test]
    public void Targeting_UsesCombinedThreatInsteadOfLexicographicTauntPriority()
    {
        const string configKey = "TauntThreatPerLevel";
        Assert.IsTrue(
            DistanceUnitConverter.TryGetEditorTestPositiveFixedConfig(configKey, out Fix64 previousValue),
            "Test setup requires a configured taunt threat value.");
        DistanceUnitConverter.SetEditorTestPositiveFixedConfig(configKey, (Fix64)20);
        try
        {
            var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
            var closeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
            var distantHigherTauntTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
            closeTarget.TauntLevel = 1;
            distantHigherTauntTarget.TauntLevel = 2;
            EntityRegistry.Register(self);
            EntityRegistry.Register(closeTarget);
            EntityRegistry.Register(distantHigherTauntTarget);

            var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)5 };
            targeting.Init(self);
            targeting.UpdateTargeting((Fix64)0.2f);

            Assert.AreSame(closeTarget, targeting.CurrentTarget,
                "A higher taunt level must lose when its level bonus is smaller than its extra game distance.");
        }
        finally
        {
            DistanceUnitConverter.SetEditorTestPositiveFixedConfig(configKey, previousValue);
        }
    }

    [Test]
    public void Attacking_SwitchesToHigherThreatLevelInsideAdditionalPursuitDistance()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var tauntingTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        tauntingTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(tauntingTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(tauntingTarget, targeting.CurrentTarget,
            "A higher threat level inside attackRange + configured pursuit distance must interrupt target lock.");
    }

    [Test]
    public void Attacking_DoesNotSwitchBeyondAdditionalPursuitDistance()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var distantTauntingTarget = MakeSoldier(new Vector3(18f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        distantTauntingTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(distantTauntingTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "Target lock must remain when the higher threat level is beyond attackRange + configured pursuit distance.");
    }

    [Test]
    public void Attacking_DoesNotSwitchToCloserTargetAtSameThreatLevel()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1.4f, 0f, 0f), SideType.EnemySide);
        var closerTarget = MakeSoldier(new Vector3(0.8f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        closerTarget.TauntLevel = 1;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(closerTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "An active attack remains locked unless another target has a higher threat level.");
    }

    [Test]
    public void AttackLock_RemainsBetweenAttackAnimations()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1.4f, 0f, 0f), SideType.EnemySide);
        var closerTarget = MakeSoldier(new Vector3(0.8f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(closerTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);
        SetAttacking(self, false);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "Once an attack starts, target lock must remain between attack animations.");
    }

    [Test]
    public void AdditionalPursuitTarget_RemainsValidOutsideLegacyForgetRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var tauntingTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        tauntingTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(tauntingTarget);

        var targeting = new CharacterTargetingComp
        {
            AggroRangeFixed = (Fix64)20,
            ForgetRangeFixed = (Fix64)8,
        };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);
        targeting.UpdateTargeting(Fix64.One / (Fix64)60);

        Assert.AreSame(tauntingTarget, targeting.CurrentTarget,
            "A target acquired through additional pursuit must remain retained up to attackRange + pursuit distance.");
    }

    [Test]
    public void DefendEnemyTargeting_AttackLockUsesAdditionalPursuitDistance()
    {
        var self = MakeSoldier(Vector3.zero, SideType.EnemySide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.PlayerSide);
        var tauntingTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.PlayerSide);
        currentTarget.TauntLevel = 1;
        tauntingTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(tauntingTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.UseDefendEnemyMode(null);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(tauntingTarget, targeting.CurrentTarget,
            "Defend-wave units must use the same taunt pursuit interruption rule.");
    }

    [Test]
    public void BuildingTargeting_ChoosesHighestThreatOnlyInsideAttackRange()
    {
        var tower = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        tower.WeaponComp = CreateTestWeaponComp((Fix64)1.5f);
        var inRangeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var outOfRangeTauntingTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.EnemySide);
        inRangeTarget.TauntLevel = 1;
        outOfRangeTauntingTarget.TauntLevel = 10;
        EntityRegistry.Register(tower);
        EntityRegistry.Register(inRangeTarget);
        EntityRegistry.Register(outOfRangeTauntingTarget);

        var targeting = new MeatRackTargetingComp();
        targeting.Init(tower);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(inRangeTarget, targeting.CurrentTarget,
            "Building targeting must not use the additional taunt pursuit distance.");
    }

    private static void SetAttacking(SimEntityContext entity, bool isAttacking)
    {
        var attack = entity.AtkComp as SimAtkComp;
        Assert.NotNull(attack, "Test setup requires SimAtkComp.");
        typeof(SimAtkComp)
            .GetProperty(nameof(SimAtkComp.IsAttacking), BindingFlags.Instance | BindingFlags.Public)
            .SetValue(attack, isAttacking);
    }

    [Test]
    public void Follow状态_进入领袖死区后停止主动靠近()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(5, 0, 0));

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State);

        Vector3 before = soldier.Position;
        soldier.MoveComp.Move((Fix64)0.2f);
        ((SimMoveExecutor)soldier.MoveExecutor).Execute(0.2f);
        soldier.SyncPositionFromExecutor();

        Assert.AreEqual(before.x, soldier.Position.x, 0.001f,
            $"当前跟随规则下，单位进入领袖死区后应停止主动靠近，before={before}, after={soldier.Position}");
    }

    [Test]
    public void 小兵Idle状态_玩家再远也转为Follow()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(20, 0, 0));

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "玩家超过招募距离时仍应转为 Follow");
    }

    [Test]
    public void Follow状态_玩家超过旧脱离距离仍保持Follow()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(5, 0, 0));

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State);

        soldier.Position = new Vector3(20, 0, 0);
        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "玩家超过旧脱离距离后仍应持续追踪");
    }

    [Test]
    public void 敌兵返航阈值使用定点出生点和位置()
    {
        var soldier = MakeSoldier(Vector3.zero, SideType.EnemySide);
        soldier.PositionFixed = new FixVector2(Fix64.FromRaw(Fix64.One.RawValue + 1), Fix64.Zero);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain
        {
            ChaseRange = (Fix64)1f,
            HomeArrivedRadius = (Fix64)0.1f,
        };
        brain.SetBirthPositionFixed(FixVector2.Zero);
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Returning, brain.State);
    }

    [Test]
    public void 返航状态缺少出生点时明确报错()
    {
        var soldier = MakeSoldier(Vector3.zero, SideType.EnemySide);
        soldier.LogicEntityId = new LogicEntityId(100);
        EntityRegistry.Register(soldier);
        var brain = new SoldierAIBrain();
        typeof(SoldierAIBrain)
            .GetProperty(nameof(SoldierAIBrain.State), BindingFlags.Instance | BindingFlags.Public)
            .SetValue(brain, SoldierAIBrain.SoldierState.Returning);
        soldier.Brain = brain;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime));

        StringAssert.Contains("has no birth position", exception.Message);
    }

    [Test]
    public void 敌兵进入返航时缺少Buff组件明确报错()
    {
        var soldier = MakeSoldier(new Vector3(2f, 0f, 0f), SideType.EnemySide);
        soldier.LogicEntityId = new LogicEntityId(101);
        soldier.BuffComp = null;
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain
        {
            ChaseRange = (Fix64)1,
            HomeArrivedRadius = (Fix64)0.1f,
        };
        brain.SetBirthPositionFixed(FixVector2.Zero);
        soldier.Brain = brain;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime));

        StringAssert.Contains("without a buff component", exception.Message);
        Assert.AreNotEqual(
            SoldierAIBrain.SoldierState.Returning,
            brain.State,
            "缺少返航效果的敌兵不能伪装成已进入完整 Returning 状态");
    }

    [Test]
    public void 敌兵被拉离出生点后丢失目标_立即触发强制返航效果()
    {
        var soldier = MakeSoldier(new Vector3(5f, 0f, 0f), SideType.EnemySide);
        var target = MakeSoldier(new Vector3(5.5f, 0f, 0f));
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(target);

        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { soldier, target });
        targeting.Init(soldier);
        targeting.CurrentTarget = target;
        soldier.TargetComp = targeting;

        var buffComp = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        buffComp.Init(soldier);
        soldier.BuffComp = buffComp;

        var brain = new SoldierAIBrain
        {
            ChaseRange = (Fix64)10,
            HomeArrivedRadius = (Fix64)1,
        };
        brain.SetBirthPositionFixed(FixVector2.Zero);
        soldier.Brain = brain;

        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);

        targeting.CurrentTarget = null;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Returning, brain.State,
            "敌兵未超 ChaseRange，但被拉离出生点后丢失目标，也应进入不可打断的强制返航");
        Assert.IsTrue(buffComp.HasBuff("soldier_returning"),
            "目标丢失返航必须复用超距返航入口并挂上同一个加速/回血 Buff");

        soldier.PositionFixed = FixVector2.Zero;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Idle, brain.State);
        Assert.IsTrue(soldier.CanRun(targeting), "敌兵到家后必须恢复 Targeting");
        Assert.IsFalse(buffComp.HasBuff("soldier_returning"), "敌兵到家后必须移除返航 Buff");
    }

    [Test]
    public void 敌兵当前目标死亡且附近有替代目标时继续战斗()
    {
        var soldier = MakeSoldier(new Vector3(5f, 0f, 0f), SideType.EnemySide);
        var deadTarget = MakeSoldier(new Vector3(5.5f, 0f, 0f));
        var replacement = MakeSoldier(new Vector3(6f, 0f, 0f));
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(deadTarget);
        EntityRegistry.Register(replacement);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)3f };
        targeting.Init(soldier);
        soldier.TargetComp = targeting;
        targeting.UpdateTargeting((Fix64)0.2f);
        Assert.AreSame(deadTarget, targeting.CurrentTarget, "测试前提：先锁定距离更近的旧目标");

        var brain = new SoldierAIBrain
        {
            ChaseRange = (Fix64)10,
            HomeArrivedRadius = (Fix64)1,
        };
        brain.SetBirthPositionFixed(FixVector2.Zero);
        soldier.Brain = brain;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
        var beforeLoss = new LogicStateHasher();
        brain.WriteDeterministicState(beforeLoss);

        deadTarget.Alive = false;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);
        var awaitingReplacement = new LogicStateHasher();
        brain.WriteDeterministicState(awaitingReplacement);
        Assert.AreNotEqual(beforeLoss.Hash, awaitingReplacement.Hash,
            "等待 Targeting 替换的跨阶段状态必须进入确定性哈希");
        if (soldier.CanRun(targeting))
            targeting.UpdateTargeting(LogicFrameRuntime.FixedDeltaTime);
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreSame(replacement, targeting.CurrentTarget,
            "旧目标死亡后的 Targeting 阶段应立即选中附近替代目标");
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State,
            "存在合法替代目标时不能进入不可打断的返航状态");
        Assert.IsTrue(soldier.CanRun(targeting), "切换目标时不能锁死 Targeting");
    }

    [Test]
    public void 敌兵当前目标死亡且没有替代目标时在Targeting确认后返航()
    {
        var soldier = MakeSoldier(new Vector3(5f, 0f, 0f), SideType.EnemySide);
        var deadTarget = MakeSoldier(new Vector3(5.5f, 0f, 0f));
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(deadTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)3f };
        targeting.Init(soldier);
        soldier.TargetComp = targeting;
        targeting.UpdateTargeting((Fix64)0.2f);
        Assert.AreSame(deadTarget, targeting.CurrentTarget);

        var brain = new SoldierAIBrain
        {
            ChaseRange = (Fix64)10,
            HomeArrivedRadius = (Fix64)1,
        };
        brain.SetBirthPositionFixed(FixVector2.Zero);
        soldier.Brain = brain;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        deadTarget.Alive = false;
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State,
            "Brain 阶段不能在 Targeting 检查替代目标前抢先返航");
        targeting.UpdateTargeting(LogicFrameRuntime.FixedDeltaTime);
        Assert.IsNull(targeting.CurrentTarget);

        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Returning, brain.State,
            "Targeting 确认没有替代目标后必须进入返航");
        Assert.IsFalse(soldier.CanRun(targeting), "正式返航后仍必须锁住 Targeting");
    }

    [Test]
    public void Follow状态_发现敌人后转为Combat()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(2, 0, 0));
        var enemy = MakeSoldier(new Vector3(3, 0, 0), SideType.EnemySide);

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var brain = new SoldierAIBrain();
        brain.DetectEnemyRange = (Fix64)6f;
        brain.Inject();
        soldier.Brain = brain;

        // 第一帧：Idle → Follow
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State);

        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { player, soldier, enemy });
        targeting.Init(soldier);
        targeting.CurrentTarget = enemy;
        soldier.TargetComp = targeting;

        // 第二帧：Follow → Combat（敌人在攻击范围内，状态测试不触发寻路世界解析）
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
    }

    [Test]
    public void Combat状态_敌人死后回到Follow()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(2, 0, 0));
        var enemy = MakeSoldier(new Vector3(3, 0, 0), SideType.EnemySide);

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var brain = new SoldierAIBrain();
        brain.DetectEnemyRange = (Fix64)6f;
        brain.Inject();
        soldier.Brain = brain;

        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { player, soldier, enemy });
        targeting.Init(soldier);
        targeting.CurrentTarget = enemy;
        soldier.TargetComp = targeting;

        // Idle → Follow → Combat
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);

        // 杀死敌人
        enemy.Alive = false;
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Idle, brain.State, "敌人失效当帧应先清掉 Combat 和旧目标");
        Assert.IsNull(targeting.CurrentTarget, "Brain 在 Targeting 阶段之前就必须清掉已失效目标，不能把退场实体带入寻路");
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "敌人死后回到 Follow");
    }

    [Test]
    public void Combat状态_内圈单位在武器射程内会直接攻击()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(1.2f, 0, 0));
        var enemy = MakeSoldier(new Vector3(2.1f, 0, 0), SideType.EnemySide);

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { player, soldier, enemy });
        targeting.Init(soldier);
        targeting.CurrentTarget = enemy;
        soldier.TargetComp = targeting;
        Assert.IsTrue(WeaponTargetRules.IsValidTargetForCurrentWeapon(soldier, enemy),
            $"测试前置失败 phase={(GamePhase)InGameDataModel.GetValue(IngameValueType.Phase)} " +
            $"targetable={enemy.IsAttackTargetable()} enemy={EntityCombatTeamHelper.IsEnemy(soldier, enemy)}");

        var brain = new SoldierAIBrain();
        brain.DetectEnemyRange = (Fix64)6f;
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);
        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
        Assert.IsTrue(brain.Attack, "已经在武器射程内时应直接进入攻击态，而不是继续等到站位点");
    }

    [Test]
    public void Combat状态_RuntimeDirty期间等待导航重建后再提交接近目标()
    {
        var soldier = MakeSoldier(new Vector3(-5f, 0f, 0f));
        var enemy = MakeSoldier(new Vector3(5f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var targeting = new SimTargetingComp(soldier, new List<IEntityContext> { soldier, enemy });
        targeting.Init(soldier);
        targeting.CurrentTarget = enemy;
        soldier.TargetComp = targeting;

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocityFixed(
            soldier,
            enemy.PositionFixed,
            Fix64.One,
            out _));
        FlowFieldCrowdMovementSystem.RegisterBoxObstacleFixed(
            9104,
            new FixVector2(Fix64.Zero, (Fix64)8),
            new FixVector2((Fix64)0.5f, (Fix64)0.5f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty());

        var brain = new SoldierAIBrain();
        soldier.Brain = brain;
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
        Assert.AreEqual(FixVector2.Zero, soldier.MoveComp.NavDirectionFixed,
            "runtime dirty 期间不得把不可走的敌人中心作为临时追击目标");

        for (int i = 0; i < 64 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(),
            "runtime dirty 导航重建应在测试预算内完成");
        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        brain.Tick(soldier, LogicFrameRuntime.FixedDeltaTime);

        Assert.AreNotEqual(FixVector2.Zero, soldier.MoveComp.NavDirectionFixed,
            "导航重建完成后下一次 Combat Tick 应提交合法接近目标");
    }

    #endregion

}
