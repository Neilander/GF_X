using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

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
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
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
        brain.RecruitRadius = 8f;
        brain.Inject();
        soldier.Brain = brain;

        Assert.AreEqual(SoldierAIBrain.SoldierState.Idle, brain.State);

        brain.Tick(soldier, 1f / 60f);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "玩家在 8 范围内应转为 Follow");
    }

    [Test]
    public void 小兵Idle状态_玩家太远保持Idle()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(20, 0, 0));

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);

        var brain = new SoldierAIBrain();
        brain.RecruitRadius = 8f;
        brain.Inject();
        soldier.Brain = brain;

        brain.Tick(soldier, 1f / 60f);

        Assert.AreEqual(SoldierAIBrain.SoldierState.Idle, brain.State);
    }

    [Test]
    public void Follow状态_发现敌人后转为Combat()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(2, 0, 0));
        var enemy = MakeSoldier(new Vector3(5, 0, 0), SideType.EnemySide);

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var brain = new SoldierAIBrain();
        brain.RecruitRadius = 8f;
        brain.DetectEnemyRange = 6f;
        brain.Inject();
        soldier.Brain = brain;

        // 第一帧：Idle → Follow
        brain.Tick(soldier, 1f / 60f);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State);

        // 第二帧：Follow → Combat（敌人在 3 米内 < DetectEnemyRange 6）
        brain.Tick(soldier, 1f / 60f);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);
    }

    [Test]
    public void Combat状态_敌人死后回到Follow()
    {
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var soldier = MakeSoldier(new Vector3(2, 0, 0));
        var enemy = MakeSoldier(new Vector3(4, 0, 0), SideType.EnemySide);

        EntityRegistry.RegisterAsPlayer(player);
        EntityRegistry.Register(soldier);
        EntityRegistry.Register(enemy);

        var brain = new SoldierAIBrain();
        brain.RecruitRadius = 8f;
        brain.DetectEnemyRange = 6f;
        brain.Inject();
        soldier.Brain = brain;

        // Idle → Follow → Combat
        brain.Tick(soldier, 1f / 60f);
        brain.Tick(soldier, 1f / 60f);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Combat, brain.State);

        // 杀死敌人
        enemy.Alive = false;
        brain.Tick(soldier, 1f / 60f);
        Assert.AreEqual(SoldierAIBrain.SoldierState.Follow, brain.State, "敌人死后回到 Follow");
    }

    #endregion

    #region 流体移动核心场景

    /// <summary>
    /// 模拟多帧 tick：brain → moveComp → executor → sync position
    /// </summary>
    private void SimulateTicks(List<SimEntityContext> entities, List<SoldierAIBrain> brains, int frames, float dt = 1f / 60f)
    {
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var ctx = entities[i];
                if (!ctx.Alive) continue;

                ctx.SyncPositionToExecutor();

                if (brains[i] != null)
                    brains[i].Tick(ctx, dt);

                if (ctx.MoveComp != null)
                    ctx.MoveComp.Move(dt);

                ctx.MoveExecutor.Execute(dt);
                ctx.SyncPositionFromExecutor();
            }
        }
    }

    [Test]
    public void 三个小兵过窄道_不会永远卡住()
    {
        // 场景：三个小兵在 x=0 位置，目标（玩家）在 x=10
        // 小兵间距很近，separation 应让他们自动散开通过
        var player = MakeSoldier(new Vector3(10, 0, 0));
        player.Brain = new ScriptedBrain(); // 玩家不动
        EntityRegistry.RegisterAsPlayer(player);

        var soldiers = new List<SimEntityContext>();
        var brains = new List<SoldierAIBrain>();

        for (int i = 0; i < 3; i++)
        {
            var s = MakeSoldier(new Vector3(0, 0, i * 0.3f - 0.3f)); // 密集排列
            var brain = new SoldierAIBrain();
            brain.RecruitRadius = 15f;
            brain.SeparationRadius = 1.2f;
            brain.SeparationWeight = 1.5f;
            brain.Inject();
            s.Brain = brain;
            EntityRegistry.Register(s);
            soldiers.Add(s);
            brains.Add(brain);
        }

        // 用于 SimulateTicks 的平坦列表
        var allCtx = new List<SimEntityContext> { player };
        allCtx.AddRange(soldiers);
        var allBrains = new List<SoldierAIBrain> { null }; // player 没有 SoldierAIBrain
        allBrains.AddRange(brains);

        SimulateTicks(allCtx, allBrains, 600); // 10秒

        // 所有小兵应该朝玩家移动了（至少走了一半）
        foreach (var s in soldiers)
        {
            Assert.Greater(s.Position.x, 3f,
                $"小兵应朝玩家移动，当前位置 {s.Position}");
        }
    }

    [Test]
    public void 小兵不阻拦玩家_玩家朝小兵走时小兵让开()
    {
        // 小兵在玩家正前方
        var player = MakeSoldier(new Vector3(0, 0, 0));
        var scriptedPlayerBrain = new ScriptedBrain();
        player.Brain = scriptedPlayerBrain;
        EntityRegistry.RegisterAsPlayer(player);

        var soldier = MakeSoldier(new Vector3(1.5f, 0, 0));
        var brain = new SoldierAIBrain();
        brain.RecruitRadius = 8f;
        brain.AvoidPlayerRadius = 1.8f;
        brain.AvoidPlayerStrength = 3f;
        brain.Inject();
        soldier.Brain = brain;
        EntityRegistry.Register(soldier);

        // 玩家朝右移动（朝小兵方向）
        scriptedPlayerBrain.Move = new Vector2(1f, 0f);

        var allCtx = new List<SimEntityContext> { player, soldier };
        var allBrains = new List<SoldierAIBrain> { null, brain };

        SimulateTicks(allCtx, allBrains, 120); // 2秒

        // 小兵应该不在玩家正前方了（Z方向偏移或X方向更远）
        float distBetween = Vector3.Distance(player.Position, soldier.Position);
        Assert.Greater(distBetween, 0.8f,
            $"小兵应让开玩家，距离 {distBetween}，玩家 {player.Position}，小兵 {soldier.Position}");
    }

    [Test]
    public void 五个小兵遇敌_散开而非堆叠()
    {
        var player = MakeSoldier(new Vector3(-5, 0, 0));
        player.Brain = new ScriptedBrain();
        EntityRegistry.RegisterAsPlayer(player);

        var enemy = MakeSoldier(new Vector3(5, 0, 0), SideType.EnemySide);
        enemy.Brain = new ScriptedBrain();
        EntityRegistry.Register(enemy);

        var soldiers = new List<SimEntityContext>();
        var brains = new List<SoldierAIBrain>();

        // 5个小兵初始位置几乎重叠
        for (int i = 0; i < 5; i++)
        {
            var s = MakeSoldier(new Vector3(0, 0, 0));
            var brain = new SoldierAIBrain();
            brain.RecruitRadius = 20f;
            brain.DetectEnemyRange = 10f;
            brain.SeparationRadius = 1.2f;
            brain.SeparationWeight = 1.5f;
            brain.Inject();
            s.Brain = brain;
            EntityRegistry.Register(s);
            soldiers.Add(s);
            brains.Add(brain);
        }

        var allCtx = new List<SimEntityContext> { player, enemy };
        allCtx.AddRange(soldiers);
        var allBrains = new List<SoldierAIBrain> { null, null };
        allBrains.AddRange(brains);

        SimulateTicks(allCtx, allBrains, 300); // 5秒

        // 检查：没有任何两个小兵距离 < 0.5（应该散开了）
        for (int i = 0; i < soldiers.Count; i++)
        {
            for (int j = i + 1; j < soldiers.Count; j++)
            {
                float dist = Vector3.Distance(soldiers[i].Position, soldiers[j].Position);
                Assert.Greater(dist, 0.3f,
                    $"小兵 {i} 和 {j} 堆叠了，距离 {dist:F2}");
            }
        }
    }

    #endregion
}
