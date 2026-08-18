/// <summary>
/// 建筑阶段保护 Buff：
/// - 战斗保护：非 Invade 阶段给敌方建筑提供“无敌/不可被索敌/不攻击”保护。
/// - 血条效果：进入 Build 阶段后为双方建筑施加“禁用血条”效果，离开 Build 后恢复。
/// </summary>
public class BuildingPhaseGuardBuff : BuffCallback, ILogicDeterministicStateContributor
{
    private bool _subscribed;
    private string _invincibleSourceId;

    public override void OnAdd()
    {
        _invincibleSourceId = string.IsNullOrEmpty(buffData?.id)
            ? "building_phase_guard::source"
            : $"{buffData.id}::source";

        SubscribeEvents();
        RefreshProtectionState();
    }

    public override void OnRemove()
    {
        UnsubscribeEvents();

        if (hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
        {
            building.SetPhaseProtectionByBuff(false);
            building.UnregisterInvincibleSource(_invincibleSourceId);
        }
    }

    private void SubscribeEvents()
    {
        if (_subscribed)
            return;

        LogicPhaseCommandService.PhaseApplied += OnPhaseChanged;
        if (!hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
            throw new System.InvalidOperationException("BuildingPhaseGuardBuff requires a building logic context.");
        building.OwnerFactionChanged += OnOwnerFactionChanged;
        _subscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_subscribed)
            return;

        LogicPhaseCommandService.PhaseApplied -= OnPhaseChanged;
        if (!hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
            throw new System.InvalidOperationException("BuildingPhaseGuardBuff requires a building logic context.");
        building.OwnerFactionChanged -= OnOwnerFactionChanged;
        _subscribed = false;
    }

    private void OnPhaseChanged(GamePhase oldPhase, GamePhase newPhase)
    {
        RefreshProtectionState();
    }

    private void OnOwnerFactionChanged(int oldFactionId, int newFactionId)
    {
        RefreshProtectionState();
    }

    private void RefreshProtectionState()
    {
        if (!hostEntity.TryGetLogicBuilding(out IBuildingLogicContext building))
            throw new System.InvalidOperationException("BuildingPhaseGuardBuff requires a building logic context.");

        GamePhase phase = LogicPhaseCommandService.GetRequiredCurrentPhase();

        bool isEnemyBuilding = building.OwnerFactionId == EntitySideHelper.EnemyFactionId;
        bool shouldProtect = isEnemyBuilding
                             && (phase != GamePhase.Invade
                                 || !TutorialManager.IsInvadeStrongholdResponseAllowed(building.StrongholdId));

        building.SetPhaseProtectionByBuff(shouldProtect);
        if (shouldProtect)
            building.RegisterInvincibleSource(_invincibleSourceId);
        else
            building.UnregisterInvincibleSource(_invincibleSourceId);

    }

    public void WriteDeterministicState(LogicStateHasher hasher)
    {
        hasher.Add(_subscribed);
        hasher.Add(_invincibleSourceId);
    }
}
