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

	private const string DiscardResourceConversionRateConfigKey = "DiscardResourceConversionRate";
	private const string KillRewardSupplyRatioConfigKey = "KillRewardSupplyRatio";
	private const string BaseResourceIncomeDailyGrowthConfigKey = "BaseResourceIncomeDailyGrowth";
	private const string DefendPhaseBaseResourceIncomeConfigKey = "DefendPhaseBaseResourceIncome";
	private const string InvadePhaseIncomePerCapturedOutpostConfigKey = "InvadePhaseIncomePerCapturedOutpost";
	private const float CoinFlySpawnIntervalSeconds = 0.1f;

	private static readonly Vector3 CoinSpawnOffset = new Vector3(0f, 1.2f, 0f);
	private static readonly Vector3 CoinTargetOffset = new Vector3(0f, 1.6f, 0f);

	private static RewardManager s_CachedManager;

	private bool m_LogicEventsSubscribed;
	private bool m_KillRewardConfigInvalidLogged;
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

	private void Update()
	{
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
		m_KillRewardConfigInvalidLogged = false;
		m_PendingCoinFlyPresentation.Clear();
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
		manager.GrantBuildPhaseIncomeFromPlayerProdBuildings();
	}

	public static void HandleBuildingRecycleReward(FixVector2 sourceWorldPosition, int gainedCoin)
	{
		if (gainedCoin <= 0)
			return;

		if (!LogicInteractionCommandService.IsApplyingFrame)
			throw new InvalidOperationException("Building recycle rewards may only be applied by LogicInteractionCommandService.");

		RewardManager manager = GetRuntimeManager()
			?? throw new InvalidOperationException("Building recycle reward requires RewardManager.");
		manager.GrantCoin(sourceWorldPosition, gainedCoin, "building_recycle");
	}

	private void TrySubscribeEvents()
	{
		if (m_LogicEventsSubscribed)
			return;

		LogicUnitDeathEventService.UnitDied += OnLogicUnitDied;
		LogicBuildingOwnershipEventService.OwnerFactionChanged += OnLogicBuildingOwnerFactionChanged;
		PhaseManager.OnPhaseChanged += OnPhaseChanged;
		m_LogicEventsSubscribed = true;
	}

	private void TryUnsubscribeEvents()
	{
		if (!m_LogicEventsSubscribed)
			return;

		LogicUnitDeathEventService.UnitDied -= OnLogicUnitDied;
		LogicBuildingOwnershipEventService.OwnerFactionChanged -= OnLogicBuildingOwnerFactionChanged;
		PhaseManager.OnPhaseChanged -= OnPhaseChanged;

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

		Fix64 dailyGrowth = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(BaseResourceIncomeDailyGrowthConfigKey);
		int currentDay = Mathf.Max(0, InGameDataModel.GetValue(IngameValueType.Day));

		long phaseBaseIncome = 0;
		if (previousPhase == GamePhase.Defend)
		{
			phaseBaseIncome = GF.Config != null ? GF.Config.GetInt(DefendPhaseBaseResourceIncomeConfigKey, 0) : 0;
		}
		else
		{
			int incomePerCapturedOutpost = GF.Config != null ? GF.Config.GetInt(InvadePhaseIncomePerCapturedOutpostConfigKey, 0) : 0;
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

		int ratio = GF.Config != null ? GF.Config.GetInt(KillRewardSupplyRatioConfigKey, 0) : 0;
		if (ratio <= 0)
		{
			if (!m_KillRewardConfigInvalidLogged)
			{
				m_KillRewardConfigInvalidLogged = true;
				Log.Error("[RewardManager] Kill reward config invalid. key={0}, value={1}", KillRewardSupplyRatioConfigKey, ratio);
			}

			return;
		}

		m_KillRewardConfigInvalidLogged = false;
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
			return;

		int resolvedCoin = gainedCoin;
		if (resolvedCoin < 0)
		{
			int occupiedSupply = Mathf.Max(0, cardModel.GetOccupiedSupply());
			int conversionRate = GF.Config != null ? GF.Config.GetInt(DiscardResourceConversionRateConfigKey, 0) : 0;
			if (conversionRate <= 0)
			{
				Log.Error("[RewardManager] Discard reward config invalid. key={0}, value={1}", DiscardResourceConversionRateConfigKey, conversionRate);
				return;
			}

			conversionRate = DiscardRewardModifierService.CalculateConversionRate(conversionRate);
			resolvedCoin = occupiedSupply / conversionRate;
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
		InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
		if (inGameData == null)
		{
			Log.Error("[RewardManager] Build phase income skipped: InGameDataModel not ready.");
			return;
		}
		
		int totalProduction = 0;

		IList<IEntityContext> entities = EntityRegistry.AllEntities;
		for (int i = 0; i < entities.Count; i++)
		{
			if (!(entities[i] is IBuildingLogicContext building)
				|| !building.Alive
				|| building.IsDisabled
				|| building.OwnerFactionId != EntitySideHelper.PlayerFactionId
				|| building.BuildingData == null
				|| building.BuildingData.Type != BuilType.Prod
				|| building.BuildingData.Lv < 1)
				continue;

			int actualProduction = LogicBuildingProductionService.GrantProduction(building);
			if (actualProduction <= 0)
				continue;

			totalProduction += actualProduction;
			GrantCoin(building.PositionFixed, actualProduction, "build_phase_income");
		}
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
		if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, coinAmount, false))
		{
			Log.Error("[RewardManager] Apply coin failed. deltaCoin={0}, reason={1}", coinAmount, reason);
			return;
		}

		int newValue = InGameDataModel.GetValue(IngameValueType.Coin);
		if (GF.Event != null && oldValue != newValue)
			GF.Event.Fire(null, IngameValueChangedEventArgs.Create(IngameValueType.Coin, oldValue, newValue));
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
		if (s_CachedManager != null)
			return s_CachedManager;

		s_CachedManager = GameEntry.GetComponent<RewardManager>();
		if (s_CachedManager == null)
		{
			GeneralSetup generalSetup = GameEntry.GetComponent<GeneralSetup>();
			if (generalSetup != null)
				s_CachedManager = generalSetup.gameObject.AddComponent<RewardManager>();
		}

		if (s_CachedManager == null)
		{
			Debug.LogError("[RewardManager] GetRuntimeManager: Runtime component not found on GameEntry.");
		}

		return s_CachedManager;
	}
}
