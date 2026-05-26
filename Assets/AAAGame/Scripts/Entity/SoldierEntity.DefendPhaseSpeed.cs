public partial class SoldierEntity
{
    private const string DefendPhaseSpeedBuffId = "defend_phase_speed_override";

    private bool m_DefendPhaseSpeedControlEnabled;
    private bool m_HasEnteredPlayerStrongholdInDefend;
    private Fix64 m_DefendPhaseSpeedValue;

    public void EnableDefendPhaseSpeedControl(Fix64 speedValue)
    {
        m_DefendPhaseSpeedValue = speedValue;
        m_DefendPhaseSpeedControlEnabled = speedValue > Fix64.Zero;
        if (m_DefendPhaseSpeedControlEnabled && IsInsidePlayerStronghold())
        {
            m_HasEnteredPlayerStrongholdInDefend = true;
        }

        RefreshDefendPhaseSpeedBuff();
    }

    public void DisableDefendPhaseSpeedControl()
    {
        m_DefendPhaseSpeedControlEnabled = false;
        m_HasEnteredPlayerStrongholdInDefend = false;
        m_DefendPhaseSpeedValue = Fix64.Zero;
        BuffComp?.RemoveBuff(DefendPhaseSpeedBuffId);
    }

    private void TickDefendPhaseSpeedControl()
    {
        if (!m_DefendPhaseSpeedControlEnabled)
            return;

        if (!m_HasEnteredPlayerStrongholdInDefend && IsInsidePlayerStronghold())
        {
            m_HasEnteredPlayerStrongholdInDefend = true;
        }

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
                           && !m_HasEnteredPlayerStrongholdInDefend
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

    private bool IsInsidePlayerStronghold()
    {
        if (LevelEntity.ActiveLevelEntity == null)
            return false;

        Stronghold stronghold = LevelEntity.GetStrongholdAtWorldPosition(Position);
        return stronghold != null && stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId;
    }
}
