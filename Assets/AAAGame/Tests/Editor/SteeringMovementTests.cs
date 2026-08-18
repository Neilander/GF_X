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
        LogicFactionVisionService.UnbindMap();
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
        LogicFactionVisionService.UnbindMap();
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
        AssertRaw(1024000, soldier.ReturnSpeedBonus, nameof(soldier.ReturnSpeedBonus));
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
    public void Targeting_SelectsHigherTauntBeforeDistance()
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
            "Taunt level is compared before distance.");
    }

    [Test]
    public void Targeting_UsesLexicographicTauntPriorityWithoutThreatConfig()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var closeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var distantHigherTauntTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        closeTarget.TauntLevel = 1;
        distantHigherTauntTarget.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(closeTarget);
        EntityRegistry.Register(distantHigherTauntTarget);

        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(distantHigherTauntTarget, targeting.CurrentTarget,
            "Taunt is the first comparison tier and cannot be offset by distance.");
    }

    [Test]
    public void CurrentAttackTarget_SwitchesToHigherTauntInsideR()
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
            "A higher taunt level inside r must outrank the current in-range target.");
    }

    [Test]
    public void RemovedAdditionalPursuitDistance_DoesNotLimitCandidateInsideR()
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

        Assert.AreSame(distantTauntingTarget, targeting.CurrentTarget,
            "The removed x+z rule must not block a higher-taunt candidate inside the global minimum candidate range.");
    }

    [Test]
    public void CurrentAttackTarget_DoesNotSwitchToCloserSameTauntTarget()
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
            "The in-range current aggro target outranks a closer peer at the same preceding tiers.");
    }

    [Test]
    public void Attacking_CurrentTargetLeavesAttackRange_SwitchesToTargetInsideAttackRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        var inRangeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(inRangeTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(currentTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the current target outside attack range.");
        Assert.LessOrEqual(
            self.LogicFrameDistanceToTargetSurface(inRangeTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the replacement target inside attack range.");
        Assert.AreSame(inRangeTarget, targeting.CurrentTarget,
            "An attack-locked target outside attack range must yield to an attackable target already in range.");
    }

    [Test]
    public void Attacking_NonPositiveTargetLeavesAttackRangeWithoutCandidate_ClearsTarget()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        var otherOutOfRangeTarget = MakeSoldier(new Vector3(4f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(otherOutOfRangeTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "Outside attack range, the current target remains eligible and wins the distance tie-break over a farther peer.");
    }

    [Test]
    public void Pursuing_ContinuouslySwitchesToClosestTargetAtSamePriority()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var closerTarget = MakeSoldier(new Vector3(4f, 0f, 0f), SideType.EnemySide);
        var laterClosestTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        closerTarget.TauntLevel = 1;
        laterClosestTarget.TauntLevel = 1;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        SetAttacking(self, false);
        currentTarget.Position = new Vector3(5f, 0f, 0f);
        EntityRegistry.Register(closerTarget);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(closerTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the first replacement to remain in pursuit range rather than attack range.");
        Assert.AreSame(closerTarget, targeting.CurrentTarget,
            "Pursuit must switch to the closest target when all preceding priority tiers tie.");

        EntityRegistry.Register(laterClosestTarget);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(laterClosestTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the later replacement to remain outside attack range.");
        Assert.AreSame(laterClosestTarget, targeting.CurrentTarget,
            "Pursuit must keep re-evaluating the closest target instead of switching only once.");
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
    public void HigherTauntTarget_RemainsCandidateOutsideLegacyForgetRange()
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
            "A higher-taunt target inside r must remain a candidate on every scan.");
    }

    [Test]
    public void HigherTauntCurrentTarget_DoesNotSwitchToLowerTauntInsideAttackRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        var lowerPursuitThreatInRange = MakeSoldier(new Vector3(0.8f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 2;
        lowerPursuitThreatInRange.TauntLevel = 1;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(lowerPursuitThreatInRange);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(currentTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the higher-pursuit target outside attack range.");
        Assert.LessOrEqual(
            self.LogicFrameDistanceToTargetSurface(lowerPursuitThreatInRange),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the lower-pursuit target inside attack range.");
        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "Taunt is compared before attack-range membership, so the higher-taunt target remains selected.");
    }

    [Test]
    public void SameTauntPursuit_SwitchesToCloserCandidateWithinR()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var additionalPursuitTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.EnemySide);
        var closerPursuitPeer = MakeSoldier(new Vector3(9f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        additionalPursuitTarget.TauntLevel = 2;
        closerPursuitPeer.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(additionalPursuitTarget);

        var targeting = new CharacterTargetingComp
        {
            AggroRangeFixed = (Fix64)20,
            ForgetRangeFixed = (Fix64)8,
        };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(additionalPursuitTarget, targeting.CurrentTarget,
            "The higher-taunt target must first interrupt the current attack target.");

        SetAttacking(self, false);
        EntityRegistry.Register(closerPursuitPeer);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(closerPursuitPeer),
            (float)targeting.ForgetRangeFixed,
            "Test setup requires the closer peer outside the legacy forget range.");
        Assert.AreSame(closerPursuitPeer, targeting.CurrentTarget,
            "Pursuit must switch to the closer target when all earlier tiers tie.");

        targeting.UpdateTargeting(Fix64.One / (Fix64)60);

        Assert.AreSame(closerPursuitPeer, targeting.CurrentTarget,
            "The replacement remains eligible because it is inside r.");
    }

    [Test]
    public void CurrentAggroTarget_RemainsEligibleOutsideRButInsideB()
    {
        var self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        currentTarget.TauntLevel = 1;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        SetAttacking(self, false);
        currentTarget.Position = new Vector3(18f, 0f, 0f);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(currentTarget, targeting.CurrentTarget,
            "The current aggro target remains a candidate outside r until it leaves the outer range or becomes invalid.");
    }

    [Test]
    public void DefendEnemyTargeting_UsesSameLexicographicPriority()
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
            "Defend-wave units must use the same lexicographic taunt rule.");
    }

    [Test]
    public void DefendEnemyTargeting_CurrentTargetLeavesAttackRange_SwitchesToTargetInsideAttackRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.EnemySide);
        var currentTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.PlayerSide);
        var inRangeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.PlayerSide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);
        EntityRegistry.Register(inRangeTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.UseDefendEnemyMode(null);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(inRangeTarget, targeting.CurrentTarget,
            "Defend-wave attack lock must yield when its current target leaves attack range and another target is in range.");
    }

    [Test]
    public void DefendEnemyTargeting_PursuitSwitchesToCloserSamePriorityOutsideAttackRange()
    {
        var self = MakeSoldier(Vector3.zero, SideType.EnemySide);
        var currentTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.PlayerSide);
        var closerTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.PlayerSide);
        currentTarget.TauntLevel = 1;
        closerTarget.TauntLevel = 1;
        EntityRegistry.Register(self);
        EntityRegistry.Register(currentTarget);

        var targeting = new CharacterTargetingComp { AggroRangeFixed = (Fix64)20 };
        targeting.Init(self);
        targeting.UseDefendEnemyMode(null);
        targeting.CurrentTarget = currentTarget;
        SetAttacking(self, true);
        targeting.UpdateTargeting((Fix64)0.2f);

        SetAttacking(self, false);
        currentTarget.Position = new Vector3(5f, 0f, 0f);
        EntityRegistry.Register(closerTarget);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.Greater(
            self.LogicFrameDistanceToTargetSurface(closerTarget),
            (float)self.WeaponComp.AttackRange,
            "Test setup requires the replacement to remain outside attack range.");
        Assert.AreSame(closerTarget, targeting.CurrentTarget,
            "Defend-wave pursuit must continuously select the closest target when preceding tiers tie.");
    }

    [Test]
    public void BuildingTargeting_ChoosesHighestPriorityOnlyInsideAttackRange()
    {
        SimBuildingContext tower = MakeBuilding(Vector3.zero, SideType.PlayerSide);
        tower.WeaponComp = CreateTestWeaponComp((Fix64)1.5f);
        var inRangeTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        var outOfRangeTauntingTarget = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.EnemySide);
        inRangeTarget.TauntLevel = 1;
        outOfRangeTauntingTarget.TauntLevel = 10;
        EntityRegistry.Register(tower);
        EntityRegistry.Register(inRangeTarget);
        EntityRegistry.Register(outOfRangeTauntingTarget);

        var targeting = new BuildingTargetingComp();
        targeting.Init(tower);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(inRangeTarget, targeting.CurrentTarget,
            "Buildings must only select candidates inside attack range and never pursue.");
    }

    [Test]
    public void BuildingTargeting_IgnoresAlertInsideAttackRange()
    {
        SimBuildingContext tower = MakeBuilding(Vector3.zero, SideType.PlayerSide);
        tower.WeaponComp = CreateTestWeaponComp((Fix64)5);
        SimEntityContext closerTarget = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        SimEntityContext alertTarget = MakeSoldier(new Vector3(3f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(tower);
        EntityRegistry.Register(closerTarget);
        EntityRegistry.Register(alertTarget);

        var targeting = new BuildingTargetingComp();
        targeting.Init(tower);
        targeting.NotifyAllyFoundEnemy(alertTarget);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(closerTarget, targeting.CurrentTarget,
            "Buildings have no alert aggro; distance remains the tie-break inside attack range.");
    }

    [Test]
    public void FactionVision_IsComputedIndependentlyForEachSide()
    {
        SimHeroContext playerHero = MakeHero(Vector3.zero, SideType.PlayerSide);
        SimEntityContext enemy = MakeSoldier(new Vector3(55f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(playerHero);
        EntityRegistry.Register(enemy);

        Assert.IsTrue(LogicFactionVisionService.IsEntityVisibleToSide(SideType.PlayerSide, enemy));
        Assert.IsFalse(LogicFactionVisionService.IsEntityVisibleToSide(SideType.EnemySide, playerHero));
    }

    [Test]
    public void HeroAggro_UsesAttackRangeOnlyAndClearsPursuitWhenTargetLeavesRange()
    {
        SimHeroContext hero = MakeHero(Vector3.zero, SideType.PlayerSide);
        SimEntityContext enemy = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        SimEntityContext alertedEnemy = MakeSoldier(new Vector3(1.4f, 0f, 0f), SideType.EnemySide);
        var targeting = new HeroTargetingComp();
        targeting.Init(hero);
        hero.TargetComp = targeting;
        EntityRegistry.Register(hero);
        EntityRegistry.Register(enemy);
        EntityRegistry.Register(alertedEnemy);

        targeting.NotifyAllyFoundEnemy(alertedEnemy);
        targeting.UpdateTargeting((Fix64)0.2f);
        hero.TickOutOfCombatState(0f);

        Assert.AreSame(enemy, targeting.AggroTarget);
        Assert.IsFalse(hero.IsOutOfCombat);

        enemy.Position = new Vector3(10f, 0f, 0f);
        alertedEnemy.Position = new Vector3(10f, 0f, 0f);
        targeting.NotifyAllyFoundEnemy(enemy);
        targeting.UpdateTargeting((Fix64)0.2f);
        hero.TickOutOfCombatState(0f);

        Assert.IsNull(targeting.CurrentTarget);
        Assert.IsNull(targeting.AggroTarget,
            "Hero aggro must not retain, alert-pursue, or last-seen-pursue an attackable enemy outside attack range.");
        Assert.IsTrue(hero.IsOutOfCombat);
    }

    [Test]
    public void CharacterTargetingFactory_SelectsDedicatedUnitAndHeroComponents()
    {
        SimEntityContext unit = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimHeroContext hero = MakeHero(Vector3.zero, SideType.PlayerSide);
        CharacterTargetingFactory factory = ScriptableObject.CreateInstance<CharacterTargetingFactory>();
        try
        {
            ITargetingComp unitTargeting = factory.CreateTargetingComp(unit);
            ITargetingComp heroTargeting = factory.CreateTargetingComp(hero);

            Assert.IsInstanceOf<CharacterTargetingComp>(unitTargeting);
            Assert.IsInstanceOf<HeroTargetingComp>(heroTargeting);
            Assert.AreSame(unitTargeting, unit.TargetComp);
            Assert.AreSame(heroTargeting, hero.TargetComp);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(factory);
        }
    }

    [Test]
    public void DamageAlert_RevealsFixedSourceAreaAndAllowsAlertCandidateBeyondR()
    {
        ConfigureOpenNavigationGrid(160, 16, new Vector3(-80f, 0f, -8f));
        SimEntityContext victim = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimEntityContext ally = MakeSoldier(new Vector3(10f, 0f, 0f), SideType.PlayerSide);
        SimEntityContext attacker = MakeSoldier(new Vector3(50f, 0f, 0f), SideType.EnemySide);
        var victimTargeting = new CharacterTargetingComp();
        victimTargeting.Init(victim);
        victim.TargetComp = victimTargeting;
        var allyTargeting = new CharacterTargetingComp();
        allyTargeting.Init(ally);
        ally.TargetComp = allyTargeting;
        EntityRegistry.Register(victim);
        EntityRegistry.Register(ally);
        EntityRegistry.Register(attacker);

        LogicFactionVisionService.HandleSuccessfulDamage(victim, attacker);
        allyTargeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(attacker, allyTargeting.CurrentTarget,
            "The alert target is outside r but inside b and must be eligible while the fixed reveal sees it.");

        attacker.Position = new Vector3(70f, 0f, 0f);
        allyTargeting.UpdateTargeting((Fix64)0.2f);

        Assert.IsNull(allyTargeting.CurrentTarget,
            "The temporary reveal is fixed at the damage source position and must not follow the attacker.");
        Assert.IsTrue(allyTargeting.HasLastSeenPursuit);
    }

    [Test]
    public void DamageAlert_StationaryRevealExpiresAfterConfiguredDuration()
    {
        SimEntityContext observer = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimEntityContext enemy = MakeSoldier(new Vector3(50f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(observer);
        EntityRegistry.Register(enemy);

        LogicFactionVisionService.AddDamageReveal(SideType.PlayerSide, enemy.PositionFixed);
        Assert.IsTrue(LogicFactionVisionService.IsEntityVisibleToSide(SideType.PlayerSide, enemy));

        LogicFactionVisionService.Advance((Fix64)2.1f);

        Assert.IsFalse(LogicFactionVisionService.IsEntityVisibleToSide(SideType.PlayerSide, enemy));
    }

    [Test]
    public void InvisibleCurrentTarget_UsesLastSeenPositionAndCanBeReacquired()
    {
        ConfigureOpenNavigationGrid(240, 16, new Vector3(-120f, 0f, -8f));
        SimEntityContext self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimEntityContext enemy = MakeSoldier(new Vector3(40f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(enemy);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        self.TargetComp = targeting;
        targeting.CurrentTarget = enemy;
        targeting.UpdateTargeting((Fix64)0.2f);

        enemy.Position = new Vector3(100f, 0f, 0f);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.IsNull(targeting.CurrentTarget);
        Assert.IsTrue(targeting.HasLastSeenPursuit);
        Assert.AreSame(enemy, targeting.AggroTarget,
            "The last-seen target remains the aggro target while no visible attack target exists.");
        Assert.AreEqual(new FixVector2((Fix64)40, Fix64.Zero), targeting.LastSeenPositionFixed);

        enemy.Position = new Vector3(40f, 0f, 0f);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(enemy, targeting.CurrentTarget);
        Assert.AreSame(enemy, targeting.AggroTarget);
        Assert.IsFalse(targeting.HasLastSeenPursuit);
    }

    [Test]
    public void InvisibleTarget_RevealedOutsideOuterRange_ClearsLastSeenPursuit()
    {
        ConfigureOpenNavigationGrid(240, 16, new Vector3(-120f, 0f, -8f));
        SimEntityContext self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimEntityContext enemy = MakeSoldier(new Vector3(40f, 0f, 0f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(enemy);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        self.TargetComp = targeting;
        targeting.CurrentTarget = enemy;
        targeting.UpdateTargeting((Fix64)0.2f);

        enemy.Position = new Vector3(100f, 0f, 0f);
        targeting.UpdateTargeting((Fix64)0.01f);
        Assert.IsTrue(targeting.HasLastSeenPursuit);

        SimEntityContext revealer = MakeSoldier(new Vector3(100f, 0f, 0f), SideType.PlayerSide);
        EntityRegistry.Register(revealer);
        targeting.UpdateTargeting((Fix64)0.01f);

        Assert.IsFalse(targeting.HasLastSeenPursuit,
            "A target revealed beyond b is no longer a valid pursuit target and must not leave a stale last-seen pursuit.");
        Assert.IsNull(targeting.CurrentTarget);
    }

    [Test]
    public void InvincibleEnemy_IsNeverSelectedAsAggroTarget()
    {
        SimEntityContext self = MakeSoldier(Vector3.zero, SideType.PlayerSide);
        SimEntityContext invincibleEnemy = MakeSoldier(new Vector3(1f, 0f, 0f), SideType.EnemySide);
        SimEntityContext validEnemy = MakeSoldier(new Vector3(2f, 0f, 0f), SideType.EnemySide);
        invincibleEnemy.BuffComp.AddBuff(
            BuffData.Create(
                InvincibleStateBuff.BuffId,
                Fix64.Zero,
                true,
                1,
                new List<BuffCallback> { new InvincibleStateBuff() }),
            invincibleEnemy);
        EntityRegistry.Register(self);
        EntityRegistry.Register(invincibleEnemy);
        EntityRegistry.Register(validEnemy);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(validEnemy, targeting.CurrentTarget);
    }

    [Test]
    public void AggroCandidate_WhoseAttackAreaIsUnreachable_IsExcludedBeforeRanking()
    {
        ConfigureSplitNavigationGrid(12, 7, 6);
        SimEntityContext self = MakeSoldier(new Vector3(2.5f, 0f, 3.5f), SideType.PlayerSide);
        SimEntityContext reachableTarget = MakeSoldier(new Vector3(4.5f, 0f, 3.5f), SideType.EnemySide);
        SimEntityContext unreachableHigherTaunt = MakeSoldier(new Vector3(9.5f, 0f, 3.5f), SideType.EnemySide);
        reachableTarget.TauntLevel = 1;
        unreachableHigherTaunt.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(reachableTarget);
        EntityRegistry.Register(unreachableHigherTaunt);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);

        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(reachableTarget, targeting.CurrentTarget,
            "A higher-ranked target must not enter aggro ranking when its entire attack area is on another navigation island.");
    }

    [Test]
    public void AggroCandidate_UnreachableCenterWithReachableAttackArea_RemainsSelectable()
    {
        ConfigureSplitNavigationGrid(12, 7, 6);
        SimEntityContext self = MakeSoldier(new Vector3(2.5f, 0f, 3.5f), SideType.PlayerSide);
        self.WeaponComp = CreateTestWeaponComp((Fix64)2);
        SimEntityContext reachableLowerTaunt = MakeSoldier(new Vector3(4.5f, 0f, 3.5f), SideType.EnemySide);
        SimEntityContext centerAcrossWall = MakeSoldier(new Vector3(7.5f, 0f, 3.5f), SideType.EnemySide);
        reachableLowerTaunt.TauntLevel = 1;
        centerAcrossWall.TauntLevel = 2;
        EntityRegistry.Register(self);
        EntityRegistry.Register(reachableLowerTaunt);
        EntityRegistry.Register(centerAcrossWall);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);

        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.AreSame(centerAcrossWall, targeting.CurrentTarget,
            "The target center may be unreachable when a reachable point still exists inside the attack area.");
    }

    [Test]
    public void LastSeenPursuit_UsesClosestReachablePointInsideAttackRange()
    {
        ConfigureSplitNavigationGrid(60, 7, 50);
        SimEntityContext self = MakeSoldier(new Vector3(2.5f, 0f, 3.5f), SideType.PlayerSide);
        self.WeaponComp = CreateTestWeaponComp((Fix64)4);
        SimEntityContext target = MakeSoldier(new Vector3(52.5f, 0f, 3.5f), SideType.EnemySide);
        EntityRegistry.Register(self);
        EntityRegistry.Register(target);
        var targeting = new CharacterTargetingComp();
        targeting.Init(self);
        self.TargetComp = targeting;
        LogicFactionVisionService.AddDamageReveal(SideType.PlayerSide, target.PositionFixed);
        targeting.NotifyAllyFoundEnemy(target);
        targeting.UpdateTargeting((Fix64)0.2f);
        Assert.AreSame(target, targeting.CurrentTarget, "The fixed reveal must establish the initial sighting.");

        LogicFactionVisionService.Advance((Fix64)2.1f);
        targeting.UpdateTargeting((Fix64)0.2f);

        Assert.IsNull(targeting.CurrentTarget);
        Assert.IsTrue(targeting.HasLastSeenPursuit);
        Assert.AreEqual(target.PositionFixed, targeting.LastSeenPositionFixed,
            "The observed position remains the source of truth and must not be replaced by the navigation destination.");
        Assert.IsTrue(targeting.LastSeenPursuitDestinationFixed.x < (Fix64)50,
            "The pursuit destination must remain on the pursuer's navigation island.");
        Assert.IsTrue(
            FixVector2.Distance(targeting.LastSeenPursuitDestinationFixed, targeting.LastSeenPositionFixed)
            <= self.WeaponComp.AttackRange,
            "The reachable pursuit destination must be inside the last-seen attack area.");
    }

    [Test]
    public void DefendReturnConfig_ConvertsDistanceAndKeepsFixedSpeedBonus()
    {
        var brain = new SoldierAIBrain();

        brain.ConfigureReturnFromGameConfig();

        Assert.AreEqual((Fix64)90, brain.ChaseRange);
        Assert.AreEqual((Fix64)250, brain.ReturnSpeedBonus);
        Assert.AreEqual((Fix64)20 / (Fix64)100, brain.ReturnHpRegenPercentPerSec);
        Assert.AreEqual(Fix64.Zero, brain.ReturnDamageReductionPercent);
    }

    [Test]
    public void TargetingPerformance_120Units_ReportScanCostAndAllocations()
    {
        const int unitCount = 120;
        const int warmupPasses = 3;
        const int measuredPasses = 10;
        var targetings = new List<CharacterTargetingComp>(unitCount);
        for (int i = 0; i < unitCount; i++)
        {
            int column = i % 15;
            int row = i / 15;
            SideType side = (i & 1) == 0 ? SideType.PlayerSide : SideType.EnemySide;
            SimEntityContext entity = MakeSoldier(
                new Vector3(-10.5f + column * 1.5f, 0f, -5.25f + row * 1.5f),
                side);
            var targeting = new CharacterTargetingComp();
            targeting.Init(entity);
            entity.TargetComp = targeting;
            EntityRegistry.Register(entity);
            targetings.Add(targeting);
        }

        for (int pass = 0; pass < warmupPasses; pass++)
            RunTargetingPhase(targetings);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int pass = 0; pass < measuredPasses; pass++)
            RunTargetingPhase(targetings);
        stopwatch.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        for (int i = 0; i < targetings.Count; i++)
        {
            IEntityContext owner = EntityRegistry.AllEntities[i];
            Assert.NotNull(targetings[i].CurrentTarget, $"Unit {i} did not acquire an enemy.");
            Assert.IsTrue(EntityCombatTeamHelper.IsEnemy(owner, targetings[i].CurrentTarget));
        }
        double scanCount = unitCount * measuredPasses;
        TestContext.Out.WriteLine(
            $"Aggro benchmark: units={unitCount}, passes={measuredPasses}, totalMs={stopwatch.Elapsed.TotalMilliseconds:F3}, " +
            $"msPerFullPass={stopwatch.Elapsed.TotalMilliseconds / measuredPasses:F3}, " +
            $"usPerUnitScan={stopwatch.Elapsed.TotalMilliseconds * 1000.0 / scanCount:F3}, " +
            $"allocatedBytes={allocatedBytes}, bytesPerUnitScan={allocatedBytes / scanCount:F3}");
    }

    private static void RunTargetingPhase(IReadOnlyList<CharacterTargetingComp> targetings)
    {
        LogicFactionVisionService.BeginTargetingPhase();
        try
        {
            for (int i = 0; i < targetings.Count; i++)
                targetings[i].UpdateTargeting((Fix64)0.2f);
        }
        finally
        {
            LogicFactionVisionService.EndTargetingPhase();
        }
    }

    private static void ConfigureSplitNavigationGrid(int width, int height, int wallX)
    {
        if (width <= 2 || height <= 2 || wallX <= 0 || wallX >= width - 1)
            throw new ArgumentOutOfRangeException(nameof(wallX));
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                walkable[y * width + x] = x != wallX;
        }
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
            width,
            height,
            1f,
            Vector3.zero,
            walkable);
        FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        for (int i = 0;
             i < 2048 && (!FlowFieldCrowdMovementSystem.HasEditorTestWorld()
                          || FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
             i++)
        {
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        }
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld());
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
    }

    private static void ConfigureOpenNavigationGrid(int width, int height, Vector3 origin)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, origin, walkable);
        FlowFieldCrowdMovementSystem.PrepareRuntimeDependencies();
        for (int i = 0;
             i < 2048 && (!FlowFieldCrowdMovementSystem.HasEditorTestWorld()
                          || FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
             i++)
        {
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        }
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld());
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild());
    }

    private SimHeroContext MakeHero(Vector3 position, SideType side)
    {
        var hero = new SimHeroContext { Position = position, Side = side, Alive = true };
        hero.SetProperty(CreatureMainProperty.Speed, (Fix64)5);
        hero.WeaponComp = CreateTestWeaponComp((Fix64)1.5f);
        hero.MoveExecutor = new SimMoveExecutor { Position = position };
        var move = new SimMoveComp();
        move.Init(hero);
        hero.MoveComp = move;
        var attack = new SimAtkComp();
        attack.Init(hero);
        hero.AtkComp = attack;
        var targeting = new NoTargetingComp();
        targeting.Init(hero);
        hero.TargetComp = targeting;
        var buffs = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        buffs.Init(hero);
        hero.BuffComp = buffs;
        return hero;
    }

    private SimBuildingContext MakeBuilding(Vector3 position, SideType side)
    {
        var building = new SimBuildingContext { Position = position, Side = side, Alive = true };
        building.SetProperty(CreatureMainProperty.Speed, (Fix64)5);
        building.WeaponComp = CreateTestWeaponComp((Fix64)1.5f);
        building.MoveExecutor = new SimMoveExecutor { Position = position };
        var move = new SimMoveComp();
        move.Init(building);
        building.MoveComp = move;
        var attack = new SimAtkComp();
        attack.Init(building);
        building.AtkComp = attack;
        var targeting = new NoTargetingComp();
        targeting.Init(building);
        building.TargetComp = targeting;
        var buffs = new AAAGame.Scripts.BuffSystem.CharacterBuffComp();
        buffs.Init(building);
        building.BuffComp = buffs;
        return building;
    }

    private sealed class SimHeroContext : SimEntityContext, IHeroLogicContext
    {
        public bool IsHeroEntity => true;
        public bool IsGhostState { get; private set; }
        public void SetGhostStateByBuff(bool enabled) => IsGhostState = enabled;
        public void RestoreFromGhostState() => IsGhostState = false;
    }

    private sealed class SimBuildingContext : SimEntityContext, IBuildingLogicContext
    {
        public BuildingData BuildingData => null;
        public BuildingExtraProps ProductionProps => null;
        public string BuildingInstanceId => "test-building";
        public string StrongholdId => "test-stronghold";
        public int OwnerFactionId { get; private set; } = EntitySideHelper.PlayerFactionId;
        public int GetArmyForce() => 0;
        public int GetArmyForceWithoutRuntimeRules() => 0;
        public int GetArmySupplyPerUnit() => 0;
        public int GetArmyOccupiedSupply() => 0;
        public void SetArmyForceBase(int value) { }
        public void SetArmySupplyPerUnitBase(int value) { }
        public void ModifyArmyForce(IPropertyModifier modifier, bool ifAdd = true) { }
        public void ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd = true) { }
        public IReadOnlyList<LogicInteractionOptionDescriptor> InteractionOptions => Array.Empty<LogicInteractionOptionDescriptor>();
        public bool IsDisabled => false;
        public bool IsPhaseProtected => false;
        public bool IsPermanentlyInvincible { get; private set; }
        public bool IsStealthed { get; private set; }
        public bool HasPermanentNoAttackCapability => false;
        public bool BlocksLogicMovement { get; private set; }
        public IReadOnlyList<LogicCombatShape> LogicObstacleShapes => Array.Empty<LogicCombatShape>();
        public bool IsGameEndConditionBuilding { get; private set; }
        public bool IsNavigationStaticBaked => false;
        public event Action<int, int> OwnerFactionChanged;

        public void SetOwnerFaction(int ownerFactionId)
        {
            int previous = OwnerFactionId;
            OwnerFactionId = ownerFactionId;
            OwnerFactionChanged?.Invoke(previous, ownerFactionId);
        }

        public void SetGameEndConditionBuilding(bool enabled) => IsGameEndConditionBuilding = enabled;
        public void RestoreBuildingToFullHealth() { }
        public void SetCollisionBlockingByBuff(bool blocksMovement) => BlocksLogicMovement = blocksMovement;
        public void SetPermanentStealthByBuff(bool enabled) => IsStealthed = enabled;
        public void SetPermanentInvincibilityByBuff(bool enabled) => IsPermanentlyInvincible = enabled;
        public void SetPhaseProtectionByBuff(bool enabled) { }
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
        var soldier = MakeSoldier(new Vector3(10, 0, 0));
        var enemy = MakeSoldier(new Vector3(14, 0, 0), SideType.EnemySide);

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
        Assert.AreNotEqual(FixVector2.Zero, ((SimMoveComp)soldier.MoveComp).NavDirectionFixed,
            "我方单位应追击攻击范围外的仇恨目标");

        // 杀死敌人
        enemy.Alive = false;
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State,
            "Brain 应先等待紧随其后的 Targeting 阶段尝试替换失效目标");
        targeting.UpdateTargeting((Fix64)0.2f);
        Assert.IsNull(targeting.CurrentTarget);
        brain.Tick(soldier, Fix64.One / (Fix64)60);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "确认脱战后应立即重新跟随英雄");
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
