using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DirectAtkCompTests
{
    [SetUp]
    public void SetUp()
    {
        SetupCombatPhaseForTests();
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
        LogicFrameRuntime.Begin();
        LogicFrameRuntime.StartTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicFrameRuntime.IsActive)
            LogicFrameRuntime.End();
    }

    private static void StartAttack(DirectAtkComp atkComp)
    {
atkComp.Attack(Fix64.Zero);
    }

    private static void AdvanceFrames(DirectAtkComp atkComp, int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            LogicFrameRuntime.Tick(LogicFrameRuntime.CurrentFrame + 1);
atkComp.Attack(Fix64.Zero);
        }
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
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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
        targeting.UpdateTargeting(Fix64.One);
        Assert.IsNotNull(targeting.CurrentTarget, "应该找到目标");

        // 发起攻击
        StartAttack(atkComp);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "应进入前摇");
        Assert.AreEqual(1, atkComp.AttackCount);
    }

    [Test]
    public void 前摇结束后造成伤害()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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

        targeting.UpdateTargeting(Fix64.One);
        StartAttack(atkComp);

atkComp.Attack((Fix64)999);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "同一逻辑帧内 deltaTime 再大也不能推进前摇");
        Assert.AreEqual(100f, (float)target.Health.currentHealth, 0.01f);

        AdvanceFrames(atkComp, 6);

        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State, "前摇结束应进入后摇");
        Assert.AreEqual(70f, (float)target.Health.currentHealth, 0.01f, "目标应受到30点伤害");
    }

    [Test]
    public void 攻击全流程_前摇_伤害_后摇_冷却()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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

        targeting.UpdateTargeting(Fix64.One);

        // 第一次攻击
        StartAttack(atkComp);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.IsFalse(attacker.CanRun(moveComp), "前摇时移动应被锁定");

        // 0.3 秒按 30Hz 内容规则映射为 9 Tick。
        AdvanceFrames(atkComp, 9);
        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State);
        Assert.AreEqual(80f, (float)target.Health.currentHealth, 0.01f);

        AdvanceFrames(atkComp, 9);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp), "后摇结束后移动应恢复");

        AdvanceFrames(atkComp, 11);
        Assert.AreEqual(DirectAtkComp.AtkState.Cooldown, atkComp.State);
        Assert.AreEqual(1, atkComp.AttackCount);

        // ReadyFrame 到达的同一 Tick 自动起手，完整周期严格为 30 Tick。
        AdvanceFrames(atkComp, 1);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.AreEqual(2, atkComp.AttackCount);
    }

    [Test]
    public void 零前摇和零后摇在起手帧完成且不残留移动锁()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);
        var targeting = new SimTargetingComp(attacker, new List<IEntityContext> { attacker, target })
        {
            AggroRangeFixed = (Fix64)10f
        };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;
        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 20f, windUp: 0f, windDown: 0f, interval: 0f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);

        targeting.CurrentTarget = target;
        StartAttack(atkComp);

        Assert.AreEqual(80f, (float)target.Health.currentHealth, 0.01f);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State);
        Assert.IsFalse(atkComp.IsAttacking);
        Assert.IsTrue(attacker.CanRun(moveComp), "零后摇必须在起手帧释放移动锁");

        atkComp.Attack((Fix64)999);
        Assert.AreEqual(1, atkComp.AttackCount, "同一逻辑帧最多只能起手一次");
        Assert.AreEqual(80f, (float)target.Health.currentHealth, 0.01f);

        AdvanceFrames(atkComp, 1);
        Assert.AreEqual(2, atkComp.AttackCount, "零间隔攻击应在下一逻辑帧继续自动起手");
        Assert.AreEqual(60f, (float)target.Health.currentHealth, 0.01f);
    }

    [Test]
    public void 前摇中打断会取消命中且重复打断不会残留移动锁()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);
        var targeting = new SimTargetingComp(attacker, new List<IEntityContext> { attacker, target })
        {
            AggroRangeFixed = (Fix64)10f
        };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;
        var brain = new ScriptedBrain { Attack = true };
        attacker.Brain = brain;

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(damage: 20f, windUp: 0.3f, windDown: 0.3f, interval: 1f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);

        targeting.CurrentTarget = target;
        StartAttack(atkComp);
        Assert.IsFalse(attacker.CanRun(moveComp));

        brain.Attack = false;
        atkComp.InterruptAttack(AttackInterruptReason.Forced);
        atkComp.InterruptAttack(AttackInterruptReason.Forced);
        AdvanceFrames(atkComp, 9);

        Assert.AreEqual(100f, (float)target.Health.currentHealth, 0.01f, "已打断 schedule 的 HitFrame 不得提交伤害");
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp));
    }

    [Test]
    public void 攻击表现通知只在起手和实际打断时各触发一次()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);
        var targeting = new SimTargetingComp(attacker, new List<IEntityContext> { attacker, target })
        {
            AggroRangeFixed = (Fix64)10f
        };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;
        attacker.Brain = new ScriptedBrain { Attack = true };
        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;
        var weapon = MeleeWeapon(windUp: 0.3f, windDown: 0.3f, interval: 1f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new DirectAtkComp();
        atkComp.Init(attacker);
        targeting.CurrentTarget = target;

        int startedCount = 0;
        int interruptedCount = 0;
        Fix64 presentedWindUp = Fix64.Zero;
        bool presentedTrail = false;
        atkComp.AttackPresentationStarted += (windUp, playTrail) =>
        {
            startedCount++;
            presentedWindUp = windUp;
            presentedTrail = playTrail;
        };
        atkComp.AttackPresentationInterrupted += () => interruptedCount++;

        StartAttack(atkComp);
        atkComp.InterruptAttack(AttackInterruptReason.Forced);
        atkComp.InterruptAttack(AttackInterruptReason.Forced);

        Assert.AreEqual(1, startedCount);
        Assert.AreEqual((Fix64)0.3f, presentedWindUp);
        Assert.IsTrue(presentedTrail);
        Assert.AreEqual(1, interruptedCount);
    }

    [Test]
    public void 移动攻击组件在前摇和后摇期间不锁移动()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide);

        var targeting = new SimTargetingComp(attacker, new List<IEntityContext> { attacker, target })
        {
            AggroRangeFixed = (Fix64)10f
        };
        targeting.Init(attacker);
        attacker.TargetComp = targeting;
        attacker.Brain = new ScriptedBrain { Attack = true };

        var moveComp = new SimMoveComp();
        moveComp.Init(attacker);
        attacker.MoveComp = moveComp;

        var weapon = MeleeWeapon(windUp: 0.3f, windDown: 0.3f, interval: 1f);
        attacker.WeaponComp = new WeaponComp(weapon.ToWeapon("TestWeapon"));
        var atkComp = new MoveAtkComp();
        atkComp.Init(attacker);
        attacker.AtkComp = atkComp;

        targeting.UpdateTargeting(Fix64.One);
        StartAttack(atkComp);

        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp), "移动攻击组件在前摇期间不应锁移动");

        AdvanceFrames(atkComp, 9);

        Assert.AreEqual(DirectAtkComp.AtkState.WindDown, atkComp.State);
        Assert.IsTrue(attacker.CanRun(moveComp), "移动攻击组件在后摇期间不应锁移动");
    }

    [Test]
    public void 目标超出范围时不攻击()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(10, 0, 0), SideType.EnemySide); // 10m 远

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)20f };
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

        targeting.UpdateTargeting(Fix64.One);
        Assert.IsNotNull(targeting.CurrentTarget);

        StartAttack(atkComp);
        Assert.AreEqual(DirectAtkComp.AtkState.Idle, atkComp.State, "目标超出攻击范围不应攻击");
    }

    [Test]
    public void 目标死亡后不造成伤害()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide, hp: 5f);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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

        targeting.UpdateTargeting(Fix64.One);

        // 第一次攻击，杀死目标
        StartAttack(atkComp);
        AdvanceFrames(atkComp, 3);

        Assert.AreEqual(0f, (float)target.Health.currentHealth, 0.01f);
        Assert.IsFalse(target.Alive, "目标应该死亡");
    }

    [Test]
    public void 连续攻击多次击杀目标()
    {
        var attacker = CreateUnit(Vector3.zero, SideType.PlayerSide);
        var target = CreateUnit(new Vector3(1, 0, 0), SideType.EnemySide, hp: 50f);

        var allEntities = new List<IEntityContext> { attacker, target };
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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

        // 模拟 5 秒、150 个逻辑帧的战斗。
        for (int i = 0; i < 150; i++)
        {
            LogicFrameRuntime.Tick(LogicFrameRuntime.CurrentFrame + 1);
            targeting.UpdateTargeting(LogicFrameRuntime.FixedDeltaTime);
            atkComp.Attack(Fix64.Zero);
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
        var targeting = new SimTargetingComp(attacker, allEntities) { AggroRangeFixed = (Fix64)10f };
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

        targeting.UpdateTargeting(Fix64.One);

        StartAttack(atkComp);
        Assert.AreEqual(DirectAtkComp.AtkState.WindUp, atkComp.State, "远程单位应能在射程内攻击");
        Assert.AreEqual(1, atkComp.AttackCount);
    }

    // 转向功能已移除（不需要小兵在攻击时旋转），此测试已废弃
}
