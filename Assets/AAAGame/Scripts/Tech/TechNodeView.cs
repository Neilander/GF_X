using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 科技节点（预摆 UI）的统一组件：
/// - 身份数据（techId/rect）供编辑器生成器与运行时扫描使用
/// - 运行时表现（三态：已研究/可研究/不可研究）与选中高亮
/// - 点击事件（由 TechTreeDialog 统一打开详情提示框）
/// </summary>
[DisallowMultipleComponent]
public class TechNodeView : MonoBehaviour, IPointerClickHandler
{
    [Header("Identity")]
    public string techId;

    [Header("Runtime")]

    [Header("Visual")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Graphic stateBorder;
    [SerializeField] private Graphic selectedBorder;

    public event Action<TechNodeView> Clicked;

    private void Reset()
    {
        iconImage = GetComponent<Image>();

        // 约定名称（可选）：
        // - Border：用于状态亮度
        // - Selected：用于选中高亮
        stateBorder = FindGraphic("Border");
        selectedBorder = FindGraphic("Selected");
    }

    private Graphic FindGraphic(string childName)
    {
        var t = transform.Find(childName);
        return t != null ? t.GetComponent<Graphic>() : null;
    }

    public void ApplyState(bool unlocked, bool canResearch)
    {
        // 3态：
        // - 已研究：图标正常；边框略暗
        // - 可研究：图标灰；边框更亮
        // - 不可研究：图标灰；边框更暗
        if (iconImage != null)
        {
            iconImage.color = unlocked ? Color.white : Color.gray;
        }

        if (stateBorder != null)
        {
            float a;
            if (unlocked) a = 0.65f;
            else if (canResearch) a = 1.0f;
            else a = 0.25f;

            var c = stateBorder.color;
            stateBorder.color = new Color(c.r, c.g, c.b, a);
        }
    }

    public void SetSelected(bool selected)
    {
        if (selectedBorder != null)
        {
            selectedBorder.gameObject.SetActive(selected);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(this);
    }
}
