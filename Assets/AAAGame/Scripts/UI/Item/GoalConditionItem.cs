using System;
using DG.Tweening;
using UnityEngine;

public partial class GoalConditionItem : UIItemBase
{
	public void SetText(string text)
	{
		if (varGoalConditionItem != null)
		{
			varGoalConditionItem.text = text;
		}
	}
}
