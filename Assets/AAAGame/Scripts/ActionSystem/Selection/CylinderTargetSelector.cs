using UnityEngine;

public class CylinderTargetSelector:TargetableSelector
{
    private CapsuleCollider _collider;
    private SpriteRenderer _spriteRenderer;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _collider = GetComponent<CapsuleCollider>();
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (_collider == null)
            throw new System.InvalidOperationException($"CylinderTargetSelector requires CapsuleCollider. gameObject={gameObject.name}");
    }

    /// <summary>
    /// 改变胶囊中心圆柱体的尺寸
    /// </summary>
    /// <param name="ratio">X是半径，Y是高度</param>
    public override void ChangeRange(Vector3 ratio)
    {
        SetLogicQueryRadius(ratio.x);
        _collider.radius = ratio.x;
        _collider.height = ratio.y+2*ratio.x;

        if (_spriteRenderer != null)
            _spriteRenderer.transform.localScale = new Vector2(ratio.x , ratio.x );
    }
}
