# 项目继承链完整文档

> 由 4 个 Scanner Agent 并行扫描后合并生成
> 生成时间: 2026-03-22

---

## 总览

| 分类 | 数量 |
|------|------|
| ScriptableObject 类 (含抽象基类) | 27 |
| DataRowBase 派生表 | 18 |
| DataModel 类 (含基类) | 13 |
| Entity 类 (含抽象基类) | 12 |
| UIForm 类 (含基类) | 16 |
| UIItem 类 (含基类) | 7 |
| Procedure 类 | 9 |
| Brain 实现 | 5 |
| 组件 (Comp) 实现 | 11 |
| GameFrameworkComponent | 27 |
| 项目枚举 | 45 |
| 项目接口 | 20+ |
| GF 框架接口 | 40+ |
| **合计类/接口** | **约 250+** |

---

## 层级架构

```
┌──────────────────────┐
│       UI 层           │  UIForm, Widget, HUD, HealthBar
├──────────────────────┤
│     Logic 层          │  Procedure, Entity, Brain, Comp, Combat, EventArgs
├──────────────────────┤
│      Data 层          │  ScriptableObject, DataRow, DataModel, Enum
├──────────────────────┤
│    Framework 层       │  Manager, Singleton, Util, GF Extension, Helper
├──────────────────────┤
│  GameFramework (GF)   │  第三方框架 (Entity/UI/Sound/Resource/Procedure 等)
└──────────────────────┘
```

---

## 完整继承树

### 1. ScriptableObject 继承树

```
ScriptableObject (UnityEngine)
│
├── AppConfigs                                    [Infra] 游戏配置：数据表/语言/流程列表
├── AppSettings                                   [Infra] 应用设置：调试/资源/分辨率
├── BehaviorTreeGraph                             [Data]  行为树图：节点+连接
│
├── BasicSkill                                    [Data]  技能基类：动作列表/冷却
│   └── SelectPositionSkill                       [Data]  选择位置技能
│
├── BasicAction (abstract)                        [Data→Logic] 角色动作基类
│   ├── NormalAttackAction                        [Logic] 普通攻击：伤害窗口/HitBox
│   ├── ProjectileSpawnAction                     [Logic] 弹道生成
│   └── PositionSelectAction                      [Logic] 位置选择
│
├── AbstractNode (abstract)                       [Data→Logic] 行为树节点基类
│   ├── RootNode                                  [Logic] 根节点
│   ├── SelectorNode                              [Logic] 选择节点
│   ├── SequenceNode                              [Logic] 序列节点
│   ├── SetMessageNode                            [Logic] 设置消息节点
│   ├── CheckPriorityNode                         [Logic] 优先级检查节点
│   └── CheckHealthNode                           [Logic] 生命值检查节点
│
├── TargetingCompFactory (abstract)               [Data→Logic] 索敌组件工厂
│   ├── NoTargetingFactory                        [Logic] 空实现
│   └── CharacterTargetingFactory                 [Logic] 角色索敌：仇恨/遗忘/跟随距离
│
├── MoveCompFactory (abstract)                    [Data→Logic] 移动组件工厂
│   ├── NoMoveFactory                             [Logic] 空实现
│   ├── CharacterMoveFactory                      [Logic] 角色移动
│   └── PlayerMoveFactory                         [Logic] 玩家移动
│
├── AtkCompFactory (abstract)                     [Data→Logic] 攻击组件工厂
│   ├── NoAtkFactory                              [Logic] 空实现
│   ├── DirectAtkCompFactory                      [Logic] 直接攻击：伤害/距离/武器类型
│   ├── CharacterAtkFactory                       [Logic] 角色攻击：3段动作
│   └── PlayerAtkFactory                          [Logic] 玩家攻击：3段动作
│
└── SkillCompFactory (abstract)                   [Data→Logic] 技能组件工厂
    ├── PlayerSkillFactory                        [Logic] 玩家技能
    └── CharacterSkillFactory                     [Logic] 角色技能
```

### 2. Entity 继承树 (MonoBehaviour 系)

```
MonoBehaviour (Unity)
└── EntityLogic (GF框架, abstract)                [Infra] 实体逻辑基类
    └── EntityBase                                [Logic] 项目实体基类：Id/Params/位置/附着
        │
        ├── SampleEntity                          [Logic] 空壳：飘字等简单实体
        │   └── BillboardEntity                   [Logic] 自动面向摄像机
        │
        ├── ParticleEntity                        [Logic] 粒子特效：生命周期自动回收
        ├── LevelEntity                           [Logic] 关卡实体（空壳）
        ├── DeviceEntity                          [Logic] 建筑/设备：管理交互选项
        │
        ├── HitBox                                [Logic] 攻击判定盒：碰撞检测 HurtBox
        │
        ├── AbstractProjectile (abstract)         [Logic] 弹道基类：管理 HitBox 生命周期
        │   └── DirectionProjectile               [Logic] 方向弹道
        │
        ├── TargetableSelector : ISelector         [Logic] 区域目标选择器
        │
        └── GeneralCreature : ITargetable, ISelectable  [Logic] 生物基类：Side/血量/HurtBox
            └── CompCreature                      [Logic] 组件锁系统：LockComp/ResumeComp
                └── MAEntity ⚠️                   [Logic] **核心战斗实体**：Brain+Move+Atk+Target+Weapon
                    ├── SkillEntity               [Logic] 技能层：ISkillComp + CastRange
                    │   └── CharacterEntity       [Logic] 玩家角色：相机/Brain/GroupMove
                    ├── SoldierEntity             [Logic] 小兵：DirectAtk/NavMesh/GroupMove
                    └── PunchBagEntity            [Logic] 沙袋（测试用）
```

### 3. UIForm 继承树 (MonoBehaviour 系)

```
MonoBehaviour (Unity)
└── UIFormLogic (GF框架, abstract)                [Infra] UI表单逻辑基类
    └── UIFormBase : ISerializeFieldTool          [UI]   项目UI基类：动画/子界面/对象池/ESC/多语言
        │
        ├── [UIForm 组 - 全屏界面]
        │   ├── MenuUIForm                        [UI] 主菜单：金币/设置入口
        │   ├── GameUIForm                        [UI] 游戏内HUD：金币显示
        │   ├── GameOverUIForm                    [UI] 游戏结算：胜利/失败
        │   ├── ShopUIForm                        [UI] 商店（未启用，代码注释）
        │   ├── UITopbar                          [UI] 顶部资源栏：金币/钻石/能量
        │   └── MaterialModifyBar                 [UI] 材料编辑栏（测试用）
        │
        ├── [Dialog 组 - 对话框]
        │   ├── SettingDialog                     [UI] 设置：音量/振动/语言/评分
        │   ├── RatingDialog                      [UI] 评分：5星+商店跳转
        │   ├── CommonDialog                      [UI] 通用确认对话框
        │   ├── LanguagesDialog                   [UI] 语言选择列表
        │   ├── TechTreeDialog                    [UI] 科技树：节点状态+详情
        │   ├── TechNodeDetailTips                [UI] 科技节点详情
        │   └── InteractionPanel                  [UI] 交互面板基类
        │       └── CraftingDialog                [UI] 合成面板：配方/材料/长按制造
        │
        └── [Tips 组 - 提示]
            ├── ToastTips                         [UI] 吐司提示：多样式/自动关闭
            └── InteractOptionTips                [UI] 交互选项：世界跟随/快捷键/花费
```

### 4. UIItem 继承树

```
MonoBehaviour (Unity)
└── UIItemBase : ISerializeFieldTool              [UI] 列表项基类：Init/多语言/自动绑定
    ├── LanguageItem                              [UI] 语言列表项
    ├── CraftingUnit                              [UI] 合成配方单元
    ├── ItemUnit                                  [UI] 物品单元：图标/数量
    ├── InteractOptionUnit                        [UI] 交互选项单元
    ├── ItemModifyUnit                            [UI] 材料编辑单元（测试用）
    └── TextLineUnit                              [UI] 文本行单元

ObjectBase (GF框架对象池)
└── UIItemObject                                  [UI] UI Item 对象池包装器
```

### 5. UI 辅助组件 (非 UIFormLogic 体系)

```
MonoBehaviour
├── HealthBarComp                                 [UI→Logic] 世界空间血条：事件驱动
├── HoldProgress                                  [UI] 长按充能进度条
├── UIFollowWorldPoint                            [UI] RectTransform → 世界坐标投影
├── InteractOptionTipsPresenter                   [UI→Logic] 交互焦点事件 → Tips 生命周期
└── CustomUIGroupHelper                           [Infra] UIGroup Canvas/Raycaster 设置
```

### 6. Procedure 继承树

```
FsmState<IProcedureManager>
└── ProcedureBase (GF框架, abstract)              [Infra] 流程基类
    ├── LaunchProcedure                           [Infra] 启动入口
    ├── UpdateResourcesProcedure                  [Infra] 资源热更新
    ├── LoadHotfixDllProcedure                    [Infra] 加载 HybridCLR 热更 DLL
    ├── PreloadProcedure                          [Logic] 预加载 DataTable/Config/Language
    ├── ChangeSceneProcedure                      [Logic] 场景切换中转站
    ├── MenuProcedure                             [Logic] 主菜单（当前被跳过）
    ├── GameProcedure                             [Logic] 主游戏流程（空壳）
    ├── GameOverProcedure                         [Logic] 游戏结束（空壳）
    ├── CharacterTestProcedure ⚠️                 [Logic] 角色测试（当前默认入口）
    └── LevelTestProcedure                        [Logic] 关卡测试
```

**流程切换关系：**
```
LaunchProcedure
  ├──[EditorMode]──→ LoadHotfixDllProcedure
  └──[非Editor]──→ UpdateResourcesProcedure ──→ LoadHotfixDllProcedure
                                                        │
                                                        ▼
                                                  PreloadProcedure
                                                        │
                                                        ▼
                                                ChangeSceneProcedure
                                                   ├──["Game"]──→ CharacterTestProcedure (当前默认)
                                                   └──["LevelTestScene"]──→ LevelTestProcedure
```

### 7. Brain 体系

```
IControlBrain (接口)                              [Logic] 决策输出：Move/Attack/Skill
│
├── PlayerBrain                                   [Logic] 从 InputModel 读取输入
├── SoldierAIBrain : IControlBrain + ITickBrain   [Logic] 小兵AI：Idle→Follow→Combat 状态机
├── EnemyAIBrain : IControlBrain + ITickBrain     [Logic] 敌方AI：仇恨追击（标记 not used）
├── FriendlyAIBrain : IControlBrain + ITickBrain  [Logic] 友军AI：优先攻击→跟随（标记 not used）
└── ScriptedBrain : IControlBrain + ITickBrain    [Logic] 测试用脚本化Brain
```

### 8. 组件体系 (Comp)

```
ICapability (接口)                                [Infra] 组件开关：ShutDown/Resume
│
├── IMoveComp : ICapability                       [Logic] 移动组件
│   ├── CharacterMoveComp                         [Logic] NavMesh寻路+手动输入+动画驱动
│   └── NoMoveComp                                [Logic] 空实现
│
├── IAtkComp : ICapability                        [Logic] 攻击组件
│   ├── DirectAtkComp                             [Logic] 直接伤害：WindUp→DealDamage→WindDown→Cooldown
│   └── NoAtkComp                                 [Logic] 空实现
│
├── ITargetingComp : ICapability                  [Logic] 索敌组件
│   ├── CharacterTargetingComp                    [Logic] 降频扫描 EntityRegistry
│   └── NoTargetingComp                           [Logic] 空实现
│
├── ISkillComp : ICapability                      [Logic] 技能组件
│   ├── CharacterSkillComp                        [Logic] 3技能槽位：冷却/输入/组件互锁
│   └── PlayerSkillComp                           [Logic] 玩家技能
│
├── WeaponComp : ICapability                      [Logic] 纯数据：WeaponData + AttackRange
└── DurationMoveEffectComp                        [Logic] 持续移动效果（击退等）

IMoveExecutor (接口)                              [Logic] 底层移动执行器
└── MoveExecutor                                  [Logic] input+external+override → CharacterController.Move
```

### 9. DataTable / DataRow 继承树

```
IDataRow (GF框架接口)
└── DataRowBase (GF框架抽象基类)
    ├── [Core]
    │   ├── EntityGroupTable                      [Data] 实体组配置
    │   ├── UITable                               [Data] UI界面配置
    │   ├── UIGroupTable                          [Data] UIGroup配置
    │   ├── SoundGroupTable                       [Data] 音效组配置
    │   └── LanguagesTable                        [Data] 语言配置
    ├── [Text]
    │   ├── InteractOptionTextTable               [Data] 交互选项文本
    │   └── LocalizationTextTable                 [Data] 本地化文本
    ├── [Item]
    │   ├── ItemTable                             [Data] 物品定义
    │   ├── ItemWithEffectTable                   [Data] 带效果物品
    │   └── CraftingFormulaTable                  [Data] 合成配方
    ├── [Build]
    │   └── DeviceTable                           [Data] 建筑/设备
    ├── [Tech]
    │   └── TechNodeTable                         [Data] 科技节点
    ├── CharacterMainPropertyTable                [Data] 角色主属性
    ├── CharacterMAFactoryTable                   [Data] 角色组件工厂路径
    ├── CameraViewTable                           [Data] 相机视角
    ├── ColorTable                                [Data] 颜色表
    ├── CombatUnitTable                           [Data] 战斗单元
    └── LevelTable                                [Data] 关卡
```

### 10. DataModel 继承树

```
IReference (GF框架接口)
└── DataModelBase (abstract)                      [Infra] 数据模型基类：Id/Userdata
    │
    ├── DataModelStorageBase (abstract)            [Infra] 支持 JSON 持久化
    │   ├── PlayerDataModel                       [Data] 玩家数据：Hp/Coins/LevelId
    │   ├── ProfileDataModel                      [Data] 档案数据：BaseLevel
    │   ├── ItemCollectionDataModel               [Data] 物品持有量
    │   ├── TechProgressDataModel                 [Data] 科技解锁进度
    │   └── CapabilityProgressDataModel           [Data] 能力解锁进度
    │
    ├── InputModel                                [Logic] 输入状态容器：Move/Attack/Skill/Interaction
    ├── DeviceDataModel                           [Data] 设备定义数据
    ├── ItemDataModel                             [Data] 物品定义数据
    ├── CraftingDeviceDataModel                   [Data] 制造台数据
    ├── TechNodeDataModel                         [Data] 科技节点定义
    └── LocalizationTextDataModel                 [Data] 本地化文本
```

### 11. GameFrameworkComponent 继承树

```
MonoBehaviour (Unity)
└── GameFrameworkComponent (abstract)             [Infra] GF框架组件基类
    │
    ├── [GF 框架组件 - 20个]
    │   ├── BaseComponent (sealed)                帧率/游速/Helper初始化
    │   ├── ConfigComponent                       全局配置键值对
    │   ├── DataNodeComponent                     层级数据节点
    │   ├── DataTableComponent                    数据表管理
    │   ├── DebuggerComponent                     调试器UI
    │   ├── DownloadComponent                     文件下载
    │   ├── EntityComponent                       实体生命周期
    │   ├── EventComponent                        全局事件分发
    │   ├── FileSystemComponent                   虚拟文件系统
    │   ├── FsmComponent                          有限状态机
    │   ├── LocalizationComponent                 多语言
    │   ├── NetworkComponent                      网络通信
    │   ├── ObjectPoolComponent                   对象池
    │   ├── ProcedureComponent                    流程管理
    │   ├── ReferencePoolComponent                引用池调试
    │   ├── ResourceComponent                     资源加载
    │   ├── SceneComponent                        场景管理
    │   ├── SettingComponent                      持久化设置
    │   ├── SoundComponent                        音频管理
    │   ├── UIComponent                           UI管理
    │   └── WebRequestComponent                   HTTP请求
    │
    ├── [项目扩展组件 - 7个]
    │   ├── BuiltinViewComponent                  内建UI视图(加载/对话框)
    │   ├── DataModelComponent                    数据模型管理
    │   ├── StaticUIComponent                     静态UI(摇杆等)
    │   ├── VariablePoolComponent                 变量池(Entity参数传递)
    │   ├── ADComponent                           广告SDK集成（已注释）
    │   └── InputManager                          输入系统管理
    │
    └── (通过 GFBuiltin/GF 静态门面访问)
```

### 12. Manager / Singleton 继承树

```
MonoBehaviour
├── Singleton<T> : MonoBehaviour                  [Infra] 泛型单例基类（DontDestroyOnLoad）
│   └── MessageSystem                             [Infra] 全局消息总线：Register/Send
│
├── GroupMoveManager (手动单例) ⚠️                [Logic] ORCA群体移动协调
├── InteractionManager                            [Logic] 交互目标选择/评分
├── CameraController (手动单例)                   [Logic] Cinemachine 虚拟相机管理
│
└── (非 MonoBehaviour)
    └── IReference
        └── PropertyManager                       [Logic] 属性注册表
```

### 13. 事件参数继承树

```
GameFrameworkEventArgs (abstract)                 [Infra] 框架事件参数
└── BaseEventArgs (abstract)                      [Infra] 带 Id 的事件参数
    │
    ├── GameEventArgs (abstract)                  [Infra] 游戏事件参数标记
    │   ├── CreatureHealthChangedEventArgs        [Logic] 生物血量变化
    │   ├── GameplayEventArgs                     [Logic] 游戏事件（GameOver）
    │   ├── GFEventArgs                           [Logic] 框架事件（ApplicationQuit）
    │   ├── PlayerDataChangedEventArgs            [Data]  玩家数据变化
    │   ├── ProfileDataChangedEventArgs           [Data]  档案数据变化
    │   ├── ItemAmountChangedEventArgs            [Data]  物品数量变化
    │   ├── InteractionFocusChangedEventArgs      [Logic] 交互焦点变化
    │   ├── InteractionOptionTriggeredEventArgs   [Logic] 交互选项触发
    │   ├── TechUnlockedEventArgs                 [Logic] 科技解锁
    │   └── TechResearchFailedEventArgs           [Logic] 科技研究失败
    │
    └── Packet (abstract)                         [Infra] 网络包基类
        └── PacketBase : IExtensible              [Infra] ProtoBuf 网络包
            ├── CSPacketBase                      [Infra] 客户端→服务端
            └── SCPacketBase                      [Infra] 服务端→客户端
```

### 14. 属性系统继承树

```
IProperty<Fix64>
└── ValueProperty (abstract)                      [Logic] 数值属性基类
    ├── BaseValueProperty                         [Logic] 基础值属性
    ├── ComputeValueProperty                      [Logic] 计算属性（依赖其他属性）
    └── IrreversibleValueProperty                 [Logic] 不可逆属性
```

### 15. 变量继承树

```
Variable (abstract) : IReference                  [Infra] 变量基类
└── Variable<T> (abstract)                        [Infra] 泛型变量
    └── (VarBool, VarInt32, VarString, VarFloat, VarVector2, VarAction, VarObject 等)
```

### 16. 其他继承链

```
AbsStatemachine<T1, T2> (abstract, generic)       [Infra] 泛型状态机
└── InputSM                                       [Logic] 输入状态机

GameFrameworkSerializer<T> (abstract, generic)     [Infra] 版本化序列化器
└── (PackageVersionListSerializer 等)

ObjectBase (abstract) : IReference                [Infra] 池化对象基类
└── (EntityInstanceObject, UIFormInstanceObject, UIItemObject 等)

TaskBase (abstract) : IReference                  [Infra] 异步任务基类
└── (DownloadTask, WebRequestTask, LoadResourceTaskBase 等)
```

---

## 接口清单

### GF 框架核心接口

| 接口名 | 层级 | 关键方法 |
|--------|------|---------|
| `IReference` | Infra | `Clear()` |
| `IDataRow` | Infra | `int Id`, `ParseDataRow()` |
| `IEntity` | Infra | `OnInit/OnShow/OnHide/OnUpdate` |
| `IUIForm` | Infra | `OnInit/OnOpen/OnClose/OnPause/OnResume` |
| `IEntityGroup` | Infra | `HasEntity`, `GetEntity`, `GetAllEntities` |
| `IUIGroup` | Infra | `HasUIForm`, `GetUIForm`, `CurrentUIForm` |
| `IFsm<T>` | Infra | `Owner`, `CurrentState`, `Start<TState>` |
| `IObjectPool<T>` | Infra | `Spawn`, `Unspawn`, `Register`, `Release` |
| `IDataTable<T>` | Infra | `this[int]`, `HasDataRow`, `GetAllDataRows` |
| `INetworkChannel` | Infra | `Connect`, `Close`, `Send<T>` |
| `IDataProvider<T>` | Infra | `ReadData`, `ParseData` |
| `ITaskAgent<T>` | Infra | `Initialize`, `Start`, `Update`, `Shutdown` |

### GF Manager 接口

| 接口名 | 层级 | 用途 |
|--------|------|------|
| `IEntityManager` | Infra | 实体 Show/Hide/Attach |
| `IUIManager` | Infra | UI 表单生命周期 |
| `IEventManager` | Infra | 全局事件发布/订阅 |
| `IFsmManager` | Infra | 有限状态机管理 |
| `IProcedureManager` | Infra | 游戏流程管理 |
| `IResourceManager` | Infra | 资源异步加载/卸载 |
| `ISceneManager` | Infra | 场景加载/卸载 |
| `ISoundManager` | Infra | 音频播放 |
| `IObjectPoolManager` | Infra | 通用对象池 |
| `INetworkManager` | Infra | 网络频道管理 |
| `IDataTableManager` | Infra | 数据表加载/查询 |
| `IConfigManager` | Infra | 全局配置 |
| `ILocalizationManager` | Infra | 多语言 |
| `ISettingManager` | Infra | 持久化设置 |
| `IDownloadManager` | Infra | 下载任务管理 |
| `IWebRequestManager` | Infra | HTTP请求管理 |

### GF Helper 接口（依赖注入点）

| 接口名 | 层级 |
|--------|------|
| `Utility.Text.ITextHelper` | Infra |
| `Utility.Json.IJsonHelper` | Infra |
| `Utility.Compression.ICompressionHelper` | Infra |
| `GameFrameworkLog.ILogHelper` | Infra |
| `IEntityHelper` / `IEntityGroupHelper` | Infra |
| `IUIFormHelper` / `IUIGroupHelper` | Infra |
| `ISoundHelper` / `ISoundAgentHelper` | Infra |
| `ISettingHelper` / `IConfigHelper` | Infra |
| `ILocalizationHelper` | Infra |
| `INetworkChannelHelper` | Infra |
| `IDataTableHelper` / `IDataProviderHelper<T>` | Infra |
| `IDownloadAgentHelper` / `IWebRequestAgentHelper` | Infra |
| `IResourceHelper` / `ILoadResourceAgentHelper` | Infra |
| `IFileSystemHelper` | Infra |

### 项目业务接口

| 接口名 | 层级 | 关键方法 |
|--------|------|---------|
| `IEntityContext` | Logic | Position, Side, Alive, Brain, MoveExecutor, MoveComp, AtkComp, TargetComp, TakeDamage |
| `IControlBrain` | Logic | Move, Attack, Skill1/2/3 |
| `ITickBrain` | Logic | Tick(IEntityContext, float) |
| `IMoveComp` | Logic | Init, Move, MoveTo, StopMove, GetNavDirection |
| `IMoveExecutor` | Logic | SetInput, AddExternal, SetOverride, Execute |
| `IAtkComp` | Logic | Init, Attack |
| `ITargetingComp` | Logic | Init, UpdateTargeting, CurrentTarget, FollowTarget |
| `ISkillComp` | Logic | Init, Skill |
| `ICapability` | Infra | ShutDown, Resume |
| `IDurationMoveEffectComp` | Logic | StartDurationAdditionalMove, ApplyEffect |
| `ISelector` / `ISelector<T>` | Logic | Activate, GetSelected, ClearSelected |
| `ISelectable` | Logic | CanBeSelected, InSelection, DeSelection |
| `ISkillLocker` | Logic | RecordLockedSkill, DirectUnlockAll |
| `IInteractionOption` | Logic | Init, DisplayName, CostMaterial, IsExecutable, Execute |
| `IProperty` / `IProperty<T>` | Logic | GetValue, AddModifier, RemoveModifier |
| `IPropertyModifier` | Logic | Priority |
| `ISerializeFieldTool` | UI | 序列化字段绑定工具接口 |

---

## 枚举清单

### 核心分组枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `EntityGroup` | Data | Default, Player, Effect, Item, Level, Bullet, Unrecycle, Building |
| `UIGroup` | Data | UIForm, Dialog, Tips |
| `SoundGroup` | Data | Music, Sound, Vibrate, Joystick |

### 战斗 & 角色属性枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `CreatureMainProperty` | Logic | PhysicalAtk, SpecialAtk, PhysicalDef, SpecialDef, Health, Speed, Mana |
| `CreatureMinorProperty` | Logic | HealthRecover, ManaRecover |
| `CreatureCurrentProperty` | Logic | HealthCurrent, ManaCurrent |
| `WeaponType` | Logic | Melee, Projectile, InstantRanged |
| `AtkState` | Logic | Idle, WindUp, WindDown, Cooldown |
| `HealthModifyType` | Logic | reduce, mult, set, empty |
| `SideType` | Logic | NoSide, PlayerSide, EnemySide |
| `CoolDownType` | Logic | Count |
| `EModifierMergeType` | Logic | DirectAdditive, FinalAdditive, IrreversibleAdditive, ... (13种) |

### 数据模型枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `PlayerDataType` | Data | Coins, Diamond, Hp, Energy, LevelId |
| `ProfileDataType` | Data | BaseLevel |
| `InputKey` | Data | InteractionPrimary(0), InteractionSecondary(1), InteractionTertiary(2) |

### 物品 & 制造枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `ItemRarity` | Data | Common, Rare, Epic, Legendary, Heroic |
| `ItemType` | Data | Consumable, Equipment, Material, Tool |
| `ItemTag` | Data | AAA, BBB, CCC, DDD |

### 科技 & 解锁枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `TechCategory` | Data | Explore(0), Fight(1), Craft(2), Efficiency(3), Profit(4) |
| `UnlockConditionType` | Data | BaseLevel(0), Tech(1), Capability(2), Dialogue(3) |
| `TechResearchFailReason` | Logic | None(0), InvalidTechId(1), ... (10种) |

### AI & 移动枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `BrainType` | Logic | Player(0), EnemyAI(1), FriendlyAI(2), SoldierAI(3) |
| `SoldierState` | Logic | Idle, Follow, Combat |
| `AgentState` | Logic | Idle, Follow, Combat |
| `NodeState` | Logic | Success, Failure |
| `EntityPresetPointType` | Logic | Spawn, Respawn, Patrol, Device |

### UI & 输入枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `UIViews` | UI | MenuUIForm(1), GameUIForm(2), ... (15个界面) |
| `InputState` | Logic | None, StartScreen, Game, UIForm |
| `PivotAxis` | Logic | Free, X, Y |
| `ToastStyle` | UI | Blue(0), Yellow(1), Green(2), Red(3), White(4) |

### 事件枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `GameplayEventType` | Logic | GameOver |
| `GFEventType` | Logic | ApplicationQuit |
| `TempActions` | Logic | KillPlayer |

### 网络 & 广告枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `PacketType` | Infra | Undefined(0), ClientToServer(1), ServerToClient(2) |
| `ADResult` | Infra | Open, Close |
| `ADTriggerPoint` | Infra | CloseUpgradeView, CollectMoneyStack, ... (6个) |

### 调试枚举

| 枚举名 | 层级 | 值 |
|--------|------|---|
| `DebugCategory` | Logic | Targeting, Attack, Move, Brain, GroupMove |

---

## 层间依赖分析

### 依赖矩阵

|          | Data | Logic | UI | Infra |
|----------|:----:|:-----:|:--:|:-----:|
| **Data**  | -    | ✅    | ❌ | ✅    |
| **Logic** | ✅   | -     | ❌ | ✅    |
| **UI**    | ✅   | ✅    | -  | ✅    |
| **Infra** | ❌   | ❌    | ❌ | -     |

**说明：**
- ✅ = 有依赖（合理）
- ❌ = 无依赖（正确）

### 依赖详情

#### Data → Logic（合理：工厂模式跨层）
- `BasicAction` (ScriptableObject) 在 Data 层定义，被 Logic 层的 `DirectAtkComp`、`CharacterAtkFactory` 引用
- 各 `CompFactory` (ScriptableObject) 在 Data 层定义，在 Logic 层创建具体组件实例
- `AbstractNode` 行为树节点在 Data 层定义，被 Logic 层的 Brain 系统执行

#### Data → Infra（合理：使用框架基类）
- `DataRowBase` 继承 GF 框架接口 `IDataRow`
- `DataModelBase` 实现 GF 框架接口 `IReference`

#### Logic → Data（合理：读取配置）
- `CharacterTestProcedure` 读取 `CombatUnitTable`、`CharacterMAFactoryTable`
- `MAEntity` 通过 `FactoryHelper` 异步加载 ScriptableObject 工厂
- `SoldierAIBrain` 使用 `AgentState` 等枚举

#### Logic → Infra（合理：使用框架能力）
- 所有 Entity 继承 `EntityLogic` (GF 框架)
- 所有 Procedure 继承 `ProcedureBase` (GF 框架)
- 通过 `GF.Event` 发布事件，通过 `GF.Entity` 管理实体生命周期

#### UI → Data（合理：读取数据显示）
- `TechTreeDialog` 读取 `TechProgressDataModel`、`TechNodeDataModel`
- `CraftingDialog` 读取 `CraftingDeviceDataModel`、`ItemCollectionDataModel`
- `MenuUIForm`/`UITopbar` 读取 `PlayerDataModel`
- 各 UI 读取 `ItemTable`、`LanguagesTable` 等 DataTable

#### UI → Logic（合理：事件驱动）
- `HealthBarComp` 监听 `CreatureHealthChangedEventArgs`
- `InteractOptionTipsPresenter` 监听 `InteractionFocusChangedEventArgs`
- 各 UI 通过 `GF.Event.Subscribe` 监听 Logic 层事件
- `TechNodeDetailTips` 调用 `TechProgressDataModel.Research()`（注意：这是 UI 直接调用 Data 层写操作）

#### UI → Infra（合理：使用框架能力）
- 所有 UIForm 继承 `UIFormLogic` (GF 框架)
- 使用 `GF.UI`、`GF.ObjectPool`、`GF.Event` 等框架组件

#### Infra → 其他层 = ❌（正确：框架层不反向依赖业务层）

### 不合理依赖（架构风险）

#### ⚠️ 风险 1：UI 层直接调用 Data 层写操作
- **位置**: `TechNodeDetailTips` → `TechProgressDataModel.Research()`
- **问题**: UI 层直接触发数据模型的业务逻辑（研究科技），绕过了 Logic 层
- **建议**: 应通过事件或 Logic 层中介来执行，UI 层只负责展示和输入采集

#### ⚠️ 风险 2：CharacterTestProcedure 作为生产入口
- **位置**: `CharacterTestProcedure` 是当前默认游戏流程入口
- **问题**: 类名和设计意图是"测试"，但承担了生产环境的实体创建、血条注册等核心职责
- **建议**: 正式的 `GameProcedure` 应承担这些职责

#### ⚠️ 风险 3：MAEntity God Class
- **位置**: `EntityLogic → EntityBase → GeneralCreature → CompCreature → MAEntity`
- **问题**: 5层继承链，同时是组件容器+更新驱动器+Brain宿主+注册点，`Update()` 承担 7 个步骤
- **建议**: 拆分 Update 驱动为独立的 `EntityUpdateDriver`

#### ⚠️ 风险 4：GroupMoveManager 职责过载
- **位置**: `Scripts/Movement/GroupMoveManager.cs`
- **问题**: 同时承担 Inspector 调参、PlayerPrefs 持久化、障碍物管理、Agent 桥接、LateUpdate 驱动、Gizmos 绘制
- **建议**: 拆分调参面板、持久化、Gizmos 为独立的 Editor/Debug 类

#### ⚠️ 风险 5：SoldierAIBrain 逻辑密集
- **位置**: `Scripts/Movement/SoldierAIBrain.cs` (~346 行)
- **问题**: 状态机+死区逻辑+ORCA交互+NavMesh委托+攻击判定全部混在一起
- **建议**: 将每个状态的 Tick 逻辑拆为独立的策略类

---

## 各层详细信息

### Data 层

#### ScriptableObject 详细字段

| 类名 | 关键字段 | 文件路径 |
|------|---------|---------|
| AppConfigs | m_LoadFromBytes, mDataTables[], mConfigs[], mLanguages[], mProcedures[] | `Scripts/ScriptableObject/AppConfigs.cs` |
| AppSettings | DebugMode, ResourceMode, CheckVersionUrl, DesignResolution, EncryptAOTDlls[] | `ScriptsBuiltin/Runtime/ScriptableObject/AppSettings.cs` |
| BehaviorTreeGraph | root(RootNode), nodes[], connections[], boundContextTypeName | `Scripts/BehaviorTree/BehaviorTreeGraph.cs` |
| BasicSkill | actions(List\<BasicAction\>), coolDownType, coolDownInterval, banWhenOtherSkill, radius | `Scripts/SkillSystem/BasicSkill.cs` |
| BasicAction | ifUseDuration, duration, acceptInputFromPercent, relatedTriggerString | `Scripts/ActionSystem/BasicAction.cs` |
| NormalAttackAction | damageStartPercent, damageEndPercent, hitboxPrefab, hitboxScale, relativeOffset | `Scripts/ActionSystem/NormalAttackAction.cs` |
| DirectAtkCompFactory | damage, attackInterval, weaponType, attackRange, projectileSpeed, windUp, windDown, splashRadius, manaCost | `Scripts/GeneralCreature/DirectAtkCompFactory.cs` |
| CharacterTargetingFactory | defaultAggroRange, defaultForgetRange, defaultFollowRange | `Scripts/PlayerControl/CharacterTargetingFactory.cs` |

#### DataRow 字段列表

| 表名 | 字段 | 文件路径 |
|------|------|---------|
| EntityGroupTable | Id, Name, ReleaseInterval, Capacity, ExpireTime, Priority | `Scripts/DataTable/Core/EntityGroupTable.cs` |
| UITable | Id, SortOrder, UIPrefab, PauseCoveredUI, UIGroupId, EscapeClose, BlockCharacterControl | `Scripts/DataTable/Core/UITable.cs` |
| ItemTable | Id, Identifier, Rarity(ItemRarity), MaxStack, SpriteName, NameKey, DescriptionKey, Tags(ItemTag[]) | `Scripts/DataTable/Item/ItemTable.cs` |
| CombatUnitTable | Id, PrefabName, AttackRadius, MoveSpeed, Hp, Damage, MaxAttackCount | `Scripts/DataTable/CombatUnitTable.cs` |
| CharacterMAFactoryTable | Id, CharacterKey, MoveFactoryPath, AttackFactoryPath | `Scripts/DataTable/CharacterMAFactoryTable.cs` |
| DeviceTable | Id, Identifier, CostMaterial, Workload, PrefabName, BuildCondition, UpgradeID, InteractionPanelID | `Scripts/DataTable/Build/DeviceTable.cs` |
| TechNodeTable | Id, Identifier, PrereqTechIds, Level, Category, CostMaterial, AllConditions, AnyConditions | `Scripts/DataTable/Tech/TechNodeTable.cs` |

#### DataModel 字段列表

| 类名 | 基类 | 持久化 | 关键字段 |
|------|------|--------|---------|
| PlayerDataModel | DataModelStorageBase | ✅ | Dict\<PlayerDataType,int\>, Hp, Coins, LevelId |
| ProfileDataModel | DataModelStorageBase | ✅ | Dict\<ProfileDataType,int\>, BaseLevel |
| ItemCollectionDataModel | DataModelStorageBase | ✅ | Dict\<string,int\> |
| TechProgressDataModel | DataModelStorageBase | ✅ | HashSet\<string\>, List\<string\>, Dict\<string,long\> |
| CapabilityProgressDataModel | DataModelStorageBase | ✅ | HashSet\<string\> |
| InputModel | DataModelBase | ❌ | MoveX/Y, Attack, Skill1-3, InteractionPressed |
| ItemDataModel | DataModelBase | ❌ | Dict\<string,Item\> |
| DeviceDataModel | DataModelBase | ❌ | Dict\<string,Device\> |
| CraftingDeviceDataModel | DataModelBase | ❌ | Dict\<string,List\<CraftingFormula\>\> |
| TechNodeDataModel | DataModelBase | ❌ | Dict\<string,TechNodeTable\> |
| LocalizationTextDataModel | DataModelBase | ❌ | Dict\<string,string\> |

### Logic 层

#### 关键调用关系

**MAEntity Update 循环 (每帧):**
```
MAEntity.Update()
  ├── 1. GroupMoveManager.UpdateAgentPosition(this)     同步位置到协调器
  ├── 2. Brain.Tick(this, dt)                           AI 决策
  ├── 3. targetComp.UpdateTargeting(dt)                 索敌
  ├── 4. moveComp.Move(dt)                              移动
  ├── 5. atkComp.Attack(dt)                             攻击
  ├── 6. durationMoveEffectComp.ApplyEffect(dt)         持续移动效果
  └── 7. moveExecutor.Execute()                         最终执行移动
```

**GroupMove 协调流程 (LateUpdate):**
```
GroupMoveManager.LateUpdate()
  └── Coordinator.Resolve()
        ├── 遍历所有 pending VelocityRequest
        ├── ComputeSafeVelocity: LJ同组力 + LJ跨组力 + 敌对力 + 障碍物斥力
        └── 回调 Brain.ApplyVelocity(safeVelocity)
```

**伤害流程:**
```
Brain.Attack = true
  → AtkComp → target.TakeDamage()
    → GeneralCreature.TakeDamage()
      ├── Animator.SetTrigger("GetHit")
      ├── CreaturePropertyManager.ModifyCurrentProperty(HealthCurrent, -damage)
      ├── GF.Event.Fire(CreatureHealthChangedEventArgs)
      └── if (health <= 0) → GF.Entity.HideEntity(Id)
```

**实体创建流程:**
```
Procedure.OnEnter()
  └── GF.Entity.ShowEntity<T>(prefabName, group, entityParams)
        └── [GF框架异步加载Prefab]
              └── EntityLogic.OnInit() → OnShow()
                    ├── EntityBase: 解析 EntityParams
                    ├── GeneralCreature: Side/Alive/HurtBox/PropertyManager
                    ├── CompCreature: 初始化锁表
                    ├── MAEntity: FactoryHelper 加载组件, 注册 EntityRegistry
                    └── CharacterEntity/SoldierEntity: Brain创建, GroupMove注册
```

#### 自定义事件清单

| 事件类 | 关键字段 | 触发者 → 监听者 |
|--------|---------|----------------|
| CreatureHealthChangedEventArgs | EntityId, CurrentHealth, MaxHealth, Delta | GeneralCreature → HealthBarComp |
| GameplayEventArgs | EventType(GameOver), Params | 游戏流程 → UI层 |
| PlayerDataChangedEventArgs | DataType, OldValue, Value | PlayerDataModel → MenuUIForm, UITopbar |
| ProfileDataChangedEventArgs | DataType, OldValue, Value | ProfileDataModel → TechTreeDialog |
| ItemAmountChangedEventArgs | ItemIdentifier, OldValue, Value | 物品系统 → CraftingDialog, MaterialModifyBar, TechTreeDialog |
| InteractionFocusChangedEventArgs | Target(InteractionHost) | InteractionManager → InteractOptionTipsPresenter |
| InteractionOptionTriggeredEventArgs | Target, Option | InteractionHost → InteractOptionTips |
| TechUnlockedEventArgs | TechId | 科技系统 → TechTreeDialog |
| TechResearchFailedEventArgs | TechId, Reason | 科技系统 → UI层 |

### UI 层

#### UI 与逻辑层交互模式

1. **事件驱动（主流）**: UI 在 OnOpen 时 Subscribe, OnClose 时 Unsubscribe GF.Event
2. **DataModel 直接读取**: 通过 `GF.DataModel.GetOrCreate<T>()` 获取数据
3. **DataTable 查询**: 通过 `GF.DataTable.GetDataTable<T>()` 读取配置
4. **UIParams 参数传递**: 打开界面时通过键值对传递参数
5. **回调模式**: OpenCallback / CloseCallback / ButtonClickCallback
6. **子界面模式**: OpenSubUIForm 打开子界面，随父界面关闭
7. **Presenter 模式**: InteractOptionTipsPresenter 作为逻辑与UI中介

#### UIGroup 分组

| UIGroup | Depth | 用途 | 包含的界面 |
|---------|-------|------|-----------|
| UIForm | 1 | 全屏界面 | MenuUIForm, GameUIForm, GameOverUIForm, UITopbar, MaterialModifyBar |
| Dialog | 200 | 对话框 | SettingDialog, RatingDialog, CommonDialog, LanguagesDialog, CraftingDialog, TechTreeDialog, TechNodeDetailTips, InteractionPanel |
| Tips | 500 | 提示 | ToastTips, InteractOptionTips |

### Infra 层

#### GF 双层架构

```
GF (静态门面) → GameFrameworkComponent (Unity层) → GameFrameworkModule (C#纯逻辑层)
```

- **C# 层**: `GameFrameworkModule` 子类（EntityManager, UIManager 等），不依赖 Unity API
- **Unity 层**: `GameFrameworkComponent` 子类（EntityComponent, UIComponent 等），封装为 MonoBehaviour
- **驱动**: `BaseComponent.Update()` 调用 `GameFrameworkEntry.Update()` 驱动所有 Module

#### 工具类清单

| 类名 | 类型 | 关键用途 |
|------|------|---------|
| UtilityBuiltin | static | 资源路径/压缩/JSON/加密/EntityId生成 |
| EntityRegistry | static | 全局实体注册表：Register/Unregister/GetClosestLeader |
| BrainFactory | static | 控制脑创建工厂 |
| FactoryHelper | static | 组件工厂缓存+懒加载 |
| DamageHelper | static | 伤害应用 |
| PropertyHelper | static | 属性系统构建器 |
| BuildManager | static | 建筑建造/升级 |
| ClusterCalculator | static | 群体阵型位置计算 |

#### 扩展方法清单

| 类名 | 扩展目标 | 关键方法 |
|------|---------|---------|
| EntityExtension | EntityComponent | ShowEffect, ShowEntity, HideEntitySafe, ShowPopText |
| UIExtension | UIComponent/Image | SetSprite, OpenUIForm, Close, ShowToast |
| SoundExtension | SoundComponent | PlayBGM, PlaySound, PlayEffect |
| AwaitExtension | 多个GF组件 | OpenUIFormAwait, ShowEntityAwait, LoadDataTableAwait |
| DataTableExtension | DataTableComponent | LoadDataTable, ParseColor32/Vector/Fix64 |

#### 第三方依赖

| 库名 | 用途 |
|------|------|
| UnityGameFramework (GF) | 游戏框架核心 |
| UniTask | 零GC async/await |
| DOTween | 缓动动画 |
| ZString | 零GC字符串格式化 |
| protobuf-net | Protocol Buffers 序列化 |
| Newtonsoft.Json | JSON 序列化 |
| HybridCLR | 热更新方案 |
| Obfuz | 代码混淆 |

#### GF Helper 实现

| 类名 | 实现接口 | 用途 |
|------|---------|------|
| ZStringTextHelper | ITextHelper | 高性能文本格式化 |
| NewtonsoftJsonHelper | IJsonHelper | JSON 序列化 |
| JsonLocalizationHelper | ILocalizationHelper | JSON 本地化解析 |
| CustomSoundAgentHelper | SoundAgentHelperBase | 音频播放 |
| CustomUIGroupHelper | UIGroupHelperBase | UI组 Canvas 设置 |
| NetworkChannelHelper | INetworkChannelHelper | Protobuf 网络包处理 |
