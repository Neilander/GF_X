# 卡牌系统 GF_X 框架改造清单

## ✅ 代码改造（已完成）

### 核心类修改
- [x] `HandCardItem.cs` - 继承 UIItemBase
- [x] `CardUIForm.cs` - 使用 GF_X 对象池
- [x] `ChangeSceneProcedure.cs` - 添加 CardGameProcedure 支持

### 新增文件
- [x] `HandCardItemObject.cs` - 对象池包装类
- [x] `CardGameProcedure.cs` - 卡牌游戏流程
- [x] `CardGameQuickStart.cs` - 快速启动工具
- [x] `README_改造说明.md` - 详细说明文档
- [x] `改造完成总结.md` - 完成总结文档
- [x] `快速开始指南.md` - 快速开始指南
- [x] `改造清单_Checklist.md` - 本清单

## 📋 Unity 编辑器操作（需手动完成）

### 步骤 1: 重新生成 UIViews 枚举
- [ ] 打开 Unity 编辑器
- [ ] 点击菜单：`AAAGame → Generate DataTables`
- [ ] 等待生成完成
- [ ] 确认 `UIViews.CardUIForm` 存在

### 步骤 2: 配置 CardUIForm 预制体
- [ ] 打开 `Assets/AAAGame/Prefabs/UI/CardUIForm.prefab`
- [ ] 确认挂载了 `CardUIForm` 脚本
- [ ] 配置字段：
  - [ ] `handCardContainer` - 手牌容器 Transform
  - [ ] `trashBinArea` - 垃圾桶区域 Transform
  - [ ] `handCardItemPrefab` - HandCardItem 预制体
  - [ ] `populationView` - PopulationView 组件
- [ ] 保存预制体

### 步骤 3: 配置 HandCardItem 预制体
- [ ] 打开 `Assets/AAAGame/Scripts/Card/HandCardItem.prefab`
- [ ] 确认挂载了 `HandCardItem` 脚本（继承 UIItemBase）
- [ ] 配置字段：
  - [ ] `cardImage` - Image 组件
  - [ ] `populationText` - TextMeshProUGUI 组件
  - [ ] `soldierCountText` - TextMeshProUGUI 组件
  - [ ] `canvasGroup` - CanvasGroup 组件
- [ ] 保存预制体

### 步骤 4: 配置 Game 场景
- [ ] 打开 `Assets/AAAGame/Scene/Game.unity`
- [ ] 创建 `ValidArea` GameObject（可放置区域）
- [ ] 创建 `InvalidArea` GameObject（禁止区域）
- [ ] 确认场景中有 `EventSystem`
- [ ] 保存场景

### 步骤 5: 配置卡牌数据
- [ ] 创建 `Resources/CardData/` 目录
- [ ] 将 CardData ScriptableObject 放入该目录
- [ ] 或修改 `CardGameProcedure.LoadCardPool()` 方法

### 步骤 6: 测试启动
- [ ] 点击菜单：`AAAGame → Card → Quick Start Card Game`
- [ ] 确认 Launch 场景已打开
- [ ] 点击 Play 按钮运行游戏
- [ ] 观察流程是否正确

### 步骤 7: 功能测试
- [ ] 手牌是否正确显示
- [ ] 卡牌拖拽是否正常
- [ ] 快捷键 1234 是否有效
- [ ] 人口系统是否正常
- [ ] 卡牌放置是否正常
- [ ] 删除卡牌是否正常

## 🔍 配置检查（使用工具）

### 使用配置检查工具
- [ ] 点击菜单：`AAAGame → Card → Check Card System Setup`
- [ ] 查看控制台输出
- [ ] 确认所有项都是 ✅
- [ ] 如有 ❌ 或 ⚠️，根据提示修复

### 检查项目
- [ ] UITable 包含 CardUIForm 配置
- [ ] UIViews 枚举包含 CardUIForm
- [ ] CardUIForm 预制体存在
- [ ] CardUIForm 脚本已挂载
- [ ] HandCardItem 预制体存在
- [ ] HandCardItem 脚本已挂载
- [ ] CardData 数据存在
- [ ] CardGameProcedure 类存在

## 🎯 功能验证

### 基础功能
- [ ] 游戏启动流程正确
- [ ] CardUIForm 正确显示
- [ ] 手牌正确创建
- [ ] 手牌数量正确
- [ ] 人口显示正确

### 交互功能
- [ ] 鼠标拖拽卡牌
- [ ] 鼠标悬停放大
- [ ] 快捷键 1234
- [ ] 拖拽到可放置区域
- [ ] 拖拽到禁止区域
- [ ] 拖拽到垃圾桶

### 系统功能
- [ ] 人口消耗正确
- [ ] 人口不足置灰
- [ ] 卡牌权重抽取
- [ ] 对象池复用
- [ ] 事件系统正常

### 性能测试
- [ ] 多次抽卡无卡顿
- [ ] 多次删除无卡顿
- [ ] 内存占用正常
- [ ] 无内存泄漏

## 📊 可选优化

### 对象池优化
- [ ] 根据实际需求调整容量
- [ ] 根据游戏节奏调整过期时间
- [ ] 根据抽卡频率调整释放间隔

### 数据优化
- [ ] 考虑迁移到 DataTable
- [ ] 考虑使用配置表
- [ ] 考虑添加数据验证

### UI 优化
- [ ] 配置 UI 打开动画
- [ ] 配置 UI 关闭动画
- [ ] 添加音效
- [ ] 添加特效

### 功能扩展
- [ ] 添加网络同步
- [ ] 添加存档系统
- [ ] 添加多语言支持
- [ ] 添加更多卡牌类型

## 🐛 问题排查

### 如果遇到编译错误
- [ ] 检查是否重新生成了 UIViews
- [ ] 检查命名空间是否正确
- [ ] 检查引用是否完整

### 如果 UI 不显示
- [ ] 检查是否从 Launch 场景启动
- [ ] 检查流程设置是否正确
- [ ] 检查 UITable 配置是否正确
- [ ] 检查预制体路径是否正确

### 如果手牌创建失败
- [ ] 检查 handCardItemPrefab 是否赋值
- [ ] 检查 HandCardItem 脚本是否挂载
- [ ] 检查对象池是否正常工作

### 如果拖拽无效
- [ ] 检查场景中是否有 EventSystem
- [ ] 检查 Canvas 设置是否正确
- [ ] 检查拖拽接口是否实现

## 📚 文档阅读

### 必读文档
- [ ] `快速开始指南.md` - 了解如何快速启动
- [ ] `改造完成总结.md` - 了解改造内容

### 参考文档
- [ ] `README_改造说明.md` - 了解详细改造过程
- [ ] `CardGameProcedure.cs` - 了解流程实现
- [ ] `HandCardItemObject.cs` - 了解对象池包装

## ✨ 完成标志

当以下所有项都完成时，改造即完成：

- [ ] 所有代码改造完成
- [ ] 所有 Unity 编辑器操作完成
- [ ] 所有配置检查通过
- [ ] 所有功能验证通过
- [ ] 游戏可以正常运行
- [ ] 所有原有功能保留

## 🎉 恭喜！

如果你完成了以上所有步骤，那么卡牌系统已经成功融入 GF_X 框架！

现在你可以：
- 享受对象池带来的性能优化
- 使用 Procedure 管理游戏流程
- 利用 GF_X 的各种模块扩展功能
- 保持代码的规范性和可维护性

祝开发顺利！🚀
