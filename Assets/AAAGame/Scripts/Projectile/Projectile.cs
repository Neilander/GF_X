using UnityEngine;

public class Projectile : EntityBase
{
    private ulong _logicProjectileId;
    private RangedWeaponSO _weaponSO;
    private bool _completionPresented;

    protected override bool ShouldRunLogicFrameUpdate => false;

    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        if (!(userData is EntityParams entityParams))
            throw new System.InvalidOperationException("Projectile.OnShow failed: EntityParams are required.");
        if (entityParams.LogicProjectileId == 0)
            throw new System.InvalidOperationException("Projectile.OnShow failed: logic projectile id is missing.");

        _logicProjectileId = entityParams.LogicProjectileId;
        _weaponSO = entityParams.WeaponSO as RangedWeaponSO;
        _completionPresented = false;
        LogicProjectileService.BindView(_logicProjectileId);
        ApplyViewState(LogicProjectileService.GetRequiredViewState(_logicProjectileId));
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        if (_logicProjectileId == 0 || _completionPresented)
            return;

        LogicProjectileViewState state = LogicProjectileService.GetRequiredViewState(_logicProjectileId);
        ApplyViewState(state);
        if (!state.Completed)
            return;

        _completionPresented = true;
        if (state.Hit)
            PresentHit(state.Position);
        GF.Entity.HideEntity(Entity.Id);
    }

    protected override void OnHide(bool isShutdown, object userData)
    {
        if (_logicProjectileId != 0)
            LogicProjectileService.ReleaseView(_logicProjectileId);
        _logicProjectileId = 0;
        _weaponSO = null;
        _completionPresented = true;
        base.OnHide(isShutdown, userData);
    }

    private void ApplyViewState(LogicProjectileViewState state)
    {
        Vector3 previous = transform.position;
        Vector3 next = new Vector3((float)state.Position.x, previous.y, (float)state.Position.y);
        Vector3 direction = next - previous;
        transform.position = next;
        if (direction.sqrMagnitude > 0.000001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void PresentHit(FixVector2 hitPosition)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play("basicAttack");
        if (_weaponSO == null || string.IsNullOrEmpty(_weaponSO.HitVfxName))
            return;

        Vector3 worldPosition = new Vector3((float)hitPosition.x, transform.position.y, (float)hitPosition.y);
        EntityParams vfxParams = EntityParams.Create(worldPosition);
        GF.Entity.ShowEffect(_weaponSO.HitVfxName, vfxParams, 2f);
    }
}
