# FOG3 GF_X 配置说明

## 本次解决的问题

1. **可视区域与玩家/我方对象位置不对应**
   - 原因：旧方案把迷雾遮罩当作一张固定高度的平面纸张渲染。对象处在不同高度、摄像机又是斜视角或透视角度时，遮罩平面和对象脚下地形不在同一个空间表面，会产生屏幕视差。
   - 处理：`FOG3_WorldOverlayView` 现在默认会根据当前关卡地形采样高度，生成贴合地形起伏的网格遮罩。可视范围仍按对象的 XZ 坐标和半径计算，但显示网格会跟随地形高度，不再是一张固定高度平面。

2. **遮罩需要覆盖建筑和地形，但不能因为抬高遮罩而偏移**
   - 原因：物理抬高遮罩可以盖住建筑，但会导致透视摄像机下可视圆心偏离对象。
   - 处理：新增 `DrawOverSceneGeometry`。开启时遮罩使用 FOG3 专用 Shader：`AAAGame/FOG3/OverlayAlwaysOnTop`，通过深度测试覆盖场景物体。这样遮罩可以视觉上盖住建筑，同时网格仍贴着地形坐标，适配正交、透视、任意角度摄像机。

3. **GF_X 的 Launch -> Game 场景流程**
   - FOG3 可以挂在 `Launch` 场景中，并通过 `Persist Across Scene Loads` 跨场景保留。
   - FOG3 会等待 `Game` 场景加载完成后，再延迟重建地形和遮罩，避免在 Launch 阶段提前检测到错误地形。
   - FOG3 默认要求当前玩法场景存在 `TileWorldCreatorManager`。如果从 `Launch` 进入的是与 `Game` 无关的场景，或者场景里没有 TileWorldCreator/GridBased 地形，FOG3 不会生成迷雾，也会销毁旧的 `FOG3_WorldOverlayView`。

4. **不同关卡切换**
   - GF_X 关卡通常在 `Game` 场景内通过实体系统加载。FOG3 监听 GF_X 的 `ShowEntitySuccessEventArgs`，当检测到 `LevelEntity` 进场时，会延迟重建地形和贴地遮罩。
   - 每次重建会重新检测 GridBased/Collider/手动地形区域，并重新采样高度。

5. **卡牌系统生成的我方对象**
   - 卡牌生成实体后会触发 GF_X 的实体显示事件。FOG3 会自动注册玩家阵营实体。
   - 如果实体预制体上挂了 `Fog3RevealerComponent`，优先使用组件上的 `visionRadius`，不再被 Manager 默认半径覆盖。

## 推荐配置

在 `Launch` 场景中的 `FOG3System` 上配置 `Fog3Manager`：

### Terrain

- `Source Mode`: `Auto`
- `Require Tile World Creator Manager`: 开启
- `Ground Mask`: 选择 GridBased/关卡地面所在 Layer
- `Sample Walkable With Physics`: 如果 GridBased 生成的地形需要物理采样，开启
- `Physics Sample Height`: 高于关卡最高点即可，推荐 `120`
- `Physics Sample Distance`: 覆盖从采样点到地面距离，推荐 `260`

### View

- `Create World Overlay`: 开启
- `Overlay Height`: `0.15`
- `Conform Overlay To Terrain`: 开启
- `Height Sample Mask`: 优先选择地形/可行走地表 Layer  
  如果留空，会回退使用 Terrain 里的 `Ground Mask`；如果 `Ground Mask` 也为空，会使用 Unity 默认 Raycast Layers。
- `Height Sample Start Height`: 推荐 `120`
- `Height Sample Max Distance`: 推荐 `260`
- `Surface Offset`: 推荐 `0.08`
- `Draw Over Scene Geometry`: 开启
- `Outside Mask Padding`: 根据地图外黑边需要设置，默认 `1000`

### Vision

- `Current Player Vision Radius`: 当前玩家默认可视半径
- `Player Side Unit Vision Radius`: 我方普通单位默认可视半径
- `Building Vision Radius`: 我方建筑默认可视半径
- `Auto Register Player Side Entities`: 开启

如果某个卡牌生成单位需要单独半径：

1. 打开该单位/建筑预制体。
2. 添加或确认存在 `Fog3RevealerComponent`。
3. 设置 `Vision Radius`。
4. 保持 `Auto Register` 开启。

运行时如果改的是场景中已经生成出来的对象实例，`Fog3RevealerComponent` 会把新的 `Vision Radius` 同步到已注册的可视器。

### GF_X Scene Flow

- `Persist Across Scene Loads`: 开启
- `Wait For Gameplay Scene`: 开启
- `Gameplay Scene Name`: `Game`
- `Rebuild On Scene Loaded`: 开启
- `Scene Rebuild Frame Delay`: 推荐 `2`
- `Scene Rebuild Delay`: 推荐 `0.25`

## 重新配置步骤

1. 确认 `FOG3System` 在 `Launch` 场景，并挂载 `Fog3Manager`。
2. 在 `Terrain/Ground Mask` 中选择关卡地形 Layer。
3. 在 `View/Height Sample Mask` 中选择同一个地形 Layer；也可以留空，让它回退到 `Ground Mask`。
4. 保持 `Conform Overlay To Terrain` 和 `Draw Over Scene Geometry` 开启。
5. 给玩家、我方单位、卡牌生成单位的预制体添加 `Fog3RevealerComponent`，并设置各自 `Vision Radius`。
6. 从 `Launch` 运行项目，等待进入 `Game`。
7. 如果切换关卡或重新生成 GridBased 地形，使用 `Fog3Manager` Inspector 的 `Runtime/Rebuild Terrain` 按钮可手动重建；正常情况下 `LevelEntity` 进场会自动触发重建。

## 常见问题

### 可视范围还是偏移

- 检查 `Conform Overlay To Terrain` 是否开启。
- 检查 `Draw Over Scene Geometry` 是否开启。
- 检查 `Height Sample Mask` 是否采样到了建筑屋顶而不是地面。推荐只包含地形/地面 Layer，不要包含建筑 Layer。
- 检查对象 Pivot 是否严重偏离脚下位置。FOG3 使用对象 Transform 的 XZ 坐标作为可视中心。

### 完全没有迷雾

- 检查 `Create World Overlay` 是否开启。
- 检查 `Gameplay Scene Name` 是否与实际场景名 `Game` 一致。
- 检查 `FOG3System` 是否在运行时保留到了 `Game` 场景。
- 检查当前场景或当前关卡实体下是否存在 `TileWorldCreatorManager`，并且它的 `configuration` 不为空。默认开启 `Require Tile World Creator Manager` 后，没有这个组件就不会生成迷雾。
- 检查 `HiddenColor`/`ExploredColor` 的 Alpha 是否被调得太低。

### 卡牌生成单位没有可视范围

- 检查单位是否属于玩家阵营。
- 检查预制体上是否有 `Fog3RevealerComponent`。
- 检查 `Auto Register` 是否开启。
- 检查 `Fog3Manager/Auto Register Player Side Entities` 是否开启。
