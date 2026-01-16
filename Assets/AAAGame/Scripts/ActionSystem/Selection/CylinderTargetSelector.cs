using UnityEngine;

public class CylinderTargetSelector:TargetableSelector
{
    private CapsuleCollider _collider;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _collider = GetComponent<CapsuleCollider>();
    }

    /// <summary>
    /// 改变胶囊中心圆柱体的尺寸
    /// </summary>
    /// <param name="ratio">X是半径，Y是高度</param>
    public override void ChangeRange(Vector3 ratio)
    {
        _collider.radius = ratio.x;
        _collider.height = ratio.y+2*ratio.x;
    }
}