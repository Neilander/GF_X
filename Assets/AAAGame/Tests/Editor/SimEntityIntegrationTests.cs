using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 集成测试：多个 Sim 组件协同工作，模拟真实游戏场景。
/// </summary>
public class SimEntityIntegrationTests
{
    private const int PlayerTeamId = 0;
    private const int EnemyTeamId = 1;

    private SimEntityContext CreateUnit(Vector3 pos, int teamId)
    {
        var ctx = new SimEntityContext
        {
            Position = pos,
            TeamId = teamId
        };
        var executor = new SimMoveExecutor { Position = pos };
        ctx.MoveExecutor = executor;
        return ctx;
    }

    private void TickEntity(SimEntityContext ctx, float dt)
    {
        ctx.SyncPositionToExecutor();

        if (ctx.Brain is ITickBrain tickBrain)
            tickBrain.Tick(ctx, dt);

        if (ctx.CanRun(ctx.TargetComp))
            ctx.TargetComp?.UpdateTargeting(dt);

        if (ctx.CanRun(ctx.MoveComp))
            ctx.MoveComp?.Move(dt);

        if (ctx.CanRun(ctx.AtkComp))
            ctx.AtkComp?.Attack(dt);

        ctx.MoveExecutor.Execute(dt);
        ctx.SyncPositionFromExecutor();
    }

    [Test]
    public void 单位用ScriptedBrain向前移动()
    {
        var ctx = CreateUnit(Vector3.zero, PlayerTeamId);

        var brain = new ScriptedBrain { Move = new Vector2(0, 1) }; // 向前
        ctx.Brain = brain;

        var moveComp = new SimMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        // 模拟 60 帧 (1秒)
        for (int i = 0; i < 60; i++)
            TickEntity(ctx, 1f / 60f);

        Assert.Greater(ctx.Position.z, 0f, "单位应该向前移动了");
    }

    [Test]
    public void 单位MoveTo走向目标点()
    {
        var ctx = CreateUnit(Vector3.zero, PlayerTeamId);

        var moveComp = new SimMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        moveComp.MoveTo(new Vector3(10, 0, 0));

        for (int i = 0; i < 1500; i++)
            TickEntity(ctx, 1f / 60f);

        float dist = Vector3.Distance(ctx.Position, new Vector3(10, 0, 0));
        Assert.Less(dist, 1f, "单位应该接近目标点");
    }

    [Test]
    public void 敌人发现玩家后追击()
    {
        var player = CreateUnit(new Vector3(0, 0, 0), PlayerTeamId);
        var enemy = CreateUnit(new Vector3(3, 0, 0), EnemyTeamId);

        var allEntities = new List<IEntityContext> { player, enemy };

        // 给敌人装上 Sim 组件
        var targeting = new SimTargetingComp(enemy, allEntities) { AggroRange = 10f };
        targeting.Init(enemy);
        enemy.TargetComp = targeting;

        var moveComp = new SimMoveComp();
        moveComp.Init(enemy);
        enemy.MoveComp = moveComp;

        var brain = new ScriptedBrain();
        enemy.Brain = brain;

        float initialDist = Vector3.Distance(player.Position, enemy.Position);

        // 模拟：手动让敌人追击
        for (int i = 0; i < 120; i++)
        {
            targeting.UpdateTargeting(1f / 60f);

            // 如果发现目标，向目标移动
            if (targeting.CurrentTarget != null)
            {
                moveComp.MoveTo(targeting.CurrentTarget.Position);
            }

            TickEntity(enemy, 1f / 60f);
        }

        float finalDist = Vector3.Distance(player.Position, enemy.Position);
        Assert.Less(finalDist, initialDist, "敌人应该更靠近玩家了");
    }

    [Test]
    public void 攻击时移动被锁定()
    {
        var ctx = CreateUnit(Vector3.zero, PlayerTeamId);

        var brain = new ScriptedBrain();
        ctx.Brain = brain;

        var moveComp = new SimMoveComp();
        moveComp.Init(ctx);
        ctx.MoveComp = moveComp;

        var atkComp = new SimAtkComp { AttackDuration = 0.5f };
        atkComp.Init(ctx);
        ctx.AtkComp = atkComp;

        // 同时移动和攻击
        brain.Move = new Vector2(0, 1);
        brain.Attack = true;

        TickEntity(ctx, 1f / 60f);

        // 攻击后，移动应被锁定
        Assert.IsTrue(atkComp.IsAttacking);
        Assert.IsFalse(ctx.CanRun(moveComp), "攻击时移动应被锁定");

        // 攻击结束后恢复
        brain.Attack = false;
        for (int i = 0; i < 60; i++) // 跑完攻击持续时间
            TickEntity(ctx, 1f / 60f);

        Assert.IsFalse(atkComp.IsAttacking);
        Assert.IsTrue(ctx.CanRun(moveComp), "攻击结束后移动应恢复");
    }
}
