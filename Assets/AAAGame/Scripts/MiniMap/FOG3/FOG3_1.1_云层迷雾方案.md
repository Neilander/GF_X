# FOG3 1.1 固定云层与摄像机角度补偿

## 本次修复目标

当前游戏摄像机是倾斜视角，观察效果类似聚光灯从斜上方看玩家。迷雾云层本身是一张水平绘制平面，如果完全按 TileWorld 原始坐标显示，就会像从玩家头顶正上方绘制出来，导致摄像机画面中玩家和可视区域中心不重合。

你测试到把 `FOG3_WorldOverlayView` 的 Z 从约 `-31` 调到 `-19` 到 `-21` 时，可视区域能对齐玩家。这个偏移本质是摄像机斜视角产生的投影视差补偿。

## 当前技术方案

FOG3 现在采用：

- 固定云层遮罩。
- 不使用贴地形起伏网格。
- 不使用摄像机投影变形网格。
- 可视贴图仍按玩家和我方对象的真实世界 XZ 坐标更新。
- 显示层根据摄像机角度整体做固定 XZ 偏移，用来补偿斜视角。

这样遮罩本身不会跟着玩家跑，黑色边界也不会因为玩家移动而漂移；只是整张云层显示位置按摄像机角度做一次稳定补偿。

## 关键配置

在 `FOG3System/Fog3Manager` 的 View 配置中：

- `Surface Mode`: `CloudLayer`
- `Draw Over Scene Geometry`: 开启
- `Auto Height Above Scene`: 关闭
- `Cloud Layer World Offset`: 手动偏移，可用于微调。你当前测试的方向大约是 Z 正方向 `10` 到 `12`。
- `Use Camera Angle Offset`: 开启
- `Camera Projection Target World Y`: 斜视角希望对齐的目标高度。当前摄像机看玩家时建议先用 `1`。
- `Camera Angle Offset Scale`: 自动偏移倍率，默认 `1`。如果偏移过头就调小，比如 `0.8`；如果不够就调大，比如 `1.2`。
- `Outside Mask Inner Overlap`: 外侧黑遮罩向地图内侧压边的距离，默认 `2`。如果主迷雾区域和黑遮罩之间出现缝隙，优先增大它。

自动偏移公式：

```text
offset = camera.forward.xz * ((CameraProjectionTargetWorldY - cloudWorldY) / -camera.forward.y) * CameraAngleOffsetScale
```

当前摄像机约 36.87 度俯视时，这个公式通常会在 Z 正方向算出接近 `+12` 的偏移，与你手动验证的 `-31 -> -19/-21` 基本一致。

## 调参顺序

1. 保持 `Use Camera Angle Offset` 开启。
2. 先把 `Cloud Layer World Offset` 设为 `(0, 0, 0)`。
3. 把 `Camera Projection Target World Y` 设为玩家脚底或角色中心在世界坐标里的 Y。当前可以先用 `1`。
4. 运行后看玩家和可视圆心：
   - 如果可视区域还在玩家后方，增大 `Camera Angle Offset Scale`。
   - 如果可视区域跑到玩家前方，减小 `Camera Angle Offset Scale`。
5. 如果只差一点点，用 `Cloud Layer World Offset` 做最终微调，例如 `(0, 0, 1)` 或 `(0, 0, -1)`。

## 为什么不再移动整张遮罩跟玩家

玩家移动时，FOG3 只更新可视贴图，不移动 `FOG3_WorldOverlayView`。这保证：

- 黑色边界固定在 TileWorld/GridBased 地图外。
- 已探索区域不会因为玩家移动整体漂移。
- 可视半径来自 `Fog3RevealerComponent` 的对象位置。

摄像机角度补偿只和摄像机角度、云层高度、目标参考高度有关，不应该跟玩家 XZ 位置绑定。

## 常见问题

### 已探索区域仍然出现地形落差

- 确认 `Surface Mode` 是 `CloudLayer`。
- 确认不是 `TerrainConforming`。
- 确认 `Auto Height Above Scene` 关闭。
- 确认 `Draw Over Scene Geometry` 开启。

### 玩家可视区域与玩家仍然错开

- 先调 `Camera Projection Target World Y`，让它接近玩家实际视觉中心的世界 Y。
- 再调 `Camera Angle Offset Scale`。
- 最后用 `Cloud Layer World Offset` 微调。

### 玩家没到地图边界就看到黑边

- 确认不是把 `FOG3_WorldOverlayView` 挂在玩家或摄像机下面。
- 确认 TileWorldCreatorManager 的 `configuration.width/height/cellSize` 与实际地形范围一致。
- 如果只是在斜视角下边界稍早出现，优先调摄像机角度补偿，而不是改地图尺寸。

### 主迷雾和外侧黑遮罩之间出现缝隙

- 增大 `Outside Mask Inner Overlap`，例如从 `2` 调到 `3` 或 `5`。
- 保持 `Outside Mask Padding` 足够大，默认 `1000`。
- 外侧黑遮罩会比主迷雾更晚绘制，目的就是压住透明排序、贴图过滤和斜视角造成的边界缝。
