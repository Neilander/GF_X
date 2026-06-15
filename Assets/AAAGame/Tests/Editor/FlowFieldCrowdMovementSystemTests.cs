using NUnit.Framework;
using UnityEngine;

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

        effectComp.StartDurationAdditionalMove(0.15f, Vector3.zero);
        effectComp.ApplyEffect(0.1f);
        moveComp.Move(0.1f);
        ctx.MoveExecutor.Execute(0.1f);
        ctx.SyncPositionFromExecutor();

        Assert.AreEqual(0.5f, ctx.Position.x, 0.001f, "位移期间不应执行主动寻路位移");

        effectComp.ApplyEffect(0.1f);
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
        var ex = Assert.Throws<System.InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(left, goal, 2f, out _));
        StringAssert.Contains("Flow strict fail: world unavailable", ex.Message);
    }

    [Test]
    public void 细窄可走格不会被中心点误判为不可走()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        walkable[2 + 2 * width] = true;

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

            left.Position += leftVelocity * 0.2f;
            right.Position += rightVelocity * 0.2f;
        }

        Assert.IsTrue(rightWasBlocked, "窄门对向通过时，后到一侧应出现等待");
        Assert.IsTrue(rightRecovered, "等待侧在让行结束后应恢复通行");
        Assert.Greater(left.Position.x, 4.5f, $"左侧单位应已穿过窄门，当前位置={left.Position}");
        Assert.Less(right.Position.x, 3.5f, $"右侧单位应已在换向后通过窄门，当前位置={right.Position}");
    }

    private static void SimulateAgent(SimEntityContext ctx, Vector3 goal, int frames, float dt, float speed)
    {
        for (int frame = 1; frame <= frames; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, speed, out Vector3 velocity));
            ctx.Position += velocity * dt;
        }
    }

    private static SimEntityContext CreateEntity(Vector3 position)
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
        return ctx;
    }

    private static GroupMoveConfig CreateConfig()
    {
        GroupMoveConfig config = ScriptableObject.CreateInstance<GroupMoveConfig>();
        config.SectorSizeInCells = 4;
        config.PortalNarrowWidthCells = 1;
        config.FlowTileCacheLimit = 32;
        config.CrowdPredictionTime = 0.35f;
        config.LaneBiasStrength = 0.18f;
        config.BoundaryAvoidanceWeight = 0.6f;
        config.BottleneckSwitchCooldown = 0.25f;
        config.BottleneckWaitTimeout = 0.45f;
        config.BottleneckInfluenceDistance = 1.6f;
        config.DrawNavigationDebug = false;
        config.DrawFlowFieldDebug = false;
        return config;
    }

    private static void SetWalkable(bool[] walkable, int width, int x, int y)
    {
        walkable[x + y * width] = true;
    }
}
