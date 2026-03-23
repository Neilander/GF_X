# 任务规划：实现远程敌人 (Ranged Enemy) v2

> 本文档可直接交给 Coder Agent 执行。每个 TASK 是一个独立的编码单元。
>
> **v2 变更**：采用 IAttackExecutor 策略模式替代 DealDamage 分支改造；
> WeaponData 从 struct 升级为 ScriptableObject。

---

## 现状分析

### 已有的基础设施
- `WeaponType` 枚举：Melee / Projectile / InstantRanged ✅
- `AbstractProjectile → DirectionProjectile` 弹道类 ✅
- `EntityGroup.Bullet` 实体组 ✅
- `HitBox` / `HurtBox` 碰撞检测系统 ✅
- `DirectAtkCompFactory.projectileSpeed` 字段 ✅（但未使用）

### 当前问题
- **WeaponData 是 struct**，武器参数散在 DirectAtkCompFactory 的字段里，不可复用、不可独立配置
- **DirectAtkComp.DealDamage() 写死了直接扣血**，不区分 WeaponType
- **SoldierAIBrain 没有远程行为**（保持距离、后退）

### 核心设计决策

**1. IAttackExecutor 策略模式**

不改 DirectAtkComp 状态机内部逻辑，而是把"出手动作"抽象为可替换的策略：

```
DirectAtkComp（状态机：蓄力 → 出手 → 收招 → 冷却）
      │
      │ WindUp 结束，调用 _executor.Execute()
      │
      ▼
IAttackExecutor（策略接口）
      │
      ├─ MeleeAttackExecutor      → target.TakeDamage()         不变
      ├─ ProjectileAttackExecutor → ShowEntity<Projectile>()    新增
      └─ InstantRangedAttackExecutor → 射线即时伤害             预留
```

工厂在创建 DirectAtkComp 时根据 WeaponType 注入对应的 Executor。运行时零分支。

**2. WeaponData 升级为 ScriptableObject**

现状：武器参数（damage, range, speed...）散在 DirectAtkCompFactory 的 public 字段里。
目标：WeaponData 独立为 SO，工厂通过引用持有它，多个工厂可以共享同一把武器配置。

```
改造前：
  DirectAtkCompFactory (SO)
    ├─ float damage = 10
    ├─ float attackRange = 200
    ├─ WeaponType weaponType = Melee
    └─ float projectileSpeed = 0     ← 近战根本不需要这个字段

改造后：
  DirectAtkCompFactory (SO)
    └─ WeaponData weaponData ← 引用一个 WeaponData SO

  WeaponData (SO)  "近战剑"
    ├─ weaponType = Melee
    ├─ damage = 15
    ├─ attackRange = 150
    └─ (不需要 projectile 相关字段)

  WeaponData (SO)  "弓箭"
    ├─ weaponType = Projectile
    ├─ damage = 8
    ├─ attackRange = 800
    ├─ projectileSpeed = 15
    └─ projectilePrefab = "Arrow"
```

---

## 任务分解

```
TASK-R01  WeaponData 升级为 ScriptableObject           ← 基础重构，其他都依赖它
TASK-R02  实现 IAttackExecutor 策略模式                 ← 依赖 R01
TASK-R03  打通弹道伤害链（Projectile → HitBox → 扣血）   ← 依赖 R02
TASK-R04  实现 RangedAIBrain（远程 AI 行为）             ← 独立，可与 R02/R03 并行
TASK-R05  创建武器/工厂资源 + DataTable + 生成远程敌人   ← 依赖 R03 + R04
```

```
依赖关系图：

R01 ──→ R02 ──→ R03 ──→ R05
                          ↑
R04 ─────────────────────┘
```

---

## TASK-R01：WeaponData 升级为 ScriptableObject

### 目标
将 WeaponData 从 struct 升级为 ScriptableObject，使武器配置可在 Unity Inspector 中独立创建、复用。

### 修改文件
| 文件 | 改动 |
|------|------|
| `Scripts/GeneralCreature/WeaponData.cs` | struct → ScriptableObject，加 CreateAssetMenu |
| `Scripts/GeneralCreature/DirectAtkCompFactory.cs` | 散落字段 → 单个 WeaponData 引用 |
| `Scripts/GeneralCreature/WeaponComp.cs` | 确认兼容（可能无需改动） |

### 详细实现

#### 1. WeaponData.cs — 升级为 SO

```csharp
// 改造前：
// public struct WeaponData { public float Damage; ... }

// 改造后：
using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Combat/WeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("基础属性")]
    [SerializeField] private WeaponType _weaponType = WeaponType.Melee;
    [SerializeField] private float _damage = 10f;
    [SerializeField] private float _attackInterval = 1.5f;
    [SerializeField] private float _attackRange = 200f;  // 单位：百分之一米
    [SerializeField] private float _windUp = 0.3f;
    [SerializeField] private float _windDown = 0.2f;
    [SerializeField] private int _manaCost = 0;

    [Header("远程属性（仅 Projectile 类型生效）")]
    [SerializeField] private float _projectileSpeed = 15f;
    [SerializeField] private string _projectilePrefabName = "";
    [SerializeField] private float _maxProjectileRange = 30f;

    [Header("溅射（可选）")]
    [SerializeField] private float _splashRadius = 0f;

    // 公共属性（只读）
    public WeaponType WeaponType => _weaponType;
    public float Damage => _damage;
    public float AttackInterval => _attackInterval;
    public float AttackRange => _attackRange;
    public float WindUp => _windUp;
    public float WindDown => _windDown;
    public int ManaCost => _manaCost;
    public float ProjectileSpeed => _projectileSpeed;
    public string ProjectilePrefabName => _projectilePrefabName;
    public float MaxProjectileRange => _maxProjectileRange;
    public float SplashRadius => _splashRadius;
}
```

#### 2. DirectAtkCompFactory.cs — 引用 WeaponData SO

```csharp
// 改造前：
// public float damage;
// public float attackInterval;
// public WeaponType weaponType;
// ... 一堆散落字段

// 改造后：
[CreateAssetMenu(fileName = "DirectAtkFactory", menuName = "ATK Factory/DirectAtk")]
public class DirectAtkCompFactory : AtkCompFactory
{
    [SerializeField] private WeaponData _weaponData;  // 引用 WeaponData SO

    public override IAtkComp CreateAtkComp(MAEntity entity)
    {
        var comp = new DirectAtkComp(_weaponData);
        comp.Init(entity);
        entity.SetAtkComp(comp);

        var weaponComp = new WeaponComp(_weaponData);
        entity.SetWeaponComp(weaponComp);

        return comp;
    }
}
```

#### 3. DirectAtkComp 构造函数适配

```csharp
// 改造前（如果是接收散落参数的话）：
// public DirectAtkComp(float damage, float range, ...) { ... }

// 改造后：
public DirectAtkComp(WeaponData weapon)
{
    _weapon = weapon;  // 直接持有 SO 引用
}
```

#### 4. 迁移已有配置

已有的 DirectAtkCompFactory 资源实例（近战剑兵等）中的散落字段需要迁移到独立的 WeaponData SO：

```
步骤：
1. 为每个已有的 DirectAtkCompFactory 实例创建一个对应的 WeaponData SO
2. 把原来的字段值抄到 WeaponData SO 里
3. 让 DirectAtkCompFactory 引用对应的 WeaponData SO
4. 删除 DirectAtkCompFactory 上的废弃字段
```

### 验收标准
- [ ] WeaponData 是 ScriptableObject，有 [CreateAssetMenu]
- [ ] WeaponData 包含所有武器属性（含远程字段），字段用 [SerializeField] private + 公共只读属性
- [ ] DirectAtkCompFactory 改为持有单个 WeaponData 引用，不再有散落字段
- [ ] DirectAtkComp 构造函数接收 WeaponData
- [ ] WeaponComp 兼容新的 WeaponData SO
- [ ] **已有的近战配置迁移完成，近战行为不受影响**

### 注意事项
- ⚠️ **这是重构任务，最大风险是破坏现有近战**。必须先迁移已有配置再删旧字段
- WeaponData SO 是运行时只读的（配置数据），不要在运行时修改它的字段
- 远程字段（projectileSpeed 等）对于近战武器来说值为默认值，不会被使用，无害
- WeaponType 枚举保留在 WeaponData.cs 文件中

---

## TASK-R02：实现 IAttackExecutor 策略模式

### 目标
将 DirectAtkComp 的"出手动作"抽象为 IAttackExecutor 接口，由工厂根据 WeaponType 注入。

### 新建文件
| 文件 | 说明 |
|------|------|
| `Scripts/GeneralCreature/IAttackExecutor.cs` | 攻击执行策略接口 |
| `Scripts/GeneralCreature/MeleeAttackExecutor.cs` | 近战执行器 |
| `Scripts/GeneralCreature/ProjectileAttackExecutor.cs` | 远程弹道执行器 |

### 修改文件
| 文件 | 改动 |
|------|------|
| `Scripts/GeneralCreature/DirectAtkComp.cs` | DealDamage → _executor.Execute() |
| `Scripts/GeneralCreature/DirectAtkCompFactory.cs` | 根据 WeaponType 注入 Executor |

### 详细实现

#### 1. IAttackExecutor.cs

```csharp
/// <summary>
/// 攻击执行策略接口
/// 由 DirectAtkComp 在状态机 WindUp 结束时调用
/// </summary>
public interface IAttackExecutor
{
    /// <summary>
    /// 执行攻击动作
    /// </summary>
    /// <param name="ctx">攻击者实体上下文</param>
    /// <param name="target">攻击目标</param>
    /// <param name="weapon">武器数据</param>
    void Execute(IEntityContext ctx, IEntityContext target, WeaponData weapon);
}
```

#### 2. MeleeAttackExecutor.cs

```csharp
/// <summary>
/// 近战攻击执行器：直接对目标造成伤害
/// 行为与原 DealDamage() 完全一致
/// </summary>
public class MeleeAttackExecutor : IAttackExecutor
{
    public void Execute(IEntityContext ctx, IEntityContext target, WeaponData weapon)
    {
        if (target == null || !target.Alive) return;

        target.TakeDamage(weapon.Damage, HealthModifyType.reduce);

        if (weapon.SplashRadius > 0f)
        {
            ApplySplashDamage(ctx, target, weapon);
        }
    }

    private void ApplySplashDamage(IEntityContext ctx, IEntityContext mainTarget,
                                    WeaponData weapon)
    {
        // 溅射逻辑：遍历 EntityRegistry，范围内敌对单位受溅射伤害
        // TODO: 当前 DirectAtkComp 的 ApplySplashDamage 是空实现
        //       如果原来就是空的，这里也保持空
    }
}
```

#### 3. ProjectileAttackExecutor.cs

```csharp
/// <summary>
/// 远程弹道攻击执行器：在攻击者位置生成弹道 Entity
/// 伤害由弹道的 HitBox 碰撞触发，不在此处直接扣血
/// </summary>
public class ProjectileAttackExecutor : IAttackExecutor
{
    // 弹道发射点相对于实体位置的偏移（胸口高度）
    private static readonly Vector3 SpawnOffset = new Vector3(0f, 1.2f, 0f);

    public void Execute(IEntityContext ctx, IEntityContext target, WeaponData weapon)
    {
        if (target == null || !target.Alive) return;

        Vector3 spawnPos = ctx.Position + SpawnOffset;
        Vector3 direction = (target.Position + SpawnOffset - spawnPos).normalized;

        int projectileId = UtilityBuiltin.AssignEntityId();

        // 通过 VariablePool 传递弹道参数
        GF.Variable.SetVariable<VarFloat>(projectileId, "Damage", weapon.Damage);
        GF.Variable.SetVariable<VarFloat>(projectileId, "Speed", weapon.ProjectileSpeed);
        GF.Variable.SetVariable<VarFloat>(projectileId, "MaxRange", weapon.MaxProjectileRange);
        GF.Variable.SetVariable<VarVector3>(projectileId, "Direction", direction);
        GF.Variable.SetVariable<VarInt32>(projectileId, "OwnerSide", (int)ctx.Side);
        GF.Variable.SetVariable<VarFloat>(projectileId, "SplashRadius", weapon.SplashRadius);

        GF.Entity.ShowEntity<DirectionProjectile>(
            projectileId,
            weapon.ProjectilePrefabName,
            Const.Groups.EntityGroup.Bullet
        );
    }
}
```

#### 4. DirectAtkComp.cs — 改动极小

```csharp
public class DirectAtkComp : IAtkComp
{
    private WeaponData _weapon;
    private IAttackExecutor _executor;  // 新增
    private IEntityContext _ctx;
    // ... 其他字段不变

    // 构造函数新增 executor 参数
    public DirectAtkComp(WeaponData weapon, IAttackExecutor executor)
    {
        _weapon = weapon;
        _executor = executor;
    }

    // DealDamage 改造：一行搞定
    private void DealDamage()
    {
        _executor.Execute(_ctx, _lockedTarget, _weapon);
    }

    // 状态机其他代码（Idle/WindUp/WindDown/Cooldown）完全不动
}
```

#### 5. DirectAtkCompFactory.cs — 注入 Executor

```csharp
public override IAtkComp CreateAtkComp(MAEntity entity)
{
    // 根据武器类型选择执行器
    IAttackExecutor executor = _weaponData.WeaponType switch
    {
        WeaponType.Melee => new MeleeAttackExecutor(),
        WeaponType.Projectile => new ProjectileAttackExecutor(),
        WeaponType.InstantRanged => new MeleeAttackExecutor(),  // 暂时复用近战，后续实现
        _ => new MeleeAttackExecutor(),
    };

    var comp = new DirectAtkComp(_weaponData, executor);
    comp.Init(entity);
    entity.SetAtkComp(comp);

    var weaponComp = new WeaponComp(_weaponData);
    entity.SetWeaponComp(weaponComp);

    return comp;
}
```

### 验收标准
- [ ] IAttackExecutor 接口定义清晰，Execute 方法签名正确
- [ ] MeleeAttackExecutor 行为与原 DealDamage **完全一致**
- [ ] ProjectileAttackExecutor 通过 ShowEntity 生成弹道，不直接扣血
- [ ] DirectAtkComp.DealDamage() 改为调用 `_executor.Execute()`
- [ ] DirectAtkComp 构造函数接收 IAttackExecutor
- [ ] DirectAtkCompFactory 根据 WeaponType 注入正确的 Executor
- [ ] **所有现有近战单位行为不变**（MeleeAttackExecutor 兜底）

### 注意事项
- ⚠️ MeleeAttackExecutor 必须**原样复制** DealDamage 原有逻辑，不能遗漏任何细节
- ProjectileAttackExecutor 中**不扣血**，伤害完全由弹道 HitBox 碰撞链触发
- InstantRanged 先用 MeleeAttackExecutor 兜底，后续再实现专门的射线检测执行器
- SpawnOffset 高度 1.2f 是经验值，可能需要按 Entity 实际模型调整

---

## TASK-R03：打通弹道伤害链

### 目标
确保 ProjectileAttackExecutor 生成的弹道 Entity 能正确飞行、碰撞、造成伤害、自动回收。

### 修改文件
| 文件 | 改动 |
|------|------|
| `Scripts/Projectile/DirectionProjectile.cs` | 从 VariablePool 读参数，驱动飞行，碰撞后回收 |
| `Scripts/Projectile/AbstractProjectile.cs` | 确认 HitBox 管理逻辑可复用（可能无需改） |

### 详细实现

#### DirectionProjectile.cs

```csharp
public class DirectionProjectile : AbstractProjectile
{
    private float _damage;
    private float _speed;
    private float _maxRange;
    private Vector3 _direction;
    private SideType _ownerSide;
    private float _splashRadius;
    private float _distanceTraveled;

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);

        // 从 VariablePool 读取 ProjectileAttackExecutor 传入的参数
        _damage = GF.Variable.GetVariable<VarFloat>(Id, "Damage");
        _speed = GF.Variable.GetVariable<VarFloat>(Id, "Speed");
        _maxRange = GF.Variable.GetVariable<VarFloat>(Id, "MaxRange");
        _direction = GF.Variable.GetVariable<VarVector3>(Id, "Direction");
        _ownerSide = (SideType)GF.Variable.GetVariable<VarInt32>(Id, "OwnerSide");
        _splashRadius = GF.Variable.GetVariable<VarFloat>(Id, "SplashRadius");
        _distanceTraveled = 0f;

        // 朝向
        transform.forward = _direction;

        // 激活 HitBox（AbstractProjectile 管理）
        ActivateHitBox();
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        // 飞行
        float step = _speed * elapseSeconds;
        transform.position += _direction * step;
        _distanceTraveled += step;

        // 超出射程回收
        if (_distanceTraveled >= _maxRange)
        {
            GF.Entity.HideEntity(this);
        }
    }

    /// <summary>
    /// HitBox 碰撞回调（由 AbstractProjectile 的 HitBox 管理逻辑触发）
    /// </summary>
    public override void OnHitTarget(IEntityContext target)
    {
        if (target == null || !target.Alive) return;

        // 阵营过滤：不伤害友军
        if (target.Side == _ownerSide) return;

        // 造成伤害
        target.TakeDamage(_damage, HealthModifyType.reduce);

        // 溅射
        if (_splashRadius > 0f)
        {
            ApplySplashDamage(target.Position);
        }

        // 命中后回收（非穿透弹道）
        GF.Entity.HideEntity(this);
    }

    private void ApplySplashDamage(Vector3 impactPoint)
    {
        // 遍历 EntityRegistry，对范围内敌对单位造成伤害
        var entities = EntityRegistry.GetEntitiesInRange(impactPoint, _splashRadius);
        foreach (var entity in entities)
        {
            if (entity.Side != _ownerSide && entity.Alive)
            {
                entity.TakeDamage(_damage * 0.5f, HealthModifyType.reduce);  // 溅射半伤
            }
        }
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        // 清理 VariablePool 中的临时变量
        GF.Variable.RemoveAllVariables(Id);
        base.OnHide(isShutdown, userData);
    }
}
```

### 需要确认的关键点

> ⚠️ Coder 在实现前必须确认以下几点，根据实际代码调整：

1. **AbstractProjectile.ActivateHitBox()** — 确认这个方法是否存在，HitBox 的创建和激活机制是什么
2. **OnHitTarget 是否是已有的虚方法** — 如果 AbstractProjectile 没有这个回调，需要找到 HitBox 碰撞后如何通知弹道的机制（可能是事件、委托或直接方法调用）
3. **EntityRegistry.GetEntitiesInRange()** — 确认是否存在这个方法，如果没有需要新增
4. **VariablePool 清理** — 确认 Variable 的生命周期管理方式，可能 HideEntity 时自动清理

### 验收标准
- [ ] 弹道从 VariablePool 正确读取所有参数
- [ ] 弹道按指定方向匀速飞行
- [ ] 弹道碰到敌方 HurtBox 时造成伤害
- [ ] 弹道不伤害友军（OwnerSide 过滤）
- [ ] 弹道命中后自动回收
- [ ] 弹道超出 MaxRange 后自动回收
- [ ] 溅射伤害正确计算（SplashRadius > 0 时）
- [ ] VariablePool 临时变量在弹道回收时清理

### 注意事项
- 弹道是 **Entity**，用 ShowEntity/HideEntity 管理生命周期
- **先扣血，再 HideEntity**，顺序不能反
- 弹道可能在攻击者死亡后才命中目标，不要依赖攻击者的存活状态
- EntityGroup.Bullet 的**对象池容量**需要足够，否则大量弹道会导致卡顿

---

## TASK-R04：实现 RangedAIBrain

### 目标
实现远程 AI Brain，核心行为：保持在攻击范围边缘，目标太近时后退。

### 新建文件
| 文件 | 说明 |
|------|------|
| `Scripts/Movement/RangedAIBrain.cs` | 远程 AI Brain |

### 修改文件
| 文件 | 改动 |
|------|------|
| `BrainType` 枚举所在文件 | 新增 `RangedAI = 4` |
| `Scripts/NeilUtility/BrainFactory.cs` | 注册 RangedAIBrain |

### 状态机设计

```
         ┌──────────────────────────────────────┐
         │                                      │
         ▼            有目标                     │ 丢失目标
    ┌─────────┐ ──────────→ ┌───────────┐       │
    │  Idle   │             │ Approach  │       │
    │ (跟随)  │ ←────────── │ (接近)     │       │
    └─────────┘  丢失目标    └─────┬─────┘       │
                                  │ 进入射程     │
                                  ▼              │
                            ┌─────────┐         │
                            │ Combat  │─────────┘
                            │ (射击)   │
                            └────┬────┘
                                 │ 目标太近
                                 ▼
                            ┌──────────┐
                            │ Retreat  │
                            │ (后退)   │──→ 拉开距离后回到 Combat
                            └──────────┘
```

### 详细实现

```csharp
public class RangedAIBrain : IControlBrain, ITickBrain
{
    private enum State { Idle, Approach, Combat, Retreat }

    private State _state = State.Idle;

    // ─── 配置参数 ───
    private readonly float _preferredRange;    // 理想攻击距离（射程的 ~70%）
    private readonly float _minRange;          // 最小安全距离（低于此就后退）
    private readonly float _maxChaseRange;     // 最大追击范围（超出则放弃）

    // ─── IControlBrain 输出 ───
    public bool Move { get; private set; }
    public bool Attack { get; private set; }
    public bool Skill1 { get; private set; }
    public bool Skill2 { get; private set; }
    public bool Skill3 { get; private set; }

    /// <param name="weaponRange">武器射程（从 WeaponData.AttackRange * 0.01f）</param>
    /// <param name="minRangeRatio">最小距离 = weaponRange × ratio（默认 0.3）</param>
    /// <param name="preferredRangeRatio">理想距离 = weaponRange × ratio（默认 0.7）</param>
    public RangedAIBrain(float weaponRange,
                          float minRangeRatio = 0.3f,
                          float preferredRangeRatio = 0.7f,
                          float maxChaseRange = 15f)
    {
        _preferredRange = weaponRange * preferredRangeRatio;
        _minRange = weaponRange * minRangeRatio;
        _maxChaseRange = maxChaseRange;
    }

    public void Tick(IEntityContext ctx, float deltaTime)
    {
        var target = ctx.TargetComp?.CurrentTarget;
        bool hasTarget = target != null && target.Alive;

        // 目标丢失 → 回 Idle
        if (!hasTarget && _state != State.Idle)
        {
            TransitionTo(State.Idle);
        }

        switch (_state)
        {
            case State.Idle:
                TickIdle(ctx, target, hasTarget);
                break;
            case State.Approach:
                TickApproach(ctx, target, hasTarget);
                break;
            case State.Combat:
                TickCombat(ctx, target);
                break;
            case State.Retreat:
                TickRetreat(ctx, target);
                break;
        }
    }

    private void TickIdle(IEntityContext ctx, IEntityContext target, bool hasTarget)
    {
        Attack = false;
        Move = false;

        // 跟随逻辑（复用 FollowTarget）
        var follow = ctx.TargetComp?.FollowTarget;
        if (follow != null)
        {
            MoveToward(ctx, follow.Position);
        }

        if (hasTarget)
            TransitionTo(State.Approach);
    }

    private void TickApproach(IEntityContext ctx, IEntityContext target, bool hasTarget)
    {
        if (!hasTarget) { TransitionTo(State.Idle); return; }

        Attack = false;
        float dist = Vector3.Distance(ctx.Position, target.Position);

        if (dist <= _preferredRange)
        {
            TransitionTo(State.Combat);
        }
        else if (dist > _maxChaseRange)
        {
            TransitionTo(State.Idle);  // 太远了，放弃追击
        }
        else
        {
            MoveToward(ctx, target.Position);
        }
    }

    private void TickCombat(IEntityContext ctx, IEntityContext target)
    {
        float dist = Vector3.Distance(ctx.Position, target.Position);

        if (dist < _minRange)
        {
            TransitionTo(State.Retreat);
            return;
        }

        if (dist > _preferredRange * 1.3f)  // 超出理想距离 130%，重新接近
        {
            TransitionTo(State.Approach);
            return;
        }

        // 站定射击
        Attack = true;
        Move = false;
        FaceTarget(ctx, target);
    }

    private void TickRetreat(IEntityContext ctx, IEntityContext target)
    {
        Attack = false;
        float dist = Vector3.Distance(ctx.Position, target.Position);

        if (dist >= _preferredRange)
        {
            TransitionTo(State.Combat);  // 拉开了，继续射击
        }
        else
        {
            MoveAwayFrom(ctx, target.Position);
        }
    }

    // ─── 辅助方法 ───

    private void MoveToward(IEntityContext ctx, Vector3 targetPos)
    {
        Move = true;
        Vector3 dir = (targetPos - ctx.Position).normalized;
        ctx.MoveExecutor.SetInput(dir);
    }

    private void MoveAwayFrom(IEntityContext ctx, Vector3 threatPos)
    {
        Move = true;
        Vector3 dir = (ctx.Position - threatPos).normalized;
        ctx.MoveExecutor.SetInput(dir);
    }

    private void FaceTarget(IEntityContext ctx, IEntityContext target)
    {
        Vector3 dir = (target.Position - ctx.Position).normalized;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Rotation = Quaternion.LookRotation(dir);
    }

    private void TransitionTo(State newState)
    {
        _state = newState;
        // 状态切换时重置输出
        Move = false;
        Attack = false;
    }
}
```

### BrainType 枚举 + BrainFactory

```csharp
// BrainType 枚举新增
public enum BrainType
{
    Player = 0,
    EnemyAI = 1,
    FriendlyAI = 2,
    SoldierAI = 3,
    RangedAI = 4,       // 新增
}

// BrainFactory.cs 新增 case
case BrainType.RangedAI:
    float weaponRange = entity.WeaponComp?.AttackRange * 0.01f ?? 8f;
    return new RangedAIBrain(weaponRange);
```

### 验收标准
- [ ] RangedAIBrain 实现 IControlBrain + ITickBrain
- [ ] 4 状态正确切换：Idle → Approach → Combat ⇄ Retreat
- [ ] Combat 状态输出 Attack = true, Move = false
- [ ] Retreat 状态输出远离目标的移动方向
- [ ] 目标丢失 / 死亡后回到 Idle
- [ ] BrainType.RangedAI 枚举和 BrainFactory 注册完成
- [ ] 射击距离参数从 WeaponComp.AttackRange 自动计算

### 注意事项
- **不要修改 SoldierAIBrain**，RangedAIBrain 是独立新类
- MoveAwayFrom 的方向靠 MoveExecutor.SetInput() 传入，物理碰撞由 CharacterController 处理
- GroupMoveManager ORCA 可能覆盖后退速度——如果测试发现后退被拉回来，需要给远程单位更高的 ORCA 权重或短暂跳过 ORCA
- _preferredRange 基于 WeaponData.AttackRange 自动计算，不需要额外配置

---

## TASK-R05：创建资源配置 + 生成远程敌人

### 目标
创建武器 SO、工厂 SO、DataTable 数据，在 Procedure 中生成远程敌人。

### 新建资源
| 资源 | 类型 | 说明 |
|------|------|------|
| `Weapons/Bow_Basic.asset` | WeaponData SO | 基础弓箭配置 |
| `Factories/RangedAtkFactory_Archer.asset` | DirectAtkCompFactory SO | 引用 Bow_Basic |
| `Factories/TargetingFactory_Ranged.asset` | CharacterTargetingFactory SO | 更大索敌范围 |
| 弹道 Prefab `Arrow` | Prefab | 箭矢模型 + HitBox 子对象 |

### WeaponData SO 配置示例

**Bow_Basic（基础弓箭）：**

| 字段 | 值 | 说明 |
|------|------|------|
| weaponType | Projectile | |
| damage | 8 | 低于近战（远程优势换伤害） |
| attackInterval | 2.0 | 慢于近战 |
| attackRange | 800 | 8m（百分之一米单位） |
| windUp | 0.5 | 拉弓 |
| windDown | 0.3 | 射后硬直 |
| manaCost | 0 | |
| projectileSpeed | 15 | 15m/s |
| projectilePrefabName | "Arrow" | |
| maxProjectileRange | 30 | 30m 后自动消失 |
| splashRadius | 0 | 单体 |

### DataTable 新增行

**CharacterMAFactoryTable：**

| Id | CharacterKey | MoveFactoryPath | AttackFactoryPath | TargetingFactoryPath |
|----|-------------|-----------------|-------------------|---------------------|
| 201 | RangedEnemy | CharacterMoveFactory（已有） | RangedAtkFactory_Archer | TargetingFactory_Ranged |

**CombatUnitTable：**

| Id | PrefabName | MoveSpeed | Hp |
|----|-----------|-----------|-----|
| 201 | RangedEnemy | 3.0 | 60 |

### Procedure 中生成

```csharp
// CharacterTestProcedure.cs 中，在已有 Soldier 生成附近

// 通过 VariablePool 传参
int rangedId = UtilityBuiltin.AssignEntityId();
GF.Variable.SetVariable<VarInt32>(rangedId, "Side", (int)SideType.EnemySide);
GF.Variable.SetVariable<VarInt32>(rangedId, "BrainType", (int)BrainType.RangedAI);
GF.Variable.SetVariable<VarInt32>(rangedId, "CombatUnitId", 201);

GF.Entity.ShowEntity<SoldierEntity>(
    rangedId,
    "RangedEnemy",
    Const.Groups.EntityGroup.Default
);
```

### 验收标准
- [ ] WeaponData SO 创建并配置完成
- [ ] DirectAtkCompFactory SO 引用 WeaponData SO
- [ ] DataTable 中有远程敌人配置行
- [ ] Procedure 能成功生成远程敌人
- [ ] 远程敌人使用 SoldierEntity + BrainType.RangedAI
- [ ] 远程敌人保持距离射击，弹道命中目标造成伤害
- [ ] 近战敌人行为完全不受影响

---

## 总文件变更清单

### 新建文件（4 个）
| 文件 | 任务 |
|------|------|
| `Scripts/GeneralCreature/IAttackExecutor.cs` | R02 |
| `Scripts/GeneralCreature/MeleeAttackExecutor.cs` | R02 |
| `Scripts/GeneralCreature/ProjectileAttackExecutor.cs` | R02 |
| `Scripts/Movement/RangedAIBrain.cs` | R04 |

### 修改文件（6 个）
| 文件 | 任务 | 改动 |
|------|------|------|
| `Scripts/GeneralCreature/WeaponData.cs` | R01 | struct → ScriptableObject |
| `Scripts/GeneralCreature/DirectAtkCompFactory.cs` | R01+R02 | 散落字段 → WeaponData 引用 + 注入 Executor |
| `Scripts/GeneralCreature/DirectAtkComp.cs` | R02 | 构造函数加 IAttackExecutor，DealDamage 一行改 |
| `Scripts/Projectile/DirectionProjectile.cs` | R03 | 读参数 + 飞行 + 碰撞 + 回收 |
| BrainType 枚举文件 | R04 | 新增 RangedAI = 4 |
| `Scripts/NeilUtility/BrainFactory.cs` | R04 | 注册 RangedAIBrain |

### 新建资源（4 个）
| 资源 | 任务 |
|------|------|
| WeaponData SO: Bow_Basic | R05 |
| DirectAtkCompFactory SO: RangedAtkFactory_Archer | R05 |
| CharacterTargetingFactory SO: TargetingFactory_Ranged | R05 |
| Arrow 弹道 Prefab | R05 |

### DataTable 变更（2 张表）
| 表 | 改动 | 任务 |
|----|------|------|
| CombatUnitTable | 新增远程敌人行 | R05 |
| CharacterMAFactoryTable | 新增远程敌人工厂路径行 | R05 |

---

## 风险清单

| # | 风险 | 影响 | 应对 |
|---|------|------|------|
| 1 | WeaponData 迁移破坏现有近战 | 所有近战单位异常 | R01 必须先迁移已有 SO 实例再删旧字段，改完立即编译测试 |
| 2 | 弹道碰撞误伤友军 | 打自己人 | R03 中 OnHitTarget 必须检查 OwnerSide |
| 3 | ORCA 覆盖后退速度 | 远程兵退不了 | 测试 Retreat 行为，必要时给远程单位跳过 ORCA 或提高权重 |
| 4 | EntityGroup.Bullet 对象池容量不足 | 多个远程兵齐射卡顿 | 检查 EntityGroupTable 中 Bullet 组 Capacity，按需调大 |
| 5 | 弹道异步创建时序 | 攻击者已死但弹道还在生成 | DirectionProjectile.OnShow 校验参数有效性 |
| 6 | AbstractProjectile 的 HitBox 回调机制不确定 | R03 代码可能需要调整 | Coder 实现 R03 前必须先读 AbstractProjectile 源码确认 |
