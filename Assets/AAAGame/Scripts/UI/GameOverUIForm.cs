using GameFramework;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class GameOverUIForm : UIFormBase
{
    public const string P_IsWin = "IsWin";

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RefreshResultView();
        FinalizeKeepsakeUnlocks();
        ShowCareerSettlement();
        BindButtons();
        LevelSelectionService.LevelLoadCompleted += OnLevelLoadCompleted;
        UnlockPresentationService.ShowPending(transform);
    }

    private void FinalizeKeepsakeUnlocks()
    {
        bool isWin = Params.Get<VarBoolean>(P_IsWin);
        if (!isWin || !CareerRunSettings.HasActiveRun)
        {
            KeepsakeSettlementUnlockService.DiscardPending();
            return;
        }

        CareerProgressDataModel progress = GF.DataModel.GetOrCreate<CareerProgressDataModel>();
        IReadOnlyList<KeepsakeTable> unlocked = KeepsakeSettlementUnlockService.Commit(progress);
        for (int i = 0; i < unlocked.Count; i++)
        {
            KeepsakeTable keepsake = unlocked[i];
            UnlockPresentationService.Enqueue(new UnlockPayload(
                UnlockPayloadType.Keepsake,
                LocalizationTextManager.GetLocalizedText(keepsake.NameKey),
                LocalizationTextManager.GetLocalizedText(keepsake.DescKey)));
        }
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        LevelSelectionService.LevelLoadCompleted -= OnLevelLoadCompleted;
        UnbindButtons();
        base.OnClose(isShutdown, userData);
    }

    protected override void OnButtonClick(object sender, Button btSelf)
    {
        base.OnButtonClick(sender, btSelf);

        if (btSelf == varLvselectbtn)
        {
            LevelSwitchUIForm.Open(false);
        }
    }

    private void RefreshResultView()
    {
        bool isWin = Params.Get<VarBoolean>(P_IsWin);

        if (varShengli != null)
        {
            varShengli.SetActive(isWin);
        }

        if (varShibai != null)
        {
            varShibai.SetActive(!isWin);
        }
    }

    private void ShowCareerSettlement()
    {
        CareerWinRecordResult? result = CareerSettlementPresentationService.ConsumeLatest();
        if (!result.HasValue || result.Value.Ignored || varShengli == null)
            return;

        RectTransform panel = new GameObject("CareerSettlement", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(transform, false);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(620f, 250f);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.11f, 0.96f);
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        CareerWinRecordResult value = result.Value;
        CreateSettlementText(
            panel,
            string.Format(
                LocalizationTextManager.GetLocalizedText("CareerSettlement.ExperienceGrade"),
                value.TotalExperienceGained,
                value.PreviousGrade,
                value.CurrentGrade),
            26,
            42f);
        CreateSettlementText(
            panel,
            string.Format(
                LocalizationTextManager.GetLocalizedText("CareerSettlement.ExperienceBreakdown"),
                value.ClearExperience,
                value.OptionalExperience,
                value.FirstClearExperience),
            18,
            34f);
        CreateSettlementText(
            panel,
            string.Format(
                LocalizationTextManager.GetLocalizedText("CareerSettlement.OffsetMultiplier"),
                value.ExperienceMultiplier),
            18,
            34f);
    }

    private static void CreateSettlementText(Transform parent, string value, int fontSize, float height)
    {
        RectTransform rect = new GameObject("SettlementText", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private void BindButtons()
    {
        if (varLvselectbtn == null)
        {
            return;
        }

        varLvselectbtn.onClick.RemoveListener(OnLevelSelectClicked);
        varLvselectbtn.onClick.AddListener(OnLevelSelectClicked);
    }

    private void UnbindButtons()
    {
        if (varLvselectbtn == null)
        {
            return;
        }

        varLvselectbtn.onClick.RemoveListener(OnLevelSelectClicked);
    }

    private void OnLevelSelectClicked()
    {
        ClickUIButton(varLvselectbtn);
    }

    private void OnLevelLoadCompleted()
    {
        GF.UI.Close(this.UIForm);
    }
}
