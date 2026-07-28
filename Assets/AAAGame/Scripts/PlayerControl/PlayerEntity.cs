using System.Collections;
using UnityEngine;

public class PlayerEntity : SkillEntity
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        if (display == null)
            throw new System.InvalidOperationException("PlayerEntity.OnShow failed: player presentation transform is missing.");
        if (CameraController.Instance == null)
            throw new System.InvalidOperationException("PlayerEntity.OnShow failed: CameraController.Instance is null.");

        CameraController.Instance.SetFollowTarget(display);

        Side = SideType.PlayerSide;
    }

}
