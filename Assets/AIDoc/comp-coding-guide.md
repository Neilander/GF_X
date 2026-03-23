# Comp 组件编码指南

> 新增 Comp 组件的完整流程和注意事项。Coder Agent 必读文档。

---

## 新增一个 Comp 的完整清单

以新增一个 `IBuffComp`（Buff 系统组件）为例，需要创建 **5 个文件**：

```
1. IBuffComp.cs            — 接口定义
2. CharacterBuffComp.cs    — 具体实现
3. NoBuffComp.cs           — 空实现（Null Object）
4. BuffCompFactory.cs      — 抽象工厂
5. CharacterBuffFactory.cs — 具体工厂（ScriptableObject）
```

然后修改 **3 个已有文件**：

```
6. IEntityContext.cs       — 加 IBuffComp 属性
7. MAEntity.cs             — 加字段、Setter、Update 调用
8. CharacterMAFactoryTable — 加 BuffFactoryPath 列（DataTable）
```

---

## 第一步：定义接口

```csharp
// IBuffComp.cs
public interface IBuffComp : ICapability
{
    /// <summary>初始化，绑定宿主实体</summary>
    void Init(IEntityContext ctx);

    /// <summary>每帧驱动，在 MAEntity.Update 中调用</summary>
    void UpdateBuff(float deltaTime);

    /// <summary>添加 Buff</summary>
    void AddBuff(BuffData buff);

    /// <summary>移除 Buff</summary>
    void RemoveBuff(int buffId);
}
```

### 接口设计要点

- **必须继承 ICapability**，否则无法被 LockComp/ResumeComp 管理
- **必须有 Init(IEntityContext ctx)**，这是组件绑定宿主的唯一入口
- **必须有一个 per-frame 驱动方法**（如 UpdateBuff），名字自定，会被 MAEntity.Update 调用
- 方法签名要精简，只暴露 Brain 或外部系统需要调用的操作

---

## 第二步：具体实现

```csharp
// CharacterBuffComp.cs
public class CharacterBuffComp : IBuffComp
{
    private IEntityContext _ctx;
    private List<ActiveBuff> _activeBuffs = new();
    private bool _isShutDown = false;

    // ─── Init ───
    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        _activeBuffs.Clear();
        _isShutDown = false;
    }

    // ─── 核心逻辑 ───
    public void UpdateBuff(float deltaTime)
    {
        if (_isShutDown) return;  // ⚠️ 关键：被锁时跳过

        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = _activeBuffs[i];
            buff.remainingTime -= deltaTime;

            if (buff.remainingTime <= 0f)
            {
                RemoveBuffInternal(i);
            }
        }
    }

    public void AddBuff(BuffData buff) { /* ... */ }
    public void RemoveBuff(int buffId) { /* ... */ }

    // ─── ICapability ───
    public void ShutDown()
    {
        _isShutDown = true;
        // ⚠️ 如果 ShutDown 时你锁了别的组件，必须在这里释放！
    }

    public void Resume()
    {
        _isShutDown = false;
    }
}
```

### 实现要点

1. **ShutDown 时的清理是最容易出 bug 的地方**

   如果你的 Comp 在运行中锁了别的组件（比如 AtkComp 锁 MoveComp），ShutDown 时必须释放：
   ```csharp
   public void ShutDown()
   {
       // 如果正在执行中途被打断，必须释放所有持有的锁
       if (_isAttacking && _ctx != null)
       {
           _ctx.ResumeComp(_ctx.MoveComp, this);
       }
       _state = State.Idle;
   }
   ```
   **不释放 = 永久锁死对方组件 = Entity 卡住不动**

2. **UpdateXxx 方法开头检查 _isShutDown**

   被 LockComp 后 ShutDown() 会被调用，但 MAEntity.Update 仍然会调用你的 UpdateXxx。
   你自己要在方法开头 `if (_isShutDown) return;` 跳过逻辑。

3. **不要在 Comp 里缓存其他 Comp 的引用**

   通过 `_ctx.MoveComp`、`_ctx.AtkComp` 访问，不要 `private IMoveComp _move = ctx.MoveComp`。
   因为组件可能被异步加载，缓存可能拿到 null 或旧引用。

4. **构造函数注入数据，Init 绑定上下文**

   ```csharp
   // ✅ 正确：数据通过构造函数，上下文通过 Init
   public CharacterBuffComp(BuffConfig config) { _config = config; }
   public void Init(IEntityContext ctx) { _ctx = ctx; }

   // ❌ 错误：在构造函数里绑定上下文
   public CharacterBuffComp(IEntityContext ctx) { _ctx = ctx; }
   ```

---

## 第三步：Null Object 空实现

```csharp
// NoBuffComp.cs
public class NoBuffComp : IBuffComp
{
    public void Init(IEntityContext ctx) { }
    public void UpdateBuff(float deltaTime) { }
    public void AddBuff(BuffData buff) { }
    public void RemoveBuff(int buffId) { }
    public void ShutDown() { }
    public void Resume() { }
}
```

### 为什么需要

- MAEntity.Update 中直接 `_buffComp.UpdateBuff(dt)`，不需要判空
- 某些实体（沙袋、特效、装饰物）不需要 Buff 系统，给它一个 NoBuffComp 即可
- **每个 Comp 接口都必须有对应的 No*Comp**，这是项目约定

---

## 第四步：工厂

```csharp
// BuffCompFactory.cs — 抽象工厂基类
public abstract class BuffCompFactory : ScriptableObject
{
    public abstract IBuffComp CreateBuffComp(MAEntity entity);
}
```

```csharp
// CharacterBuffFactory.cs — 具体工厂
[CreateAssetMenu(fileName = "CharacterBuffFactory", menuName = "Buff Factory/Character")]
public class CharacterBuffFactory : BuffCompFactory
{
    [Header("Buff 配置")]
    [SerializeField] private float _maxBuffDuration = 30f;
    [SerializeField] private int _maxBuffStack = 5;

    public override IBuffComp CreateBuffComp(MAEntity entity)
    {
        var config = new BuffConfig
        {
            MaxDuration = _maxBuffDuration,
            MaxStack = _maxBuffStack,
        };

        var comp = new CharacterBuffComp(config);
        comp.Init(entity);
        entity.SetBuffComp(comp);  // ⚠️ 别忘了注册到 Entity
        return comp;
    }
}
```

### 工厂要点

- 抽象工厂继承 `ScriptableObject`，Create 方法接收 `MAEntity`
- 具体工厂加 `[CreateAssetMenu]`，在 Unity Inspector 里创建并配参
- 工厂内部：new 组件 → Init → SetXxxComp → return
- **工厂的 Inspector 字段就是策划调参的入口**，类型和范围要合理
- 空实现也需要工厂：`NoBuffFactory : BuffCompFactory`

---

## 第五步：挂载到 Entity

### 修改 IEntityContext（加属性）

```csharp
// IEntityContext.cs 中添加
IBuffComp BuffComp { get; }
```

### 修改 MAEntity（加字段 + Setter + Update 调用）

```csharp
// MAEntity.cs 中添加

// 字段
private IBuffComp _buffComp;

// 属性（IEntityContext 实现）
public IBuffComp BuffComp => _buffComp;

// Setter（供 Factory 调用）
public void SetBuffComp(IBuffComp comp) => _buffComp = comp;

// OnShow 中加载（注意顺序）
protected override void OnShow(object userData)
{
    base.OnShow(userData);
    // ... 已有的组件加载 ...
    FactoryHelper.CreateBuffComp(factoryRow.BuffFactoryPath, this);  // 新增
}

// Update 中调用（注意插入位置）
protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
{
    // ... 已有的 1-5 步 ...
    _buffComp?.UpdateBuff(elapseSeconds);    // 新增：第 6 步，在 AtkComp 之后
    // ... duration effect + execute ...
}
```

### Update 顺序很重要

```
1. GroupMoveManager sync     — 位置同步
2. Brain.Tick                — AI 决策
3. TargetingComp.Update      — 索敌
4. MoveComp.Move             — 移动
5. AtkComp.Attack            — 攻击
6. [你的新 Comp].Update      — ⬅️ 插在这里
7. DurationMoveEffect.Apply  — 持续移动效果
8. MoveExecutor.Execute      — 最终物理移动
```

原则：**先决策 → 再感知 → 再行动 → 最后物理执行**。你的 Comp 根据性质插到合适位置。

### 修改 FactoryHelper（加载方法）

```csharp
// FactoryHelper.cs 中添加
public static void CreateBuffComp(string factoryPath, MAEntity entity)
{
    if (string.IsNullOrEmpty(factoryPath))
    {
        entity.SetBuffComp(new NoBuffComp());  // 无配置时用空实现
        return;
    }
    // 走缓存 + 异步加载，同其他 Comp
}
```

### 修改 CharacterMAFactoryTable（DataTable 加列）

在数据表中加 `BuffFactoryPath` 列，指向具体工厂的资源路径。

---

## 常见坑 & 防御清单

| 坑 | 后果 | 防御 |
|----|------|------|
| ShutDown 时没释放持有的锁 | 其他组件永久锁死 | ShutDown 中检查并释放所有 LockComp 调用 |
| Update 方法没检查 _isShutDown | 被锁后仍在执行逻辑 | 方法开头 `if (_isShutDown) return;` |
| 缓存了其他 Comp 引用 | 异步加载导致 null 引用 | 始终通过 `_ctx.XxxComp` 访问 |
| 忘了写 NoXxxComp | 不需要此功能的实体空指针 | 每个接口必须有 Null Object |
| 忘了在 Factory 里调 SetXxxComp | Entity 拿不到组件 | Factory.Create 最后一步必须 Set |
| 忘了改 CharacterMAFactoryTable | 没有数据驱动的工厂路径 | DataTable 加列 + 填数据 |
| Update 顺序不对 | 逻辑时序错乱（如先攻击再索敌） | 参考已有顺序，插到合理位置 |
| FactoryHelper 没处理空路径 | 异步加载空字符串崩溃 | 空路径时直接 SetNoXxxComp |

---

## Comp 之间的互锁模式

如果你的新 Comp 需要锁定其他组件（比如施放 Buff 时锁定移动），遵循这个模式：

```csharp
// 锁定
_ctx.LockComp(_ctx.MoveComp, this);

// 做事...

// 释放（必须在同一组件中配对）
_ctx.ResumeComp(_ctx.MoveComp, this);
```

**规则**：
- 谁 Lock 谁 Resume，锁的 locker 参数是 `this`
- 多个组件可以同时锁同一个目标（引用计数），全部释放后才 Resume
- **永远不要在 Init 里 Lock**，只在运行时逻辑中 Lock
- **永远在 ShutDown 里检查并释放**
