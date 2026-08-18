using AAAGame.Card;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class LogicRewardStateService
{
	private static int s_EnemyDeadSupplyRemainder;
	private static readonly HashSet<string> s_PlayerCapturedStrongholdIdsInCurrentInvade = new HashSet<string>();
	private static readonly List<string> s_DeterministicCapturedStrongholdIds = new List<string>();

	public static int PlayerCapturedStrongholdCount => s_PlayerCapturedStrongholdIdsInCurrentInvade.Count;

	public static void Reset()
	{
		s_EnemyDeadSupplyRemainder = 0;
		s_PlayerCapturedStrongholdIdsInCurrentInvade.Clear();
	}

	public static int AccumulateEnemyDeadSupply(int deadSupply, int supplyPerCoin)
	{
		if (deadSupply < 0)
			throw new ArgumentOutOfRangeException(nameof(deadSupply));
		if (supplyPerCoin <= 0)
			throw new ArgumentOutOfRangeException(nameof(supplyPerCoin));

		int accumulated = checked(s_EnemyDeadSupplyRemainder + deadSupply);
		int gainedCoin = accumulated / supplyPerCoin;
		s_EnemyDeadSupplyRemainder = accumulated % supplyPerCoin;
		return gainedCoin;
	}

	public static void RecordCapturedStronghold(string strongholdId)
	{
		if (string.IsNullOrWhiteSpace(strongholdId))
			throw new ArgumentException("Captured stronghold id is empty.", nameof(strongholdId));
		s_PlayerCapturedStrongholdIdsInCurrentInvade.Add(strongholdId);
	}

	public static void ClearCapturedStrongholds()
	{
		s_PlayerCapturedStrongholdIdsInCurrentInvade.Clear();
	}

	public static void WriteDeterministicState(LogicStateHasher hasher)
	{
		if (hasher == null)
			throw new ArgumentNullException(nameof(hasher));

		hasher.Add(s_EnemyDeadSupplyRemainder);
		s_DeterministicCapturedStrongholdIds.Clear();
		s_DeterministicCapturedStrongholdIds.AddRange(s_PlayerCapturedStrongholdIdsInCurrentInvade);
		s_DeterministicCapturedStrongholdIds.Sort(StringComparer.Ordinal);
		hasher.Add(s_DeterministicCapturedStrongholdIds.Count);
		for (int i = 0; i < s_DeterministicCapturedStrongholdIds.Count; i++)
			hasher.Add(s_DeterministicCapturedStrongholdIds[i]);
	}
}

internal enum ProductionBuildingDisabledResult
{
	Ignore,
	RecordPlayerProductionLoss,
	GrantEnemyProductionReward,
}

public class RewardManager : GameFrameworkComponent
{
	private readonly struct CoinFlyPresentationRequest
	{
		public CoinFlyPresentationRequest(FixVector2 sourcePosition, int coinAmount)
		{
			SourcePosition = sourcePosition;
			CoinAmount = coinAmount;
		}

		public FixVector2 SourcePosition { get; }
		public int CoinAmount { get; }
	}

	private const string KillRewardSupplyRatioConfigKey = "KillRewardSupplyRatio";
	private const string BaseResourceIncomeDailyGrowthConfigKey = "BaseResourceIncomeDailyGrowth";
	private const string DefendPhaseBaseResourceIncomeConfigKey = "DefendPhaseBaseResourceIncome";
	private const string InvadePhaseIncomePerCapturedOutpostConfigKey = "InvadePhaseIncomePerCapturedOutpost";
	private const string DemolishedProdRevenueLossRateConfigKey = "DemolishedProdRevenueLossRate";
	private const string DemolishEnemyProdRewardConfigKey = "DemolishEnemyProdReward";
	private const float CoinFlySpawnIntervalSeconds = 0.1f;

	private static readonly Vector3 CoinSpawnOffset = new Vector3(0f, 1.2f, 0f);
	private static readonly Vector3 CoinTargetOffset = new Vector3(0f, 1.6f, 0f);

	private static RewardManager s_CachedManager;

	private bool m_LogicEventsSubscribed;
	private bool m_RuntimeDependenciesPrepared;
	private Fix64 m_BaseResourceIncomeDailyGrowth;
	private int m_DefendPhaseBaseResourceIncome;
	private int m_InvadePhaseIncomePerCapturedOutpost;
	private int m_KillRewardSupplyRatio;
	private int m_DemolishedProdRevenueLossRate;
	private int m_DemolishEnemyProdReward;
	private readonly Queue<CoinFlyPresentationRequest> m_PendingCoinFlyPresentation = new();

	protected override void Awake()
	{
		base.Awake();
		s_CachedManager = this;
	}

	private void Start()
	{
		TrySubscribeEvents();
	}

	private void OnEnable()
	{
		TrySubscribeEvents();
	}

	public void UpdatePresentation()
	{
		if (LogicFrameRuntime.IsExecutingFrame)
			throw new InvalidOperationException("Reward presentation cannot run during a logic frame.");
		if (m_PendingCoinFlyPresentation.Count == 0 || GF.UI == null)
			return;
		if (!TryGetPlayerPresentationPosition(out Vector3 playerPosition))
			return;

		while (m_PendingCoinFlyPresentation.Count > 0)
		{
			CoinFlyPresentationRequest request = m_PendingCoinFlyPresentation.Dequeue();
			Vector3 spawnPosition = new Vector3(
				(float)request.SourcePosition.x,
				0f,
				(float)request.SourcePosition.y) + CoinSpawnOffset;
			Vector3 targetPosition = playerPosition + CoinTargetOffset;
			GF.UI.ShowCoinFlyEffectToDynamicTarget(
				spawnPosition,
				() => TryGetPlayerPresentationPosition(out Vector3 dynamicPlayerPosition)
					? dynamicPlayerPosition + CoinTargetOffset
					: targetPosition,
				0f,
				null,
				request.CoinAmount,
				null,
				CoinFlySpawnIntervalSeconds);
		}
	}

	private void OnDisable()
	{
		TryUnsubscribeEvents();
	}

	private void OnDestroy()
	{
		TryUnsubscribeEvents();
		m_PendingCoinFlyPresentation.Clear();
		if (s_CachedManager == this)
			s_CachedManager = null;
	}

	public void ResetLevelCounters()
	{
		LogicRewardStateService.Reset();
		m_PendingCoinFlyPresentation.Clear();
	}

	public void PrepareRuntimeDependencies()
	{
		if (m_RuntimeDependenciesPrepared)
			return;
		if (GF.Config == null)
			throw new InvalidOperationException("RewardManager requires initialized game config.");

		m_BaseResourceIncomeDailyGrowth = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(BaseResourceIncomeDailyGrowthConfigKey);
		m_DefendPhaseBaseResourceIncome = GF.Config.GetInt(DefendPhaseBaseResourceIncomeConfigKey, 0);
		m_InvadePhaseIncomePerCapturedOutpost = GF.Config.GetInt(InvadePhaseIncomePerCapturedOutpostConfigKey, 0);
		m_KillRewardSupplyRatio = GF.Config.GetInt(KillRewardSupplyRatioConfigKey, 0);
		m_DemolishedProdRevenueLossRate = GF.Config.GetInt(DemolishedProdRevenueLossRateConfigKey, -1);
		m_DemolishEnemyProdReward = GF.Config.GetInt(DemolishEnemyProdRewardConfigKey, -1);
		if (m_DefendPhaseBaseResourceIncome < 0
			|| m_InvadePhaseIncomePerCapturedOutpost < 0
			|| m_KillRewardSupplyRatio <= 0
			|| m_DemolishedProdRevenueLossRate < 0
			|| m_DemolishedProdRevenueLossRate > 100
			|| m_DemolishEnemyProdReward < 0)
		{
			throw new InvalidOperationException(
				$"RewardManager runtime config is invalid. defend={m_DefendPhaseBaseResourceIncome}, " +
				$"invade={m_InvadePhaseIncomePerCapturedOutpost}, killRatio={m_KillRewardSupplyRatio}, " +
				$"prodLoss={m_DemolishedProdRevenueLossRate}, enemyProdReward={m_DemolishEnemyProdReward}.");
		}
		m_RuntimeDependenciesPrepared = true;
		TrySubscribeEvents();
	}

	public static void HandleCardDiscardReward(CardModel cardModel, int gainedCoin)
	{
		if (!LogicCardCommandService.IsApplyingFrame)
			throw new InvalidOperationException("Card discard rewards may only be applied by LogicCardCommandService.");

		RewardManager manager = GetRuntimeManager()
			?? throw new InvalidOperationException("Card discard reward requires RewardManager.");
		manager.GrantDiscardCardReward(cardModel, gainedCoin);
	}

	public static void HandleEnterBuildPhaseReward(bool isFirstPhase, GamePhase previousPhase)
	{
		if (isFirstPhase)
		{
			return;
		}

		if (!LogicPhaseCommandService.IsApplyingFrame)
			throw new InvalidOperationException("Build phase rewards may only be applied by LogicPhaseCommandService.");

		RewardManager manager = GetRuntimeManager()
			?? throw new InvalidOperationException("Build phase reward requires RewardManager.");
		manager.GrantBattlePhaseIncomeOnEnterBuild(previousPhase);
		manager.GrantPeriodicCareerOrange(previousPhase);
		manager.GrantBuildPhaseIncomeFromPlayerProdBuildings();
	}

	public static void HandleBuildingUndoReward(FixVector2 sourceWorldPosition, int gainedCoin)
	{
		if (gainedCoin <= 0)
			return;

		if (!LogicInteractionCommandService.IsApplyingFrame)
			throw new InvalidOperationException("Building undo rewards may only be applied by LogicInteractionCommandService.");

		RewardManager manager = GetRuntimeManager()
			?? throw new InvalidOperationException("Building undo reward requires RewardManager.");
		manager.GrantCoin(sourceWorldPosition, gainedCoin, "building_undo");
	}

	private void TrySubscribeEvents()
	{
		if (m_LogicEventsSubscribed || !m_RuntimeDependenciesPrepared)
			return;

		LogicUnitDeathEventService.UnitDied += OnLogicUnitDied;
		LogicBuildingOwnershipEventService.OwnerFactionChanged += OnLogicBuildingOwnerFactionChanged;
		LogicBuildingDisabledEventService.BuildingDisabled += OnLogicBuildingDisabled;
		LogicPhaseCommandService.PhaseApplied += OnPhaseChanged;
		m_LogicEventsSubscribed = true;
	}

	private void TryUnsubscribeEvents()
	{
		if (!m_LogicEventsSubscribed)
			return;

		LogicUnitDeathEventService.UnitDied -= OnLogicUnitDied;
		LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnLogicBuildingOwnerFactionChanged;
		LogicBuildingDisabledEventService.BuildingDisabled -= OnLogicBuildingDisabled;
		LogicPhaseCommandService.PhaseApplied -= OnPhaseChanged;

		m_LogicEventsSubscribed = false;
	}

	private void OnLogicBuildingOwnerFactionChanged(
		IBuildingLogicContext building,
		int oldFactionId,
		int newFactionId)
	{
		if ((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Invade)
			return;

		if (oldFactionId == EntitySideHelper.PlayerFactionId || newFactionId != EntitySideHelper.PlayerFactionId)
			return;

		if (building == null || string.IsNullOrWhiteSpace(building.StrongholdId))
			throw new InvalidOperationException("Captured building ownership event has no stronghold id.");

		LogicRewardStateService.RecordCapturedStronghold(building.StrongholdId);
	}

	private void OnLogicBuildingDisabled(IBuildingLogicContext building, IEntityContext attacker)
	{
		if (building?.BuildingData?.Type == BuilType.Prod
			&& building.OwnerFactionId == EntitySideHelper.PlayerFactionId
			&& LogicBuildingProductionService.IsPackageRack(building))
		{
			int storedProduction = building.ProductionProps?.StoredProduction
				?? throw new InvalidOperationException(
					$"Disabled package rack has no production props. instance={building.BuildingInstanceId}.");
			int actualProduction = LogicBuildingProductionService.GrantProduction(building, storedProduction);
			if (actualProduction > 0)
				GrantCoin(building.PositionFixed, actualProduction, "package_rack_destroyed");
			return;
		}

		switch (ResolveProductionBuildingDisabledResult(building))
		{
			case ProductionBuildingDisabledResult.Ignore:
				return;
			case ProductionBuildingDisabledResult.RecordPlayerProductionLoss:
				InGameDataModel.RecordDemolishedPlayerProductionBuilding(building.BuildingInstanceId);
				return;
			case ProductionBuildingDisabledResult.GrantEnemyProductionReward:
				RequireRuntimeDependencies();
				GrantCoin(building.PositionFixed, m_DemolishEnemyProdReward, "enemy_production_demolished");
				return;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	internal static ProductionBuildingDisabledResult ResolveProductionBuildingDisabledResult(
		IBuildingLogicContext building)
	{
		if (building == null)
			throw new ArgumentNullException(nameof(building));
		if (building.BuildingData == null)
			throw new InvalidOperationException(
				$"Disabled building has no building data. instance={building.BuildingInstanceId}.");
		if (building.BuildingData.Type != BuilType.Prod)
			return ProductionBuildingDisabledResult.Ignore;
		if (building.OwnerFactionId == EntitySideHelper.PlayerFactionId)
			return ProductionBuildingDisabledResult.RecordPlayerProductionLoss;
		if (building.OwnerFactionId == EntitySideHelper.EnemyFactionId)
			return ProductionBuildingDisabledResult.GrantEnemyProductionReward;
		throw new InvalidOperationException(
			$"Production building has invalid owner faction. instance={building.BuildingInstanceId}, faction={building.OwnerFactionId}.");
	}

	private void OnPhaseChanged(GamePhase oldPhase, GamePhase newPhase)
	{
		if (newPhase == GamePhase.Invade)
		{
			LogicRewardStateService.ClearCapturedStrongholds();
		}
	}

	private void GrantBattlePhaseIncomeOnEnterBuild(GamePhase previousPhase)
	{
		if (previousPhase != GamePhase.Defend && previousPhase != GamePhase.Invade)
			return;

		RequireRuntimeDependencies();
		Fix64 dailyGrowth = m_BaseResourceIncomeDailyGrowth;
		int currentDay = Mathf.Max(0, InGameDataModel.GetValue(IngameValueType.Day));

		long phaseBaseIncome = 0;
		if (previousPhase == GamePhase.Defend)
		{
			phaseBaseIncome = m_DefendPhaseBaseResourceIncome;
		}
		else
		{
			int incomePerCapturedOutpost = m_InvadePhaseIncomePerCapturedOutpost;
			incomePerCapturedOutpost += LevelTagRuntime.GetCapturedOutpostIncomeDelta();
			phaseBaseIncome = (long)incomePerCapturedOutpost * LogicRewardStateService.PlayerCapturedStrongholdCount;
			LogicRewardStateService.ClearCapturedStrongholds();
		}

		Fix64 totalIncome = (Fix64)currentDay * dailyGrowth
			+ (Fix64)LevelTagRuntime.GetDailyBaseIncomeDelta()
			+ (Fix64)phaseBaseIncome
			- (Fix64)LevelTagRuntime.GetCapturedStrongholdDailyCost() * CountPlayerOwnedStrongholds();
		int coinAmount = RoundFixedAwayFromZero(totalIncome);
		if (coinAmount <= 0)
			return;

		GrantCoin(GetRequiredPlayerLogicPosition(), coinAmount, "battle_to_build_income");
	}

	private void GrantPeriodicCareerOrange(GamePhase previousPhase)
	{
		if (previousPhase != GamePhase.Defend)
			return;

		int currentDay = Math.Max(1, InGameDataModel.GetValue(IngameValueType.Day));
		if (CareerRuntimeEffects.ShouldGrantPeriodicOrange(currentDay))
			GrantCoin(GetRequiredPlayerLogicPosition(), 1, "career_periodic_orange");
	}

	private static int RoundFixedAwayFromZero(Fix64 value)
	{
		long raw = value.RawValue;
		long absoluteRaw = raw >= 0 ? raw : checked(-raw);
		long rounded = checked(absoluteRaw + (1L << (Fix64.FRACTIONAL_PLACES - 1)))
			>> Fix64.FRACTIONAL_PLACES;
		return checked((int)(raw >= 0 ? rounded : -rounded));
	}

#if UNITY_EDITOR
	public static int GetEditorTestRoundedIncome(Fix64 value)
	{
		return RoundFixedAwayFromZero(value);
	}
#endif

	private void OnLogicUnitDied(IEntityContext victim)
	{
		if (victim == null)
			throw new InvalidOperationException("RewardManager received a null logic death victim.");
		if (victim.Side != SideType.EnemySide)
			return;
		CharacterDataDetail characterData = victim.CharacterData
			?? throw new InvalidOperationException($"RewardManager logic death victim has no character data. entity={victim.LogicEntityId.Value}.");

		int deadSupply = Math.Max(0, characterData.Supply);
		if (deadSupply <= 0)
			return;

		RequireRuntimeDependencies();
		int ratio = m_KillRewardSupplyRatio;
		ratio = LevelTagRuntime.ModifyKillRewardConversionRate(ratio);
		if (ratio <= 0)
			throw new InvalidOperationException($"RewardManager kill reward ratio became non-positive after logic modifiers. value={ratio}.");
		int gainedCoin = LogicRewardStateService.AccumulateEnemyDeadSupply(deadSupply, ratio);
		if (gainedCoin <= 0)
			return;

		GrantCoin(victim.PositionFixed, gainedCoin, "kill_supply");
	}

	private void GrantDiscardCardReward(CardModel cardModel, int gainedCoin)
	{
		if (cardModel == null)
			throw new ArgumentNullException(nameof(cardModel));

		int resolvedCoin = gainedCoin;
		if (resolvedCoin < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(gainedCoin), gainedCoin, "Discard reward amount must be resolved before commit.");
		}

		if (resolvedCoin <= 0)
			return;

		string sourceBuildingInstanceId = cardModel.GetSourceBuildingInstanceId();
		FixVector2 sourcePosition = string.IsNullOrWhiteSpace(sourceBuildingInstanceId)
			? GetRequiredPlayerLogicPosition()
			: LogicBuildingQueryService.GetRequiredByInstanceId(sourceBuildingInstanceId).PositionFixed;
		GrantCoin(sourcePosition, resolvedCoin, "discard_card");
	}

	private static int CountPlayerOwnedStrongholds()
	{
		return LogicStrongholdMap.CountOwnedStrongholds(EntitySideHelper.PlayerFactionId);
	}

	private void GrantBuildPhaseIncomeFromPlayerProdBuildings()
	{
		int totalProduction = 0;

		IList<IEntityContext> entities = EntityRegistry.AllEntities;
		for (int i = 0; i < entities.Count; i++)
		{
			if (!entities[i].TryGetLogicBuilding(out IBuildingLogicContext building)
				|| !building.Alive
				|| building.IsDisabled
				|| building.OwnerFactionId != EntitySideHelper.PlayerFactionId
				|| building.BuildingData == null
				|| building.BuildingData.Type != BuilType.Prod
				|| building.BuildingData.Lv < 1)
				continue;

			int rawProduction = LogicBuildingProductionService.GetProduction(building);
			if (InGameDataModel.ConsumeDemolishedPlayerProductionBuilding(building.BuildingInstanceId))
			{
				rawProduction = LogicBuildingProductionService.IsResidence(building)
					? 0
					: CalculateProductionAfterDemolitionLoss(rawProduction, m_DemolishedProdRevenueLossRate);
			}
			int actualProduction = LogicBuildingProductionService.GrantProduction(building, rawProduction);
			if (actualProduction <= 0)
				continue;

			totalProduction += actualProduction;
			GrantCoin(building.PositionFixed, actualProduction, "build_phase_income");
		}
	}

	internal static int CalculateProductionAfterDemolitionLoss(int production, int lossPercent)
	{
		if (production < 0)
			throw new ArgumentOutOfRangeException(nameof(production));
		if (lossPercent < 0 || lossPercent > 100)
			throw new ArgumentOutOfRangeException(nameof(lossPercent));
		return checked((int)(((long)production * (100 - lossPercent) + 99L) / 100L));
	}

	private void GrantCoin(FixVector2 sourceWorldPosition, int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		ApplyCoinDirectly(coinAmount, reason);
		m_PendingCoinFlyPresentation.Enqueue(
			new CoinFlyPresentationRequest(sourceWorldPosition, coinAmount));
	}

	private static void ApplyCoinDirectly(int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		int oldValue = InGameDataModel.GetValue(IngameValueType.Coin);
		if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, coinAmount, true))
		{
			Log.Error("[RewardManager] Apply coin failed. deltaCoin={0}, reason={1}", coinAmount, reason);
			return;
		}

		int newValue = InGameDataModel.GetValue(IngameValueType.Coin);
		Log.Info("[RewardManager] Coin committed. old={0}, new={1}, reason={2}.", oldValue, newValue, reason);
	}

	private static FixVector2 GetRequiredPlayerLogicPosition()
	{
		IEntityContext player = EntityRegistry.Player
			?? throw new InvalidOperationException("RewardManager requires a registered player for reward presentation origin.");
		return player.PositionFixed;
	}

	private static bool TryGetPlayerPresentationPosition(out Vector3 position)
	{
		IEntityContext player = EntityRegistry.Player;
		if (player != null
			&& LogicEntityLifecycleService.TryGetBoundView(player.LogicEntityId, out MAEntity playerEntity))
		{
			position = playerEntity.transform.position;
			return true;
		}

		if (player != null)
		{
			position = player.Position;
			return true;
		}

		position = Vector3.zero;
		return false;
	}

	private static RewardManager GetRuntimeManager()
	{
		return s_CachedManager
			?? throw new InvalidOperationException("RewardManager runtime component is not bound during initialization.");
	}

	private void RequireRuntimeDependencies()
	{
		if (!m_RuntimeDependenciesPrepared)
			throw new InvalidOperationException("RewardManager runtime dependencies were not prepared before a logic reward.");
	}
}
