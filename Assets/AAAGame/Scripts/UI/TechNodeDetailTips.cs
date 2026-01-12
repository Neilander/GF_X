using System;
using GameFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class TechNodeDetailTips : UIFormBase
{
	public const string P_TechId = "TechId";
	public const string P_NodePos = "NodePos";

	[SerializeField] private float edgePadding = 24f;

	private string m_TechId;
	private Vector2 m_NodePos;

	private static readonly Color OkColor = Color.white;
	private static readonly Color FailColor = Color.red;
	private static readonly Color DisabledColor = Color.gray;

	protected override void OnOpen(object userData)
	{
		base.OnOpen(userData);

		m_TechId = Params.Get<VarString>(P_TechId);
		m_NodePos = Params.Get<VarVector2>(P_NodePos);

		varResearchBtn.onClick.RemoveListener(OnClickResearch);
		varResearchBtn.onClick.AddListener(OnClickResearch);

		RefreshAll();
	}

	protected override void OnClose(bool isShutdown, object userData)
	{
		varResearchBtn.onClick.RemoveListener(OnClickResearch);

		base.OnClose(isShutdown, userData);
	}

	public void RefreshAll()
	{
		var data = TechNodeDataModel.GetNodeData(m_TechId);
		bool unlocked = TechProgressDataModel.IsUnlocked(m_TechId);
		bool canResearch = !unlocked && TechProgressDataModel.CanResearch(m_TechId, out _);

		varNameText.text = GF.Localization.GetString(data.NameKey);
		varDescriptionText.text = GF.Localization.GetString(data.DescriptionKey);
		RefreshConditions(data, unlocked);
		RefreshCosts(data, unlocked);
		RefreshResearchButton(unlocked, canResearch);

		// 内容高度可能变化，重新做一次偏移/夹取。
		Reposition();
	}

	private void RefreshResearchButton(bool unlocked, bool canResearch)
	{
		string id = unlocked ? "Tech_Researched" : "Tech_Research";
		varResearchBtnText.text = LocalizationTextDataModel.GetText(id);
		varResearchBtnText.color = (unlocked || !canResearch) ? DisabledColor : OkColor;

		varResearchBtn.interactable = !unlocked && canResearch;
	}

	private void RefreshConditions(TechNodeTable data, bool unlocked)
	{

		UnspawnAllItem<UIItemObject>(varTextLineUnit);


		// 必备：基地等级
		int curLevel = ProfileDataModel.GetData(ProfileDataType.BaseLevel);
		bool okLevel = unlocked || curLevel >= data.Level;
		SpawnTextLine($"基地等级 >= {data.Level} (当前 {curLevel})", okLevel, forceGray: unlocked);

		// 前置科技
		if (data.PrereqTechIds != null)
		{
			for (int i = 0; i < data.PrereqTechIds.Length; i++)
			{
				var prereq = data.PrereqTechIds[i];
				bool ok = unlocked || TechProgressDataModel.IsUnlocked(prereq);

				var prereqData = TechNodeDataModel.GetNodeData(prereq);

				SpawnTextLine($"前置科技: {GF.Localization.GetString(prereqData.NameKey)}", ok, forceGray: unlocked);
			}
		}

		// AND
		if (data.AllConditions != null)
		{
			for (int i = 0; i < data.AllConditions.Length; i++)
			{
				var c = data.AllConditions[i];
				bool ok = unlocked || UnlockCondition.IsSatisfied(c);
				SpawnTextLine(DescribeUnlockCondition(c), ok, forceGray: unlocked);
			}
		}

		// OR
		if (data.AnyConditions != null && data.AnyConditions.Length > 0)
		{
			bool anyOk = unlocked;
			if (!unlocked)
			{
				for (int i = 0; i < data.AnyConditions.Length; i++)
				{
					if (UnlockCondition.IsSatisfied(data.AnyConditions[i])) { anyOk = true; break; }
				}
			}

			SpawnTextLine("满足其一:", anyOk, forceGray: unlocked);

			for (int i = 0; i < data.AnyConditions.Length; i++)
			{
				var c = data.AnyConditions[i];
				bool ok = unlocked || (anyOk && UnlockCondition.IsSatisfied(c));
				SpawnTextLine("- " + DescribeUnlockCondition(c), ok, forceGray: unlocked);
			}
		}
	}

	private void RefreshCosts(TechNodeTable data, bool unlocked)
	{

		UnspawnAllItem<UIItemObject>(varItemUnit);

		if (data?.CostMaterial == null)
			return;

		for (int i = 0; i < data.CostMaterial.Length; i++)
		{
			var cost = data.CostMaterial[i];

			var item = SpawnItem<UIItemObject>(varItemUnit, varCosts).itemLogic as ItemUnit;

			// ItemUnit 内部会根据背包数量刷新颜色（不足为红色）。
			item.SetData(cost.str, cost.num, true);

			if (unlocked)
				ApplyDisabledColor(item.gameObject);
		}
	}

	private void OnClickResearch()
	{
		// 研究成功/失败事件由 TechProgressDataModel 触发，父界面会刷新节点状态。
		TechProgressDataModel.Research(m_TechId);
		RefreshAll();
	}

	private void Reposition()
	{
		if (varTechNodeDetailTipsPanel == null)
			return;

		var parent = varTechNodeDetailTipsPanel.parent as RectTransform;
		if (parent == null)
			return;

		// m_NodePos 现在是屏幕坐标：先换算到 tips parent 的局部坐标。
		var canvas = GetComponentInParent<Canvas>();
		Camera cam = null;
		if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
			cam = canvas.worldCamera;

		Vector2 baseLocalPos = Vector2.zero;
		RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, m_NodePos, cam, out baseLocalPos);

		// ContentSizeFitter/HorizontalLayoutGroup 会驱动尺寸变化；先强制刷新布局，拿到最新 rect.size。
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(varTechNodeDetailTipsPanel);

		// 让提示框靠近节点，同时做简单夹取。
		float x = baseLocalPos.x + 320f;
		float y = baseLocalPos.y + 120f;
		varTechNodeDetailTipsPanel.anchoredPosition = new Vector2(x, y);
		// 夹取到父级范围内（布局驱动尺寸：用 rect.size + pivot 来算可视范围）
		var size = varTechNodeDetailTipsPanel.rect.size;
		var pSize = parent.rect.size;
		var pivot = varTechNodeDetailTipsPanel.pivot;

		float w = Mathf.Max(0f, size.x);
		float h = Mathf.Max(0f, size.y);

		float minX = -pSize.x * 0.5f + w * pivot.x;
		float maxX = pSize.x * 0.5f - w * (1f - pivot.x);
		float minY = -pSize.y * 0.5f + h * pivot.y;
		float maxY = pSize.y * 0.5f - h * (1f - pivot.y);

		// 留出一定边距，避免贴边
		float pad = Mathf.Max(0f, edgePadding);
		minX += pad;
		maxX -= pad;
		minY += pad;
		maxY -= pad;
		if (minX > maxX) { float mid = (minX + maxX) * 0.5f; minX = mid; maxX = mid; }
		if (minY > maxY) { float mid = (minY + maxY) * 0.5f; minY = mid; maxY = mid; }

		var pos = varTechNodeDetailTipsPanel.anchoredPosition;
		pos.x = Mathf.Clamp(pos.x, minX, maxX);
		pos.y = Mathf.Clamp(pos.y, minY, maxY);
		varTechNodeDetailTipsPanel.anchoredPosition = pos;
	}

	private void SpawnTextLine(string text, bool ok, bool forceGray)
	{
		var unit = SpawnItem<UIItemObject>(varTextLineUnit, varConditions).itemLogic as TextLineUnit;
		var tmp = unit.varTextLineUnit;
		tmp.text = text;
		if (forceGray)
		{
			ApplyDisabledColor(unit.gameObject);
			tmp.color = DisabledColor;
		}
		else
		{
			tmp.color = ok ? OkColor : FailColor;
		}
	}

	private static void ApplyDisabledColor(GameObject root)
	{
		if (root == null)
			return;

		var gs = root.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < gs.Length; i++)
		{
			var g = gs[i];
			if (g == null) continue;
			var c = g.color;
			g.color = new Color(DisabledColor.r, DisabledColor.g, DisabledColor.b, c.a);
		}
	}

	private static string DescribeUnlockCondition(UnlockCondition c)
	{
		if (c == null) return string.Empty;
		switch (c.Type)
		{
			case UnlockConditionType.Tech:
				{
					var data = TechNodeDataModel.GetNodeData(c.Identifier);
					if (data != null && !string.IsNullOrWhiteSpace(data.NameKey))
						return $"解锁科技: {GF.Localization.GetString(data.NameKey)}";
					return $"解锁科技: {c.Identifier}";
				}
			case UnlockConditionType.Capability:
				return $"持有能力: {c.Identifier}";
			case UnlockConditionType.BaseLevel:
				return $"基地等级 >= {c.Identifier}";
			default:
				return c.Type + ":" + c.Identifier;
		}
	}
}