using System.Collections.Generic;
using BaseUtility;
using GameFramework;
using GameFramework.Event;
using UnityGameFramework.Runtime;

public partial class InputManager
{
    private HashSet<string> _blockingFormAssets;
    private int _openBlockingCount;
    private bool _uiFormEventSubscribed;

    private void InitializeUIFormControl()
    {
        _blockingFormAssets = BuildBlockingAssetSet();

        GF.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnUIFormChanged);
        GF.Event.Subscribe(CloseUIFormCompleteEventArgs.EventId, OnUIFormChanged);

        RecountOpenBlockingForms();
        ApplyInputStateByBlockingCount();
    }

    private void CleanupUIFormControl()
    {
        // 退出运行时，EventComponent/EventPool 可能已先被销毁或清空，直接 Unsubscribe 会抛异常。
        try
        {
            GF.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnUIFormChanged);
            GF.Event.Unsubscribe(CloseUIFormCompleteEventArgs.EventId, OnUIFormChanged);
        }
        catch (GameFrameworkException)
        {
            // 忽略：仅在生命周期销毁顺序导致的退订失败时触发
        }
    }

    private void OnUIFormChanged(object sender, GameEventArgs e)
    {
        if (_blockingFormAssets == null || _blockingFormAssets.Count == 0)
        {
            return;
        }

        switch (e)
        {
            case OpenUIFormSuccessEventArgs opened:
                if (IsBlockingAsset(opened.UIForm.UIFormAssetName))
                {
                    _openBlockingCount++;
                    ApplyInputStateByBlockingCount();
                }
                break;
            case CloseUIFormCompleteEventArgs closed:
                if (IsBlockingAsset(closed.UIFormAssetName))
                {
                    if (_openBlockingCount > 0)
                    {
                        _openBlockingCount--;
                    }
                    ApplyInputStateByBlockingCount();
                }
                break;
            default:
                break;
        }
    }

    private HashSet<string> BuildBlockingAssetSet()
    {
        if (GF.DataTable == null || !GF.DataTable.HasDataTable<UITable>())
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

    private void RecountOpenBlockingForms()
    {
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

    private void ApplyInputStateByBlockingCount()
    {
        if (_openBlockingCount > 0)
        {
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
}
