using GameFramework;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 科技节点交互：
/// - 悬浮显示名称/描述/点亮条件（不满足标红；已点亮不显示条件）
/// - 满足条件时长按一段时间点亮（HoldProgress）
/// - 未点亮图标灰色，已点亮正常
/// </summary>
public class TechNodeInteract : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    [SerializeField] private TechNodeView nodeView;
    [SerializeField] private Image iconImage;
    [SerializeField] private HoldProgress holdProgress;

    [Header("Tooltip")]
    [SerializeField] private GameObject tooltipRoot;
    [SerializeField] private Text tooltipText;

    [Header("Hold")]
    [SerializeField] private float holdSeconds = 1.0f;

    private Color m_NormalIconColor = Color.white;

    private void Reset()
    {
        nodeView = GetComponent<TechNodeView>();
        iconImage = GetComponent<Image>();
        holdProgress = GetComponent<HoldProgress>();

        var t = transform.Find("Tooltip");
        if (t != null)
        {
            tooltipRoot = t.gameObject;
            tooltipText = t.GetComponentInChildren<Text>(true);
        }
    }

    private void Awake()
    {
        if (nodeView == null) nodeView = GetComponent<TechNodeView>();
        if (iconImage == null) iconImage = GetComponent<Image>();
        if (holdProgress == null) holdProgress = GetComponent<HoldProgress>();

        if (iconImage != null) m_NormalIconColor = iconImage.color;

        if (holdProgress != null)
        {
            holdProgress.SetHoldSeconds(holdSeconds);
            holdProgress.onFull = OnHoldFull;
        }

        if (tooltipRoot != null) tooltipRoot.SetActive(false);

        RefreshState();
    }

    private void OnEnable()
    {
        RefreshState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        RefreshTooltipAndHold();
        if (tooltipRoot != null) tooltipRoot.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    private void OnHoldFull()
    {
        if (nodeView == null || string.IsNullOrWhiteSpace(nodeView.techId))
            return;

        // 只在满足条件时点亮
        if (!TechProgressDataModel.CanResearch(nodeView.techId, out _))
        {
            RefreshTooltipAndHold();
            return;
        }

        bool ok = TechProgressDataModel.Research(nodeView.techId);
        if (ok)
        {
            RefreshState();
            if (holdProgress != null)
            {
                holdProgress.AllowHold = false;
                holdProgress.ResetProgress();
            }
            RefreshTooltipAndHold();
        }
    }

    private void RefreshState()
    {
        if (nodeView == null || string.IsNullOrWhiteSpace(nodeView.techId))
            return;

        bool unlocked = TechProgressDataModel.IsUnlocked(nodeView.techId);

        if (iconImage != null)
        {
            iconImage.color = unlocked ? m_NormalIconColor : Color.gray;
        }

        if (holdProgress != null)
        {
            if (unlocked)
            {
                holdProgress.AllowHold = false;
                holdProgress.ResetProgress();
            }
            else
            {
                holdProgress.SetHoldSeconds(holdSeconds);
                holdProgress.AllowHold = TechProgressDataModel.CanResearch(nodeView.techId, out _);
            }
        }
    }

    private void RefreshTooltipAndHold()
    {
        RefreshState();

        if (tooltipText == null || nodeView == null || string.IsNullOrWhiteSpace(nodeView.techId))
            return;

        var data = TechTreeDataModel.GetNodeData(nodeView.techId);
        if (data == null)
        {
            tooltipText.text = nodeView.techId;
            return;
        }

        bool unlocked = TechProgressDataModel.IsUnlocked(nodeView.techId);

        var sb = new StringBuilder(256);
        if (!string.IsNullOrWhiteSpace(data.Name))
            sb.AppendLine("<b>" + EscapeRich(data.Name) + "</b>");
        else
            sb.AppendLine("<b>" + EscapeRich(data.Identifier) + "</b>");

        if (!string.IsNullOrWhiteSpace(data.Description))
            sb.AppendLine(EscapeRich(data.Description));

        if (!unlocked)
        {
            sb.AppendLine();
            sb.AppendLine("<b>点亮条件</b>");

            // 必备：基地等级
            var baseDm = GF.DataModel.GetOrCreate<BaseProgressDataModel>();
            AppendReq(sb, $"基地等级 >= {data.Level}", baseDm.BaseLevel >= data.Level);

            // 前置科技
            if (data.PrereqTechIds != null)
            {
                for (int i = 0; i < data.PrereqTechIds.Length; i++)
                {
                    var prereq = data.PrereqTechIds[i];
                    if (string.IsNullOrWhiteSpace(prereq)) continue;
                    AppendReq(sb, $"前置科技: {prereq}", TechProgressDataModel.IsUnlocked(prereq));
                }
            }

            // AND 条件
            if (data.AllConditions != null)
            {
                for (int i = 0; i < data.AllConditions.Length; i++)
                {
                    var c = data.AllConditions[i];
                    AppendReq(sb, DescribeCondition(c), IsConditionMet(c));
                }
            }

            // OR 条件
            if (data.AnyConditions != null && data.AnyConditions.Length > 0)
            {
                bool anyOk = false;
                for (int i = 0; i < data.AnyConditions.Length; i++)
                {
                    if (IsConditionMet(data.AnyConditions[i])) { anyOk = true; break; }
                }

                sb.AppendLine("满足其一:");
                for (int i = 0; i < data.AnyConditions.Length; i++)
                {
                    var c = data.AnyConditions[i];
                    AppendReq(sb, " - " + DescribeCondition(c), anyOk ? IsConditionMet(c) : false);
                }
            }

            // 成本（消耗）
            if (data.CostMaterial != null && data.CostMaterial.Length > 0)
            {
                sb.AppendLine("消耗:");
                for (int i = 0; i < data.CostMaterial.Length; i++)
                {
                    var cost = data.CostMaterial[i];
                    if (string.IsNullOrWhiteSpace(cost.str)) continue;
                    AppendReq(sb, $" - {cost.str} x {cost.num}", ItemCollectionDataModel.HasItem(cost.str, cost.num));
                }
            }
        }

        tooltipText.supportRichText = true;
        tooltipText.text = sb.ToString();
    }

    private static void AppendReq(StringBuilder sb, string text, bool ok)
    {
        if (ok)
        {
            sb.AppendLine(text);
        }
        else
        {
            sb.AppendLine("<color=#FF0000>" + EscapeRich(text) + "</color>");
        }
    }

    private static string DescribeCondition(TechCondition c)
    {
        switch (c.Type)
        {
            case TechConditionType.BaseLevelGE:
                return $"基地等级 >= {c.I1}";
            case TechConditionType.HasTech:
                return $"已研究科技: {c.S1}";
            case TechConditionType.HasItem:
                return $"拥有物品: {c.S1} x {c.I1}";
            case TechConditionType.QuestDone:
                return $"任务完成: {c.S1}";
            case TechConditionType.PlotFlag:
                return $"剧情旗标: {c.S1}";
            default:
                return c.Type.ToString();
        }
    }

    private static bool IsConditionMet(TechCondition c)
    {
        switch (c.Type)
        {
            case TechConditionType.BaseLevelGE:
                {
                    var baseDm = GF.DataModel.GetOrCreate<BaseProgressDataModel>();
                    return baseDm.BaseLevel >= c.I1;
                }
            case TechConditionType.HasTech:
                return !string.IsNullOrWhiteSpace(c.S1) && TechProgressDataModel.IsUnlocked(c.S1);
            case TechConditionType.HasItem:
                return !string.IsNullOrWhiteSpace(c.S1) && ItemCollectionDataModel.HasItem(c.S1, c.I1);
            case TechConditionType.QuestDone:
            case TechConditionType.PlotFlag:
                // 预留入口：未接入前默认不满足
                return false;
            default:
                return false;
        }
    }

    private static string EscapeRich(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
