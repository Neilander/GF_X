# 卡牌系统 GF_X 框架改造说明

## 改造完成的内容

### 1. HandCardItem 改造
- ✅ 继承 `UIItemBase` 而不是 `MonoBehaviour`
- ✅ 使用 `OnInit()` 替代 `Awake()`
- ✅ 保留所有原有功能（拖拽、悬停、动画等）

### 2. CardUIForm 改造
- ✅ 继承 `UIFormBase`（已经是了）
- ✅ 使用 GF_X 对象池系统 `SpawnItem<T>()` 创建 HandCardItem
- ✅ 使用 `UnspawnItem<T>()` 回收 HandCardItem
- ✅ 使用 `UnspawnAllItem<T>()` 清理所有 HandCardItem
- ✅ 保留所有原有功能（快捷键、拖拽、事件等）

### 3. 新增文件
- ✅ `HandCardItemObject.cs` - 对象池包装类
- ✅ `CardGameProcedure.cs` - 卡牌游戏流程

### 4. 流程设计
```
Launch 场景启动
    ↓
LaunchProcedure (初始化)
    ↓
ChangeSceneProcedure (加载 Game 场景)
    ↓
CardGameProcedure (显示卡牌 UI)
    ↓
CardUIForm 打开，动态创建 HandCardItem
```

## 需要手动完成的步骤

### 步骤 1: 重新生成 UIViews 枚举
1. 打开 Unity 编辑器
2. 点击菜单：`AAAGame → Generate DataTables`
3. 这将根据 `UITable.txt` 重新生成 `UIViews.cs`
4. 确认 `UIViews.CardUIForm` 枚举值存在

### 步骤 2: 配置 ChangeSceneProcedure
修改 `ChangeSceneProcedure.cs`，添加 CardGameProcedure 支持：

```csharp
// 在 SelectedProcedureForGame 默认值中添加
public static string SelectedProcedureForGame = "CardGameProcedure";

// 在 ChangeStateForGame 方法中添加 case
case "CardGameProcedure":
    ChangeState<CardGameProcedure>(procedureOwner);
    break;
```

### 步骤 3: 配置 CardUIForm 预制体
1. 确保 `CardUIForm.prefab` 存在于 `Assets/AAAGame/Prefabs/UI/`
2. 确保预制体上挂载了 `CardUIForm` 脚本
3. 配置以下字段：
   - `handCardContainer` - 手牌容器 Transform
   - `trashBinArea` - 垃圾桶区域 Transform
   - `handCardItemPrefab` - HandCardItem 预制体
   - `populationView` - 人口显示组件

### 步骤 4: 配置 HandCardItem 预制体
1. 确保 `HandCardItem.prefab` 存在
2. 确保预制体上挂载了 `HandCardItem` 脚本（继承 UIItemBase）
3. 配置以下字段：
   - `cardImage` - 卡面图片
   - `populationText` - 人口消耗文本
   - `soldierCountText` - 士兵数量文本
   - `canvasGroup` - CanvasGroup 组件

### 步骤 5: 配置场景区域对象
在 Game 场景中创建：
1. `ValidArea` - 可放置区域（GameObject）
2. `InvalidArea` - 禁止放置区域（GameObject）

### 步骤 6: 配置卡牌数据
将 CardData ScriptableObject 放置在 `Resources/CardData/` 目录下，
或修改 `CardGameProcedure.LoadCardPool()` 方法以适配你的数据加载方式。

### 步骤 7: 测试流程
1. 打开 Launch 场景
2. 运行游戏
3. 确认流程：Launch → Game 场景 → CardUIForm 显示
4. 测试功能：
   - ✅ 手牌显示
   - ✅ 卡牌拖拽
   - ✅ 快捷键 1234
   - ✅ 重新抽卡
   - ✅ 删除卡牌
   - ✅ 卡牌放置
   - ✅ 人口系统

## 保留的原有功能

### 卡牌交互
- ✅ 鼠标拖拽卡牌
- ✅ 鼠标悬停放大
- ✅ 拖拽到可放置区域生成物体
- ✅ 拖拽到禁止区域驳回卡牌
- ✅ 拖拽到垃圾桶删除卡牌

### 快捷键
- ✅ 1234 键快速打出对应位置的卡牌

### UI 功能
- ✅ 显示/隐藏 UI
- ✅ 人口显示
- ✅ 手牌数量限制

### 数据系统
- ✅ 卡牌数据（CardData）
- ✅ 权重抽卡系统
- ✅ 人口消耗系统

## 架构优势

### 使用 GF_X 对象池的好处
1. **性能优化** - 避免频繁 Instantiate/Destroy
2. **内存管理** - 自动回收和复用
3. **配置灵活** - 可设置容量、过期时间、自动释放间隔
4. **框架统一** - 符合 GF_X 规范

### 使用 Procedure 的好处
1. **流程清晰** - 游戏状态管理规范
2. **生命周期** - OnEnter/OnUpdate/OnLeave 明确
3. **状态切换** - 方便在不同游戏模式间切换
4. **资源管理** - 流程结束时自动清理

### 使用 UIFormBase 的好处
1. **UI 动画** - 内置打开/关闭动画支持
2. **事件系统** - 统一的 UI 事件处理
3. **多语言** - 自动多语言支持
4. **子 UI** - 支持子界面管理

## 注意事项

1. **对象池配置**
   - 默认容量：50
   - 过期时间：50 秒
   - 自动释放间隔：5 秒
   - 可在 `CardUIForm.CreateHandCardItem()` 中调整

2. **事件订阅**
   - 在 `OnInit()` 中订阅事件
   - 在 `OnRecycle()` 中取消订阅
   - 避免内存泄漏

3. **数据传递**
   - 使用 `UIParams.UserData` 传递 CardSystemController
   - 在 `OnOpen()` 中接收数据

4. **场景切换**
   - CardUIForm 只在 Game 场景显示
   - 切换场景时自动清理

## 后续扩展建议

1. **DataTable 集成**
   - 将 CardData 迁移到 DataTable
   - 使用 `GF.DataTable.GetDataTable<CardTable>()`

2. **网络同步**
   - 添加卡牌操作的网络同步
   - 使用 GF.Network 模块

3. **音效**
   - 添加卡牌抽取音效
   - 添加卡牌打出音效
   - 使用 `GF.Sound.PlayEffect()`

4. **特效**
   - 添加卡牌生成特效
   - 添加卡牌打出特效
   - 使用 GF.Entity 显示特效实体

5. **存档**
   - 保存玩家卡组
   - 使用 `GF.Setting` 或自定义存档系统
