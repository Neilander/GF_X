# UI 层代码扫描报告

> 扫描日期: 2026-03-22
> 项目: GF_X (基于 GameFramework / UnityGameFramework)

---

## 1. UIForm 完整继承树

```
MonoBehaviour (Unity)
 |
 +-- UIFormLogic (UnityGameFramework.Runtime)          // 框架抽象基类
 |    |
 |    +-- UIFormBase (项目自定义基类)                   // : UIFormLogic, ISerializeFieldTool
 |         |
 |         +-- MenuUIForm                              // 主菜单
 |         +-- GameUIForm                              // 游戏内 HUD
 |         +-- GameOverUIForm                          // 游戏结算
 |         +-- ShopUIForm                              // 商店（代码全部注释，未启用）
 |         +-- UITopbar                                // 顶部资源栏（子界面）
 |         +-- MaterialModifyBar                       // 材料编辑栏
 |         +-- SettingDialog                           // 设置
 |         +-- RatingDialog                            // 评分
 |         +-- CommonDialog                            // 通用确认对话框
 |         +-- LanguagesDialog                         // 语言选择
 |         +-- ToastTips                               // 吐司提示
 |         +-- TechTreeDialog                          // 科技树
 |         +-- TechNodeDetailTips                      // 科技节点详情（子界面）
 |         +-- InteractOptionTips                      // 交互选项提示
 |         +-- InteractionPanel                        // 交互面板基类
 |              |
 |              +-- CraftingDialog                     // 合成面板
 |
 +-- UIItemBase (项目自定义基类)                       // : MonoBehaviour, ISerializeFieldTool
      |
      +-- LanguageItem                                 // 语言列表项
      +-- CraftingUnit                                 // 合成配方单元
      +-- ItemUnit                                     // 物品单元（显示数量/图标）
      +-- InteractOptionUnit                           // 交互选项单元
      +-- ItemModifyUnit                               // 材料编辑单元（+/- 按钮）
      +-- TextLineUnit                                 // 文本行单元

ObjectBase (GameFramework 对象池)
 |
 +-- UIItemObject                                      // UI Item 对象池包装器
```

### UI 辅助组件（非 UIFormLogic 体系）

```
MonoBehaviour
 |
 +-- HealthBarComp             // 世界空间血条，事件驱动，独立创建
 +-- HoldProgress              // 长按充能进度条（IPointerDown/Up/Exit）
 +-- UIFollowWorldPoint        // 将 RectTransform 锚定到世界坐标点
 +-- InteractOptionTipsPresenter // 监听 InteractionFocusChanged 事件，控制 InteractOptionTips 生命周期
```

---

## 2. UIGroup 分组表

数据来源: `Assets/AAAGame/DataTable/Core/UIGroupTable.txt` + `UITable.txt`

UIGroup 定义 (枚举 `Const.UIGroup`):

| UIGroup 枚举 | DataTable Name | Depth (基础排序) | 用途 |
|---|---|---|---|
| UIForm | UIForm | 1 | 全屏界面 |
| Dialog | Dialog | 200 | 对话框（中层） |
| Tips | Tips | 500 | 提示（顶层） |

### 各 Form 所属 UIGroup

| UIViews 枚举 | ID | 名称 | UIGroup | SortOrder | PauseCovered | EscapeClose | BlockControl |
|---|---|---|---|---|---|---|---|
| MenuUIForm | 1 | 主菜单 | UIForm | 1 | True | False | False |
| GameUIForm | 2 | 游戏界面 | UIForm | 1 | True | False | False |
| GameOverUIForm | 3 | 游戏结算 | UIForm | 1 | True | False | True |
| Topbar | 4 | 顶部资源栏 | UIForm | 2 | False | False | False |
| MaterialModifyBar | 13 | 材料编辑栏 | UIForm | 2 | False | False | False |
| SettingDialog | 5 | 设置 | Dialog | 1 | True | True | True |
| RatingDialog | 6 | 评分 | Dialog | 2 | True | True | True |
| TermsOfServiceDialog | 7 | 服务条款 | Dialog | 2 | True | True | True |
| CommonDialog | 8 | 通用提示 | Dialog | 3 | True | False | True |
| LanguagesDialog | 9 | 语言设置 | Dialog | 2 | False | True | True |
| CraftingDialog | 11 | 合成面板 | Dialog | 1 | False | True | True |
| TechTreeDialog | 14 | 科技树 | Dialog | 1 | False | True | True |
| TechNodeDetailTips | 15 | 科技节点详情 | Dialog | 2 | False | False | True |
| ToastTips | 10 | 吐司提示 | Tips | 1 | False | False | False |
| InteractOptionTips | 12 | 交互选项 | Tips | 2 | False | False | False |

---

## 3. 自定义 UI 组件

### 3.1 UIFormBase -- 项目级 UI 界面基类

文件: `Assets/AAAGame/Scripts/UI/Core/UIFormBase.cs`

继承自 `UIFormLogic`，实现 `ISerializeFieldTool`。封装了以下核心能力:

| 能力 | 说明 |
|---|---|
| **开/关动画** | 支持 DOTween Sequence 和 Animation 两种动画模式 (`UIFormAnimationType`)。关闭时可自动倒放开场动画。调用 `CloseWithAnimation()` 播放关闭动画后自动 `CloseUIForm`。 |
| **UIParams 参数** | 每次 OnOpen 接收 `UIParams` (继承自 `RefParams`)，包含 SortOrder、AllowEscapeClose、回调等。 |
| **子界面管理** | `OpenSubUIForm(UIViews, subUiOrder, params)` / `CloseSubUIForm` / `CloseAllSubUIForms`，子界面随父界面关闭而关闭。 |
| **对象池 Item** | `SpawnItem<T>` / `UnspawnItem<T>` / `UnspawnAllItem<T>`，基于 GF.ObjectPool 管理列表项的创建回收，界面关闭时自动 Unspawn。 |
| **ESC 关闭** | OnUpdate 中检测 Escape 键，仅当本界面是顶层时触发关闭。 |
| **多语言** | `InitLocalization()` 扫描所有 `UIStringKey` 子物体并设置本地化文本。 |
| **按钮回调** | `ClickUIButton(string/Button)` 统一播放点击音效，派发到 `OnButtonClick` 虚方法。 |
| **序列化字段** | `SerializeFieldData[]` + `ISerializeFieldTool` 接口，由编辑器工具自动生成变量绑定代码（各 `*.Variables.cs` 分部类）。 |

### 3.2 InteractionPanel -- 交互面板基类

文件: `Assets/AAAGame/Scripts/UI/InteractionPanel.cs`

继承自 `UIFormBase`，是所有设备交互面板的中间基类。定义了:
- `P_Owner` 常量，用于在 UIParams 中传递所属 `DeviceEntity`。

目前仅 `CraftingDialog` 继承此类。

### 3.3 UIItemBase -- 列表项基类

文件: `Assets/AAAGame/Scripts/UI/Core/UIItemBase.cs`

继承自 `MonoBehaviour`，实现 `ISerializeFieldTool`。用于列表中的单个 Item（与 UIFormLogic 体系无关）。
- `OnInit()` 在 Awake 中调用，自动初始化多语言。
- 子类通过 `partial class` + `*.Variables.cs` 自动绑定 UI 控件引用。

### 3.4 UIItemObject -- 对象池包装器

文件: `Assets/AAAGame/Scripts/UI/Core/UIItemObject.cs`

继承自 `ObjectBase`(GF对象池基类)。作为 `UIItemBase` 的对象池容器:
- `Create<T>(GameObject)` 工厂方法实例化并注册到对象池。
- `OnSpawn` 时激活、`OnUnspawn` 时隐藏。
- `Release` 时 Destroy GameObject。

### 3.5 UIParams -- UI 参数

文件: `Assets/AAAGame/Scripts/UI/Core/UIParams.cs`

继承自 `RefParams`(引用池可回收)。每次打开 UI 时创建，关闭时释放回池。字段:
- `AllowEscapeClose` -- 是否允许 ESC 关闭
- `SortOrder` -- 排序层级
- `IsSubUIForm` -- 是否为子界面
- `OpenCallback` / `CloseCallback` -- 打开/关闭回调
- `ButtonClickCallback` -- 按钮点击回调
- 可通过 `Set<T>(key, value)` / `Get<T>(key)` 存取自定义键值对。

### 3.6 UIViews 枚举

文件: `Assets/AAAGame/Scripts/UI/Core/UIViews.cs`

工具自动生成，枚举所有界面 ID (与 UITable 行 ID 一一对应)。

### 3.7 辅助组件

| 组件 | 文件 | 说明 |
|---|---|---|
| HoldProgress | `Assets/AAAGame/Scripts/MeiyouUtility/UI/HoldProgress.cs` | 长按充能进度条，支持 IPointerDown/Up/Exit，充满触发 `onFull` 回调。用于 CraftingUnit 的制造按钮。 |
| UIFollowWorldPoint | `Assets/AAAGame/Scripts/MeiyouUtility/UI/UIFollowWorldPoint.cs` | 将 RectTransform 投影到世界坐标点，LateUpdate + Canvas.willRenderCanvases 双重更新保证位置精确。用于 InteractOptionTips。 |
| HealthBarComp | `Assets/AAAGame/Scripts/UI/HealthBarComp.cs` | 世界空间血条。通过 `HealthBarComp.Create()` 工厂方法在运行时创建独立 Canvas(WorldSpace)。监听 `CreatureHealthChangedEventArgs` 按 entityId 过滤。单位死亡后自动销毁。 |
| InteractOptionTipsPresenter | `Assets/AAAGame/Scripts/Interaction/InteractOptionTipsPresenter.cs` | 监听 `InteractionFocusChangedEventArgs`，管理 InteractOptionTips 的生命周期（打开/关闭/复用）。 |
| CustomUIGroupHelper | `Assets/AAAGame/ScriptsBuiltin/Runtime/GFHelper/CustomUIGroupHelper.cs` | UIGroup 的自定义 Helper，为每个 UIGroup 创建独立 Canvas + GraphicRaycaster。 |

---

## 4. UI 与逻辑层的交互模式

### 4.1 事件驱动模式 (主流)

项目广泛使用 `GF.Event`（GameFramework 全局事件系统）实现 UI 与逻辑层的解耦:

```
逻辑层 DataModel --> 触发事件 (EventArgs) --> GF.Event.Fire
                                                  |
UI 层 OnOpen 时 Subscribe, OnClose 时 Unsubscribe <--+
```

常用事件:
| 事件类 | 发布者 | 订阅的 UI |
|---|---|---|
| `PlayerDataChangedEventArgs` | PlayerDataModel | MenuUIForm, UITopbar |
| `ItemAmountChangedEventArgs` | ItemCollectionDataModel | CraftingDialog, InteractOptionTips, MaterialModifyBar, TechTreeDialog |
| `ProfileDataChangedEventArgs` | ProfileDataModel | TechTreeDialog |
| `TechUnlockedEventArgs` | TechProgressDataModel | TechTreeDialog |
| `InteractionFocusChangedEventArgs` | 交互系统 | InteractOptionTipsPresenter |
| `InteractionOptionTriggeredEventArgs` | 交互系统 | InteractOptionTips |
| `CreatureHealthChangedEventArgs` | 战斗系统 | HealthBarComp |
| `LoadDictionarySuccessEventArgs` | GF.Localization | SettingDialog |

### 4.2 DataModel 直接引用

UI 直接通过 `GF.DataModel.GetOrCreate<T>()` 获取 DataModel 读取数据:
- `PlayerDataModel` -- 金币、钻石、能量等
- `ItemCollectionDataModel` -- 背包物品数量（静态方法调用）
- `TechProgressDataModel` -- 科技解锁状态
- `TechNodeDataModel` -- 科技节点配置
- `ProfileDataModel` -- 玩家档案（基地等级等）
- `CraftingDeviceDataModel` -- 合成配方
- `ItemDataModel` -- 物品静态数据
- `LocalizationTextDataModel` -- 多语言文本

### 4.3 DataTable 数据表

UI 通过 `GF.DataTable.GetDataTable<T>()` 读取配置表:
- `UITable` / `UIGroupTable` -- 界面配置
- `LanguagesTable` -- 语言列表
- `ItemTable` -- 物品表

### 4.4 UIParams 参数传递

打开界面时通过 `UIParams.Create()` + `Set<VarXxx>(key, value)` 传递参数，界面在 OnOpen 中通过 `Params.Get<VarXxx>(key)` 读取。支持的类型包括:
- `VarBoolean`, `VarString`, `VarFloat`, `VarUInt32`, `VarVector2`
- `VarAction` (回调委托)
- `VarObject` (任意引用对象，如 `GameFrameworkAction`)
- 直接 `Set(key, object)` / `Get(key)` 存取引用

### 4.5 回调模式

- `UIParams.OpenCallback` / `CloseCallback` -- 界面生命周期回调
- `UIParams.ButtonClickCallback` -- 按钮点击回调
- `VarAction` 传递 -- 如 LanguagesDialog 通过 `P_LangChangedCb` 将语言切换回调传回给 SettingDialog

### 4.6 子界面模式

父界面通过 `OpenSubUIForm(UIViews, order, params)` 打开子界面，子界面随父界面关闭。典型用例:
- MenuUIForm -> UITopbar
- GameUIForm -> UITopbar
- TechTreeDialog -> TechNodeDetailTips

### 4.7 Presenter 模式

`InteractOptionTipsPresenter` 挂在常驻对象上，作为逻辑层与 UI 之间的中介，监听交互事件并控制 Tips 的打开/关闭/复用。

---

## 5. 界面清单

### 5.1 UIForm 组（全屏界面）

| 文件路径 | UIGroup | 功能描述 | 引用的数据类/逻辑类 | 打开的子界面 |
|---|---|---|---|---|
| `Assets/AAAGame/Scripts/UI/MenuUIForm.cs` | UIForm | 主菜单界面，显示金币，监听玩家数据变化刷新。通过按钮打开设置。 | PlayerDataModel, PlayerDataChangedEventArgs | UITopbar, SettingDialog |
| `Assets/AAAGame/Scripts/UI/GameUIForm.cs` | UIForm | 游戏内 HUD，显示金币数量。 | PlayerDataModel | UITopbar |
| `Assets/AAAGame/Scripts/UI/GameOverUIForm.cs` | UIForm | 游戏结算界面，根据参数 P_IsWin 显示胜利/失败。 | GF.Localization | -- |
| `Assets/AAAGame/Scripts/UI/UITopbar.cs` | UIForm | 顶部资源栏（金币/钻石/能量），作为子界面嵌入 MenuUIForm/GameUIForm。通过按钮打开设置。 | PlayerDataModel, PlayerDataChangedEventArgs | SettingDialog |
| `Assets/AAAGame/Scripts/UI/MaterialModifyBar.cs` | UIForm | 材料编辑栏，列出所有材料并提供 +/- 按钮（测试用）。 | ItemCollectionDataModel, ItemAmountChangedEventArgs, ItemTable, ItemDataModel | -- |
| `Assets/AAAGame/Scripts/UI/ShopUIForm.cs` | UIForm | 商店界面（代码全部注释，未启用）。 | -- | -- |

### 5.2 Dialog 组（对话框）

| 文件路径 | UIGroup | 功能描述 | 引用的数据类/逻辑类 | 打开的子界面 |
|---|---|---|---|---|
| `Assets/AAAGame/Scripts/UI/SettingDialog.cs` | Dialog | 设置界面：音量滑块、振动开关、语言切换、评分入口、隐私/帮助/服务条款入口。版本号连续点击5次打开调试器。 | GF.Setting, LanguagesTable, LoadDictionarySuccessEventArgs | LanguagesDialog, RatingDialog |
| `Assets/AAAGame/Scripts/UI/RatingDialog.cs` | Dialog | 评分界面：5 星评分，>=4 星跳转应用商店。 | GF.Config (AppStore URL) | -- |
| `Assets/AAAGame/Scripts/UI/CommonDialog.cs` | Dialog | 通用确认对话框：标题+内容+确定/取消按钮，通过 UIParams 传入 PositiveAction/NegativeAction 回调。 | -- | -- |
| `Assets/AAAGame/Scripts/UI/LanguagesDialog.cs` | Dialog | 语言选择列表，使用对象池 SpawnItem 生成 LanguageItem 列表项。选中后通过 VarAction 回调通知调用方。 | LanguagesTable, VarAction | -- |
| `Assets/AAAGame/Scripts/UI/CraftingDialog.cs` | Dialog | 合成面板，显示设备可用配方、所需材料及产出物品。长按制造按钮(HoldProgress)消耗材料产出物品。继承自 InteractionPanel。 | CraftingDeviceDataModel, ItemCollectionDataModel, ItemAmountChangedEventArgs, DeviceEntity | -- |
| `Assets/AAAGame/Scripts/UI/TechTreeDialog.cs` | Dialog | 科技树，扫描子物体中的 TechNodeView 显示科技节点状态（已解锁/可研究/锁定），点击节点弹出详情子界面。 | TechProgressDataModel, TechNodeDataModel, ItemAmountChangedEventArgs, ProfileDataChangedEventArgs, TechUnlockedEventArgs | TechNodeDetailTips (子界面) |
| `Assets/AAAGame/Scripts/UI/TechNodeDetailTips.cs` | Dialog | 科技节点详情面板：显示名称、描述、解锁条件、花费材料、研究按钮。点击研究后调用 TechProgressDataModel.Research()。 | TechNodeDataModel, TechProgressDataModel, ProfileDataModel, ItemCollectionDataModel, UnlockCondition, LocalizationTextDataModel | -- |
| `Assets/AAAGame/Scripts/UI/InteractionPanel.cs` | Dialog | 交互面板抽象基类，定义 P_Owner 参数。由 DeviceOpenPanelInteractionOption 通过 deviceData.InteractionPanelID 动态打开。 | DeviceEntity | -- |

### 5.3 Tips 组（提示）

| 文件路径 | UIGroup | 功能描述 | 引用的数据类/逻辑类 | 打开的子界面 |
|---|---|---|---|---|
| `Assets/AAAGame/Scripts/UI/ToastTips.cs` | Tips | 吐司提示，显示一段时间后自动关闭。支持多种样式 (Blue/Yellow/Green/Red/White)。 | UIExtension.ToastStyle | -- |
| `Assets/AAAGame/Scripts/UI/InteractOptionTips.cs` | Tips | 交互选项提示，跟随世界坐标点(UIFollowWorldPoint)，显示可用交互选项及其快捷键和花费材料。 | InteractionHost, IInteractionOption, ItemCollectionDataModel, InteractionOptionTriggeredEventArgs, ItemAmountChangedEventArgs | -- |

### 5.4 UIItem（列表项组件）

| 文件路径 | 继承 | 功能描述 | 引用的数据类 |
|---|---|---|---|
| `Assets/AAAGame/Scripts/UI/Item/LanguageItem.cs` | UIItemBase | 语言列表项，Toggle 切换语言 | LanguagesTable, GF.Setting |
| `Assets/AAAGame/Scripts/UI/Item/CraftingUnit.cs` | UIItemBase | 合成配方单元（壳子，逻辑在 CraftingDialog 中通过 varFillProgress 绑定） | -- |
| `Assets/AAAGame/Scripts/UI/Item/ItemUnit.cs` | UIItemBase | 物品单元，显示物品图标及"需求(拥有)"数量文本，不足标红 | ItemCollectionDataModel, ItemDataModel |
| `Assets/AAAGame/Scripts/UI/Item/InteractOptionUnit.cs` | UIItemBase | 交互选项单元，显示选项名称和快捷键，不可用时降低透明度 | -- |
| `Assets/AAAGame/Scripts/UI/Item/ItemModifyUnit.cs` | UIItemBase | 材料编辑单元，+/- 按钮增减物品数量（测试用） | ItemCollectionDataModel, ItemDataModel |
| `Assets/AAAGame/Scripts/UI/Item/TextLineUnit.cs` | UIItemBase | 文本行单元（壳子），用于 TechNodeDetailTips 显示条件列表 | -- |

### 5.5 非 UIFormLogic 体系的 UI 组件

| 文件路径 | 功能描述 | 引用的数据类/事件 |
|---|---|---|
| `Assets/AAAGame/Scripts/UI/HealthBarComp.cs` | 世界空间血条，跟随实体 Transform，面向主摄像机。工厂方法 `Create()` 运行时生成独立 Canvas。 | CreatureHealthChangedEventArgs |
| `Assets/AAAGame/Scripts/MeiyouUtility/UI/HoldProgress.cs` | 长按充能 UI 组件，用于合成按钮 | -- |
| `Assets/AAAGame/Scripts/MeiyouUtility/UI/UIFollowWorldPoint.cs` | 将 RectTransform 投影到世界坐标点 | -- |
| `Assets/AAAGame/Scripts/Interaction/InteractOptionTipsPresenter.cs` | Presenter：监听 InteractionFocusChangedEventArgs，管理 InteractOptionTips 界面生命周期 | InteractionFocusChangedEventArgs |

---

## 附录: 关键文件路径索引

### 框架层
- `Assets/Plugins/UnityGameFramework/Scripts/Runtime/UI/UIFormLogic.cs` -- 框架 UI 逻辑基类
- `Assets/Plugins/UnityGameFramework/Scripts/Runtime/UI/UIForm.cs` -- 框架 UI 实体
- `Assets/Plugins/UnityGameFramework/Scripts/Runtime/UI/UIComponent.cs` -- 框架 UI 组件

### 项目核心
- `Assets/AAAGame/Scripts/UI/Core/UIFormBase.cs` -- 项目 UI 基类
- `Assets/AAAGame/Scripts/UI/Core/UIItemBase.cs` -- 项目 Item 基类
- `Assets/AAAGame/Scripts/UI/Core/UIItemObject.cs` -- Item 对象池包装
- `Assets/AAAGame/Scripts/UI/Core/UIParams.cs` -- UI 参数
- `Assets/AAAGame/Scripts/UI/Core/UIViews.cs` -- 界面枚举（自动生成）
- `Assets/AAAGame/Scripts/Common/Core/Const.Groups.cs` -- UIGroup/EntityGroup/SoundGroup 枚举
- `Assets/AAAGame/Scripts/Extension/UIExtension.cs` -- UI 扩展方法（OpenUIForm、ShowToast、Close 等）

### 数据表
- `Assets/AAAGame/DataTable/Core/UITable.txt` -- UI 界面配置表
- `Assets/AAAGame/DataTable/Core/UIGroupTable.txt` -- UIGroup 配置表
- `Assets/AAAGame/Scripts/DataTable/Core/UITable.cs` -- UITable 数据行类
- `Assets/AAAGame/Scripts/DataTable/Core/UIGroupTable.cs` -- UIGroupTable 数据行类
