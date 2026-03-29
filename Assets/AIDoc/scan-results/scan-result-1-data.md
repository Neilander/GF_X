# GF_X Data 层扫描报告

> 扫描日期: 2026-03-22
> 扫描范围: `Assets/AAAGame/Scripts/` 下所有数据定义类
> 说明: GF 框架自带基类仅列类名不展开，跨层类已标注

---

## 目录

1. [ScriptableObject 完整继承树](#1-scriptableobject-完整继承树)
2. [DataTable / DataRow 继承树](#2-datatable--datarow-继承树)
3. [DataModel 继承树](#3-datamodel-继承树)
4. [枚举清单](#4-枚举清单)
5. [每个数据类的详细信息](#5-每个数据类的详细信息)

---

## 1. ScriptableObject 完整继承树

```
ScriptableObject (UnityEngine)
├── AppConfigs
├── AppSettings
├── BehaviorTreeGraph
├── BasicSkill
│   └── SelectPositionSkill
├── BasicAction                          [跨层: ActionSystem]
│   ├── NormalAttackAction
│   ├── ProjectileSpawnAction
│   └── PositionSelectAction
├── AbstractNode                         [跨层: BehaviorTree]
│   ├── RootNode
│   ├── SelectorNode
│   ├── SequenceNode
│   ├── SetMessageNode
│   ├── CheckPriorityNode
│   └── CheckHealthNode
├── TargetingCompFactory (abstract)      [跨层: GeneralCreature]
│   ├── NoTargetingFactory
│   └── CharacterTargetingFactory
├── MoveCompFactory (abstract)           [跨层: GeneralCreature]
│   ├── NoMoveFactory
│   ├── CharacterMoveFactory
│   └── PlayerMoveFactory
├── AtkCompFactory (abstract)            [跨层: GeneralCreature]
│   ├── NoAtkFactory
│   ├── DirectAtkCompFactory
│   ├── CharacterAtkFactory
│   └── PlayerAtkFactory
└── SkillCompFactory (abstract)          [跨层: PlayerControl]
    ├── PlayerSkillFactory
    └── CharacterSkillFactory
```

---

## 2. DataTable / DataRow 继承树

```
IDataRow (GF框架接口)
└── DataRowBase (GF框架抽象基类)
    ├── [Core]
    │   ├── EntityGroupTable
    │   ├── UITable
    │   ├── UIGroupTable
    │   ├── SoundGroupTable
    │   └── LanguagesTable
    ├── [Text]
    │   ├── InteractOptionTextTable
    │   └── LocalizationTextTable
    ├── [Item]
    │   ├── ItemTable
    │   ├── ItemWithEffectTable
    │   └── CraftingFormulaTable
    ├── [Build]
    │   └── DeviceTable
    ├── [Tech]
    │   └── TechNodeTable
    ├── CharacterMainPropertyTable
    ├── CharacterMAFactoryTable
    ├── CameraViewTable
    ├── ColorTable
    ├── CombatUnitTable
    └── LevelTable
```

### DataRow 字段列表

| 表名 | 字段 | 文件路径 |
|------|------|---------|
| **EntityGroupTable** | Id, Name, ReleaseInterval(float), Capacity(int), ExpireTime(float), Priority(int) | `Scripts/DataTable/Core/EntityGroupTable.cs` |
| **UITable** | Id, SortOrder(int), UIPrefab(string), PauseCoveredUI(bool), UIGroupId(int), EscapeClose(bool), BlockCharacterControl(bool) | `Scripts/DataTable/Core/UITable.cs` |
| **UIGroupTable** | Id, Name(string), Depth(int) | `Scripts/DataTable/Core/UIGroupTable.cs` |
| **SoundGroupTable** | Id, Name(string), SoundAgentCount(int), AvoidBeingReplacedBySamePriority(bool), Mute(bool), Volume(float) | `Scripts/DataTable/Core/SoundGroupTable.cs` |
| **LanguagesTable** | Id, LanguageKey(string), AssetName(string), LanguageDisplay(string), LanguageIcon(string) | `Scripts/DataTable/Core/LanguagesTable.cs` |
| **InteractOptionTextTable** | Id, Identifier(string), Text(string) | `Scripts/DataTable/Text/InteractOptionTextTable.cs` |
| **LocalizationTextTable** | Id, Identifier(string), TextKey(string) | `Scripts/DataTable/Text/LocalizationTextTable.cs` |
| **ItemTable** | Id, Identifier(string), Rarity(ItemRarity), MaxStack(int), SpriteName(string), NameKey(string), DescriptionKey(string), Tags(ItemTag[]) | `Scripts/DataTable/Item/ItemTable.cs` |
| **ItemWithEffectTable** | Id, Identifier(string), Rarity(ItemRarity), MaxStack(int), PropertyNumerals(StringFix64Pair[]), EffectNumerals(Fix64[]), EffectIntroduction(string), SpriteName(string), NameKey(string), DescriptionKey(string), Tags(ItemTag[]) | `Scripts/DataTable/Item/ItemWithEffectTable.cs` |
| **CraftingFormulaTable** | Id, CraftingPlace(string), ProducedItems(StringIntPair[]), RequiredItems(StringIntPair[]), Workload(Fix64) | `Scripts/DataTable/Item/CraftingFormulaTable.cs` |
| **DeviceTable** | Id, Identifier(string), CostMaterial(StringIntPair[]), Workload(Fix64), PrefabName(string), NameKey(string), DescriptionKey(string), BuildCondition(UnlockCondition), UpgradeID(string), InteractionPanelID(UIViews?) | `Scripts/DataTable/Build/DeviceTable.cs` |
| **TechNodeTable** | Id, Identifier(string), PrereqTechIds(string[]), Level(int), Category(TechCategory), CostMaterial(StringIntPair[]), AllConditions(UnlockCondition[]), AnyConditions(UnlockCondition[]), SpriteName(string), NameKey(string), DescriptionKey(string) | `Scripts/DataTable/Tech/TechNodeTable.cs` |
| **CharacterMainPropertyTable** | Id, CharacterKey(string), PhysicalAtk(int), SpecialAtk(int), PhysicalDef(int), SpecialDef(int), Health(int), Speed(int), Mana(int) | `Scripts/DataTable/CharacterMainPropertyTable.cs` |
| **CharacterMAFactoryTable** | Id, CharacterKey(string), MoveFactoryPath(string), AttackFactoryPath(string) | `Scripts/DataTable/CharacterMAFactoryTable.cs` |
| **CameraViewTable** | Id, FollowOffset(Vector3), AimOffset(Vector3) | `Scripts/DataTable/CameraViewTable.cs` |
| **ColorTable** | Id, ColorHex(string) | `Scripts/DataTable/ColorTable.cs` |
| **CombatUnitTable** | Id, PrefabName(string), AttackRadius(float), MoveSpeed(float), Hp(int), Damage(int), MaxAttackCount(int) | `Scripts/DataTable/CombatUnitTable.cs` |
| **LevelTable** | Id, LvPfbName(string), InitMoney(int), MoneyColorId(int), LvDisplayName(string) | `Scripts/DataTable/LevelTable.cs` |

---

## 3. DataModel 继承树

```
IReference (GF框架接口)
└── DataModelBase (abstract)
    │   字段: Id(int), Userdata(RefParams)
    │   命名空间: GameFramework
    │
    ├── DataModelStorageBase (abstract)    [支持 JSON 持久化]
    │   │   字段: StorageKey(string)
    │   │
    │   ├── PlayerDataModel
    │   ├── ProfileDataModel
    │   ├── ItemCollectionDataModel
    │   ├── TechProgressDataModel
    │   └── CapabilityProgressDataModel
    │
    ├── InputModel
    ├── DeviceDataModel
    ├── ItemDataModel
    ├── CraftingDeviceDataModel
    ├── TechNodeDataModel
    └── LocalizationTextDataModel
```

### DataModel 字段列表

| 类名 | 基类 | 关键字段 | 文件路径 |
|------|------|---------|---------|
| **PlayerDataModel** | DataModelStorageBase | m_PlayerDataDic(Dict\<PlayerDataType,int\>), Hp, Coins, LevelId | `Scripts/DataModel/PlayerDataModel.cs` |
| **ProfileDataModel** | DataModelStorageBase | m_ProfileDataDic(Dict\<ProfileDataType,int\>), BaseLevel | `Scripts/DataModel/ProfileDataModel.cs` |
| **ItemCollectionDataModel** | DataModelStorageBase | itemCollectionDataDic(Dict\<string,int\>) | `Scripts/DataModel/Craft/ItemCollectionDataModel.cs` |
| **TechProgressDataModel** | DataModelStorageBase | m_UnlockedTechIds(HashSet\<string\>), m_UnlockOrder(List\<string\>), m_UnlockTimeTicks(Dict\<string,long\>) | `Scripts/DataModel/Tech/TechProgressDataModel.cs` |
| **CapabilityProgressDataModel** | DataModelStorageBase | m_Capabilities(HashSet\<string\>) | `Scripts/DataModel/Tech/CapabilityProgressDataModel.cs` |
| **InputModel** | DataModelBase | MoveX(Fix64), MoveY(Fix64), InteractionPressed, Interaction2Pressed, Interaction3Pressed, OpenTechTreePressed, PlayerAttack, Skill1-3Pressed, SelectScreenPosition(Vector2), SkillConfirmPressed | `Scripts/DataModel/InputModel.cs` |
| **DeviceDataModel** | DataModelBase | deviceDataDic(Dict\<string,Device\>) | `Scripts/DataModel/Build/DeviceDataModel.cs` |
| **ItemDataModel** | DataModelBase | itemDataDic(Dict\<string,Item\>) | `Scripts/DataModel/Craft/ItemDataModel.cs` |
| **CraftingDeviceDataModel** | DataModelBase | craftingDeviceDataDic(Dict\<string,List\<CraftingFormula\>\>) | `Scripts/DataModel/Craft/CraftingDeviceDataModel.cs` |
| **TechNodeDataModel** | DataModelBase | m_NodeDatas(Dict\<string,TechNodeTable\>) | `Scripts/DataModel/Tech/TechNodeDataModel.cs` |
| **LocalizationTextDataModel** | DataModelBase | _localizationKeyDic(Dict\<string,string\>) | `Scripts/DataModel/Text/LocalizationTextDataModel.cs` |

---

## 4. 枚举清单

### 4.1 核心分组枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **EntityGroup** | Default, Player, Effect, Item, Level, Bullet, Unrecycle, Building | `Scripts/Common/Core/Const.Groups.cs` |
| **UIGroup** | UIForm, Dialog, Tips | `Scripts/Common/Core/Const.Groups.cs` |
| **SoundGroup** | Music, Sound, Vibrate, Joystick | `Scripts/Common/Core/Const.Groups.cs` |

### 4.2 数据模型枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **PlayerDataType** | Coins, Diamond, Hp, Energy, LevelId | `Scripts/DataModel/PlayerDataModel.cs` |
| **ProfileDataType** | BaseLevel | `Scripts/DataModel/ProfileDataModel.cs` |
| **InputKey** | InteractionPrimary(0), InteractionSecondary(1), InteractionTertiary(2) | `Scripts/DataModel/InputModel.cs` |

### 4.3 物品 & 制造枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **ItemRarity** | Common, Rare, Epic, Legendary, Heroic | `Scripts/Craft/Item.cs` |
| **ItemType** | Consumable, Equipment, Material, Tool | `Scripts/Craft/Item.cs` |
| **ItemTag** | AAA, BBB, CCC, DDD | `Scripts/Craft/Item.cs` |

### 4.4 科技 & 解锁枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **TechCategory** | Explore(0), Fight(1), Craft(2), Efficiency(3), Profit(4) | `Scripts/Tech/TechCategory.cs` |
| **UnlockConditionType** | BaseLevel(0), Tech(1), Capability(2), Dialogue(3) | `Scripts/UnlockCondition/UnlockCondition.cs` |
| **TechResearchFailReason** | None(0), InvalidTechId(1), TechNotFound(2), AlreadyUnlocked(3), MissingPrereqTech(10), BaseLevelTooLow(11), ConditionNotMet(12), InvalidConditionArgs(13), UnknownCondition(14), NotEnoughCost(20) | `Scripts/EventArgs/Tech/TechResearchFailReason.cs` |

### 4.5 战斗 & 角色属性枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **CreatureMainProperty** | PhysicalAtk, SpecialAtk, PhysicalDef, SpecialDef, Health, Speed, Mana | `Scripts/Property/CreaturePropertyManager.cs` |
| **CreatureMinorProperty** | HealthRecover, ManaRecover | `Scripts/Property/CreaturePropertyManager.cs` |
| **CreatureCurrentProperty** | HealthCurrent, ManaCurrent | `Scripts/Property/CreaturePropertyManager.cs` |
| **RawComponent** | Level, Config | `Scripts/Property/CreaturePropertyManager.cs` |
| **NormalBaseValueTp** | Base, Buff | `Scripts/Property/CreaturePropertyManager.cs` |
| **NormalComputeTp** | Value, Mul | `Scripts/Property/CreaturePropertyManager.cs` |
| **WeaponType** | Melee, Projectile, InstantRanged | `Scripts/GeneralCreature/WeaponData.cs` |
| **AtkState** | Idle, WindUp, WindDown, Cooldown | `Scripts/GeneralCreature/DirectAtkComp.cs` |
| **HealthModifyType** | reduce, mult, set, empty | `Scripts/GeneralCreature/HealthContainer.cs` |
| **SideType** | NoSide, PlayerSide, EnemySide | `Scripts/GeneralCreature/GeneralCreature.cs` |
| **CoolDownType** | Count | `Scripts/SkillSystem/BasicSkill.cs` |

### 4.6 属性修改器枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **EModifierMergeType** | DirectAdditive, FinalAdditive, IrreversibleAdditive, DirectMultiplicative, FinalMultiplicative, IrreversibleMultiplicative, Override, IrreversibleOverride, Clamp, Vector2IntArrayAdditive, Vector2IntArrayPreOverride, Vector2IntArrayFinalOverride, RangeAdditive | `Scripts/Property/Modifier/IPropertyModifier.cs` |

### 4.7 AI & 移动枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **BrainType** | Player(0), EnemyAI(1), FriendlyAI(2), SoldierAI(3) | `Scripts/Entity/IControlBrain.cs` |
| **SoldierState** | Idle, Follow, Combat | `Scripts/Movement/SoldierAIBrain.cs` |
| **AgentState** | Idle, Follow, Combat | `Scripts/Movement/GroupMoveCoordinator.cs` |
| **NodeState** | Success, Failure | `Scripts/BehaviorTree/AbstractNode.cs` |
| **EntityPresetPointType** | Spawn, Respawn, Patrol, Device | `Scripts/MeiyouUtility/EntityPresetPoint.cs` |

### 4.8 UI & 输入枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **UIViews** | MenuUIForm(1), GameUIForm(2), GameOverUIForm(3), Topbar(4), MaterialModifyBar(13), SettingDialog(5), RatingDialog(6), TermsOfServiceDialog(7), CommonDialog(8), LanguagesDialog(9), CraftingDialog(11), TechTreeDialog(14), TechNodeDetailTips(15), ToastTips(10), InteractOptionTips(12) | `Scripts/UI/Core/UIViews.cs` |
| **InputState** | None, StartScreen, Game, UIForm | `Scripts/UTManagers/InputManager.cs` |
| **PivotAxis** | Free, X, Y | `Scripts/Entity/Core/BillboardEntity.cs` |
| **ToastStyle** | Blue(0), Yellow(1), Green(2), Red(3), White(4) | `Scripts/Extension/UIExtension.cs` |

### 4.9 事件枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **GameplayEventType** | GameOver | `Scripts/EventArgs/GameplayEventArgs.cs` |
| **GFEventType** | ApplicationQuit | `Scripts/EventArgs/GFEventArgs.cs` |
| **TempActions** | KillPlayer | `Scripts/NeilUtility/DamageSystem.cs` |

### 4.10 网络 & 广告枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **PacketType** | Undefined(0), ClientToServer(1), ServerToClient(2) | `Scripts/Network/PacketType.cs` |
| **ADResult** | Open, Close | `Scripts/Extension/AD/ADComponent.cs` |
| **ADTriggerPoint** | CloseUpgradeView, CollectMoneyStack, PlayerStayIdle, OfflineBonus, BuffItem, Upgrade | `Scripts/Extension/AD/ADComponent.cs` |

### 4.11 调试枚举

| 枚举名 | 值 | 文件路径 |
|--------|---|---------|
| **DebugCategory** | Targeting, Attack, Move, Brain, GroupMove | `Scripts/Common/GameDebugSettings.cs` |

---

## 5. 每个数据类的详细信息

### 5.1 ScriptableObject 类

---

#### AppConfigs
- **文件**: `Scripts/ScriptableObject/AppConfigs.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `[SerializeField] bool m_LoadFromBytes` — 是否从二进制加载
  - `[SerializeField] string[] mDataTables` — 数据表列表
  - `[SerializeField] string[] mConfigs` — 配置列表
  - `[SerializeField] string[] mLanguages` — 语言列表
  - `[SerializeField] string[] mProcedures` — 流程列表

#### AppSettings
- **文件**: `ScriptsBuiltin/Runtime/ScriptableObject/AppSettings.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `public bool DebugMode`
  - `public ResourceMode ResourceMode`
  - `public string CheckVersionUrl`
  - `public Vector2Int DesignResolution`
  - `public string[] EncryptAOTDlls`

#### BehaviorTreeGraph
- **文件**: `Scripts/BehaviorTree/BehaviorTreeGraph.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `public RootNode root`
  - `public List<AbstractNode> nodes`
  - `public List<NodeConnection> connections`
  - `public string boundContextTypeName`

#### BasicSkill
- **文件**: `Scripts/SkillSystem/BasicSkill.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `public List<BasicAction> actions`
  - `public CoolDownType coolDownType`
  - `public float coolDownInterval`
  - `public bool banWhenOtherSkill`
  - `public bool banOtherSkillWhenCast`
  - `public float radius`
  - `public Vector3 selectRatio`

#### SelectPositionSkill
- **文件**: `Scripts/SkillSystem/SelectPositionSkill.cs`
- **基类**: BasicSkill
- **命名空间**: 全局
- **字段**: 无额外字段

---

#### BasicAction (abstract) [跨层: ActionSystem]
- **文件**: `Scripts/ActionSystem/BasicAction.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `[SerializeField] bool ifUseDuration`
  - `[SerializeField] float duration`
  - `[SerializeField] float acceptInputFromPercent`
  - `public string relatedTriggerString`

#### NormalAttackAction
- **文件**: `Scripts/ActionSystem/NormalAttackAction.cs`
- **基类**: BasicAction
- **字段**:
  - `[SerializeField] float damageStartPercent`
  - `[SerializeField] float damageEndPercent`
  - `[SerializeField] GameObject hitboxPrefab`
  - `[SerializeField] Vector3 hitboxScale`
  - `[SerializeField] Vector3 relativeOffset`

#### ProjectileSpawnAction
- **文件**: `Scripts/ActionSystem/ProjectileSpawnAction.cs`
- **基类**: BasicAction
- **字段**:
  - `[SerializeField] List<StringPercentPair> projectileNamePercentPairs`

#### PositionSelectAction
- **文件**: `Scripts/ActionSystem/PositionSelectAction.cs`
- **基类**: BasicAction
- **字段**:
  - `[SerializeField] string posSelectPrefabName`

---

#### AbstractNode (abstract) [跨层: BehaviorTree]
- **文件**: `Scripts/BehaviorTree/AbstractNode.cs`
- **基类**: ScriptableObject
- **命名空间**: 全局
- **字段**:
  - `public List<AbstractNode> children`
  - `public AbstractNode parent`
  - `public Rect nodeRect`
  - `public string nodeID`
  - `public string note`

#### RootNode / SelectorNode / SequenceNode
- **文件**: `Scripts/BehaviorTree/` 下各文件
- **基类**: AbstractNode
- **字段**: 无额外字段

#### SetMessageNode
- **文件**: `Scripts/BehaviorTree/Node/SetMessageNode.cs`
- **基类**: AbstractNode
- **字段**: `public string message`

#### CheckPriorityNode
- **文件**: `Scripts/BehaviorTree/Node/CheckPriorityNode.cs`
- **基类**: AbstractNode
- **字段**: `public int requiredPriority`, `public bool isGreater`

#### CheckHealthNode
- **文件**: `Scripts/BehaviorTree/Node/CheckHealthNode.cs`
- **基类**: AbstractNode
- **字段**: `public float threshold`, `public bool isBelow`

---

#### TargetingCompFactory (abstract) [跨层: GeneralCreature]
- **文件**: `Scripts/GeneralCreature/NoTargetingFactory.cs`
- **基类**: ScriptableObject
- **命名空间**: AAAGame.Scripts.GeneralCreature

#### NoTargetingFactory
- **文件**: `Scripts/GeneralCreature/NoTargetingFactory.cs`
- **基类**: TargetingCompFactory
- **字段**: 无

#### CharacterTargetingFactory
- **文件**: `Scripts/PlayerControl/CharacterTargetingFactory.cs`
- **基类**: TargetingCompFactory
- **字段**:
  - `public float defaultAggroRange`
  - `public float defaultForgetRange`
  - `public float defaultFollowRange`

---

#### MoveCompFactory (abstract) [跨层: GeneralCreature]
- **文件**: `Scripts/GeneralCreature/NoMoveFactory.cs`
- **基类**: ScriptableObject

#### NoMoveFactory
- **文件**: `Scripts/GeneralCreature/NoMoveFactory.cs`
- **基类**: MoveCompFactory
- **字段**: 无

#### CharacterMoveFactory
- **文件**: `Scripts/PlayerControl/CharacterMoveFactory.cs`
- **基类**: MoveCompFactory
- **命名空间**: AAAGame.Scripts.PlayerControl
- **字段**: 无

#### PlayerMoveFactory
- **文件**: `Scripts/PlayerControl/PlayerMoveFactory.cs`
- **基类**: MoveCompFactory
- **字段**: 无

---

#### AtkCompFactory (abstract) [跨层: GeneralCreature]
- **文件**: `Scripts/GeneralCreature/NoAtkFactory.cs`
- **基类**: ScriptableObject

#### NoAtkFactory
- **文件**: `Scripts/GeneralCreature/NoAtkFactory.cs`
- **基类**: AtkCompFactory
- **字段**: 无

#### DirectAtkCompFactory
- **文件**: `Scripts/GeneralCreature/DirectAtkCompFactory.cs`
- **基类**: AtkCompFactory
- **字段**:
  - `public float damage`
  - `public float attackInterval`
  - `public WeaponType weaponType`
  - `public float attackRange`
  - `public float projectileSpeed`
  - `public float windUp`
  - `public float windDown`
  - `public float splashRadius`
  - `public float manaCost`

#### CharacterAtkFactory
- **文件**: `Scripts/PlayerControl/CharacterAtkFactory.cs`
- **基类**: AtkCompFactory
- **命名空间**: AAAGame.Scripts.PlayerControl
- **字段**:
  - `public BasicAction atkAction1`
  - `public BasicAction atkAction2`
  - `public BasicAction atkAction3`

#### PlayerAtkFactory
- **文件**: `Scripts/PlayerControl/PlayerAtkFactory.cs`
- **基类**: AtkCompFactory
- **字段**:
  - `public BasicAction atkAction1`
  - `public BasicAction atkAction2`
  - `public BasicAction atkAction3`

---

#### SkillCompFactory (abstract) [跨层: PlayerControl]
- **文件**: `Scripts/PlayerControl/PlayerSkillFactory.cs`
- **基类**: ScriptableObject

#### PlayerSkillFactory
- **文件**: `Scripts/PlayerControl/PlayerSkillFactory.cs`
- **基类**: SkillCompFactory
- **字段**: `public List<BasicSkill> skills`

#### CharacterSkillFactory
- **文件**: `Scripts/PlayerControl/CharacterSkillFactory.cs`
- **基类**: SkillCompFactory
- **字段**: `public List<BasicSkill> skills`

---

### 5.2 DataModel 基类

#### DataModelBase (abstract)
- **文件**: `Scripts/Extension/DataModel/DataModelBase.cs`
- **基类**: 无
- **接口**: IReference (GF框架)
- **命名空间**: GameFramework
- **字段**:
  - `public int Id { get; private set; }`
  - `public RefParams Userdata { get; private set; }`

#### DataModelStorageBase (abstract)
- **文件**: `Scripts/Extension/DataModel/DataModelStorageBase.cs`
- **基类**: DataModelBase
- **命名空间**: 全局
- **字段**:
  - `protected string StorageKey { get; private set; }` — 自动设为类型全名
- **特性**: 支持 JSON 序列化持久化 (Save 方法)

---

### 5.3 DataModel 派生类

#### PlayerDataModel
- **文件**: `Scripts/DataModel/PlayerDataModel.cs`
- **基类**: DataModelStorageBase (可持久化)
- **字段**:
  - `[JsonProperty] Dict<PlayerDataType, int> m_PlayerDataDic`
  - `int Hp { get; set; }`
  - `int Coins { get; set; }`
  - `int LevelId { get; set; }`

#### ProfileDataModel
- **文件**: `Scripts/DataModel/ProfileDataModel.cs`
- **基类**: DataModelStorageBase (可持久化)
- **字段**:
  - `[JsonProperty] Dict<ProfileDataType, int> m_ProfileDataDic`
  - `[JsonIgnore] int BaseLevel { get; set; }`

#### ItemCollectionDataModel
- **文件**: `Scripts/DataModel/Craft/ItemCollectionDataModel.cs`
- **基类**: DataModelStorageBase (可持久化)
- **字段**:
  - `[JsonProperty] Dict<string, int> itemCollectionDataDic`

#### TechProgressDataModel
- **文件**: `Scripts/DataModel/Tech/TechProgressDataModel.cs`
- **基类**: DataModelStorageBase (可持久化)
- **字段**:
  - `[JsonProperty] HashSet<string> m_UnlockedTechIds`
  - `[JsonProperty] List<string> m_UnlockOrder`
  - `[JsonProperty] Dict<string, long> m_UnlockTimeTicks`

#### CapabilityProgressDataModel
- **文件**: `Scripts/DataModel/Tech/CapabilityProgressDataModel.cs`
- **基类**: DataModelStorageBase (可持久化)
- **字段**:
  - `[JsonProperty] HashSet<string> m_Capabilities`

#### InputModel
- **文件**: `Scripts/DataModel/InputModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Fix64 MoveX, MoveY`
  - `bool InteractionPressed, Interaction2Pressed, Interaction3Pressed`
  - `bool OpenTechTreePressed, PlayerAttack`
  - `bool Skill1Pressed, Skill2Pressed, Skill3Pressed`
  - `Vector2 SelectScreenPosition`
  - `bool SkillConfirmPressed`

#### DeviceDataModel
- **文件**: `Scripts/DataModel/Build/DeviceDataModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Dict<string, Device> deviceDataDic`

#### ItemDataModel
- **文件**: `Scripts/DataModel/Craft/ItemDataModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Dict<string, Item> itemDataDic`

#### CraftingDeviceDataModel
- **文件**: `Scripts/DataModel/Craft/CraftingDeviceDataModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Dict<string, List<CraftingFormula>> craftingDeviceDataDic`

#### TechNodeDataModel
- **文件**: `Scripts/DataModel/Tech/TechNodeDataModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Dict<string, TechNodeTable> m_NodeDatas`

#### LocalizationTextDataModel
- **文件**: `Scripts/DataModel/Text/LocalizationTextDataModel.cs`
- **基类**: DataModelBase (非持久化)
- **字段**:
  - `Dict<string, string> _localizationKeyDic`

---

## 统计摘要

| 分类 | 数量 |
|------|------|
| ScriptableObject 类 (含抽象基类) | 27 |
| DataRowBase 派生表 | 18 |
| DataModel 类 (含基类) | 13 |
| 项目枚举 | 45 |
| **合计数据定义类** | **103** |
