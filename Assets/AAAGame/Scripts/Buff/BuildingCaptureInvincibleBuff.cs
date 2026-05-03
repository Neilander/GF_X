using GameFramework;

/// <summary>
/// 据点占领后短时无敌 Buff（3s）——仅负责注册/注销无敌来源。
/// </summary>
public class BuildingCaptureInvincibleBuff : BuffCallback
{
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "building_capture_invincible::source"
            : $"{buffData.id}::source";

        if (hostEntity is BuildingEntity building)
        {
            building.RegisterInvincibleSource(_invincibleSourceId);
        }
    }

    public override void OnRemove()
    {
        if (hostEntity is BuildingEntity building)
        {
            building.UnregisterInvincibleSource(_invincibleSourceId);
        }
    }
}
