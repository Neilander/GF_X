using System;
using DG.Tweening;
using UnityEngine;

public partial class GoalConditionItem : UIItemBase
{
    public void SetText(string text)
    {
        if (varGoalConditionItem != null)
            varGoalConditionItem.text = text;
    }

    public void SetState(bool satisfied, Color iconActiveColor, Color iconInactiveColor, Color textSatisfiedColor, Color textUnsatisfiedColor)
    {
        if (varIcon != null)
            varIcon.color = satisfied ? iconActiveColor : iconInactiveColor;

        if (varGoalConditionItem != null)
            varGoalConditionItem.color = satisfied ? textSatisfiedColor : textUnsatisfiedColor;
    }
}
