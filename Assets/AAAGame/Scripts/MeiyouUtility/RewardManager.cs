using AAAGame.Card;
using GameFramework;
using GameFramework.Event;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

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

	private bool m_DeathEventSubscribed;
	private bool m_WaitingEventReadyLogged;
	private int m_EnemyDeadSupplyRemainder;
	private bool m_KillRewardConfigInvalidLogged;
	private readonly HashSet<string> m_PlayerCapturedStrongholdIdsInCurrentInvade = new HashSet<string>();

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
		if (!m_DeathEventSubscribed)
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
		m_EnemyDeadSupplyRemainder = 0;
		m_KillRewardConfigInvalidLogged = false;
		m_PlayerCapturedStrongholdIdsInCurrentInvade.Clear();
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
		Debug.Log($"[RewardManager] HandleEnterBuildPhaseReward called. isFirstPhase={isFirstPhase}");
		
		if (isFirstPhase)
		{
			Debug.Log("[RewardManager] Skipping first phase reward.");
			return;
		}

		RewardManager manager = GetRuntimeManager();
		if (manager == null)
		{
			Debug.LogError("[RewardManager] GetRuntimeManager returned null!");
			return;
		}

		Debug.Log("[RewardManager] Granting build phase income...");
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
		if (m_DeathEventSubscribed)
			return;

		if (GF.Event == null)
		{
			if (!m_WaitingEventReadyLogged)
			{
				m_WaitingEventReadyLogged = true;
				Log.Info("[RewardManager] Waiting for GF.Event to become ready...");
			}

			return;
		}

		GF.Event.Subscribe(SoldierDeadEventArgs.EventId, OnSoldierDead);
		GF.Event.Subscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
		GF.Event.Subscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
		m_DeathEventSubscribed = true;
		m_WaitingEventReadyLogged = false;
	}

	private void TryUnsubscribeEvents()
	{
		if (!m_DeathEventSubscribed)
			return;

		if (GF.Event != null)
		{
			try
			{
				GF.Event.Unsubscribe(SoldierDeadEventArgs.EventId, OnSoldierDead);
				GF.Event.Unsubscribe(EntityFactionChangedEventArgs.EventId, OnEntityFactionChanged);
				GF.Event.Unsubscribe(IngamePhaseChangedEventArgs.EventId, OnIngamePhaseChanged);
			}
			catch (GameFrameworkException)
			{
				// PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
			}
		}

		m_DeathEventSubscribed = false;
	}

	private void OnEntityFactionChanged(object sender, GameEventArgs e)
	{
		if (e is not EntityFactionChangedEventArgs args)
			return;

		if ((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Invade)
			return;

		if (args.OldFactionId == EntitySideHelper.PlayerFactionId || args.NewFactionId != EntitySideHelper.PlayerFactionId)
			return;

		if (!TryResolveStrongholdIdFromFactionChanged(args, out string strongholdId))
			return;

		m_PlayerCapturedStrongholdIdsInCurrentInvade.Add(strongholdId);
	}

	private void OnIngamePhaseChanged(object sender, GameEventArgs e)
	{
		if (e is not IngamePhaseChangedEventArgs args)
			return;

		if (args.NewPhase == GamePhase.Invade)
		{
			m_PlayerCapturedStrongholdIdsInCurrentInvade.Clear();
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
			phaseBaseIncome = (long)incomePerCapturedOutpost * m_PlayerCapturedStrongholdIdsInCurrentInvade.Count;
			m_PlayerCapturedStrongholdIdsInCurrentInvade.Clear();
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

	private static bool TryResolveStrongholdIdFromFactionChanged(EntityFactionChangedEventArgs args, out string strongholdId)
	{
		strongholdId = null;

		InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
		if (inGameData == null)
			return false;

		foreach (var building in inGameData.Buildings)
		{
			if (building == null)
				continue;

			bool entityMatched = args.EntityId > 0 && building.Id == args.EntityId;
			bool instanceMatched = !string.IsNullOrWhiteSpace(args.BuildingInstanceId)
				&& args.BuildingInstanceId == building.BuildingInstanceId;
			if (!entityMatched && !instanceMatched)
				continue;

			strongholdId = building.CurrentStronghold?.strongholdData?.StrongholdId;
			return !string.IsNullOrWhiteSpace(strongholdId);
		}

		return false;
	}

	private void OnSoldierDead(object sender, GameEventArgs e)
	{
		if (e is not SoldierDeadEventArgs args)
			return;

		if (args.VictimSide != SideType.EnemySide)
			return;

		int deadSupply = Mathf.Max(0, args.VictimSupply);
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
		m_EnemyDeadSupplyRemainder += deadSupply;

		int gainedCoin = m_EnemyDeadSupplyRemainder / ratio;
		m_EnemyDeadSupplyRemainder %= ratio;
		if (gainedCoin <= 0)
			return;

		GrantCoinAfterFly(args.WorldPosition, gainedCoin, "kill_supply");
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
		InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
		if (inGameData?.Strongholds == null)
			return 0;

		int count = 0;
		foreach (Stronghold stronghold in inGameData.Strongholds)
		{
			if (stronghold != null && stronghold.OwnerFactionId == EntitySideHelper.PlayerFactionId)
				count++;
		}

		return count;
	}

	private void GrantBuildPhaseIncomeFromPlayerProdBuildings()
	{
		Debug.Log("[RewardManager] GrantBuildPhaseIncomeFromPlayerProdBuildings called.");
		
		InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
		if (inGameData == null)
		{
			Log.Error("[RewardManager] Build phase income skipped: InGameDataModel not ready.");
			return;
		}

		Debug.Log($"[RewardManager] Checking {inGameData.Buildings?.Count ?? 0} buildings for production income...");
		
		int playerProdBuildings = 0;
		int totalProduction = 0;
		
		foreach (var building in inGameData.Buildings)
		{
			if (building == null || building.buildingData == null)
			{
				Debug.Log($"[RewardManager] Building skipped: null building or buildingData");
				continue;
			}

			Debug.Log($"[RewardManager] Checking building: {building.buildingData.Identifier}, Type={building.buildingData.Type}, Owner={building.OwnerFactionID}");

			if (building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
			{
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} skipped: not player-owned (Owner={building.OwnerFactionID}, Player={EntitySideHelper.PlayerFactionId})");
				continue;
			}

			Debug.Log($"[RewardManager] Before type check: {building.buildingData.Identifier}, Type={building.buildingData.Type}, BuilType.Prod={(int)BuilType.Prod}");
			
			if (building.buildingData.Type != BuilType.Prod)
			{
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} skipped: not production type (Type={building.buildingData.Type}, Expected={BuilType.Prod})");
				continue;
			}

			// 检查建造等级：只有Lv1及以上的建筑才能生产资源
			if (building.buildingData.Lv < 1)
			{
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} skipped: not completed construction (Lv={building.buildingData.Lv}, need Lv>=1)");
				continue;
			}

			Debug.Log($"[RewardManager] PASSED TYPE CHECK - {building.buildingData.Identifier} is production type! About to increment playerProdBuildings...");
			playerProdBuildings++;
			Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} - playerProdBuildings incremented to: {playerProdBuildings}");
			Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} passed all checks, attempting to get production...");
			
			int production = 0;
			try
			{
				production = building.GetProduction();
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} production={production}");
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[RewardManager] Error getting production for {building.buildingData.Identifier}: {ex.Message}");
				continue;
			}
			
			if (production <= 0)
			{
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} skipped: production <= 0");
				continue;
			}

			int actualProduction = ResolveProductionByCoinReserves(building, production);
			if (actualProduction <= 0)
			{
				Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} skipped: no coin reserves left");
				continue;
			}

			totalProduction += actualProduction;
			Debug.Log($"[RewardManager] Building {building.buildingData.Identifier} will produce {actualProduction} coins (raw={production})");
			building.NotifyProductionGranted(production, actualProduction);
			GrantCoinAfterFly(building.transform.position, actualProduction, "build_phase_income");
		}
		
		Debug.Log($"[RewardManager] Production summary: {playerProdBuildings} player production buildings, total production={totalProduction}");
	}

	private static int ResolveProductionByCoinReserves(BuildingEntity building, int rawProduction)
	{
		if (building == null || rawProduction <= 0)
			return 0;

		int consumed = InGameDataModel.ConsumeProductionBuildingCoinReserves(building.BuildingInstanceId, rawProduction);
		return Mathf.Max(0, consumed);
	}

	private void GrantCoinAfterFly(Vector3 sourceWorldPos, int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		Debug.Log($"[RewardManager] GrantCoinAfterFly: {coinAmount} coins from {reason}");

		if (GF.UI == null || !TryGetPlayerPosition(out Vector3 playerPos))
		{
			Debug.Log("[RewardManager] UI system not ready, applying coin directly.");
			ApplyCoinDirectly(coinAmount, reason);
			return;
		}

		Debug.Log("[RewardManager] Showing coin fly effect...");
		Vector3 spawnPos = sourceWorldPos + CoinSpawnOffset;
		Vector3 targetPos = playerPos + CoinTargetOffset;

		GF.UI.ShowCoinFlyEffectToDynamicTarget(
			spawnPos,
			() => TryGetPlayerPosition(out Vector3 dynamicPlayerPos) ? dynamicPlayerPos + CoinTargetOffset : targetPos,
			0f,
			null,
			coinAmount,
			() => ApplyCoinDirectly(1, reason),
			CoinFlySpawnIntervalSeconds);
	}

	private static void ApplyCoinDirectly(int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		Debug.Log($"[RewardManager] ApplyCoinDirectly: Attempting to add {coinAmount} coins for {reason}");

		if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, coinAmount, true))
		{
			Log.Error("[RewardManager] Apply coin failed. deltaCoin={0}, reason={1}", coinAmount, reason);
		}
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
		if (EntityRegistry.Player is MAEntity playerEntity)
		{
			position = playerEntity.transform.position;
			return true;
		}

		if (EntityRegistry.Player != null)
		{
			position = EntityRegistry.Player.Position;
			return true;
		}

		position = Vector3.zero;
		return false;
	}

	private static RewardManager GetRuntimeManager()
	{
		if (s_CachedManager != null)
		{
			Debug.Log($"[RewardManager] GetRuntimeManager: Returning cached instance.");
			return s_CachedManager;
		}

		Debug.Log("[RewardManager] GetRuntimeManager: Attempting to find existing component...");
		s_CachedManager = GameEntry.GetComponent<RewardManager>();
		if (s_CachedManager == null)
		{
			Debug.Log("[RewardManager] GetRuntimeManager: No existing component found, trying to add dynamically...");
			GeneralSetup generalSetup = GameEntry.GetComponent<GeneralSetup>();
			if (generalSetup != null)
			{
				s_CachedManager = generalSetup.gameObject.AddComponent<RewardManager>();
				Debug.Log($"[RewardManager] GetRuntimeManager: Dynamically added component. Success={s_CachedManager != null}");
			}
		}

		if (s_CachedManager == null)
		{
			Debug.LogError("[RewardManager] GetRuntimeManager: Runtime component not found on GameEntry.");
		}
		else
		{
			Debug.Log("[RewardManager] GetRuntimeManager: Component found/added successfully.");
		}

		return s_CachedManager;
	}
}
