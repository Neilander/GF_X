using AAAGame.Card;
using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

public class RewardManager : GameFrameworkComponent
{
	private const string DiscardResourceConversionRateConfigKey = "DiscardResourceConversionRate";
	private const string KillRewardSupplyRatioConfigKey = "KillRewardSupplyRatio";
	private const int MaxFlyCoinVisualCount = 30;

	private static readonly Vector3 CoinSpawnOffset = new Vector3(0f, 1.2f, 0f);
	private static readonly Vector3 CoinTargetOffset = new Vector3(0f, 1.6f, 0f);

	private static RewardManager s_CachedManager;

	private bool m_DeathEventSubscribed;
	private bool m_WaitingEventReadyLogged;
	private int m_EnemyDeadSupplyRemainder;
	private bool m_KillRewardConfigInvalidLogged;

	private void Awake()
	{
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
	}

	public static void HandleCardDiscardReward(CardModel cardModel)
	{
		RewardManager manager = GetRuntimeManager();
		if (manager == null)
			return;

		manager.GrantDiscardCardReward(cardModel);
	}

	public static void HandleEnterBuildPhaseReward(bool isFirstPhase)
	{
		if (isFirstPhase)
			return;

		RewardManager manager = GetRuntimeManager();
		if (manager == null)
			return;

		manager.GrantBuildPhaseIncomeFromPlayerProdBuildings();
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
			}
			catch (GameFrameworkException)
			{
				// PlayMode 退出时 EventPool 可能已释放，忽略退订异常。
			}
		}

		m_DeathEventSubscribed = false;
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
		m_EnemyDeadSupplyRemainder += deadSupply;

		int gainedCoin = m_EnemyDeadSupplyRemainder / ratio;
		m_EnemyDeadSupplyRemainder %= ratio;
		if (gainedCoin <= 0)
			return;

		GrantCoinAfterFly(args.WorldPosition, gainedCoin, "kill_supply");
	}

	private void GrantDiscardCardReward(CardModel cardModel)
	{
		if (cardModel == null)
			return;

		int occupiedSupply = Mathf.Max(0, cardModel.GetOccupiedSupply());
		int conversionRate = GF.Config != null ? GF.Config.GetInt(DiscardResourceConversionRateConfigKey, 0) : 0;
		if (conversionRate <= 0)
		{
			Log.Error("[RewardManager] Discard reward config invalid. key={0}, value={1}", DiscardResourceConversionRateConfigKey, conversionRate);
			return;
		}

		int gainedCoin = occupiedSupply / conversionRate;
		if (gainedCoin <= 0)
			return;

		Vector3 sourcePos = ResolveDiscardRewardSourcePosition(cardModel);
		GrantCoinAfterFly(sourcePos, gainedCoin, "discard_card");
	}

	private void GrantBuildPhaseIncomeFromPlayerProdBuildings()
	{
		InGameDataModel inGameData = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
		if (inGameData == null)
		{
			Log.Error("[RewardManager] Build phase income skipped: InGameDataModel not ready.");
			return;
		}

		foreach (var building in inGameData.Buildings)
		{
			if (building == null || building.buildingData == null)
				continue;

			if (building.OwnerFactionID != EntitySideHelper.PlayerFactionId)
				continue;

			if (building.buildingData.Type != BuilType.Prod)
				continue;

			int production = building.GetProduction();
			if (production <= 0)
				continue;

			GrantCoinAfterFly(building.transform.position, production, "build_phase_income");
		}
	}

	private void GrantCoinAfterFly(Vector3 sourceWorldPos, int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		if (GF.UI == null || !TryGetPlayerPosition(out Vector3 playerPos))
		{
			ApplyCoinDirectly(coinAmount, reason);
			return;
		}

		Vector3 spawnPos = sourceWorldPos + CoinSpawnOffset;
		Vector3 targetPos = playerPos + CoinTargetOffset;
		int visualCoinCount = Mathf.Clamp(coinAmount, 1, MaxFlyCoinVisualCount);

		GF.UI.ShowRewardEffect(
			spawnPos,
			targetPos,
			0f,
			() => ApplyCoinDirectly(coinAmount, reason),
			visualCoinCount);
	}

	private static void ApplyCoinDirectly(int coinAmount, string reason)
	{
		if (coinAmount <= 0)
			return;

		if (!InGameDataModel.TryModifyValue(IngameValueType.Coin, coinAmount, true))
		{
			Log.Error("[RewardManager] Apply coin failed. deltaCoin={0}, reason={1}", coinAmount, reason);
		}
	}

	private static Vector3 ResolveDiscardRewardSourcePosition(CardModel cardModel)
	{
		if (cardModel.SourceBuilding != null)
			return cardModel.SourceBuilding.transform.position;

		if (TryGetPlayerPosition(out Vector3 playerPos))
			return playerPos;

		return Vector3.zero;
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
			return s_CachedManager;

		s_CachedManager = GameEntry.GetComponent<RewardManager>();
		if (s_CachedManager == null)
		{
			GeneralSetup generalSetup = GameEntry.GetComponent<GeneralSetup>();
			if (generalSetup != null)
				s_CachedManager = generalSetup.gameObject.AddComponent<RewardManager>();
		}

		if (s_CachedManager == null)
			Log.Error("[RewardManager] Runtime component not found on GameEntry.");

		return s_CachedManager;
	}
}