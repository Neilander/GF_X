using System;
using UnityEngine;
using UnityGameFramework.Runtime;

public class UIItemBase : MonoBehaviour, ISerializeFieldTool
{
    [HideInInspector][SerializeField] SerializeFieldData[] _fields;
    public SerializeFieldData[] SerializeFieldArr { get => _fields; set => _fields = value; }

    private void Awake()
    {
        Array.Clear(_fields, 0, _fields.Length);
        OnInit();
    }

    protected virtual void OnInit()
    {
        InitLocalization();
    }
    /// <summary>
    /// 更新界面中静态文本的多语言文字
    /// </summary>
    public virtual void InitLocalization()
    {
        UIStringKey[] texts = GetComponentsInChildren<UIStringKey>(true);
        foreach (var t in texts)
        {
            // UIStringKey 这里读的是直接本地化 key；LocalizationTextTable 的 identifier 入口请走 LocalizationTextDataModel。
            if (t.TryGetComponent<TMPro.TextMeshProUGUI>(out var textMeshCom))
            {
                textMeshCom.text = LocalizationTextManager.ProcessText(GF.Localization.GetString(t.Key));
            }
            else if (t.TryGetComponent<UnityEngine.UI.Text>(out var textCom))
            {
                textCom.text = LocalizationTextManager.ProcessText(GF.Localization.GetString(t.Key));
            }
        }
    }
}
