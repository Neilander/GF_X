using System.Collections.Generic;
using UnityEngine;
using GameFramework;

/// <summary>
/// 交互实例（可池化）：由 InteractionHost 创建并持有。
/// 交互自身承载展示文本、执行条件与执行逻辑，不再通过独立的 Option 结构传递。
/// </summary>
public interface IInteractionOption : IReference
{
    void Init(object owner, string displayName, InteractionParams @params);

    /// <summary>
    /// 标题/展示名称
    /// </summary>
    string DisplayName { get; }

    KeyValuePair<IngameValueType, int>[] CostResource { get; }

    /// <summary>
    /// 是否需要展示在交互面板中。
    /// </summary>
    bool IsVisible();

    /// <summary>
    /// 当前交互是否可执行（用于 UI 置灰判断）
    /// </summary>
    bool IsExecutable();

    /// <summary>
    /// 执行交互逻辑
    /// </summary>
    void Execute();
}
