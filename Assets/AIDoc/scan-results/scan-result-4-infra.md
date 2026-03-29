# 扫描结果 4：框架层与基础设施继承链

> 扫描时间：2026-03-22
> 扫描范围：`Assets/Plugins/UnityGameFramework/`, `Assets/AAAGame/Scripts/`, `Assets/AAAGame/ScriptsBuiltin/`

---

## 1. GameEntry 注册的所有 Component 清单

### 1.1 注册机制

```
GameFrameworkComponent (abstract) : MonoBehaviour
  └─ Awake() 中调用 GameEntry.RegisterComponent(this)
```

**GameEntry** (`Assets/Plugins/UnityGameFramework/Scripts/Runtime/Base/GameEntry.cs`) 是一个 static class，维护一个 `GameFrameworkLinkedList<GameFrameworkComponent>`。所有继承 `GameFrameworkComponent` 的类在 `Awake()` 中自动注册。

### 1.2 GFBuiltin / GF 静态访问器

项目通过 **GFBuiltin** (`Assets/AAAGame/ScriptsBuiltin/Runtime/Extension/GFBuiltin.cs`) 和 **GF** (`Assets/AAAGame/Scripts/Extension/GF.cs`) 提供静态属性访问，GF 继承 GFBuiltin。

#### GFBuiltin 在 Start() 中注册的 Component（20 个）：

| # | 静态属性 | Component 类型 | 来源 |
|---|---------|---------------|------|
| 1 | `GF.Base` | BaseComponent | GF 框架 |
| 2 | `GF.Config` | ConfigComponent | GF 框架 |
| 3 | `GF.DataNode` | DataNodeComponent | GF 框架 |
| 4 | `GF.DataTable` | DataTableComponent | GF 框架 |
| 5 | `GF.Debugger` | DebuggerComponent | GF 框架 |
| 6 | `GF.Download` | DownloadComponent | GF 框架 |
| 7 | `GF.Entity` | EntityComponent | GF 框架 |
| 8 | `GF.Event` | EventComponent | GF 框架 |
| 9 | `GF.Fsm` | FsmComponent | GF 框架 |
| 10 | `GF.FileSystem` | FileSystemComponent | GF 框架 |
| 11 | `GF.Localization` | LocalizationComponent | GF 框架 |
| 12 | `GF.Network` | NetworkComponent | GF 框架 |
| 13 | `GF.Procedure` | ProcedureComponent | GF 框架 |
| 14 | `GF.Resource` | ResourceComponent | GF 框架 |
| 15 | `GF.Scene` | SceneComponent | GF 框架 |
| 16 | `GF.Setting` | SettingComponent | GF 框架 |
| 17 | `GF.Sound` | SoundComponent | GF 框架 |
| 18 | `GF.UI` | UIComponent | GF 框架 |
| 19 | `GF.ObjectPool` | ObjectPoolComponent | GF 框架 |
| 20 | `GF.WebRequest` | WebRequestComponent | GF 框架 |
| 21 | `GF.BuiltinView` | BuiltinViewComponent | 项目扩展 |

#### GF 在 Start() 中额外注册的 Component（3 个）：

| # | 静态属性 | Component 类型 | 来源 |
|---|---------|---------------|------|
| 22 | `GF.DataModel` | DataModelComponent | 项目扩展 |
| 23 | `GF.StaticUI` | StaticUIComponent | 项目扩展 |
| 24 | `GF.VariablePool` | VariablePoolComponent | 项目扩展 |

#### 其他自注册 Component（场景挂载时自动注册）：

| # | Component 类型 | 来源 |
|---|---------------|------|
| 25 | ADComponent | 项目扩展（代码中被注释掉） |
| 26 | InputManager | 项目扩展 |
| 27 | ReferencePoolComponent | GF 框架 |

---

## 2. GF 组件扩展树（继承 GameFrameworkComponent 的类）

```
MonoBehaviour
└── GameFrameworkComponent (abstract)                    [GF框架基类]
    │
    ├── BaseComponent (sealed)                           [基础：帧率/游速/Helper初始化]
    │
    ├── ConfigComponent                                  [全局配置键值对]
    ├── DataNodeComponent                                [层级数据节点]
    ├── DataTableComponent                               [数据表管理]
    ├── DebuggerComponent                                [调试器UI]
    ├── DownloadComponent                                [文件下载]
    ├── EntityComponent                                  [实体生命周期]
    ├── EventComponent                                   [全局事件分发]
    ├── FileSystemComponent                              [虚拟文件系统]
    ├── FsmComponent                                     [有限状态机]
    ├── LocalizationComponent                            [多语言]
    ├── NetworkComponent                                 [网络通信]
    ├── ObjectPoolComponent                              [对象池]
    ├── ProcedureComponent                               [流程管理]
    ├── ReferencePoolComponent                           [引用池调试]
    ├── ResourceComponent                                [资源加载]
    ├── SceneComponent                                   [场景管理]
    ├── SettingComponent                                 [持久化设置]
    ├── SoundComponent                                   [音频管理]
    ├── UIComponent                                      [UI管理]
    ├── WebRequestComponent                              [HTTP请求]
    │
    ├── BuiltinViewComponent                             [内建UI视图(加载/对话框)]
    ├── ADComponent                                      [广告SDK集成]
    ├── DataModelComponent                               [数据模型管理]
    ├── InputManager                                     [输入系统管理]
    ├── StaticUIComponent                                [静态UI(摇杆等)]
    └── VariablePoolComponent                            [变量池(Entity参数传递)]
```

### 2.1 各 Component 职责与关键方法

| Component | 职责 | 关键方法 |
|-----------|------|---------|
| **BaseComponent** | 初始化 Helper、控制帧率/游速/暂停 | `PauseGame()`, `ResumeGame()`, `GameSpeed`, `FrameRate` |
| **EntityComponent** | Entity 的 Show/Hide/Attach 生命周期管理 | `ShowEntity(...)`, `HideEntity(...)`, `AttachEntity(...)`, `HasEntityGroup(string)` |
| **UIComponent** | UI 表单生命周期、UI 分组管理 | `OpenUIForm(...)`, `CloseUIForm(...)`, `HasUIGroup(string)`, `AddUIGroup(...)` |
| **EventComponent** | 全局事件发布/订阅 | `Subscribe(int, EventHandler)`, `Unsubscribe(...)`, `Fire(...)`, `FireNow(...)` |
| **FsmComponent** | 创建/销毁有限状态机 | `CreateFsm<T>(...)`, `DestroyFsm<T>()`, `HasFsm<T>()`, `GetFsm<T>()` |
| **ProcedureComponent** | 游戏流程（启动→主菜单→游戏等）的状态机 | `CurrentProcedure`, `HasProcedure<T>()`, `GetProcedure<T>()` |
| **ResourceComponent** | 资源异步加载/卸载 | `LoadAsset(...)`, `UnloadAsset(...)`, `AssetCount` |
| **SceneComponent** | 场景加载/卸载 | `LoadScene(...)`, `UnloadScene(...)`, `SceneIsLoaded(string)` |
| **SoundComponent** | 音频播放（分组管理） | `PlaySound(...)`, `StopSound(...)`, `HasSoundGroup(string)` |
| **ObjectPoolComponent** | 通用对象池 | `CreateSingleSpawnObjectPool<T>(...)`, `ReleaseAllUnused()` |
| **NetworkComponent** | 网络频道管理 | `CreateNetworkChannel(...)`, `GetNetworkChannel(string)`, `DestroyNetworkChannel(string)` |
| **DataTableComponent** | 数据表加载/查询 | `CreateDataTable<T>()`, `LoadDataTable(...)`, `GetDataTable<T>()` |
| **DataNodeComponent** | 层级键值数据存储 | `GetData<T>(string)`, `SetData<T>(string, T)`, `GetOrAddNode(string)` |
| **ConfigComponent** | 全局配置读写 | `ReadData(string)`, `HasConfig(string)`, `GetConfig<T>(string)` |
| **LocalizationComponent** | 多语言切换/字典查询 | `Language { get; set; }`, `ReadData(string)`, `GetString(string)` |
| **SettingComponent** | 持久化设置(PlayerPrefs) | `GetBool/Int/Float(string)`, `SetBool/Int/Float(...)`, `Save()` |
| **DownloadComponent** | 文件下载任务管理 | `AddDownload(...)`, `Paused`, `TotalAgentCount` |
| **WebRequestComponent** | HTTP 请求管理 | `AddWebRequest(string)`, `GetWebRequestInfo(int)` |
| **FileSystemComponent** | 虚拟文件系统 | `CreateFileSystem(...)`, `GetFileSystem(string)`, `DestroyFileSystem(...)` |
| **DebuggerComponent** | 运行时调试面板 | `ActiveWindow`, `ShowFullWindow` |
| **ReferencePoolComponent** | 引用池严格检查开关 | `EnableStrictCheck` |
| **BuiltinViewComponent** | 热更前的内建 UI | `ShowLoadingProgress(float)`, `ShowDialog(...)`, `HideDialog()` |
| **DataModelComponent** | 数据模型的创建/获取/释放 | `CreateDataModel<T>(...)`, `GetDataModel<T>()`, `ReleaseDataModel<T>()` |
| **StaticUIComponent** | 常驻静态 UI（摇杆等） | `UpdateCanvasScaler()`, `Joystick` |
| **VariablePoolComponent** | 按 EntityId 存取临时变量 | `SetVariable<T>(int, string, T)`, `GetVariable<T>(int, string)`, `ClearVariables(int)` |
| **InputManager** | 输入系统和输入状态机 | `CurState`, `ChangeState(InputState)`, `FindModel()` |

---

## 3. Manager / Singleton 完整继承树 + 职责

### 3.1 双层架构：GameFrameworkModule (C#层) ↔ GameFrameworkComponent (Unity层)

```
┌──────────────────────────────────────────────────────┐
│  C# 纯逻辑层 (GameFramework namespace)               │
│                                                      │
│  GameFrameworkModule (internal abstract)              │
│  ├── ConfigManager                                   │
│  ├── DataNodeManager                                 │
│  ├── DataTableManager                                │
│  ├── DebuggerManager                                 │
│  ├── DownloadManager                                 │
│  ├── EntityManager                                   │
│  ├── EventManager                                    │
│  ├── FileSystemManager                               │
│  ├── FsmManager                                      │
│  ├── LocalizationManager                             │
│  ├── NetworkManager                                  │
│  ├── ObjectPoolManager                               │
│  ├── ProcedureManager                                │
│  ├── ResourceManager                                 │
│  ├── SceneManager                                    │
│  ├── SettingManager                                  │
│  ├── SoundManager                                    │
│  └── WebRequestManager                               │
│                                                      │
│  由 GameFrameworkEntry 统一管理 Update/Shutdown       │
│  通过 GetModule<IXxxManager>() 自动创建              │
└──────────────────────────────────────────────────────┘
        ↑ 内部持有引用
┌──────────────────────────────────────────────────────┐
│  Unity 层 (UnityGameFramework.Runtime namespace)      │
│                                                      │
│  GameFrameworkComponent → XxxComponent               │
│  在 Awake() 中通过 GameFrameworkEntry.GetModule<>()   │
│  获取对应 Manager, 封装为 Unity 可序列化的组件         │
└──────────────────────────────────────────────────────┘
```

**GameFrameworkModule** (`Assets/Plugins/UnityGameFramework/GameFramework/Base/GameFrameworkModule.cs`):
```csharp
internal abstract class GameFrameworkModule
{
    internal virtual int Priority { get; }   // 优先级越高越先 Update，越后 Shutdown
    internal abstract void Update(float elapseSeconds, float realElapseSeconds);
    internal abstract void Shutdown();
}
```

### 3.2 项目自定义 Manager/Singleton

```
MonoBehaviour
├── Singleton<T> : MonoBehaviour                        [通用泛型单例基类]
│   └── MessageSystem : Singleton<MessageSystem>        [全局消息总线]
│
├── GroupMoveManager (手动单例模式)                      [ORCA群体移动协调]
├── InteractionManager                                  [交互目标选择/评分]
│
└── (非MonoBehaviour)
    └── IReference
        └── PropertyManager                             [属性注册表]
```

| Manager | 基类 | 职责 | 关键方法 |
|---------|------|------|---------|
| **MessageSystem** | `Singleton<MessageSystem>` | 枚举标签事件总线，组件间消息通信 | `Register<T>(tag, handler)`, `Unregister<T>(tag, handler)`, `Send<T>(tag, box, sender)` |
| **GroupMoveManager** | `MonoBehaviour` (单例) | ORCA 群体移动：注册Agent/障碍物、协调避障 | `RegisterAgent(MAEntity)`, `UnregisterAgent(MAEntity)`, `RegisterCircleObstacle(...)`, `UnregisterObstacle(int)` |
| **InteractionManager** | `MonoBehaviour` | 交互目标选择：距离/角度评分+滞后切换 | `ResolveBestTarget(...)`, `TryScore(...)`, `HandleInput()` |
| **PropertyManager** | `IReference` | 属性注册表：按 ID 查询不同类型的属性 | `RegisterProperty(IProperty)`, `GetValueProperty(string)`, `GetBaseValueProperty(string)`, `GetComputeValueProperty(string)` |

### 3.3 Singleton 泛型基类

**文件**: `Assets/AAAGame/Scripts/NeilUtility/MessageSystem/Singleton.cs`
**命名空间**: `BaseUtility`

```csharp
public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; }    // 自动查找或创建，DontDestroyOnLoad
    public static bool HasInstance { get; }
    protected virtual void Awake();       // 防重复实例
}
```

---

## 4. 接口清单

### 4.1 GF 框架核心接口

#### 基础设施接口

| 接口名 | 关键方法签名 | 文件路径 |
|--------|-------------|---------|
| `IReference` | `void Clear()` | `GameFramework/Base/ReferencePool/IReference.cs` |
| `ITaskAgent<T>` where T : TaskBase | `void Initialize()`, `StartTaskStatus Start(T task)`, `void Update(...)`, `void Shutdown()`, `void Reset()` | `GameFramework/Base/TaskPool/ITaskAgent.cs` |
| `IDataProvider<T>` | `void ReadData(string)`, `bool ParseData(string/byte[])`, events: ReadDataSuccess/Failure/Update | `GameFramework/Base/DataProvider/IDataProvider.cs` |
| `IDataRow` | `int Id { get; }`, `bool ParseDataRow(string/byte[], ...)` | `GameFramework/DataTable/IDataRow.cs` |

#### Manager 接口

| 接口名 | 关键方法签名 | 文件路径 |
|--------|-------------|---------|
| `IEntityManager` | `ShowEntity(int, string, string, ...)`, `HideEntity(...)`, `AttachEntity(...)`, `DetachEntity(...)` | `GameFramework/Entity/IEntityManager.cs` |
| `IUIManager` | `OpenUIForm(string, string, ...)`, `CloseUIForm(...)`, `RefocusUIForm(...)` | `GameFramework/UI/IUIManager.cs` |
| `IEventManager` | `Subscribe(int, EventHandler)`, `Unsubscribe(...)`, `Fire(...)`, `FireNow(...)` | `GameFramework/Event/IEventManager.cs` |
| `IFsmManager` | `CreateFsm<T>(...)`, `DestroyFsm<T>()`, `GetFsm<T>()` | `GameFramework/Fsm/IFsmManager.cs` |
| `IProcedureManager` | `StartProcedure<T>()`, `HasProcedure<T>()`, `CurrentProcedure` | `GameFramework/Procedure/IProcedureManager.cs` |
| `IResourceManager` | `LoadAsset(...)`, `UnloadAsset(...)`, 60+ 方法 (资源检查/更新/版本) | `GameFramework/Resource/IResourceManager.cs` |
| `ISceneManager` | `LoadScene(string, ...)`, `UnloadScene(string, ...)`, `SceneIsLoaded(string)` | `GameFramework/Scene/ISceneManager.cs` |
| `ISoundManager` | `PlaySound(string, string, ...)`, `StopSound(int)`, `PauseSound(int)` | `GameFramework/Sound/ISoundManager.cs` |
| `IObjectPoolManager` | `CreateSingleSpawnObjectPool<T>(...)`, `GetObjectPool<T>()`, `HasObjectPool<T>()` | `GameFramework/ObjectPool/IObjectPoolManager.cs` |
| `INetworkManager` | `CreateNetworkChannel(string, ServiceType, INetworkChannelHelper)`, `DestroyNetworkChannel(string)` | `GameFramework/Network/INetworkManager.cs` |
| `IDataTableManager` | `CreateDataTable<T>(...)`, `GetDataTable<T>()`, `DestroyDataTable<T>()` | `GameFramework/DataTable/IDataTableManager.cs` |
| `IDataNodeManager` | `GetData<T>(string)`, `SetData<T>(string, T)`, `GetOrAddNode(string)` | `GameFramework/DataNode/IDataNodeManager.cs` |
| `IConfigManager` | `HasConfig(string)`, `GetBool/Int/Float/String(string)`, `AddConfig(...)` | `GameFramework/Config/IConfigManager.cs` |
| `ILocalizationManager` | `Language { get; set; }`, `GetString(string)`, `GetString<T>(string, T)` | `GameFramework/Localization/ILocalizationManager.cs` |
| `ISettingManager` | `GetBool/Int/Float/String(string)`, `SetBool/Int/Float/String(...)`, `Save()`, `Load()` | `GameFramework/Setting/ISettingManager.cs` |
| `IDownloadManager` | `AddDownload(string, string, ...)`, `RemoveDownload(int)`, `Paused { get; set; }` | `GameFramework/Download/IDownloadManager.cs` |
| `IWebRequestManager` | `AddWebRequest(string, ...)`, `RemoveWebRequest(int)`, `Timeout { get; set; }` | `GameFramework/WebRequest/IWebRequestManager.cs` |
| `IFileSystemManager` | `CreateFileSystem(...)`, `LoadFileSystem(...)`, `DestroyFileSystem(...)` | `GameFramework/FileSystem/IFileSystemManager.cs` |
| `IDebuggerManager` | `RegisterDebuggerWindow(string, IDebuggerWindow)`, `SelectDebuggerWindow(string)` | `GameFramework/Debugger/IDebuggerManager.cs` |

#### Entity/UI/Network 子接口

| 接口名 | 关键方法签名 | 文件路径 |
|--------|-------------|---------|
| `IEntity` | `int Id`, `OnInit(...)`, `OnShow(...)`, `OnHide(...)`, `OnUpdate(...)` | `GameFramework/Entity/IEntity.cs` |
| `IEntityGroup` | `string Name`, `HasEntity(int)`, `GetEntity(int)`, `GetAllEntities()` | `GameFramework/Entity/IEntityGroup.cs` |
| `IUIForm` | `int SerialId`, `OnInit(...)`, `OnOpen(...)`, `OnClose(...)`, `OnPause()`, `OnResume()` | `GameFramework/UI/IUIForm.cs` |
| `IUIGroup` | `string Name`, `int Depth`, `HasUIForm(int)`, `GetUIForm(int)`, `CurrentUIForm` | `GameFramework/UI/IUIGroup.cs` |
| `INetworkChannel` | `bool Connected`, `Connect(IPAddress, int)`, `Close()`, `Send<T>(T)`, `RegisterHandler(IPacketHandler)` | `GameFramework/Network/INetworkChannel.cs` |
| `IFsm<T>` | `T Owner`, `FsmState<T> CurrentState`, `Start<TState>()`, `HasData(string)`, `SetData(...)` | `GameFramework/Fsm/IFsm.cs` |
| `IObjectPool<T>` | `Spawn()`, `Unspawn(T)`, `Register(T, bool)`, `Release()`, `ReleaseAllUnused()` | `GameFramework/ObjectPool/IObjectPool.cs` |
| `IDataTable<T>` | `T this[int]`, `HasDataRow(int)`, `GetDataRow(int)`, `GetAllDataRows()` | `GameFramework/DataTable/IDataTable.cs` |
| `IDataNode` | `string Name`, `GetData<T>()`, `SetData<T>(T)`, `GetChild(string)`, `GetOrAddChild(string)` | `GameFramework/DataNode/IDataNode.cs` |

#### Helper 接口（依赖注入点）

| 接口名 | 文件路径 |
|--------|---------|
| `Utility.Text.ITextHelper` | `GameFramework/Utility/Utility.Text.ITextHelper.cs` |
| `Utility.Json.IJsonHelper` | `GameFramework/Utility/Utility.Json.IJsonHelper.cs` |
| `Utility.Compression.ICompressionHelper` | `GameFramework/Utility/Utility.Compression.ICompressionHelper.cs` |
| `GameFrameworkLog.ILogHelper` | `GameFramework/Base/Log/GameFrameworkLog.ILogHelper.cs` |
| `Version.IVersionHelper` | `GameFramework/Base/Version/Version.IVersionHelper.cs` |
| `IEntityHelper` / `IEntityGroupHelper` | `GameFramework/Entity/` |
| `IUIFormHelper` / `IUIGroupHelper` | `GameFramework/UI/` |
| `ISoundHelper` / `ISoundAgentHelper` / `ISoundGroupHelper` | `GameFramework/Sound/` |
| `ISettingHelper` | `GameFramework/Setting/ISettingHelper.cs` |
| `IConfigHelper` | `GameFramework/Config/IConfigHelper.cs` |
| `ILocalizationHelper` | `GameFramework/Localization/ILocalizationHelper.cs` |
| `ILoadResourceAgentHelper` / `IResourceHelper` | `GameFramework/Resource/` |
| `IDownloadAgentHelper` | `GameFramework/Download/IDownloadAgentHelper.cs` |
| `IWebRequestAgentHelper` | `GameFramework/WebRequest/IWebRequestAgentHelper.cs` |
| `INetworkChannelHelper` | `GameFramework/Network/INetworkChannelHelper.cs` |
| `IFileSystemHelper` | `GameFramework/FileSystem/IFileSystemHelper.cs` |
| `IDataTableHelper` | `GameFramework/DataTable/IDataTableHelper.cs` |
| `IDataProviderHelper<T>` | `GameFramework/Base/DataProvider/IDataProviderHelper.cs` |

### 4.2 项目业务接口

#### 实体/生物系统接口

| 接口名 | 方法签名 | 文件路径 |
|--------|---------|---------|
| **IEntityContext** | `Vector3 Position { get; set; }`, `Quaternion Rotation { get; set; }`, `SideType Side`, `bool Alive`, `string ReferenceId`, `IControlBrain Brain`, `IMoveExecutor MoveExecutor`, `IMoveComp MoveComp`, `IAtkComp AtkComp`, `ITargetingComp TargetComp`, `WeaponComp WeaponComp`, `float GetProperty(CreatureMainProperty)`, `void TakeDamage(float, HealthModifyType)`, `bool CanRun(ICapability)`, `void LockComp(ICapability, ICapability)`, `void ResumeComp(ICapability, ICapability)` | `Scripts/GeneralCreature/IEntityContext.cs` |
| **IControlBrain** | `Vector2 Move { get; }`, `bool Attack { get; }`, `bool Skill1 { get; }`, `bool Skill2 { get; }`, `bool Skill3 { get; }` | `Scripts/Entity/IControlBrain.cs` |
| **IMoveComp** | `void Init(IEntityContext)`, `void Move(float)`, `void MoveTo(Vector3)`, `void StopMove()`, `Vector3 GetNavDirection()` | `Scripts/GeneralCreature/IMoveComp.cs` |
| **IMoveExecutor** | `void SetInput(Vector3)`, `void AddExternal(Vector3)`, `void SetOverride(Vector3)`, `void ClearOverride()`, `void SetExternal(Vector3)`, `void Execute()`, `void Execute(float)` | `Scripts/GeneralCreature/IMoveExecutor.cs` |
| **IAtkComp** | `void Init(IEntityContext)`, `void Attack(float)` | `Scripts/GeneralCreature/IAtkComp.cs` |
| **ITargetingComp** | `void Init(IEntityContext)`, `void UpdateTargeting(float)`, `IEntityContext CurrentTarget { get; set; }`, `IEntityContext FollowTarget`, `float AggroRange { get; set; }`, `float ForgetRange { get; set; }`, `float FollowSearchRange { get; set; }` | `Scripts/GeneralCreature/ITargetingComp.cs` |
| **IDurationMoveEffectComp** | `void Init(IEntityContext)`, `int StartDurationAdditionalMove(float, Vector3, Func<Vector3,Vector3>)`, `int StartDurationOverrideMove(...)`, `void StopAddtionalMove(int)`, `void StopOverrideMove(int)`, `void StopAllMove()`, `void ApplyEffect(float)` | `Scripts/GeneralCreature/IDurationMoveEffectComp.cs` |
| **ICapability** | `void ShutDown()`, `void Resume()` | `Scripts/NeilUtility/ICapability.cs` |
| **IMoveCompo** (BaseUtility) | `void BanMove()`, `void AllowMove()` | `Scripts/NeilUtility/IMoveCompo.cs` |

#### 动作/选择系统接口

| 接口名 | 方法签名 | 文件路径 |
|--------|---------|---------|
| **ISelector** | `void ReleaseSelection()`, `void Activate()`, `void ClearSelected()`, `void ChangeRange(Vector3)`, `int GetEntityID()`, `void SetPosition(Vector3)` | `Scripts/ActionSystem/Selection/ISelector.cs` |
| **ISelector\<T\>** | `bool Validate(GameObject)`, `int GetSelected(out List<T>)`, `void Activate(List<T>)` | 同上 |
| **ISelectable** | `bool CanBeSelected()`, `void InSelection(ISelector)`, `void DeSelection()` | `Scripts/ActionSystem/Selection/ISelectable.cs` |

#### 技能/交互/属性接口

| 接口名 | 方法签名 | 文件路径 |
|--------|---------|---------|
| **ISkillComp** | `void Init(SkillEntity, List<BasicSkill>)`, `void Skill()` | `Scripts/SkillSystem/ISkillComp.cs` |
| **ISkillLocker** | `void RecordLockedSkill(SkillSlot)`, `void DirectUnlockAll()` | `Scripts/PlayerControl/ISkillLocker.cs` |
| **IInteractionOption** | `void Init(object, string, InteractionParams)`, `string DisplayName`, `StringIntPair[] CostMaterial`, `bool IsExecutable()`, `bool IsAvailable()`, `void Execute()` | `Scripts/Interaction/IInteractionOption.cs` |
| **IProperty** | `PropertyManager PropertyManager`, `IReadOnlyList<IPropertyModifier> Modifiers`, `void AddModifier(IPropertyModifier)`, `void RemoveModifier(IPropertyModifier)`, `string PropertyId`, `void MakeDirty()`, `void OnDirty(Action)` | `Scripts/Property/IProperty.cs` |
| **IProperty\<T\>** | `T GetValue()`, `Func<T> GetValueGetter()` | 同上 |
| **IPropertyModifier** | `int Priority { get; }` | `Scripts/Property/Modifier/IPropertyModifier.cs` |
| **IPropertyAdditiveModifier\<T\>** | `T Value { get; }` | `Scripts/Property/Modifier/IPropertyAdditiveModifier.cs` |
| **IPropertyMultiplicativeModifier\<T\>** | `T Value { get; }` | `Scripts/Property/Modifier/IPropertyMultiplicativeModifier.cs` |
| **IPropertyOverrideModifier\<T\>** | `T Value { get; }` | `Scripts/Property/Modifier/IPropertyOverrideModifier.cs` |
| **IPropertyClampModifier\<T\>** | `T Min { get; }`, `T Max { get; }` | `Scripts/Property/Modifier/IPropertyClampModifier.cs` |

---

## 5. 抽象基类完整继承树

### 5.1 Entity 继承链

```
MonoBehaviour
└── EntityLogic (abstract)                              [GF实体逻辑基类]
    │   OnInit/OnShow/OnHide/OnUpdate/OnRecycle
    │   OnAttached/OnDetached/OnAttachTo/OnDetachFrom
    │
    └── EntityBase                                      [项目实体基类: Id, Params, 自动Attach]
        │
        ├── GeneralCreature : ITargetable, ISelectable  [生物基类: 血量/阵营/伤害/动画]
        │   └── (业务层各种生物实体)
        │
        ├── AbstractProjectile                          [抽象弹丸: Move/Destroy/HitBox]
        │   └── (各种子弹/投射物)
        │
        ├── TargetableSelector : ISelector<ISelectable> [区域目标选择器: Trigger检测]
        │   └── (各种范围选择器)
        │
        └── (其他 Entity 子类)
```

### 5.2 UI 继承链

```
MonoBehaviour
└── UIFormLogic (abstract, GF)                          [UI表单逻辑基类]
    │   OnInit/OnOpen/OnClose/OnPause/OnResume/OnUpdate
    │
    └── UIFormBase : ISerializeFieldTool                [项目UI基类]
        │   OpenSubUIForm, CloseSubUIForm
        │   SpawnItem<T>, UnspawnItem<T> (对象池)
        │   OnButtonClick, OnOpenAnimationComplete
        │   InitLocalization
        │
        └── (所有具体UI表单)
```

### 5.3 流程继承链

```
FsmState<IProcedureManager>
└── ProcedureBase (abstract)                            [流程基类]
    │   OnInit/OnEnter/OnUpdate/OnLeave/OnDestroy
    │
    └── (各 Procedure 子类: ProcedureLaunch, ProcedureMain 等)
```

### 5.4 FSM 状态继承链

```
FsmState<T> (abstract, where T : class)                 [泛型状态基类]
│   OnInit/OnEnter/OnUpdate/OnLeave/OnDestroy
│   ChangeState<TState>(IFsm<T>)
│
├── ProcedureBase → 见 5.3
└── (自定义 FSM 状态)
```

### 5.5 事件参数继承链

```
GameFrameworkEventArgs (abstract)                       [框架事件参数]
└── BaseEventArgs (abstract)                            [带 Id 的事件参数]
    │   abstract int Id { get; }
    │
    ├── GameEventArgs (abstract)                        [游戏事件参数标记]
    │   └── (各种 XxxEventArgs: ShowEntitySuccessEventArgs 等)
    │
    └── Packet (abstract)                               [网络包基类]
        └── PacketBase : IExtensible                    [ProtoBuf 网络包]
            ├── CSPacketBase                            [客户端→服务端]
            └── SCPacketBase                            [服务端→客户端]
```

### 5.6 变量继承链

```
Variable (abstract) : IReference                        [变量基类]
│   abstract Type Type, GetValue(), SetValue(), Clear()
│
└── Variable<T> (abstract)                              [泛型变量]
    │   T Value { get; set; }
    │
    └── (VarBool, VarInt32, VarString 等具体类型)
```

### 5.7 数据表行继承链

```
IDataRow (interface)
└── DataRowBase (abstract)                              [数据行基类]
    │   abstract int Id { get; }
    │   virtual ParseDataRow(string/byte[])
    │
    └── (各 DR_Xxx 数据行类)
```

### 5.8 对象池继承链

```
ObjectBase (abstract) : IReference                      [池化对象基类]
│   Name, Target, Locked, Priority, LastUseTime
│   OnSpawn(), OnUnspawn(), abstract Release(bool)
│
└── (EntityInstanceObject, UIFormInstanceObject 等)

ObjectPoolBase (abstract)                               [对象池基类]
│   Count, Capacity, ExpireTime, AutoReleaseInterval
│   abstract Release(), ReleaseAllUnused()
│
└── ObjectPoolManager.ObjectPool<T>                     [泛型对象池实现]
```

### 5.9 任务继承链

```
TaskBase (abstract) : IReference                        [异步任务基类]
│   SerialId, Tag, Priority, UserData, Done
│
└── (DownloadTask, WebRequestTask, LoadResourceTaskBase 等)
```

### 5.10 网络继承链

```
IPacketHandler (interface)
└── PacketHandlerBase (abstract)                        [网络包处理器基类]
    │   abstract int Id, abstract void Handle(object, Packet)
    │
    └── (各种具体 PacketHandler)

IPacketHeader : IReference
└── PacketHeaderBase (abstract)                         [包头基类: Type/Id/Length]
```

### 5.11 行为树继承链

```
ScriptableObject
└── AbstractNode (abstract)                             [行为树节点基类]
    │   abstract NodeState Execute(IBTContext)
    │   virtual string GetNodeName()
    │
    └── (ActionNode, ConditionNode, CompositeNode 等)
```

### 5.12 动作系统继承链

```
ScriptableObject
└── BasicAction (abstract)                              [角色动作基类]
    │   StartAction(GeneralCreature, out ActionInfo)
    │   Tick(ActionInfo, float), Interrupt(ActionInfo)
    │   virtual OnStart/OnUpdate/OnFinish/OnInterrupt
    │
    └── (各种具体 Action)
```

### 5.13 状态机继承链

```
AbsStatemachine<T1, T2> (abstract, generic)             [泛型状态机]
│   T1 = 状态枚举, T2 = 上下文数据
│   StartState(T1), UpdateState()
│   virtual SwitchWhenStart/End/Update
│
└── (InputSM, 各种业务状态机)
```

### 5.14 属性系统继承链

```
IProperty<Fix64>
└── ValueProperty (abstract)                            [数值属性基类]
    │   abstract GetValue(), abstract ApplyModify(ref Fix64)
    │   AddModifier/RemoveModifier, MakeDirty, OnDirty
    │   Register(PropertyManager), NotifyParentDirty
    │
    ├── BaseValueProperty                               [基础值属性]
    ├── ComputeValueProperty                            [计算属性(依赖其他属性)]
    └── IrreversibleValueProperty                       [不可逆属性]
```

### 5.15 数据模型继承链

```
IReference
└── DataModelBase (abstract)                            [数据模型基类]
    │   OnCreate(RefParams), OnRelease()
    │   Init(int, RefParams), Clear(), Shutdown()
    │
    └── DataModelStorageBase (abstract)                  [可持久化数据模型]
        │   Load(), Save(), OnInitialDataModel()
        │
        └── (各种持久化数据模型)
```

### 5.16 工厂继承链

```
ScriptableObject
├── MoveCompFactory (abstract)                          [移动组件工厂]
│   │   abstract IMoveComp CreateMoveComp(MAEntity)
│   └── NoMoveFactory                                   [空移动]
│
├── AtkCompFactory (abstract)                           [攻击组件工厂]
│   │   abstract IAtkComp CreateAtkComp(MAEntity)
│   └── NoAtkFactory                                    [空攻击]
│
├── TargetingCompFactory (abstract)                     [目标锁定工厂]
│   │   abstract ITargetingComp CreateTargetingComp(MAEntity)
│   └── NoTargetingFactory                              [空目标锁定]
│
└── SkillCompFactory (abstract)                         [技能组件工厂]
    │   abstract ISkillComp CreateSkillComp(SkillEntity)
    └── PlayerSkillFactory                              [玩家技能工厂]
```

### 5.17 序列化器继承链

```
GameFrameworkSerializer<T> (abstract, generic)           [版本化序列化器]
│   RegisterSerializeCallback(byte, callback)
│   RegisterDeserializeCallback(byte, callback)
│   Serialize(Stream, T), Deserialize(Stream)
│   abstract byte[] GetHeader()
│
└── (PackageVersionListSerializer, UpdatableVersionListSerializer 等)
```

---

## 6. 工具类和扩展方法列表

### 6.1 项目工具类

| 类名 | 类型 | 职责 | 关键方法 | 文件路径 |
|------|------|------|---------|---------|
| **UtilityBuiltin** | static | 资源路径/压缩/JSON/加密/EntityId 生成 | `GenerateEntityId()`, `Zip.Compress/Decompress()`, `Json.ToJson/ToObject()`, `MD5_32.Encrypt()`, `XOR.QuickXor()`, `DES.Encrypt/Decrypt()` | `ScriptsBuiltin/Runtime/Extension/UtilityBuiltin.cs` |
| **UtilityEx** | static | 网络检查、UI 触摸检测 | `CheckNetwork()`, `IsPointerOverUIObject(Vector2)` | `Scripts/Extension/UtilityEx.cs` |
| **EntityRegistry** | static | 全局实体注册表 | `Register(IEntityContext)`, `RegisterAsPlayer(IEntityContext)`, `Unregister(IEntityContext)`, `GetClosestLeader(Vector3)`, `AllEntities`, `Player` | `Scripts/Entity/EntityRegistry.cs` |
| **EntitySideHelper** | static | 阵营→受击阵营映射 | `GetHitSide(SideType)` | `Scripts/Entity/EntitySideHelper.cs` |
| **ClusterCalculator** | static | 群体阵型位置计算(NavMesh 避障) | `GetClusteredPosition(Transform, MAEntity, float)` | `Scripts/Entity/ClusterCalculator.cs` |
| **BrainFactory** | static | 控制脑创建工厂 | `Create(BrainType, MAEntity, EntityParams)` | `Scripts/Entity/BrainFactory.cs` |
| **FactoryHelper** | static | 组件工厂缓存+懒加载 | `CreateAtkComp(string, MAEntity)`, `CreateMoveComp(...)`, `CreateSkillComp(...)`, `CreateTargetingComp(...)` | `Scripts/GeneralCreature/FactoryHelper.cs` |
| **DamageHelper** | static | 伤害应用 | `DoDamage(ITargetable, Damage)` | `Scripts/DamageSystem/DamageHelper.cs` |
| **DamageSystem** | static (BaseUtility) | 效果包伤害处理 | `ProcessDamage(EffectPackage, ITarget)` | `Scripts/NeilUtility/DamageSystem.cs` |
| **PropertyHelper** | static | 属性系统构建器 | `CreateBaseProperty()`, `FormComputeBasePropertyTree()`, `BindComputePropertyToOne()` | `Scripts/Property/PropertyHelper.cs` |
| **PropertyFuncRef** | static | 属性计算公式引用 | `SumAll`, `MultAll`, `GetAbilityWithConfigAndLevel()`, `GetHealthWithConfigAndLevel()`, `GetSpeedWithConfigAndLevel()` | `Scripts/Property/PropertyFuncRef.cs` |
| **BuildManager** | static | 建筑建造/升级 | `BuildDevice(string, Vector3)`, `UpgradeDevice(DeviceEntity, string)`, `HasBuildCost(string)` | `Scripts/Build/BuildManager.cs` |
| **ConstBuiltin** | static | 常量定义(热更/加密/AB测试) | `HOT_FIX_DLL_DIR`, `DES_KEY`, `AB_TEST_TAG`, `Setting.*` | `ScriptsBuiltin/Runtime/Common/ConstBuiltin.cs` |
| **UserDataHelper** | 数据类集合 | 数组数据包装器 | `Vector3ArrayData`, `FloatArrayData`, `IntArrayData`, `UniversalUserData` | `Scripts/NeilUtility/UserDataHelper.cs` |
| **EffectPackage** | 实例类 (BaseUtility) | 效果数据容器(装饰器模式) | `AddEffect(IEPDecoration)`, `damage`, `decorations` | `Scripts/NeilUtility/EffectPackage.cs` |

### 6.2 项目扩展方法

| 类名 | 扩展目标 | 关键方法 | 文件路径 |
|------|---------|---------|---------|
| **EntityExtension** | EntityComponent | `ShowEffect()`, `ShowEntity()`, `HideEntitySafe()`, `GetEntity<T>()`, `ShowPopText()` | `Scripts/Extension/EntityExtension.cs` |
| **UIExtension** | UIComponent / Image / RawImage | `SetSprite()`, `SetTexture()`, `OpenUIForm()`, `Close()`, `ShowToast()`, `GetTopUIFormId()`, `SetColorAlpha()` | `Scripts/Extension/UIExtension.cs` |
| **SoundExtension** | SoundComponent | `PlayBGM()`, `PlaySound()`, `PlayEffect()`, `PlayVibrate()` | `Scripts/Extension/SoundExtension.cs` |
| **ResourceExtension** | ResourceComponent | `LoadAsset()` | `Scripts/Extension/ResourceExtension.cs` |
| **ConfigExtension** | ConfigComponent | `LoadConfig()`, `GetVector2/3()`, `GetArray<T>()` | `Scripts/Extension/ConfigExtension.cs` |
| **SettingExtension** | SettingComponent | `SetABTestGroup()`, `SetLanguage()`, `SetMediaMute()` | `Scripts/Extension/SettingExtension.cs` |
| **LocalizationExtension** | LocalizationComponent | `LoadLanguage()` | `Scripts/Extension/LocalizationExtension.cs` |
| **DataTableExtension** | DataTableComponent / BinaryReader | `LoadDataTable()`, `ParseColor32/Vector2/3/4/Quaternion()`, `ParseArray<T>()`, `ParseFix64()` | `Scripts/Extension/DataTableExtension.cs` |
| **TransformExtension** | Transform | `DoBlinkScale()`, `FindWithTag()`, `FindChildrenWithTag()` | `Scripts/Extension/TransformExtension.cs` |
| **DOTweenExtension** | Rigidbody2D / CanvasGroup / TMP | `DOPath()`, `DOFade()` | `Scripts/Extension/DOTweenExtension.cs` |
| **AnimationExtension** | Animation | `PlayBackward()`, `PlayForward()` | `Scripts/Extension/Animation/AnimationExtension.cs` |
| **AwaitExtension** | 多个 GF 组件 | `OpenUIFormAwait()`, `ShowEntityAwait()`, `LoadDataTableAwait()`, `LoadSceneAwait()`, `LoadAssetAwait()` | `Scripts/Extension/AwaitExtension/AwaitExtension.cs` |
| **LocalizationExtension** (Builtin) | LocalizationComponent | `GetText()` (带大小写转换) | `ScriptsBuiltin/Runtime/Extension/LocalizationExtension.cs` |

### 6.3 GF Helper 实现

| 类名 | 实现接口 | 职责 | 文件路径 |
|------|---------|------|---------|
| **ZStringTextHelper** | `Utility.Text.ITextHelper` | 高性能文本格式化 (ZString) | `ScriptsBuiltin/Runtime/GFHelper/ZStringTextHelper.cs` |
| **NewtonsoftJsonHelper** | `Utility.Json.IJsonHelper` | JSON 序列化 (Newtonsoft.Json) | `ScriptsBuiltin/Runtime/GFHelper/NewtonsoftJsonHelper.cs` |
| **JsonLocalizationHelper** | `ILocalizationHelper` | JSON 格式本地化解析 | `ScriptsBuiltin/Runtime/GFHelper/JsonLocalizationHelper.cs` |
| **CustomSoundAgentHelper** | `SoundAgentHelperBase` | 自定义音频播放（淡入淡出/Entity绑定） | `ScriptsBuiltin/Runtime/GFHelper/CustomSoundAgentHelper.cs` |
| **CustomUIGroupHelper** | `UIGroupHelperBase` | UI 组 Canvas/Raycaster 设置 | `ScriptsBuiltin/Runtime/GFHelper/CustomUIGroupHelper.cs` |
| **NetworkChannelHelper** | `INetworkChannelHelper` | Protobuf 网络包序列化/反序列化 | `Scripts/Network/NetworkChannelHelper.cs` |

### 6.4 GF 框架内置工具类

| 类名 | 职责 | 文件路径 |
|------|------|---------|
| `Utility.Assembly` | 程序集类型查找 | `GameFramework/Utility/Utility.Assembly.cs` |
| `Utility.Converter` | 数值/屏幕转换 | `GameFramework/Utility/Utility.Converter.cs` |
| `Utility.Encryption` | 加密工具 | `GameFramework/Utility/Utility.Encryption.cs` |
| `Utility.Marshal` | 非托管内存管理 | `GameFramework/Utility/Utility.Marshal.cs` |
| `Utility.Path` | 路径处理 | `GameFramework/Utility/Utility.Path.cs` |
| `Utility.Random` | 随机数 | `GameFramework/Utility/Utility.Random.cs` |
| `Utility.Text` | 文本格式化 (通过 ITextHelper) | `GameFramework/Utility/Utility.Text.cs` |
| `Utility.Verifier` / `Utility.Verifier.Crc32` | CRC32 校验 | `GameFramework/Utility/Utility.Verifier*.cs` |
| `Utility.Compression` | 压缩 (通过 ICompressionHelper) | `GameFramework/Utility/Utility.Compression.cs` |
| `Utility.Json` | JSON (通过 IJsonHelper) | `GameFramework/Utility/Utility.Json.cs` |
| `ReferencePool` | 全局对象池 | `GameFramework/Base/ReferencePool/ReferencePool.cs` |
| `GameFrameworkEntry` | 模块注册/轮询/关闭 | `GameFramework/Base/GameFrameworkEntry.cs` |

### 6.5 GF Runtime 扩展方法

| 类名 | 职责 | 文件路径 |
|------|------|---------|
| `StringExtension` | 字符串扩展 | `Scripts/Runtime/Utility/StringExtension.cs` |
| `BinaryExtension` | 二进制读写扩展 | `Scripts/Runtime/Utility/BinaryExtension.cs` |
| `UnityExtension` | Unity 对象扩展 | `Scripts/Runtime/Utility/UnityExtension.cs` |

---

## 7. 第三方依赖列表

| 库名 | 路径 | 用途 |
|------|------|------|
| **UnityGameFramework (GF)** | `Assets/Plugins/UnityGameFramework/` | 游戏框架核心，提供 Entity/UI/Sound/Resource/Procedure 等全套模块化管理 |
| **UniTask** | `Assets/Plugins/UniTask/` | 零 GC 的 Unity async/await 库，替代 Coroutine |
| **DOTween** | `Assets/Plugins/DOTween/` | 缓动动画库 |
| **ZString** | `Assets/Plugins/ZString/` | 零 GC 字符串格式化，作为 GF TextHelper 实现 |
| **protobuf-net** | `Assets/Plugins/Protobuf/` (DLL) | Protocol Buffers 序列化，用于网络通信 |
| **Newtonsoft.Json** | 通过 Helper 引用 | JSON 序列化/反序列化 |
| **BOXOPHOBIC / Polyverse Skies** | `Assets/BOXOPHOBIC/` | 天空盒和美术编辑器工具 |
| **Obfuz** | 通过特性引用 `[Obfuz.ObfuzIgnore]` | 代码混淆工具 |
| **HybridCLR** | `Assets/AAAGame/ScriptsBuiltin/Editor/HybridCLRExtensionTool.cs` | 热更新方案 (CLR 级别) |

---

## 附录：架构概览图

```
┌─────────────────────────────────────────────────────────────────────┐
│                        GF (静态访问门面)                            │
│  GFBuiltin (20个框架组件) + GF (3个项目扩展组件)                     │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ 通过 GameEntry.GetComponent<T>()
┌──────────────────────▼──────────────────────────────────────────────┐
│              GameFrameworkComponent (Unity 层)                       │
│  EntityComponent, UIComponent, EventComponent, ...                  │
│  每个 Component 内部持有对应的 GameFrameworkModule                    │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ 通过 GameFrameworkEntry.GetModule<IXxxManager>()
┌──────────────────────▼──────────────────────────────────────────────┐
│              GameFrameworkModule (C# 纯逻辑层)                      │
│  EntityManager, UIManager, EventManager, ...                        │
│  不依赖 Unity API，由 BaseComponent.Update() 驱动轮询                │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                    项目自定义基础设施                                 │
│                                                                     │
│  Singleton<T>          → MessageSystem (全局消息)                    │
│  MonoBehaviour (手动)   → GroupMoveManager, InteractionManager       │
│  IReference            → PropertyManager (属性注册表)                │
│  ScriptableObject      → 各种 Factory (工厂模式)                    │
│  static class          → EntityRegistry, BrainFactory, DamageHelper │
└─────────────────────────────────────────────────────────────────────┘
```
