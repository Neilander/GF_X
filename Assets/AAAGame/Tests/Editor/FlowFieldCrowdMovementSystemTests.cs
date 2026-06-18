using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
public class FlowFieldCrowdMovementSystemTests
{
    [SetUp]
    public void SetUp()
    {
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
    }

    [TearDown]
    public void TearDown()
    {
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
    }

    [Test]
    public void 位移期间不清空导航目标_结束后恢复寻路移动()
    {
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        DurationMoveEffectComp effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        moveComp.MoveTo(new Vector3(6.5f, 0f, 0.5f));

        bool[] walkable = new bool[7];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(7, 1, 1f, Vector3.zero, walkable);

        effectComp.StartDurationAdditionalMove(0.1f, Vector3.zero);
        effectComp.ApplyEffect(0.1f);
        Assert.AreEqual(MovementMode.Displaced, ctx.MoveExecutor.MovementMode, "位移生效期间应切入 Displaced");
        moveComp.Move(0.1f);
        ctx.MoveExecutor.Execute(0.1f);
        ctx.SyncPositionFromExecutor();

        Assert.AreEqual(0.5f, ctx.Position.x, 0.001f, "位移期间不应执行主动寻路位移");

        effectComp.ApplyEffect(0.1f);
        Assert.AreEqual(MovementMode.Normal, ctx.MoveExecutor.MovementMode, "位移结束后应恢复 Normal");
        moveComp.Move(0.1f);
        ctx.MoveExecutor.Execute(0.1f);
        ctx.SyncPositionFromExecutor();

        Assert.Greater(ctx.Position.x, 0.5f, "位移结束后应沿原目标继续移动");
    }

    [Test]
    public void 非瓶颈普通寻路会输出正常速度()
    {
        const int width = 4;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(3.5f, 0f, 0.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 1.5f, $"普通寻路应保持正常前进速度，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.15f, $"普通寻路不应出现异常侧向蠕动，velocity={velocity}");
    }

    [Test]
    public void 没有导航世界时会严格报错而不是Fallback()
    {
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        SimEntityContext left = CreateEntity(new Vector3(0f, 0f, 0f));
        Vector3 goal = new Vector3(6f, 0f, 0f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[TestUnit\] Flow strict fail: world unavailable"));
        var ex = Assert.Throws<System.InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, goal, 2f, out _));
        StringAssert.Contains("Flow strict fail: world unavailable", ex.Message);
    }

    [Test]
    public void 起点和目标不在同一Island时会解析到最近可达目标()
    {
        const int width = 5;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        SetWalkable(walkable, width, 0, 1);
        SetWalkable(walkable, width, 1, 1);
        SetWalkable(walkable, width, 3, 1);
        SetWalkable(walkable, width, 4, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(4.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FlowGoalResolvedToReachable\]"));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"不可达目标应解析到当前 island 上最近可达点并继续前进，velocity={velocity}");
    }

    [Test]
    public void 细窄可走格不会被中心点误判为不可走()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
            walkable[2 + y * width] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(2.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.AreEqual(Vector3.zero, velocity);
    }

    [Test]
    public void PortalGraph会避开同Sector内不可达出口()
    {
        const int width = 12;
        const int height = 4;
        bool[] walkable = new bool[width * height];

        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 3);

        for (int x = 4; x <= 7; x++)
        {
            SetWalkable(walkable, width, x, 0);
            SetWalkable(walkable, width, x, 3);
        }

        for (int x = 8; x <= 11; x++)
        {
            SetWalkable(walkable, width, x, 0);
            SetWalkable(walkable, width, x, 3);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 3.5f);

        SimulateAgent(ctx, goal, 36, 0.2f, 2.4f);

        Assert.Greater(ctx.Position.x, 8.5f, $"应选择上方可达 portal 抵达目标附近，当前位置={ctx.Position}");
        Assert.AreEqual(3.5f, ctx.Position.z, 0.75f, $"不应被错误出口引到下方死路，当前位置={ctx.Position}");
    }

    [Test]
    public void 起点目标同Sector但局部不通时会经Portal绕路()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        SetWalkable(walkable, width, 0, 1);
        SetWalkable(walkable, width, 3, 1);
        SetWalkable(walkable, width, 0, 4);
        SetWalkable(walkable, width, 1, 4);
        SetWalkable(walkable, width, 2, 4);
        SetWalkable(walkable, width, 3, 4);
        SetWalkable(walkable, width, 0, 3);
        SetWalkable(walkable, width, 3, 3);
        SetWalkable(walkable, width, 0, 2);
        SetWalkable(walkable, width, 3, 2);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(3.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.z, 0.5f, $"同 sector 内部不可达时应先经 portal 绕路，而不是报错或直冲墙，velocity={velocity}");
    }

    [Test]
    public void 跨Sector寻路在PortalTile内不会直冲最终目标而是沿可走走廊前进()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Less(velocity.z, -0.8f, $"在 portal 前应先沿竖向走廊朝拐角推进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.x, 0.35f, $"不应在 portal tile 内直接斜切向最终目标，velocity={velocity}");
    }

    [Test]
    public void 单位位于当前Sector的Portal边界格时仍会继续穿过Portal()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(3.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.8f, $"位于 portal 边界格时仍应继续向下个 sector 前进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"直走 portal 时不应产生异常侧偏，velocity={velocity}");

        ctx.Position = new Vector3(4.5f, 0f, 1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.4f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out velocity));

        Assert.Greater(velocity.x, 0.8f, $"进入 portal 对侧后一帧也应继续前进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"穿过 portal 后仍不应异常侧偏，velocity={velocity}");
    }

    [Test]
    public void 多Sector路径站在PortalGoalCell时不会把Portal当终点停住()
    {
        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(3.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.8f, $"站在 portal goal cell 上时应跨过 portal 继续前往后续 sector，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.1f, $"直线 portal handoff 不应产生异常侧偏，velocity={velocity}");
    }

    [Test]
    public void FlowField跨格转向会保留上一格方向记忆()
    {
        const int width = 7;
        const int height = 7;
        bool[] walkable = new bool[width * height];

        for (int y = 1; y <= 5; y++)
            SetWalkable(walkable, width, 3, y);
        for (int x = 3; x <= 5; x++)
            SetWalkable(walkable, width, x, 5);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext rawAgent = CreateEntity(new Vector3(3.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(5.5f, 0f, 5.5f);

        GroupMoveConfig rawConfig = CreateConfig();
        rawConfig.VelocitySmoothing = 1f;
        FlowFieldCrowdMovementSystem.SetConfig(rawConfig);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(rawAgent, goal, 2f, out _));
        rawAgent.Position = new Vector3(3.5f, 0f, 5.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(rawAgent, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDesiredDirection(rawAgent.GetHashCode(), out Vector3 rawDirection));

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();

        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext blendedAgent = CreateEntity(new Vector3(3.5f, 0f, 2.5f));
        GroupMoveConfig blendedConfig = CreateConfig();
        blendedConfig.VelocitySmoothing = 0.25f;
        FlowFieldCrowdMovementSystem.SetConfig(blendedConfig);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(blendedAgent, goal, 2f, out _));
        blendedAgent.Position = new Vector3(3.5f, 0f, 5.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(blendedAgent, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDesiredDirection(blendedAgent.GetHashCode(), out Vector3 blendedDirection));

        Assert.Greater(rawDirection.x, 0.8f, $"无平滑时转角应迅速贴向横向，raw={rawDirection}");
        Assert.Greater(blendedDirection.z, rawDirection.z + 0.15f, $"跨格转向时应保留上一格方向记忆，blended={blendedDirection} raw={rawDirection}");
        Assert.Greater(blendedDirection.magnitude, 0.9f, $"平滑后方向仍应保持有效，blended={blendedDirection}");
    }

    [Test]
    public void PortalTile积分应能从portal反向覆盖到当前格()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];

        for (int y = 4; y <= 7; y++)
            SetWalkable(walkable, width, 1, y);
        for (int x = 1; x <= 6; x++)
            SetWalkable(walkable, width, x, 4);
        for (int y = 1; y <= 4; y++)
            SetWalkable(walkable, width, 6, y);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.magnitude, 0.1f, $"portal tile 应对当前格给出有效引导，velocity={velocity}");
        Assert.Less(velocity.z, 0f, $"起点应先沿走廊推进而不是停滞，velocity={velocity}");
    }

    [Test]
    public void 墙边成本梯度会让积分流场避开贴边路径()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];

        for (int y = 1; y <= 3; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext edgeAgent = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(edgeAgent, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.z, 0.25f, $"墙边格应受到成本梯度引导离开边缘，而不是只沿墙横走，velocity={velocity}");
        Assert.Greater(velocity.x, 0.25f, $"成本梯度不应让单位放弃朝目标推进，velocity={velocity}");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(1, 1, out Vector2 flow));
        Assert.Greater(flow.x, 0.6f, $"flow pass 应按八邻居最低 integration 选择朝目标推进的斜向，flow={flow}");
        Assert.Greater(flow.y, 0.6f, $"flow pass 应按八邻居最低 integration 选择离墙的斜向，flow={flow}");
    }

    [Test]
    public void CostStamp会进入CostField并影响积分流场()
    {
        const int width = 7;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1001, new Vector3(3.5f, 0f, 2.5f), new Vector3(1.49f, 0f, 0.49f), 30);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost));
        Assert.AreEqual(30, cost, "cost stamp 应写入 CostField，而不是只存在注册表里");
        Assert.Greater(velocity.x, 0.5f, $"高成本带不应阻断朝目标推进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.25f, $"高成本带应让 flow/integration 产生绕行分量，velocity={velocity}");
    }

    [Test]
    public void CostStamp只影响匹配MovementType的CostField()
    {
        const int width = 5;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1002, 12345, new Vector3(2.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f), 40);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(4.5f, 0f, 1.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 1, out byte cost));
        Assert.AreEqual(1, cost, "不匹配 movement type 的 cost stamp 不应污染当前 agent type 的 CostField");
    }

    [Test]
    public void RuntimeDirtyQueue会原子提交运行时障碍重建()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte beforeCost));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(3.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte pendingCost));

        for (int i = 0; i < 8; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte committedCost));
        Assert.AreEqual(1, beforeCost, "测试初始格应是普通可走 cost");
        Assert.AreEqual(beforeCost, pendingCost, "runtime dirty job 完成前不应把半更新 working world 暴露给导航 world");
        Assert.AreEqual(255, committedCost, "runtime dirty queue 完成后应原子提交运行时障碍到 cost field");
    }

    [Test]
    public void RuntimeDirtyQueue重排不会丢失已Pending的DirtySector()
    {
        GroupMoveConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(31.5f, 0f, 1.5f), 2f, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(5.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9102, new Vector3(25.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));

        for (int i = 0; i < 32; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte firstCost));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(25, 1, out byte secondCost));
        Assert.AreEqual(255, firstCost, "pending job 被新 dirty 重排时，旧 dirty sector 不能丢失");
        Assert.AreEqual(255, secondCost, "新 dirty sector 也必须进入重排后的 runtime dirty job");
    }

    [Test]
    public void RuntimeDirtyQueue会分帧重建IslandField()
    {
        GroupMoveConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 9;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(8.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 1, out int initialLeftIsland, out int initialIslandCount));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(8, 1, out int initialRightIsland, out _));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9201, new Vector3(4.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 32; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 1, out int committedLeftIsland, out int committedIslandCount));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(8, 1, out int committedRightIsland, out _));
        Assert.AreEqual(1, initialIslandCount, "封堵前整条走廊应是单连通 island");
        Assert.AreEqual(initialLeftIsland, initialRightIsland, "封堵前左右两端应连通");
        Assert.AreEqual(2, committedIslandCount, "runtime dirty island field 分帧完成后应反映封堵造成的两个连通分支");
        Assert.AreNotEqual(committedLeftIsland, committedRightIsland, "封堵后左右两端不应仍在同一 island");
    }

    [Test]
    public void WorldBuildQueue会构建脏World但不暴露半成品()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build queue 处理前不应暴露半成品 world");

        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build queue 完成后应原子提交 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out int islandId, out int islandCount));
        Assert.AreEqual(1, islandId);
        Assert.AreEqual(1, islandCount);
    }

    [Test]
    public void WorldBuildQueue会分帧完成FullWorldBuild后再原子提交()
    {
        GroupMoveConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "world build job 未完成前不应暴露半成品 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(), "低预算下 full world build 应保留 pending job，而不是同步一次做完");

        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(), "world build queue 应在预算帧内最终完成");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "完成后才应原子提交 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out int islandId, out int islandCount));
        Assert.AreEqual(1, islandId);
        Assert.AreEqual(1, islandCount);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), 0, "full world build 完成后应具备 portal graph");
    }

    [Test]
    public void 大单位MovementType会扩大墙边Cost缓冲()
    {
        const int width = 9;
        const int height = 9;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext small = CreateEntity(new Vector3(4.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(small, new Vector3(7.5f, 0f, 4.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte smallCost));

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(0, 2.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext large = CreateEntity(new Vector3(4.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(large, new Vector3(7.5f, 0f, 4.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte largeCost));

        Assert.AreEqual(1, smallCost, $"默认单位半径下该格不应进入墙边缓冲，smallCost={smallCost}");
        Assert.Greater(largeCost, smallCost, $"大单位 movement type 应扩大墙边 CostField 缓冲，small={smallCost} large={largeCost}");
    }

    [Test]
    public void 窄门双向对冲时会让行并最终完成换向通行()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext left = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        SimEntityContext right = CreateEntity(new Vector3(7.5f, 0f, 1.5f));
        Vector3 leftGoal = new Vector3(7.5f, 0f, 1.5f);
        Vector3 rightGoal = new Vector3(0.5f, 0f, 1.5f);

        bool rightWasBlocked = false;
        bool rightRecovered = false;
        for (int frame = 1; frame <= 48; frame++)
        {
            float time = frame * 0.2f;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, time);

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, leftGoal, 2f, out Vector3 leftVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(right, rightGoal, 2f, out Vector3 rightVelocity));

            Vector3 rightGoalDir = (rightGoal - right.Position).normalized;
            float rightForwardProgress = Vector3.Dot(rightVelocity, rightGoalDir);
            if (rightForwardProgress <= 0.1f)
                rightWasBlocked = true;
            else if (rightWasBlocked)
                rightRecovered = true;

            left.Position = AdvanceWithinBounds(left.Position, leftGoal, leftVelocity, 0.2f, width, height);
            right.Position = AdvanceWithinBounds(right.Position, rightGoal, rightVelocity, 0.2f, width, height);
        }

        Assert.IsTrue(rightWasBlocked, "窄门对向通过时，后到一侧应出现等待");
        Assert.IsTrue(rightRecovered, "等待侧在让行结束后应恢复通行");
        Assert.Greater(left.Position.x, 4.5f, $"左侧单位应已穿过窄门，当前位置={left.Position}");
        Assert.Less(right.Position.x, 3.5f, $"右侧单位应已在换向后通过窄门，当前位置={right.Position}");
    }

    [Test]
    public void 交叉流相遇时会产生稳定侧绕而不是互相顶住()
    {
        const int width = 7;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext horizontal = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext vertical = CreateEntity(new Vector3(3.5f, 0f, 0.5f));
        Vector3 horizontalGoal = new Vector3(6.5f, 0f, 3.5f);
        Vector3 verticalGoal = new Vector3(3.5f, 0f, 6.5f);

        bool sawLateralDeviation = false;
        float minDistance = float.MaxValue;
        for (int frame = 1; frame <= 24; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(horizontal, horizontalGoal, 2.4f, out Vector3 horizontalVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(vertical, verticalGoal, 2.4f, out Vector3 verticalVelocity));

            if (Mathf.Abs(horizontalVelocity.z) > 0.08f || Mathf.Abs(verticalVelocity.x) > 0.08f)
                sawLateralDeviation = true;

            horizontal.Position = AdvanceTowardsGoal(horizontal.Position, horizontalGoal, horizontalVelocity, 0.2f);
            vertical.Position = AdvanceTowardsGoal(vertical.Position, verticalGoal, verticalVelocity, 0.2f);
            minDistance = Mathf.Min(minDistance, Vector3.Distance(horizontal.Position, vertical.Position));
        }

        Assert.IsTrue(sawLateralDeviation, "交叉流相遇时应出现侧绕分量，而不是只做纯正向顶撞");
        Assert.Greater(minDistance, 0.55f, $"交叉流相遇时不应压成重叠，minDistance={minDistance:F3}");
        Assert.Greater(horizontal.Position.x, 4.5f, $"横向单位应继续完成前进，当前位置={horizontal.Position}");
        Assert.Greater(vertical.Position.z, 4.5f, $"纵向单位应继续完成前进，当前位置={vertical.Position}");
    }

    [Test]
    public void 错峰交叉流在天然有先后手时不应被过度刹停()
    {
        const int width = 7;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext horizontal = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext vertical = CreateEntity(new Vector3(3.5f, 0f, 1.7f));
        Vector3 horizontalGoal = new Vector3(6.5f, 0f, 3.5f);
        Vector3 verticalGoal = new Vector3(3.5f, 0f, 6.5f);

        float minHorizontalForward = float.MaxValue;
        float minVerticalForward = float.MaxValue;
        for (int frame = 1; frame <= 8; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(horizontal, horizontalGoal, 2.4f, out Vector3 horizontalVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(vertical, verticalGoal, 2.4f, out Vector3 verticalVelocity));

            float horizontalForward = Vector3.Dot(horizontalVelocity, (horizontalGoal - horizontal.Position).normalized);
            float verticalForward = Vector3.Dot(verticalVelocity, (verticalGoal - vertical.Position).normalized);
            minHorizontalForward = Mathf.Min(minHorizontalForward, horizontalForward);
            minVerticalForward = Mathf.Min(minVerticalForward, verticalForward);

            horizontal.Position = AdvanceTowardsGoal(horizontal.Position, horizontalGoal, horizontalVelocity, 0.2f);
            vertical.Position = AdvanceTowardsGoal(vertical.Position, verticalGoal, verticalVelocity, 0.2f);
        }

        Assert.Greater(minHorizontalForward, 1.55f, $"错峰交叉流里，横向单位不应被过度刹停，minForward={minHorizontalForward:F3}");
        Assert.Greater(minVerticalForward, 1.85f, $"错峰交叉流里，纵向单位不应被过度刹停，minForward={minVerticalForward:F3}");
    }

    [Test]
    public void 同向跟随且无碰撞风险时预测避让不应制造无谓蛇形()
    {
        const int width = 10;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        SimEntityContext follower = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(8.5f, 0f, 2.5f);

        float maxFollowerLateral = 0f;
        for (int frame = 1; frame <= 10; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, goal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(follower, goal, 2f, out Vector3 followerVelocity));

            maxFollowerLateral = Mathf.Max(maxFollowerLateral, Mathf.Abs(followerVelocity.z));

            leader.Position = AdvanceTowardsGoal(leader.Position, goal, leaderVelocity, 0.2f);
            follower.Position = AdvanceTowardsGoal(follower.Position, goal, followerVelocity, 0.2f);
        }

        Assert.Less(maxFollowerLateral, 0.22f, $"同向安全跟随时不应被预测避让制造明显蛇形，maxLateral={maxFollowerLateral:F3}");
    }

    [Test]
    public void 瓶颈车道偏置对同向单位保持稳定侧偏()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        SimEntityContext follower = CreateEntity(new Vector3(1.1f, 0f, 1.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 1.5f);

        bool initialized = false;
        float initialLeaderSign = 0f;
        float initialFollowerSign = 0f;
        for (int frame = 1; frame <= 12; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, goal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(follower, goal, 2f, out Vector3 followerVelocity));

            if (!initialized && Mathf.Abs(leaderVelocity.z) > 0.01f && Mathf.Abs(followerVelocity.z) > 0.01f)
            {
                initialLeaderSign = Mathf.Sign(leaderVelocity.z);
                initialFollowerSign = Mathf.Sign(followerVelocity.z);
                initialized = true;
            }

            if (initialized)
            {
                Assert.AreEqual(initialLeaderSign, Mathf.Sign(leaderVelocity.z), $"领头单位侧偏方向不应在瓶颈前后抖动，frame={frame}, vel={leaderVelocity}");
                Assert.AreEqual(initialFollowerSign, Mathf.Sign(followerVelocity.z), $"后随单位侧偏方向不应在瓶颈前后抖动，frame={frame}, vel={followerVelocity}");
            }

            leader.Position = AdvanceTowardsGoal(leader.Position, goal, leaderVelocity, 0.2f);
            follower.Position = AdvanceTowardsGoal(follower.Position, goal, followerVelocity, 0.2f);
        }

        Assert.IsTrue(initialized, "同向通过窄口时应形成稳定侧偏，而不是始终零侧偏");
    }

    [Test]
    public void 宽Portal但出口立即收窄时仍会触发瓶颈让行()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];

        for (int x = 0; x <= 4; x++)
        {
            SetWalkable(walkable, width, x, 1);
            SetWalkable(walkable, width, x, 2);
            SetWalkable(walkable, width, x, 3);
        }

        for (int x = 5; x <= 8; x++)
            SetWalkable(walkable, width, x, 2);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext left = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext right = CreateEntity(new Vector3(8.5f, 0f, 2.5f));
        Vector3 leftGoal = new Vector3(8.5f, 0f, 2.5f);
        Vector3 rightGoal = new Vector3(0.5f, 0f, 2.5f);

        bool sawWait = false;
        bool sawRecovery = false;
        for (int frame = 1; frame <= 48; frame++)
        {
            float time = frame * 0.2f;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, time);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, leftGoal, 2f, out Vector3 leftVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(right, rightGoal, 2f, out Vector3 rightVelocity));

            Vector3 rightGoalDir = (rightGoal - right.Position).normalized;
            float rightForward = Vector3.Dot(rightVelocity, rightGoalDir);
            if (rightForward <= 0.1f)
                sawWait = true;
            else if (sawWait)
                sawRecovery = true;

            left.Position = AdvanceWithinBounds(left.Position, leftGoal, leftVelocity, 0.2f, width, height);
            right.Position = AdvanceWithinBounds(right.Position, rightGoal, rightVelocity, 0.2f, width, height);
        }

        Assert.IsTrue(sawWait, "出口立刻收窄的宽 portal 也应触发让行");
        Assert.IsTrue(sawRecovery, "等待方应在主通行方向出清后恢复前进");
    }

    [Test]
    public void 窄门同向连续流会优先整批出清而不是中途来回换向()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        SimEntityContext follower = CreateEntity(new Vector3(1.1f, 0f, 1.5f));
        SimEntityContext opponent = CreateEntity(new Vector3(7.5f, 0f, 1.5f));
        Vector3 rightGoal = new Vector3(7.5f, 0f, 1.5f);
        Vector3 leftGoal = new Vector3(0.5f, 0f, 1.5f);

        bool followerEnteredBeforeOpponent = false;
        bool opponentYielded = false;
        for (int frame = 1; frame <= 48; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, rightGoal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(follower, rightGoal, 2f, out Vector3 followerVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(opponent, leftGoal, 2f, out Vector3 opponentVelocity));

            float opponentForward = Vector3.Dot(opponentVelocity, (leftGoal - opponent.Position).normalized);
            if (opponentForward <= 0.1f)
                opponentYielded = true;

            leader.Position = AdvanceWithinBounds(leader.Position, rightGoal, leaderVelocity, 0.2f, width, height);
            follower.Position = AdvanceWithinBounds(follower.Position, rightGoal, followerVelocity, 0.2f, width, height);
            opponent.Position = AdvanceWithinBounds(opponent.Position, leftGoal, opponentVelocity, 0.2f, width, height);

            if (!followerEnteredBeforeOpponent && follower.Position.x >= 4.1f && opponent.Position.x > 4.0f)
                followerEnteredBeforeOpponent = true;
        }

        Assert.IsTrue(opponentYielded, "对向来流应先让已经成型的同向流出清");
        Assert.IsTrue(followerEnteredBeforeOpponent, $"同向后继单位应跟随前队连续过门，而不是中途被对向抢断。 follower={follower.Position} opponent={opponent.Position}");
    }

    [Test]
    public void 瓶颈等待排序会优先已标记Leader的单位()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leftLeader = CreateEntity(new Vector3(0.5f, 0f, 1.5f), isLeader: true);
        SimEntityContext leftFollower = CreateEntity(new Vector3(1.1f, 0f, 1.5f));
        SimEntityContext rightA = CreateEntity(new Vector3(7.5f, 0f, 1.5f));
        SimEntityContext rightB = CreateEntity(new Vector3(6.9f, 0f, 1.5f));
        Vector3 rightGoal = new Vector3(7.5f, 0f, 1.5f);
        Vector3 leftGoal = new Vector3(0.5f, 0f, 1.5f);

        bool leaderCrossedBeforeBothOpponents = false;
        for (int frame = 1; frame <= 48; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leftLeader, rightGoal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leftFollower, rightGoal, 2f, out Vector3 followerVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(rightA, leftGoal, 2f, out Vector3 rightAVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(rightB, leftGoal, 2f, out Vector3 rightBVelocity));

            leftLeader.Position = AdvanceWithinBounds(leftLeader.Position, rightGoal, leaderVelocity, 0.2f, width, height);
            leftFollower.Position = AdvanceWithinBounds(leftFollower.Position, rightGoal, followerVelocity, 0.2f, width, height);
            rightA.Position = AdvanceWithinBounds(rightA.Position, leftGoal, rightAVelocity, 0.2f, width, height);
            rightB.Position = AdvanceWithinBounds(rightB.Position, leftGoal, rightBVelocity, 0.2f, width, height);

            if (!leaderCrossedBeforeBothOpponents
                && leftLeader.Position.x >= 4.1f
                && (rightA.Position.x > 4.0f || rightB.Position.x > 4.0f))
            {
                leaderCrossedBeforeBothOpponents = true;
            }
        }

        Assert.IsTrue(leaderCrossedBeforeBothOpponents, $"Leader 等待单位应优先获得瓶颈通行机会。 leader={leftLeader.Position} follower={leftFollower.Position} rightA={rightA.Position} rightB={rightB.Position}");
        Assert.Greater(leftLeader.Position.x, 7.0f, $"Leader 应最终通过瓶颈到达目标侧。 leader={leftLeader.Position}");
    }

    [Test]
    public void 同Sector长窄走廊中段会车时应出现调度而不是纯顶撞()
    {
        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext left = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        SimEntityContext right = CreateEntity(new Vector3(10.5f, 0f, 1.5f));
        Vector3 leftGoal = new Vector3(10.5f, 0f, 1.5f);
        Vector3 rightGoal = new Vector3(1.5f, 0f, 1.5f);

        bool sawWait = false;
        float minDistance = float.MaxValue;
        for (int frame = 1; frame <= 40; frame++)
        {
            float time = frame * 0.2f;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, time);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, leftGoal, 2f, out Vector3 leftVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(right, rightGoal, 2f, out Vector3 rightVelocity));

            float leftForward = Vector3.Dot(leftVelocity, (leftGoal - left.Position).normalized);
            float rightForward = Vector3.Dot(rightVelocity, (rightGoal - right.Position).normalized);
            if (leftForward <= 0.1f || rightForward <= 0.1f)
                sawWait = true;

            left.Position = AdvanceWithinBounds(left.Position, leftGoal, leftVelocity, 0.2f, width, height);
            right.Position = AdvanceWithinBounds(right.Position, rightGoal, rightVelocity, 0.2f, width, height);
            minDistance = Mathf.Min(minDistance, Vector3.Distance(left.Position, right.Position));
        }

        Assert.IsTrue(sawWait, "长窄走廊中段会车时应出现等待/让行，而不是双方始终满速对冲");
        Assert.Greater(minDistance, 0.55f, $"长窄走廊中段会车不应压成重叠，minDistance={minDistance:F3}");
    }

    [Test]
    public void Leader在交叉避让中不应被非Leader过度刹停()
    {
        const int width = 7;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(0.5f, 0f, 3.5f), isLeader: true);
        SimEntityContext crosser = CreateEntity(new Vector3(3.5f, 0f, 0.9f));
        Vector3 leaderGoal = new Vector3(6.5f, 0f, 3.5f);
        Vector3 crosserGoal = new Vector3(3.5f, 0f, 6.5f);

        float minLeaderForward = float.MaxValue;
        float minCrosserForward = float.MaxValue;
        for (int frame = 1; frame <= 10; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, leaderGoal, 2.4f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(crosser, crosserGoal, 2.4f, out Vector3 crosserVelocity));

            minLeaderForward = Mathf.Min(minLeaderForward, Vector3.Dot(leaderVelocity, (leaderGoal - leader.Position).normalized));
            minCrosserForward = Mathf.Min(minCrosserForward, Vector3.Dot(crosserVelocity, (crosserGoal - crosser.Position).normalized));

            leader.Position = AdvanceTowardsGoal(leader.Position, leaderGoal, leaderVelocity, 0.2f);
            crosser.Position = AdvanceTowardsGoal(crosser.Position, crosserGoal, crosserVelocity, 0.2f);
        }

        Assert.Greater(minLeaderForward, 1.45f, $"Leader 在交叉避让中不应被明显过刹，minLeaderForward={minLeaderForward:F3}");
        Assert.Greater(minCrosserForward, 1.1f, $"非Leader 仍应保持正常穿行，而不是被策略压成停滞，minCrosserForward={minCrosserForward:F3}");
    }

    [Test]
    public void 同组流进入瓶颈时应优先保持同一ConvoyToken()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(0.5f, 0f, 1.5f), isLeader: true);
        SimEntityContext follower = CreateEntity(new Vector3(1.1f, 0f, 1.5f));
        follower.SetProperty(CreatureMainProperty.Speed, (Fix64)38f);
        Vector3 goal = new Vector3(7.5f, 0f, 1.5f);

        bool sawLeaderTokenHold = false;
        for (int frame = 1; frame <= 24; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, goal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(follower, goal, 2f, out Vector3 followerVelocity));

            if (leaderVelocity.x > 0.3f && followerVelocity.x > 0.1f)
                sawLeaderTokenHold = true;

            leader.Position = AdvanceWithinBounds(leader.Position, goal, leaderVelocity, 0.2f, width, height);
            follower.Position = AdvanceWithinBounds(follower.Position, goal, followerVelocity, 0.2f, width, height);
        }

        Assert.IsTrue(sawLeaderTokenHold, "同组通过瓶颈时应优先保持同一 convoy token 的连续通行语义");
    }

    [Test]
    public void 高优先级跟随状态在瓶颈前应优先插队于普通等待者()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext leader = CreateEntity(new Vector3(0.5f, 0f, 1.5f), isLeader: true);
        SimEntityContext followHigh = CreateEntity(new Vector3(1.1f, 0f, 1.5f), isLeader: false);
        SimEntityContext followLow = CreateEntity(new Vector3(1.7f, 0f, 1.5f), isLeader: false);
        FlowFieldCrowdMovementSystem.SetAgentState(followHigh.GetHashCode(), GroupMoveCoordinator.AgentState.Follow);
        FlowFieldCrowdMovementSystem.SetAgentState(followLow.GetHashCode(), GroupMoveCoordinator.AgentState.Idle);
        Vector3 goal = new Vector3(7.5f, 0f, 1.5f);

        float highForward = 0f;
        float lowForward = 0f;
        for (int frame = 1; frame <= 8; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.2f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(leader, goal, 2f, out Vector3 leaderVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(followHigh, goal, 2f, out Vector3 highVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(followLow, goal, 2f, out Vector3 lowVelocity));

            highForward = Mathf.Max(highForward, highVelocity.x);
            lowForward = Mathf.Max(lowForward, lowVelocity.x);

            leader.Position = AdvanceWithinBounds(leader.Position, goal, leaderVelocity, 0.2f, width, height);
            followHigh.Position = AdvanceWithinBounds(followHigh.Position, goal, highVelocity, 0.2f, width, height);
            followLow.Position = AdvanceWithinBounds(followLow.Position, goal, lowVelocity, 0.2f, width, height);
        }

        Assert.Greater(highForward, lowForward * 0.95f, $"高优先级等待者应不劣于普通等待者，high={highForward:F3} low={lowForward:F3}");
    }

    [Test]
    public void 同Sector拐角出口抢出时应出现让行而不是拐角互顶()
    {
        const int width = 7;
        const int height = 7;
        bool[] walkable = new bool[width * height];

        for (int y = 0; y <= 3; y++)
            SetWalkable(walkable, width, 3, y);
        for (int x = 3; x < width; x++)
            SetWalkable(walkable, width, x, 3);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext south = CreateEntity(new Vector3(3.5f, 0f, 0.5f));
        SimEntityContext east = CreateEntity(new Vector3(6.5f, 0f, 3.5f));
        Vector3 southGoal = new Vector3(6.5f, 0f, 3.5f);
        Vector3 eastGoal = new Vector3(3.5f, 0f, 0.5f);

        bool sawYield = false;
        float minDistance = float.MaxValue;
        for (int frame = 1; frame <= 32; frame++)
        {
            float time = frame * 0.2f;
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, time);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(south, southGoal, 2f, out Vector3 southVelocity));
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(east, eastGoal, 2f, out Vector3 eastVelocity));

            float southForward = Vector3.Dot(southVelocity, (southGoal - south.Position).normalized);
            float eastForward = Vector3.Dot(eastVelocity, (eastGoal - east.Position).normalized);
            if (southForward <= 0.1f || eastForward <= 0.1f)
                sawYield = true;

            south.Position = AdvanceWithinBounds(south.Position, southGoal, southVelocity, 0.2f, width, height);
            east.Position = AdvanceWithinBounds(east.Position, eastGoal, eastVelocity, 0.2f, width, height);
            minDistance = Mathf.Min(minDistance, Vector3.Distance(south.Position, east.Position));
        }

        Assert.IsTrue(sawYield, "拐角出口会车时应有一方短暂让行，而不是都抢拐角");
        Assert.Greater(minDistance, 0.55f, $"拐角出口会车不应压成重叠，minDistance={minDistance:F3}");
    }

    [Test]
    public void 动态障碍生成后会重路由而不是沿旧走廊硬顶()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(8.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2.4f, out Vector3 beforeBlockVelocity));
        Assert.Greater(beforeBlockVelocity.x, 1.6f, $"初始无障碍时应直走，velocity={beforeBlockVelocity}");
        Assert.AreEqual(0f, beforeBlockVelocity.z, 0.2f, $"初始无障碍时不应无故侧偏，velocity={beforeBlockVelocity}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9001, new Vector3(4.5f, 0f, 2.5f), new Vector3(0.6f, 0f, 0.6f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.4f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2.4f, out Vector3 afterBlockVelocity));
        Assert.Greater(afterBlockVelocity.x, 0.4f, $"动态障碍后仍应保持朝目标前进分量，velocity={afterBlockVelocity}");
        Assert.Greater(Mathf.Abs(afterBlockVelocity.z), 0.25f, $"动态障碍后应出现明显绕行动量而不是继续直冲，velocity={afterBlockVelocity}");
    }

    [Test]
    public void 动态障碍改变连通性后会同步重建Island()
    {
        const int width = 7;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 farGoal = new Vector3(6.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 initialReachable, out string initialReason), initialReason);
        Assert.AreEqual(6.5f, initialReachable.x, 0.01f, $"无障碍时目标应保持原位置，reachable={initialReachable}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(3.5f, 0f, 1.5f), new Vector3(0.6f, 0f, 0.6f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FlowGoalResolutionIslandDiag\]"));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 blockedReachable, out string blockedReason), blockedReason);

        Assert.Less(blockedReachable.x, 3.5f, $"动态障碍切断走廊后，最近可达目标应留在起点同 island，而不是继续使用旧 island 追到障碍另一侧，reachable={blockedReachable}");
    }

    [Test]
    public void 动态障碍HaloSector不会被BoundsClamp误封边界格()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 farGoal = new Vector3(3.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 initialReachable, out string initialReason), initialReason);
        Assert.AreEqual(3.5f, initialReachable.x, 0.01f, $"初始目标应可达 reachable={initialReachable}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9102, new Vector3(4.5f, 0f, 1.5f), new Vector3(0.4f, 0f, 0.4f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out Vector3 reachableAfterHaloDirty, out string blockedReason), blockedReason);

        Assert.AreEqual(3.5f, reachableAfterHaloDirty.x, 0.01f, $"障碍只影响相邻 sector 时，halo sector 不能被 clamp 误封，reachable={reachableAfterHaloDirty}");
    }

    [Test]
    public void 位移后旧路径失效会触发重算而不是沿失效路径继续撞()
    {
        const int width = 9;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;
        moveComp.MoveTo(new Vector3(8.5f, 0f, 2.5f));

        DurationMoveEffectComp effectComp = new DurationMoveEffectComp();
        effectComp.Init(ctx);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        moveComp.Move(0.2f);
        ctx.MoveExecutor.Execute(0.2f);
        ctx.SyncPositionFromExecutor();
        Assert.Greater(ctx.Position.x, 0.7f, $"首次寻路应正常向前，pos={ctx.Position}");

        effectComp.StartDurationAdditionalMove(0.1f, new Vector3(0f, 0f, -10f));
        effectComp.ApplyEffect(0.1f);
        moveComp.Move(0.1f);
        ctx.MoveExecutor.Execute(0.1f);
        ctx.SyncPositionFromExecutor();
        Assert.Less(ctx.Position.z, 2.0f, $"位移应把单位推离原走廊，pos={ctx.Position}");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9002, new Vector3(4.5f, 0f, 2.5f), new Vector3(0.6f, 0f, 0.6f));

        effectComp.ApplyEffect(0.1f);
        Assert.AreEqual(MovementMode.Normal, ctx.MoveExecutor.MovementMode, "位移结束后应恢复 Normal");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.4f);
        moveComp.Move(0.2f);
        ctx.MoveExecutor.Execute(0.2f);
        ctx.SyncPositionFromExecutor();

        SimMoveExecutor executor = ctx.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(executor, "测试上下文应使用 SimMoveExecutor");
        Assert.Greater(executor.LastFrameVelocity.x, 0.2f, $"路径失效重算后仍应保持前进分量，velocity={executor.LastFrameVelocity}");
        Assert.Greater(Mathf.Abs(executor.LastFrameVelocity.z), 0.2f, $"路径失效重算后应绕开新障碍，而不是沿旧直线路径继续撞，velocity={executor.LastFrameVelocity}");
    }

    [Test]
    public void Portal窗口部分槽位不可达时会裁掉坏槽位而不是整窗报错()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];

        for (int y = 1; y <= 3; y++)
            SetWalkable(walkable, width, 2, y);
        for (int x = 0; x <= 2; x++)
            SetWalkable(walkable, width, x, 3);
        for (int x = 0; x <= 1; x++)
            SetWalkable(walkable, width, x, 2);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(0.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.DoesNotThrow(() =>
        {
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
            Assert.Less(velocity.x, -0.2f, $"应沿仍然可达的 portal 槽位继续向目标推进，velocity={velocity}");
        });
    }

    [Test]
    public void 不可达目标会解析到同岛最近可达点()
    {
        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 1);
        SetWalkable(walkable, width, 10, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 unreachableGoal = new Vector3(10.5f, 0f, 1.5f);

        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(
                ctx,
                unreachableGoal,
                1f,
                out Vector3 reachableGoal,
                out string failureReason),
            failureReason);

        Assert.AreEqual(3.5f, reachableGoal.x, 0.001f, $"应选择当前 island 上离不可达目标最近的可达点 reachable={reachableGoal}");
        Assert.AreEqual(1.5f, reachableGoal.z, 0.001f, $"应保持最近可达走廊中心 reachable={reachableGoal}");
    }

    [Test]
    public void 静止单位仍会作为动态避让障碍()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext mover = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        SimEntityContext idleBlocker = CreateEntity(new Vector3(1.1f, 0f, 1.5f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(mover, new Vector3(7.5f, 0f, 1.5f), 2f, out Vector3 velocity));

        Assert.Greater(Mathf.Abs(velocity.z), 0.05f, $"静止单位应进入空间桶并让移动单位产生侧向避让，blocker={idleBlocker.Position} velocity={velocity}");
    }

    [Test]
    public void 无导航目标的重叠单位会产生分离恢复速度()
    {
        const int width = 4;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext left = CreateEntity(new Vector3(1.0f, 0f, 1.5f));
        SimEntityContext right = CreateEntity(new Vector3(1.45f, 0f, 1.5f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetIdleOverlapRecoveryVelocity(left, 2f, out Vector3 leftVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetIdleOverlapRecoveryVelocity(right, 2f, out Vector3 rightVelocity));

        Assert.Less(leftVelocity.x, -0.05f, $"左侧重叠单位应向左恢复分离，leftVelocity={leftVelocity}");
        Assert.Greater(rightVelocity.x, 0.05f, $"右侧重叠单位应向右恢复分离，rightVelocity={rightVelocity}");
    }

    [Test]
    public void 同一移动目标换格时会立即刷新导航目标()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 firstVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.15f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 reusedVelocity));

        Assert.Greater(firstVelocity.z, 0.2f, $"初始目标在上方，应有向上分量 first={firstVelocity}");
        Assert.Less(reusedVelocity.z, -0.2f, $"目标换到下方新格后应立即刷新导航目标 reused={reusedVelocity}");
    }

    [Test]
    public void 移动目标落在不可达Island时会追向最近可达点()
    {
        const int width = 8;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 3; x++)
            SetWalkable(walkable, width, x, 1);
        SetWalkable(walkable, width, 6, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        SimEntityContext target = CreateEntity(new Vector3(6.5f, 0f, 1.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FlowGoalResolvedToReachable\]"));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"移动目标处于不可达 island 时应追向当前 island 上最近可达点，velocity={velocity}");
    }

    [Test]
    public void 普通导航目标点被占用时会旋转到附近空位()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext mover = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext blocker = CreateEntity(new Vector3(5.5f, 0f, 2.5f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(mover, blocker.Position, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"被占目标点应仍保持朝目标附近前进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.01f, $"被占目标点应旋转到附近空位，而不是继续直冲占位单位，velocity={velocity}");
    }

    [Test]
    public void 目标占位候选不会跨到不可达Island再投回墙边()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x <= 5; x++)
        {
            SetWalkable(walkable, width, x, 1);
            SetWalkable(walkable, width, x, 2);
        }

        SetWalkable(walkable, width, 3, 4);
        SetWalkable(walkable, width, 4, 4);
        SetWalkable(walkable, width, 5, 4);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext second = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        SimEntityContext target = CreateEntity(new Vector3(4.5f, 0f, 2.5f));
        first.TargetComp = new SimTargetingComp(first, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        second.TargetComp = new SimTargetingComp(second, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(second, target.Position, 2f, out Vector3 secondVelocity));

        Assert.Greater(secondVelocity.x, 0.5f, $"第二个追击者应继续沿主岛朝目标前进，而不是被不可达候选点拉向隔墙碎岛，velocity={secondVelocity}");
        Assert.Less(Mathf.Abs(secondVelocity.z), 1.6f, $"目标占位候选不应跨 island 后被最近可达点投回墙边，velocity={secondVelocity}");
    }

    [Test]
    public void 多个单位追同一目标时不同目标点不会被共享锚点压成一个点()
    {
        const int width = 10;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext second = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(6.5f, 0f, 3.5f));
        first.TargetComp = new SimTargetingComp(first, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        second.TargetComp = new SimTargetingComp(second, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, new Vector3(5.5f, 0f, 2.5f), 2f, out Vector3 firstVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(second, new Vector3(5.5f, 0f, 4.5f), 2f, out Vector3 secondVelocity));

        Assert.Less(firstVelocity.z, -0.1f, $"第一个目标点在目标下侧，应保持下侧分量 first={firstVelocity}");
        Assert.Greater(secondVelocity.z, 0.1f, $"第二个目标点在目标上侧，不应被同 targetId 共享锚点压回下侧 second={secondVelocity}");
    }

    [Test]
    public void 多个单位追同一移动目标时使用最新共享目标格()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out Vector3 firstVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.15f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out Vector3 secondVelocity));

        Assert.Greater(firstVelocity.z, 0.2f, $"第一个追击者应朝初始目标上方移动 first={firstVelocity}");
        Assert.Less(secondVelocity.z, -0.2f, $"同目标第二个追击者应使用目标最新共享格并立即追下方 second={secondVelocity}");
    }

    [Test]
    public void 移动目标仍在同一Sector时不应重建整条SectorPath()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(9.5f, 0f, 5.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        target.Position = new Vector3(10.5f, 0f, 5.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.5f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"目标同 sector 移动后应继续沿已有 sector path 前进，只重建末端 tile，velocity={velocity}");
    }

    [Test]
    public void 非末端PortalTile会递归构建下游Tile以携带跨SectorLOS()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));

        int tileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        int tileBuildCount = FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount();
        Assert.Greater(velocity.x, 0.5f, $"长路径首段仍应沿走廊前进，velocity={velocity}");
        Assert.Greater(tileCount, 1, $"非末端 portal tile 应构建下游 tile 链来携带论文式跨 sector LOS flags，tileCount={tileCount}");
        Assert.AreEqual(tileCount, tileBuildCount, $"同一次 portal tile 构建中 seed/LOS 应共享 downstream tile，不应重复构建同一链路 tileCount={tileCount} tileBuilds={tileBuildCount}");
    }

    [Test]
    public void FlowTileBuildQueue会在查询前预构建路径Tile链()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount());

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        int buildCountBeforeQueue = FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount();
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        int queuedBuildCount = FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount() - buildCountBeforeQueue;
        int queuedTileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        Assert.Greater(queuedTileCount, 1, $"flow tile queue 应能在查询前预构建下游 tile 链，tileCount={queuedTileCount}");
        Assert.AreEqual(queuedTileCount, queuedBuildCount, $"预构建 tile 数应等于实际 build 数，tileCount={queuedTileCount} builds={queuedBuildCount}");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        int buildCountBeforeQuery = FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"预构建 tile 命中后仍应沿走廊前进，velocity={velocity}");
        Assert.AreEqual(buildCountBeforeQuery, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "查询命中预构建 tile 后不应再次同步构建 tile");
    }

    [Test]
    public void FlowTileBuildQueue低预算会保留未完成TileJob()
    {
        GroupMoveConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(47.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "低预算下 flow tile queue 应保留未完成 job，而不是同步一次做完整条 tile 链");

        for (int frame = 3; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "flow tile queue 应在后续预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 1, "完成后应提交预构建 tile 链");
    }

    [Test]
    public void PortalTile会在构建期缓存PortalTarget()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedPortalTarget(0, 3, out Vector3 portalTarget, out int visibleCount, out int selectedPair, out bool usedOppositeCenter));
        Assert.Greater(visibleCount, 0, $"portal tile 应在构建期缓存可见 portal 候选，target={portalTarget}");
        Assert.GreaterOrEqual(selectedPair, 0, $"portal tile 应缓存选中的 portal 槽位，target={portalTarget}");
        Assert.IsFalse(usedOppositeCenter, "直走廊 portal target 不应退化成 opposite center");
        Assert.Greater(portalTarget.x, chaser.Position.x, $"portal target 应指向下游 portal 对侧，target={portalTarget}");
    }

    [Test]
    public void LosPass只在真实转角生成WaveFrontBlocked()
    {
        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 1; y <= 5; y++)
            walkable[4 + y * width] = false;
        for (int x = 4; x <= 6; x++)
            walkable[x + 5 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(2.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(2.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(5, 6, out bool hiddenLos, out _));
        Assert.IsFalse(hiddenLos, "L 型墙体背后的格子不应被 LOS wavefront 绕过去标成直视");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(4, 4, out _, out bool cornerBlocked));
        Assert.IsTrue(cornerBlocked, "LOS 真转角外延线上应标记 WaveFrontBlocked");
    }

    [Test]
    public void PortalTile会续接下游WaveFrontBlocked遮挡线()
    {
        const int width = 8;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        walkable[5 + 2 * width] = false;
        walkable[5 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(4, 2, out _, out bool downstreamBlocked));
        Assert.IsTrue(downstreamBlocked, "final tile 的 LOS corner 应先在下游 portal cell 形成 WaveFrontBlocked");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(2, 2, out _, out bool carriedBlocked));
        Assert.IsTrue(carriedBlocked, "portal tile 应把下游 WaveFrontBlocked 续接到本 sector 内部，而不是只标 portal 单格");
    }

    [Test]
    public void 移动目标跨Sector时立即切换到共享目标场()
    {
        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(2.5f, 0f, 6.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 initialVelocity));

        target.Position = new Vector3(2.5f, 0f, 0.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.5f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 oldFlowVelocity));

        Assert.Greater(initialVelocity.z, 0.15f, $"初始目标在上方，应有上行分量 initial={initialVelocity}");
        Assert.Less(oldFlowVelocity.z, -0.15f, $"目标跨 sector 后应立即切到新共享目标场 oldFlow={oldFlowVelocity}");
    }

    [Test]
    public void 固定点导航目标变化仍会立即响应()
    {
        const int width = 16;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 1);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(8.5f, 0f, 1.5f));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(12.5f, 0f, 1.5f), 2f, out Vector3 rightVelocity));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.15f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(4.5f, 0f, 1.5f), 2f, out Vector3 leftVelocity));

        Assert.Greater(rightVelocity.x, 0.5f, $"初始固定点应向右，velocity={rightVelocity}");
        Assert.Less(leftVelocity.x, -0.5f, $"固定点改变应立即向左，velocity={leftVelocity}");
    }

    private static void SimulateAgent(SimEntityContext ctx, Vector3 goal, int frames, float dt, float speed)
    {
        for (int frame = 1; frame <= frames; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out Vector3 velocity));
            ctx.Position = AdvanceTowardsGoal(ctx.Position, goal, velocity, dt);
        }
    }

    private static Vector3 AdvanceTowardsGoal(Vector3 position, Vector3 goal, Vector3 velocity, float dt)
    {
        Vector3 displacement = velocity * dt;
        Vector3 toGoal = goal - position;
        toGoal.y = 0f;
        Vector3 horizontalDisplacement = new Vector3(displacement.x, 0f, displacement.z);
        if (toGoal.sqrMagnitude > 0.0001f && horizontalDisplacement.sqrMagnitude > 0.0001f)
        {
            Vector3 goalDir = toGoal.normalized;
            float forwardDistance = Vector3.Dot(horizontalDisplacement, goalDir);
            if (forwardDistance > toGoal.magnitude)
            {
                horizontalDisplacement = goalDir * toGoal.magnitude;
                displacement = new Vector3(horizontalDisplacement.x, displacement.y, horizontalDisplacement.z);
            }
        }

        return position + displacement;
    }

    private static Vector3 AdvanceWithinBounds(Vector3 position, Vector3 goal, Vector3 velocity, float dt, int width, int height)
    {
        Vector3 next = AdvanceTowardsGoal(position, goal, velocity, dt);
        float minX = 0.5f;
        float maxX = width - 0.5f;
        float minZ = 0.5f;
        float maxZ = height - 0.5f;
        next.x = Mathf.Clamp(next.x, minX, maxX);
        next.z = Mathf.Clamp(next.z, minZ, maxZ);
        return next;
    }

    private static SimEntityContext CreateEntity(Vector3 position)
    {
        return CreateEntity(position, false);
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader)
    {
        SimEntityContext ctx = new SimEntityContext
        {
            Position = position,
            Side = SideType.PlayerSide,
            Alive = true
        };
        ctx.SetProperty(CreatureMainProperty.Speed, (Fix64)40f);
        ctx.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)10f);
        ctx.MoveExecutor = new SimMoveExecutor { Position = position };
        FlowFieldCrowdMovementSystem.RegisterAgent(ctx, isLeader, 0.5f);
        return ctx;
    }

    private static GroupMoveConfig CreateConfig()
    {
        GroupMoveConfig config = ScriptableObject.CreateInstance<GroupMoveConfig>();
        config.SectorSizeInCells = 4;
        config.PortalNarrowWidthCells = 1;
        config.FlowTileCacheLimit = 32;
        config.RuntimeRebuildBudgetMilliseconds = 1.5f;
        config.CrowdPredictionTime = 0.35f;
        config.LaneBiasStrength = 0.18f;
        config.BoundaryAvoidanceWeight = 0.6f;
        config.BottleneckSwitchCooldown = 0.25f;
        config.BottleneckWaitTimeout = 0.45f;
        config.BottleneckInfluenceDistance = 1.6f;
        config.BottleneckClearanceHoldTime = 0.35f;
        config.DrawNavigationDebug = false;
        config.DrawFlowFieldDebug = false;
        return config;
    }

    private static void SetWalkable(bool[] walkable, int width, int x, int y)
    {
        walkable[x + y * width] = true;
    }
}
