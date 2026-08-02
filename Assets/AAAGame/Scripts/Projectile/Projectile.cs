using UnityEngine;

public class Projectile : EntityBase
{
    private ulong _logicProjectileId;
    private RangedWeaponSO _weaponSO;
    private bool _completionPresented;
    private FixVector2 _previousLogicPosition;
    private Vector2 _presentationOffset;

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
        LogicProjectileViewState initialState = LogicProjectileService.GetRequiredViewState(_logicProjectileId);
        _previousLogicPosition = initialState.Position;
        _presentationOffset = new Vector2(
            transform.position.x - (float)initialState.Position.x,
            transform.position.z - (float)initialState.Position.y);
        ApplyViewState(initialState, true);
    }

    protected override void OnRenderFrameUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnRenderFrameUpdate(elapseSeconds, realElapseSeconds);
        if (_logicProjectileId == 0 || _completionPresented)
            return;

        LogicProjectileViewState state = LogicProjectileService.GetRequiredViewState(_logicProjectileId);
        ApplyViewState(state, false);
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
        _previousLogicPosition = default;
        _presentationOffset = Vector2.zero;
        base.OnHide(isShutdown, userData);
    }

    private void ApplyViewState(LogicProjectileViewState state, bool initializing)
    {
        if (!initializing)
            _presentationOffset = AdvancePresentationOffset(_presentationOffset, _previousLogicPosition, state.Position, state.Completed);

        Vector3 previous = transform.position;
        Vector3 next = new Vector3(
            (float)state.Position.x + _presentationOffset.x,
            previous.y,
            (float)state.Position.y + _presentationOffset.y);
        Vector3 direction = next - previous;
        transform.position = next;
        _previousLogicPosition = state.Position;
        if (direction.sqrMagnitude > 0.000001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public static Vector2 AdvancePresentationOffset(
        Vector2 currentOffset,
        FixVector2 previousLogicPosition,
        FixVector2 currentLogicPosition,
        bool completed)
    {
        if (completed)
            return Vector2.zero;

        FixVector2 logicDelta = currentLogicPosition - previousLogicPosition;
        float traveledDistance = (float)FixVector2.Magnitude(logicDelta);
        return Vector2.MoveTowards(currentOffset, Vector2.zero, traveledDistance);
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
