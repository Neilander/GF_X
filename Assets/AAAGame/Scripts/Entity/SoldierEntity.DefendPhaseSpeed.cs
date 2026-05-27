public partial class SoldierEntity
{
    private const string DefendPhaseSpeedBuffId = "defend_phase_speed_override";
    public const string P_DefendAssignedSpeed = "DefendAssignedSpeed";

    private bool m_DefendPhaseSpeedControlEnabled;
    private Fix64 m_DefendPhaseSpeedValue;

    public void EnableDefendPhaseSpeedControl(Fix64 speedValue)
    {
        m_DefendPhaseSpeedValue = speedValue;
        m_DefendPhaseSpeedControlEnabled = speedValue > Fix64.Zero;
        RefreshDefendPhaseSpeedBuff();
    }

    public void DisableDefendPhaseSpeedControl()
    {
        m_DefendPhaseSpeedControlEnabled = false;
        m_DefendPhaseSpeedValue = Fix64.Zero;
        BuffComp?.RemoveBuff(DefendPhaseSpeedBuffId);
    }

    private void TickDefendPhaseSpeedControl()
    {
        if (!m_DefendPhaseSpeedControlEnabled)
            return;

        RefreshDefendPhaseSpeedBuff();
    }

    private void OnPhaseChangedForDefendPhaseSpeed(IngamePhaseChangedEventArgs args)
    {
        if (args == null)
            return;

        if (args.NewPhase != GamePhase.Defend)
        {
            BuffComp?.RemoveBuff(DefendPhaseSpeedBuffId);
            return;
        }

        RefreshDefendPhaseSpeedBuff();
    }

    private void RefreshDefendPhaseSpeedBuff()
    {
        if (BuffComp == null)
            return;

        GamePhase phase = (GamePhase)InGameDataModel.GetValue(IngameValueType.Phase);
        bool shouldApply = m_DefendPhaseSpeedControlEnabled
                           && Alive
                           && Side == SideType.EnemySide
                           && phase == GamePhase.Defend
                           && IsOutsidePlayerVision()
                           && m_DefendPhaseSpeedValue > Fix64.Zero;

        bool hasBuff = BuffComp.HasBuff(DefendPhaseSpeedBuffId);
        if (shouldApply)
        {
            if (hasBuff)
                return;

            BuffData buffData = BuffData.Create(
                id: DefendPhaseSpeedBuffId,
                duration: float.MaxValue,
                isForever: true,
                maxStack: 1,
                modules: new System.Collections.Generic.List<BuffCallback> { new FixedMoveSpeedOverrideBuff(m_DefendPhaseSpeedValue) });
            BuffComp.AddBuff(buffData, this);
            return;
        }

        if (hasBuff)
            BuffComp.RemoveBuff(DefendPhaseSpeedBuffId);
    }

    private bool IsOutsidePlayerVision()
    {
        var fogManager = AAAGame.MiniMap.FOG3.Fog3Manager.Instance;
        var mapData = fogManager != null && fogManager.IsInitialized ? fogManager.MapData : null;
        if (mapData == null)
            return false;

        return !mapData.IsPositionVisible(Position);
    }
}
