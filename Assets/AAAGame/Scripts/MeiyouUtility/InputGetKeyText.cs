using UnityEngine.InputSystem;
using UnityGameFramework.Runtime;

public static class InputGetKeyText
{
    public static string GetKeyText(InputKey key)
    {
        string actionName = key switch
        {
            InputKey.InteractionPrimary => "Player/Interact",
            InputKey.InteractionSecondary => "Player/Interact2",
            InputKey.InteractionTertiary => "Player/Interact3",
            _ => "Player/Interact"
        };

        return GetKeyText(actionName);
    }

    public static string GetKeyText(InputActionReference actionReference, int bindingIndex = -1)
    {
        if (actionReference == null)
            return string.Empty;
        return GetKeyText(actionReference.action, bindingIndex);
    }

    public static string GetKeyText(InputAction action, int bindingIndex = -1)
    {
        if (action == null)
            return string.Empty;

        // bindingIndex 指定则显示指定 binding，否则使用 InputSystem 的默认显示策略。
        if (bindingIndex >= 0 && bindingIndex < action.bindings.Count)
        {
            return action.GetBindingDisplayString(
                bindingIndex,
                options: InputBinding.DisplayStringOptions.DontIncludeInteractions |
                         InputBinding.DisplayStringOptions.DontUseShortDisplayNames
            );
        }

        return action.GetBindingDisplayString(
            options: InputBinding.DisplayStringOptions.DontIncludeInteractions |
                     InputBinding.DisplayStringOptions.DontUseShortDisplayNames
        );
    }

    /// <summary>
    /// 支持：action 名（例如 "Player/Interact" 或 "interact"）或 binding path（例如 "<Keyboard>/e"）。
    /// </summary>
    public static string GetKeyText(string actionNameOrBindingPath, int bindingIndex = -1)
    {
        if (string.IsNullOrEmpty(actionNameOrBindingPath))
            return string.Empty;

        // 如果像是 binding path，就直接转人类可读文本。
        if (actionNameOrBindingPath.Contains("<") && actionNameOrBindingPath.Contains(">") && actionNameOrBindingPath.Contains("/"))
        {
            return InputControlPath.ToHumanReadableString(
                actionNameOrBindingPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice
            );
        }

        var inputManager = GameEntry.GetComponent<InputManager>();
        if (inputManager == null || inputManager.playerInput == null || inputManager.playerInput.actions == null)
            return string.Empty;

        var actions = inputManager.playerInput.actions;

        // 兼容常见命名写法
        var action = actions.FindAction(actionNameOrBindingPath) ??
                     actions.FindAction(actionNameOrBindingPath.Replace("Player/", "")) ??
                     actions.FindAction(actionNameOrBindingPath.Replace("Player/", "").ToLowerInvariant());

        return GetKeyText(action, bindingIndex);
    }
}
