using System.Collections;
using UnityEngine;

public class PlayerEntity : SkillEntity
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        CameraController.Instance.SetFollowTarget(gameObject.transform);

        Side = SideType.PlayerSide;
    }

}
