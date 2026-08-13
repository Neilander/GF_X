using System.Collections.Generic;
using BaseUtility;
using GameFramework.Event;
using UnityGameFramework.Runtime;

public partial class InputManager
{
    private HashSet<string> _blockingFormAssets;
    private HashSet<string> _uiFormModeCoveredTipAssets;
    private readonly List<int> _uiFormModeHiddenBuildingTipIds = new();
    private int _openBlockingCount;
    private bool _uiFormEventSubscribed;

    private void InitializeUIFormControl()
    {
        RebuildUIFormControlAssetSets();

        EventComponent eventComponent = GF.Event;
        if (eventComponent == null)
            throw new System.InvalidOperationException("InputManager.InitializeUIFormControl failed: EventComponent is unavailable.");

        eventComponent.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnUIFormChanged);
        eventComponent.Subscribe(CloseUIFormCompleteEventArgs.EventId, OnUIFormChanged);
        _uiFormEventSubscribed = true;

        RecountOpenBlockingForms();
        ApplyInputStateByBlockingCount();
    }

    private void CleanupUIFormControl()
    {
        if (!_uiFormEventSubscribed)
            return;

        EventComponent eventComponent = GF.Event;
        if (eventComponent != null)
        {
            eventComponent.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnUIFormChanged);
            eventComponent.Unsubscribe(CloseUIFormCompleteEventArgs.EventId, OnUIFormChanged);
        }

        _uiFormEventSubscribed = false;
    }

    private void OnUIFormChanged(object sender, GameEventArgs e)
    {
        EnsureUIFormControlAssetSets();

        if (e is OpenUIFormSuccessEventArgs opened
            && CurState == InputState.UIForm
            && IsUIFormModeCoveredTipAsset(opened.UIForm.UIFormAssetName))
        {
            HideUIFormModeCoveredTip(opened.UIForm);
        }

        if (_blockingFormAssets == null || _blockingFormAssets.Count == 0)
        {
            return;
        }

        bool shouldRefresh = e is OpenUIFormSuccessEventArgs openedBlocking && IsBlockingAsset(openedBlocking.UIForm.UIFormAssetName)
                             || e is CloseUIFormCompleteEventArgs closed && IsBlockingAsset(closed.UIFormAssetName);
        if (!shouldRefresh)
        {
            return;
        }

        RefreshUIFormInputState();
    }

    private void EnsureUIFormControlAssetSets()
    {
        if (!CanBuildUIFormControlAssetSets())
        {
            return;
        }

        if (_blockingFormAssets == null || _blockingFormAssets.Count == 0
            || _uiFormModeCoveredTipAssets == null || _uiFormModeCoveredTipAssets.Count == 0)
        {
            RebuildUIFormControlAssetSets();
        }
    }

    private void RebuildUIFormControlAssetSets()
    {
        _blockingFormAssets = BuildBlockingAssetSet();
        _uiFormModeCoveredTipAssets = BuildUIFormModeCoveredTipAssetSet();
    }

    private bool CanBuildUIFormControlAssetSets()
    {
        return GF.DataTable != null && GF.DataTable.HasDataTable<UITable>();
    }

    private HashSet<string> BuildBlockingAssetSet()
    {
        if (!CanBuildUIFormControlAssetSets())
        {
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }

        var uiTable = GF.DataTable.GetDataTable<UITable>();
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var row in uiTable.GetAllDataRows())
        {
            if (row.BlockCharacterControl)
            {
                set.Add(UtilityBuiltin.AssetsPath.GetUIFormPath(row.UIPrefab));
            }
        }

        return set;
    }

    private HashSet<string> BuildUIFormModeCoveredTipAssetSet()
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        AddUIFormAsset(set, UIViews.BuildingBuildTips);
        AddUIFormAsset(set, UIViews.BuildingUpgradeTips);
        AddUIFormAsset(set, UIViews.BuildingInfoTips);
        return set;
    }

    private void AddUIFormAsset(HashSet<string> set, UIViews view)
    {
        if (!CanBuildUIFormControlAssetSets())
        {
            return;
        }

        string assetName = GF.UI.GetUIFormAssetName(view);
        if (!string.IsNullOrEmpty(assetName))
        {
            set.Add(assetName);
        }
    }

    private void RecountOpenBlockingForms()
    {
        EnsureUIFormControlAssetSets();

        _openBlockingCount = 0;
        if (_blockingFormAssets == null || _blockingFormAssets.Count == 0)
        {
            return;
        }

        foreach (var form in GF.UI.GetAllLoadedUIForms())
        {
            if (IsBlockingAsset(form.UIFormAssetName))
            {
                _openBlockingCount++;
            }
        }
    }

    private bool IsBlockingAsset(string assetName)
    {
        return _blockingFormAssets != null && _blockingFormAssets.Contains(assetName);
    }

    private bool IsUIFormModeCoveredTipAsset(string assetName)
    {
        return _uiFormModeCoveredTipAssets != null && _uiFormModeCoveredTipAssets.Contains(assetName);
    }

    public void RefreshUIFormInputState()
    {
        RecountOpenBlockingForms();
        ApplyInputStateByBlockingCount();
    }

    private void ApplyInputStateByBlockingCount()
    {
        if (_openBlockingCount > 0)
        {
            HideUIFormModeCoveredTips();
            if (CurState != InputState.UIForm)
            {
                ChangeState(InputState.UIForm);
            }
        }
        else if (CurState == InputState.UIForm)
        {
            ChangeState(InputState.Game);
        }
    }

    private void HideUIFormModeCoveredTips()
    {
        EnsureUIFormControlAssetSets();

        if (GF.UI == null)
        {
            return;
        }

        if (_uiFormModeCoveredTipAssets == null || _uiFormModeCoveredTipAssets.Count == 0)
        {
            return;
        }

        foreach (string assetName in _uiFormModeCoveredTipAssets)
        {
            foreach (var form in GF.UI.GetUIForms(assetName))
            {
                HideUIFormModeCoveredTip(form);
            }
        }
    }

    private void HideUIFormModeCoveredTip(UIForm form)
    {
        if (form == null || form.Logic == null || !form.Logic.Visible)
        {
            return;
        }

        form.Logic.Visible = false;
        if (!_uiFormModeHiddenBuildingTipIds.Contains(form.SerialId))
        {
            _uiFormModeHiddenBuildingTipIds.Add(form.SerialId);
        }
    }

    private void RestoreUIFormModeCoveredTips()
    {
        if (GF.UI == null || _uiFormModeHiddenBuildingTipIds.Count == 0)
        {
            _uiFormModeHiddenBuildingTipIds.Clear();
            return;
        }

        foreach (int serialId in _uiFormModeHiddenBuildingTipIds)
        {
            if (!GF.UI.HasUIForm(serialId))
            {
                continue;
            }

            var form = GF.UI.GetUIForm(serialId);
            if (form?.Logic == null || !form.Logic.Available || form.Logic.Visible)
            {
                continue;
            }

            form.Logic.Visible = true;
        }

        _uiFormModeHiddenBuildingTipIds.Clear();
    }
}
