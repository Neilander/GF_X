using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Card;
using AAAGame.MiniMap.FOG3;

public partial class CardSetup : GameFrameworkComponent, ILogicCardRuntimeStateContributor
{
    private const long CardPhasePerfWarnMs = 30;
    private CardSystemController m_CardSystemController;
    private int m_CardUIFormId = -1;
    private readonly LogicCardAutoDrawClock m_AutoDrawClock = new LogicCardAutoDrawClock();
    private List<ICardDataProvider> m_PreloadedCardPool;

    private void OnEnable()
    {
        LevelSelectionService.LevelLoadStarted += OnLevelLoadStarted;
    }

    private void OnDisable()
    {
        LevelSelectionService.LevelLoadStarted -= OnLevelLoadStarted;
    }

    private void OnLevelLoadStarted()
    {
        CardSystemShutdown(false);
        PreloadCardPool();
    }

    public void CardSystemSetup()
    {
        long totalStartTicks = Stopwatch.GetTimestamp();
        long totalStartBytes = System.GC.GetAllocatedBytesForCurrentThread();

        long stageStartTicks = Stopwatch.GetTimestamp();
        long stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        CardSystemShutdown(false);
        RecordPerf(MainThreadPerfScope.CardSetupShutdown, stageStartTicks, stageStartBytes);

        m_AutoDrawClock.Reset();
        InitializeCardSystem();

        stageStartTicks = Stopwatch.GetTimestamp();
        stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        LogicCardCommandService.CommandApplying += m_CardSystemController.ApplyLogicCardCommand;
        LogicCardRuntimeState.Bind(this);
        RecordPerf(MainThreadPerfScope.CardSetupBindRuntime, stageStartTicks, stageStartBytes);

        long elapsedTicks = Stopwatch.GetTimestamp() - totalStartTicks;
        MainThreadFrameProfiler.Record(
            MainThreadPerfScope.CardSetupTotal,
            elapsedTicks,
            System.GC.GetAllocatedBytesForCurrentThread() - totalStartBytes);
        LogPhasePerf("card-setup.total", TicksToMilliseconds(elapsedTicks));
    }

    public void CardSystemUpdate()
    {
        if (m_CardSystemController != null)
            m_CardSystemController.UpdatePlacement();
    }

    public void ApplyLogicFrame(ulong frameId)
    {
        if (frameId == 0 || frameId != LogicTimeControlService.CurrentFrame)
            throw new System.InvalidOperationException(
                $"CardSetup.ApplyLogicFrame frame mismatch. requested={frameId}, current={LogicTimeControlService.CurrentFrame}.");
        if (m_CardSystemController == null || !m_AutoDrawClock.IsDue(frameId))
            return;

        if (m_CardSystemController.TryAutoDrawOneCardFromDeck())
            m_AutoDrawClock.RecordDraw(frameId);
    }

    public void CardSystemShutdown(bool playCloseAnimation = true)
    {
        var totalWatch = Stopwatch.StartNew();

        var closeUiWatch = Stopwatch.StartNew();
        CloseCardUI(playCloseAnimation);
        closeUiWatch.Stop();
        LogPhasePerf("card-shutdown.close-ui", closeUiWatch.ElapsedMilliseconds);

        // Shutdown card system controller.
        if (m_CardSystemController != null)
        {
            LogicCardRuntimeState.Unbind(this);
            LogicCardCommandService.CommandApplying -= m_CardSystemController.ApplyLogicCardCommand;
            var controllerShutdownWatch = Stopwatch.StartNew();
            m_CardSystemController.Shutdown();
            m_CardSystemController = null;
            controllerShutdownWatch.Stop();
            LogPhasePerf("card-shutdown.controller", controllerShutdownWatch.ElapsedMilliseconds);
        }

        m_AutoDrawClock.Reset();
        totalWatch.Stop();
        LogPhasePerf("card-shutdown.total", totalWatch.ElapsedMilliseconds);
    }

    void ILogicCardRuntimeStateContributor.WriteDeterministicState(LogicStateHasher hasher)
    {
        if (m_CardSystemController == null)
            throw new System.InvalidOperationException("Bound card runtime state has no controller.");
        m_AutoDrawClock.WriteDeterministicState(hasher);
        m_CardSystemController.WriteDeterministicState(hasher);
    }

    private void CloseCardUI(bool playCloseAnimation)
    {
        if (GF.UI == null)
        {
            m_CardUIFormId = -1;
            return;
        }

        var ui = GF.UI;
        if (m_CardUIFormId > 0 && (ui.IsLoadingUIForm(m_CardUIFormId) || ui.HasUIForm(m_CardUIFormId)))
        {
            try
            {
                if (playCloseAnimation && !ui.IsLoadingUIForm(m_CardUIFormId))
                {
                    var uiForm = ui.GetUIForm(m_CardUIFormId) as UIForm;
                    var cardUIForm = uiForm != null ? uiForm.Logic as CardUIForm : null;
                    if (cardUIForm != null)
                    {
                        cardUIForm.CloseCardPanelWithAnimation();
                        m_CardUIFormId = -1;
                        return;
                    }
                }

                ui.CloseUIForm(m_CardUIFormId);
            }
            catch (GameFrameworkException ex)
            {
                Log.Warning("[CardGame] Close Card UI ignored: {0}", ex.Message);
            }
        }

        CloseAllLoadedCardUIFormsImmediately(ui);

        m_CardUIFormId = -1;
    }

    private static void CloseAllLoadedCardUIFormsImmediately(UIComponent ui)
    {
        if (ui == null)
            return;

        string cardUiAssetName = ui.GetUIFormAssetName(UIViews.CardUIForm);
        if (string.IsNullOrEmpty(cardUiAssetName))
            return;

        var forms = ui.GetUIForms(cardUiAssetName);
        for (int i = 0; i < forms.Length; i++)
        {
            if (forms[i] == null)
                continue;

            try
            {
                ui.CloseUIForm(forms[i].SerialId);
            }
            catch (GameFrameworkException ex)
            {
                Log.Warning("[CardGame] Close stray Card UI ignored: {0}", ex.Message);
            }
        }
    }

    private void InitializeCardSystem()
    {
        long stageStartTicks = Stopwatch.GetTimestamp();
        long stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        m_CardSystemController = new CardSystemController();
        m_CardSystemController.Initialize();
        RecordPerf(MainThreadPerfScope.CardSetupController, stageStartTicks, stageStartBytes);

        if (m_PreloadedCardPool == null)
        {
            throw new System.InvalidOperationException(
                "CardSetup.InitializeCardSystem failed: card pool was not preloaded during level loading.");
        }

        // The controller owns and clears its list during shutdown.
        stageStartTicks = Stopwatch.GetTimestamp();
        stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        List<ICardDataProvider> cardPool = new List<ICardDataProvider>(m_PreloadedCardPool);
        RecordPerf(MainThreadPerfScope.CardSetupCopyPool, stageStartTicks, stageStartBytes);

        stageStartTicks = Stopwatch.GetTimestamp();
        stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        m_CardSystemController.SetCardPool(cardPool);
        RecordPerf(MainThreadPerfScope.CardSetupSetPool, stageStartTicks, stageStartBytes);

        // Reset to empty deck and empty hand on each setup.
        stageStartTicks = Stopwatch.GetTimestamp();
        stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        m_CardSystemController.ResetDeckAndHand();
        RecordPerf(MainThreadPerfScope.CardSetupReset, stageStartTicks, stageStartBytes);

        stageStartTicks = Stopwatch.GetTimestamp();
        stageStartBytes = System.GC.GetAllocatedBytesForCurrentThread();
        EnsureLogicCardPlacementWorldBound();
        RecordPerf(MainThreadPerfScope.CardSetupBindWorld, stageStartTicks, stageStartBytes);

        Log.Info("[CardGame] Card system initialized.");
    }

    private List<ICardDataProvider> LoadCardPool()
    {
        List<ICardDataProvider> cardPool = new List<ICardDataProvider>();

        CardData[] cardDataArray = Resources.LoadAll<CardData>("CardData");
        foreach (var cardData in cardDataArray)
        {
            cardPool.Add(new CardDataAdapter(cardData));
        }

        if (cardPool.Count == 0)
        {
            Log.Warning("[CardGame] Card pool is empty. Check card data config.");
        }

        return cardPool;
    }

    private void PreloadCardPool()
    {
        if (m_PreloadedCardPool != null)
            return;

        var watch = Stopwatch.StartNew();
        m_PreloadedCardPool = LoadCardPool();
        watch.Stop();
        Log.Info(
            "[CardGame] Card pool preloaded during level loading. count={0}, elapsedMs={1}.",
            m_PreloadedCardPool.Count,
            watch.ElapsedMilliseconds);
    }

    public void EnsureLogicCardPlacementWorldBound()
    {
        Fog3Manager fogManager = Fog3Manager.Instance;
        if (fogManager == null || !fogManager.IsInitialized || fogManager.MapData == null)
            throw new System.InvalidOperationException("CardSetup requires initialized Fog3MapData before binding card placement.");

        if (LogicCardPlacementAuthority.IsWorldBound)
        {
            if (!LogicCardPlacementAuthority.IsBoundTo(fogManager.MapData))
            {
                throw new System.InvalidOperationException(
                    "CardSetup found logic fog exploration bound to a stale Fog3MapData instance.");
            }
            return;
        }

        LogicCardPlacementAuthority.BindRuntimeWorld(
            fogManager.MapData,
            LevelSelectionService.SelectedLevelIdentifier,
            fogManager.BlocksHiddenRevealByEnemyStronghold);
    }

    public void OpenCardUI()
    {
        var watch = Stopwatch.StartNew();
        if (GF.UI.IsLoadingUIForm(UIViews.CardUIForm) || GF.UI.HasUIForm(UIViews.CardUIForm))
        {
            Log.Info("[CardGame] Card UI is already loading/open. Skip duplicate open.");
            watch.Stop();
            LogPhasePerf("card-open-ui.total", watch.ElapsedMilliseconds);
            return;
        }

        UIParams uiParams = UIParams.Create();
        uiParams.Set("CardSystemController", m_CardSystemController);

        m_CardUIFormId = GF.UI.OpenUIForm(UIViews.CardUIForm, uiParams);
        if (m_CardUIFormId == -1)
        {
            Log.Error("[CardGame] Open Card UI failed.");
        }
        else
        {
            Log.Info("[CardGame] Card UI opened.");
        }

        watch.Stop();
        LogPhasePerf("card-open-ui.total", watch.ElapsedMilliseconds);
    }

    private static void LogPhasePerf(string step, long elapsedMilliseconds)
    {
        if (elapsedMilliseconds >= CardPhasePerfWarnMs)
            Log.Warning("[PhasePerf] {0}: {1}ms", step, elapsedMilliseconds);
    }

    private static void RecordPerf(MainThreadPerfScope scope, long startTicks, long startAllocatedBytes)
    {
        MainThreadFrameProfiler.Record(
            scope,
            Stopwatch.GetTimestamp() - startTicks,
            System.GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes);
    }

    private static long TicksToMilliseconds(long ticks)
    {
        return ticks * 1000L / Stopwatch.Frequency;
    }

    public bool GenerateCardToDeck(IBuildingLogicContext sourceBuilding)
    {
        return m_CardSystemController != null && m_CardSystemController.AddCardToDeck(sourceBuilding);
    }
}
