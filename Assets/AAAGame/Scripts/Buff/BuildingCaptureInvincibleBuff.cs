using GameFramework;

/// <summary>
/// 据点占领后短时无敌 Buff（3s）——仅负责注册/注销无敌来源。
/// </summary>
public class BuildingCaptureInvincibleBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "building_capture_invincible::source"
            : $"{buffData.id}::source";

        if (!hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
            throw new System.InvalidOperationException("BuildingCaptureInvincibleBuff requires a building logic context.");
        building.RegisterInvincibleSource(_invincibleSourceId);
    }

    public override void OnRemove()
    {
        if (!hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
            throw new System.InvalidOperationException("BuildingCaptureInvincibleBuff requires a building logic context.");
        building.UnregisterInvincibleSource(_invincibleSourceId);
    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_invincibleSourceId);
    }
}
