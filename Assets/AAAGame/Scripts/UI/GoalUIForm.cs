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
	private const string PrimaryTitleTextId = "GoalUI_PrimaryTitle";
	private const string OptionalTitleTextId = "GoalUI_OptionalTitle";
	private const string OptionalExperienceTextId = "GoalUI_OptionalExperience";

	private bool m_IsExpanded = true;

	protected override void OnOpen(object userData)
	{
		base.OnOpen(userData);
		m_IsExpanded = true;
		BindButtons();
		RefreshGoalList();
		ApplyExpandedState();
		GF.Event.Subscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
		GF.Event.Subscribe(TutorialObjectivesChangedEventArgs.EventId, OnTutorialObjectivesChanged);
		GF.Event.Subscribe(LevelObjectivesChangedEventArgs.EventId, OnLevelObjectivesChanged);
	}

	protected override void OnClose(bool isShutdown, object userData)
	{
		GF.Event.Unsubscribe(IngameValueChangedEventArgs.EventId, OnIngameValueChanged);
		GF.Event.Unsubscribe(TutorialObjectivesChangedEventArgs.EventId, OnTutorialObjectivesChanged);
		GF.Event.Unsubscribe(LevelObjectivesChangedEventArgs.EventId, OnLevelObjectivesChanged);
		UnbindButtons();
		UnspawnAllItem<UIItemObject>(varGoalConditionItem);
		base.OnClose(isShutdown, userData);
	}

	private void OnTutorialObjectivesChanged(object sender, GameEventArgs e)
	{
		RefreshGoalList();
	}

	private void OnLevelObjectivesChanged(object sender, GameEventArgs e)
	{
		RefreshGoalList();
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

		if (TutorialObjectiveService.HasObjectives)
		{
			IReadOnlyList<TutorialObjective> objectives = TutorialObjectiveService.GetSnapshot();
			for (int i = 0; i < objectives.Count; i++)
			{
				TutorialObjective objective = objectives[i];
				var itemObject = SpawnItem<UIItemObject>(varGoalConditionItem, varTextPanel.transform);
				if (itemObject?.itemLogic is GoalConditionItem goalConditionItem)
				{
					goalConditionItem.SetTutorialObjective(
						ObjectiveDataModel.GetText(
							objective.DefinitionIdentifier,
							objective.FormatArgs),
						objective.Status);
				}
			}
			return;
		}

		var gameEndManager = GameEntry.GetComponent<GameEndManager>();
		if (gameEndManager == null)
		{
			Log.Error("[GoalUIForm] RefreshGoalList failed: GameEndManager is missing.");
			return;
		}

		if (!gameEndManager.TryGetLevelObjectives(out IReadOnlyList<LevelObjectiveState> levelObjectives))
		{
			Log.Warning("[GoalUIForm] RefreshGoalList skipped: no displayable objectives.");
			return;
		}

		SpawnLevelObjectiveSection(levelObjectives, true, PrimaryTitleTextId);
		SpawnLevelObjectiveSection(levelObjectives, false, OptionalTitleTextId);
	}

	private void SpawnLevelObjectiveSection(
		IReadOnlyList<LevelObjectiveState> objectives,
		bool isPrimary,
		string titleTextId)
	{
		bool hasObjectives = false;
		for (int i = 0; i < objectives.Count; i++)
		{
			if (objectives[i].Definition.IsPrimary == isPrimary)
			{
				hasObjectives = true;
				break;
			}
		}
		if (!hasObjectives)
			return;

		var titleObject = SpawnItem<UIItemObject>(varGoalConditionItem, varTextPanel.transform);
		if (titleObject?.itemLogic is GoalConditionItem titleItem)
			titleItem.SetSectionTitle(LocalizationTextDataModel.GetText(titleTextId));

		for (int i = 0; i < objectives.Count; i++)
		{
			LevelObjectiveState objective = objectives[i];
			if (objective.Definition.IsPrimary != isPrimary)
				continue;

			string line = ObjectiveDataModel.GetText(
				objective.Definition.DefinitionId,
				objective.Definition.UniqueValues);
			if (!isPrimary && objective.Definition.Experience > 0)
			{
				line += string.Format(
					LocalizationTextDataModel.GetText(OptionalExperienceTextId),
					objective.Definition.Experience);
			}

			var itemObject = SpawnItem<UIItemObject>(varGoalConditionItem, varTextPanel.transform);
			if (itemObject?.itemLogic is GoalConditionItem goalConditionItem)
			{
				goalConditionItem.SetLevelObjective(line, objective.Status);
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
