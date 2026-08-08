using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityGameFramework.Runtime;

[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class LvEnterDialog : UIFormBase
{
    private const string MaxCountKey = "LvTagPositiveMaxCount";

    private static string s_LevelIdentifier;
    private static bool s_IsStartup;

    public static void Open(string levelIdentifier, bool isStartup)
    {
        s_LevelIdentifier = levelIdentifier;
        s_IsStartup = isStartup;
        GF.UI.OpenUIForm(UIViews.LvEnterDialog);
    }

    private int m_MaxPositive;
    private readonly List<LvTagItem> m_PositiveItems = new();
    private readonly List<LvTagItem> m_NegativeItems = new();
    private readonly HashSet<int> m_SelectedIds = new();

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        CareerConfigRuntime.Prepare();
        InitializeCareerEntryUI();
        m_MaxPositive = GF.Config.GetInt(MaxCountKey, 3);
        varReturnBtn.onClick.RemoveAllListeners();
        varReturnBtn.onClick.AddListener(OnClickClose);
        varEnterBtn.onClick.RemoveAllListeners();
        varEnterBtn.onClick.AddListener(OnEnterClick);

        varInfoDesc.text = string.Empty;
        SpawnTags();
        RefreshTexts();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        UnspawnAllItem<UIItemObject>(varLvTagItem);
        m_PositiveItems.Clear();
        m_NegativeItems.Clear();
        m_SelectedIds.Clear();
        ShutdownCareerEntryUI();
        base.OnClose(isShutdown, userData);
    }

    private void SpawnTags()
    {
        var table = GF.DataTable.GetDataTable<LevelTagTable>();
        foreach (var row in table.GetAllDataRows())
        {
            if (!CareerConfigRuntime.IsTagAvailableForLevel(row, s_LevelIdentifier))
                continue;

            var item = SpawnItem<UIItemObject>(varLvTagItem,
                row.IsPositiveTag ? varPositiveTagGrids : varNegativeTagGrids)
                .itemLogic as LvTagItem;
            item.Setup(row, OnTagClicked);

            if (row.IsPositiveTag) m_PositiveItems.Add(item);
            else m_NegativeItems.Add(item);
        }
    }

    private void OnTagClicked(LvTagItem item)
    {
        var row = GF.DataTable.GetDataTable<LevelTagTable>().GetDataRow(item.TagId);
        varInfoDesc.text = DescriptionValueFormatter.LocalizeAndFill(row.DescKey, row.UniqueValues);

        if (m_SelectedIds.Contains(item.TagId))
        {
            m_SelectedIds.Remove(item.TagId);
            item.SetSelected(false);
            if (item.GroupID > 0) SetGroupGrayed(item.GroupID, -1);
        }
        else
        {
            if (item.IsPositive && m_SelectedIds.Count(id => m_PositiveItems.Any(i => i.TagId == id)) >= m_MaxPositive)
                return;

            if (item.GroupID > 0)
            {
                var prev = FindSelectedInGroup(item.GroupID);
                if (prev != null) { m_SelectedIds.Remove(prev.TagId); prev.SetSelected(false); }
            }

            m_SelectedIds.Add(item.TagId);
            item.SetGrayed(false);
            item.SetSelected(true);
            if (item.GroupID > 0) SetGroupGrayed(item.GroupID, item.TagId);
        }

        RefreshTexts();
    }

    private LvTagItem FindSelectedInGroup(int groupId)
    {
        foreach (var list in new[] { m_PositiveItems, m_NegativeItems })
            foreach (var i in list)
                if (i.GroupID == groupId && m_SelectedIds.Contains(i.TagId)) return i;
        return null;
    }

    private void SetGroupGrayed(int groupId, int selectedId)
    {
        foreach (var list in new[] { m_PositiveItems, m_NegativeItems })
            foreach (var i in list)
                if (i.GroupID == groupId && i.TagId != selectedId)
                    i.SetGrayed(selectedId > 0);
    }

    private void RefreshTexts()
    {
        int posCount = m_PositiveItems.Count(i => m_SelectedIds.Contains(i.TagId));
        varPositiveSelectNumText.text =
            $"{LocalizationTextDataModel.GetText("LvTag_Positive")} {posCount}/{m_MaxPositive}";

        int negLevel = m_NegativeItems.Where(i => m_SelectedIds.Contains(i.TagId)).Sum(i => i.TagLevel);
        varNegativeSelectNumText.text =
            $"{LocalizationTextDataModel.GetText("LvTag_Negative")} {negLevel}";
    }

    private void OnEnterClick()
    {
        string runtimeLevelIdentifier;
        try
        {
            runtimeLevelIdentifier = CareerRunSettings.BeginRun(
                s_LevelIdentifier,
                m_IsVariableExperiment,
                m_SelectedArchetype);
        }
        catch (System.Exception exception)
        {
            Log.Error("[LvEnterDialog] Career run selection is invalid. level={0}, error={1}", s_LevelIdentifier, exception.Message);
            return;
        }

        if (m_IsVariableExperiment || CareerConfigRuntime.IsTutorialLevel(s_LevelIdentifier))
            LevelTagRuntime.SetActiveTagIds(System.Array.Empty<int>());
        else
            LevelTagRuntime.SetActiveTagIds(m_SelectedIds);
        string error;
        bool ok = s_IsStartup
            ? StartupLevelSelectProcedure.TryEnterLevel(runtimeLevelIdentifier, out error)
            : LevelSelectionService.TryEnterLevelInPlace(runtimeLevelIdentifier, out error);

        if (!ok)
        {
            CareerRunSettings.CancelRun();
            Log.Warning("[LvEnterDialog] Failed to enter level {0}: {1}", runtimeLevelIdentifier, error);
        }
        else
            GF.UI.Close(this.UIForm);
    }
}
