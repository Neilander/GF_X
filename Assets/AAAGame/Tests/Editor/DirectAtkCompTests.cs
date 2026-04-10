using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DirectAtkCompTests
{
    private const int PlayerTeamId = 0;
    private const int EnemyTeamId = 1;

    private SimEntityContext CreateUnit(Vector3 pos, int teamId, float hp = 100f)
    {
        var ctx = new SimEntityContext
        {
            Position = pos,
            TeamId = teamId
        };
        ctx.Health.Init(hp);
        var executor = new SimMoveExecutor { Position = pos };
        ctx.MoveExecutor = executor;
        return ctx;
    }

    private WeaponData MeleeWeapon(float damage = 10f, float range = 150f,
        float windUp = 0.2f, float windDown = 0.2f, float interval = 1f)
    {
        return new WeaponData
        {
            Damage = damage,
            AttackRange = range,
            Type = WeaponType.Melee,
            WindUp = windUp,
            WindDown = windDown,
            AttackInterval = interval
        };
    }

    [Test]
    public void 目标在范围内时发起攻击()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(1, 0, 0), EnemyTeamId);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        var brain = new ScriptedBrain { Attack = true };
        attacker.Brain = brain;

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon();
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        // 覆盖 Init 内部创建的默认武器数据
        attacker.WeaponComp = new WeaponComp(weapon);

        // 先让 targeting 找到目标
        targeting.UpdateTargeting(1f);
        Assert.IsNotNull(targeting.CurrentTarget, "应该找到目标");

        // 发起攻击
        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "应进入前摇");
        Assert.AreEqual(1, atkComp.AttackCount);
    }

    [Test]
    public void 前摇结束后造成伤害()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(1, 0, 0), EnemyTeamId);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 30f, windUp: 0.2f, windDown: 0.2f);
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        targeting.UpdateTargeting(1f);
        atkComp.Attack(0.01f); // 进入 WindUp

        // 推进到前摇结束
        atkComp.Attack(0.2f);

        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State, "前摇结束应进入后摇");
        Assert.AreEqual(70f, target.Health.currentHealth, 0.01f, "目标应受到30点伤害");
    }

    [Test]
    public void 攻击全流程_前摇_伤害_后摇_冷却()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(1, 0, 0), EnemyTeamId);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        // interval=1s, windUp=0.3, windDown=0.3, cooldown=0.4
        var weapon = MeleeWeapon(damage: 20f, windUp: 0.3f, windDown: 0.3f, interval: 1f);
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        targeting.UpdateTargeting(1f);

        // 第一次攻击
        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.IsFalse(attacker.CanRun(moveComp), "前摇时移动应被锁定");

        // 前摇结束
        atkComp.Attack(0.3f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State);
        Assert.AreEqual(80f, target.Health.currentHealth, 0.01f);

        // 后摇结束
        atkComp.Attack(0.3f);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp), "后摇结束后移动应恢复");

        // 冷却中不能再次攻击
        atkComp.Attack(0.2f);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);

        // 冷却结束
        atkComp.Attack(0.3f);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State);

        // 可以发起第二次攻击
        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.AreEqual(2, atkComp.AttackCount);
    }

    [Test]
    public void 目标超出范围时不攻击()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(10, 0, 0), EnemyTeamId); // 10m 远

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 20f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        // 攻击范围 150码 = 1.5m，目标在 10m 外
        var weapon = MeleeWeapon(range: 150f);
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        targeting.UpdateTargeting(1f);
        Assert.IsNotNull(targeting.CurrentTarget);

        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State, "目标超出攻击范围不应攻击");
    }

    [Test]
    public void 目标死亡后不造成伤害()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(1, 0, 0), EnemyTeamId, hp: 5f);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 10f, windUp: 0.1f);
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        targeting.UpdateTargeting(1f);

        // 第一次攻击，杀死目标
        atkComp.Attack(0.01f); // WindUp
        atkComp.Attack(0.1f);  // 造成伤害 → hp: 5 - 10 = 0 → 死亡

        Assert.AreEqual(0f, target.Health.currentHealth, 0.01f);
        Assert.IsFalse(target.Alive, "目标应该死亡");
    }

    [Test]
    public void 连续攻击多次击杀目标()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(1, 0, 0), EnemyTeamId, hp: 50f);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        // 20伤害，间隔0.5s
        var weapon = MeleeWeapon(damage: 20f, windUp: 0.1f, windDown: 0.1f, interval: 0.5f);
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        // 模拟 5 秒战斗
        float dt = 1f / 60f;
        for (int i = 0; i < 300; i++)
        {
            targeting.UpdateTargeting(dt);
            atkComp.Attack(dt);
        }

        Assert.GreaterOrEqual(atkComp.AttackCount, 3, "应至少攻击3次");
        Assert.IsFalse(target.Alive, "50血被20伤害攻击3次应该死亡");
    }

    [Test]
    public void 远程武器范围内可以攻击()
    {
        var attacker = CreateUnit(Vector3.zero, PlayerTeamId);
        var target = CreateUnit(new Vector3(5, 0, 0), EnemyTeamId); // 5m 远

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        // 700码 = 7m 射程
        var weapon = new WeaponData
        {
            Damage = 11f,
            AttackRange = 700f,
            Type = WeaponType.Projectile,
            WindUp = 0.25f,
            WindDown = 0.4f,
            AttackInterval = 1f
        };
        var atkComp = new DirectAtkComp(WeaponType.Melee);
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;
        attacker.WeaponComp = new WeaponComp(weapon);

        targeting.UpdateTargeting(1f);

        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "远程单位应能在射程内攻击");

        atkComp.Attack(0.25f);
        Assert.AreEqual(89f, target.Health.currentHealth, 0.01f, "应造成11点伤害");
    }

    // 转向功能已移除（不需要小兵在攻击时旋转），此测试已废弃
}
