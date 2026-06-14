using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
using AAAGame.Card;

public partial class CardSetup : GameFrameworkComponent
{
    private const long CardPhasePerfWarnMs = 30;
    private CardSystemController m_CardSystemController;
    private int m_CardUIFormId = -1;
    private float m_NextAutoDrawTime;
    private const float AutoDrawInterval = 0.15f;

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
    }

    public void CardSystemSetup()
    {
        var watch = Stopwatch.StartNew();
        CardSystemShutdown(false);
        m_NextAutoDrawTime = 0f;
        InitializeCardSystem();
        watch.Stop();
        LogPhasePerf("card-setup.total", watch.ElapsedMilliseconds);
    }

    public void CardSystemUpdate()
    {
        if (m_CardSystemController != null)
        {
            if (CanAutoDrawNextCard())
            {
                if (m_CardSystemController.TryAutoDrawOneCardFromDeck())
                {
                    m_NextAutoDrawTime = Time.unscaledTime + AutoDrawInterval;
                }
            }

            m_CardSystemController.UpdatePlacement();
        }
    }

    private bool CanAutoDrawNextCard()
    {
        if (Time.unscaledTime < m_NextAutoDrawTime)
        {
            return false;
        }

        if (m_CardUIFormId <= 0 || GF.UI == null)
        {
            return false;
        }

        var uiForm = GF.UI.GetUIForm(m_CardUIFormId) as UIForm;
        var cardUIForm = uiForm != null ? uiForm.Logic as CardUIForm : null;
        return cardUIForm != null && cardUIForm.IsReadyForAutoDraw;
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
            var controllerShutdownWatch = Stopwatch.StartNew();
            m_CardSystemController.Shutdown();
            m_CardSystemController = null;
            controllerShutdownWatch.Stop();
            LogPhasePerf("card-shutdown.controller", controllerShutdownWatch.ElapsedMilliseconds);
        }

        m_NextAutoDrawTime = 0f;
        totalWatch.Stop();
        LogPhasePerf("card-shutdown.total", totalWatch.ElapsedMilliseconds);
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
        m_CardSystemController = new CardSystemController();
        m_CardSystemController.Initialize();

        // Load card pool from resources.
        List<ICardDataProvider> cardPool = LoadCardPool();
        m_CardSystemController.SetCardPool(cardPool);

        // Reset to empty deck and empty hand on each setup.
        m_CardSystemController.ResetDeckAndHand();

        // Bind placeable area objects.
        SetupAreaObjects();

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

    private void SetupAreaObjects()
    {
        GameObject validArea = GameObject.Find("ValidArea");
        GameObject invalidArea = GameObject.Find("InvalidArea");

        if (validArea != null && invalidArea != null)
        {
            m_CardSystemController.SetAreaObjects(validArea, invalidArea);
        }
        else
        {
            Log.Warning("[CardGame] Area objects not found. Card placement may not work.");
        }
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

    public bool GenerateCardToDeck(BuildingEntity sourceBuilding)
    {
        return m_CardSystemController != null && m_CardSystemController.AddCardToDeck(sourceBuilding);
    }

    public bool AddCardToDeck(CardData cardData)
    {
        return m_CardSystemController != null && m_CardSystemController.AddCardToDeck(cardData);
    }

    public bool DrawCard()
    {
        return m_CardSystemController != null && m_CardSystemController.DrawCard();
    }
}
