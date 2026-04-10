using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GroupMoveCoordinator 纯逻辑测试。
/// 验证 ORCA 协调器的注册/注销、安全速度计算、障碍物规避。
/// </summary>
[TestFixture]
public class GroupMoveCoordinatorTests
{
    private const int PlayerTeamId = 0;

    private GroupMoveCoordinator _coord;

    [SetUp]
    public void SetUp()
    {
        _coord = new GroupMoveCoordinator();
    }

    #region 注册/注销

    [Test]
    public void 注册Agent后可查询()
    {
        _coord.RegisterAgent(1, Vector3.zero, PlayerTeamId);
        Assert.IsTrue(_coord.HasAgent(1));
        Assert.AreEqual(1, _coord.AgentCount);
    }

    [Test]
    public void 注销Agent后不可查询()
    {
        _coord.RegisterAgent(1, Vector3.zero, PlayerTeamId);
        _coord.UnregisterAgent(1);
        Assert.IsFalse(_coord.HasAgent(1));
        Assert.AreEqual(0, _coord.AgentCount);
    }

    [Test]
    public void 注册Obstacle后可查询()
    {
        _coord.RegisterObstacle(100, new Vector3(5, 0, 0), 2f);
        Assert.IsTrue(_coord.HasObstacle(100));
        Assert.AreEqual(1, _coord.ObstacleCount);
    }

    [Test]
    public void 注销Obstacle后不可查询()
    {
        _coord.RegisterObstacle(100, new Vector3(5, 0, 0), 2f);
        _coord.UnregisterObstacle(100);
        Assert.IsFalse(_coord.HasObstacle(100));
    }

    #endregion

    #region 基本求解

    [Test]
    public void 单个Agent无邻居_安全速度等于期望速度()
    {
        _coord.RegisterAgent(1, Vector3.zero, PlayerTeamId);

        Vector3 result = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => result = v);
        _coord.Resolve();

        Assert.AreEqual(1f, result.x, 0.01f, "无邻居时安全速度应等于期望速度");
        Assert.AreEqual(0f, result.z, 0.01f);
    }

    [Test]
    public void Resolve后请求被清空()
    {
        _coord.RegisterAgent(1, Vector3.zero, PlayerTeamId);
        _coord.SubmitDesiredVelocity(1, Vector3.right, v => { });
        Assert.AreEqual(1, _coord.PendingRequestCount);

        _coord.Resolve();
        Assert.AreEqual(0, _coord.PendingRequestCount);
    }

    [Test]
    public void 未注册Agent提交请求_回调不触发()
    {
        bool called = false;
        _coord.SubmitDesiredVelocity(999, Vector3.right, v => called = true);
        _coord.Resolve();

        Assert.IsFalse(called, "未注册的 agent 不应触发回调");
    }

    #endregion

    #region Agent 之间避让

    [Test]
    public void 两个Agent面对面_安全速度偏离碰撞方向()
    {
        // Agent1 在左边，朝右走；Agent2 在右边，朝左走
        _coord.RegisterAgent(1, new Vector3(0, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(2, new Vector3(2, 0, 0), PlayerTeamId, false, 0.5f);

        Vector3 safe1 = Vector3.zero;
        Vector3 safe2 = Vector3.zero;

        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safe1 = v);
        _coord.SubmitDesiredVelocity(2, new Vector3(-1, 0, 0), v => safe2 = v);
        _coord.Resolve();

        // 两个 agent 的安全速度应该有 Z 分量偏移（互相避让）
        // 或者 X 分量减小（减速）
        // 核心断言：不应该保持原方向不变
        bool agent1Adjusted = Mathf.Abs(safe1.z) > 0.01f || safe1.x < 0.95f;
        bool agent2Adjusted = Mathf.Abs(safe2.z) > 0.01f || safe2.x > -0.95f;

        Assert.IsTrue(agent1Adjusted || agent2Adjusted,
            $"面对面的 agent 应调整速度。safe1={safe1}, safe2={safe2}");
    }

    [Test]
    public void 多Agent同目标_不会完全重叠()
    {
        // 5 个 agent 从不同位置朝同一点移动
        _coord.RegisterAgent(1, new Vector3(-2, 0, -1), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(2, new Vector3(-2, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(3, new Vector3(-2, 0, 1), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(4, new Vector3(-1, 0, -0.5f), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(5, new Vector3(-1, 0, 0.5f), PlayerTeamId, false, 0.5f);

        Vector3 target = new Vector3(5, 0, 0);
        var safeVelocities = new Dictionary<int, Vector3>();

        for (int id = 1; id <= 5; id++)
        {
            int capturedId = id;
            Vector3 desiredDir = (target - _coord_GetAgentPos(id)).normalized;
            _coord.SubmitDesiredVelocity(id, desiredDir, v => safeVelocities[capturedId] = v);
        }
        _coord.Resolve();

        // 安全速度不应完全相同（至少有一些差异）
        bool allSame = true;
        Vector3 first = safeVelocities[1];
        for (int id = 2; id <= 5; id++)
        {
            if (Vector3.Distance(safeVelocities[id], first) > 0.05f)
            {
                allSame = false;
                break;
            }
        }

        Assert.IsFalse(allSame, "5个 agent 朝同一目标走，安全速度不应完全相同");
    }

    // 辅助方法：获取 agent 位置（用于测试）
    private Vector3 _coord_GetAgentPos(int id)
    {
        // 硬编码位置匹配上面的注册
        switch (id)
        {
            case 1: return new Vector3(-2, 0, -1);
            case 2: return new Vector3(-2, 0, 0);
            case 3: return new Vector3(-2, 0, 1);
            case 4: return new Vector3(-1, 0, -0.5f);
            case 5: return new Vector3(-1, 0, 0.5f);
            default: return Vector3.zero;
        }
    }

    #endregion

    #region 障碍物规避

    [Test]
    public void Agent朝障碍物走_安全速度偏离障碍物()
    {
        // Agent 在左边朝右走，障碍物在右边
        _coord.RegisterAgent(1, new Vector3(0, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterObstacle(100, new Vector3(3, 0, 0), 1f);

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        // 安全速度应该有切线分量（Z 偏移）或 X 分量减小
        bool adjusted = Mathf.Abs(safeVel.z) > 0.01f || safeVel.x < 0.95f;
        Assert.IsTrue(adjusted,
            $"朝障碍物走应调整速度。safeVel={safeVel}");
    }

    [Test]
    public void Agent已在障碍物内_被强力推出()
    {
        // Agent 和障碍物重叠
        _coord.RegisterAgent(1, new Vector3(0.5f, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterObstacle(100, new Vector3(0, 0, 0), 1f);

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, Vector3.zero, v => safeVel = v);
        _coord.Resolve();

        // 应该有强力推出（正X方向，远离障碍物中心）
        Assert.Greater(safeVel.x, 0.5f,
            $"在障碍物内应被强力推出。safeVel={safeVel}");
    }

    [Test]
    public void Agent平行于障碍物走_不受影响()
    {
        // Agent 在障碍物旁边，但朝平行方向走（不朝障碍物）
        _coord.RegisterAgent(1, new Vector3(0, 0, 2), PlayerTeamId, false, 0.5f);
        _coord.RegisterObstacle(100, new Vector3(0, 0, 0), 1f);

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        // 平行走应该基本不受影响
        Assert.AreEqual(1f, safeVel.x, 0.2f,
            $"平行于障碍物走应基本不变。safeVel={safeVel}");
    }

    [Test]
    public void 障碍物不影响远处Agent()
    {
        _coord.RegisterAgent(1, new Vector3(0, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterObstacle(100, new Vector3(20, 0, 0), 1f);

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        Assert.AreEqual(1f, safeVel.x, 0.01f, "远处障碍物不应影响安全速度");
        Assert.AreEqual(0f, safeVel.z, 0.01f);
    }

    #endregion

    #region AABB 方形障碍物

    [Test]
    public void 方形障碍物_Agent正面冲过来被偏转()
    {
        // 方形障碍物在原点，2x2（halfExtents = 1,0,1）
        // Agent 从左边朝右走
        _coord.RegisterAgent(1, new Vector3(-3, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, Vector3.zero, new Vector3(1, 0, 1));

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        // 应该有 Z 偏转或 X 减小
        bool adjusted = Mathf.Abs(safeVel.z) > 0.01f || safeVel.x < 0.95f;
        Assert.IsTrue(adjusted,
            $"朝方形障碍物正面冲应偏转。safeVel={safeVel}");
    }

    [Test]
    public void 方形障碍物_Agent在内部被强力推出()
    {
        // Agent 在方形障碍物内部
        _coord.RegisterAgent(1, new Vector3(0.3f, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, Vector3.zero, new Vector3(1, 0, 1));

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, Vector3.zero, v => safeVel = v);
        _coord.Resolve();

        Assert.Greater(safeVel.magnitude, 0.5f,
            $"在方形障碍物内应被强力推出。safeVel={safeVel}");
    }

    [Test]
    public void 方形障碍物_Agent沿长墙平行走不受影响()
    {
        // 长墙：X 方向很长（halfExtents.x = 5），Z 方向薄（halfExtents.z = 0.5）
        // Agent 在墙的上方，沿 X 方向走
        _coord.RegisterAgent(1, new Vector3(0, 0, 3), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, Vector3.zero, new Vector3(5, 0, 0.5f));

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        Assert.AreEqual(1f, safeVel.x, 0.2f,
            $"沿长墙平行走应基本不变。safeVel={safeVel}");
    }

    [Test]
    public void 方形障碍物_Agent从角落接近被偏转()
    {
        // 方形障碍物 2x2，Agent 从对角线方向接近
        _coord.RegisterAgent(1, new Vector3(-3, 0, -3), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, Vector3.zero, new Vector3(1, 0, 1));

        Vector3 desiredDir = new Vector3(1, 0, 1).normalized;
        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, desiredDir, v => safeVel = v);
        _coord.Resolve();

        // 对角线接近应有某种偏转
        bool adjusted = Vector3.Distance(safeVel, desiredDir) > 0.01f;
        Assert.IsTrue(adjusted,
            $"从角落接近方形障碍物应偏转。desired={desiredDir}, safeVel={safeVel}");
    }

    [Test]
    public void 模拟多帧_Agent不会穿过方形障碍物()
    {
        // Agent 从左边朝右直冲，中间有一面墙
        _coord.RegisterAgent(1, new Vector3(-5, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, Vector3.zero, new Vector3(0.5f, 0, 2)); // 薄墙，Z 方向长

        Vector3 agentPos = new Vector3(-5, 0, 0);
        float speed = 5f;
        float dt = 1f / 60f;

        for (int frame = 0; frame < 300; frame++)
        {
            _coord.UpdateAgentPosition(1, agentPos);

            Vector3 desiredDir = (new Vector3(5, 0, 0) - agentPos).normalized;
            Vector3 safeVel = Vector3.zero;
            _coord.SubmitDesiredVelocity(1, desiredDir * speed, v => safeVel = v);
            _coord.Resolve();

            agentPos += safeVel * dt;
        }

        // Agent 应绕过墙到达右边，或被挡住，但不应在墙内
        var obs = new GroupMoveCoordinator.ObstacleData
        {
            Position = Vector3.zero,
            HalfExtents = new Vector3(0.5f, 0, 2)
        };
        float surfaceDist = obs.DistanceToSurface(agentPos);
        Assert.Greater(surfaceDist, -0.1f,
            $"Agent 不应深入方形障碍物内部。最终位置={agentPos}, surfaceDist={surfaceDist}");
    }

    [Test]
    public void 方形障碍物_远处不影响()
    {
        _coord.RegisterAgent(1, new Vector3(0, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterBoxObstacle(100, new Vector3(20, 0, 0), new Vector3(1, 0, 1));

        Vector3 safeVel = Vector3.zero;
        _coord.SubmitDesiredVelocity(1, new Vector3(1, 0, 0), v => safeVel = v);
        _coord.Resolve();

        Assert.AreEqual(1f, safeVel.x, 0.01f, "远处方形障碍物不应影响");
        Assert.AreEqual(0f, safeVel.z, 0.01f);
    }

    #endregion

    #region 多帧模拟

    [Test]
    public void 模拟多帧_Agent不会穿过障碍物()
    {
        // Agent 朝障碍物直冲，模拟 100 帧，不应穿过
        _coord.RegisterAgent(1, new Vector3(-5, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterObstacle(100, new Vector3(0, 0, 0), 1f);

        Vector3 agentPos = new Vector3(-5, 0, 0);
        float speed = 5f;
        float dt = 1f / 60f;

        for (int frame = 0; frame < 300; frame++) // 5 秒
        {
            _coord.UpdateAgentPosition(1, agentPos);

            Vector3 desiredDir = (new Vector3(5, 0, 0) - agentPos).normalized;
            Vector3 safeVel = Vector3.zero;
            _coord.SubmitDesiredVelocity(1, desiredDir * speed, v => safeVel = v);
            _coord.Resolve();

            agentPos += safeVel * dt;
        }

        // Agent 不应到达障碍物中心（0,0,0），应该绕开
        float distToObstacle = Vector3.Distance(agentPos, Vector3.zero);
        Assert.Greater(distToObstacle, 1.0f,
            $"Agent 不应穿过障碍物。最终位置={agentPos}, 距障碍物={distToObstacle}");
    }

    [Test]
    public void 模拟多帧_两Agent不会重叠()
    {
        _coord.RegisterAgent(1, new Vector3(-3, 0, 0), PlayerTeamId, false, 0.5f);
        _coord.RegisterAgent(2, new Vector3(3, 0, 0), PlayerTeamId, false, 0.5f);

        Vector3 pos1 = new Vector3(-3, 0, 0);
        Vector3 pos2 = new Vector3(3, 0, 0);
        float speed = 3f;
        float dt = 1f / 60f;

        float minDist = float.MaxValue;

        for (int frame = 0; frame < 300; frame++)
        {
            _coord.UpdateAgentPosition(1, pos1);
            _coord.UpdateAgentPosition(2, pos2);

            // 都朝原点走
            Vector3 desired1 = (Vector3.zero - pos1).normalized * speed;
            Vector3 desired2 = (Vector3.zero - pos2).normalized * speed;

            Vector3 safe1 = Vector3.zero;
            Vector3 safe2 = Vector3.zero;

            _coord.SubmitDesiredVelocity(1, desired1, v => safe1 = v);
            _coord.SubmitDesiredVelocity(2, desired2, v => safe2 = v);
            _coord.Resolve();

            pos1 += safe1 * dt;
            pos2 += safe2 * dt;

            float dist = Vector3.Distance(pos1, pos2);
            if (dist < minDist) minDist = dist;
        }

        Assert.Greater(minDist, 0.5f,
            $"两个 agent 最小距离={minDist:F2}，不应重叠（combined radius=1.0）");
    }

    #endregion

    #region 复杂场景压力测试

    /// <summary>
    /// 5 种不同长宽的方形障碍物 + 10 个单位从左侧到右侧，跑 5 次随机种子。
    /// 断言：无单位深入障碍物内部，且无两单位严重重叠。
    /// </summary>
    [Test]
    public void 复杂场景_10单位穿越5个方形障碍物_跑5次()
    {
        int[] seeds = { 42, 123, 777, 2024, 9999 };

        foreach (int seed in seeds)
        {
            var coord = new GroupMoveCoordinator();
            Random.InitState(seed);

            // 5 个不同形状的方形障碍物，散布在中间区域
            var obstacles = new (Vector3 center, Vector3 halfExt)[]
            {
                (new Vector3(0, 0, 0), new Vector3(0.5f, 0, 2f)),      // 薄竖墙
                (new Vector3(3, 0, 3), new Vector3(2f, 0, 0.3f)),      // 宽横墙
                (new Vector3(-2, 0, -2), new Vector3(1f, 0, 1f)),      // 方块
                (new Vector3(5, 0, -1), new Vector3(0.3f, 0, 3f)),     // 窄长竖墙
                (new Vector3(-4, 0, 2), new Vector3(1.5f, 0, 0.5f)),   // 中横墙
            };

            for (int i = 0; i < obstacles.Length; i++)
            {
                coord.RegisterBoxObstacle(100 + i, obstacles[i].center, obstacles[i].halfExt);
            }

            // 10 个单位，随机生成在左侧（x = -8 ~ -6），Z 随机
            int agentCount = 10;
            Vector3[] positions = new Vector3[agentCount];
            Vector3[] targets = new Vector3[agentCount];

            for (int i = 0; i < agentCount; i++)
            {
                positions[i] = new Vector3(
                    Random.Range(-8f, -6f),
                    0f,
                    Random.Range(-4f, 4f)
                );
                // 目标在右侧
                targets[i] = new Vector3(
                    Random.Range(8f, 10f),
                    0f,
                    Random.Range(-4f, 4f)
                );
                coord.RegisterAgent(i, positions[i], PlayerTeamId, false, 0.5f);
            }

            // 模拟 600 帧（10 秒）
            float speed = 4f;
            float dt = 1f / 60f;

            for (int frame = 0; frame < 600; frame++)
            {
                for (int i = 0; i < agentCount; i++)
                {
                    coord.UpdateAgentPosition(i, positions[i]);
                }

                Vector3[] safeVels = new Vector3[agentCount];
                for (int i = 0; i < agentCount; i++)
                {
                    int idx = i;
                    Vector3 desiredDir = (targets[i] - positions[i]).normalized;
                    coord.SubmitDesiredVelocity(i, desiredDir * speed, v => safeVels[idx] = v);
                }
                coord.Resolve();

                for (int i = 0; i < agentCount; i++)
                {
                    positions[i] += safeVels[i] * dt;
                }
            }

            // 断言 1：无单位深入任何障碍物内部（允许 0.3 容差，因为是帧级别离散）
            for (int i = 0; i < agentCount; i++)
            {
                for (int j = 0; j < obstacles.Length; j++)
                {
                    var obsData = new GroupMoveCoordinator.ObstacleData
                    {
                        Position = obstacles[j].center,
                        HalfExtents = obstacles[j].halfExt
                    };
                    float surfDist = obsData.DistanceToSurface(positions[i]);
                    Assert.Greater(surfDist, -0.3f,
                        $"[seed={seed}] Agent {i} 深入障碍物 {j} 内部。pos={positions[i]}, surfDist={surfDist:F2}");
                }
            }

            // 断言 2：无两单位严重重叠（combined radius = 1.0，允许 0.3 容差）
            for (int i = 0; i < agentCount; i++)
            {
                for (int j = i + 1; j < agentCount; j++)
                {
                    float dist = Vector3.Distance(positions[i], positions[j]);
                    Assert.Greater(dist, 0.3f,
                        $"[seed={seed}] Agent {i} 和 {j} 严重重叠。dist={dist:F2}, pos_i={positions[i]}, pos_j={positions[j]}");
                }
            }
        }
    }

    #endregion
}
