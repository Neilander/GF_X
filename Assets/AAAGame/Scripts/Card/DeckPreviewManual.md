# 卡牌预览手动配置完整流程

## 目标效果

鼠标悬浮到抽卡点 `CardDeck` 上方时，打开一个卡牌预览大面板，显示 `Assets/Resources/CardData` 目录下所有 `CardData` ScriptableObject 卡牌。

当前方案是手动 UI 配置模式：

- UI 面板由你在 Unity 里手动拼好，或直接使用你已经做好的预制体。
- 代码不再创建 `DeckPreviewPanel`、`Viewport`、`Content`、卡牌条目等 UI 结构。
- `CardUIForm` 只负责显示/隐藏面板、读取 `CardData`、复制条目模板并填充数据。

## 第 1 步：确认卡牌数据目录

所有卡牌 ScriptableObject 必须放在：

```text
Assets/Resources/CardData
```

`CardUIForm` 上的 `卡牌数据Resources路径` 填：

```text
CardData
```

注意：这里不能填 `Assets/Resources/CardData`。Unity 的 `Resources.LoadAll<CardData>()` 读取的是 `Resources` 目录内部路径。

## 第 2 步：创建预览面板根节点

在 `CardUIForm` 所在 Canvas 下创建：

```text
DeckPreviewPanel
```

建议运行前先设为不激活，代码会在鼠标悬浮抽卡点时自动打开。

`DeckPreviewPanel` 添加组件：

- `RectTransform`
- `CanvasRenderer`
- `Image`
- `ScrollRect`

推荐参数：

```text
Anchor Min: (0.5, 0.5)
Anchor Max: (0.5, 0.5)
Pivot:      (0.5, 0.5)
Pos X:      0
Pos Y:      80
Width:      860
Height:     560
Image Color: RGBA(0.06, 0.075, 0.09, 0.96)
```

`ScrollRect` 参数：

```text
Horizontal: 关闭
Vertical:   开启
Movement Type: Clamped
Scroll Sensitivity: 24
```

## 第 3 步：创建标题文本

在 `DeckPreviewPanel` 下创建：

```text
DeckPreviewTitle
```

添加 `TextMeshProUGUI`。

推荐 RectTransform：

```text
Anchor Min: (0, 1)
Anchor Max: (1, 1)
Pivot:      (0.5, 1)
Pos X:      0
Pos Y:      -14
Width:      -36
Height:     42
```

推荐 TMP 参数：

```text
Text: 卡牌预览
Font Size: 28
Alignment: Midline Left
Color: White
Raycast Target: 关闭
Word Wrapping: 开启
Overflow: Ellipsis
```

运行时标题会被代码改成：

```text
卡牌预览  共 N 张
```

## 第 4 步：创建空数据提示

在 `DeckPreviewPanel` 下创建：

```text
DeckPreviewEmptyText
```

添加 `TextMeshProUGUI`。

这个对象用于没有读取到卡牌数据时显示提示。平时可以设为不激活。

推荐参数：

```text
Text: 未找到卡牌数据
Font Size: 24
Alignment: Center
Color: RGBA(0.86, 0.9, 0.92, 1)
Raycast Target: 关闭
```

这个对象是可选项。如果不需要空提示，可以不绑定。

## 第 5 步：创建 Viewport

在 `DeckPreviewPanel` 下创建：

```text
DeckPreviewViewport
```

添加组件：

- `RectTransform`
- `CanvasRenderer`
- `Image`
- `Mask`

推荐 RectTransform：

```text
Anchor Min: (0, 0)
Anchor Max: (1, 1)
Left:       18
Right:      18
Bottom:     18
Top:        66
```

推荐 Image 参数：

```text
Color: RGBA(1, 1, 1, 0.04)
Raycast Target: 开启或关闭都可以
```

推荐 Mask 参数：

```text
Show Mask Graphic: 关闭
```

然后回到 `DeckPreviewPanel` 的 `ScrollRect`，绑定：

```text
Viewport: DeckPreviewViewport
```

## 第 6 步：创建 Content

在 `DeckPreviewViewport` 下创建：

```text
DeckPreviewContent
```

添加组件：

- `RectTransform`
- `GridLayoutGroup`
- `ContentSizeFitter`

推荐 RectTransform：

```text
Anchor Min: (0, 1)
Anchor Max: (1, 1)
Pivot:      (0.5, 1)
Pos X:      0
Pos Y:      0
Width:      0
Height:     0
```

`GridLayoutGroup` 推荐参数：

```text
Cell Size:        (150, 210)
Spacing:          (12, 14)
Padding Left:     12
Padding Right:    12
Padding Top:      12
Padding Bottom:   12
Start Corner:     Upper Left
Start Axis:       Horizontal
Child Alignment:  Upper Center
Constraint:       Fixed Column Count
Constraint Count: 5
```

`ContentSizeFitter` 推荐参数：

```text
Horizontal Fit: Unconstrained
Vertical Fit:   Preferred Size
```

然后回到 `DeckPreviewPanel` 的 `ScrollRect`，绑定：

```text
Content: DeckPreviewContent
```

## 第 7 步：创建卡牌条目模板

在 `DeckPreviewContent` 下创建：

```text
DeckPreviewItem_Template
```

这个对象作为模板，代码会复制它来生成每张预览卡牌。

模板默认设为不激活。

添加组件：

- `RectTransform`
- `CanvasRenderer`
- `Image`
- `CardDeckPreviewItem`

推荐 RectTransform：

```text
Width:  150
Height: 210
Scale:  (1, 1, 1)
```

推荐 Image：

```text
Color: RGBA(0.12, 0.14, 0.16, 0.96)
Raycast Target: 关闭
```

## 第 8 步：配置卡牌条目内部 UI

在 `DeckPreviewItem_Template` 下创建以下子节点。

### CardBackImage

如果模板根节点的 Image 已经作为卡牌背景，可以不单独创建这个对象，直接把模板根节点的 Image 绑定给 `卡牌背景图`。

如果单独创建，推荐：

```text
Anchor Min: (0, 0)
Anchor Max: (1, 1)
Left/Right/Top/Bottom: 0
Raycast Target: 关闭
```

### CardImage

用于显示卡面立绘。

推荐 RectTransform：

```text
Anchor Min: (0, 1)
Anchor Max: (1, 1)
Pivot:      (0.5, 1)
Pos X:      0
Pos Y:      -8
Width:      -16
Height:     86
```

推荐 Image：

```text
Preserve Aspect: 开启
Raycast Target: 关闭
```

### CardNameText

用于显示卡牌名称。

推荐 RectTransform：

```text
Anchor Min: (0, 1)
Anchor Max: (1, 1)
Pivot:      (0.5, 1)
Pos X:      0
Pos Y:      -102
Width:      -14
Height:     46
```

推荐 TMP：

```text
Font Size: 18
Auto Size: 开启
Font Size Min: 12
Font Size Max: 18
Alignment: Center
Color: White
Raycast Target: 关闭
Word Wrapping: 开启
Overflow: Ellipsis
```

### PopulationText

用于显示人口消耗。

推荐 RectTransform：

```text
Anchor Min: (0, 0)
Anchor Max: (0.5, 0)
Pivot:      (0.5, 0)
Pos Y:      12
Height:     48
```

推荐 TMP：

```text
Font Size: 15
Alignment: Center
Color: RGBA(0.82, 0.9, 0.92, 1)
Raycast Target: 关闭
```

### SoldierCountText

用于显示生成士兵数量。

推荐 RectTransform：

```text
Anchor Min: (0.5, 0)
Anchor Max: (1, 0)
Pivot:      (0.5, 0)
Pos Y:      12
Height:     48
```

推荐 TMP：

```text
Font Size: 15
Alignment: Center
Color: RGBA(0.82, 0.9, 0.92, 1)
Raycast Target: 关闭
```

### SoldierNameText

可选，用于显示士兵名字。

如果空间不够，可以不创建、不绑定。代码允许为空。

## 第 9 步：绑定 CardDeckPreviewItem

选中 `DeckPreviewItem_Template`，在 `CardDeckPreviewItem` 上绑定：

```text
卡牌背景图:     CardBackImage 或模板根节点 Image
卡面立绘:       CardImage
卡牌名称文本:   CardNameText
人口消耗文本:   PopulationText
士兵数量文本:   SoldierCountText
士兵名称文本:   SoldierNameText，可选
```

如果某个字段不需要显示，可以不绑定。

## 第 10 步：绑定 CardUIForm

选中挂有 `CardUIForm` 的对象，在 `卡组预览` 区域绑定：

```text
悬浮抽卡点显示卡组: 勾选
卡牌数据Resources路径: CardData
卡组预览面板: DeckPreviewPanel
卡组预览内容容器: DeckPreviewContent
卡组预览标题文本: DeckPreviewTitle
卡组为空提示文本: DeckPreviewEmptyText，可选
卡组预览卡牌条目模板: DeckPreviewItem_Template 上的 CardDeckPreviewItem 组件
```

同时确认 `抽卡动画` 区域：

```text
cardDeckTransform: 抽卡点 CardDeck 的 RectTransform
```

鼠标悬浮检测使用的就是这个 `cardDeckTransform`。

## 第 11 步：保存成预制体

如果你希望以后复用这套 UI：

1. 把 `DeckPreviewPanel` 做成 Prefab。
2. 放到你自己的 UI 预制体目录下。
3. 在 `CardUIForm` 所在 UI 预制体中实例化它。
4. 重新绑定 `CardUIForm` 上的预览字段。

注意：`DeckPreviewItem_Template` 必须在运行时存在于 `DeckPreviewContent` 下，否则代码没有模板可复制。

## 第 12 步：运行检查

1. 运行游戏，进入有卡牌 UI 的战斗状态。
2. 确认 `Assets/Resources/CardData` 下存在至少一个 `CardData`。
3. 鼠标移动到抽卡点 `CardDeck` 上方。
4. `DeckPreviewPanel` 应该显示。
5. 标题显示 `卡牌预览  共 N 张`。
6. `DeckPreviewContent` 中生成卡牌条目。
7. 鼠标离开抽卡点和预览面板后，面板隐藏。

## 常见问题

### 鼠标悬浮抽卡点没有显示面板

检查：

- `CardUIForm` 的 `悬浮抽卡点显示卡组` 是否勾选。
- `cardDeckTransform` 是否绑定。
- `CardDeck` 的 RectTransform 范围是否太小。
- `卡组预览面板` 是否绑定。
- `卡组预览内容容器` 是否绑定。
- `卡组预览卡牌条目模板` 是否绑定。

### 面板出现但没有卡牌

检查：

- `CardData` 是否放在 `Assets/Resources/CardData`。
- `卡牌数据Resources路径` 是否是 `CardData`。
- `DeckPreviewItem_Template` 是否挂了 `CardDeckPreviewItem`。
- `DeckPreviewItem_Template` 是否绑定了至少一个可显示的 Image 或 TMP。

### 生成出来的条目布局不对

检查 `DeckPreviewContent`：

- 是否挂了 `GridLayoutGroup`。
- `Cell Size` 是否是 `(150, 210)`。
- `Constraint` 是否是 `Fixed Column Count`。
- `Constraint Count` 是否是 `5`。
- 是否挂了 `ContentSizeFitter`。
- `Vertical Fit` 是否是 `Preferred Size`。

### 模板本身也显示在列表里

检查：

- `DeckPreviewItem_Template` 是否默认不激活。
- `CardUIForm` 绑定的模板是否就是 `DeckPreviewContent` 下的模板对象。

代码清理内容时会跳过模板本体，只删除复制出来的条目。

### 鼠标移到面板上面板立刻关闭

检查：

- `卡组预览面板` 绑定的是 `DeckPreviewPanel` 根节点。
- `DeckPreviewPanel` 的 RectTransform 是否覆盖实际面板区域。

代码判断鼠标是否仍在预览面板上，用的是 `DeckPreviewPanel` 的 RectTransform。
