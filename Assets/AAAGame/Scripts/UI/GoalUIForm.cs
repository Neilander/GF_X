using DG.Tweening;
using GameFramework;
using GameFramework.Event;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class GoalUIForm : UIFormBase
{
	private bool m_IsExpanded = true;

	protected override void OnOpen(object userData)
	{
		base.OnOpen(userData);
		m_IsExpanded = true;
		BindButtons();
		RefreshGoalList();
		ApplyExpandedState();
		GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
	}

	protected override void OnClose(bool isShutdown, object userData)
	{
		GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
		UnbindButtons();
		UnspawnAllItem<UIItemObject>(varGoalConditionItem);
		base.OnClose(isShutdown, userData);
	}

	private void OnIngameValueChanged(object sender, GameEventArgs e)
	{
		var args = e as IngameValueChangedEventArgs;
		if (args != null && args.DataType == IngameValueType.Day)
		{
			RefreshGoalList();
		}
	}

	protected override void OnButtonClick(object sender, Button btSelf)
	{
		base.OnButtonClick(sender, btSelf);

		if (btSelf == var展开)
		{
			m_IsExpanded = !m_IsExpanded;
			ApplyExpandedState();
		}
	}

	private void RefreshGoalList()
	{
		if (varTextPanel == null || varGoalConditionItem == null)
		{
			Log.Error("[GoalUIForm] RefreshGoalList failed: missing text panel or goal condition item template.");
			return;
		}

		UnspawnAllItem<UIItemObject>(varGoalConditionItem);

		var gameEndManager = GameEntry.GetComponent<GameEndManager>();
		if (gameEndManager == null)
		{
			Log.Error("[GoalUIForm] RefreshGoalList failed: GameEndManager is missing.");
			return;
		}

		if (!gameEndManager.TryGetLevelObjectiveLines(out var objectiveLines) || objectiveLines == null)
		{
			Log.Warning("[GoalUIForm] RefreshGoalList skipped: no displayable objectives.");
			return;
		}

		for (int i = 0; i < objectiveLines.Count; i++)
		{
			string line = objectiveLines[i];
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var itemObject = SpawnItem<UIItemObject>(varGoalConditionItem, varTextPanel.transform);
			if (itemObject?.itemLogic is GoalConditionItem goalConditionItem)
			{
				goalConditionItem.SetText(line);
			}
		}
	}

	private void BindButtons()
	{
		if (var展开 == null)
		{
			return;
		}

		var展开.onClick.RemoveListener(OnExpandButtonClicked);
		var展开.onClick.AddListener(OnExpandButtonClicked);
	}

	private void UnbindButtons()
	{
		if (var展开 == null)
		{
			return;
		}

		var展开.onClick.RemoveListener(OnExpandButtonClicked);
	}

	private void OnExpandButtonClicked()
	{
		ClickUIButton(var展开);
	}

	private void ApplyExpandedState()
	{
		if (varTextPanel != null)
		{
			varTextPanel.SetActive(m_IsExpanded);
		}

		var buttonText = var展开 != null ? var展开.GetComponentInChildren<TMP_Text>(true) : null;
		if (buttonText != null)
		{
			buttonText.text = m_IsExpanded ? "收起目标" : "展开目标";
		}
	}
}
