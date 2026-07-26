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
		if (!m_LogicEventsSubscribed)
			TrySubscribeEvents();
	}

	private void OnDisable()
	{
		TryUnsubscribeEvents();
	}

	private void OnDestroy()
	{
		TryUnsubscribeEvents();
		if (s_CachedManager == this)
			s_CachedManager = null;
	}

	public void ResetLevelCounters()
	{
		LogicRewardStateService.Reset();
		m_KillRewardConfigInvalidLogged = false;
	}

	public static void HandleCardDiscardReward(CardModel cardModel)
	{
		RewardManager manager = GetRuntimeManager();
		if (manager == null)
			return;

		manager.GrantDiscardCardReward(cardModel, -1, null);
	}

	public static void HandleCardDiscardReward(CardModel cardModel, int gainedCoin, Vector2? discardScreenPosition)
	{
		RewardManager manager = GetRuntimeManager();
		if (manager == null)
			return;

		manager.GrantDiscardCardReward(cardModel, gainedCoin, discardScreenPosition);
	}

	public static void HandleEnterBuildPhaseReward(bool isFirstPhase, GamePhase previousPhase)
	{
		if (isFirstPhase)
		{
			return;
		}

		RewardManager manager = GetRuntimeManager();
		if (manager == null)
		{
			Debug.LogError("[RewardManager] GetRuntimeManager returned null!");
			return;
		}

		manager.GrantBattlePhaseIncomeOnEnterBuild(previousPhase);
		manager.GrantBuildPhaseIncomeFromPlayerProdBuildings();
	}

	public static void HandleBuildingRecycleReward(Vector3 sourceWorldPos, int gainedCoin)
	{
		if (gainedCoin <= 0)
			return;

		RewardManager manager = GetRuntimeManager();
		if (manager == null)
			return;

		manager.GrantCoinAfterFly(sourceWorldPos, gainedCoin, "building_recycle");
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

		float dailyGrowth = GF.Config != null ? GF.Config.GetFloat(BaseResourceIncomeDailyGrowthConfigKey, 0f) : 0f;
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

		float totalIncome = currentDay * dailyGrowth
			+ LevelTagRuntime.GetDailyBaseIncomeDelta()
			+ phaseBaseIncome
			- LevelTagRuntime.GetCapturedStrongholdDailyCost() * CountPlayerOwnedStrongholds();
		int coinAmount = (int)System.Math.Round(totalIncome, System.MidpointRounding.AwayFromZero);
		if (coinAmount <= 0)
			return;

		Vector3 sourcePosition = TryGetPlayerPosition(out Vector3 playerPos) ? playerPos : Vector3.zero;
		GrantCoinAfterFly(sourcePosition, coinAmount, "battle_to_build_income");
	}

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

		FixVector2 deathPosition = victim.PositionFixed;
		GrantCoinAfterFly(
			new Vector3((float)deathPosition.x, 0f, (float)deathPosition.y),
			gainedCoin,
			"kill_supply");
	}

	private void GrantDiscardCardReward(CardModel cardModel, int gainedCoin, Vector2? discardScreenPosition)
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

		Vector3 sourcePos = ResolveDiscardRewardSourcePosition(cardModel, discardScreenPosition);
		GrantCoinAfterFly(sourcePos, resolvedCoin, "discard_card");
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
			Vector3 sourcePosition = new Vector3(
				(float)building.PositionFixed.x,
				0f,
				(float)building.PositionFixed.y);
			GrantCoinAfterFly(sourcePosition, actualProduction, "build_phase_income");
		}
	}

	private void GrantCoinAfterFly(Vector3 sourceWorldPos, int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		// Economy is committed in the phase transaction; the fly effect is presentation only.
		ApplyCoinDirectly(coinAmount, reason);
		if (GF.UI == null || !TryGetPlayerPosition(out Vector3 playerPos))
			return;

		Vector3 spawnPos = sourceWorldPos + CoinSpawnOffset;
		Vector3 targetPos = playerPos + CoinTargetOffset;

		GF.UI.ShowCoinFlyEffectToDynamicTarget(
			spawnPos,
			() => TryGetPlayerPosition(out Vector3 dynamicPlayerPos) ? dynamicPlayerPos + CoinTargetOffset : targetPos,
			0f,
			null,
			coinAmount,
			null,
			CoinFlySpawnIntervalSeconds);
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

	private static Vector3 ResolveDiscardRewardSourcePosition(CardModel cardModel, Vector2? discardScreenPosition)
	{
		if (discardScreenPosition.HasValue && TryResolveWorldPositionFromScreen(discardScreenPosition.Value, out Vector3 screenWorldPos))
			return screenWorldPos;

		if (cardModel.SourceBuilding != null)
			return cardModel.SourceBuilding.transform.position;

		if (TryGetPlayerPosition(out Vector3 playerPos))
			return playerPos;

		return Vector3.zero;
	}

	private static bool TryResolveWorldPositionFromScreen(Vector2 screenPosition, out Vector3 worldPosition)
	{
		worldPosition = Vector3.zero;
		Camera cam = Camera.main;
		if (cam == null)
			return false;

		Ray ray = cam.ScreenPointToRay(screenPosition);
		if (Physics.Raycast(ray, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
		{
			worldPosition = hit.point;
			return true;
		}

		Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
		if (groundPlane.Raycast(ray, out float distance))
		{
			worldPosition = ray.GetPoint(distance);
			return true;
		}

		return false;
	}

	private static bool TryGetPlayerPosition(out Vector3 position)
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
