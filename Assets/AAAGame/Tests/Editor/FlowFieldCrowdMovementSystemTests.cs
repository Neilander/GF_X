using NUnit.Framework;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.TestTools;

[TestFixture]
[SingleThreaded]
public class FlowFieldCrowdMovementSystemTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        SetupCombatPhaseForTests();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(CreateConfig());
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
    }

    private static void SetupCombatPhaseForTests()
    {
        FieldInfo dataModelField = typeof(GF).GetField("<DataModel>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        GameFramework.DataModelComponent current = dataModelField?.GetValue(null) as GameFramework.DataModelComponent;
        if (current == null)
        {
            GameObject go = new GameObject("FlowFieldTests_DataModel");
            current = go.AddComponent<GameFramework.DataModelComponent>();
            dataModelField?.SetValue(null, current);
        }

        FieldInfo dataModelsField = typeof(GameFramework.DataModelComponent).GetField("m_DataModels", BindingFlags.Instance | BindingFlags.NonPublic);
        object dataModels = dataModelsField?.GetValue(current);
        if (dataModelsField != null && (dataModels == null || dataModels.GetType() != dataModelsField.FieldType))
        {
            dataModels = Activator.CreateInstance(dataModelsField.FieldType);
            dataModelsField.SetValue(current, dataModels);
        }

        InGameDataModel model = current.GetDataModel<InGameDataModel>();
        if (model == null)
        {
            model = (InGameDataModel)Activator.CreateInstance(typeof(InGameDataModel), true);
            Type typeIdPairType = typeof(GameFramework.DataModelComponent).Assembly.GetType("TypeIdPair");
            object pair = Activator.CreateInstance(
                typeIdPairType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { typeof(InGameDataModel), 0 },
                null);
            object dict = dataModelsField.GetValue(current);
            dict.GetType().GetMethod("Add")?.Invoke(dict, new[] { pair, model });
        }

        FieldInfo phaseField = typeof(InGameDataModel).GetField("m_IngameValue", BindingFlags.Instance | BindingFlags.NonPublic);
        Dictionary<IngameValueType, int> values = new Dictionary<IngameValueType, int>
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

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(2.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[TestUnit\] Flow strict fail: start blocked"));
        Vector3 velocity = Vector3.one;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out velocity));
        StringAssert.Contains("Flow strict fail: start blocked", ex.Message);
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
    public void 跨Sector待建PortalTile时不会用软视线直冲最终目标()
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
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 firstVelocity));

        Assert.Less(firstVelocity.z, -0.8f, $"portal tile 尚未可用时也应先朝 portal access 前进，velocity={firstVelocity}");
        Assert.AreEqual(0f, firstVelocity.x, 0.35f, $"portal tile 尚未可用时不应软视线斜切最终目标，velocity={firstVelocity}");
    }

    [Test]
    public void PortalTile当前格未积分时不会让LineOfSight覆盖有限邻居方向()
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
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(32, 3.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.Less(velocity.z, -0.8f, $"当前格未积分时应沿有限邻居流向推进，不能用 LOS 斜切最终目标，velocity={velocity}");
        Assert.AreEqual(0f, velocity.x, 0.35f, $"当前格未积分时不应输出朝最终目标的横向分量，velocity={velocity}");
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

        FlowFieldNavigationConfig rawConfig = CreateConfig();
        rawConfig.PathDirectionBlend = 1f;
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
        FlowFieldNavigationConfig blendedConfig = CreateConfig();
        blendedConfig.PathDirectionBlend = 0.25f;
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
    public void 同一格内不会重复混合路径方向()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 1.5f));
        FlowFieldNavigationConfig config = CreateConfig();
        config.PathDirectionBlend = 0.25f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(4.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDesiredDirection(ctx.GetHashCode(), out Vector3 firstDirection));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(4.5f, 0f, 1.5f), 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDesiredDirection(ctx.GetHashCode(), out Vector3 secondDirection));

        Assert.AreEqual(firstDirection.x, secondDirection.x, 0.0001f, $"同一格内不应重新混合路径方向，first={firstDirection} second={secondDirection}");
        Assert.AreEqual(firstDirection.z, secondDirection.z, 0.0001f, $"同一格内不应重新混合路径方向，first={firstDirection} second={secondDirection}");
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

        ProcessFlowTileBuildQueueUntilTileCount(1);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(1, 1, out Vector2 flow));
        string diag = FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(1, 1);
        Assert.Greater(flow.x, 0.6f, $"flow pass 应按八邻居最低 integration 选择朝目标推进的斜向，flow={flow} diag={diag}");
        Assert.Greater(flow.y, 0.6f, $"flow pass 应按八邻居最低 integration 选择离墙的斜向，flow={flow} diag={diag}");
    }

    [Test]
    public void Los格不写FlowDirection而由Los直接转向()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 16;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(6.5f, 0f, 8.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 8.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(8, 8, out bool hasLos, out _, out bool pathable));
        Assert.IsTrue(pathable, "可走格应写入 FlowField pathable flag");
        Assert.IsTrue(hasLos, "开阔区域目标附近应由 LOS flag 驱动");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(8, 8, out Vector2 flow));
        Assert.AreEqual(Vector2.zero, flow, "LOS 格不应再写 flow direction，避免 flow pass 重复处理 goal LOS 区域");
    }

    [Test]
    public void PortalLos目标应绑定选中对侧槽位()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(6.5f, 0f, 4.5f));
        Vector3 goal = new Vector3(14.5f, 0f, 4.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(2);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedPortalTarget(6, 4, out Vector3 portalTarget, out int visibleCount, out int selectedPair, out bool usedOppositeCenter));
        Assert.Greater(visibleCount, 0, $"portal target 应有当前侧可见候选，target={portalTarget}");
        Assert.GreaterOrEqual(selectedPair, 0, $"portal target 应选中 portal 槽位，target={portalTarget}");
        Assert.IsFalse(usedOppositeCenter, "当前侧可见时不应退化成 center");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestResolvedPortalTargets(6, 4, out Vector3 handoffTarget, out Vector3 lineOfSightTarget));
        Assert.Greater(handoffTarget.x, 8f, $"缓存 handoff target 应指向 portal 对侧，target={handoffTarget}");
        Assert.Greater(lineOfSightTarget.x, 8f, $"LOS target 应指向选中对侧 portal 槽位，target={lineOfSightTarget}");
        Assert.AreEqual(portalTarget, handoffTarget, "公开的 portal target 应保持为跨 sector handoff target");
        Assert.AreEqual(handoffTarget, lineOfSightTarget, "portal handoff 与 LOS target 应绑定同一选中对侧槽位，避免把单位拉回当前边界");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSource(ctx.GetHashCode(), out int source, out bool hasLineOfSight));
        Assert.AreEqual(0, source, $"选中对侧槽位可见时应由 LOS 直接转向，velocity={velocity}");
        Assert.IsTrue(hasLineOfSight, $"portal LOS 应检查选中对侧槽位，velocity={velocity}");
        Assert.Greater(velocity.x, 1.5f, $"应继续朝选中对侧槽位推进，velocity={velocity}");
        Assert.AreEqual(0f, velocity.z, 0.2f, $"直走廊 portal LOS 不应产生明显侧向，velocity={velocity}");
    }

    [Test]
    public void 不可达可走格不应输出FlowDirection()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                SetWalkable(walkable, width, x, y);
        }

        for (int x = 0; x < width; x++)
            walkable[x + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 6.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(1, 1, out _, out _, out bool pathable));
        Assert.IsTrue(pathable, "测试格必须是可走但与目标隔离的格子");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(1, 1, out Vector2 storedFlow));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(1, 1, out Vector2 runtimeFlow));
        string diag = FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(1, 1);

        Assert.AreEqual(Vector2.zero, storedFlow, $"不可达格不应写入 stored flow，diag={diag}");
        Assert.AreEqual(Vector2.zero, runtimeFlow, $"不可达格运行期不应输出方向，diag={diag}");
    }

    [Test]
    public void ClearTile释放Integration后会用Descriptor解析FlowDirection()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 8; y < 15; y++)
            walkable[35 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(9.5f, 0f, 11.5f));
        Vector3 goal = new Vector3(46.5f, 0f, 15.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(5);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 11, out bool leftClear));
        Assert.IsTrue(leftClear, "内部左侧 sector 没有墙和软成本，应保持 clear cost 状态");

        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestReleasedIntegrationTileCount(), 1, "flow tile commit 后应释放完整 integration payload，运行期依赖 flow byte 与摘要");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestDebugIntegrationPayloadTileCount(), "未显式请求 debug rebuild 时不应保留 debug integration payload");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryRebuildEditorDebugTileIntegration(9, 11), "released tile 应支持显式按需重建 debug integration heatmap payload");
        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestDebugIntegrationPayloadTileCount(), 1, "显式 debug rebuild 后应只保留调试 payload，不恢复运行期 integration");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestDebugIntegrationCost(9, 11, out float debugCost));
        Assert.IsFalse(float.IsPositiveInfinity(debugCost), $"debug integration 应能读到有限成本，cost={debugCost}");

        bool foundDescriptorFlow = false;
        for (int y = 8; y < 16 && !foundDescriptorFlow; y++)
        {
            for (int x = 8; x < 16; x++)
            {
                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(x, y, out bool hasLos, out _, out bool pathable));
                if (!pathable || hasLos)
                    continue;

                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(x, y, out Vector2 storedFlow));
                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(x, y, out Vector2 runtimeFlow));
                if (storedFlow.sqrMagnitude <= 0.0001f && runtimeFlow.sqrMagnitude > 0.0001f)
                {
                    foundDescriptorFlow = true;
                    break;
                }
            }
        }

        Assert.IsTrue(foundDescriptorFlow, "clear tile 释放 integration 后应能用 descriptor 解析普通格方向，不再依赖 float integration 或每格固化 flow byte");
    }

    [Test]
    public void UnknownAgentType不会被静默解析成默认移动类型()
    {
        const int width = 4;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(
                MAEntity.UnknownNavAgentTypeId,
                width,
                height,
                1f,
                Vector3.zero,
                walkable));

        StringAssert.Contains("explicit agentTypeId is Unknown", ex.Message);
    }

    [Test]
    public void 宽PortalWindow会按最大宽度拆成多个Graph节点()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.PortalMaxWindowWidthCells = 2;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));

        Assert.AreEqual(2, FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), "4 格宽 sector 边界应按 PortalMaxWindowWidthCells=2 拆成两个 portal graph 节点");
    }

    [Test]
    public void 有墙Sector即使成本清晰也不能当ClearFlowTile跳过方向预写()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        for (int y = 1; y <= 5; y++)
            walkable[3 + y * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(7.5f, 0f, 7.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(0, 0, out bool clearCost));
        Assert.IsFalse(clearCost, "墙边 soft cost blur 会进入 CostField，含墙 sector 不应再被标记为 clear cost field");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearFlowTileState(0, 0, out bool clearFlow));
        Assert.IsFalse(clearFlow, "含墙或不可通行邻接的 sector 不是论文意义上的 clear flow tile");

        bool foundStoredFlow = false;
        for (int y = 0; y < height && !foundStoredFlow; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(x, y, out bool hasLos, out _, out bool pathable));
                if (!pathable || hasLos)
                    continue;

                Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(x, y, out Vector2 storedFlow));
                if (storedFlow.sqrMagnitude > 0.0001f)
                {
                    foundStoredFlow = true;
                    break;
                }
            }
        }

        Assert.IsTrue(foundStoredFlow, "非 clear flow tile 应在构建期写入 stored flow direction");
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
        Assert.Greater(Mathf.Abs(velocity.z), 0.2f, $"高成本带应让 flow/integration 产生绕行分量，velocity={velocity}");
    }

    [Test]
    public void AuthoredCostField会进入CostField并影响积分流场()
    {
        const int width = 7;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        byte[] costs = new byte[width * height];
        for (int i = 0; i < walkable.Length; i++)
        {
            walkable[i] = true;
            costs[i] = 1;
        }

        for (int x = 2; x <= 4; x++)
            costs[x + 2 * width] = 30;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(int.MinValue + 1, width, height, 1f, Vector3.zero, walkable, null, costs);

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out Vector3 velocity));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost));
        Assert.AreEqual(30, cost, "authored source cost 应写入 CostField，而不是运行期退回全 1 成本。");
        Assert.Greater(velocity.x, 0.5f, $"authored 高成本带不应阻断朝目标推进，velocity={velocity}");
        Assert.Greater(Mathf.Abs(velocity.z), 0.2f, $"authored 高成本带应让 flow/integration 产生绕行分量，velocity={velocity}");
    }

    [Test]
    public void CostStamp只影响匹配MovementType的CostField()
    {
        const int width = 9;
        const int height = 7;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1002, 12345, new Vector3(4.5f, 0f, 3.5f), new Vector3(0.49f, 0f, 0.49f), 40);

        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 3.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 3.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 3, out byte cost));
        Assert.AreEqual(1, cost, "不匹配 movement type 的 cost stamp 不应污染当前 agent type 的 CostField");
    }

    [Test]
    public void GridCostStamp会按逐格成本写入CostField()
    {
        const int width = 6;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        byte[] costs =
        {
            5, 7,
            11, 13
        };

        FlowFieldCrowdMovementSystem.RegisterGridCostStamp(1004, new Vector3(2f, 0f, 1f), 1f, 2, 2, costs);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(5.5f, 0f, 1.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 1, out byte cost00));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 1, out byte cost10));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(2, 2, out byte cost01));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 2, out byte cost11));
        Assert.AreEqual(5, cost00, "grid cost stamp 左下格成本应写入 CostField");
        Assert.AreEqual(7, cost10, "grid cost stamp 右下格成本应写入 CostField");
        Assert.AreEqual(11, cost01, "grid cost stamp 左上格成本应写入 CostField");
        Assert.AreEqual(13, cost11, "grid cost stamp 右上格成本应写入 CostField");
    }

    [Test]
    public void AuthoredGridAnchor高度不会隐式转换成CostField成本()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        Vector3[] anchors = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                walkable[index] = true;
                anchors[index] = new Vector3(x + 0.5f, x * 0.5f, y + 0.5f);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable, anchors);
        SimEntityContext ctx = CreateEntity(new Vector3(1.5f, 0f, 4.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(6.5f, 0f, 4.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(4, 4, out byte cost));
        Assert.AreEqual(1, cost, $"authored grid 锚点高度不应作为隐藏坡度成本写入 CostField，cost={cost}");
    }

    [Test]
    public void 清成本Sector会记录ClearCost状态并随CostStamp重建()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 20;
        const int height = 20;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(8.5f, 0f, 9.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(11.5f, 0f, 9.5f), 2f, out _));

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearBefore));
        Assert.IsTrue(leftClearBefore, "没有额外成本的 sector 应标记为 clear cost field");

        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(1003, new Vector3(9.5f, 0f, 9.5f), new Vector3(0.49f, 0f, 0.49f), 20);
        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearAfterStamp));
        Assert.IsFalse(leftClearAfterStamp, "被 cost stamp 写入额外成本的 sector 不应继续标记为 clear");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(13, 9, out bool rightClearAfterStamp));
        Assert.IsTrue(rightClearAfterStamp, "未受 cost stamp 影响的相邻 sector 应保持 clear");

        FlowFieldCrowdMovementSystem.UnregisterCostStamp(1003);
        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorClearCostState(9, 9, out bool leftClearAfterRemove));
        Assert.IsTrue(leftClearAfterRemove, "撤销 cost stamp 并完成 dirty rebuild 后应恢复 clear cost field");
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
        Assert.Less(beforeCost, 255, "测试初始格应是可走 cost");
        Assert.AreEqual(beforeCost, pendingCost, "runtime dirty job 完成前不应把半更新 working world 暴露给导航 world");
        Assert.AreEqual(255, committedCost, "runtime dirty queue 完成后应原子提交运行时障碍到 cost field");
    }

    [Test]
    public void ColliderObstacle注册使用Transform世界几何而非滞后PhysicsBounds()
    {
        const int width = 8;
        const int height = 4;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, new Vector3(7.5f, 0f, 1.5f), 2f, out _));

        GameObject managerObject = new GameObject("GroupMoveManagerTest");
        GroupMoveManager manager = managerObject.AddComponent<GroupMoveManager>();
        GameObject root = new GameObject("BuildingRoot");
        GameObject autoBox = new GameObject("_AutoBox_Test");
        try
        {
            root.transform.position = new Vector3(5f, 0f, 1f);
            autoBox.transform.SetParent(root.transform, false);
            autoBox.transform.localPosition = new Vector3(0.5f, 0f, 0.5f);
            BoxCollider collider = autoBox.AddComponent<BoxCollider>();
            collider.size = Vector3.one;

            manager.RegisterColliderObstacle(collider);

            for (int i = 0; i < 16 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
                FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte worldCellCost));
            Assert.AreEqual(255, worldCellCost, "AutoBox 应按当前 Transform 世界坐标阻塞建筑所在格");
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(0, 0, out byte originCellCost));
            Assert.Less(originCellCost, 255, "AutoBox 不应因滞后的 collider.bounds 误注册到原点附近");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(autoBox);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void 导航查询不会同步DrainRuntimeDirtyJob()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 12;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 1.5f));
        Vector3 goal = new Vector3(11.5f, 0f, 1.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte beforeCost));

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9101, new Vector3(5.5f, 0f, 1.5f), new Vector3(0.49f, 0f, 0.49f));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "注册运行时障碍后应只标记 dirty，等待 runtime queue 分帧提交");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "导航查询不应同步 drain runtime dirty job");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte duringQueryCost));
        Assert.AreEqual(beforeCost, duringQueryCost, "runtime dirty commit 前查询应继续使用已提交的旧 world，而不是同步改写 CostField");
    }

    [Test]
    public void RuntimeDirtyCommit会清理受影响的PendingFieldBuildJob()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        config.SectorSizeInCells = 48;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 96;
        const int height = 48;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 23.5f));
        SimEntityContext target = CreateEntity(new Vector3(95.5f, 0f, 23.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0, "低预算下应留下 pending shared goal job");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "低预算下应留下 pending flow tile job");

        FlowFieldCrowdMovementSystem.RegisterBoxObstacle(9301, new Vector3(47.5f, 0f, 23.5f), new Vector3(0.49f, 0f, 0.49f));
        for (int i = 0; i < 4096; i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), "dirty commit 后受影响的 shared goal job 不能继续提交旧结果");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "dirty commit 后受影响的 flow tile job 不能继续提交旧结果");
    }

    [Test]
    public void RuntimeDirtyQueue重排不会丢失已Pending的DirtySector()
    {
        FlowFieldNavigationConfig config = CreateConfig();
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

        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "runtime dirty job 应完成提交后再验证 CostField，避免读取旧 world");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(5, 1, out byte firstCost));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(25, 1, out byte secondCost));
        Assert.AreEqual(255, firstCost, "pending job 被新 dirty 重排时，旧 dirty sector 不能丢失");
        Assert.AreEqual(255, secondCost, "新 dirty sector 也必须进入重排后的 runtime dirty job");
    }

    [Test]
    public void RuntimeDirtyQueue会分帧重建IslandField()
    {
        FlowFieldNavigationConfig config = CreateConfig();
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
        FlowFieldNavigationConfig config = CreateConfig();
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

        for (int i = 0; i < 2048 && FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(), "world build queue 应在预算帧内最终完成");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.HasEditorTestWorld(), "完成后才应原子提交 world");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out int islandId, out int islandCount));
        Assert.AreEqual(1, islandId);
        Assert.AreEqual(1, islandCount);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPortalCount(), 0, "full world build 完成后应具备 portal graph");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount(), 0, "portal access integration 应由 world build 阶段预构建，不应等查询热路径同步生成");
    }

    [Test]
    public void PortalAccessCache使用压缩势能且保持下坡成本()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[3 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        int portalAccessCount = FlowFieldCrowdMovementSystem.GetEditorTestSectorPortalAccessCacheCount();
        Assert.Greater(portalAccessCount, 0, "world build 应预构建 sector portal access 场");
        Assert.AreEqual(portalAccessCount, FlowFieldCrowdMovementSystem.GetEditorTestQuantizedSectorPortalAccessCacheCount(), "portal access cache 不应长期保留 float integration，应全部转为压缩势能");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestFirstPortalForSector(0, out int portalId, out int oppositeSectorId));
        Assert.AreEqual(1, oppositeSectorId);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCost(0, portalId, 0, 3, out float farCost));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCost(0, portalId, 6, 3, out float nearCost));
        Assert.IsFalse(float.IsPositiveInfinity(farCost), $"远端可走格应能解码为有限 portal access cost，cost={farCost}");
        Assert.IsFalse(float.IsPositiveInfinity(nearCost), $"portal 附近可走格应能解码为有限 portal access cost，cost={nearCost}");
        Assert.Less(nearCost, farCost, $"压缩后仍必须保持朝 portal 下坡的成本关系，near={nearCost} far={farCost}");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorPortalAccessCost(0, portalId, 3, 3, out float blockedCost));
        Assert.IsTrue(float.IsPositiveInfinity(blockedCost), $"不可走格应保持 INF，不应被压缩成有限成本，cost={blockedCost}");
    }

    [Test]
    public void CommittedCostField按SectorChunk存储而不是整图数组()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;
        walkable[3 + 3 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestCommittedFullCostField(), "committed world 不应长期保留整图 CostField");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestCommittedSectorCostChunkCount(), 0, "包含障碍/软成本的 sector 应保存局部 cost chunk");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(10, 3, out byte clearCost));
        Assert.AreEqual(1, clearCost, "clear sector 应通过静态语义读出普通成本 1");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCostFieldValue(3, 3, out byte blockedCost));
        Assert.AreEqual(255, blockedCost, "chunk sector 内不可走格应保持硬阻挡成本 255");
    }

    [Test]
    public void IslandField会为单IslandSector记录UniformIslandId()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 4;
        const int height = 3;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        for (int i = 0; i < 32 && !FlowFieldCrowdMovementSystem.HasEditorTestWorld(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorUniformIslandId(0, 0, out int uniformIslandId));
        Assert.AreEqual(1, uniformIslandId, "单 island sector 应记录统一 island id，避免每次都查格子 island field");

        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();
        FlowFieldCrowdMovementSystem.ClearEditorTestClock();
        FlowFieldCrowdMovementSystem.SetConfig(config);

        bool[] splitWalkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            SetWalkable(splitWalkable, width, 0, y);
            SetWalkable(splitWalkable, width, 3, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, splitWalkable);
        for (int i = 0; i < 32 && !FlowFieldCrowdMovementSystem.HasEditorTestWorld(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestIslandFieldValue(0, 0, out _, out int islandCount));
        Assert.AreEqual(2, islandCount, "测试地图应在同一个 sector 内形成两个 island");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestSectorUniformIslandId(0, 0, out int mixedIslandId));
        Assert.AreEqual(-1, mixedIslandId, "混合 island sector 必须保留逐格 island field 判断");
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
    public void 不同MovementType可以使用独立AuthoredWalkableMask()
    {
        const int width = 5;
        const int height = 3;
        bool[] smallWalkable = new bool[width * height];
        bool[] largeWalkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(smallWalkable, width, x, 1);
            SetWalkable(largeWalkable, width, x, 1);
        }

        largeWalkable[2 + 1 * width] = false;
        int smallAgentType = AgentTypeHelper.SmallMovementTypeId;
        int largeAgentType = AgentTypeHelper.LargeMovementTypeId;
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(smallAgentType, width, height, 1f, Vector3.zero, smallWalkable, null),
            new AuthoredNavigationSourceData(largeAgentType, width, height, 1f, Vector3.zero, largeWalkable, null)
        });

        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(smallAgentType, 0.35f);
        FlowFieldCrowdMovementSystem.SetEditorTestAgentTypeRadius(largeAgentType, 0.75f);

        SimEntityContext small = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, smallAgentType);
        SimMoveExecutor smallExecutor = small.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(smallExecutor, "small 测试实体应使用 SimMoveExecutor");

        SimEntityContext large = CreateEntity(new Vector3(0.5f, 0f, 1.5f), false, largeAgentType);
        SimMoveExecutor largeExecutor = large.MoveExecutor as SimMoveExecutor;
        Assert.IsNotNull(largeExecutor, "large 测试实体应使用 SimMoveExecutor");

        Vector3 goal = new Vector3(4.5f, 0f, 1.5f);
        for (int frame = 1; frame <= 30; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(small, goal, 2f, out Vector3 smallVelocity));
            smallExecutor.SetInput(smallVelocity);
            smallExecutor.Execute(0.1f);
            small.SyncPositionFromExecutor();

            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(large, goal, 2f, out Vector3 largeVelocity));
            largeExecutor.SetInput(largeVelocity);
            largeExecutor.Execute(0.1f);
            large.SyncPositionFromExecutor();
        }

        Assert.Greater(small.Position.x, 2.5f, $"small movement type 应能穿过自己的 authored mask 通道，pos={small.Position}");
        Assert.Less(large.Position.x, 2.0f, $"large movement type 应使用自己的封闭 authored mask，不能复用 small mask 穿过封闭格，pos={large.Position}");
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
    public void 小型单位刚进入安全间距时避让不应反转主路径方向()
    {
        const int width = 80;
        const int height = 20;
        const float cellSize = 0.09f;
        const float radius = 0.18f;
        const float speed = 4.2f;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, cellSize, Vector3.zero, walkable);
        SimEntityContext front = CreateEntity(new Vector3(2.00f, 0f, 0.90f), false, 0, radius);
        SimEntityContext rear = CreateEntity(new Vector3(1.63f, 0f, 0.90f), false, 0, radius);
        Vector3 goal = new Vector3(6.5f, 0f, 0.90f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(front, goal, speed, out Vector3 frontVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(rear, goal, speed, out Vector3 rearVelocity));

        Vector3 forward = Vector3.right;
        float rearForwardSpeed = Vector3.Dot(rearVelocity, forward);
        Assert.Greater(rearForwardSpeed, speed * 0.6f,
            $"小型单位仅刚进入安全间距时仍应沿主路径前进，不能被避让反转。frontVelocity={frontVelocity}, rearVelocity={rearVelocity}, rearForward={rearForwardSpeed:F3}");
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
                if (Mathf.Abs(leaderVelocity.z) > 0.001f)
                    Assert.AreEqual(initialLeaderSign, Mathf.Sign(leaderVelocity.z), $"领头单位侧偏方向不应在瓶颈前后抖动，frame={frame}, vel={leaderVelocity}");
                if (Mathf.Abs(followerVelocity.z) > 0.001f)
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
        for (int frame = 1; frame <= 64; frame++)
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
        FlowFieldCrowdMovementSystem.SetAgentState(followHigh.GetHashCode(), FlowFieldAgentState.Follow);
        FlowFieldCrowdMovementSystem.SetAgentState(followLow.GetHashCode(), FlowFieldAgentState.Idle);
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
    public void 动态障碍改变连通性后会经RuntimeQueue重建Island()
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
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out _, out string pendingReason),
            pendingReason);
        StringAssert.Contains("runtime dirty pending", pendingReason);

        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
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
        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.TryResolveNearestReachableGoal(ctx, farGoal, 2f, out _, out string pendingReason),
            pendingReason);
        StringAssert.Contains("runtime dirty pending", pendingReason);

        for (int i = 0; i < 32 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
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
    public void 同SectorPathHandle不应在查询热路径同步构建Integration()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(6.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        int integrationsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));

        Assert.Greater(velocity.x, 0.5f, $"同 sector 目标应直接使用局部连通判定后构建 tile，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "同 sector final tile 也不应在查询热路径同步构建");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "同 sector final tile 应进入预算队列");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSource(chaser.GetHashCode(), out int source, out bool hasLineOfSight));
        Assert.AreEqual(3, source, $"tile pending 时应使用 PendingFinalGoal 临时方向，source={source}");
        Assert.IsFalse(hasLineOfSight, "PendingFinalGoal 不是已构建 final tile 的 LOS 结果");
        Assert.AreEqual(
            integrationsBefore,
            FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount(),
            "同 sector path handle 构建不应再同步跑整块 sector integration");
    }

    [Test]
    public void 首次跨Sector查询不应同步构建SharedGoalFieldIntegration()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(0.5f, 0f, 5.5f));
        SimEntityContext target = CreateEntity(new Vector3(23.5f, 0f, 3.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        int integrationsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out Vector3 firstVelocity));
        int integrationsAfterFirst = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out Vector3 secondVelocity));
        int integrationsAfterSecond = FlowFieldCrowdMovementSystem.GetEditorTestFrameSynchronousSectorIntegrationCount();

        Assert.Greater(firstVelocity.x, 0.5f, $"第一个追击者应朝跨 sector 目标前进 first={firstVelocity}");
        Assert.Greater(secondVelocity.x, 0.5f, $"第二个追击者应朝跨 sector 目标前进 second={secondVelocity}");
        Assert.AreEqual(integrationsBefore, integrationsAfterFirst, "首次跨 sector 查询应只跑轻量 portal graph，不应同步构建 SharedGoalField integration");
        Assert.AreEqual(integrationsAfterFirst, integrationsAfterSecond, "同一帧后续追击者也不应在查询热路径构建 SharedGoalField integration");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), 0, "SharedGoalField 应进入预算队列，由后续 ProcessFlowTileBuildQueue 分帧完成");
    }

    [Test]
    public void 查询热路径不应同步构建非末端PortalTile链()
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
        Assert.AreEqual(0, tileCount, $"查询热路径不应同步构建 portal tile 链，tileCount={tileCount}");
        Assert.AreEqual(0, tileBuildCount, $"查询热路径不应同步 build tile，tileBuilds={tileBuildCount}");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "portal tile 链应进入预算队列");
    }

    [Test]
    public void PortalGraph不应为了复用旧Path牺牲更短Portal()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(walkable, width, x, 5);
            SetWalkable(walkable, width, x, 18);
        }

        for (int y = 5; y <= 18; y++)
        {
            SetWalkable(walkable, width, 1, y);
            SetWalkable(walkable, width, 22, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext upperChaser = CreateEntity(new Vector3(1.5f, 0f, 5.5f));
        SimEntityContext lowerChaser = CreateEntity(new Vector3(1.5f, 0f, 18.5f));
        SimEntityContext target = CreateEntity(new Vector3(22.5f, 0f, 11.5f));
        upperChaser.TargetComp = new SimTargetingComp(upperChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        lowerChaser.TargetComp = new SimTargetingComp(lowerChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(upperChaser, target.Position, 2f, out Vector3 upperVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(upperChaser.GetHashCode(), out int[] upperPortals));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(lowerChaser, target.Position, 2f, out Vector3 lowerVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(lowerChaser.GetHashCode(), out int[] lowerPortals));

        Assert.Greater(upperVelocity.x, 0.5f, $"上路单位应建立可用 path，upper={upperVelocity}");
        Assert.Greater(lowerVelocity.x, 0.5f, $"下路单位应建立可用 path，lower={lowerVelocity}");
        Assert.AreNotEqual(upperPortals[0], lowerPortals[0], $"第二个单位应选择自身最近的下路 portal，而不是 merge 到第一个单位旧 path。upper=[{string.Join(",", upperPortals)}] lower=[{string.Join(",", lowerPortals)}]");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFramePortalGraphMergeHitCount(), "portal graph 不应再用提前 merge 改变最短路选择");
    }

    [Test]
    public void PortalGraph会在不牺牲路径代价时合并到既有PathSuffix()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
            SetWalkable(walkable, width, x, 3);

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext first = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext second = CreateEntity(new Vector3(2.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(31.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(first, goal, 2f, out Vector3 firstVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(first.GetHashCode(), out int[] firstPortals));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(second, goal, 2f, out Vector3 secondVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(second.GetHashCode(), out int[] secondPortals));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(second.GetHashCode(), out string buildSource));

        Assert.Greater(firstVelocity.x, 0.5f, $"第一个单位应建立可用 path，velocity={firstVelocity}");
        Assert.Greater(secondVelocity.x, 0.5f, $"第二个单位应沿合并后的 path 推进，velocity={secondVelocity}");
        Assert.AreEqual("portalGraphMerged", buildSource, $"第二个单位应通过 merging A* 拼接既有 path suffix，source={buildSource}");
        Assert.AreEqual(firstPortals[firstPortals.Length - 1], secondPortals[secondPortals.Length - 1], $"合并后的尾段应复用同一终点 portal，first=[{string.Join(",", firstPortals)}] second=[{string.Join(",", secondPortals)}]");
        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestFramePortalGraphMergeHitCount(), 1, "merge 命中应计入 portalGraphMergeHits");
    }

    [Test]
    public void 移动目标换格后会重建PortalPath而不是沿旧Portal链绕远()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 24;
        bool[] walkable = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            SetWalkable(walkable, width, x, 5);
            SetWalkable(walkable, width, x, 18);
        }

        for (int y = 5; y <= 18; y++)
        {
            SetWalkable(walkable, width, 1, y);
            SetWalkable(walkable, width, 22, y);
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 18.5f));
        Vector3 upperGoal = new Vector3(22.5f, 0f, 5.5f);
        Vector3 lowerGoal = new Vector3(22.5f, 0f, 18.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, upperGoal, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.GetHashCode(), out int[] upperPortals));

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, lowerGoal, 2f, out Vector3 lowerVelocity));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.GetHashCode(), out int[] lowerPortals));

        Assert.AreNotEqual(upperPortals[0], lowerPortals[0], $"目标换到下路后应重建 portal path，而不是继续沿旧上路 portal。upper=[{string.Join(",", upperPortals)}] lower=[{string.Join(",", lowerPortals)}]");
        Assert.Greater(lowerVelocity.x, 0.5f, $"重建后仍应沿下路推进，velocity={lowerVelocity}");
    }

    [Test]
    public void 长路径PendingTile时不应直奔最终目标()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(31.5f, 0f, 5.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"长路径 pending 阶段仍应朝下游 portal 前进，velocity={velocity}");

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSource(chaser.GetHashCode(), out _, out bool hasLineOfSight));
        Assert.IsFalse(hasLineOfSight, "多 sector 长路径的 pending 方向不应利用最终目标 LOS 直奔目标；论文语义要求先朝 next portal 前进");
    }

    [Test]
    public void FlowTileBuildQueue会在后续查询前预构建路径Tile链()
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

        int queuedTileCommitCount = 0;
        for (int frame = 2; frame < 64 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() <= 1; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            int cacheCountBefore = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            queuedTileCommitCount += FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() - cacheCountBefore;
        }

        int queuedTileCount = FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount();
        Assert.Greater(queuedTileCount, 1, $"flow tile queue 应能在后续查询前预构建下游 tile 链，tileCount={queuedTileCount}");
        Assert.AreEqual(queuedTileCount, queuedTileCommitCount, $"预构建 tile 数应等于实际提交数，tileCount={queuedTileCount} commits={queuedTileCommitCount}");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(3, 0.3f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"预构建 tile 命中后仍应沿走廊前进，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "查询命中预构建 tile 后当前查询帧不应再次构建 tile");
    }

    [Test]
    public void NavigationRequest会在Move前提交路径Tile链()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 goal = new Vector3(23.5f, 0f, 3.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryPrepareNavigationRequest(chaser, goal, out string failureReason), failureReason);
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), 0, "path request 应立即提交 flow tile build jobs");

        for (int frame = 2; frame < 64 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() <= 1; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 1, "Move 前提交的请求应能被队列预构建为路径 tile 链");

        FlowFieldCrowdMovementSystem.SetEditorTestClock(64, 6.4f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out Vector3 velocity));
        Assert.Greater(velocity.x, 0.5f, $"预提交请求后 steering 应命中 flow 链继续前进，velocity={velocity}");
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestFrameTileBuildCount(), "预提交命中后 steering 查询帧不应同步构建 tile");
    }

    [Test]
    public void FlowTileBuildQueue低预算会保留未完成TileJob()
    {
        FlowFieldNavigationConfig config = CreateConfig();
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
    public void 移动目标旧TileJob不会淹没当前活动Tile()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 4;
        config.RuntimeRebuildBudgetMilliseconds = 0.01f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 80;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(79.5f, 0f, 7.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        for (int frame = 1; frame <= 24; frame++)
        {
            target.Position = new Vector3(79.5f - frame * 0.5f, 0f, 4.5f + frame % 7);
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        }

        int staleQueueCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        Assert.Greater(staleQueueCount, 16, "测试必须先制造足够多的旧移动目标 pending tile jobs，才能覆盖真实日志里的队列淹没根因。");

        target.Position = new Vector3(79.5f, 0f, 7.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(32, 3.2f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        int currentQueueIndex = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildQueueIndex(chaser.GetHashCode());
        int prunedQueueCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        Assert.LessOrEqual(currentQueueIndex, 8,
            $"当前活动单位所需 tile 必须被提升到队列前部，不能被旧移动目标 tile jobs 淹没。index={currentQueueIndex}, before={staleQueueCount}, after={prunedQueueCount}");
        Assert.Less(prunedQueueCount, staleQueueCount,
            $"处理活动队列时应剪掉不再被任何活动 path 引用的旧移动目标 tile jobs。before={staleQueueCount}, after={prunedQueueCount}");
    }

    [Test]
    public void FlowTileCache不会淘汰ActivePath引用的Tile()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.FlowTileCacheLimit = 16;
        config.RuntimeRebuildBudgetMilliseconds = 1.5f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 96;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        SimEntityContext target = CreateEntity(new Vector3(95.5f, 0f, 3.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        for (int frame = 2; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount(), "长路径 flow tile chain 应在预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount(), 16, "active path 引用中的 flow tile 不能为了满足 cache limit 被淘汰");
    }

    [Test]
    public void PortalChoice诊断不应构建SharedGoalField()
    {
        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(0.5f, 0f, 3.5f));
        Vector3 sameSectorGoal = new Vector3(6.5f, 0f, 3.5f);
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, sameSectorGoal, 2f, out _));
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), "同 sector 请求不应创建 SharedGoalField");

        string diagnostics = FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceDiagnostics(0, 2, 0, 3, 23, 3, 0);
        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), $"portal choice 诊断应只读缓存，不应构建 SharedGoalField，diag={diagnostics}");
        StringAssert.Contains("shared-field-not-cached", diagnostics);
    }

    [Test]
    public void FlowTileBuildQueue应按TileKey去重而不是按PathHandle去重()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.RuntimeRebuildBudgetMilliseconds = 0.05f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 48;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext firstChaser = CreateEntity(new Vector3(0.5f, 0f, 7.5f));
        SimEntityContext secondChaser = CreateEntity(new Vector3(1.5f, 0f, 7.5f));
        SimEntityContext target = CreateEntity(new Vector3(47.5f, 0f, 7.5f));
        firstChaser.TargetComp = new SimTargetingComp(firstChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };
        secondChaser.TargetComp = new SimTargetingComp(secondChaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(firstChaser, target.Position, 2f, out _));
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(secondChaser, target.Position, 2f, out _));

        FlowFieldCrowdMovementSystem.ClearEditorTestFlowTileCache();
        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        int pendingCount = FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount();
        Assert.Greater(pendingCount, 0, "低预算下应留下未完成 tile job");
        int duplicateCount = FlowFieldCrowdMovementSystem.GetEditorTestDuplicatePendingFlowTileBuildKeyCount();
        Assert.AreEqual(0, duplicateCount, $"同一 tile key 不应因不同 PathHandleId 重复排队，pending={pendingCount} duplicate={duplicateCount}");
    }

    [Test]
    public void SharedGoalFieldBuildQueue低预算会保留并完成Job()
    {
        FlowFieldNavigationConfig config = CreateConfig();
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

        FlowFieldCrowdMovementSystem.SetEditorTestClock(2, 0.2f);
        int sharedBuildsBefore = FlowFieldCrowdMovementSystem.GetEditorTestFrameSharedGoalFieldBuildCount();
        FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

        Assert.GreaterOrEqual(FlowFieldCrowdMovementSystem.GetEditorTestFrameSharedGoalFieldBuildCount(), sharedBuildsBefore, "shared goal field queue 应接入 flow rebuild 预算入口");

        for (int frame = 3; frame < 256 && FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount() > 0; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }

        Assert.AreEqual(0, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount(), "shared goal field queue 应在后续预算帧内完成");
        Assert.Greater(FlowFieldCrowdMovementSystem.GetEditorTestSharedGoalFieldCacheCount(), 0, "完成后应提交 SharedGoalField cache");
    }

    [Test]
    public void PortalTile会在构建期缓存PortalTarget()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(8.5f, 0f, 8.5f));
        SimEntityContext target = CreateEntity(new Vector3(26.5f, 0f, 8.5f));
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { target })
        {
            CurrentTarget = target
        };

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, target.Position, 2f, out _));

        ProcessFlowTileBuildQueueUntilPortalTargetReady(8, 8, out Vector3 portalTarget, out int visibleCount, out int selectedPair, out bool usedOppositeCenter);
        Assert.Greater(visibleCount, 0, $"portal tile 应在构建期缓存可见 portal 候选，target={portalTarget}");
        Assert.GreaterOrEqual(selectedPair, 0, $"portal tile 应缓存选中的 portal 槽位，target={portalTarget}");
        Assert.IsFalse(usedOppositeCenter, "直走廊 portal target 不应退化成 opposite center");
        Assert.Greater(portalTarget.x, chaser.Position.x, $"portal target 应指向下游 portal 对侧，target={portalTarget}");
    }

    [Test]
    public void PortalTargetLineOfSight会被软成本截断()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 32;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(2001, new Vector3(10.5f, 0f, 7.5f), new Vector3(0.49f, 0f, 4f), 60);

        SimEntityContext chaser = CreateEntity(new Vector3(8.5f, 0f, 8.5f));
        Vector3 goal = new Vector3(26.5f, 0f, 8.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out _));

        ProcessFlowTileBuildQueueUntilPortalTargetReady(8, 8, out Vector3 portalTarget, out int visibleCount, out int selectedPair, out bool usedOppositeCenter);
        Assert.AreEqual(0, visibleCount, $"论文 LOS pass 碰到 cost > 1 应停止，target={portalTarget}");
        Assert.GreaterOrEqual(selectedPair, 0, $"LOS 候选被截断后仍应由 integration 追踪实际 portal 槽位，target={portalTarget}");
        Assert.IsFalse(usedOppositeCenter, "LOS 截断不应强制退到 portal center，否则会覆盖 integration 已知的更优槽位");
    }

    [Test]
    public void PortalTarget无Los但Integration可达时应绑定实际槽位()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 24;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        FlowFieldCrowdMovementSystem.RegisterBoxCostStamp(2002, new Vector3(4.5f, 0f, 3.5f), new Vector3(0.49f, 0f, 4f), 60);

        SimEntityContext chaser = CreateEntity(new Vector3(1.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(18.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out _));

        ProcessFlowTileBuildQueueUntilPortalTargetReady(1, 2, out Vector3 portalTarget, out int visibleCount, out int selectedPair, out bool usedOppositeCenter);
        Assert.AreEqual(0, visibleCount, $"软成本应截断当前侧 portal LOS，target={portalTarget}");
        Assert.GreaterOrEqual(selectedPair, 0, $"无 LOS 但 integration 可达时应追踪到实际 portal 槽位，target={portalTarget}");
        Assert.IsFalse(usedOppositeCenter, $"不能退到 portal center，否则会把宽 portal 目标拉偏，target={portalTarget}");
    }

    [Test]
    public void PortalLos格必须保留IntegrationFlow()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 16;
        const int height = 16;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext chaser = CreateEntity(new Vector3(6.5f, 0f, 6.5f));
        Vector3 goal = new Vector3(10.5f, 0f, 6.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilPortalTargetReady(6, 6, out _, out _, out _, out _);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlags(6, 6, out bool hasLos, out _, out bool pathable));
        Assert.IsTrue(pathable, "portal tile 当前格应可走");
        Assert.IsTrue(hasLos, "portal tile 当前格仍应保留 LOS flag，供运行时直视成立时直接转向");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellStoredFlowDirection(6, 6, out Vector2 storedFlow));
        Assert.Greater(storedFlow.sqrMagnitude, 0.0001f, $"portal LOS 格不是 portal goal cell 时必须存储 integration flow，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(6, 6)}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(6, 6, out Vector2 runtimeFlow));
        Assert.Greater(runtimeFlow.sqrMagnitude, 0.0001f, $"运行期 portal LOS 目标不成立时必须能回到 integration flow，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(6, 6)}");
    }

    [Test]
    public void LosPass不会把L型墙体背后标成LineOfSight()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

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

        SimEntityContext ctx = CreateEntity(new Vector3(0.5f, 0f, 0.5f));
        Vector3 goal = new Vector3(2.5f, 0f, 2.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(5, 6, out bool hiddenLos, out _));
        Assert.IsFalse(hiddenLos, "L 型墙体背后的格子不应被 LOS wavefront 绕过去标成直视");
    }

    [Test]
    public void LosPass不能把严格格线不可见的格标成LineOfSight()
    {
        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = 8;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const int width = 8;
        const int height = 8;
        bool[] walkable = new bool[width * height];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = true;

        walkable[2 + 1 * width] = false;
        walkable[3 + 1 * width] = false;
        walkable[2 + 2 * width] = false;

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);

        SimEntityContext ctx = CreateEntity(new Vector3(4.5f, 0f, 2.5f));
        Vector3 goal = new Vector3(1.5f, 0f, 1.5f);

        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, 0.1f);
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(ctx, goal, 2f, out _));
        ProcessFlowTileBuildQueueUntilTileCount(1);

        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellLineOfSightState(4, 2, out bool hasLos, out _));
        Assert.IsFalse(hasLos, $"严格格线被障碍截断的格子不能只因 LOS flood 可达就标成 LOS，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(4, 2)}");
        Assert.IsTrue(FlowFieldCrowdMovementSystem.TryGetEditorTestCachedTileCellFlowDirection(4, 2, out Vector2 flow));
        Assert.Greater(flow.sqrMagnitude, 0.0001f, $"非 LOS 可达格必须由 integration 写入 flow，diag={FlowFieldCrowdMovementSystem.GetEditorTestCachedTileCellDiagnostic(4, 2)}");
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
        ProcessFlowTileBuildQueueUntilTileCount(2);

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

    [Test]
    public void 贴墙手动位移约束不应翻转到远离输入方向()
    {
        const int width = 5;
        const int height = 5;
        bool[] walkable = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (y >= 1)
                    SetWalkable(walkable, width, x, y);
            }
        }

        FlowFieldCrowdMovementSystem.SetEditorTestNavigationSource(width, height, 1f, Vector3.zero, walkable);
        ProcessWorldBuildQueueUntilReady();

        Vector3 position = new Vector3(2.5f, 0f, 1.35f);
        Vector3 desired = new Vector3(0.2f, 0f, -0.2f);
        Assert.IsTrue(
            FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                position,
                desired,
                0,
                0.35f,
                out Vector3 constrained),
            "贴墙手动位移约束应成功。");

        Assert.Greater(
            Vector3.Dot(desired.normalized, constrained.normalized),
            0.15f,
            $"约束不能把输入翻到近乎垂直或反向。desired={desired} constrained={constrained}");
        Assert.LessOrEqual(
            constrained.z,
            0.001f,
            $"贴下侧墙输入右下时，约束不应翻成右上。desired={desired} constrained={constrained}");
    }

    [Test]
    public void Lv3真实坏点接敌链路不应在PendingPortal阶段朝建筑正面走()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid, "真实坏点回归必须直接使用 Lv3_FlowNavigationGrid_Small.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Small.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Small.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const int startX = 758;
        const int startY = 36;
        const int goalX = 832;
        const int goalY = 37;
        Vector3 start = grid.GetCellAnchor(startX, startY);
        Vector3 goal = grid.GetCellAnchor(goalX, goalY);
        int startSectorId = ResolveDerivedSectorId(derivedData, startX, startY);
        int goalSectorId = ResolveDerivedSectorId(derivedData, goalX, goalY);

        SimEntityContext hero = CreateEntity(goal, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;
        SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, 0.45f);
        chaser.Side = SideType.EnemySide;
        chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)80f);
        chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
        {
            CurrentTarget = hero
        };

        CharacterMoveComp moveComp = new CharacterMoveComp();
        moveComp.Init(chaser, grid.AgentTypeId);
        chaser.MoveComp = moveComp;
        SimMoveExecutor executor = new SimMoveExecutor
        {
            Position = chaser.Position,
            ApplyNavigationConstraint = true,
            AgentTypeId = grid.AgentTypeId,
            EdgeClearance = 0.5f
        };
        chaser.MoveExecutor = executor;

        const float dt = 0.1f;
        FlowFieldCrowdMovementSystem.SetEditorTestClock(1, dt);
        moveComp.MoveTo(hero.Position);
        moveComp.Move(dt);
        executor.Execute(dt);
        chaser.SyncPositionFromExecutor();

        string diagnostics = BuildLv3BadPointDiagnostics(
            grid,
            derivedData,
            chaser,
            executor,
            startX,
            startY,
            goalX,
            goalY,
            startSectorId,
            goalSectorId);

        Assert.IsTrue(executor.LastConstraintSucceeded, $"真实链路的导航约束不应失败。\n{diagnostics}");
        Assert.Greater(executor.LastDesiredDisplacement.x, Mathf.Abs(executor.LastDesiredDisplacement.z) * 1.5f,
            $"目标几乎在正东，PendingPortal 阶段不应先给出朝建筑正面/南侧的主方向。\n{diagnostics}");
        Assert.Greater(executor.LastConstrainedDisplacement.x, 0.12f,
            $"真实 MoveExecutor 约束后仍应有明显向目标前进的位移，而不是被投影回原地。\n{diagnostics}");
    }

    [Test]
    public void Lv3右下角返程链路中敌兵追击返程后不应在建筑夹角长时间聚团停滞()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        config.CrowdPredictionTime = 0.35f;
        config.LaneBiasStrength = 0.18f;
        config.BoundaryAvoidanceWeight = 0.6f;
        config.BottleneckInfluenceDistance = 1.6f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        const float dt = 0.1f;
        const float heroSpeed = 2.4f;
        const float chaserSpeed = 3.5f;
        Fix64 chaserSpeedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
        System.Text.StringBuilder setupDiagnostics = new System.Text.StringBuilder();
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, setupDiagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 969000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实右下角返程回归必须先提交研发中心 Autobox runtime dirty。");

        const float agentRadius = 0.45f;
        route.HeroStart = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.HeroStart, 5f, agentRadius, "return-route-hero-start", setupDiagnostics);
        route.LowerApproach = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.LowerApproach, 5f, agentRadius, "return-route-lower-approach", setupDiagnostics);
        route.RightBottomCorner = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.RightBottomCorner, 6f, agentRadius, "return-route-right-bottom", setupDiagnostics);
        route.ReturnPoint = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ReturnPoint, 5f, agentRadius, "return-route-return", setupDiagnostics);
        for (int i = 0; i < route.ChaserStarts.Length; i++)
            route.ChaserStarts[i] = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ChaserStarts[i], 4f, agentRadius, "return-route-chaser-" + i, setupDiagnostics);

        SimEntityContext hero = CreateEntity(route.HeroStart, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;

        SimEntityContext[] interns = new SimEntityContext[route.ChaserStarts.Length];
        for (int i = 0; i < interns.Length; i++)
            interns[i] = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, 0.45f);

        var allEntities = new System.Collections.Generic.List<IEntityContext>();
        allEntities.Add(hero);
        for (int i = 0; i < interns.Length; i++)
            allEntities.Add(interns[i]);

        var brains = new SoldierAIBrain[interns.Length];
        for (int i = 0; i < interns.Length; i++)
        {
            interns[i].Side = SideType.EnemySide;
            interns[i].SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            interns[i].TargetComp = new SimTargetingComp(interns[i], allEntities)
            {
                CurrentTarget = hero,
                AggroRange = 32f,
                ForgetRange = 48f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(interns[i], grid.AgentTypeId);
            interns[i].MoveComp = moveComp;
            interns[i].MoveExecutor = new SimMoveExecutor
            {
                Position = interns[i].Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            brains[i] = new SoldierAIBrain();
            brains[i].DetectEnemyRange = 32f;
            brains[i].WeaponRange = 0.75f;
            brains[i].SetBirthPosition(interns[i].Position);
            brains[i].Inject();
            interns[i].Brain = brains[i];
            EntityRegistry.Register(interns[i]);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            route.LowerApproach,
            route.RightBottomCorner,
            route.RightBottomCorner,
            route.ReturnPoint,
        };
        int heroWaypointIndex = 0;
        int cornerHoldFrames = 0;
        int[] consecutiveZeroFrames = new int[interns.Length];
        int[] maxConsecutiveZeroFrames = new int[interns.Length];
        Vector3[] lastVelocity = new Vector3[interns.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[interns.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[interns.Length];
        Vector3[] lastPositions = new Vector3[interns.Length];
        for (int i = 0; i < interns.Length; i++)
            lastPositions[i] = interns[i].Position;

        int overlapFrames = 0;
        int cornerSlowFramesAfterReturn = 0;
        int maxCornerSlowStreak = 0;
        int currentCornerSlowStreak = 0;
        int framesWithChaserNearCorner = 0;
        int maxCornerOccupancyAfterReturn = 0;
        float minReturnDistanceToHero = float.PositiveInfinity;
        float minDistanceToCorner = float.PositiveInfinity;
        System.Text.StringBuilder timeline = new System.Text.StringBuilder();
        timeline.Append(setupDiagnostics);

        for (int frame = 1; frame <= 520; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 heroWaypoint = heroWaypoints[heroWaypointIndex];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                heroWaypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                0.5f,
                timeline);
            float heroWaypointDistanceSqr = (hero.Position - heroWaypoint).sqrMagnitude;
            bool reachedHeroWaypoint = heroWaypointDistanceSqr <= 0.0025f;
            if (heroWaypointIndex == 1 || heroWaypointIndex == 2)
                reachedHeroWaypoint = heroWaypointDistanceSqr <= 0.75f * 0.75f;

            if (reachedHeroWaypoint && heroWaypointIndex < heroWaypoints.Length - 1)
            {
                if (heroWaypointIndex == 1 && cornerHoldFrames < 35)
                    cornerHoldFrames++;
                else
                {
                    cornerHoldFrames = 0;
                    heroWaypointIndex++;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                interns,
                frame,
                dt,
                consecutiveZeroFrames,
                maxConsecutiveZeroFrames,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            float movedThisFrame = 0f;
            for (int i = 0; i < interns.Length; i++)
            {
                float actualMoved = Vector3.Distance(interns[i].Position, lastPositions[i]);
                movedThisFrame += actualMoved;
                lastPositions[i] = interns[i].Position;
                if (heroWaypointIndex >= 3)
                    minReturnDistanceToHero = Mathf.Min(minReturnDistanceToHero, Vector3.Distance(interns[i].Position, hero.Position));
                minDistanceToCorner = Mathf.Min(minDistanceToCorner, Vector3.Distance(interns[i].Position, route.RightBottomCorner));
            }

            for (int i = 0; i < interns.Length; i++)
            for (int j = i + 1; j < interns.Length; j++)
                if (Vector3.Distance(interns[i].Position, interns[j].Position) < 0.55f)
                    overlapFrames++;

            bool afterHeroReturn = heroWaypointIndex >= 3;
            float heroCornerDistance = Vector3.Distance(hero.Position, route.RightBottomCorner);
            bool afterHeroLeftCorner = afterHeroReturn && heroCornerDistance > 4.5f;
            int cornerOccupancy = 0;
            for (int i = 0; i < interns.Length; i++)
            {
                if (IsInsideLv3RightBottomCornerWindow(interns[i].Position, route.RightBottomCorner))
                {
                    cornerOccupancy++;
                }
            }

            if (cornerOccupancy > 0)
                framesWithChaserNearCorner++;
            if (afterHeroLeftCorner)
                maxCornerOccupancyAfterReturn = Mathf.Max(maxCornerOccupancyAfterReturn, cornerOccupancy);

            bool cornerSlow = afterHeroLeftCorner && cornerOccupancy > 0 && movedThisFrame < 0.45f;
            if (cornerSlow)
            {
                cornerSlowFramesAfterReturn++;
                currentCornerSlowStreak++;
                maxCornerSlowStreak = Mathf.Max(maxCornerSlowStreak, currentCornerSlowStreak);
            }
            else
            {
                currentCornerSlowStreak = 0;
            }

            if (frame % 20 == 0 || cornerSlow)
            {
                int sampleStart = timeline.Length;
                timeline.Append("frame=").Append(frame)
                    .Append(" hero=").Append(hero.Position)
                    .Append(" waypoint=").Append(heroWaypointIndex)
                    .Append(" heroCornerDist=").Append(heroCornerDistance.ToString("F3"))
                    .Append(" moved=").Append(movedThisFrame.ToString("F3"))
                    .Append(" cornerOcc=").Append(cornerOccupancy)
                    .Append(" agents=");
                for (int i = 0; i < interns.Length; i++)
                {
                    grid.WorldToCell(interns[i].Position, out int x, out int y);
                    timeline.Append(i)
                        .Append(':').Append(interns[i].Position)
                        .Append("/cell=(").Append(x).Append(',').Append(y).Append(')')
                        .Append("/vel=").Append(lastVelocity[i])
                        .Append("/desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append("/constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append("/zeroStreak=").Append(consecutiveZeroFrames[i])
                        .Append('/')
                        .Append(FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(interns[i].GetHashCode()));
                    AppendSteeringBreakdown(timeline, interns[i]);
                    timeline.Append(' ');
                }

                timeline.AppendLine();
                if (cornerSlow)
                    Debug.LogWarning("[Lv3CornerSlowSample] " + timeline.ToString(sampleStart, timeline.Length - sampleStart));
            }
        }

        Assert.Less(minDistanceToCorner, 3.2f, $"追兵必须实际追到 SH_1_3 右下角附近，否则这条真实回归没有覆盖手测场景。minDistanceToCorner={minDistanceToCorner:F3}, cornerFrames={framesWithChaserNearCorner}\n{timeline}");
        Assert.Less(maxCornerSlowStreak, 8, $"英雄离开右下角后，追兵不应在建筑夹角连续蠕动停滞。cornerSlowFrames={cornerSlowFramesAfterReturn}, maxStreak={maxCornerSlowStreak}, overlapFrames={overlapFrames}, maxCornerOccAfterReturn={maxCornerOccupancyAfterReturn}, minReturnDist={minReturnDistanceToHero:F3}\n{timeline}");
        Assert.Less(minReturnDistanceToHero, 6f, $"英雄返回后追兵应重新追上，而不是继续滞留角落。minReturnDistanceToHero={minReturnDistanceToHero:F3}\n{timeline}");
    }

    [Test]
    public void Lv3英雄绕到研发中心背面时追兵不应冲建筑聚团停滞()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        config.CrowdPredictionTime = 0.35f;
        config.LaneBiasStrength = 0.18f;
        config.BoundaryAvoidanceWeight = 0.6f;
        config.BottleneckInfluenceDistance = 1.6f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const float dt = 0.1f;
        const float heroSpeed = 2.45f;
        const float chaserSpeed = 3.5f;
        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, diagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 970000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实 Lv3 背面追击测试必须先提交研发中心 Autobox runtime dirty，避免用半更新导航世界。");

        const float agentRadius = 0.45f;
        const float heroEdgeClearance = 0.5f;
        route.HeroStart = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.HeroStart, 5f, heroEdgeClearance, "hero-start-runtime", diagnostics);
        route.LowerApproach = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.LowerApproach, 5f, heroEdgeClearance, "lower-approach-runtime", diagnostics);
        route.RightBottomCorner = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.RightBottomCorner, 6f, heroEdgeClearance, "right-bottom-corner-runtime", diagnostics);
        route.ReturnPoint = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ReturnPoint, 5f, heroEdgeClearance, "return-point-runtime", diagnostics);
        for (int i = 0; i < route.ChaserStarts.Length; i++)
            route.ChaserStarts[i] = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, route.ChaserStarts[i], 4f, agentRadius, "chaser-runtime-" + i, diagnostics);

        Vector3 behindApproach = ResolveRuntimeLegalLv3MainIslandCell(
            grid,
            derivedData,
            new Vector3(route.ResearchCenterBounds.xMax + 1.0f, 0f, route.ResearchCenterBounds.yMin - 1.4f),
            6f,
            heroEdgeClearance,
            "behind-approach",
            diagnostics);

        SimEntityContext hero = CreateEntity(route.HeroStart, false, grid.AgentTypeId, agentRadius);
        hero.Side = SideType.PlayerSide;

        int chaserCount = Mathf.Min(14, route.ChaserStarts.Length);
        SimEntityContext[] chasers = new SimEntityContext[chaserCount];
        var allEntities = new System.Collections.Generic.List<IEntityContext> { hero };
        for (int i = 0; i < chaserCount; i++)
        {
            SimEntityContext chaser = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
            chasers[i] = chaser;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = hero,
                AggroRange = 34f,
                ForgetRange = 50f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            chaser.MoveExecutor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            SoldierAIBrain brain = new SoldierAIBrain();
            brain.DetectEnemyRange = 34f;
            brain.WeaponRange = 0.75f;
            brain.SetBirthPosition(chaser.Position);
            brain.Inject();
            chaser.Brain = brain;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            route.LowerApproach,
            behindApproach,
            route.RightBottomCorner,
            route.RightBottomCorner,
            route.ReturnPoint
        };
        int waypointIndex = 0;
        int cornerHoldFrames = 0;
        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        Vector3[] lastVelocity = new Vector3[chasers.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[chasers.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[chasers.Length];
        Vector3[] lastPositions = new Vector3[chasers.Length];
        for (int i = 0; i < chasers.Length; i++)
            lastPositions[i] = chasers[i].Position;

        int wallBeforePortalSamples = 0;
        int wallStallSamples = 0;
        int nearWallGoalSamples = 0;
        const float chaserNavigationRadius = 0.45f;
        int maxWallSlowStreak = 0;
        int currentWallSlowStreak = 0;
        float minDistanceToBack = float.PositiveInfinity;
        float minWallHitDistance = float.PositiveInfinity;

        for (int frame = 1; frame <= 560; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 waypoint = heroWaypoints[waypointIndex];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                waypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                heroEdgeClearance,
                diagnostics);

            float waypointDistanceSqr = (hero.Position - waypoint).sqrMagnitude;
            bool reachedWaypoint = waypointDistanceSqr <= 0.05f * 0.05f;
            if (waypointIndex == 2 || waypointIndex == 3)
                reachedWaypoint = waypointDistanceSqr <= 0.75f * 0.75f;
            if (reachedWaypoint && waypointIndex < heroWaypoints.Length - 1)
            {
                if (waypointIndex == 2 && cornerHoldFrames < 35)
                    cornerHoldFrames++;
                else
                {
                    cornerHoldFrames = 0;
                    waypointIndex++;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                chasers,
                frame,
                dt,
                zeroStreak,
                maxZeroStreak,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            int wallOccupancy = 0;
            float movedNearWall = 0f;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                Vector3 decisionPosition = lastPositions[i];
                Vector3 afterPosition = chaser.Position;
                float moved = Vector3.Distance(afterPosition, decisionPosition);
                lastPositions[i] = afterPosition;

                minDistanceToBack = Mathf.Min(minDistanceToBack, Vector3.Distance(afterPosition, route.RightBottomCorner));
                Vector3 desired = lastDesiredDisplacement[i];
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    0.45f);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                    && SegmentViolatesFootprintClearance(
                                                        researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f,
                                                       chaserNavigationRadius);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter;
                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }

                float researchDistance = researchCenter.DistanceToClosestBox(afterPosition);
                bool nearBackWall = waypointIndex >= 2
                                    && researchDistance <= 1.1f
                                    && afterPosition.x >= route.ResearchCenterBounds.center.x
                                    && afterPosition.z <= route.ResearchCenterBounds.center.y;
                if (nearBackWall)
                {
                    wallOccupancy++;
                    movedNearWall += moved;
                    if (moved < 0.035f && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f)
                        wallStallSamples++;
                }

                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.GetHashCode(), out Vector3 steeringGoal, out int steeringFrame);
                bool hasCurrentSteeringGoal = hasSteeringGoal && steeringFrame == frame;
                float steeringGoalResearchDistance = hasSteeringGoal ? researchCenter.DistanceToClosestBox(steeringGoal) : float.PositiveInfinity;
                bool flowSteeringGoalClear = false;
                float flowSteeringGoalClearanceViolation = float.PositiveInfinity;
                int flowRuntimeBoxCount = 0;
                int flowRuntimeCircleCount = 0;
                bool hasFlowClearance = hasSteeringGoal
                                        && FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                                            steeringGoal,
                                            chaserNavigationRadius,
                                            out flowSteeringGoalClear,
                                            out flowSteeringGoalClearanceViolation,
                                            out flowRuntimeBoxCount,
                                            out flowRuntimeCircleCount);
                bool nearWallGoal = hasCurrentSteeringGoal
                                    && steeringGoalResearchDistance < chaserNavigationRadius - 0.02f
                                    && HorizontalDistance(steeringGoal, hero.Position) > chaserNavigationRadius;
                if (nearWallGoal)
                    nearWallGoalSamples++;

                if (hitsBeforePortal || (nearBackWall && moved < 0.035f) || nearWallGoal || frame % 40 == 0)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3BacksideResearchChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" waypoint=").Append(waypointIndex)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(afterPosition);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append(" constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append(" moved=").Append(moved.ToString("F3"))
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" nearBackWall=").Append(nearBackWall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" steeringGoalResearchDist=").Append(float.IsPositiveInfinity(steeringGoalResearchDistance) ? "INF" : steeringGoalResearchDistance.ToString("F3"))
                        .Append(" nearWallGoal=").Append(nearWallGoal)
                        .Append(" flowClearance=").Append(hasFlowClearance ? flowSteeringGoalClear.ToString() : "missing")
                        .Append(" flowClearanceViolation=").Append(hasFlowClearance ? flowSteeringGoalClearanceViolation.ToString("F3") : "missing")
                        .Append(" flowRuntimeBoxes=").Append(hasFlowClearance ? flowRuntimeBoxCount.ToString() : "missing")
                        .Append(" flowRuntimeCircles=").Append(hasFlowClearance ? flowRuntimeCircleCount.ToString() : "missing");
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal || (nearBackWall && moved < 0.035f) || nearWallGoal)
                        Debug.LogWarning("[Lv3BacksideResearchChaseSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }

            bool wallSlow = waypointIndex >= 2 && wallOccupancy >= 3 && movedNearWall < 0.2f;
            if (wallSlow)
            {
                currentWallSlowStreak++;
                maxWallSlowStreak = Mathf.Max(maxWallSlowStreak, currentWallSlowStreak);
            }
            else
            {
                currentWallSlowStreak = 0;
            }
        }

        Assert.Less(minDistanceToBack, 3.2f, $"追兵必须实际追到研发中心背面附近，否则测试没有覆盖手测场景。minDistanceToBack={minDistanceToBack:F3}\n{diagnostics}");
        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"英雄绕到研发中心背面时，追兵不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}\n{diagnostics}");
        Assert.Less(maxWallSlowStreak, 6,
            $"英雄绕到研发中心背面时，追兵不应在背面墙侧连续聚团蠕动。maxWallSlowStreak={maxWallSlowStreak}, wallStallSamples={wallStallSamples}\n{diagnostics}");
        Assert.Less(wallStallSamples, 8,
            $"英雄绕到研发中心背面时，单兵贴墙停滞样本过多。wallStallSamples={wallStallSamples}, maxWallSlowStreak={maxWallSlowStreak}\n{diagnostics}");
        Assert.LessOrEqual(nearWallGoalSamples, 0,
            $"英雄绕到研发中心背面时，接战目标点不能贴进研发中心碰撞体半径内。nearWallGoalSamples={nearWallGoalSamples}\n{diagnostics}");
    }

    [UnityTest]
    public IEnumerator Lv3研发中心右下边缘真实追击不应多人挤住()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        config.CrowdPredictionTime = 0.35f;
        config.LaneBiasStrength = 0.18f;
        config.BoundaryAvoidanceWeight = 0.6f;
        config.BottleneckInfluenceDistance = 1.6f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 982000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实右下边缘回归必须先提交研发中心 Autobox runtime dirty。");

        const float dt = 0.1f;
        const float heroSpeed = 2.45f;
        const float chaserSpeed = 4.087f;
        const float agentRadius = 0.45f;
        SimEntityContext hero = CreateEntity(
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.21f, 0f, 18.33f), 2.5f, agentRadius, "right-edge-hero-start", diagnostics),
            false,
            grid.AgentTypeId,
            agentRadius);
        hero.Side = SideType.PlayerSide;
        hero.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));

        Vector3[] preferredStarts =
        {
            new Vector3(84.95f, 0f, 11.77f),
            new Vector3(84.63f, 0f, 11.77f),
            new Vector3(84.24f, 0f, 11.77f),
            new Vector3(83.88f, 0f, 11.75f),
            new Vector3(83.38f, 0f, 11.74f),
            new Vector3(85.12f, 0f, 12.08f),
            new Vector3(84.72f, 0f, 12.18f),
            new Vector3(84.30f, 0f, 12.16f),
            new Vector3(83.92f, 0f, 12.08f),
            new Vector3(83.50f, 0f, 12.04f),
            new Vector3(85.25f, 0f, 12.48f),
            new Vector3(84.85f, 0f, 12.55f)
        };

        SimEntityContext[] chasers = new SimEntityContext[preferredStarts.Length];
        var allEntities = new List<IEntityContext> { hero };
        for (int i = 0; i < preferredStarts.Length; i++)
        {
            Vector3 start = ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, preferredStarts[i], 4.0f, agentRadius, "right-edge-chaser-" + i, diagnostics);
            SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
            chaser.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));
            chasers[i] = chaser;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = hero,
                AggroRange = 34f,
                ForgetRange = 50f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            chaser.MoveExecutor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };

            SoldierAIBrain brain = new SoldierAIBrain();
            brain.DetectEnemyRange = 34f;
            brain.WeaponRange = 0.75f;
            brain.ChaseRange = 120f;
            brain.SetBirthPosition(chaser.Position);
            brain.Inject();
            chaser.Brain = brain;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        Vector3[] heroWaypoints =
        {
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.21f, 0f, 18.33f), 2.5f, agentRadius, "right-edge-hero-hold", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(85.49f, 0f, 18.05f), 2.5f, agentRadius, "right-edge-hero-shift", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(88.40f, 0f, 18.00f), 4.5f, agentRadius, "right-edge-hero-away", diagnostics),
            ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(86.10f, 0f, 15.30f), 3.5f, agentRadius, "right-edge-hero-return", diagnostics)
        };

        int waypointIndex = 0;
        int waypointHoldFrames = 0;
        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        Vector3[] lastVelocity = new Vector3[chasers.Length];
        Vector3[] lastDesiredDisplacement = new Vector3[chasers.Length];
        Vector3[] lastConstrainedDisplacement = new Vector3[chasers.Length];
        Vector3[] lastPositions = new Vector3[chasers.Length];
        for (int i = 0; i < chasers.Length; i++)
            lastPositions[i] = chasers[i].Position;

        int rightEdgeStallSamples = 0;
        int maxRightEdgeSlowStreak = 0;
        int currentRightEdgeSlowStreak = 0;
        int wallBeforePortalSamples = 0;
        int nearWallGoalSamples = 0;
        int crowdCancellationSamples = 0;
        int afterHeroLeftWallStallSamples = 0;
        int maxAfterHeroLeftWallStreak = 0;
        int currentAfterHeroLeftWallStreak = 0;
        int maxRequiredTileQueueIndex = -1;
        int requiredTileDeepQueueSamples = 0;
        int longPendingPortalSamples = 0;
        int requiredTilePendingSamples = 0;
        float minDistanceToHero = float.PositiveInfinity;
        float minWallHitDistance = float.PositiveInfinity;

        for (int frame = 1; frame <= 180; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);

            Vector3 heroGoal = heroWaypoints[waypointIndex];
            if (waypointIndex < 2 && waypointHoldFrames < 28)
            {
                waypointHoldFrames++;
            }
            else
            {
                hero.Position = AdvanceHeroWithNavigationConstraint(
                    hero.Position,
                    heroGoal,
                    heroSpeed,
                    dt,
                    grid.AgentTypeId,
                    0.5f,
                    diagnostics);
                if (HorizontalDistance(hero.Position, heroGoal) <= 0.2f && waypointIndex < heroWaypoints.Length - 1)
                {
                    waypointIndex++;
                    waypointHoldFrames = 0;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            AdvanceChasersThroughRuntimeMoveChain(
                chasers,
                frame,
                dt,
                zeroStreak,
                maxZeroStreak,
                lastVelocity,
                lastDesiredDisplacement,
                lastConstrainedDisplacement);

            int rightEdgeOccupancy = 0;
            float rightEdgeMovedSum = 0f;
            bool anyAfterHeroLeftWallStallThisFrame = false;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                Vector3 decisionPosition = lastPositions[i];
                Vector3 afterPosition = chaser.Position;
                float moved = HorizontalDistance(decisionPosition, afterPosition);
                lastPositions[i] = afterPosition;
                minDistanceToHero = Mathf.Min(minDistanceToHero, HorizontalDistance(afterPosition, hero.Position));

                Vector3 desired = lastDesiredDisplacement[i];
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    agentRadius);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentViolatesFootprintClearance(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f,
                                                       agentRadius);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter
                                        && targetDistance > 1.25f;
                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }

                float researchDistance = researchCenter.DistanceToClosestBox(afterPosition);
                bool inLoggedRightEdgeCluster = afterPosition.x >= 83.2f
                                                && afterPosition.x <= 85.45f
                                                && afterPosition.z >= 11.45f
                                                && afterPosition.z <= 12.85f;
                if (inLoggedRightEdgeCluster)
                {
                    rightEdgeOccupancy++;
                    rightEdgeMovedSum += moved;
                    if (moved < 0.04f && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f)
                        rightEdgeStallSamples++;
                }

                bool heroHasLeftInitialCorner = waypointIndex >= 2 || HorizontalDistance(hero.Position, heroWaypoints[0]) > 1.25f;
                bool afterHeroLeftWallStall = heroHasLeftInitialCorner
                                              && targetDistance > 1.25f
                                              && researchDistance <= 1.05f
                                              && moved < 0.04f
                                              && lastDesiredDisplacement[i].sqrMagnitude > 0.08f * 0.08f;
                if (afterHeroLeftWallStall)
                {
                    afterHeroLeftWallStallSamples++;
                    anyAfterHeroLeftWallStallThisFrame = true;
                }

                if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                        chaser.GetHashCode(),
                        out Vector3 desiredVelocity,
                        out _,
                        out Vector3 agentAvoidance,
                        out Vector3 clampedAgentAvoidance,
                        out _,
                        out _,
                        out _,
                        out Vector3 resultVelocity)
                    && inLoggedRightEdgeCluster
                    && desiredVelocity.magnitude > chaserSpeed * 0.55f
                    && resultVelocity.magnitude < chaserSpeed * 0.20f
                    && agentAvoidance.magnitude > chaserSpeed)
                {
                    crowdCancellationSamples++;
                }

                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.GetHashCode(), out Vector3 steeringGoal, out int steeringFrame);
                bool hasCurrentSteeringGoal = hasSteeringGoal && steeringFrame == frame;
                float steeringGoalResearchDistance = hasSteeringGoal ? researchCenter.DistanceToClosestBox(steeringGoal) : float.PositiveInfinity;
                bool nearWallGoal = hasCurrentSteeringGoal
                                    && steeringGoalResearchDistance < agentRadius - 0.02f
                                    && HorizontalDistance(steeringGoal, hero.Position) > agentRadius;
                if (nearWallGoal)
                    nearWallGoalSamples++;

                bool hasTileQueueState = FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                    chaser.GetHashCode(),
                    out string requiredGoalKind,
                    out int requiredPortalId,
                    out bool requiredTileCached,
                    out bool requiredTilePending,
                    out int requiredTileQueueIndex,
                    out string requiredTileStage,
                    out bool requiredTileWaiting,
                    out bool requiredTileStale,
                    out int pendingPortalFrames);
                if (hasTileQueueState)
                {
                    if (requiredTilePending && !requiredTileCached)
                        requiredTilePendingSamples++;
                    if (requiredTileQueueIndex > maxRequiredTileQueueIndex)
                        maxRequiredTileQueueIndex = requiredTileQueueIndex;
                    if (requiredTileQueueIndex >= 128)
                        requiredTileDeepQueueSamples++;
                    if (pendingPortalFrames >= 12)
                        longPendingPortalSamples++;
                }

                bool shouldLog = hitsBeforePortal
                                 || nearWallGoal
                                 || afterHeroLeftWallStall
                                 || (hasTileQueueState && (requiredTileQueueIndex >= 128 || pendingPortalFrames >= 12))
                                 || (inLoggedRightEdgeCluster && (moved < 0.04f || frame % 20 == 0))
                                 || frame <= 5;
                if (shouldLog)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3RightEdgeRealChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" waypoint=").Append(waypointIndex)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(afterPosition);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(lastDesiredDisplacement[i])
                        .Append(" constrainedDisp=").Append(lastConstrainedDisplacement[i])
                        .Append(" moved=").Append(moved.ToString("F3"))
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" inLoggedRightEdgeCluster=").Append(inLoggedRightEdgeCluster)
                        .Append(" heroHasLeftInitialCorner=").Append(heroHasLeftInitialCorner)
                        .Append(" afterHeroLeftWallStall=").Append(afterHeroLeftWallStall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" steeringGoalResearchDist=").Append(float.IsPositiveInfinity(steeringGoalResearchDistance) ? "INF" : steeringGoalResearchDistance.ToString("F3"))
                        .Append(" nearWallGoal=").Append(nearWallGoal)
                        .Append(" tileState=").Append(hasTileQueueState ? "present" : "missing")
                        .Append(" tileGoalKind=").Append(hasTileQueueState ? requiredGoalKind : "missing")
                        .Append(" tilePortal=").Append(hasTileQueueState ? requiredPortalId.ToString() : "missing")
                        .Append(" tileCached=").Append(hasTileQueueState ? requiredTileCached.ToString() : "missing")
                        .Append(" tilePending=").Append(hasTileQueueState ? requiredTilePending.ToString() : "missing")
                        .Append(" tileQueueIndex=").Append(hasTileQueueState ? requiredTileQueueIndex.ToString() : "missing")
                        .Append(" tileStage=").Append(hasTileQueueState ? requiredTileStage : "missing")
                        .Append(" tileWaiting=").Append(hasTileQueueState ? requiredTileWaiting.ToString() : "missing")
                        .Append(" tileStale=").Append(hasTileQueueState ? requiredTileStale.ToString() : "missing")
                        .Append(" pendingPortalFrames=").Append(hasTileQueueState ? pendingPortalFrames.ToString() : "missing")
                        .Append(" pendingQueue=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal || nearWallGoal || afterHeroLeftWallStall || (hasTileQueueState && (requiredTileQueueIndex >= 128 || pendingPortalFrames >= 12)) || (inLoggedRightEdgeCluster && moved < 0.04f))
                        Debug.LogWarning("[Lv3RightEdgeRealChaseSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }

            bool rightEdgeSlow = rightEdgeOccupancy >= 4 && rightEdgeMovedSum < 0.22f;
            if (rightEdgeSlow)
            {
                currentRightEdgeSlowStreak++;
                maxRightEdgeSlowStreak = Mathf.Max(maxRightEdgeSlowStreak, currentRightEdgeSlowStreak);
            }
            else
            {
                currentRightEdgeSlowStreak = 0;
            }

            bool afterLeaveWallSlow = waypointIndex >= 2 && anyAfterHeroLeftWallStallThisFrame;
            if (afterLeaveWallSlow)
            {
                currentAfterHeroLeftWallStreak++;
                maxAfterHeroLeftWallStreak = Mathf.Max(maxAfterHeroLeftWallStreak, currentAfterHeroLeftWallStreak);
            }
            else
            {
                currentAfterHeroLeftWallStreak = 0;
            }

            yield return null;
        }

        Assert.Less(minDistanceToHero, 6f, $"追兵必须实际追到英雄附近，否则这条真实回归没有覆盖手测接敌场景。minDistanceToHero={minDistanceToHero:F3}\n{diagnostics}");
        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"右下边缘真实追击不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}\n{diagnostics}");
        Assert.LessOrEqual(nearWallGoalSamples, 0,
            $"右下边缘真实追击的接战目标点不能贴进研发中心碰撞体半径内。nearWallGoalSamples={nearWallGoalSamples}\n{diagnostics}");
        Assert.Less(maxRightEdgeSlowStreak, 5,
            $"右下边缘真实追击不应多人连续挤住蠕动。maxRightEdgeSlowStreak={maxRightEdgeSlowStreak}, rightEdgeStallSamples={rightEdgeStallSamples}, crowdCancellationSamples={crowdCancellationSamples}\n{diagnostics}");
        Assert.Less(rightEdgeStallSamples, 8,
            $"右下边缘真实追击出现过多单兵停滞样本。rightEdgeStallSamples={rightEdgeStallSamples}, crowdCancellationSamples={crowdCancellationSamples}\n{diagnostics}");
        Assert.Less(afterHeroLeftWallStallSamples, 5,
            $"英雄离开右下边缘后，追兵不应继续贴研发中心墙侧停滞。afterHeroLeftWallStallSamples={afterHeroLeftWallStallSamples}, maxAfterHeroLeftWallStreak={maxAfterHeroLeftWallStreak}\n{diagnostics}");
        Assert.LessOrEqual(requiredTileDeepQueueSamples, 0,
            $"真实右下追击时，当前单位所需 tile 不能被旧移动目标 tile job 淹没到队列深处。deepSamples={requiredTileDeepQueueSamples}, maxQueueIndex={maxRequiredTileQueueIndex}, pendingSamples={requiredTilePendingSamples}\n{diagnostics}");
        Assert.LessOrEqual(longPendingPortalSamples, 0,
            $"真实右下追击时，单位不能长期停留在 PendingPortal/Infinity 方向上。longPendingPortalSamples={longPendingPortalSamples}, maxQueueIndex={maxRequiredTileQueueIndex}\n{diagnostics}");
    }

    [Test]
    public void Lv3研发中心左侧追击英雄绕到右下时不应冲建筑聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 984000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "真实左侧追击右下回归必须先提交研发中心 Autobox runtime dirty。");
        Rect bounds = preset.ResearchCenterFootprintBounds;
        Vector3 heroStart = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMin - 5.0f, 0f, bounds.yMin - 1.35f),
            6f,
            "moving-hero-start",
            diagnostics);
        Vector3 heroGoal = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f),
            6f,
            "moving-hero-goal",
            diagnostics);
        Vector3 chaserCenter = ResolveNearestMainIslandCell(
            grid,
            derivedData,
            new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
            8f,
            "moving-chaser-center",
            diagnostics);

        SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;

        const int chaserCount = 16;
        const float dt = 0.1f;
        const float heroSpeed = 2.4f;
        const float targetCellJitter = 0.035f;
        Fix64 chaserSpeedProperty = (Fix64)(3.5f / DistanceUnitConverter.DefaultDistanceConversionRate);
        SimEntityContext[] chasers = new SimEntityContext[chaserCount];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserCount];
        SimMoveExecutor[] executors = new SimMoveExecutor[chaserCount];
        int[] zeroStreak = new int[chaserCount];
        int[] maxZeroStreak = new int[chaserCount];
        for (int i = 0; i < chaserCount; i++)
        {
            float angle = i * 2.39996323f;
            float radius = 0.55f + (i % 4) * 0.55f;
            Vector3 preferred = chaserCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 start = ResolveNearestMainIslandCell(grid, derivedData, preferred, 4f, "moving-chaser-" + i, diagnostics);
            SimEntityContext chaser = CreateEntity(start, false, grid.AgentTypeId, 0.45f);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
            {
                CurrentTarget = hero
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };
            chaser.MoveExecutor = executor;
            chasers[i] = chaser;
            moveComps[i] = moveComp;
            executors[i] = executor;
        }

        int wallBeforePortalSamples = 0;
        int constrainedWallStallSamples = 0;
        int staleSteeringSamples = 0;
        int targetCellSwitches = 0;
        int maxPendingFlowTiles = 0;
        int maxPendingSharedGoals = 0;
        int lastHeroCellX = int.MinValue;
        int lastHeroCellY = int.MinValue;
        int issueDetailedSamples = 0;
        int backgroundDetailedSamples = 0;
        float minWallHitDistance = float.PositiveInfinity;
        for (int frame = 1; frame <= 220; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                heroGoal,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                0.5f,
                diagnostics);
            if (grid.WorldToCell(hero.Position, out int heroCellX, out int heroCellY)
                && heroCellX > lastHeroCellX)
            {
                float lowerCellCenterZ = grid.Origin.z + (heroCellY + 0.5f) * grid.CellSize;
                float jitterSign = (frame & 1) == 0 ? -1f : 1f;
                Vector3 jitteredHeroPosition = hero.Position;
                jitteredHeroPosition.z = lowerCellCenterZ + jitterSign * (grid.CellSize * 0.5f + targetCellJitter);
                hero.Position = jitteredHeroPosition;
                if (grid.WorldToCell(hero.Position, out heroCellX, out heroCellY))
                {
                    if (lastHeroCellX != int.MinValue
                        && (heroCellX != lastHeroCellX || heroCellY != lastHeroCellY))
                    {
                        targetCellSwitches++;
                    }

                    lastHeroCellX = heroCellX;
                    lastHeroCellY = heroCellY;
                }
            }
            else if (heroCellX != lastHeroCellX || heroCellY != lastHeroCellY)
            {
                if (lastHeroCellX != int.MinValue)
                    targetCellSwitches++;
                lastHeroCellX = heroCellX;
                lastHeroCellY = heroCellY;
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            maxPendingFlowTiles = Mathf.Max(maxPendingFlowTiles, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
            maxPendingSharedGoals = Mathf.Max(maxPendingSharedGoals, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount());

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                moveComps[i].MoveTo(hero.Position);
                moveComps[i].Move(dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                Vector3 desired = executor.LastDesiredDisplacement;
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentEntersFootprint(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f);
                bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
                    researchCenter,
                    decisionPosition,
                    chaser,
                    grid.CellSize * 0.5f,
                    0.45f);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter
                                        && actualNavigationSegmentViolatesResearchCenter;
                bool projectedToZero = desired.sqrMagnitude > 0.04f * 0.04f
                                       && executor.LastConstrainedDisplacement.sqrMagnitude <= 0.015f * 0.015f;
                zeroStreak[i] = projectedToZero ? zeroStreak[i] + 1 : 0;
                maxZeroStreak[i] = Mathf.Max(maxZeroStreak[i], zeroStreak[i]);
                float researchDistance = researchCenter.DistanceToClosestBox(decisionPosition);
                bool constrainedWallStall = projectedToZero && researchDistance <= 0.9f;
                bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.GetHashCode(), out Vector3 steeringGoal, out int steeringFrame);
                bool staleSteering = !hasSteeringGoal || frame - steeringFrame > 2;

                if (hitsBeforePortal)
                {
                    wallBeforePortalSamples++;
                    minWallHitDistance = Mathf.Min(minWallHitDistance, wallHitDistance);
                }
                if (constrainedWallStall)
                    constrainedWallStallSamples++;
                if (staleSteering)
                    staleSteeringSamples++;

                bool isIssueSample = hitsBeforePortal || constrainedWallStall;
                bool isBackgroundSample = staleSteering || frame % 30 == 0;
                bool shouldRecordDetailedSample = (isIssueSample && issueDetailedSamples < 16)
                                                  || (!isIssueSample && isBackgroundSample && backgroundDetailedSamples < 6);
                if (shouldRecordDetailedSample)
                {
                    if (isIssueSample)
                        issueDetailedSamples++;
                    else
                        backgroundDetailedSamples++;
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3MovingHeroResearchChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" hero=").Append(hero.Position)
                        .Append(" decision=").Append(decisionPosition)
                        .Append(" after=").Append(chaser.Position);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    diagnostics.Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
                        .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
                        .Append(" zeroStreak=").Append(zeroStreak[i])
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                        .Append(" constrainedWallStall=").Append(constrainedWallStall)
                        .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
                        .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing")
                        .Append(" staleSteering=").Append(staleSteering)
                        .Append(" pendingFlow=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount())
                        .Append(" pendingShared=").Append(FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount())
                        .Append(" heroCell=(").Append(lastHeroCellX).Append(',').Append(lastHeroCellY).Append(')');
                    if (FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                            chaser.GetHashCode(),
                            out string queueGoalKind,
                            out int queuePortalId,
                            out bool queueCached,
                            out bool queuePending,
                            out int queueIndex,
                            out string queueStage,
                            out bool queueWaiting,
                            out bool queueStale,
                            out int pendingPortalFrames))
                    {
                        diagnostics.Append(" tileQueue={kind=").Append(queueGoalKind)
                            .Append(",portal=").Append(queuePortalId)
                            .Append(",cached=").Append(queueCached)
                            .Append(",pending=").Append(queuePending)
                            .Append(",index=").Append(queueIndex)
                            .Append(",stage=").Append(queueStage)
                            .Append(",waiting=").Append(queueWaiting)
                            .Append(",stale=").Append(queueStale)
                            .Append(",pendingPortalFrames=").Append(pendingPortalFrames)
                            .Append('}');
                    }
                    else
                    {
                        diagnostics.Append(" tileQueue=missing");
                    }
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                }
            }
        }

        int worstZeroStreak = 0;
        for (int i = 0; i < maxZeroStreak.Length; i++)
            worstZeroStreak = Mathf.Max(worstZeroStreak, maxZeroStreak[i]);

        Assert.LessOrEqual(wallBeforePortalSamples, 0,
            $"移动英雄绕研发中心右下时，追兵不应在到达当前 portal 前朝建筑正面推进。wallBeforePortal={wallBeforePortalSamples}, minHit={minWallHitDistance:F3}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
        Assert.LessOrEqual(constrainedWallStallSamples, 0,
            $"移动英雄绕研发中心右下时，追兵不应贴近建筑后连续被导航约束吃掉位移。stallSamples={constrainedWallStallSamples}, worstZeroStreak={worstZeroStreak}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
        Assert.LessOrEqual(staleSteeringSamples, 0,
            $"移动英雄绕研发中心右下时，Combat 链路不应长时间停用 steering。staleSteeringSamples={staleSteeringSamples}, targetCellSwitches={targetCellSwitches}, maxPendingFlow={maxPendingFlowTiles}, maxPendingShared={maxPendingSharedGoals}\n{diagnostics}");
    }

    [Test]
    public void Lv3研发中心真实路径矩阵不应撞建筑绕大圈或墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实路径矩阵必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect bounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario[] scenarios =
        {
            new Lv3CrowdRouteScenario
            {
                Name = "真实SH1-3右下角追击后返回",
                HeroStart = new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.2f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "左侧中央追右下窄路后返回",
                HeroStart = new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.2f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f), 28),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.2f, 0f, bounds.yMin - 1.35f), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.6f, 0f, bounds.yMin - 0.8f),
                },
                ChaserCount = 18,
                Frames = 260,
                ExpectLowerRoute = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "英雄从左下绕到右下角抖动再离开",
                HeroStart = new Vector3(bounds.xMin - 4.8f, 0f, bounds.yMin - 1.7f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x - 0.6f, 0f, bounds.yMin - 1.5f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 3.2f, 0f, bounds.yMin - 1.75f), 10),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 3.2f, 0f, bounds.yMin - 0.85f), 10),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.2f, 0f, bounds.yMin - 1.35f), 18),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 8.0f, 0f, bounds.center.y), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 6.8f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 6.0f, 0f, bounds.yMin + 0.4f),
                },
                ChaserCount = 20,
                Frames = 270,
                ExpectLowerRoute = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "英雄先走研发中心背面再切右下",
                HeroStart = new Vector3(bounds.xMin - 5.4f, 0f, bounds.center.y),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 3.0f, 0f, bounds.yMax + 1.4f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 4.2f, 0f, bounds.yMax + 1.5f), 14),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.0f, 0f, bounds.yMin - 1.35f), 22),
                    new Lv3HeroPathNode(new Vector3(bounds.xMin - 5.4f, 0f, bounds.center.y), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.8f, 0f, bounds.yMax + 1.2f),
                },
                ChaserCount = 18,
                Frames = 270,
                ExpectLowerRoute = false,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "混合兵群从上下两侧追右下角",
                HeroStart = new Vector3(bounds.xMin - 5.0f, 0f, bounds.yMin - 1.25f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(bounds.center.x, 0f, bounds.yMin - 1.3f), 0),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 5.4f, 0f, bounds.yMin - 1.25f), 34),
                    new Lv3HeroPathNode(new Vector3(bounds.xMax + 8.4f, 0f, bounds.yMin + 1.2f), 0),
                },
                ChaserCenters = new[]
                {
                    new Vector3(bounds.xMin - 7.0f, 0f, bounds.center.y),
                    new Vector3(bounds.xMin - 5.4f, 0f, bounds.yMin - 0.7f),
                    new Vector3(bounds.xMin - 5.4f, 0f, bounds.yMax + 1.1f),
                },
                ChaserCount = 22,
                Frames = 250,
                ExpectLowerRoute = true,
            },
        };

        for (int i = 0; i < scenarios.Length; i++)
            RunLv3ResearchCenterCrowdRouteScenario(grid, derivedData, preset, scenarios[i], 990000 + i * 100);
    }

    [Test]
    public void Lv3真实SH13多路径追击不应卡建筑或墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实 SH_1_3 多路径压力测试必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario[] scenarios =
        {
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄左下穿研发中心下侧到SH13右下再返回",
                HeroStart = preset.HeroStart,
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 7.0f, 0f, researchBounds.center.y), 0),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 4.8f, 0f, researchBounds.yMin - 1.4f), 0),
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 48),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 620,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄贴右下角短暂停留后向左上脱离",
                HeroStart = new Vector3(researchBounds.xMin - 5.5f, 0f, researchBounds.yMin - 1.45f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 54),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMax + 3.5f, 0f, researchBounds.yMin + 0.2f), 18),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.5f, 0f, researchBounds.yMax + 1.2f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄从建筑上侧切到右下再返回",
                HeroStart = new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.yMax + 1.3f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMax + 4.0f, 0f, researchBounds.yMax + 1.2f), 16),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.5f, 0f, researchBounds.yMin - 1.2f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 560,
                ExpectLowerRoute = false,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "真实刷怪-英雄右下角小范围折返后离开",
                HeroStart = new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 1.0f, 0f, strongholdBounds.yMin + 0.5f), 18),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 18),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 2.0f, 0f, strongholdBounds.yMin + 1.0f), 18),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 640,
                ExpectLowerRoute = true,
                UseNaturalTargetAcquisition = true,
            },
        };

        for (int i = 0; i < scenarios.Length; i++)
            RunLv3ResearchCenterCrowdRouteScenario(grid, derivedData, preset, scenarios[i], 992000 + i * 100);
    }

    [Test]
    public void Lv3真实SH13追击使用CharacterController时不应被建筑或墙角卡住()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "CharacterController 真实追击测试必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        const float agentRadius = 0.45f;
        const float dt = 0.1f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 3.5f;
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, 993000);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), "CharacterController 真实追击测试必须先提交研发中心 Autobox runtime dirty。");

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        List<GameObject> createdObjects = new List<GameObject>();
        try
        {
            GameObject ground = new GameObject("FlowTest_GroundCollider");
            ground.transform.position = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                -0.055f,
                grid.Origin.y + grid.Height * grid.CellSize * 0.5f);
            BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(grid.Width * grid.CellSize + 24f, 0.1f, grid.Height * grid.CellSize + 24f);
            createdObjects.Add(ground);

            for (int i = 0; i < researchCenter.Boxes.Length; i++)
            {
                Rect box = researchCenter.Boxes[i];
                GameObject obstacle = new GameObject("FlowTest_ResearchCenterCollider_" + i);
                obstacle.transform.position = new Vector3(box.center.x, 1f, box.center.y);
                BoxCollider collider = obstacle.AddComponent<BoxCollider>();
                collider.size = new Vector3(box.width, 2f, box.height);
                createdObjects.Add(obstacle);
            }
            Physics.SyncTransforms();

            Vector3 heroStart = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                preset.HeroStart,
                6f,
                agentRadius,
                "controller-hero-start",
                diagnostics);
            Vector3[] heroWaypoints =
            {
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 7.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-left", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 7f, agentRadius, "controller-hero-lower", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 7f, agentRadius, "controller-hero-sh13-corner", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-return", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 5.8f, 0f, researchBounds.yMax + 1.2f), 7f, agentRadius, "controller-hero-upper-left", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMax + 4.0f, 0f, researchBounds.yMax + 1.2f), 7f, agentRadius, "controller-hero-upper-right", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 7f, agentRadius, "controller-hero-sh13-corner-repeat", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f), 7f, agentRadius, "controller-hero-lower-return", diagnostics),
                ResolveRuntimeLegalLv3MainIslandCell(grid, derivedData, new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 7f, agentRadius, "controller-hero-final-left", diagnostics)
            };

            Lv3CrowdRouteScenario spawnScenario = new Lv3CrowdRouteScenario
            {
                Name = "CharacterController真实刷怪",
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount
            };
            Vector3[] starts = ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(grid, derivedData, spawnScenario, agentRadius, diagnostics);
            SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
            hero.Side = SideType.PlayerSide;
            EntityRegistry.Register(hero);

            SimEntityContext[] chasers = new SimEntityContext[starts.Length];
            MoveExecutor[] executors = new MoveExecutor[starts.Length];
            Transform[] transforms = new Transform[starts.Length];
            for (int i = 0; i < starts.Length; i++)
            {
                SimEntityContext chaser = CreateEntity(starts[i], false, grid.AgentTypeId, agentRadius);
                chaser.Side = SideType.EnemySide;
                chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate));
                GameObject go = new GameObject("FlowTest_CC_Chaser_" + i);
                go.transform.position = starts[i];
                CharacterController controller = go.AddComponent<CharacterController>();
                controller.radius = agentRadius;
                controller.height = 2f;
                controller.center = new Vector3(0f, 1f, 0f);
                MoveExecutor executor = go.AddComponent<MoveExecutor>();
                executor.Init(controller, grid.AgentTypeId);
                chaser.MoveExecutor = executor;
                chasers[i] = chaser;
                executors[i] = executor;
                transforms[i] = go.transform;
                createdObjects.Add(go);
                EntityRegistry.Register(chaser);
            }
            Physics.SyncTransforms();

            int waypointIndex = 0;
            int waypointHoldFrames = 0;
            int controllerStallSamples = 0;
            int wallClusterStreak = 0;
            int maxWallClusterStreak = 0;
            int overlapPairSamples = 0;
            int maxOverlapPairsThisFrame = 0;
            int overlapPairStreak = 0;
            int maxOverlapPairStreak = 0;
            float minHeroDistance = float.PositiveInfinity;
            for (int frame = 1; frame <= 1120; frame++)
            {
                FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
                Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
                hero.Position = AdvanceHeroWithNavigationConstraint(hero.Position, activeWaypoint, heroSpeed, dt, grid.AgentTypeId, 0.5f, diagnostics);
                Vector3 toWaypoint = activeWaypoint - hero.Position;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
                {
                    int holdFrames = waypointIndex == 2 || waypointIndex == 6 ? 48 : waypointIndex == 4 ? 18 : 0;
                    if (waypointHoldFrames < holdFrames)
                        waypointHoldFrames++;
                    else
                    {
                        waypointIndex++;
                        waypointHoldFrames = 0;
                    }
                }

                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
                int slowNearWallThisFrame = 0;
                for (int i = 0; i < chasers.Length; i++)
                {
                    SimEntityContext chaser = chasers[i];
                    chaser.Position = transforms[i].position;
                    Vector3 before = transforms[i].position;
                    bool gotVelocity = FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, hero.Position, chaserSpeed, out Vector3 velocity);
                    Assert.IsTrue(gotVelocity, $"CharacterController 真实追击必须能取得流场速度。frame={frame}, agent={i}, pos={chaser.Position}, hero={hero.Position}");
                    executors[i].SetInput(velocity);
                    executors[i].Execute(dt);
                    Vector3 after = transforms[i].position;
                    chaser.Position = after;

                    Vector3 actual = after - before;
                    actual.y = 0f;
                    minHeroDistance = Mathf.Min(minHeroDistance, HorizontalDistance(after, hero.Position));
                    float researchDistance = researchCenter.DistanceToClosestBox(after);
                    bool activeButControllerStopped = velocity.magnitude > chaserSpeed * 0.45f
                                                      && actual.magnitude < chaserSpeed * dt * 0.12f
                                                      && HorizontalDistance(after, hero.Position) > 1.25f;
                    if (activeButControllerStopped && researchDistance <= 1.05f)
                    {
                        controllerStallSamples++;
                        slowNearWallThisFrame++;
                        if (controllerStallSamples <= 20)
                        {
                            diagnostics.Append("[Lv3ControllerStall] frame=").Append(frame)
                                .Append(" agent=").Append(i)
                                .Append(" before=").Append(before)
                                .Append(" after=").Append(after)
                                .Append(" hero=").Append(hero.Position)
                                .Append(" velocity=").Append(velocity)
                                .Append(" actual=").Append(actual)
                                .Append(" researchDist=").Append(researchDistance.ToString("F3"));
                            AppendCellDiagnostic(diagnostics, grid, derivedData, after, "after");
                            AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                            diagnostics.AppendLine();
                        }
                    }
                }

                int overlapPairsThisFrame = CollectLv3ControllerOverlapSamples(
                    diagnostics,
                    grid,
                    derivedData,
                    transforms,
                    hero.Position,
                    frame,
                    agentRadius,
                    maxOverlapPairStreak,
                    overlapPairSamples,
                    chasers,
                    hero);
                overlapPairSamples += overlapPairsThisFrame;
                maxOverlapPairsThisFrame = Mathf.Max(maxOverlapPairsThisFrame, overlapPairsThisFrame);
                if (overlapPairsThisFrame >= 3)
                    overlapPairStreak++;
                else
                    overlapPairStreak = 0;
                maxOverlapPairStreak = Mathf.Max(maxOverlapPairStreak, overlapPairStreak);

                if (slowNearWallThisFrame >= 3)
                    wallClusterStreak++;
                else
                    wallClusterStreak = 0;
                maxWallClusterStreak = Mathf.Max(maxWallClusterStreak, wallClusterStreak);
            }

            Debug.LogWarning(
                $"[Lv3ControllerScenarioSummary] controllerStall={controllerStallSamples} maxWallClusterStreak={maxWallClusterStreak} " +
                $"overlapPairs={overlapPairSamples} maxOverlapPairsFrame={maxOverlapPairsThisFrame} maxOverlapPairStreak={maxOverlapPairStreak} " +
                $"minHeroDist={minHeroDistance:F3}");
            Assert.Less(controllerStallSamples, 6,
                $"CharacterController 真实追击不应出现多次已获得流场速度但被建筑/墙角实际位移卡住。stall={controllerStallSamples}, maxWallClusterStreak={maxWallClusterStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.Less(maxWallClusterStreak, 4,
                $"CharacterController 真实追击不应出现多人连续贴墙停滞。stall={controllerStallSamples}, maxWallClusterStreak={maxWallClusterStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairsThisFrame, 2,
                $"CharacterController 真实追击不应允许多个追兵在英雄附近叠成一个点。overlapPairs={overlapPairSamples}, maxOverlapPairsFrame={maxOverlapPairsThisFrame}, maxOverlapPairStreak={maxOverlapPairStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairStreak, 3,
                $"CharacterController 真实追击不应持续出现追兵重叠聚团。overlapPairs={overlapPairSamples}, maxOverlapPairsFrame={maxOverlapPairsThisFrame}, maxOverlapPairStreak={maxOverlapPairStreak}, minHeroDist={minHeroDistance:F3}\n{diagnostics}");
            Assert.Less(minHeroDistance, 5f,
                $"CharacterController 真实追击必须实际接近英雄，否则测试未覆盖接敌链路。minHeroDist={minHeroDistance:F3}\n{diagnostics}");
        }
        finally
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
    }

    [Test]
    public void Lv3真实SH13多路径Combat链路使用CharacterController和地形边界时不应卡住()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid_Small.asset");
        Assert.NotNull(grid, "Combat + CharacterController 真实追击测试必须直接使用 Lv3_FlowNavigationGrid_Small.asset。");
        FlowNavigationGridAsset spawnGrid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(spawnGrid, "Combat + CharacterController 真实追击测试必须同时注册默认刷怪导航源。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid_Small.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid_Small.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario[] scenarios =
        {
            new Lv3CrowdRouteScenario
            {
                Name = "CC真实Combat-下侧穿研发中心到SH13右下后返回",
                HeroStart = preset.HeroStart,
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 7.0f, 0f, researchBounds.center.y), 0),
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 60),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.center.y), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 760,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "CC真实Combat-右下角小范围折返后从下侧离开",
                HeroStart = new Vector3(researchBounds.xMin - 5.6f, 0f, researchBounds.yMin - 1.35f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.35f), 0),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 1.4f, 0f, strongholdBounds.yMin + 0.55f), 24),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 48),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax - 2.6f, 0f, strongholdBounds.yMin + 1.15f), 24),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.4f, 0f, researchBounds.yMin - 1.25f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 820,
                UseNaturalTargetAcquisition = true,
            },
            new Lv3CrowdRouteScenario
            {
                Name = "CC真实Combat-上侧绕到右上再切右下",
                HeroStart = new Vector3(researchBounds.xMin - 6.0f, 0f, researchBounds.yMax + 1.3f),
                HeroNodes = new[]
                {
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMax + 4.0f, 0f, researchBounds.yMax + 1.2f), 18),
                    new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 54),
                    new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.4f, 0f, researchBounds.yMin - 1.2f), 0),
                },
                ChaserCenters = preset.UnitSpawnCenters,
                ChaserCenterCounts = preset.UnitSpawnCounts,
                ChaserCount = preset.TotalUnitSpawnCount,
                Frames = 760,
                UseNaturalTargetAcquisition = true,
            },
        };

        for (int i = 0; i < scenarios.Length; i++)
            RunLv3CharacterControllerCombatScenario(grid, spawnGrid, derivedData, preset, scenarios[i], 995000 + i * 100);
    }

    [Test]
    public void Lv3真实SH13右下角追击返程不应墙角聚团()
    {
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实 SH_1_3 右下角回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");

        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Rect researchBounds = preset.ResearchCenterFootprintBounds;
        Rect strongholdBounds = preset.StrongholdSh13Bounds;
        Lv3CrowdRouteScenario scenario = new Lv3CrowdRouteScenario
        {
            Name = "真实SH1-3右下角追击后返回",
            HeroStart = preset.HeroStart,
            HeroNodes = new[]
            {
                new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 8.0f, 0f, researchBounds.center.y), 0),
                new Lv3HeroPathNode(new Vector3(researchBounds.center.x, 0f, researchBounds.yMin - 1.2f), 0),
                new Lv3HeroPathNode(new Vector3(strongholdBounds.xMax, 0f, strongholdBounds.yMin), 42),
                new Lv3HeroPathNode(new Vector3(researchBounds.xMin - 5.2f, 0f, researchBounds.yMin - 1.35f), 0),
            },
            ChaserCenters = preset.UnitSpawnCenters,
            ChaserCenterCounts = preset.UnitSpawnCounts,
            ChaserCount = preset.TotalUnitSpawnCount,
            Frames = 560,
            ExpectLowerRoute = true,
            UseNaturalTargetAcquisition = true,
        };

        RunLv3ResearchCenterCrowdRouteScenario(grid, derivedData, preset, scenario, 991000);
    }

    [Test]
    public void Lv3研发中心下侧追击时初段方向不应指向建筑正面()
    {
        System.Diagnostics.Stopwatch testWatch = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log("[Lv3ResearchCenterChaseTest] stage=begin");
        FlowNavigationGridAsset grid = UnityEditor.AssetDatabase.LoadAssetAtPath<FlowNavigationGridAsset>("Assets/AAAGame/Tilemap/Lv3_FlowNavigationGrid.asset");
        Assert.NotNull(grid, "真实场景回归必须直接使用 Lv3_FlowNavigationGrid.asset。");
        FlowNavigationGridAsset.DerivedNavigationData derivedData = grid.GetDerivedNavigationDataRuntimeReadOnlyReference();
        Assert.NotNull(derivedData, "Lv3_FlowNavigationGrid.asset 必须带预烘焙 derived navigation data，测试才与实机链路一致。");
        Assert.IsTrue(derivedData.IsValid, "Lv3_FlowNavigationGrid.asset 的 derived navigation data 必须有效。");
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=grid-loaded elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} size={grid.Width}x{grid.Height} cell={grid.CellSize:F3} agentType={grid.AgentTypeId}");

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 256;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);

        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=source-applied elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
        ProcessWorldBuildQueueUntilReady();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=world-ready elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");

        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-begin elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
        Lv3RightBottomRoute route = ResolveLv3RightBottomRoute(grid, derivedData, diagnostics);
        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(route.ResearchCenterPosition);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-end elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} chasers={route.ChaserStarts.Length} hero={route.RightBottomCorner} research={route.ResearchCenterPosition}");

        SimEntityContext hero = CreateEntity(route.RightBottomCorner, false, grid.AgentTypeId, 0.45f);
        hero.Side = SideType.PlayerSide;
        int testedChasers = Mathf.Min(8, route.ChaserStarts.Length);
        SimEntityContext[] chasers = new SimEntityContext[testedChasers];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[testedChasers];
        SimMoveExecutor[] executors = new SimMoveExecutor[testedChasers];
        Assert.IsTrue(grid.WorldToCell(hero.Position, out int goalX, out int goalY), "英雄目标点必须在 Lv3 grid 内。");
        for (int i = 0; i < testedChasers; i++)
        {
            SimEntityContext chaser = CreateEntity(route.ChaserStarts[i], false, grid.AgentTypeId, 0.45f);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, (Fix64)(3.5f / DistanceUnitConverter.DefaultDistanceConversionRate));
            chaser.TargetComp = new SimTargetingComp(chaser, new System.Collections.Generic.List<IEntityContext> { hero })
            {
                CurrentTarget = hero
            };

            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = 0.5f
            };
            chaser.MoveExecutor = executor;

            Assert.IsTrue(grid.WorldToCell(chaser.Position, out _, out _), "追兵起点必须在 Lv3 grid 内。");
            chasers[i] = chaser;
            moveComps[i] = moveComp;
            executors[i] = executor;
        }

        int wallDirectedBeforePortalSamples = 0;
        int nearWallPushSamples = 0;
        int anyWallRaySamples = 0;
        int totalSamples = 0;
        float minWallHitBeforePortalDistance = float.PositiveInfinity;
        float minResearchCenterDistance = float.PositiveInfinity;
        const float dt = 0.1f;
        for (int frame = 1; frame <= 40; frame++)
        {
            Debug.Log($"[Lv3ResearchCenterChaseTest] stage=frame-begin frame={frame} elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1}");
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            if (frame > 1)
                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                moveComps[i].MoveTo(hero.Position);
                moveComps[i].Move(dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                Vector3 desired = executor.LastDesiredDisplacement;
                desired.y = 0f;
                Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
                Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
                int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
                bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                          && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
                float wallHitDistance = hitsResearchCenter
                    ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
                    : float.PositiveInfinity;
                bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
                float selectedPortalDistance = hasSelectedPortal
                    ? HorizontalDistance(decisionPosition, selectedPortalCenter)
                    : float.PositiveInfinity;
                float targetDistance = HorizontalDistance(decisionPosition, hero.Position);
                float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
                bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                                   && SegmentEntersFootprint(
                                                       researchCenter,
                                                       decisionPosition,
                                                       desiredDirection,
                                                       Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                                       grid.CellSize * 0.5f);
                bool hitsBeforePortal = hitsResearchCenter
                                        && wallHitDistance <= routeLimitDistance + 0.2f
                                        && segmentEntersResearchCenter;
                float researchCenterDistance = researchCenter.DistanceToClosestBox(decisionPosition);
                minResearchCenterDistance = Mathf.Min(minResearchCenterDistance, researchCenterDistance);
                bool nearWallPush = researchCenterDistance <= 0.65f
                                    && hitsResearchCenter
                                    && wallHitDistance <= 1.0f
                                    && desired.sqrMagnitude > 0.0004f;
                if (hitsResearchCenter)
                {
                    anyWallRaySamples++;
                    if (hitsBeforePortal)
                    {
                        wallDirectedBeforePortalSamples++;
                        minWallHitBeforePortalDistance = Mathf.Min(minWallHitBeforePortalDistance, wallHitDistance);
                    }

                    if (nearWallPush)
                        nearWallPushSamples++;
                }
                totalSamples++;

                if (hitsBeforePortal || nearWallPush || frame <= 4 || frame % 10 == 0)
                {
                    int sampleStart = diagnostics.Length;
                    diagnostics.Append("[Lv3ResearchCenterChase] frame=").Append(frame)
                        .Append(" agent=").Append(i)
                        .Append(" decisionPos=").Append(decisionPosition)
                        .Append(" afterPos=").Append(chaser.Position);
                    AppendCellDiagnostic(diagnostics, grid, derivedData, decisionPosition, "decision");
                    AppendCellDiagnostic(diagnostics, grid, derivedData, chaser.Position, "after");
                    diagnostics.Append(" hero=").Append(hero.Position)
                        .Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
                        .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
                        .Append(" desiredDir=").Append(desiredDirection)
                        .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
                        .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
                        .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
                        .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
                        .Append(" targetDist=").Append(targetDistance.ToString("F3"))
                        .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
                        .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
                        .Append(" researchDist=").Append(researchCenterDistance.ToString("F3"))
                        .Append(" nearWallPush=").Append(nearWallPush);
                    AppendSteeringBreakdown(diagnostics, chaser);
                    AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                    if (hitsBeforePortal)
                        AppendSegmentGridDiagnostics(diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
                    diagnostics.AppendLine();
                    if (hitsBeforePortal)
                        Debug.LogWarning("[Lv3WallBeforePortalSample] " + diagnostics.ToString(sampleStart, diagnostics.Length - sampleStart));
                }
            }
        }
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=assert elapsedMs={testWatch.Elapsed.TotalMilliseconds:F1} wallBeforePortal={wallDirectedBeforePortalSamples}/{totalSamples} nearWallPush={nearWallPushSamples} anyWallRay={anyWallRaySamples} minResearchDist={minResearchCenterDistance:F3}");

        Assert.LessOrEqual(wallDirectedBeforePortalSamples, 0,
            $"真实 Lv3 研发中心下侧追击不应在到达当前 portal 前就朝研发中心碰撞体推进。wallBeforePortal={wallDirectedBeforePortalSamples}/{totalSamples}, minHitDist={minWallHitBeforePortalDistance:F3}, anyWallRay={anyWallRaySamples}\n{diagnostics}");
        Assert.LessOrEqual(nearWallPushSamples, 0,
            $"真实 Lv3 研发中心下侧追击不应贴近研发中心后仍向碰撞体内部推进。nearWallPush={nearWallPushSamples}, minResearchDist={minResearchCenterDistance:F3}, anyWallRay={anyWallRaySamples}\n{diagnostics}");
    }

    private struct Lv3RightBottomRoute
    {
        public Vector3 HeroStart;
        public Vector3 LowerApproach;
        public Vector3 RightBottomCorner;
        public Vector3 ReturnPoint;
        public Vector3 ResearchCenterPosition;
        public Vector3[] ChaserStarts;
        public Rect ResearchCenterBounds;
        public Rect StrongholdBounds;
    }

    private struct Lv3PresetSnapshot
    {
        public Vector3 HeroStart;
        public Vector3 ResearchCenterPosition;
        public Vector3[] UnitSpawnCenters;
        public int[] UnitSpawnCounts;
        public int TotalUnitSpawnCount;
        public Rect ResearchCenterFootprintBounds;
        public Rect StrongholdSh13Bounds;
    }

    private struct Lv3UnitSpawnPreset
    {
        public Vector3 Position;
        public int Count;
    }

    private struct Lv3HeroPathNode
    {
        public Vector3 Preferred;
        public int HoldFrames;

        public Lv3HeroPathNode(Vector3 preferred, int holdFrames)
        {
            Preferred = preferred;
            HoldFrames = holdFrames;
        }
    }

    private struct Lv3CrowdRouteScenario
    {
        public string Name;
        public Vector3 HeroStart;
        public Lv3HeroPathNode[] HeroNodes;
        public Vector3[] ChaserCenters;
        public int[] ChaserCenterCounts;
        public int ChaserCount;
        public int Frames;
        public bool ExpectLowerRoute;
        public bool UseNaturalTargetAcquisition;
    }

    private sealed class Lv3CrowdRouteMetrics
    {
        public int WallBeforePortalSamples;
        public int ConstrainedWallStallSamples;
        public int StaleSteeringSamples;
        public int NearWallGoalSamples;
        public int UpperDetourSamples;
        public int MaxWallClusterStreak;
        public int MaxZeroStreak;
        public int MaxPendingFlowTiles;
        public int MaxPendingSharedGoals;
        public int MaxTargetedSlowStreak;
        public int ConstraintFailureSamples;
        public int OverlapPairSamples;
        public int MaxOverlapPairsThisFrame;
        public int MaxOverlapPairStreak;
        public int DetailedIssueSamples;
        public int DetailedBackgroundSamples;
        public float MinWallHitDistance = float.PositiveInfinity;
        public float MinFinalHeroDistance = float.PositiveInfinity;
        public readonly System.Text.StringBuilder Diagnostics = new System.Text.StringBuilder();
    }

    private static int ResolveDerivedSectorId(FlowNavigationGridAsset.DerivedNavigationData derivedData, int x, int y)
    {
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveDerivedSectorId failed: derivedData is invalid.");

        int sectorX = x / derivedData.SectorSizeInCells;
        int sectorY = y / derivedData.SectorSizeInCells;
        if (sectorX < 0 || sectorX >= derivedData.SectorCountX || sectorY < 0 || sectorY >= derivedData.SectorCountY)
            throw new InvalidOperationException($"ResolveDerivedSectorId failed: cell=({x},{y}) sector=({sectorX},{sectorY}) outside {derivedData.SectorCountX}x{derivedData.SectorCountY}.");

        FlowNavigationGridAsset.SectorDerivedData sector = derivedData.Sectors[sectorX + sectorY * derivedData.SectorCountX];
        return sector.SectorId;
    }

    private static void RunLv3ResearchCenterCrowdRouteScenario(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        int obstacleIdBase)
    {
        if (scenario.HeroNodes == null || scenario.HeroNodes.Length == 0)
            throw new InvalidOperationException($"RunLv3ResearchCenterCrowdRouteScenario failed: {scenario.Name} has no hero nodes.");
        if (scenario.ChaserCenters == null || scenario.ChaserCenters.Length == 0)
            throw new InvalidOperationException($"RunLv3ResearchCenterCrowdRouteScenario failed: {scenario.Name} has no chaser centers.");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSource(
            grid.AgentTypeId,
            grid.Width,
            grid.Height,
            grid.CellSize,
            grid.Origin,
            grid.GetWalkableMaskRuntimeReadOnlyReference(),
            grid.GetCellAnchorsRuntimeReadOnlyReference(),
            grid.GetCostFieldRuntimeReadOnlyReference(),
            grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
            grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
            useRuntimeReadOnlyReferences: true);
        ProcessWorldBuildQueueUntilReady();

        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        Rect researchBounds = researchCenter.CalculateBounds();
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, obstacleIdBase);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), $"真实路径矩阵 {scenario.Name} 必须先提交研发中心 Autobox runtime dirty。");

        const float agentRadius = 0.45f;
        const float edgeClearance = 0.5f;
        const float dt = 0.1f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 3.5f;
        Lv3CrowdRouteMetrics metrics = new Lv3CrowdRouteMetrics();
        metrics.Diagnostics.Append("[Lv3CrowdScenario] name=").Append(scenario.Name)
            .Append(" research=").Append(preset.ResearchCenterPosition)
            .Append(" bounds=").Append(researchBounds)
            .AppendLine();

        Vector3 heroStart = ResolveRuntimeLegalLv3MainIslandCell(
            grid,
            derivedData,
            scenario.HeroStart,
            6f,
            agentRadius,
            scenario.Name + "-hero-start",
            metrics.Diagnostics);
        Vector3[] heroWaypoints = new Vector3[scenario.HeroNodes.Length];
        for (int i = 0; i < scenario.HeroNodes.Length; i++)
        {
            heroWaypoints[i] = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                scenario.HeroNodes[i].Preferred,
                7f,
                agentRadius,
                scenario.Name + "-hero-node-" + i,
                metrics.Diagnostics);
        }

        Vector3[] chaserStarts = ResolveLv3ScenarioChaserStarts(
            grid,
            derivedData,
            scenario,
            agentRadius,
            metrics.Diagnostics);
        SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
        hero.Side = SideType.PlayerSide;

        SimEntityContext[] chasers = new SimEntityContext[chaserStarts.Length];
        CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserStarts.Length];
        SimMoveExecutor[] executors = new SimMoveExecutor[chaserStarts.Length];
        bool[] agentExpectsLowerRoute = new bool[chaserStarts.Length];
        var allEntities = new System.Collections.Generic.List<IEntityContext> { hero };
        Fix64 chaserSpeedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
        for (int i = 0; i < chaserStarts.Length; i++)
        {
            SimEntityContext chaser = CreateEntity(chaserStarts[i], false, grid.AgentTypeId, agentRadius);
            chaser.Side = SideType.EnemySide;
            chaser.SetProperty(CreatureMainProperty.Speed, chaserSpeedProperty);
            chasers[i] = chaser;
            agentExpectsLowerRoute[i] = chaserStarts[i].z <= researchBounds.yMax + 0.3f;
            allEntities.Add(chaser);
        }

        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
            {
                CurrentTarget = scenario.UseNaturalTargetAcquisition ? null : hero,
                AggroRange = 40f,
                ForgetRange = 60f
            };
            CharacterMoveComp moveComp = new CharacterMoveComp();
            moveComp.Init(chaser, grid.AgentTypeId);
            chaser.MoveComp = moveComp;
            SimMoveExecutor executor = new SimMoveExecutor
            {
                Position = chaser.Position,
                ApplyNavigationConstraint = true,
                AgentTypeId = grid.AgentTypeId,
                EdgeClearance = edgeClearance
            };
            chaser.MoveExecutor = executor;
            SoldierAIBrain brain = new SoldierAIBrain
            {
                DetectEnemyRange = 40f,
                WeaponRange = 0.75f,
                ChaseRange = 80f
            };
            brain.SetBirthPosition(chaser.Position);
            brain.Inject();
            chaser.Brain = brain;
            moveComps[i] = moveComp;
            executors[i] = executor;
            EntityRegistry.Register(chaser);
        }
        EntityRegistry.Register(hero);

        int[] zeroStreak = new int[chasers.Length];
        int[] maxZeroStreak = new int[chasers.Length];
        int waypointIndex = 0;
        int waypointHoldFrames = 0;
        int wallClusterStreak = 0;
        int targetedSlowStreak = 0;
        int overlapPairStreak = 0;
        for (int frame = 1; frame <= scenario.Frames; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
            Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
            hero.Position = AdvanceHeroWithNavigationConstraint(
                hero.Position,
                activeWaypoint,
                heroSpeed,
                dt,
                grid.AgentTypeId,
                edgeClearance,
                metrics.Diagnostics);
            Vector3 toWaypoint = activeWaypoint - hero.Position;
            toWaypoint.y = 0f;
            if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
            {
                int holdFrames = Mathf.Max(0, scenario.HeroNodes[waypointIndex].HoldFrames);
                if (waypointHoldFrames < holdFrames)
                    waypointHoldFrames++;
                else
                {
                    waypointIndex++;
                    waypointHoldFrames = 0;
                }
            }
            hero.SyncPositionToExecutor();

            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            metrics.MaxPendingFlowTiles = Mathf.Max(metrics.MaxPendingFlowTiles, FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount());
            metrics.MaxPendingSharedGoals = Mathf.Max(metrics.MaxPendingSharedGoals, FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount());

            int slowWallAgentsThisFrame = 0;
            int slowTargetedAgentsThisFrame = 0;
            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                SimMoveExecutor executor = executors[i];
                chaser.SyncPositionToExecutor();
                Vector3 decisionPosition = chaser.Position;
                if (chaser.TargetComp is SimTargetingComp targeting)
                    targeting.UpdateTargeting(dt);
                if (chaser.Brain is SoldierAIBrain brain)
                    brain.Tick(chaser, dt);
                moveComps[i].Move(dt);
                executor.Execute(dt);
                chaser.SyncPositionFromExecutor();

                CollectLv3CrowdRouteSample(
                    scenario,
                    metrics,
                    grid,
                    derivedData,
                    researchCenter,
                    researchBounds,
                    chaser,
                    executor,
                    decisionPosition,
                    hero,
                    hero.Position,
                    frame,
                    i,
                    agentExpectsLowerRoute[i],
                    zeroStreak,
                    maxZeroStreak,
                    ref slowWallAgentsThisFrame,
                    ref slowTargetedAgentsThisFrame);
            }

            int overlapPairsThisFrame = CollectLv3CrowdRouteOverlapSamples(
                scenario,
                metrics,
                grid,
                derivedData,
                chasers,
                hero,
                hero.Position,
                frame,
                agentRadius,
                overlapPairStreak);
            metrics.OverlapPairSamples += overlapPairsThisFrame;
            metrics.MaxOverlapPairsThisFrame = Mathf.Max(metrics.MaxOverlapPairsThisFrame, overlapPairsThisFrame);
            if (overlapPairsThisFrame >= 3)
                overlapPairStreak++;
            else
                overlapPairStreak = 0;
            metrics.MaxOverlapPairStreak = Mathf.Max(metrics.MaxOverlapPairStreak, overlapPairStreak);

            if (slowWallAgentsThisFrame >= 3)
                wallClusterStreak++;
            else
                wallClusterStreak = 0;
            metrics.MaxWallClusterStreak = Mathf.Max(metrics.MaxWallClusterStreak, wallClusterStreak);
            if (slowTargetedAgentsThisFrame >= 3)
                targetedSlowStreak++;
            else
                targetedSlowStreak = 0;
            metrics.MaxTargetedSlowStreak = Mathf.Max(metrics.MaxTargetedSlowStreak, targetedSlowStreak);

            if (waypointIndex >= heroWaypoints.Length - 1)
            {
                for (int i = 0; i < chasers.Length; i++)
                    metrics.MinFinalHeroDistance = Mathf.Min(metrics.MinFinalHeroDistance, HorizontalDistance(chasers[i].Position, hero.Position));
            }

        }

        for (int i = 0; i < maxZeroStreak.Length; i++)
            metrics.MaxZeroStreak = Mathf.Max(metrics.MaxZeroStreak, maxZeroStreak[i]);

        Debug.LogWarning(
            $"[Lv3CrowdScenarioSummary] name={scenario.Name} wallBeforePortal={metrics.WallBeforePortalSamples} " +
            $"stall={metrics.ConstrainedWallStallSamples} stale={metrics.StaleSteeringSamples} nearWallGoal={metrics.NearWallGoalSamples} " +
            $"upperDetour={metrics.UpperDetourSamples} maxWallClusterStreak={metrics.MaxWallClusterStreak} " +
            $"maxZeroStreak={metrics.MaxZeroStreak} maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak} " +
            $"constraintFail={metrics.ConstraintFailureSamples} overlapPairs={metrics.OverlapPairSamples} " +
            $"maxOverlapPairsFrame={metrics.MaxOverlapPairsThisFrame} maxOverlapPairStreak={metrics.MaxOverlapPairStreak} " +
            $"minFinalHeroDist={metrics.MinFinalHeroDistance:F3} " +
            $"pendingFlow={metrics.MaxPendingFlowTiles} pendingShared={metrics.MaxPendingSharedGoals}");

        Assert.LessOrEqual(metrics.WallBeforePortalSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中追兵不应在到达当前 portal 前朝研发中心碰撞体推进。wallBeforePortal={metrics.WallBeforePortalSamples}, minHit={metrics.MinWallHitDistance:F3}, pendingFlow={metrics.MaxPendingFlowTiles}, pendingShared={metrics.MaxPendingSharedGoals}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.ConstrainedWallStallSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中追兵不应贴研发中心墙侧被导航约束吃掉位移。stall={metrics.ConstrainedWallStallSamples}, maxZeroStreak={metrics.MaxZeroStreak}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.StaleSteeringSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中 Combat 链路不应长时间停用 steering。stale={metrics.StaleSteeringSamples}\n{metrics.Diagnostics}");
        Assert.LessOrEqual(metrics.NearWallGoalSamples, 0,
            $"真实路径矩阵 {scenario.Name} 中 steering 目标不应落到研发中心碰撞体近距离内。nearWallGoal={metrics.NearWallGoalSamples}\n{metrics.Diagnostics}");
        if (scenario.ExpectLowerRoute)
        {
            Assert.LessOrEqual(metrics.UpperDetourSamples, 0,
                $"真实路径矩阵 {scenario.Name} 中英雄在研发中心下侧/右下时不应大量选择上侧远路。upperDetour={metrics.UpperDetourSamples}\n{metrics.Diagnostics}");
        }
        Assert.LessOrEqual(metrics.MaxWallClusterStreak, 10,
            $"真实路径矩阵 {scenario.Name} 中不应出现多人贴研发中心墙侧长期聚团低速。maxWallClusterStreak={metrics.MaxWallClusterStreak}\n{metrics.Diagnostics}");
        if (scenario.UseNaturalTargetAcquisition)
        {
            Assert.LessOrEqual(metrics.ConstraintFailureSamples, 0,
                $"真实路径矩阵 {scenario.Name} 中已锁定英雄且仍主动移动的单位不应被导航约束直接拒绝位移。constraintFail={metrics.ConstraintFailureSamples}, maxZeroStreak={metrics.MaxZeroStreak}, maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}\n{metrics.Diagnostics}");
            Assert.LessOrEqual(metrics.MaxZeroStreak, 12,
                $"真实路径矩阵 {scenario.Name} 中锁定英雄后的单位不应长期主动移动但被导航约束压成零位移。maxZeroStreak={metrics.MaxZeroStreak}, maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}\n{metrics.Diagnostics}");
            Assert.LessOrEqual(metrics.MaxTargetedSlowStreak, 8,
                $"真实路径矩阵 {scenario.Name} 中不应出现多个已锁定英雄的单位持续主动移动但整体低速。maxTargetedSlowStreak={metrics.MaxTargetedSlowStreak}, maxZeroStreak={metrics.MaxZeroStreak}\n{metrics.Diagnostics}");
        }
        Assert.Less(metrics.MinFinalHeroDistance, 5.0f,
            $"真实路径矩阵 {scenario.Name} 必须至少有追兵接近最终英雄位置，否则测试没有覆盖追击链路。minFinalHeroDist={metrics.MinFinalHeroDistance:F3}\n{metrics.Diagnostics}");
    }

    private static void RunLv3CharacterControllerCombatScenario(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset spawnGrid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        int obstacleIdBase)
    {
        System.Diagnostics.Stopwatch scenarioWatch = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=begin frames={scenario.Frames}");
        if (scenario.HeroNodes == null || scenario.HeroNodes.Length == 0)
            throw new InvalidOperationException($"RunLv3CharacterControllerCombatScenario failed: {scenario.Name} has no hero nodes.");

        EntityRegistry.Clear();
        FlowFieldCrowdMovementSystem.ResetAll();
        FlowFieldCrowdMovementSystem.ClearEditorTestNavigationSource();

        FlowFieldNavigationConfig config = CreateConfig();
        config.SectorSizeInCells = derivedData.ConfigSectorSizeInCells;
        config.PortalNarrowWidthCells = derivedData.ConfigPortalNarrowWidthCells;
        config.PortalMaxWindowWidthCells = derivedData.ConfigPortalMaxWindowWidthCells;
        config.FlowTileCacheLimit = 320;
        config.RuntimeRebuildBudgetMilliseconds = 4f;
        FlowFieldCrowdMovementSystem.SetConfig(config);
        if (spawnGrid == null)
            throw new InvalidOperationException("RunLv3CharacterControllerCombatScenario failed: spawnGrid is null.");
        FlowFieldCrowdMovementSystem.SetAuthoredNavigationSources(new[]
        {
            new AuthoredNavigationSourceData(
                spawnGrid.AgentTypeId,
                spawnGrid.Width,
                spawnGrid.Height,
                spawnGrid.CellSize,
                spawnGrid.Origin,
                spawnGrid.GetWalkableMaskRuntimeReadOnlyReference(),
                spawnGrid.GetCellAnchorsRuntimeReadOnlyReference(),
                spawnGrid.GetCostFieldRuntimeReadOnlyReference(),
                spawnGrid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
                spawnGrid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
                useRuntimeReadOnlyReferences: true),
            new AuthoredNavigationSourceData(
                grid.AgentTypeId,
                grid.Width,
                grid.Height,
                grid.CellSize,
                grid.Origin,
                grid.GetWalkableMaskRuntimeReadOnlyReference(),
                grid.GetCellAnchorsRuntimeReadOnlyReference(),
                grid.GetCostFieldRuntimeReadOnlyReference(),
                grid.GetNeighborTraversalMaskRuntimeReadOnlyReference(),
                grid.GetDerivedNavigationDataRuntimeReadOnlyReference(),
                useRuntimeReadOnlyReferences: true),
        });
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=source-applied elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
        ProcessAllWorldBuildQueuesUntilReady();
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=world-ready elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        Lv3ResearchCenterFootprint researchCenter = CreateLv3ResearchCenterLv1Footprint(preset.ResearchCenterPosition);
        RegisterLv3ResearchCenterFootprintObstacles(researchCenter, obstacleIdBase);
        for (int i = 0; i < 256 && FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(); i++)
            FlowFieldCrowdMovementSystem.ProcessRuntimeRebuildQueue();
        Assert.IsFalse(FlowFieldCrowdMovementSystem.HasEditorTestPendingRuntimeDirty(), $"真实 CC Combat 场景 {scenario.Name} 必须先提交研发中心 Autobox runtime dirty。");
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=runtime-dirty-ready elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        const float agentRadius = 0.18f;
        const float edgeClearance = 0.18f;
        const float dt = 0.1f;
        const float heroSpeed = 2.5f;
        const float chaserSpeed = 4.2f;
        System.Text.StringBuilder diagnostics = new System.Text.StringBuilder(4096);
        diagnostics.Append("[Lv3ControllerCombatScenario] name=").Append(scenario.Name)
            .Append(" research=").Append(preset.ResearchCenterPosition)
            .Append(" researchBounds=").Append(preset.ResearchCenterFootprintBounds)
            .Append(" stronghold=").Append(preset.StrongholdSh13Bounds)
            .AppendLine();

        Vector3 heroStart = ResolveRuntimeLegalLv3MainIslandCell(
            grid,
            derivedData,
            scenario.HeroStart,
            6f,
            agentRadius,
            scenario.Name + "-hero-start",
            diagnostics);
        Vector3[] heroWaypoints = new Vector3[scenario.HeroNodes.Length];
        for (int i = 0; i < scenario.HeroNodes.Length; i++)
        {
            heroWaypoints[i] = ResolveRuntimeLegalLv3MainIslandCell(
                grid,
                derivedData,
                scenario.HeroNodes[i].Preferred,
                7f,
                agentRadius,
                scenario.Name + "-hero-node-" + i,
                diagnostics);
        }

        Vector3[] chaserStarts = ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(
            grid,
            derivedData,
            scenario,
            agentRadius,
            diagnostics);
        Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=points-resolved chasers={chaserStarts.Length} heroNodes={heroWaypoints.Length} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

        List<GameObject> createdObjects = new List<GameObject>();
        try
        {
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=physics-build-begin elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
            CreateLv3CharacterControllerTestPhysics(
                grid,
                derivedData,
                researchCenter,
                preset,
                scenario,
                heroStart,
                heroWaypoints,
                chaserStarts,
                createdObjects,
                diagnostics);
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=physics-build-end createdObjects={createdObjects.Count} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

            SimEntityContext hero = CreateEntity(heroStart, false, grid.AgentTypeId, agentRadius);
            hero.Side = SideType.PlayerSide;
            hero.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate));
            int characterLayer = ResolveRequiredLayer("character");
            GameObject heroCollision = new GameObject("FlowTest_CC_Combat_HeroCollider");
            heroCollision.layer = characterLayer;
            heroCollision.transform.position = heroStart;
            CapsuleCollider heroCollider = heroCollision.AddComponent<CapsuleCollider>();
            heroCollider.radius = agentRadius;
            heroCollider.height = 2f;
            heroCollider.center = new Vector3(0f, 1f, 0f);
            createdObjects.Add(heroCollision);

            SimEntityContext[] chasers = new SimEntityContext[chaserStarts.Length];
            CharacterMoveComp[] moveComps = new CharacterMoveComp[chaserStarts.Length];
            MoveExecutor[] executors = new MoveExecutor[chaserStarts.Length];
            Transform[] transforms = new Transform[chaserStarts.Length];
            CharacterController[] controllers = new CharacterController[chaserStarts.Length];
            List<IEntityContext> allEntities = new List<IEntityContext>(chaserStarts.Length + 1) { hero };
            Fix64 speedProperty = (Fix64)(chaserSpeed / DistanceUnitConverter.DefaultDistanceConversionRate);
            Fix64 radiusProperty = (Fix64)(agentRadius / DistanceUnitConverter.DefaultDistanceConversionRate);
            for (int i = 0; i < chaserStarts.Length; i++)
            {
                SimEntityContext chaser = CreateEntity(chaserStarts[i], false, grid.AgentTypeId, agentRadius);
                chaser.Side = SideType.EnemySide;
                chaser.SetProperty(CreatureMainProperty.Speed, speedProperty);
                chaser.SetProperty(CreatureMainProperty.CollisionRadius, radiusProperty);
                chasers[i] = chaser;
                allEntities.Add(chaser);
            }

            for (int i = 0; i < chasers.Length; i++)
            {
                SimEntityContext chaser = chasers[i];
                chaser.TargetComp = new SimTargetingComp(chaser, allEntities)
                {
                    CurrentTarget = scenario.UseNaturalTargetAcquisition ? null : hero,
                    AggroRange = 40f,
                    ForgetRange = 60f
                };
                CharacterMoveComp moveComp = new CharacterMoveComp();
                moveComp.Init(chaser, grid.AgentTypeId);
                chaser.MoveComp = moveComp;
                SoldierAIBrain brain = new SoldierAIBrain
                {
                    DetectEnemyRange = 40f,
                    WeaponRange = 0.75f,
                    ChaseRange = 80f
                };
                brain.SetBirthPosition(chaser.Position);
                brain.Inject();
                chaser.Brain = brain;

                GameObject go = new GameObject("FlowTest_CC_Combat_Chaser_" + i);
                go.layer = characterLayer;
                go.transform.position = chaser.Position;
                CharacterController controller = go.AddComponent<CharacterController>();
                controller.radius = agentRadius;
                controller.height = 2f;
                controller.center = new Vector3(0f, 1f, 0f);
                MoveExecutor executor = go.AddComponent<MoveExecutor>();
                executor.Init(controller, grid.AgentTypeId);
                chaser.MoveExecutor = executor;
                controllers[i] = controller;
                transforms[i] = go.transform;
                moveComps[i] = moveComp;
                executors[i] = executor;
                createdObjects.Add(go);
                EntityRegistry.Register(chaser);
            }
            EntityRegistry.Register(hero);
            Physics.SyncTransforms();
            Debug.Log($"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=agents-ready chasers={chasers.Length} elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");

            int waypointIndex = 0;
            int waypointHoldFrames = 0;
            int[] stallStreak = new int[chasers.Length];
            int[] maxStallStreak = new int[chasers.Length];
            int[] reverseSteeringStreak = new int[chasers.Length];
            int[] maxReverseSteeringStreak = new int[chasers.Length];
            int stallSamples = 0;
            int wallStallSamples = 0;
            int crowdWallStreak = 0;
            int maxCrowdWallStreak = 0;
            int targetedSlowStreak = 0;
            int maxTargetedSlowStreak = 0;
            int overlapPairSamples = 0;
            int maxOverlapPairsThisFrame = 0;
            int overlapPairStreak = 0;
            int maxOverlapPairStreak = 0;
            int detailedSamples = 0;
            float minHeroDistance = float.PositiveInfinity;
            for (int frame = 1; frame <= scenario.Frames; frame++)
            {
                if (scenarioWatch.Elapsed.TotalSeconds > 90d)
                    throw new TimeoutException($"Lv3 controller combat scenario timed out: scenario={scenario.Name}, frame={frame}, elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}.");
                bool logFrame = frame == 1 || frame % 60 == 0 || frame == scenario.Frames;
                if (logFrame)
                {
                    Debug.Log(
                        $"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=frame-begin frame={frame} " +
                        $"pendingFlow={FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount()} " +
                        $"pendingShared={FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount()} " +
                        $"elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
                }
                FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
                Vector3 activeWaypoint = heroWaypoints[Mathf.Min(waypointIndex, heroWaypoints.Length - 1)];
                hero.Position = AdvanceHeroWithNavigationConstraint(
                    hero.Position,
                    activeWaypoint,
                    heroSpeed,
                    dt,
                    grid.AgentTypeId,
                    edgeClearance,
                    diagnostics);
                heroCollision.transform.position = hero.Position;
                Vector3 toWaypoint = activeWaypoint - hero.Position;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude <= 0.35f * 0.35f && waypointIndex < heroWaypoints.Length - 1)
                {
                    int holdFrames = Mathf.Max(0, scenario.HeroNodes[waypointIndex].HoldFrames);
                    if (waypointHoldFrames < holdFrames)
                        waypointHoldFrames++;
                    else
                    {
                        waypointIndex++;
                        waypointHoldFrames = 0;
                    }
                }

                FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
                int slowTargetedThisFrame = 0;
                int slowWallThisFrame = 0;
                for (int i = 0; i < chasers.Length; i++)
                {
                    SimEntityContext chaser = chasers[i];
                    MoveExecutor executor = executors[i];
                    chaser.Position = transforms[i].position;
                    Vector3 before = chaser.Position;
                    if (chaser.TargetComp is SimTargetingComp targeting)
                        targeting.UpdateTargeting(dt);
                    if (chaser.Brain is SoldierAIBrain brain)
                    {
                        try
                        {
                            brain.Tick(chaser, dt);
                        }
                        catch (Exception ex)
                        {
                            System.Text.StringBuilder failureDiagnostics = new System.Text.StringBuilder(4096);
                            AppendControllerCombatTickFailureDiagnostics(
                                failureDiagnostics,
                                scenario,
                                frame,
                                i,
                                chaser,
                                hero,
                                grid,
                                derivedData,
                                before,
                                hero.Position,
                                waypointIndex,
                                heroWaypoints.Length,
                                researchCenter);
                            Debug.LogError(failureDiagnostics.ToString());
                            throw new InvalidOperationException(
                                $"Lv3 controller combat tick failed scenario={scenario.Name} frame={frame} agent={i}: {ex.Message}\n{failureDiagnostics}",
                                ex);
                        }
                    }
                    moveComps[i].Move(dt);
                    Vector3 requestedVelocity = executor.DebugInputVelocity;
                    executor.Execute(dt);
                    Vector3 after = transforms[i].position;
                    chaser.Position = after;
                    FlowFieldCrowdMovementSystem.UpdateAgentForEditorTest(chaser, agentRadius, grid.AgentTypeId);

                    Vector3 actual = after - before;
                    actual.y = 0f;
                    Vector3 requestedHorizontalDisplacement = executor.DebugRequestedHorizontalDisplacement;
                    requestedHorizontalDisplacement.y = 0f;
                    Vector3 constrainedHorizontalDisplacement = executor.DebugConstrainedHorizontalDisplacement;
                    constrainedHorizontalDisplacement.y = 0f;
                    float heroDistance = HorizontalDistance(after, hero.Position);
                    float researchDistance = researchCenter.DistanceToClosestBox(after);
                    bool hasHeroTarget = chaser.TargetComp is SimTargetingComp targetingComp && ReferenceEquals(targetingComp.CurrentTarget, hero);
                    bool activeButStopped = hasHeroTarget
                                            && heroDistance > 1.25f
                                            && requestedHorizontalDisplacement.magnitude > chaserSpeed * dt * 0.35f
                                            && actual.magnitude < chaserSpeed * dt * 0.12f;
                    bool reverseSteering = false;
                    if (hasHeroTarget
                        && heroDistance > 1.25f
                        && FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                            chaser.GetHashCode(),
                            out Vector3 desiredVelocity,
                            out _,
                            out _,
                            out _,
                            out _,
                            out _,
                            out _,
                            out Vector3 resultVelocity)
                        && desiredVelocity.sqrMagnitude > chaserSpeed * chaserSpeed * 0.25f
                        && resultVelocity.sqrMagnitude > 0.01f)
                    {
                        reverseSteering = Vector3.Dot(desiredVelocity.normalized, resultVelocity.normalized) < -0.15f;
                    }
                    reverseSteeringStreak[i] = reverseSteering ? reverseSteeringStreak[i] + 1 : 0;
                    maxReverseSteeringStreak[i] = Mathf.Max(maxReverseSteeringStreak[i], reverseSteeringStreak[i]);
                    stallStreak[i] = activeButStopped ? stallStreak[i] + 1 : 0;
                    maxStallStreak[i] = Mathf.Max(maxStallStreak[i], stallStreak[i]);
                    if (activeButStopped)
                    {
                        stallSamples++;
                        slowTargetedThisFrame++;
                        if (researchDistance <= 1.05f)
                        {
                            wallStallSamples++;
                            slowWallThisFrame++;
                        }

                        if (detailedSamples < 32)
                        {
                            detailedSamples++;
                            diagnostics.Append("[Lv3ControllerCombatStall] scenario=").Append(scenario.Name)
                                .Append(" frame=").Append(frame)
                                .Append(" agent=").Append(i)
                                .Append(" before=").Append(before)
                                .Append(" after=").Append(after)
                                .Append(" hero=").Append(hero.Position)
                                .Append(" requestedVelocity=").Append(requestedVelocity)
                                .Append(" requestedDisp=").Append(requestedHorizontalDisplacement)
                                .Append(" constrainedDisp=").Append(constrainedHorizontalDisplacement)
                                .Append(" constraintEnabled=").Append(executor.DebugNavigationConstraintEnabled)
                                .Append(" actual=").Append(actual)
                                .Append(" heroDist=").Append(heroDistance.ToString("F3"))
                                .Append(" researchDist=").Append(researchDistance.ToString("F3"))
                                .Append(" stallStreak=").Append(stallStreak[i]);
                            AppendCellDiagnostic(diagnostics, grid, derivedData, before, "before");
                            AppendCellDiagnostic(diagnostics, grid, derivedData, after, "after");
                            AppendCharacterControllerPhysicsDiagnostics(diagnostics, controllers[i], executor);
                            AppendSteeringBreakdown(diagnostics, chaser);
                            AppendControllerCandidateProbeDiagnostics(diagnostics, controllers[i], chaser, dt);
                            AppendPathHandleDiagnostics(diagnostics, chaser, grid, derivedData, hero.Position);
                            diagnostics.AppendLine();
                        }
                    }

                    minHeroDistance = Mathf.Min(minHeroDistance, heroDistance);
                }

                int overlapPairsThisFrame = CollectLv3ControllerOverlapSamples(
                    diagnostics,
                    grid,
                    derivedData,
                    transforms,
                    hero.Position,
                    frame,
                    agentRadius,
                    overlapPairStreak,
                    overlapPairSamples,
                    chasers,
                    hero);
                overlapPairSamples += overlapPairsThisFrame;
                maxOverlapPairsThisFrame = Mathf.Max(maxOverlapPairsThisFrame, overlapPairsThisFrame);
                if (overlapPairsThisFrame >= 3)
                    overlapPairStreak++;
                else
                    overlapPairStreak = 0;
                maxOverlapPairStreak = Mathf.Max(maxOverlapPairStreak, overlapPairStreak);

                if (slowWallThisFrame >= 3)
                    crowdWallStreak++;
                else
                    crowdWallStreak = 0;
                maxCrowdWallStreak = Mathf.Max(maxCrowdWallStreak, crowdWallStreak);
                if (slowTargetedThisFrame >= 3)
                    targetedSlowStreak++;
                else
                    targetedSlowStreak = 0;
                maxTargetedSlowStreak = Mathf.Max(maxTargetedSlowStreak, targetedSlowStreak);
                if (logFrame)
                {
                    Debug.Log(
                        $"[Lv3ControllerCombatProgress] scenario={scenario.Name} stage=frame-end frame={frame} " +
                        $"waypoint={waypointIndex}/{heroWaypoints.Length - 1} slowWall={slowWallThisFrame} slowTargeted={slowTargetedThisFrame} " +
                        $"overlapPairs={overlapPairsThisFrame} minHeroDist={minHeroDistance:F3} " +
                        $"pendingFlow={FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount()} " +
                        $"pendingShared={FlowFieldCrowdMovementSystem.GetEditorTestPendingSharedGoalFieldBuildCount()} " +
                        $"elapsedMs={scenarioWatch.Elapsed.TotalMilliseconds:F1}");
                }
            }

            int maxSingleStallStreak = 0;
            int maxSingleReverseSteeringStreak = 0;
            for (int i = 0; i < maxStallStreak.Length; i++)
            {
                maxSingleStallStreak = Mathf.Max(maxSingleStallStreak, maxStallStreak[i]);
                maxSingleReverseSteeringStreak = Mathf.Max(maxSingleReverseSteeringStreak, maxReverseSteeringStreak[i]);
            }

            Debug.LogWarning(
                $"[Lv3ControllerCombatScenarioSummary] name={scenario.Name} stall={stallSamples} wallStall={wallStallSamples} " +
                $"maxSingleStallStreak={maxSingleStallStreak} maxCrowdWallStreak={maxCrowdWallStreak} " +
                $"maxTargetedSlowStreak={maxTargetedSlowStreak} maxReverseSteeringStreak={maxSingleReverseSteeringStreak} overlapPairs={overlapPairSamples} " +
                $"maxOverlapPairsFrame={maxOverlapPairsThisFrame} maxOverlapPairStreak={maxOverlapPairStreak} minHeroDist={minHeroDistance:F3}");

            Assert.LessOrEqual(maxSingleStallStreak, 8,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现单兵长期主动追击但被建筑/边界卡住。maxStall={maxSingleStallStreak}, stall={stallSamples}, wallStall={wallStallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxCrowdWallStreak, 4,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现多人连续贴建筑或地形边界挤住。maxCrowdWallStreak={maxCrowdWallStreak}, wallStall={wallStallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxTargetedSlowStreak, 6,
                $"真实 CC Combat 场景 {scenario.Name} 不应出现多个已锁定英雄的单位持续低速蠕动。maxTargetedSlowStreak={maxTargetedSlowStreak}, stall={stallSamples}\n{diagnostics}");
            Assert.LessOrEqual(maxSingleReverseSteeringStreak, 3,
                $"真实 CC Combat 场景 {scenario.Name} 中避让不能连续反转主路径方向。maxReverseSteeringStreak={maxSingleReverseSteeringStreak}\n{diagnostics}");
            Assert.LessOrEqual(maxOverlapPairsThisFrame, 4,
                $"真实 CC Combat 场景 {scenario.Name} 不应允许大量追兵在英雄附近叠成一团。overlapPairs={overlapPairSamples}, maxFrame={maxOverlapPairsThisFrame}, maxStreak={maxOverlapPairStreak}\n{diagnostics}");
            Assert.Less(minHeroDistance, 5f,
                $"真实 CC Combat 场景 {scenario.Name} 必须实际接近英雄，否则测试未覆盖接敌链路。minHeroDist={minHeroDistance:F3}\n{diagnostics}");
        }
        finally
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
    }

    private static void CreateLv3CharacterControllerTestPhysics(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint researchCenter,
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        List<GameObject> createdObjects,
        System.Text.StringBuilder diagnostics)
    {
        int groundLayer = ResolveRequiredLayer("Ground");
        GameObject ground = new GameObject("FlowTest_CC_Combat_Ground");
        ground.layer = groundLayer;
        ground.transform.position = new Vector3(
            grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
            -0.055f,
            grid.Origin.y + grid.Height * grid.CellSize * 0.5f);
        BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
        groundCollider.size = new Vector3(grid.Width * grid.CellSize + 24f, 0.1f, grid.Height * grid.CellSize + 24f);
        createdObjects.Add(ground);

        for (int i = 0; i < researchCenter.Boxes.Length; i++)
        {
            Rect box = researchCenter.Boxes[i];
            GameObject obstacle = new GameObject("FlowTest_CC_Combat_ResearchCenter_" + i);
            obstacle.layer = groundLayer;
            obstacle.transform.position = new Vector3(box.center.x, 1f, box.center.y);
            BoxCollider collider = obstacle.AddComponent<BoxCollider>();
            collider.size = new Vector3(box.width, 2f, box.height);
            createdObjects.Add(obstacle);
        }

        Rect region = BuildLv3ControllerCombatPhysicsRegion(preset, scenario, heroStart, heroWaypoints, chaserStarts, 8f);
        int staticBlockers = CreateGridStaticNavigationBlockers(grid, derivedData, region, heroStart, heroWaypoints, chaserStarts, createdObjects, groundLayer);
        diagnostics.Append("[Lv3ControllerCombatPhysics] scenario=").Append(scenario.Name)
            .Append(" region=").Append(region)
            .Append(" staticBlockers=").Append(staticBlockers)
            .AppendLine();
        Physics.SyncTransforms();
    }

    private static Rect BuildLv3ControllerCombatPhysicsRegion(
        Lv3PresetSnapshot preset,
        Lv3CrowdRouteScenario scenario,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        float margin)
    {
        float minX = Mathf.Min(preset.ResearchCenterFootprintBounds.xMin, preset.StrongholdSh13Bounds.xMin);
        float minZ = Mathf.Min(preset.ResearchCenterFootprintBounds.yMin, preset.StrongholdSh13Bounds.yMin);
        float maxX = Mathf.Max(preset.ResearchCenterFootprintBounds.xMax, preset.StrongholdSh13Bounds.xMax);
        float maxZ = Mathf.Max(preset.ResearchCenterFootprintBounds.yMax, preset.StrongholdSh13Bounds.yMax);
        IncludePointInBounds(heroStart, ref minX, ref minZ, ref maxX, ref maxZ);
        for (int i = 0; i < heroWaypoints.Length; i++)
            IncludePointInBounds(heroWaypoints[i], ref minX, ref minZ, ref maxX, ref maxZ);
        for (int i = 0; i < chaserStarts.Length; i++)
            IncludePointInBounds(chaserStarts[i], ref minX, ref minZ, ref maxX, ref maxZ);
        if (scenario.ChaserCenters != null)
        {
            for (int i = 0; i < scenario.ChaserCenters.Length; i++)
                IncludePointInBounds(scenario.ChaserCenters[i], ref minX, ref minZ, ref maxX, ref maxZ);
        }

        return Rect.MinMaxRect(minX - margin, minZ - margin, maxX + margin, maxZ + margin);
    }

    private static void IncludePointInBounds(Vector3 point, ref float minX, ref float minZ, ref float maxX, ref float maxZ)
    {
        minX = Mathf.Min(minX, point.x);
        minZ = Mathf.Min(minZ, point.z);
        maxX = Mathf.Max(maxX, point.x);
        maxZ = Mathf.Max(maxZ, point.z);
    }

    private static int CreateGridStaticNavigationBlockers(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Rect worldRegion,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts,
        List<GameObject> createdObjects,
        int blockerLayer)
    {
        const int maxStaticBlockerColliders = 1400;
        int minX = Mathf.Clamp(Mathf.FloorToInt((worldRegion.xMin - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((worldRegion.xMax - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((worldRegion.yMin - grid.Origin.y) / grid.CellSize), 0, grid.Height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((worldRegion.yMax - grid.Origin.y) / grid.CellSize), 0, grid.Height - 1);
        HashSet<int> blockerCells = CollectGridStaticNavigationBlockerCells(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            heroStart,
            heroWaypoints,
            chaserStarts);
        Dictionary<long, ActiveNavigationBlockerSpan> active = new Dictionary<long, ActiveNavigationBlockerSpan>();
        List<long> finishedKeys = new List<long>();
        int colliderCount = 0;
        for (int y = minY; y <= maxY; y++)
        {
            foreach (ActiveNavigationBlockerSpan span in active.Values)
                span.SeenInCurrentRow = false;

            int x = minX;
            while (x <= maxX)
            {
                while (x <= maxX && !blockerCells.Contains(y * grid.Width + x))
                    x++;
                if (x > maxX)
                    break;

                int startX = x;
                while (x <= maxX && blockerCells.Contains(y * grid.Width + x))
                    x++;
                int endX = x - 1;
                long key = PackBlockerSpanKey(startX, endX);
                if (active.TryGetValue(key, out ActiveNavigationBlockerSpan existing))
                {
                    existing.EndY = y;
                    existing.SeenInCurrentRow = true;
                }
                else
                {
                    active.Add(key, new ActiveNavigationBlockerSpan
                    {
                        StartX = startX,
                        EndX = endX,
                        StartY = y,
                        EndY = y,
                        SeenInCurrentRow = true
                    });
                }
            }

            finishedKeys.Clear();
            foreach (KeyValuePair<long, ActiveNavigationBlockerSpan> pair in active)
            {
                if (pair.Value.SeenInCurrentRow)
                    continue;

                CreateGridStaticNavigationBlocker(grid, pair.Value, createdObjects, colliderCount++, blockerLayer);
                if (colliderCount > maxStaticBlockerColliders)
                    throw new InvalidOperationException($"CreateGridStaticNavigationBlockers failed: generated too many boundary colliders ({colliderCount}) for region={worldRegion}. The test physics proxy is too broad.");
                finishedKeys.Add(pair.Key);
            }

            for (int i = 0; i < finishedKeys.Count; i++)
                active.Remove(finishedKeys[i]);
        }

        foreach (ActiveNavigationBlockerSpan span in active.Values)
        {
            CreateGridStaticNavigationBlocker(grid, span, createdObjects, colliderCount++, blockerLayer);
            if (colliderCount > maxStaticBlockerColliders)
                throw new InvalidOperationException($"CreateGridStaticNavigationBlockers failed: generated too many boundary colliders ({colliderCount}) for region={worldRegion}. The test physics proxy is too broad.");
        }

        return colliderCount;
    }

    private static HashSet<int> CollectGridStaticNavigationBlockerCells(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 heroStart,
        Vector3[] heroWaypoints,
        Vector3[] chaserStarts)
    {
        const float pointRadiusWorld = 3.25f;
        const int boundarySearchRadiusCells = 1;
        bool[] walkableMask = grid.GetWalkableMaskRuntimeReadOnlyReference();
        int[] islandIds = derivedData.IslandIds;
        int mainIslandId = derivedData.MainIslandId;
        HashSet<int> blockerCells = new HashSet<int>();
        HashSet<int> testedCells = new HashSet<int>();
        Debug.Log("[Lv3ControllerCombatPhysics] stage=static-boundary-collect-begin");
        AddGridStaticNavigationBlockerCellsNearPoint(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            heroStart,
            pointRadiusWorld,
            boundarySearchRadiusCells,
            walkableMask,
            islandIds,
            mainIslandId,
            testedCells,
            blockerCells);

        for (int i = 0; i < heroWaypoints.Length; i++)
        {
            Debug.Log($"[Lv3ControllerCombatPhysics] stage=static-boundary-hero-point index={i} tested={testedCells.Count} blockers={blockerCells.Count}");
            AddGridStaticNavigationBlockerCellsNearPoint(
                grid,
                derivedData,
                minX,
                maxX,
                minY,
                maxY,
                heroWaypoints[i],
                pointRadiusWorld,
                boundarySearchRadiusCells,
                walkableMask,
                islandIds,
                mainIslandId,
                testedCells,
                blockerCells);
        }

        Debug.Log($"[Lv3ControllerCombatPhysics] stage=static-boundary-cells tested={testedCells.Count} blockers={blockerCells.Count}");
        return blockerCells;
    }

    private static void AddGridStaticNavigationBlockerCellsNearPoint(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 point,
        float radiusWorld,
        int boundarySearchRadiusCells,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        HashSet<int> testedCells,
        HashSet<int> blockerCells)
    {
        AddGridStaticNavigationBlockerCellsNearSegment(
            grid,
            derivedData,
            minX,
            maxX,
            minY,
            maxY,
            point,
            point,
            radiusWorld,
            boundarySearchRadiusCells,
            walkableMask,
            islandIds,
            mainIslandId,
            testedCells,
            blockerCells);
    }

    private static void AddGridStaticNavigationBlockerCellsNearSegment(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector3 start,
        Vector3 end,
        float radiusWorld,
        int boundarySearchRadiusCells,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        HashSet<int> testedCells,
        HashSet<int> blockerCells)
    {
        float segmentMinX = Mathf.Min(start.x, end.x) - radiusWorld;
        float segmentMaxX = Mathf.Max(start.x, end.x) + radiusWorld;
        float segmentMinZ = Mathf.Min(start.z, end.z) - radiusWorld;
        float segmentMaxZ = Mathf.Max(start.z, end.z) + radiusWorld;
        int cellMinX = Mathf.Max(minX, Mathf.FloorToInt((segmentMinX - grid.Origin.x) / grid.CellSize));
        int cellMaxX = Mathf.Min(maxX, Mathf.CeilToInt((segmentMaxX - grid.Origin.x) / grid.CellSize));
        int cellMinY = Mathf.Max(minY, Mathf.FloorToInt((segmentMinZ - grid.Origin.y) / grid.CellSize));
        int cellMaxY = Mathf.Min(maxY, Mathf.CeilToInt((segmentMaxZ - grid.Origin.y) / grid.CellSize));
        float radiusSqr = radiusWorld * radiusWorld;
        Vector2 a = new Vector2(start.x, start.z);
        Vector2 b = new Vector2(end.x, end.z);
        for (int y = cellMinY; y <= cellMaxY; y++)
        for (int x = cellMinX; x <= cellMaxX; x++)
        {
            int key = y * grid.Width + x;
            if (!testedCells.Add(key))
                continue;
            Vector2 center = new Vector2(
                grid.Origin.x + (x + 0.5f) * grid.CellSize,
                grid.Origin.y + (y + 0.5f) * grid.CellSize);
            if (DistancePointSegmentSqr(center, a, b) > radiusSqr)
                continue;
            if (!IsGridStaticNavigationBoundaryBlocker(grid, walkableMask, islandIds, mainIslandId, x, y, boundarySearchRadiusCells))
                continue;
            blockerCells.Add(key);
        }
    }

    private static float DistancePointSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;
        if (abSqr <= 0.000001f)
            return (point - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqr);
        Vector2 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    private static bool IsGridStaticNavigationBoundaryBlocker(
        FlowNavigationGridAsset grid,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        int x,
        int y,
        int boundarySearchRadiusCells)
    {
        if (IsMainIslandWalkableFast(grid, walkableMask, islandIds, mainIslandId, x, y))
            return false;

        for (int oy = -boundarySearchRadiusCells; oy <= boundarySearchRadiusCells; oy++)
        for (int ox = -boundarySearchRadiusCells; ox <= boundarySearchRadiusCells; ox++)
        {
            if (ox == 0 && oy == 0)
                continue;
            int nx = x + ox;
            int ny = y + oy;
            if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height)
                continue;
            if (IsMainIslandWalkableFast(grid, walkableMask, islandIds, mainIslandId, nx, ny))
                return true;
        }

        return false;
    }

    private static bool IsMainIslandWalkableFast(
        FlowNavigationGridAsset grid,
        bool[] walkableMask,
        int[] islandIds,
        int mainIslandId,
        int x,
        int y)
    {
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return false;
        int index = x + y * grid.Width;
        return walkableMask != null
               && islandIds != null
               && index >= 0
               && index < walkableMask.Length
               && index < islandIds.Length
               && walkableMask[index]
               && islandIds[index] == mainIslandId;
    }

    private static long PackBlockerSpanKey(int startX, int endX)
    {
        return ((long)startX << 32) ^ (uint)endX;
    }

    private static void CreateGridStaticNavigationBlocker(
        FlowNavigationGridAsset grid,
        ActiveNavigationBlockerSpan span,
        List<GameObject> createdObjects,
        int index,
        int layer)
    {
        float sizeX = (span.EndX - span.StartX + 1) * grid.CellSize;
        float sizeZ = (span.EndY - span.StartY + 1) * grid.CellSize;
        Vector3 center = new Vector3(
            grid.Origin.x + (span.StartX + span.EndX + 1) * grid.CellSize * 0.5f,
            1f,
            grid.Origin.y + (span.StartY + span.EndY + 1) * grid.CellSize * 0.5f);
        GameObject obstacle = new GameObject("FlowTest_CC_Combat_StaticBlocker_" + index);
        obstacle.layer = layer;
        obstacle.transform.position = center;
        BoxCollider collider = obstacle.AddComponent<BoxCollider>();
        collider.size = new Vector3(sizeX, 2f, sizeZ);
        createdObjects.Add(obstacle);
    }

    private sealed class ActiveNavigationBlockerSpan
    {
        public int StartX;
        public int EndX;
        public int StartY;
        public int EndY;
        public bool SeenInCurrentRow;
    }

    private static int CollectLv3ControllerOverlapSamples(
        System.Text.StringBuilder diagnostics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Transform[] transforms,
        Vector3 heroPosition,
        int frame,
        float agentRadius,
        int currentOverlapPairStreak,
        int existingOverlapPairSamples,
        SimEntityContext[] chasers = null,
        IEntityContext heroTarget = null)
    {
        const int maxDetailedSamples = 24;
        float overlapDistance = agentRadius * 1.55f;
        float overlapDistanceSq = overlapDistance * overlapDistance;
        int overlapPairs = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int j = i + 1; j < transforms.Length; j++)
            {
                Vector3 a = transforms[i].position;
                Vector3 b = transforms[j].position;
                float distanceSq = HorizontalSqrMagnitude(a - b);
                if (distanceSq > overlapDistanceSq)
                    continue;

                overlapPairs++;
                if (existingOverlapPairSamples + overlapPairs > maxDetailedSamples)
                    continue;

                diagnostics.Append("[Lv3ControllerOverlap] frame=").Append(frame)
                    .Append(" pair=").Append(i).Append('/').Append(j)
                    .Append(" a=").Append(a)
                    .Append(" b=").Append(b)
                    .Append(" hero=").Append(heroPosition)
                    .Append(" distance=").Append(Mathf.Sqrt(distanceSq).ToString("F3"))
                    .Append(" overlapDistance=").Append(overlapDistance.ToString("F3"))
                    .Append(" currentStreak=").Append(currentOverlapPairStreak);
                AppendCellDiagnostic(diagnostics, grid, derivedData, a, "a");
                AppendCellDiagnostic(diagnostics, grid, derivedData, b, "b");
                if (chasers != null)
                {
                    AppendControllerOverlapAgentState(diagnostics, "a", i, chasers, heroTarget);
                    AppendControllerOverlapAgentState(diagnostics, "b", j, chasers, heroTarget);
                    AppendSteeringBreakdown(diagnostics, chasers[i]);
                    AppendSteeringBreakdown(diagnostics, chasers[j]);
                    AppendPathHandleDiagnostics(diagnostics, chasers[i], grid, derivedData, heroPosition);
                    AppendPathHandleDiagnostics(diagnostics, chasers[j], grid, derivedData, heroPosition);
                }
                diagnostics.AppendLine();
            }
        }

        return overlapPairs;
    }

    private static void AppendControllerOverlapAgentState(
        System.Text.StringBuilder diagnostics,
        string label,
        int index,
        SimEntityContext[] chasers,
        IEntityContext heroTarget)
    {
        diagnostics.Append(' ').Append(label).Append("State={");
        if (chasers == null || index < 0 || index >= chasers.Length || chasers[index] == null)
        {
            diagnostics.Append("missing}");
            return;
        }

        SimEntityContext chaser = chasers[index];
        IEntityContext currentTarget = chaser.TargetComp?.CurrentTarget;
        SoldierAIBrain brain = chaser.Brain as SoldierAIBrain;
        CharacterMoveComp moveComp = chaser.MoveComp as CharacterMoveComp;
        float targetSurfaceDistance = currentTarget != null ? chaser.DistanceToTargetSurface(currentTarget) : float.PositiveInfinity;
        float heroSurfaceDistance = heroTarget != null ? chaser.DistanceToTargetSurface(heroTarget) : float.PositiveInfinity;
        float attackRange = chaser.WeaponComp != null
            ? (float)chaser.WeaponComp.AttackRange
            : brain != null ? brain.WeaponRange : float.NaN;

        diagnostics.Append("key=").Append(chaser.CharacterKey)
            .Append(",targetHero=").Append(ReferenceEquals(currentTarget, heroTarget))
            .Append(",targetNull=").Append(currentTarget == null)
            .Append(",brainState=").Append(brain != null ? brain.State.ToString() : "none")
            .Append(",attack=").Append(brain != null && brain.Attack)
            .Append(",moveTarget=").Append(moveComp != null && moveComp.HasNavigationTarget)
            .Append(",moving=").Append(chaser.MoveComp != null && chaser.MoveComp.IsMoving)
            .Append(",targetSurfaceDist=").Append(float.IsPositiveInfinity(targetSurfaceDistance) ? "INF" : targetSurfaceDistance.ToString("F3"))
            .Append(",heroSurfaceDist=").Append(float.IsPositiveInfinity(heroSurfaceDistance) ? "INF" : heroSurfaceDistance.ToString("F3"))
            .Append(",attackRange=").Append(float.IsNaN(attackRange) ? "NaN" : attackRange.ToString("F3"))
            .Append(",navTarget=");
        if (moveComp != null && moveComp.TryGetNavigationTarget(out Vector3 navigationTarget))
            diagnostics.Append(navigationTarget);
        else
            diagnostics.Append("none");
        diagnostics.Append('}');
    }

    private static int CollectLv3CrowdRouteOverlapSamples(
        Lv3CrowdRouteScenario scenario,
        Lv3CrowdRouteMetrics metrics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        SimEntityContext[] chasers,
        IEntityContext heroTarget,
        Vector3 heroPosition,
        int frame,
        float agentRadius,
        int currentOverlapPairStreak)
    {
        float overlapDistance = agentRadius * 1.55f;
        float overlapDistanceSq = overlapDistance * overlapDistance;
        int overlapPairs = 0;
        for (int i = 0; i < chasers.Length; i++)
        {
            for (int j = i + 1; j < chasers.Length; j++)
            {
                Vector3 a = chasers[i].Position;
                Vector3 b = chasers[j].Position;
                float distanceSq = HorizontalSqrMagnitude(a - b);
                if (distanceSq > overlapDistanceSq)
                    continue;

                overlapPairs++;
                if (metrics.DetailedIssueSamples >= 48)
                    continue;

                bool aHasHeroTarget = chasers[i].TargetComp is SimTargetingComp targetingA && ReferenceEquals(targetingA.CurrentTarget, heroTarget);
                bool bHasHeroTarget = chasers[j].TargetComp is SimTargetingComp targetingB && ReferenceEquals(targetingB.CurrentTarget, heroTarget);
                metrics.DetailedIssueSamples++;
                metrics.Diagnostics.Append("[Lv3CrowdOverlap] scenario=").Append(scenario.Name)
                    .Append(" frame=").Append(frame)
                    .Append(" pair=").Append(i).Append('/').Append(j)
                    .Append(" a=").Append(a)
                    .Append(" b=").Append(b)
                    .Append(" hero=").Append(heroPosition)
                    .Append(" distance=").Append(Mathf.Sqrt(distanceSq).ToString("F3"))
                    .Append(" overlapDistance=").Append(overlapDistance.ToString("F3"))
                    .Append(" currentStreak=").Append(currentOverlapPairStreak)
                    .Append(" aHasHeroTarget=").Append(aHasHeroTarget)
                    .Append(" bHasHeroTarget=").Append(bHasHeroTarget);
                AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, a, "a");
                AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, b, "b");
                AppendSteeringBreakdown(metrics.Diagnostics, chasers[i]);
                AppendSteeringBreakdown(metrics.Diagnostics, chasers[j]);
                AppendPathHandleDiagnostics(metrics.Diagnostics, chasers[i], grid, derivedData, heroPosition);
                AppendPathHandleDiagnostics(metrics.Diagnostics, chasers[j], grid, derivedData, heroPosition);
                metrics.Diagnostics.AppendLine();
            }
        }

        return overlapPairs;
    }

    private static Vector3[] ResolveLv3ScenarioChaserStarts(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3CrowdRouteScenario scenario,
        float agentRadius,
        System.Text.StringBuilder diagnostics)
    {
        if (scenario.ChaserCenterCounts != null)
            return ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(grid, derivedData, scenario, agentRadius, diagnostics);

        List<Vector3> starts = new List<Vector3>(scenario.ChaserCount);
        const float minDistance = 0.75f;
        int centerIndex = 0;
        int guard = 0;
        while (starts.Count < scenario.ChaserCount && guard < scenario.ChaserCount * 64)
        {
            Vector3 center = scenario.ChaserCenters[centerIndex % scenario.ChaserCenters.Length];
            float angle = guard * 2.39996323f;
            float radius = 0.25f + Mathf.Sqrt((guard % 48) / 48f) * 3.4f;
            Vector3 preferred = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (TryResolveRuntimeLegalUniqueLv3Start(grid, derivedData, preferred, 4.5f, agentRadius, minDistance, starts, out Vector3 start))
                starts.Add(start);
            centerIndex++;
            guard++;
        }

        if (starts.Count < scenario.ChaserCount)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStarts failed: scenario={scenario.Name} requested={scenario.ChaserCount}, resolved={starts.Count}.");

        diagnostics.Append("[Lv3CrowdScenario] chaserStarts=").Append(starts.Count).AppendLine();
        for (int i = 0; i < starts.Count; i++)
        {
            diagnostics.Append("[Lv3CrowdScenario] chaser-").Append(i)
                .Append(" start=").Append(starts[i]);
            AppendCellDiagnostic(diagnostics, grid, derivedData, starts[i], "start");
            diagnostics.AppendLine();
        }

        return starts.ToArray();
    }

    private static Vector3[] ResolveLv3ScenarioChaserStartsFromRealSpawnPreview(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3CrowdRouteScenario scenario,
        float agentRadius,
        System.Text.StringBuilder diagnostics)
    {
        if (scenario.ChaserCenterCounts.Length != scenario.ChaserCenters.Length)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name} centers={scenario.ChaserCenters.Length}, counts={scenario.ChaserCenterCounts.Length}.");

        const float enemyPresetClusterRadius = 3f;
        const float enemyPresetClusterMinDistance = 1.2f;
        List<Vector3> starts = new List<Vector3>(Mathf.Max(0, scenario.ChaserCount));
        List<Vector3> preview = new List<Vector3>();
        for (int centerIndex = 0; centerIndex < scenario.ChaserCenters.Length; centerIndex++)
        {
            int count = scenario.ChaserCenterCounts[centerIndex];
            if (count <= 0)
                continue;

            preview.Clear();
            if (!ClusterSpawnSystem.TryGetPreviewSpawnPositions(
                    scenario.ChaserCenters[centerIndex],
                    count,
                    enemyPresetClusterRadius,
                    enemyPresetClusterMinDistance,
                    preview))
            {
                throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name}, center={scenario.ChaserCenters[centerIndex]}, count={count} cannot resolve real preview spawn positions.");
            }

            diagnostics.Append("[Lv3CrowdScenario] realSpawnCenter=").Append(centerIndex)
                .Append(" center=").Append(scenario.ChaserCenters[centerIndex])
                .Append(" count=").Append(count)
                .Append(" preview=").Append(preview.Count)
                .AppendLine();

            for (int i = 0; i < preview.Count; i++)
            {
                Vector3 start = ResolveRuntimeLegalLv3MainIslandCell(
                    grid,
                    derivedData,
                    preview[i],
                    2.0f,
                    agentRadius,
                    scenario.Name + "-real-spawn-" + centerIndex + "-" + i,
                    diagnostics);
                starts.Add(start);
            }
        }

        if (starts.Count != scenario.ChaserCount)
            throw new InvalidOperationException($"ResolveLv3ScenarioChaserStartsFromRealSpawnPreview failed: scenario={scenario.Name} expected={scenario.ChaserCount}, resolved={starts.Count}.");

        return starts.ToArray();
    }

    private static bool TryResolveRuntimeLegalUniqueLv3Start(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        float agentRadius,
        float minDistance,
        List<Vector3> existing,
        out Vector3 result)
    {
        result = Vector3.zero;
        if (!TryResolveNearestMainIslandCell(grid, derivedData, preferred, maxRadius, out Vector3 mainIsland))
            return false;
        if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                mainIsland,
                grid.AgentTypeId,
                grid.CellSize * 0.75f,
                Mathf.Max(0f, agentRadius - grid.CellSize * 0.2f),
                out Vector3 legal))
        {
            return false;
        }
        if (!grid.WorldToCell(legal, out int legalX, out int legalY) || !IsMainIslandWalkable(grid, derivedData, legalX, legalY))
            return false;

        float minDistanceSq = minDistance * minDistance;
        for (int i = 0; i < existing.Count; i++)
        {
            if (HorizontalSqrMagnitude(existing[i] - legal) < minDistanceSq)
                return false;
        }

        result = legal;
        return true;
    }

    private static void CollectLv3CrowdRouteSample(
        Lv3CrowdRouteScenario scenario,
        Lv3CrowdRouteMetrics metrics,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint researchCenter,
        Rect researchBounds,
        SimEntityContext chaser,
        SimMoveExecutor executor,
        Vector3 decisionPosition,
        IEntityContext heroTarget,
        Vector3 heroPosition,
        int frame,
        int agentIndex,
        bool agentExpectsLowerRoute,
        int[] zeroStreak,
        int[] maxZeroStreak,
        ref int slowWallAgentsThisFrame,
        ref int slowTargetedAgentsThisFrame)
    {
        Vector3 desired = executor.LastDesiredDisplacement;
        desired.y = 0f;
        Vector3 constrained = executor.LastConstrainedDisplacement;
        constrained.y = 0f;
        Vector3 desiredDirection = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
        Assert.IsTrue(grid.WorldToCell(decisionPosition, out int currentX, out int currentY), "追兵决策位置必须在 Lv3 grid 内。");
        int currentSectorId = ResolveDerivedSectorId(derivedData, currentX, currentY);
        bool hitsResearchCenter = desiredDirection.sqrMagnitude > 0f
                                  && researchCenter.RayHitsAnyBox(decisionPosition, desiredDirection, 18f);
        float wallHitDistance = hitsResearchCenter
            ? researchCenter.DistanceToFirstRayHit(decisionPosition, desiredDirection, 18f)
            : float.PositiveInfinity;
        bool hasSelectedPortal = TryResolveSelectedPortalCenter(chaser, currentSectorId, out int selectedPortalId, out Vector3 selectedPortalCenter);
        float selectedPortalDistance = hasSelectedPortal
            ? HorizontalDistance(decisionPosition, selectedPortalCenter)
            : float.PositiveInfinity;
        float targetDistance = HorizontalDistance(decisionPosition, heroPosition);
        float routeLimitDistance = Mathf.Min(selectedPortalDistance, targetDistance);
        bool segmentEntersResearchCenter = desiredDirection.sqrMagnitude > 0f
                                           && SegmentEntersFootprint(
                                               researchCenter,
                                               decisionPosition,
                                               desiredDirection,
                                               Mathf.Min(routeLimitDistance + 0.2f, 18f),
                                               grid.CellSize * 0.5f);
        bool actualNavigationSegmentViolatesResearchCenter = SteeringTileTargetSegmentViolatesFootprintClearance(
            researchCenter,
            decisionPosition,
            chaser,
            grid.CellSize * 0.5f,
            0.45f);
        bool hitsBeforePortal = hitsResearchCenter
                                && wallHitDistance <= routeLimitDistance + 0.2f
                                && segmentEntersResearchCenter
                                && actualNavigationSegmentViolatesResearchCenter;
        bool projectedToZero = desired.sqrMagnitude > 0.04f * 0.04f
                               && constrained.sqrMagnitude <= 0.015f * 0.015f;
        zeroStreak[agentIndex] = projectedToZero ? zeroStreak[agentIndex] + 1 : 0;
        maxZeroStreak[agentIndex] = Mathf.Max(maxZeroStreak[agentIndex], zeroStreak[agentIndex]);
        float researchDistance = researchCenter.DistanceToClosestBox(decisionPosition);
        bool constrainedWallStall = projectedToZero && researchDistance <= 0.9f;
        bool hasSteeringGoal = FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(chaser.GetHashCode(), out Vector3 steeringGoal, out int steeringFrame);
        bool hasHeroAsCombatTarget = chaser.TargetComp is SimTargetingComp targeting && ReferenceEquals(targeting.CurrentTarget, heroTarget);
        bool expectsActiveSteering = hasHeroAsCombatTarget && desired.sqrMagnitude > 0.04f * 0.04f && targetDistance > 1.25f;
        bool staleSteering = expectsActiveSteering && (!hasSteeringGoal || frame - steeringFrame > 2);
        bool nearWallGoal = hasSteeringGoal && researchCenter.DistanceToClosestBox(steeringGoal) < 0.35f;
        bool constraintFailed = !executor.LastConstraintSucceeded && expectsActiveSteering;
        bool upperDetour = scenario.ExpectLowerRoute
                           && agentExpectsLowerRoute
                           && hasSelectedPortal
                           && heroPosition.z <= researchBounds.yMin + 0.8f
                           && decisionPosition.x <= researchBounds.xMax + 1.5f
                           && selectedPortalCenter.z >= researchBounds.yMax + 0.25f;
        if (hitsBeforePortal)
        {
            metrics.WallBeforePortalSamples++;
            metrics.MinWallHitDistance = Mathf.Min(metrics.MinWallHitDistance, wallHitDistance);
        }
        if (constrainedWallStall)
            metrics.ConstrainedWallStallSamples++;
        if (staleSteering)
            metrics.StaleSteeringSamples++;
        if (nearWallGoal)
            metrics.NearWallGoalSamples++;
        if (constraintFailed)
            metrics.ConstraintFailureSamples++;
        if (upperDetour)
            metrics.UpperDetourSamples++;
        if (researchDistance <= 1.05f && constrained.magnitude <= 0.08f && desired.magnitude > 0.12f)
            slowWallAgentsThisFrame++;
        if (hasHeroAsCombatTarget && targetDistance > 1.25f && constrained.magnitude <= 0.08f && desired.magnitude > 0.12f)
            slowTargetedAgentsThisFrame++;

        bool isIssueSample = hitsBeforePortal || constrainedWallStall || nearWallGoal || constraintFailed || upperDetour;
        bool isBackgroundSample = staleSteering || frame % 45 == 0;
        bool shouldRecordDetailedSample = (isIssueSample && metrics.DetailedIssueSamples < 24)
                                          || (!isIssueSample && isBackgroundSample && metrics.DetailedBackgroundSamples < 8);
        if (!shouldRecordDetailedSample)
            return;

        if (isIssueSample)
            metrics.DetailedIssueSamples++;
        else
            metrics.DetailedBackgroundSamples++;
        metrics.Diagnostics.Append("[Lv3CrowdScenarioSample] scenario=").Append(scenario.Name)
            .Append(" frame=").Append(frame)
            .Append(" agent=").Append(agentIndex)
            .Append(" hero=").Append(heroPosition)
            .Append(" decision=").Append(decisionPosition)
            .Append(" after=").Append(chaser.Position);
        AppendCellDiagnostic(metrics.Diagnostics, grid, derivedData, decisionPosition, "decision");
        metrics.Diagnostics.Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
            .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
            .Append(" zeroStreak=").Append(zeroStreak[agentIndex])
            .Append(" rayHitsResearchCenter=").Append(hitsResearchCenter)
            .Append(" wallHitDist=").Append(float.IsPositiveInfinity(wallHitDistance) ? "INF" : wallHitDistance.ToString("F3"))
            .Append(" selectedPortal=").Append(hasSelectedPortal ? selectedPortalId.ToString() : "missing")
            .Append(" selectedPortalCenter=").Append(hasSelectedPortal ? selectedPortalCenter.ToString() : "missing")
            .Append(" portalDist=").Append(float.IsPositiveInfinity(selectedPortalDistance) ? "INF" : selectedPortalDistance.ToString("F3"))
            .Append(" targetDist=").Append(targetDistance.ToString("F3"))
            .Append(" segmentEntersResearchCenter=").Append(segmentEntersResearchCenter)
            .Append(" actualNavSegmentViolatesResearchCenter=").Append(actualNavigationSegmentViolatesResearchCenter)
            .Append(" hitsBeforePortal=").Append(hitsBeforePortal)
            .Append(" constrainedWallStall=").Append(constrainedWallStall)
            .Append(" hasHeroTarget=").Append(hasHeroAsCombatTarget)
            .Append(" expectsActiveSteering=").Append(expectsActiveSteering)
            .Append(" staleSteering=").Append(staleSteering)
            .Append(" nearWallGoal=").Append(nearWallGoal)
            .Append(" constraintSucceeded=").Append(executor.LastConstraintSucceeded)
            .Append(" constraintFailed=").Append(constraintFailed)
            .Append(" agentExpectsLowerRoute=").Append(agentExpectsLowerRoute)
            .Append(" upperDetour=").Append(upperDetour)
            .Append(" steeringGoal=").Append(hasSteeringGoal ? steeringGoal.ToString() : "missing")
            .Append(" steeringFrame=").Append(hasSteeringGoal ? steeringFrame.ToString() : "missing");
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestCurrentTileQueueState(
                chaser.GetHashCode(),
                out string queueGoalKind,
                out int queuePortalId,
                out bool queueCached,
                out bool queuePending,
                out int queueIndex,
                out string queueStage,
                out bool queueWaiting,
                out bool queueStale,
                out int pendingPortalFrames))
        {
            metrics.Diagnostics.Append(" tileQueue={kind=").Append(queueGoalKind)
                .Append(",portal=").Append(queuePortalId)
                .Append(",cached=").Append(queueCached)
                .Append(",pending=").Append(queuePending)
                .Append(",index=").Append(queueIndex)
                .Append(",stage=").Append(queueStage)
                .Append(",waiting=").Append(queueWaiting)
                .Append(",stale=").Append(queueStale)
                .Append(",pendingPortalFrames=").Append(pendingPortalFrames)
                .Append('}');
        }
        else
        {
            metrics.Diagnostics.Append(" tileQueue=missing");
        }
        AppendSteeringBreakdown(metrics.Diagnostics, chaser);
        AppendPathHandleDiagnostics(metrics.Diagnostics, chaser, grid, derivedData, heroPosition);
        if (hitsBeforePortal || upperDetour)
            AppendSegmentGridDiagnostics(metrics.Diagnostics, grid, derivedData, researchCenter, decisionPosition, "decision-to-tileTarget", chaser);
        metrics.Diagnostics.AppendLine();
    }

    private static string BuildLv3BadPointDiagnostics(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        SimEntityContext chaser,
        SimMoveExecutor executor,
        int startX,
        int startY,
        int goalX,
        int goalY,
        int startSectorId,
        int goalSectorId)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(2048);
        Vector3 start = grid.GetCellAnchor(startX, startY);
        Vector3 goal = grid.GetCellAnchor(goalX, goalY);
        int startIsland = derivedData.IslandIds[startX + startY * grid.Width];
        int goalIsland = derivedData.IslandIds[goalX + goalY * grid.Width];

        builder.Append("Lv3BadPoint")
            .Append(" start=").Append(start)
            .Append(" goal=").Append(goal)
            .Append(" startCell=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(" goalCell=(").Append(goalX).Append(',').Append(goalY).Append(')')
            .Append(" startIsland=").Append(startIsland)
            .Append(" goalIsland=").Append(goalIsland)
            .Append(" mainIsland=").Append(derivedData.MainIslandId)
            .Append(" startSector=").Append(startSectorId)
            .Append(" goalSector=").Append(goalSectorId)
            .AppendLine();

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(chaser.GetHashCode(), out string buildSource))
            builder.Append("pathBuildSource=").Append(buildSource).AppendLine();
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.GetHashCode(), out int[] sectorIds))
            builder.Append("pathSectors=[").Append(string.Join("->", sectorIds)).Append("]").AppendLine();
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.GetHashCode(), out int[] portalIds))
        {
            builder.Append("pathPortals=[").Append(string.Join("->", portalIds)).Append("]").AppendLine();
            if (portalIds.Length > 0)
            {
                builder.Append("pendingPortalAccess=")
                    .Append(FlowFieldCrowdMovementSystem.BuildEditorTestPendingPortalAccessDiagnostics(
                        startSectorId,
                        portalIds[0],
                        startX,
                        startY,
                        start,
                        goalX,
                        goalY,
                        0.45f))
                    .AppendLine();
            }
        }

        builder.Append("portalChoice=")
            .Append(FlowFieldCrowdMovementSystem.GetEditorTestStartPortalChoiceDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY, grid.AgentTypeId))
            .AppendLine();
        builder.Append("portalGraph=")
            .Append(FlowFieldCrowdMovementSystem.BuildEditorTestPortalGraphCostDiagnostics(startSectorId, goalSectorId, startX, startY, goalX, goalY))
            .AppendLine();

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                chaser.GetHashCode(),
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 agentAvoidance,
                out Vector3 clampedAgentAvoidance,
                out Vector3 boundaryAvoidance,
                out Vector3 laneVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            builder.Append("steering desired=").Append(desiredVelocity)
                .Append(" base=").Append(baseVelocity)
                .Append(" avoid=").Append(agentAvoidance)
                .Append(" clampedAvoid=").Append(clampedAgentAvoidance)
                .Append(" boundary=").Append(boundaryAvoidance)
                .Append(" lane=").Append(laneVelocity)
                .Append(" pre=").Append(resultPreClamp)
                .Append(" result=").Append(result)
                .AppendLine();

            if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSeparationDiagnostic(
                    chaser.GetHashCode(),
                    out Vector3 rawAgentAvoidance,
                    out Vector3 immediateSeparation,
                    out Vector3 strongestImmediateSeparation,
                    out int immediateOverlapCount,
                    out float closestImmediateOverlapDistance,
                    out float closestImmediateOverlapClearance,
                    out int closeNeighborCount,
                    out int skippedCloseNeighborCount,
                    out float closestNeighborDistance,
                    out float closestNeighborClearance,
                    out float runtimeRadius,
                    out float registeredRadius,
                    out Vector3 runtimeObstacleAvoidance,
                    out float maxSpeed))
            {
                builder.Append("steeringSeparation rawAgentAvoid=").Append(rawAgentAvoidance)
                    .Append(" immediateSep=").Append(immediateSeparation)
                    .Append(" strongestImmediateSep=").Append(strongestImmediateSeparation)
                    .Append(" immediateOverlapCount=").Append(immediateOverlapCount)
                    .Append(" closestImmediateDist=").Append(closestImmediateOverlapDistance == float.MaxValue ? "MAX" : closestImmediateOverlapDistance.ToString("F3"))
                    .Append(" closestImmediateClearance=").Append(closestImmediateOverlapClearance.ToString("F3"))
                    .Append(" closeNeighborCount=").Append(closeNeighborCount)
                    .Append(" skippedCloseNeighborCount=").Append(skippedCloseNeighborCount)
                    .Append(" closestNeighborDist=").Append(closestNeighborDistance == float.MaxValue ? "MAX" : closestNeighborDistance.ToString("F3"))
                    .Append(" closestNeighborClearance=").Append(closestNeighborClearance.ToString("F3"))
                    .Append(" runtimeRadius=").Append(runtimeRadius.ToString("F3"))
                    .Append(" registeredRadius=").Append(registeredRadius.ToString("F3"))
                    .Append(" runtimeObstacle=").Append(runtimeObstacleAvoidance)
                    .Append(" maxSpeed=").Append(maxSpeed.ToString("F3"))
                    .AppendLine();
            }

            if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringResolution(
                    chaser.GetHashCode(),
                    out string source,
                    out Vector2 flow,
                    out bool hasLineOfSight,
                    out Vector3 tileTarget,
                    out float integration,
                    out Vector3 edgeNormal,
                    out float edgeDistance))
            {
                builder.Append("steeringResolution source=").Append(source)
                    .Append(" flow=").Append(flow)
                    .Append(" los=").Append(hasLineOfSight)
                    .Append(" tileTarget=").Append(tileTarget)
                    .Append(" integration=").Append(float.IsPositiveInfinity(integration) ? "INF" : integration.ToString("F3"))
                    .Append(" edgeNormal=").Append(edgeNormal)
                    .Append(" edgeDist=").Append(edgeDistance == float.MaxValue ? "MAX" : edgeDistance.ToString("F3"))
                    .AppendLine();
            }
        }
        else
        {
            builder.AppendLine("steering=missing");
        }

        builder.Append("executor input=").Append(executor.LastInputVelocity)
            .Append(" frameVelocity=").Append(executor.LastFrameVelocity)
            .Append(" desiredDisp=").Append(executor.LastDesiredDisplacement)
            .Append(" constrainedDisp=").Append(executor.LastConstrainedDisplacement)
            .Append(" constraintOk=").Append(executor.LastConstraintSucceeded)
            .Append(" finalPos=").Append(chaser.Position)
            .AppendLine();

        return builder.ToString();
    }

    private static Lv3RightBottomRoute ResolveLv3RightBottomRoute(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        System.Text.StringBuilder diagnostics)
    {
        if (grid == null)
            throw new InvalidOperationException("ResolveLv3RightBottomRoute failed: grid is null.");
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveLv3RightBottomRoute failed: derivedData is invalid.");

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=load-preset-begin");
        Lv3PresetSnapshot preset = LoadLv3PresetSnapshot();
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=load-preset-end hero={preset.HeroStart} research={preset.ResearchCenterPosition} unitCenters={preset.UnitSpawnCenters.Length} bounds={preset.ResearchCenterFootprintBounds} sh13={preset.StrongholdSh13Bounds}");
        Vector3 rightBottomPreferred = new Vector3(
            preset.StrongholdSh13Bounds.xMax,
            0f,
            preset.StrongholdSh13Bounds.yMin);
        Vector2 rightBottomMin = new Vector2(
            preset.StrongholdSh13Bounds.xMax - 6.0f,
            preset.StrongholdSh13Bounds.yMin - 2.0f);
        Vector2 rightBottomMax = new Vector2(
            preset.StrongholdSh13Bounds.xMax + 1.6f,
            preset.StrongholdSh13Bounds.yMin + 6.0f);

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=resolve-route-points-begin");
        Lv3RightBottomRoute route = new Lv3RightBottomRoute
        {
            HeroStart = ResolveNearestMainIslandCell(grid, derivedData, preset.HeroStart, 5f, "hero-start", diagnostics),
            LowerApproach = ResolveNearestMainIslandCell(
                grid,
                derivedData,
                new Vector3(preset.ResearchCenterPosition.x, 0f, preset.ResearchCenterFootprintBounds.yMin - 1.2f),
                5f,
                "lower-approach",
                diagnostics),
            RightBottomCorner = ResolveBestMainIslandCellInWorldBounds(
                grid,
                derivedData,
                rightBottomPreferred,
                rightBottomMin,
                rightBottomMax,
                "right-bottom-corner",
                diagnostics),
            ReturnPoint = ResolveNearestMainIslandCell(grid, derivedData, preset.HeroStart, 5f, "return-point", diagnostics),
            ResearchCenterPosition = preset.ResearchCenterPosition,
            ResearchCenterBounds = preset.ResearchCenterFootprintBounds,
            StrongholdBounds = preset.StrongholdSh13Bounds
        };
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=resolve-route-points-end heroStart={route.HeroStart} corner={route.RightBottomCorner} return={route.ReturnPoint}");

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=cluster-candidates-begin");
        List<Vector3> chaserCandidates = ResolveLv3ClusterSpawnCandidates(grid, derivedData, preset.UnitSpawnCenters, diagnostics);
        Debug.Log($"[Lv3ResearchCenterChaseTest] stage=cluster-candidates-end count={chaserCandidates.Count}");
        if (chaserCandidates.Count < 6)
            throw new InvalidOperationException($"ResolveLv3RightBottomRoute failed: only {chaserCandidates.Count} legal chaser candidates generated.");

        route.ChaserStarts = new Vector3[chaserCandidates.Count];
        for (int i = 0; i < chaserCandidates.Count; i++)
            route.ChaserStarts[i] = ResolveNearestMainIslandCell(grid, derivedData, chaserCandidates[i], 3.5f, "chaser-" + i, diagnostics);

        Debug.Log("[Lv3ResearchCenterChaseTest] stage=route-map-begin");
        AppendLv3RouteMap(grid, derivedData, route, diagnostics);
        Debug.Log("[Lv3ResearchCenterChaseTest] stage=route-map-end");
        return route;
    }

    private static Lv3PresetSnapshot LoadLv3PresetSnapshot()
    {
        const string levelPrefabPath = "Assets/AAAGame/Prefabs/Entity/Level/Level_3.prefab";
        GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(levelPrefabPath);
        if (root == null)
            throw new InvalidOperationException($"LoadLv3PresetSnapshot failed: cannot load {levelPrefabPath}.");

        try
        {
            EntityPresetPoint[] points = root.GetComponentsInChildren<EntityPresetPoint>(true);
            if (points == null || points.Length == 0)
                throw new InvalidOperationException($"LoadLv3PresetSnapshot failed: no EntityPresetPoint in {levelPrefabPath}.");

            EntityPresetPoint researchPoint = null;
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null
                    || point.PointType != EntityPresetPointType.Building
                    || !string.Equals(point.Identifier, "Buil_ResearchCenter_Lv1", StringComparison.Ordinal))
                    continue;

                if (researchPoint == null
                    || point.Position.z < researchPoint.Position.z - 0.001f
                    || (Mathf.Abs(point.Position.z - researchPoint.Position.z) <= 0.001f && point.Position.x > researchPoint.Position.x))
                {
                    researchPoint = point;
                }
            }

            if (researchPoint == null)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: Buil_ResearchCenter_Lv1 preset point not found.");

            Lv3ResearchCenterFootprint footprint = CreateLv3ResearchCenterLv1Footprint(researchPoint.Position);
            Rect footprintBounds = footprint.CalculateBounds();
            Rect strongholdSh13Bounds = ResolveStrongholdBlueprintBounds(root, "SH_1_3");

            EntityPresetPoint heroPoint = null;
            float bestHeroScore = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null || point.PointType != EntityPresetPointType.Hero)
                    continue;

                float score = HorizontalSqrMagnitude(point.Position - researchPoint.Position);
                if (score < bestHeroScore)
                {
                    bestHeroScore = score;
                    heroPoint = point;
                }
            }

            if (heroPoint == null)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: hero preset point not found.");

            List<Lv3UnitSpawnPreset> unitSpawns = new List<Lv3UnitSpawnPreset>();
            for (int i = 0; i < points.Length; i++)
            {
                EntityPresetPoint point = points[i];
                if (point == null
                    || point.PointType != EntityPresetPointType.Unit
                    || !string.Equals(point.Identifier, "Unit_Scapegoat", StringComparison.Ordinal))
                    continue;
                if (point.UnitSpawnCount <= 0)
                    continue;

                Vector3 position = point.Position;
                if (HorizontalSqrMagnitude(position - researchPoint.Position) > 18f * 18f)
                    continue;
                if (position.z < footprintBounds.yMin - 0.2f)
                    continue;

                unitSpawns.Add(new Lv3UnitSpawnPreset
                {
                    Position = position,
                    Count = point.UnitSpawnCount
                });
            }

            if (unitSpawns.Count == 0)
                throw new InvalidOperationException("LoadLv3PresetSnapshot failed: no nearby Unit_Scapegoat preset points around lower ResearchCenter.");

            unitSpawns.Sort((a, b) =>
                HorizontalSqrMagnitude(a.Position - researchPoint.Position).CompareTo(HorizontalSqrMagnitude(b.Position - researchPoint.Position)));
            Vector3[] unitCenters = new Vector3[unitSpawns.Count];
            int[] unitCounts = new int[unitSpawns.Count];
            int totalUnitCount = 0;
            for (int i = 0; i < unitSpawns.Count; i++)
            {
                unitCenters[i] = unitSpawns[i].Position;
                unitCounts[i] = unitSpawns[i].Count;
                totalUnitCount += unitSpawns[i].Count;
            }

            return new Lv3PresetSnapshot
            {
                HeroStart = heroPoint.Position,
                ResearchCenterPosition = researchPoint.Position,
                ResearchCenterFootprintBounds = footprintBounds,
                StrongholdSh13Bounds = strongholdSh13Bounds,
                UnitSpawnCenters = unitCenters,
                UnitSpawnCounts = unitCounts,
                TotalUnitSpawnCount = totalUnitCount
            };
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Rect ResolveStrongholdBlueprintBounds(GameObject levelRoot, string layerName)
    {
        if (levelRoot == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: levelRoot is null.");
        if (string.IsNullOrEmpty(layerName))
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: layerName is empty.");

        Type managerType = Type.GetType("GiantGrey.TileWorldCreator.TileWorldCreatorManager, GiantGrey.TileWorldCreator");
        if (managerType == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager type not found.");

        Component manager = levelRoot.GetComponentInChildren(managerType, true);
        if (manager == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager or configuration is null.");

        object configuration = managerType.GetField("configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(manager);
        if (configuration == null)
            throw new InvalidOperationException("ResolveStrongholdBlueprintBounds failed: TileWorldCreatorManager or configuration is null.");

        IList blueprintLayerFolders = GetRequiredFieldValue<IList>(configuration, "blueprintLayerFolders");
        object targetLayer = null;
        for (int folderIndex = 0; folderIndex < blueprintLayerFolders.Count; folderIndex++)
        {
            object folder = blueprintLayerFolders[folderIndex];
            if (folder == null)
                continue;

            IList blueprintLayers = GetRequiredFieldValue<IList>(folder, "blueprintLayers");
            if (blueprintLayers == null)
                continue;

            for (int layerIndex = 0; layerIndex < blueprintLayers.Count; layerIndex++)
            {
                object layer = blueprintLayers[layerIndex];
                string currentLayerName = layer != null
                    ? GetRequiredFieldValue<string>(layer, "layerName")
                    : null;
                if (layer != null && string.Equals(currentLayerName, layerName, StringComparison.Ordinal))
                {
                    targetLayer = layer;
                    break;
                }
            }

            if (targetLayer != null)
                break;
        }

        if (targetLayer == null)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} not found.");
        IEnumerable allPositions = GetRequiredFieldValue<IEnumerable>(targetLayer, "allPositions");
        if (allPositions == null)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} has no positions.");

        float minX = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;
        float cellSize = GetRequiredFieldValue<float>(configuration, "cellSize");
        int positionCount = 0;
        foreach (Vector2 cell in allPositions)
        {
            positionCount++;
            Vector3 world = manager.transform.TransformPoint(new Vector3(cell.x * cellSize, 0f, cell.y * cellSize));
            minX = Mathf.Min(minX, world.x);
            minZ = Mathf.Min(minZ, world.z);
            maxX = Mathf.Max(maxX, world.x);
            maxZ = Mathf.Max(maxZ, world.z);
        }

        if (positionCount == 0)
            throw new InvalidOperationException($"ResolveStrongholdBlueprintBounds failed: blueprint layer {layerName} has no positions.");

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    private static T GetRequiredFieldValue<T>(object instance, string fieldName)
    {
        if (instance == null)
            throw new InvalidOperationException($"GetRequiredFieldValue failed: instance is null for {fieldName}.");

        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"GetRequiredFieldValue failed: field {fieldName} not found on {instance.GetType().FullName}.");

        object value = field.GetValue(instance);
        if (value == null)
            return default;
        if (value is T typed)
            return typed;

        throw new InvalidOperationException($"GetRequiredFieldValue failed: field {fieldName} on {instance.GetType().FullName} is {value.GetType().FullName}, expected {typeof(T).FullName}.");
    }

    private static List<Vector3> ResolveLv3ClusterSpawnCandidates(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3[] unitSpawnCenters,
        System.Text.StringBuilder diagnostics)
    {
        if (grid == null)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: grid is null.");
        if (derivedData == null || !derivedData.IsValid)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: derivedData is invalid.");
        if (unitSpawnCenters == null || unitSpawnCenters.Length == 0)
            throw new InvalidOperationException("ResolveLv3ClusterSpawnCandidates failed: unitSpawnCenters is empty.");

        const float enemyPresetClusterRadius = 3f;
        const float enemyPresetClusterMinDistance = 1.2f;
        const int maxCountPerPreset = 6;
        const int samplesPerPreset = 48;
        List<Vector3> candidates = new List<Vector3>();
        for (int i = 0; i < unitSpawnCenters.Length; i++)
        {
            int before = candidates.Count;
            TryAddUniqueMainIslandCandidate(grid, derivedData, unitSpawnCenters[i], enemyPresetClusterMinDistance, candidates);

            for (int sample = 0; sample < samplesPerPreset && candidates.Count - before < maxCountPerPreset; sample++)
            {
                float angle = sample * 2.39996323f;
                float t = (sample + 0.5f) / samplesPerPreset;
                float radius = Mathf.Sqrt(t) * enemyPresetClusterRadius;
                Vector3 preferred = unitSpawnCenters[i] + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                TryAddUniqueMainIslandCandidate(grid, derivedData, preferred, enemyPresetClusterMinDistance, candidates);
            }

            diagnostics.Append("[Lv3Route] cluster-center-").Append(i)
                .Append(" center=").Append(unitSpawnCenters[i])
                .Append(" generated=").Append(candidates.Count - before)
                .AppendLine();
        }

        return candidates;
    }

    private static bool TryAddUniqueMainIslandCandidate(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float minDistance,
        List<Vector3> candidates)
    {
        if (!TryResolveNearestMainIslandCell(grid, derivedData, preferred, 3.5f, out Vector3 resolved))
            return false;

        float minDistanceSq = minDistance * minDistance;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (HorizontalSqrMagnitude(candidates[i] - resolved) < minDistanceSq)
                return false;
        }

        candidates.Add(resolved);
        return true;
    }

    private static bool TryResolveNearestMainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        out Vector3 result)
    {
        result = Vector3.zero;
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            return false;

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float bestDistance = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
            for (int x = originX - radius; x <= originX + radius; x++)
            {
                if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                    continue;
                if (!IsMainIslandWalkable(grid, derivedData, x, y))
                    continue;

                Vector3 candidate = grid.GetCellAnchor(x, y);
                float distance = HorizontalSqrMagnitude(candidate - preferred);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestX = x;
                    bestY = y;
                }
            }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            return false;

        result = grid.GetCellAnchor(bestX, bestY);
        return true;
    }

    private static Vector3 ResolveNearestMainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            throw new InvalidOperationException($"ResolveNearestMainIslandCell failed: {label} preferred point {preferred} is outside grid.");

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float bestDistance = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
            for (int x = originX - radius; x <= originX + radius; x++)
            {
                if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                    continue;
                if (!IsMainIslandWalkable(grid, derivedData, x, y))
                    continue;

                Vector3 candidate = grid.GetCellAnchor(x, y);
                float distance = HorizontalSqrMagnitude(candidate - preferred);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestX = x;
                    bestY = y;
                }
            }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveNearestMainIslandCell failed: no main-island walkable cell found for {label}, preferred={preferred}, maxRadius={maxRadius:F2}.");

        Vector3 result = grid.GetCellAnchor(bestX, bestY);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(result)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" island=").Append(derivedData.IslandIds[bestX + bestY * grid.Width])
            .AppendLine();
        return result;
    }

    private static Vector3 ResolveRuntimeLegalLv3MainIslandCell(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        float maxRadius,
        float agentRadius,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        if (!grid.WorldToCell(preferred, out int originX, out int originY))
            throw new InvalidOperationException($"ResolveRuntimeLegalLv3MainIslandCell failed: {label} preferred point {preferred} is outside grid.");

        int maxRadiusCells = Mathf.CeilToInt(maxRadius / grid.CellSize);
        float edgeClearance = Mathf.Max(0f, agentRadius - grid.CellSize * 0.2f);
        float bestDistance = float.PositiveInfinity;
        Vector3 best = Vector3.zero;
        int bestX = -1;
        int bestY = -1;
        for (int radius = 0; radius <= maxRadiusCells; radius++)
        {
            for (int y = originY - radius; y <= originY + radius; y++)
            for (int x = originX - radius; x <= originX + radius; x++)
            {
                if (Mathf.Abs(x - originX) != radius && Mathf.Abs(y - originY) != radius)
                    continue;
                if (!IsMainIslandWalkable(grid, derivedData, x, y))
                    continue;

                Vector3 candidate = grid.GetCellAnchor(x, y);
                if (!FlowFieldCrowdMovementSystem.TryResolveLegalNavigationPoint(
                        candidate,
                        grid.AgentTypeId,
                        grid.CellSize * 0.75f,
                        edgeClearance,
                        out Vector3 legal))
                {
                    continue;
                }

                if (!grid.WorldToCell(legal, out int legalX, out int legalY) || !IsMainIslandWalkable(grid, derivedData, legalX, legalY))
                    continue;

                float distance = HorizontalSqrMagnitude(legal - preferred);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = legal;
                    bestX = legalX;
                    bestY = legalY;
                }
            }

            if (bestX >= 0)
                break;
        }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveRuntimeLegalLv3MainIslandCell failed: no runtime legal main-island cell found for {label}, preferred={preferred}, maxRadius={maxRadius:F2}, clearance={edgeClearance:F3}.");

        bool hasClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
            best,
            edgeClearance,
            out bool isClear,
            out float violation,
            out int runtimeBoxCount,
            out int runtimeCircleCount);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(best)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" island=").Append(derivedData.IslandIds[bestX + bestY * grid.Width])
            .Append(" runtimeClearance=").Append(hasClearance ? isClear.ToString() : "missing")
            .Append(" violation=").Append(hasClearance ? violation.ToString("F3") : "missing")
            .Append(" runtimeBoxes=").Append(hasClearance ? runtimeBoxCount.ToString() : "missing")
            .Append(" runtimeCircles=").Append(hasClearance ? runtimeCircleCount.ToString() : "missing")
            .AppendLine();
        return best;
    }

    private static Vector3 ResolveBestMainIslandCellInWorldBounds(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 preferred,
        Vector2 minWorld,
        Vector2 maxWorld,
        string label,
        System.Text.StringBuilder diagnostics)
    {
        grid.WorldToCell(new Vector3(minWorld.x, 0f, minWorld.y), out int minX, out int minY);
        grid.WorldToCell(new Vector3(maxWorld.x, 0f, maxWorld.y), out int maxX, out int maxY);
        minX = Mathf.Clamp(minX, 0, grid.Width - 1);
        maxX = Mathf.Clamp(maxX, 0, grid.Width - 1);
        minY = Mathf.Clamp(minY, 0, grid.Height - 1);
        maxY = Mathf.Clamp(maxY, 0, grid.Height - 1);

        float bestScore = float.PositiveInfinity;
        int bestX = -1;
        int bestY = -1;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            if (!IsMainIslandWalkable(grid, derivedData, x, y))
                continue;

            Vector3 candidate = grid.GetCellAnchor(x, y);
            float score = HorizontalSqrMagnitude(candidate - preferred);
            if (score < bestScore)
            {
                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }

        if (bestX < 0)
            throw new InvalidOperationException($"ResolveBestMainIslandCellInWorldBounds failed: no main-island walkable cell found for {label}, bounds=({minWorld})..({maxWorld}).");

        Vector3 result = grid.GetCellAnchor(bestX, bestY);
        diagnostics.Append("[Lv3Route] ").Append(label)
            .Append(" preferred=").Append(preferred)
            .Append(" resolved=").Append(result)
            .Append(" cell=(").Append(bestX).Append(',').Append(bestY).Append(')')
            .Append(" bounds=(").Append(minWorld).Append(")..(").Append(maxWorld).Append(')')
            .AppendLine();
        return result;
    }

    private static bool IsMainIslandWalkable(FlowNavigationGridAsset grid, FlowNavigationGridAsset.DerivedNavigationData derivedData, int x, int y)
    {
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return false;
        if (!grid.IsCellWalkable(x, y))
            return false;
        return derivedData.IslandIds[x + y * grid.Width] == derivedData.MainIslandId;
    }

    private static int ResolveRequiredLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            throw new InvalidOperationException($"Required Unity layer is missing: {layerName}.");
        return layer;
    }

    private static bool IsInsideLv3RightBottomCornerWindow(Vector3 position, Vector3 corner)
    {
        return position.x >= corner.x - 4.0f
               && position.x <= corner.x + 2.0f
               && position.z >= corner.z - 2.2f
               && position.z <= corner.z + 4.0f;
    }

    private static float HorizontalSqrMagnitude(Vector3 value)
    {
        return value.x * value.x + value.z * value.z;
    }

    private static void AppendCellDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 position,
        string label)
    {
        if (!grid.WorldToCell(position, out int x, out int y))
        {
            builder.Append(' ').Append(label).Append("Cell=outside");
            return;
        }

        int index = x + y * grid.Width;
        int island = derivedData != null && derivedData.IsValid && derivedData.IslandIds != null && index >= 0 && index < derivedData.IslandIds.Length
            ? derivedData.IslandIds[index]
            : -1;
        int sector = derivedData != null && derivedData.IsValid ? ResolveDerivedSectorId(derivedData, x, y) : -1;
        builder.Append(' ').Append(label)
            .Append("Cell=(").Append(x).Append(',').Append(y).Append(')')
            .Append("/walk=").Append(grid.IsCellWalkable(x, y))
            .Append("/island=").Append(island)
            .Append("/sector=").Append(sector);
    }

    private static void AppendControllerCombatTickFailureDiagnostics(
        System.Text.StringBuilder builder,
        Lv3CrowdRouteScenario scenario,
        int frame,
        int agentIndex,
        SimEntityContext chaser,
        SimEntityContext hero,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 chaserBefore,
        Vector3 heroPosition,
        int waypointIndex,
        int waypointCount,
        Lv3ResearchCenterFootprint researchCenter)
    {
        IEntityContext target = chaser?.TargetComp?.CurrentTarget;
        builder.Append("[Lv3ControllerCombatTickFailure] scenario=").Append(scenario.Name)
            .Append(" frame=").Append(frame)
            .Append(" agent=").Append(agentIndex)
            .Append(" targetIsHero=").Append(ReferenceEquals(target, hero))
            .Append(" targetNull=").Append(target == null)
            .Append(" waypoint=").Append(waypointIndex).Append('/').Append(Mathf.Max(0, waypointCount - 1))
            .Append(" chaserBefore=").Append(chaserBefore)
            .Append(" chaserNow=").Append(chaser != null ? chaser.Position : default)
            .Append(" hero=").Append(heroPosition);
        if (target != null)
        {
            builder.Append(" targetKey=").Append(target.CharacterKey)
                .Append(" targetSide=").Append(target.Side)
                .Append(" targetAlive=").Append(target.Alive)
                .Append(" targetPos=").Append(target.Position)
                .Append(" targetDist=").Append(chaser != null ? chaser.DistanceToTargetSurface(target).ToString("F3") : "NA");
        }

        builder.Append(" researchDist=").Append(researchCenter.DistanceToClosestBox(chaserBefore).ToString("F3"));
        AppendCellDiagnostic(builder, grid, derivedData, chaserBefore, "assetSelfBefore");
        AppendRuntimeCellDiagnostic(builder, grid, chaserBefore, "runtimeSelfBefore");
        AppendRuntimeNeighborhoodDiagnostic(builder, grid, chaserBefore, "runtimeSelfNeighborhood", 2);
        if (chaser != null)
        {
            AppendCellDiagnostic(builder, grid, derivedData, chaser.Position, "assetSelfNow");
            AppendRuntimeCellDiagnostic(builder, grid, chaser.Position, "runtimeSelfNow");
            AppendSteeringBreakdown(builder, chaser);
            AppendPathHandleDiagnostics(builder, chaser, grid, derivedData, heroPosition);
        }

        AppendCellDiagnostic(builder, grid, derivedData, heroPosition, "assetHero");
        AppendRuntimeCellDiagnostic(builder, grid, heroPosition, "runtimeHero");
        AppendRuntimeNeighborhoodDiagnostic(builder, grid, heroPosition, "runtimeHeroNeighborhood", 2);
        if (target != null && !ReferenceEquals(target, hero))
        {
            AppendCellDiagnostic(builder, grid, derivedData, target.Position, "assetTarget");
            AppendRuntimeCellDiagnostic(builder, grid, target.Position, "runtimeTarget");
            AppendRuntimeNeighborhoodDiagnostic(builder, grid, target.Position, "runtimeTargetNeighborhood", 2);
        }

        builder.AppendLine();
    }

    private static void AppendRuntimeCellDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        Vector3 position,
        string label)
    {
        if (!grid.WorldToCell(position, out int x, out int y))
        {
            builder.Append(' ').Append(label).Append("=outside");
            return;
        }

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
                x,
                y,
                out bool walkable,
                out int islandId,
                out int islandCount,
                out int mainIslandId,
                out int mainIslandSize,
                out byte neighborTraversalMask))
        {
            builder.Append(' ').Append(label).Append("=unavailable cell=(").Append(x).Append(',').Append(y).Append(')');
            return;
        }

        builder.Append(' ').Append(label)
            .Append("Cell=(").Append(x).Append(',').Append(y).Append(')')
            .Append("/walk=").Append(walkable)
            .Append("/island=").Append(islandId)
            .Append("/islands=").Append(islandCount)
            .Append("/main=").Append(mainIslandId).Append(':').Append(mainIslandSize)
            .Append("/mask=0x").Append(neighborTraversalMask.ToString("X2"));
    }

    private static void AppendRuntimeNeighborhoodDiagnostic(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        Vector3 position,
        string label,
        int radius)
    {
        if (!grid.WorldToCell(position, out int centerX, out int centerY))
        {
            builder.Append(' ').Append(label).Append("=outside");
            return;
        }

        builder.Append(' ').Append(label).Append('=');
        for (int y = centerY + radius; y >= centerY - radius; y--)
        {
            if (y < centerY + radius)
                builder.Append('|');
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x > centerX - radius)
                    builder.Append(',');
                if (!FlowFieldCrowdMovementSystem.TryGetEditorTestRuntimeCellDiagnostics(
                        x,
                        y,
                        out bool walkable,
                        out int islandId,
                        out _,
                        out _,
                        out _,
                        out byte neighborTraversalMask))
                {
                    builder.Append("out");
                    continue;
                }

                builder.Append(walkable ? 'W' : 'B')
                    .Append(islandId)
                    .Append(':')
                    .Append(neighborTraversalMask.ToString("X2"));
            }
        }
    }

    private static void AppendCharacterControllerPhysicsDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        MoveExecutor executor)
    {
        if (controller == null)
        {
            builder.Append("/cc=null");
            return;
        }

        Vector3 requestedDisplacement = executor != null ? executor.DebugRequestedHorizontalDisplacement : Vector3.zero;
        requestedDisplacement.y = 0f;
        Vector3 constrainedDisplacement = executor != null ? executor.DebugConstrainedHorizontalDisplacement : Vector3.zero;
        constrainedDisplacement.y = 0f;
        Vector3 actual = executor != null ? executor.DebugActualHorizontalDisplacement : Vector3.zero;
        actual.y = 0f;
        builder.Append("/ccFlags=").Append(controller.collisionFlags)
            .Append("/ccGrounded=").Append(controller.isGrounded)
            .Append("/ccVelocity=").Append(controller.velocity)
            .Append("/ccRequestedDisp=").Append(requestedDisplacement)
            .Append("/ccConstrainedDisp=").Append(constrainedDisplacement)
            .Append("/ccActual=").Append(actual);

        if (executor != null)
        {
            builder.Append("/ccHit=")
                .Append(string.IsNullOrEmpty(executor.DebugLastControllerHitName) ? "none" : executor.DebugLastControllerHitName)
                .Append("/ccHitNormal=").Append(executor.DebugLastControllerHitNormal)
                .Append("/ccHitMoveDir=").Append(executor.DebugLastControllerHitMoveDirection)
                .Append("/ccVerticalDisp=").Append(executor.DebugVerticalDisplacement)
                .Append("/ccFinalDisp=").Append(executor.DebugFinalDisplacement);
        }

        AppendControllerOverlapDiagnostics(builder, controller);
        AppendControllerCastDiagnostics(builder, controller, constrainedDisplacement);
        if (executor != null)
            AppendControllerMoveProbeDiagnostics(builder, controller, constrainedDisplacement, executor.DebugVerticalDisplacement);
    }

    private static void AppendControllerCandidateProbeDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        SimEntityContext entity,
        float deltaTime)
    {
        if (controller == null || entity == null)
            return;

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                entity.GetHashCode(),
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 agentAvoidance,
                out Vector3 clampedAgentAvoidance,
                out Vector3 boundaryAvoidance,
                out Vector3 laneVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            return;
        }

        FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSeparationDiagnostic(
            entity.GetHashCode(),
            out _,
            out Vector3 immediateSeparation,
            out Vector3 strongestImmediateSeparation,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out float maxSpeed);

        FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringConstraintDiagnostic(
            entity.GetHashCode(),
            out Vector3 rawCombinedVelocity,
            out Vector3 firstConstrainedVelocity,
            out _,
            out _,
            out _,
            out _,
            out _,
            out Vector3 finalConstraintInputVelocity,
            out _,
            out _,
            out _,
            out _,
            out _);

        if (maxSpeed <= 0.0001f)
            maxSpeed = Mathf.Max(desiredVelocity.magnitude, result.magnitude);

        builder.Append("/ccCandidateCasts=");
        bool first = true;
        AppendControllerCandidateCast(builder, controller, "result", result, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "pre", resultPreClamp, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "raw", rawCombinedVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "first", firstConstrainedVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "finalIn", finalConstraintInputVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "desired", desiredVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "base", baseVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "clampedAvoid", clampedAgentAvoidance, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "boundary", boundaryAvoidance, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "lane", laneVelocity, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "immediate", immediateSeparation, deltaTime, maxSpeed, ref first);
        AppendControllerCandidateCast(builder, controller, "strongest", strongestImmediateSeparation, deltaTime, maxSpeed, ref first);
        if (first)
            builder.Append("none");
    }

    private static void AppendControllerCandidateCast(
        System.Text.StringBuilder builder,
        CharacterController controller,
        string label,
        Vector3 velocity,
        float deltaTime,
        float maxSpeed,
        ref bool first)
    {
        velocity.y = 0f;
        if (velocity.sqrMagnitude <= 0.0001f || deltaTime <= 0f)
            return;

        Vector3 clampedVelocity = maxSpeed > 0.0001f
            ? Vector3.ClampMagnitude(velocity, maxSpeed)
            : velocity;
        Vector3 displacement = clampedVelocity * deltaTime;
        displacement.y = 0f;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector3 direction = displacement / distance;
        Vector3 center = controller.transform.position + controller.center;
        float radius = Mathf.Max(0.01f, controller.radius - 0.01f);
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (!first)
            builder.Append('|');
        first = false;
        builder.Append(label)
            .Append(":vel=").Append(clampedVelocity)
            .Append(",disp=").Append(displacement)
            .Append(",hit=");

        int emitted = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('&');
            builder.Append(hit.gameObject.name)
                .Append('@')
                .Append(hits[i].distance.ToString("F3"));
            emitted++;
            if (emitted >= 3)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendControllerMoveProbeDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController source,
        Vector3 horizontalDisplacement,
        Vector3 verticalDisplacement)
    {
        bool sourceWasEnabled = source.enabled;
        source.enabled = false;
        GameObject probeObject = null;
        try
        {
            probeObject = new GameObject("FlowTest_CC_MoveProbe");
            probeObject.transform.position = source.transform.position;
            CharacterController probe = probeObject.AddComponent<CharacterController>();
            probe.radius = source.radius;
            probe.height = source.height;
            probe.center = source.center;
            probe.slopeLimit = source.slopeLimit;
            probe.stepOffset = source.stepOffset;
            probe.skinWidth = source.skinWidth;
            probe.minMoveDistance = source.minMoveDistance;
            probe.detectCollisions = source.detectCollisions;
            probe.enableOverlapRecovery = source.enableOverlapRecovery;
            ControllerHitRecorder recorder = probeObject.AddComponent<ControllerHitRecorder>();
            Physics.SyncTransforms();

            Vector3 start = probeObject.transform.position;
            CollisionFlags horizontalFlags = probe.Move(horizontalDisplacement);
            Vector3 horizontalActual = probeObject.transform.position - start;

            probe.enabled = false;
            probeObject.transform.position = source.transform.position;
            probe.enabled = true;
            Physics.SyncTransforms();
            start = probeObject.transform.position;
            CollisionFlags verticalFlags = probe.Move(verticalDisplacement);
            Vector3 verticalActual = probeObject.transform.position - start;

            probe.enabled = false;
            probeObject.transform.position = source.transform.position;
            probe.enabled = true;
            Physics.SyncTransforms();
            start = probeObject.transform.position;
            CollisionFlags combinedFlags = probe.Move(horizontalDisplacement + verticalDisplacement);
            Vector3 combinedActual = probeObject.transform.position - start;

            builder.Append("/ccProbeHFlags=").Append(horizontalFlags)
                .Append("/ccProbeHActual=").Append(horizontalActual)
                .Append("/ccProbeVFlags=").Append(verticalFlags)
                .Append("/ccProbeVActual=").Append(verticalActual)
                .Append("/ccProbeCFlags=").Append(combinedFlags)
                .Append("/ccProbeCActual=").Append(combinedActual)
                .Append("/ccProbeHit=")
                .Append(string.IsNullOrEmpty(recorder.LastHitName) ? "none" : recorder.LastHitName)
                .Append("/ccProbeHitNormal=").Append(recorder.LastHitNormal)
                .Append("/ccProbeHitMoveDir=").Append(recorder.LastHitMoveDirection);
        }
        finally
        {
            source.enabled = sourceWasEnabled;
            if (probeObject != null)
                UnityEngine.Object.DestroyImmediate(probeObject);
            Physics.SyncTransforms();
        }
    }

    private sealed class ControllerHitRecorder : MonoBehaviour
    {
        public string LastHitName { get; private set; }
        public Vector3 LastHitNormal { get; private set; }
        public Vector3 LastHitMoveDirection { get; private set; }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null || hit.collider == null)
                return;

            LastHitName = hit.collider.gameObject.name;
            LastHitNormal = hit.normal;
            LastHitMoveDirection = hit.moveDirection;
        }
    }

    private static void AppendControllerOverlapDiagnostics(System.Text.StringBuilder builder, CharacterController controller)
    {
        Vector3 center = controller.transform.position + controller.center;
        float radius = controller.radius + 0.03f;
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
        int emitted = 0;
        builder.Append("/ccOverlaps=");
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider hit = overlaps[i];
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('|');
            builder.Append(hit.gameObject.name);
            emitted++;
            if (emitted >= 6)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendControllerCastDiagnostics(
        System.Text.StringBuilder builder,
        CharacterController controller,
        Vector3 requestedDisplacement)
    {
        Vector3 direction = requestedDisplacement;
        direction.y = 0f;
        float distance = direction.magnitude;
        builder.Append("/ccCast=");
        if (distance <= 0.0001f)
        {
            builder.Append("none");
            return;
        }

        direction /= distance;
        Vector3 center = controller.transform.position + controller.center;
        float radius = Mathf.Max(0.01f, controller.radius - 0.01f);
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        int emitted = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == controller.transform)
                continue;

            if (emitted > 0)
                builder.Append('|');
            builder.Append(hit.gameObject.name)
                .Append('@')
                .Append(hits[i].distance.ToString("F3"));
            emitted++;
            if (emitted >= 6)
                break;
        }

        if (emitted == 0)
            builder.Append("none");
    }

    private static void AppendPathHandleDiagnostics(
        System.Text.StringBuilder builder,
        SimEntityContext chaser,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Vector3 goal)
    {
        if (chaser == null)
            throw new InvalidOperationException("AppendPathHandleDiagnostics failed: chaser is null.");
        if (!grid.WorldToCell(chaser.Position, out int startX, out int startY)
            || !grid.WorldToCell(goal, out int goalX, out int goalY))
        {
            builder.Append("/pathDiag=outside-grid");
            return;
        }

        int startSectorId = ResolveDerivedSectorId(derivedData, startX, startY);
        int goalSectorId = ResolveDerivedSectorId(derivedData, goalX, goalY);
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathBuildSource(chaser.GetHashCode(), out string buildSource))
            builder.Append("/pathSource=").Append(buildSource);
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.GetHashCode(), out int[] sectorIds))
            builder.Append("/sectors=").Append(string.Join(">", sectorIds));
        if (FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.GetHashCode(), out int[] portalIds))
            builder.Append("/portals=").Append(string.Join(">", portalIds));
        AppendStableGoalDiagnostics(builder, chaser, grid, derivedData);
        AppendSelectedPortalGeometryDiagnostics(builder, chaser.Position, startSectorId, sectorIds, portalIds);
        builder.Append("/portalChoice=startSector=").Append(startSectorId)
            .Append(",goalSector=").Append(goalSectorId)
            .Append(",start=(").Append(startX).Append(',').Append(startY).Append(')')
            .Append(",goal=(").Append(goalX).Append(',').Append(goalY).Append(')');
    }

    private static void AppendStableGoalDiagnostics(
        System.Text.StringBuilder builder,
        SimEntityContext chaser,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                chaser.GetHashCode(),
                out int targetId,
                out int rawX,
                out int rawY,
                out int stableX,
                out int stableY,
                out Vector3 stableWorld))
        {
            builder.Append("/stableGoal=missing");
            return;
        }

        builder.Append("/stableGoal=target:").Append(targetId)
            .Append(",raw=(").Append(rawX).Append(',').Append(rawY).Append(')')
            .Append(",rawWalk=").Append(grid.IsCellWalkable(rawX, rawY))
            .Append(",rawIsland=").Append(ResolveIslandForCell(derivedData, grid, rawX, rawY))
            .Append(",stable=(").Append(stableX).Append(',').Append(stableY).Append(')')
            .Append(",stableWalk=").Append(grid.IsCellWalkable(stableX, stableY))
            .Append(",stableIsland=").Append(ResolveIslandForCell(derivedData, grid, stableX, stableY))
            .Append(",world=").Append(stableWorld)
            .Append('/')
            .Append(FlowFieldCrowdMovementSystem.GetEditorTestMovingTargetAnchorDiagnostics(chaser.GetHashCode()));
    }

    private static void AppendSegmentGridDiagnostics(
        System.Text.StringBuilder builder,
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3ResearchCenterFootprint footprint,
        Vector3 from,
        string label,
        SimEntityContext chaser)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringResolution(
                chaser.GetHashCode(),
                out _,
                out _,
                out _,
                out Vector3 tileTarget,
                out _,
                out _,
                out _))
        {
            builder.Append('/').Append(label).Append("=steering-resolution-missing");
            return;
        }

        Vector3 delta = tileTarget - from;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            builder.Append('/').Append(label).Append("=zero");
            return;
        }

        Vector3 direction = delta / distance;
        float step = Mathf.Max(0.02f, grid.CellSize * 0.5f);
        int sampleCount = Mathf.Min(96, Mathf.CeilToInt(distance / step) + 1);
        builder.Append('/').Append(label).Append("=dist:").Append(distance.ToString("F3")).Append(",samples:[");
        int previousX = int.MinValue;
        int previousY = int.MinValue;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = Mathf.Min(distance, i * step);
            Vector3 sample = from + direction * t;
            if (!grid.WorldToCell(sample, out int x, out int y))
            {
                builder.Append("(outside@").Append(t.ToString("F2")).Append(')');
                continue;
            }

            if (x == previousX && y == previousY)
                continue;

            previousX = x;
            previousY = y;
            builder.Append('(')
                .Append(x).Append(',').Append(y)
                .Append("@").Append(t.ToString("F2"))
                .Append("/walk=").Append(grid.IsCellWalkable(x, y))
                .Append("/island=").Append(ResolveIslandForCell(derivedData, grid, x, y))
                .Append("/insideBox=").Append(footprint.ContainsPoint(sample))
                .Append(')');
        }

        builder.Append(']');
    }

    private static bool SegmentEntersFootprint(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        float step)
    {
        if (direction.sqrMagnitude <= 0.0001f || maxDistance <= 0f)
            return false;

        direction.y = 0f;
        direction.Normalize();
        step = Mathf.Max(0.01f, step);
        int sampleCount = Mathf.CeilToInt(maxDistance / step);
        for (int i = 0; i <= sampleCount; i++)
        {
            float distance = Mathf.Min(maxDistance, i * step);
            if (footprint.ContainsPoint(origin + direction * distance))
                return true;
        }

        return false;
    }

    private static bool SegmentViolatesFootprintClearance(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        float step,
        float clearance)
    {
        if (direction.sqrMagnitude <= 0.0001f || maxDistance <= 0f)
            return false;

        direction.y = 0f;
        direction.Normalize();
        step = Mathf.Max(0.01f, step);
        float clampedClearance = Mathf.Max(0f, clearance);
        int sampleCount = Mathf.CeilToInt(maxDistance / step);
        for (int i = 0; i <= sampleCount; i++)
        {
            float distance = Mathf.Min(maxDistance, i * step);
            if (footprint.DistanceToClosestBox(origin + direction * distance) < clampedClearance)
                return true;
        }

        return false;
    }

    private static bool SteeringTileTargetSegmentViolatesFootprintClearance(
        Lv3ResearchCenterFootprint footprint,
        Vector3 origin,
        SimEntityContext chaser,
        float step,
        float clearance)
    {
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringResolution(
                chaser.GetHashCode(),
                out _,
                out _,
                out _,
                out Vector3 tileTarget,
                out _,
                out _,
                out _))
        {
            return false;
        }

        Vector3 delta = tileTarget - origin;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return false;

        return SegmentViolatesFootprintClearance(
            footprint,
            origin,
            delta / distance,
            distance,
            step,
            clearance);
    }

    private static int ResolveIslandForCell(
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        FlowNavigationGridAsset grid,
        int x,
        int y)
    {
        if (derivedData == null || !derivedData.IsValid || derivedData.IslandIds == null)
            return -1;
        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height)
            return -1;

        int index = x + y * grid.Width;
        return index >= 0 && index < derivedData.IslandIds.Length ? derivedData.IslandIds[index] : -1;
    }

    private static void AppendSelectedPortalGeometryDiagnostics(
        System.Text.StringBuilder builder,
        Vector3 position,
        int startSectorId,
        int[] sectorIds,
        int[] portalIds)
    {
        if (sectorIds == null || portalIds == null || portalIds.Length == 0)
        {
            builder.Append("/selectedPortal=missing");
            return;
        }

        int sectorIndex = -1;
        for (int i = 0; i < sectorIds.Length; i++)
        {
            if (sectorIds[i] == startSectorId)
            {
                sectorIndex = i;
                break;
            }
        }

        if (sectorIndex < 0 || sectorIndex >= portalIds.Length)
        {
            builder.Append("/selectedPortal=not-in-current-sector");
            return;
        }

        int portalId = portalIds[sectorIndex];
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestPortalSummary(
                portalId,
                out int sectorAId,
                out int sectorBId,
                out int widthCells,
                out bool isVerticalBoundary,
                out Vector3 center))
        {
            builder.Append("/selectedPortal=").Append(portalId).Append(":summary-missing");
            return;
        }

        Vector3 toPortal = center - position;
        toPortal.y = 0f;
        builder.Append("/selectedPortal=").Append(portalId)
            .Append(",sectors=").Append(sectorAId).Append('>').Append(sectorBId)
            .Append(",width=").Append(widthCells)
            .Append(",vertical=").Append(isVerticalBoundary)
            .Append(",center=").Append(center)
            .Append(",toPortal=").Append(toPortal.sqrMagnitude > 0.0001f ? toPortal.normalized : Vector3.zero);
    }

    private static bool TryResolveSelectedPortalCenter(SimEntityContext chaser, int startSectorId, out int portalId, out Vector3 center)
    {
        portalId = -1;
        center = Vector3.zero;
        if (chaser == null)
            throw new InvalidOperationException("TryResolveSelectedPortalCenter failed: chaser is null.");
        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestPathSectorIds(chaser.GetHashCode(), out int[] sectorIds)
            || !FlowFieldCrowdMovementSystem.TryGetEditorTestPathPortalIds(chaser.GetHashCode(), out int[] portalIds))
        {
            return false;
        }

        int sectorIndex = -1;
        for (int i = 0; i < sectorIds.Length; i++)
        {
            if (sectorIds[i] == startSectorId)
            {
                sectorIndex = i;
                break;
            }
        }

        if (sectorIndex < 0 || sectorIndex >= portalIds.Length)
            return false;

        portalId = portalIds[sectorIndex];
        return FlowFieldCrowdMovementSystem.TryGetEditorTestPortalSummary(
            portalId,
            out _,
            out _,
            out _,
            out _,
            out center);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static void AppendLv3RouteMap(
        FlowNavigationGridAsset grid,
        FlowNavigationGridAsset.DerivedNavigationData derivedData,
        Lv3RightBottomRoute route,
        System.Text.StringBuilder diagnostics)
    {
        diagnostics.Append("[Lv3Route] heroStart=").Append(route.HeroStart)
            .Append(" lower=").Append(route.LowerApproach)
            .Append(" corner=").Append(route.RightBottomCorner)
            .Append(" return=").Append(route.ReturnPoint)
            .Append(" researchCenter=").Append(route.ResearchCenterPosition)
            .Append(" researchBounds=").Append(route.ResearchCenterBounds)
            .AppendLine();

        int minX;
        int minY;
        int maxX;
        int maxY;
        float minWorldX = Mathf.Min(route.ResearchCenterBounds.xMin - 8f, route.HeroStart.x - 4f, route.RightBottomCorner.x - 4f);
        float maxWorldX = Mathf.Max(route.ResearchCenterBounds.xMax + 8f, route.HeroStart.x + 4f, route.RightBottomCorner.x + 4f);
        float minWorldZ = Mathf.Min(route.ResearchCenterBounds.yMin - 5f, route.HeroStart.z - 4f, route.RightBottomCorner.z - 4f);
        float maxWorldZ = Mathf.Max(route.ResearchCenterBounds.yMax + 6f, route.HeroStart.z + 4f, route.RightBottomCorner.z + 4f);
        grid.WorldToCell(new Vector3(minWorldX, 0f, minWorldZ), out minX, out minY);
        grid.WorldToCell(new Vector3(maxWorldX, 0f, maxWorldZ), out maxX, out maxY);
        minX = Mathf.Clamp(minX, 0, grid.Width - 1);
        maxX = Mathf.Clamp(maxX, 0, grid.Width - 1);
        minY = Mathf.Clamp(minY, 0, grid.Height - 1);
        maxY = Mathf.Clamp(maxY, 0, grid.Height - 1);

        diagnostics.Append("[Lv3RouteMap] W=main w=other .=blocked step=8 cells").AppendLine();
        for (int y = maxY; y >= minY; y -= 8)
        {
            diagnostics.Append("z=").Append(grid.GetCellCenter(minX, y).z.ToString("F1")).Append(' ');
            for (int x = minX; x <= maxX; x += 8)
            {
                if (!grid.IsCellWalkable(x, y))
                {
                    diagnostics.Append('.');
                    continue;
                }

                diagnostics.Append(derivedData.IslandIds[x + y * grid.Width] == derivedData.MainIslandId ? 'W' : 'w');
            }

            diagnostics.AppendLine();
        }
    }

    private struct Lv3ResearchCenterFootprint
    {
        public Rect[] Boxes;

        public Rect CalculateBounds()
        {
            if (Boxes == null || Boxes.Length == 0)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.CalculateBounds failed: Boxes is empty.");

            float minX = Boxes[0].xMin;
            float maxX = Boxes[0].xMax;
            float minZ = Boxes[0].yMin;
            float maxZ = Boxes[0].yMax;
            for (int i = 1; i < Boxes.Length; i++)
            {
                minX = Mathf.Min(minX, Boxes[i].xMin);
                maxX = Mathf.Max(maxX, Boxes[i].xMax);
                minZ = Mathf.Min(minZ, Boxes[i].yMin);
                maxZ = Mathf.Max(maxZ, Boxes[i].yMax);
            }

            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        public bool RayHitsAnyBox(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.RayHitsAnyBox failed: Boxes is null.");
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (RayIntersectsRect(origin, direction, maxDistance, Boxes[i]))
                    return true;
            }

            return false;
        }

        public float DistanceToFirstRayHit(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.DistanceToFirstRayHit failed: Boxes is null.");

            float best = float.PositiveInfinity;
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (TryGetRayRectDistance(origin, direction, maxDistance, Boxes[i], out float distance))
                    best = Mathf.Min(best, distance);
            }

            return float.IsPositiveInfinity(best) ? 0f : best;
        }

        public float DistanceToClosestBox(Vector3 point)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.DistanceToClosestBox failed: Boxes is null.");

            float best = float.PositiveInfinity;
            for (int i = 0; i < Boxes.Length; i++)
            {
                Rect box = Boxes[i];
                float dx = Mathf.Max(box.xMin - point.x, 0f, point.x - box.xMax);
                float dz = Mathf.Max(box.yMin - point.z, 0f, point.z - box.yMax);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return float.IsPositiveInfinity(best) ? 0f : best;
        }

        public bool ContainsPoint(Vector3 point)
        {
            if (Boxes == null)
                throw new InvalidOperationException("Lv3ResearchCenterFootprint.ContainsPoint failed: Boxes is null.");

            for (int i = 0; i < Boxes.Length; i++)
            {
                Rect box = Boxes[i];
                if (point.x >= box.xMin
                    && point.x <= box.xMax
                    && point.z >= box.yMin
                    && point.z <= box.yMax)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static Lv3ResearchCenterFootprint CreateLv3ResearchCenterLv1Footprint(Vector3 root)
    {
        Rect[] boxes =
        {
            CreateWorldRect(root, new Vector2(-0.5143721f, 2.0f), new Vector2(5.143723f, 1.3333333f)),
            CreateWorldRect(root, new Vector2(0.51437247f, -1.9999996f), new Vector2(5.1437225f, 3.9999998f)),
            CreateWorldRect(root, new Vector2(0.00000023841858f, 0.66666687f), new Vector2(4.1149783f, 1.3333333f)),
            CreateWorldRect(root, new Vector2(-0.51437217f, 3.3333335f), new Vector2(1.0287446f, 1.3333333f)),
        };

        return new Lv3ResearchCenterFootprint { Boxes = boxes };
    }

    private static void RegisterLv3ResearchCenterFootprintObstacles(Lv3ResearchCenterFootprint footprint, int obstacleIdBase)
    {
        if (footprint.Boxes == null || footprint.Boxes.Length == 0)
            throw new InvalidOperationException("RegisterLv3ResearchCenterFootprintObstacles failed: footprint boxes are empty.");

        for (int i = 0; i < footprint.Boxes.Length; i++)
        {
            Rect box = footprint.Boxes[i];
            Vector3 center = new Vector3(box.center.x, 0f, box.center.y);
            Vector3 halfExtents = new Vector3(box.width * 0.5f, 0f, box.height * 0.5f);
            FlowFieldCrowdMovementSystem.RegisterBoxObstacle(obstacleIdBase + i, center, halfExtents);
        }
    }

    private static Rect CreateWorldRect(Vector3 root, Vector2 localCenter, Vector2 size)
    {
        return new Rect(
            root.x + localCenter.x - size.x * 0.5f,
            root.z + localCenter.y - size.y * 0.5f,
            size.x,
            size.y);
    }

    private static bool RayIntersectsRect(Vector3 origin, Vector3 direction, float maxDistance, Rect rect)
    {
        return TryGetRayRectDistance(origin, direction, maxDistance, rect, out _);
    }

    private static bool TryGetRayRectDistance(Vector3 origin, Vector3 direction, float maxDistance, Rect rect, out float distance)
    {
        distance = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        float originX = origin.x;
        float originZ = origin.z;
        float dirX = direction.x;
        float dirZ = direction.z;
        float tMin = 0f;
        float tMax = maxDistance;
        if (!ClipRayAxis(originX, dirX, rect.xMin, rect.xMax, ref tMin, ref tMax))
            return false;
        if (!ClipRayAxis(originZ, dirZ, rect.yMin, rect.yMax, ref tMin, ref tMax))
            return false;
        if (tMax < 0f || tMin > maxDistance)
            return false;

        distance = Mathf.Max(0f, tMin);
        return true;
    }

    private static bool ClipRayAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(direction) < 0.00001f)
            return origin >= min && origin <= max;

        float inv = 1f / direction;
        float t1 = (min - origin) * inv;
        float t2 = (max - origin) * inv;
        if (t1 > t2)
        {
            float temp = t1;
            t1 = t2;
            t2 = temp;
        }

        tMin = Mathf.Max(tMin, t1);
        tMax = Mathf.Min(tMax, t2);
        return tMin <= tMax;
    }

    private sealed class TestPathOpenSet
    {
        private readonly List<TestPathNode> _heap = new List<TestPathNode>();

        public int Count => _heap.Count;

        public void Push(int index, float priority)
        {
            TestPathNode node = new TestPathNode(index, priority);
            _heap.Add(node);
            int child = _heap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (_heap[parent].Priority <= node.Priority)
                    break;
                _heap[child] = _heap[parent];
                child = parent;
            }

            _heap[child] = node;
        }

        public int Pop()
        {
            if (_heap.Count == 0)
                throw new InvalidOperationException("TestPathOpenSet.Pop failed: heap is empty.");

            int result = _heap[0].Index;
            TestPathNode tail = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count == 0)
                return result;

            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= _heap.Count)
                    break;
                int right = left + 1;
                int child = right < _heap.Count && _heap[right].Priority < _heap[left].Priority ? right : left;
                if (_heap[child].Priority >= tail.Priority)
                    break;
                _heap[parent] = _heap[child];
                parent = child;
            }

            _heap[parent] = tail;
            return result;
        }
    }

    private struct TestPathNode
    {
        public readonly int Index;
        public readonly float Priority;

        public TestPathNode(int index, float priority)
        {
            Index = index;
            Priority = priority;
        }
    }

    private static void AppendSteeringBreakdown(System.Text.StringBuilder builder, SimEntityContext entity)
    {
        if (entity == null)
        {
            builder.Append("/diag=null-entity");
            return;
        }

        if (!FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                entity.GetHashCode(),
                out Vector3 desiredVelocity,
                out Vector3 baseVelocity,
                out Vector3 agentAvoidance,
                out Vector3 clampedAgentAvoidance,
                out Vector3 boundaryAvoidance,
                out Vector3 laneVelocity,
                out Vector3 resultPreClamp,
                out Vector3 result))
        {
            builder.Append("/diag=none");
            return;
        }

        builder.Append("/desired=").Append(desiredVelocity)
            .Append("/base=").Append(baseVelocity)
            .Append("/avoid=").Append(agentAvoidance)
            .Append("/clampedAvoid=").Append(clampedAgentAvoidance)
            .Append("/boundary=").Append(boundaryAvoidance)
            .Append("/lane=").Append(laneVelocity)
            .Append("/pre=").Append(resultPreClamp)
            .Append("/result=").Append(result);

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringGoal(entity.GetHashCode(), out Vector3 lastSteeringGoal, out int lastSteeringGoalFrame))
        {
            builder.Append("/steerGoal=").Append(lastSteeringGoal)
                .Append("/steerFrame=").Append(lastSteeringGoalFrame);
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestStableGoal(
                entity.GetHashCode(),
                out int stableTargetId,
                out int stableRawX,
                out int stableRawY,
                out int stableX,
                out int stableY,
                out Vector3 stableWorld))
        {
            builder.Append("/stableTargetId=").Append(stableTargetId)
                .Append("/stableRaw=(").Append(stableRawX).Append(',').Append(stableRawY).Append(')')
                .Append("/stableCell=(").Append(stableX).Append(',').Append(stableY).Append(')')
                .Append("/stableWorld=").Append(stableWorld);
        }
        else
        {
            builder.Append("/stable=none");
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringTopAvoidanceDiagnostic(
                entity.GetHashCode(),
                out string topAvoidContributors))
        {
            builder.Append("/topAvoid=").Append(topAvoidContributors);
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringConstraintDiagnostic(
                entity.GetHashCode(),
                out Vector3 rawCombinedVelocity,
                out Vector3 firstConstrainedVelocity,
                out string firstConstraintKind,
                out float firstConstraintScore,
                out int firstTestedCandidateMask,
                out int firstWalkableCandidateMask,
                out bool firstOriginalWalkable,
                out Vector3 finalConstraintInputVelocity,
                out string finalConstraintKind,
                out float finalConstraintScore,
                out int finalTestedCandidateMask,
                out int finalWalkableCandidateMask,
                out bool finalOriginalWalkable))
        {
            builder.Append("/rawCombined=").Append(rawCombinedVelocity)
                .Append("/firstConstrained=").Append(firstConstrainedVelocity)
                .Append("/firstKind=").Append(firstConstraintKind)
                .Append("/firstScore=").Append(float.IsNegativeInfinity(firstConstraintScore) ? "-INF" : firstConstraintScore.ToString("F3"))
                .Append("/firstTestedMask=").Append(firstTestedCandidateMask)
                .Append("/firstWalkableMask=").Append(firstWalkableCandidateMask)
                .Append("/firstWalkable=").Append(firstOriginalWalkable)
                .Append("/finalInput=").Append(finalConstraintInputVelocity)
                .Append("/finalKind=").Append(finalConstraintKind)
                .Append("/finalScore=").Append(float.IsNegativeInfinity(finalConstraintScore) ? "-INF" : finalConstraintScore.ToString("F3"))
                .Append("/finalTestedMask=").Append(finalTestedCandidateMask)
                .Append("/finalWalkableMask=").Append(finalWalkableCandidateMask)
                .Append("/finalWalkable=").Append(finalOriginalWalkable);
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringSeparationDiagnostic(
                entity.GetHashCode(),
                out Vector3 rawAgentAvoidance,
                out Vector3 immediateSeparation,
                out Vector3 strongestImmediateSeparation,
                out int immediateOverlapCount,
                out float closestImmediateOverlapDistance,
                out float closestImmediateOverlapClearance,
                out int closeNeighborCount,
                out int skippedCloseNeighborCount,
                out float closestNeighborDistance,
                out float closestNeighborClearance,
                out float runtimeRadius,
                out float registeredRadius,
                out Vector3 runtimeObstacleAvoidance,
                out float maxSpeed))
        {
            builder.Append("/rawAgentAvoid=").Append(rawAgentAvoidance)
                .Append("/immediateSep=").Append(immediateSeparation)
                .Append("/strongestImmediateSep=").Append(strongestImmediateSeparation)
                .Append("/immediateOverlapCount=").Append(immediateOverlapCount)
                .Append("/closestImmediateDist=").Append(closestImmediateOverlapDistance == float.MaxValue ? "MAX" : closestImmediateOverlapDistance.ToString("F3"))
                .Append("/closestImmediateClearance=").Append(closestImmediateOverlapClearance.ToString("F3"))
                .Append("/closeNeighborCount=").Append(closeNeighborCount)
                .Append("/skippedCloseNeighborCount=").Append(skippedCloseNeighborCount)
                .Append("/closestNeighborDist=").Append(closestNeighborDistance == float.MaxValue ? "MAX" : closestNeighborDistance.ToString("F3"))
                .Append("/closestNeighborClearance=").Append(closestNeighborClearance.ToString("F3"))
                .Append("/runtimeRadius=").Append(runtimeRadius.ToString("F3"))
                .Append("/registeredRadius=").Append(registeredRadius.ToString("F3"))
                .Append("/runtimeObstacle=").Append(runtimeObstacleAvoidance)
                .Append("/maxSpeed=").Append(maxSpeed.ToString("F3"));
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastDynamicAvoidanceSkipDiagnostic(
                entity.GetHashCode(),
                out string skipReason,
                out int skippedOtherId,
                out bool skipSelfParticipates,
                out bool skipOtherCanUse,
                out bool skipOtherHasIntent,
                out string skipOtherMoveComp,
                out string skipOtherMoveMode))
        {
            builder.Append("/skipReason=").Append(skipReason)
                .Append("/skipOtherId=").Append(skippedOtherId)
                .Append("/skipSelfParticipates=").Append(skipSelfParticipates)
                .Append("/skipOtherCanUse=").Append(skipOtherCanUse)
                .Append("/skipOtherHasIntent=").Append(skipOtherHasIntent)
                .Append("/skipOtherMoveComp=").Append(skipOtherMoveComp)
                .Append("/skipOtherMoveMode=").Append(skipOtherMoveMode);
        }

        if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringResolution(
                entity.GetHashCode(),
                out string source,
                out Vector2 flow,
                out bool hasLineOfSight,
                out Vector3 tileTarget,
                out float integration,
                out Vector3 edgeNormal,
                out float edgeDistance))
        {
            builder.Append("/src=").Append(source)
                .Append("/flow=").Append(flow)
                .Append("/los=").Append(hasLineOfSight)
                .Append("/tileTarget=").Append(tileTarget)
                .Append("/integration=").Append(float.IsPositiveInfinity(integration) ? "INF" : integration.ToString("F3"))
                .Append("/edgeNormal=").Append(edgeNormal)
                .Append("/edgeDist=").Append(edgeDistance == float.MaxValue ? "MAX" : edgeDistance.ToString("F3"));
        }
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

    private static void AdvanceChasersWithNavigationConstraint(
        SimEntityContext[] chasers,
        Vector3 goal,
        int frame,
        float dt,
        float speed,
        float edgeBuffer,
        int width,
        int height,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            if (chaser.MoveComp == null)
            {
                SimMoveComp moveComp = new SimMoveComp();
                moveComp.Init(chaser);
                chaser.MoveComp = moveComp;
            }

            if (chaser.MoveExecutor == null)
                chaser.MoveExecutor = new SimMoveExecutor { Position = chaser.Position };

            chaser.SyncPositionToExecutor();

            if (chaser.Brain is SoldierAIBrain brain)
                brain.Tick(chaser, dt);

            chaser.MoveComp.Move(dt);
            chaser.MoveExecutor.Execute(dt);
            chaser.SyncPositionFromExecutor();

            if (FlowFieldCrowdMovementSystem.TryGetEditorTestLastSteeringDiagnostic(
                    chaser.GetHashCode(),
                    out Vector3 desiredVelocity,
                    out Vector3 baseVelocity,
                    out Vector3 agentAvoidance,
                    out Vector3 clampedAgentAvoidance,
                    out Vector3 boundaryAvoidance,
                    out Vector3 laneVelocity,
                    out Vector3 resultPreClamp,
                    out Vector3 result))
            {
                lastVelocity[i] = result;
                lastDesiredDisplacement[i] = result * dt;
                lastConstrainedDisplacement[i] = result * dt;
            }
            else
            {
                lastVelocity[i] = Vector3.zero;
                lastDesiredDisplacement[i] = Vector3.zero;
                lastConstrainedDisplacement[i] = Vector3.zero;
            }

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
        }
    }

    private static void AdvanceChasersThroughRuntimeMoveChain(
        SimEntityContext[] chasers,
        int frame,
        float dt,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            Assert.IsNotNull(chaser.MoveComp, $"真实 Lv3 回归必须预先挂 CharacterMoveComp。agent={i}");
            Assert.IsInstanceOf<CharacterMoveComp>(chaser.MoveComp, $"真实 Lv3 回归不能使用 SimMoveComp 简化链路。agent={i}");

            if (chaser.TargetComp is SimTargetingComp targeting)
                targeting.UpdateTargeting(dt);

            chaser.SyncPositionToExecutor();

            if (chaser.Brain is SoldierAIBrain brain)
                brain.Tick(chaser, dt);

            chaser.MoveComp.Move(dt);
            chaser.MoveExecutor.Execute(dt);
            chaser.SyncPositionFromExecutor();

            if (chaser.MoveExecutor is SimMoveExecutor executor)
            {
                lastVelocity[i] = executor.LastFrameVelocity;
                lastDesiredDisplacement[i] = executor.LastDesiredDisplacement;
                lastConstrainedDisplacement[i] = executor.LastConstrainedDisplacement;
            }
            else
            {
                lastVelocity[i] = Vector3.zero;
                lastDesiredDisplacement[i] = Vector3.zero;
                lastConstrainedDisplacement[i] = Vector3.zero;
            }

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
        }
    }

    private static Vector3 AdvanceHeroWithNavigationConstraint(
        Vector3 position,
        Vector3 waypoint,
        float speed,
        float dt,
        int agentTypeId,
        float edgeClearance,
        System.Text.StringBuilder diagnostics)
    {
        Vector3 toWaypoint = waypoint - position;
        toWaypoint.y = 0f;
        if (toWaypoint.sqrMagnitude <= 0.0001f)
            return position;

        Vector3 desired = toWaypoint.normalized * Mathf.Min(speed * dt, toWaypoint.magnitude);
        if (!FlowFieldCrowdMovementSystem.TryConstrainNavigationDisplacement(
                position,
                desired,
                agentTypeId,
                edgeClearance,
                out Vector3 constrained))
        {
            Vector3 target = position + desired;
            bool hasStartClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                position,
                edgeClearance,
                out bool startClear,
                out float startViolation,
                out int startBoxes,
                out int startCircles);
            bool hasTargetClearance = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationClearance(
                target,
                edgeClearance,
                out bool targetClear,
                out float targetViolation,
                out int targetBoxes,
                out int targetCircles);
            bool hasSegmentDiagnostics = FlowFieldCrowdMovementSystem.TryGetEditorTestNavigationSegmentDiagnostics(
                position,
                desired,
                agentTypeId,
                edgeClearance,
                out string segmentDiagnostics);
            diagnostics?.Append("[Lv3HeroRoute] navigation constraint failed pos=")
                .Append(position)
                .Append(" waypoint=").Append(waypoint)
                .Append(" desired=").Append(desired)
                .Append(" target=").Append(target)
                .Append(" startClear=").Append(hasStartClearance ? startClear.ToString() : "missing")
                .Append(" startViolation=").Append(hasStartClearance ? startViolation.ToString("F3") : "missing")
                .Append(" startBoxes=").Append(hasStartClearance ? startBoxes.ToString() : "missing")
                .Append(" startCircles=").Append(hasStartClearance ? startCircles.ToString() : "missing")
                .Append(" targetClear=").Append(hasTargetClearance ? targetClear.ToString() : "missing")
                .Append(" targetViolation=").Append(hasTargetClearance ? targetViolation.ToString("F3") : "missing")
                .Append(" targetBoxes=").Append(hasTargetClearance ? targetBoxes.ToString() : "missing")
                .Append(" targetCircles=").Append(hasTargetClearance ? targetCircles.ToString() : "missing")
                .Append(" segment=").Append(hasSegmentDiagnostics ? segmentDiagnostics : "missing")
                .AppendLine();
            return position;
        }

        return position + constrained;
    }

    private static void AdvanceChasersWithFlowSteering(
        SimEntityContext[] chasers,
        Vector3 goal,
        int frame,
        float dt,
        float speed,
        int[] consecutiveZeroFrames,
        int[] maxConsecutiveZeroFrames,
        Vector3[] lastVelocity,
        Vector3[] lastDesiredDisplacement,
        Vector3[] lastConstrainedDisplacement)
    {
        FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * dt);
        for (int i = 0; i < chasers.Length; i++)
        {
            SimEntityContext chaser = chasers[i];
            bool gotVelocity = FlowFieldCrowdMovementSystem.TryGetSteeringVelocity(chaser, goal, speed, out Vector3 velocity);
            Assert.IsTrue(gotVelocity, $"真实 Lv3 回归必须能取得流场速度。agent={i} pos={chaser.Position} goal={goal}");

            Vector3 displacement = velocity * dt;
            chaser.Position += displacement;
            chaser.SyncPositionToExecutor();

            lastVelocity[i] = velocity;
            lastDesiredDisplacement[i] = displacement;
            lastConstrainedDisplacement[i] = displacement;

            bool projectedToZero = lastDesiredDisplacement[i].sqrMagnitude > 0.02f * 0.02f
                                   && lastConstrainedDisplacement[i].sqrMagnitude <= 0.02f * 0.02f;
            consecutiveZeroFrames[i] = projectedToZero ? consecutiveZeroFrames[i] + 1 : 0;
            if (consecutiveZeroFrames[i] > maxConsecutiveZeroFrames[i])
                maxConsecutiveZeroFrames[i] = consecutiveZeroFrames[i];
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

    private static void ProcessFlowTileBuildQueueUntilTileCount(int minTileCount)
    {
        for (int frame = 2; frame < 128 && FlowFieldCrowdMovementSystem.GetEditorTestFlowTileCacheCount() < minTileCount; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
        }
    }

    private static void ProcessFlowTileBuildQueueUntilTileReady(int worldX, int worldY)
    {
        for (int frame = 2; frame < 256; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            if (FlowFieldCrowdMovementSystem.TryGetEditorTestCachedPortalTarget(worldX, worldY, out _, out _, out _, out _))
                continue;

            if (FlowFieldCrowdMovementSystem.GetEditorTestPendingFlowTileBuildCount() == 0)
                return;
        }
    }

    private static void ProcessFlowTileBuildQueueUntilPortalTargetReady(
        int worldX,
        int worldY,
        out Vector3 portalTarget,
        out int visibleCount,
        out int selectedPair,
        out bool usedOppositeCenter)
    {
        for (int frame = 2; frame < 128; frame++)
        {
            FlowFieldCrowdMovementSystem.SetEditorTestClock(frame, frame * 0.1f);
            FlowFieldCrowdMovementSystem.ProcessFlowTileBuildQueue();
            if (FlowFieldCrowdMovementSystem.TryGetEditorTestCachedPortalTarget(worldX, worldY, out portalTarget, out visibleCount, out selectedPair, out usedOppositeCenter))
                return;
        }

        Assert.Fail($"portal target was not built for cell=({worldX},{worldY})");
        portalTarget = Vector3.zero;
        visibleCount = 0;
        selectedPair = -1;
        usedOppositeCenter = false;
    }

    private static void ProcessWorldBuildQueueUntilReady()
    {
        for (int i = 0; i < 2048 && (!FlowFieldCrowdMovementSystem.HasEditorTestWorld() || FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild()); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
    }

    private static void ProcessAllWorldBuildQueuesUntilReady()
    {
        FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();
        for (int i = 0; i < 4096 && FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(); i++)
            FlowFieldCrowdMovementSystem.ProcessWorldBuildQueue();

        Assert.IsFalse(
            FlowFieldCrowdMovementSystem.HasEditorTestPendingWorldBuild(),
            "多导航源测试必须在进入运行模拟前完成全部 world build。");
    }

    private static SimEntityContext CreateEntity(Vector3 position)
    {
        return CreateEntity(position, false);
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader)
    {
        return CreateEntity(position, isLeader, 0);
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader, int agentTypeId)
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
        FlowFieldCrowdMovementSystem.RegisterAgentForEditorTest(ctx, isLeader, 0.5f, agentTypeId);
        return ctx;
    }

    private static SimEntityContext CreateEntity(Vector3 position, bool isLeader, int agentTypeId, float radius)
    {
        SimEntityContext ctx = CreateEntity(position, isLeader, agentTypeId);
        ctx.SetProperty(CreatureMainProperty.CollisionRadius, (Fix64)(radius / DistanceUnitConverter.DefaultDistanceConversionRate));
        FlowFieldCrowdMovementSystem.RegisterAgentForEditorTest(ctx, isLeader, radius, agentTypeId);
        return ctx;
    }

    private static FlowFieldNavigationConfig CreateConfig()
    {
        FlowFieldNavigationConfig config = ScriptableObject.CreateInstance<FlowFieldNavigationConfig>();
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
