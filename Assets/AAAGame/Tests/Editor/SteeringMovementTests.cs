using NUnit.Framework;
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
        SetupCombatPhaseForTests();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
    }

    private static void SetupCombatPhaseForTests()
    {
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
        AssertRaw(6144, soldier.WeaponRange, nameof(soldier.WeaponRange));
        AssertRaw(40960, soldier.DetectEnemyRange, nameof(soldier.DetectEnemyRange));
        AssertRaw(94208, soldier.ChaseRange, nameof(soldier.ChaseRange));
        AssertRaw(6144, soldier.HomeArrivedRadius, nameof(soldier.HomeArrivedRadius));
        AssertRaw(2048, soldier.ReturnSpeedBonusPercent, nameof(soldier.ReturnSpeedBonusPercent));
        AssertRaw(820, soldier.ReturnHpRegenPercentPerSec, nameof(soldier.ReturnHpRegenPercentPerSec));
        AssertRaw(2458, soldier.SoftReturnRatio, nameof(soldier.SoftReturnRatio));

        AssertStaticRaw<SoldierAIBrain>("CombatApproachRangeSlackFixed", 328);
        AssertStaticRaw<SoldierAIBrain>("CombatApproachRingSpacingFixed", 2253);
        AssertStaticRaw<SoldierAIBrain>("CombatApproachOccupancyPaddingFixed", 1434);
        AssertStaticRaw<SoldierAIBrain>("FallbackDeadZoneRange", 49152);
        AssertStaticRaw<SoldierAIBrain>("FallbackInnerDeadZoneRange", 8192);

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

    private SimEntityContext MakeSoldier(Vector3 pos, SideType side = SideType.PlayerSide)
    {
        var ctx = new SimEntityContext
        {
            Position = pos,
            Side = side,
            Alive = true
        };
        ctx.SetProperty(CreatureMainProperty.Speed, (Fix64)5f);

        var exec = new SimMoveExecutor();
        exec.Position = pos;
        ctx.MoveExecutor = exec;

        var moveComp = new SimMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        var atkComp = new SimAtkComp();
        atkComp.Init(ctx);
        ctx.AtkComp = atkComp;

        return ctx;
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
        brain.WeaponRange = (Fix64)1.5f;
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, Fix64.One / (Fix64)60);
        brain.Tick(soldier, Fix64.One / (Fix64)60);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
        Assert.IsTrue(brain.Attack, "已经在武器射程内时应直接进入攻击态，而不是继续等到站位点");
    }

    #endregion

}
