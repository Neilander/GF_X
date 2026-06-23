using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DirectAtkCompTests
{
    private const float AttackStepEpsilon = 0.02f;

    [SetUp]
    public void SetUp()
    {
        SetupCombatPhaseForTests();
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

    private SimEntityContext CreateUnit(Vector3 pos, SideType side, float hp = 100f)
    {
        var ctx = new SimEntityContext
        {
            Position = pos,
            Side = side
        };
        ctx.Health.Init((Fix64)hp);
        var executor = new SimMoveExecutor { Position = pos };
        ctx.MoveExecutor = executor;
        return ctx;
    }

    private WeaponData MeleeWeapon(float damage = 10f, float range = 150f,
        float windUp = 0.2f, float windDown = 0.2f, float interval = 1f)
    {
        return new WeaponData(
            WeaponType.Melee,
            (Fix64)damage,
            (Fix64)interval,
            (Fix64)range,
            Fix64.Zero,
            (Fix64)windUp,
            (Fix64)windDown,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            new Fix64[0]);
    }

    [Test]
    public void 目标在范围内时发起攻击()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

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
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

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
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 30f, windUp: 0.2f, windDown: 0.2f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(1f);
        atkComp.Attack(0.01f); // 进入 WindUp

        // 推进到前摇结束
        atkComp.Attack(0.2f + AttackStepEpsilon);

        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State, "前摇结束应进入后摇");
        Assert.AreEqual(70f, (float)target.Health.currentHealth, 0.01f, "目标应受到30点伤害");
    }

    [Test]
    public void 攻击全流程_前摇_伤害_后摇_冷却()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

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
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(1f);

        // 第一次攻击
        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.IsFalse(attacker.CanRun(moveComp), "前摇时移动应被锁定");

        // 前摇结束
        atkComp.Attack(0.3f + AttackStepEpsilon);
        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State);
        Assert.AreEqual(80f, (float)target.Health.currentHealth, 0.01f);

        // 后摇结束
        atkComp.Attack(0.3f + AttackStepEpsilon);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp), "后摇结束后移动应恢复");

        // 冷却中不能再次攻击
        atkComp.Attack(0.2f);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);

        // 冷却结束
        atkComp.Attack(0.3f + AttackStepEpsilon);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State);

        // 可以发起第二次攻击
        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.AreEqual(2, atkComp.AttackCount);
    }

    [Test]
    public void 目标超出范围时不攻击()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(10, 0, 0), SideType.EnemySide); // 10m 远

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
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(1f);
        Assert.IsNotNull(targeting.CurrentTarget);

        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State, "目标超出攻击范围不应攻击");
    }

    [Test]
    public void 目标死亡后不造成伤害()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide, hp: 5f);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 10f, windUp: 0.1f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(1f);

        // 第一次攻击，杀死目标
        atkComp.Attack(0.01f); // WindUp
        atkComp.Attack(0.1f + AttackStepEpsilon);  // 造成伤害 → hp: 5 - 10 = 0 → 死亡

        Assert.AreEqual(0f, (float)target.Health.currentHealth, 0.01f);
        Assert.IsFalse(target.Alive, "目标应该死亡");
    }

    [Test]
    public void 连续攻击多次击杀目标()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide, hp: 50f);

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
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

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
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(5, 0, 0), SideType.EnemySide); // 5m 远

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRange = 10f };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;

        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        // 700码 = 7m 射程
        var weapon = new WeaponData(
            WeaponType.Projectile,
            (Fix64)11f,
            (Fix64)1f,
            (Fix64)700f,
            Fix64.Zero,
            (Fix64)0.25f,
            (Fix64)0.4f,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            new Fix64[0]);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(1f);

        atkComp.Attack(0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "远程单位应能在射程内攻击");
        Assert.AreEqual(1, atkComp.AttackCount);
    }

    // 转向功能已移除（不需要小兵在攻击时旋转），此测试已废弃
}
