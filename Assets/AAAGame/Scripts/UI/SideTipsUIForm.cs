using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName)]
public partial class SideTipsUIForm : UIFormBase
{
    private struct PendingTipData
    {
        public string Key;
        public string Title;
        public string Content;
        public float Duration;
    }

    private const float TipSpacing = 10f;
    private const float TipMoveDuration = 0.2f;
    private static readonly Queue<PendingTipData> s_PendingTips = new Queue<PendingTipData>(4);
    private static readonly HashSet<string> s_PendingTipKeys = new HashSet<string>();

    private sealed class ActiveTipData
    {
        public TipsItem Item;
        public string Key;
    }

    public static SideTipsUIForm Instance { get; private set; }

    private readonly List<ActiveTipData> m_ActiveItems = new List<ActiveTipData>(4);
    private readonly HashSet<string> m_ActiveTipKeys = new HashSet<string>();
    private bool m_HasBaseTipPos;
    private Vector2 m_BaseTipPos;

    private static string BuildTipKey(string title, string content)
    {
        return (title ?? string.Empty) + "\n" + (content ?? string.Empty);
    }

    public static void EnqueuePendingTips(string title, string content, float duration)
    {
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
        {
            return;
        }

        string key = BuildTipKey(title, content);
        if (s_PendingTipKeys.Contains(key))
        {
            return;
        }

        if (Instance != null && Instance.m_ActiveTipKeys.Contains(key))
        {
            return;
        }

        s_PendingTipKeys.Add(key);

        s_PendingTips.Enqueue(new PendingTipData
        {
            Key = key,
            Title = title,
            Content = content,
            Duration = duration,
        });
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        Instance = this;
        TryInitBaseTipPos();
        FlushPendingTips();
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        if (Instance == this)
        {
            Instance = null;
        }
        m_ActiveItems.Clear();
        m_ActiveTipKeys.Clear();
        m_HasBaseTipPos = false;
        base.OnClose(isShutdown, userData);
    }

    public void ShowTips(string title, string content, float duration = 2f)
    {
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
        {
            return;
        }

        string key = BuildTipKey(title, content);
        if (m_ActiveTipKeys.Contains(key))
        {
            return;
        }

        if (varTipsItem == null)
        {
            Log.Warning("SideTipsUIForm.ShowTips失败, varTipsItem为空。");
            return;
        }
        if (!TryInitBaseTipPos())
        {
            return;
        }

        CleanupInvalidItems();
        var itemObject = SpawnItem<UIItemObject>(varTipsItem, transform);
        var tipsItem = itemObject?.itemLogic as TipsItem;
        if (tipsItem == null)
        {
            Log.Warning("SideTipsUIForm.ShowTips失败, TipsItem组件不存在。");
            if (itemObject?.gameObject != null)
            {
                UnspawnItem<UIItemObject>(varTipsItem, itemObject.gameObject);
            }
            return;
        }

        int newIndex = m_ActiveItems.Count;
        m_ActiveItems.Add(new ActiveTipData
        {
            Item = tipsItem,
            Key = key,
        });
        m_ActiveTipKeys.Add(key);
        tipsItem.SetData(title, content);
        tipsItem.MoveTo(GetTipPos(newIndex), 0f);

        tipsItem.Play(title, content, duration, OnTipsItemComplete);
    }

    private void OnTipsItemComplete(TipsItem tipsItem)
    {
        if (tipsItem == null)
        {
            return;
        }

        int removedIndex = -1;
        for (int i = 0; i < m_ActiveItems.Count; i++)
        {
            if (m_ActiveItems[i].Item != tipsItem)
            {
                continue;
            }

            removedIndex = i;
            m_ActiveTipKeys.Remove(m_ActiveItems[i].Key);
            m_ActiveItems.RemoveAt(i);
            break;
        }

        if (tipsItem.gameObject != null)
        {
            UnspawnItem<UIItemObject>(varTipsItem, tipsItem.gameObject);
        }

        CleanupInvalidItems();
        if (removedIndex < 0)
        {
            removedIndex = 0;
        }
        for (int i = removedIndex; i < m_ActiveItems.Count; i++)
        {
            TipsItem item = m_ActiveItems[i].Item;
            if (item != null)
            {
                item.MoveTo(GetTipPos(i), TipMoveDuration);
            }
        }
    }

    private void FlushPendingTips()
    {
        while (s_PendingTips.Count > 0)
        {
            var tip = s_PendingTips.Dequeue();
            if (!string.IsNullOrEmpty(tip.Key))
            {
                s_PendingTipKeys.Remove(tip.Key);
            }

            ShowTips(tip.Title, tip.Content, tip.Duration);
        }
    }

    private void CleanupInvalidItems()
    {
        for (int i = m_ActiveItems.Count - 1; i >= 0; i--)
        {
            ActiveTipData data = m_ActiveItems[i];
            if (data == null || data.Item == null)
            {
                if (data != null)
                {
                    m_ActiveTipKeys.Remove(data.Key);
                }

                m_ActiveItems.RemoveAt(i);
            }
        }
    }

    private Vector2 GetTipPos(int index)
    {
        return new Vector2(m_BaseTipPos.x, GetTipCenterY(index));
    }

    private float GetTipCenterY(int index)
    {
        if (index <= 0)
            return m_BaseTipPos.y;

        float centerY = m_BaseTipPos.y;
        float previousHalf = GetItemHeight(0) * 0.5f;
        for (int i = 1; i <= index; i++)
        {
            float currentHalf = GetItemHeight(i) * 0.5f;
            centerY -= previousHalf + TipSpacing + currentHalf;
            previousHalf = currentHalf;
        }

        return centerY;
    }

    private float GetItemHeight(int index)
    {
        if (index < 0 || index >= m_ActiveItems.Count)
            return TipsItem.DefaultHeight;

        TipsItem item = m_ActiveItems[index].Item;
        if (item == null)
            return TipsItem.DefaultHeight;

        RectTransform itemRect = item.transform as RectTransform;
        if (itemRect == null)
            return TipsItem.DefaultHeight;

        LayoutRebuilder.ForceRebuildLayoutImmediate(itemRect);

        float preferredHeight = LayoutUtility.GetPreferredHeight(itemRect);
        if (preferredHeight > 0f)
            return preferredHeight;

        float actualHeight = itemRect.rect.height;
        return actualHeight > 0f ? actualHeight : TipsItem.DefaultHeight;
    }

    private bool TryInitBaseTipPos()
    {
        if (m_HasBaseTipPos)
        {
            return true;
        }

        if (varTipsItem == null)
        {
            Log.Warning("SideTipsUIForm初始化失败, varTipsItem为空。");
            return false;
        }

        var rect = varTipsItem.GetComponent<RectTransform>();
        if (rect == null)
        {
            Log.Warning("SideTipsUIForm初始化失败, varTipsItem没有RectTransform。");
            return false;
        }

        m_BaseTipPos = rect.anchoredPosition;
        m_HasBaseTipPos = true;
        return true;
    }
}
