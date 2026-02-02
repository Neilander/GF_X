# 交互系统接入清单（最小可跑通）

## 玩家（Actor）侧

1. 在玩家对象或其子对象上添加一个球形触发器（推荐挂在子对象，例如 `Player/InteractionTrigger`）：

   - `SphereCollider`，勾选 `Is Trigger`
   - 半径会由 `InteractionManager` 自动同步为：`interactionRange + triggerPadding`
2. 在该触发器对象上挂：

   - `InteractionDetector`
3. 在玩家对象（或同一对象）挂：

   - `SimpleInteractionResolver`
   - `InteractionManager`
4. 在 `InteractionManager`：

   - `detector` 指向你的 `InteractionDetector`
   - `resolverBehaviour` 指向你的 `SimpleInteractionResolver`
   - 配置 `interactionRange`（默认 10）和 `triggerPadding`（默认 3），无需再分别填写 detector/resolver 的 maxDistance

## 可交互物体侧

1. 物体必须有 Collider（非 Trigger 也行，取决于你检测方式），并且 Layer 要在 `InteractionDetector.interactableLayerMask` 内。
2. 物体挂一个InteractionHost。

## 输入侧（新 InputSystem）

- 当前交互按键从 `InputModel.InteractionPressed/2/3Pressed` 读取。
- `InputManager` 会尝试 FindAction：
  - Primary: `Player/Interact` 或 `interact` 或 `Interact`
  - Secondary: `Player/Interact2` 或 `interact2` 或 `Interact2`
  - Tertiary: `Player/Interact3` 或 `interact3` 或 `Interact3`

## 订阅执行

- 示例脚本：`InteractionCommandExecutorExample`
  - 订阅 `InteractionFocusChangedEventArgs` 用于更新 UI
  - 订阅 `InteractionOptionTriggeredEventArgs` 用于执行命令
