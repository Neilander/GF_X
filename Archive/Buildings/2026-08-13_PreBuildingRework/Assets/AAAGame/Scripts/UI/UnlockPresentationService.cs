using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum UnlockPayloadType
{
    Industry,
    LevelTag,
    Keepsake
}

public readonly struct UnlockPayload
{
    public UnlockPayload(UnlockPayloadType type, string title, string description)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Unlock title is empty.", nameof(title));
        Type = type;
        Title = title;
        Description = description ?? string.Empty;
    }

    public UnlockPayloadType Type { get; }
    public string Title { get; }
    public string Description { get; }
}

public static class UnlockPresentationService
{
    private static readonly Queue<UnlockPayload> s_Pending = new();

    public static void Enqueue(UnlockPayload payload)
    {
        s_Pending.Enqueue(payload);
    }

    public static void ShowPending(Transform parent)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));
        if (s_Pending.Count == 0)
            return;

        ShowNext(parent);
    }

    private static void ShowNext(Transform parent)
    {
        if (s_Pending.Count == 0)
            return;

        UnlockPayload payload = s_Pending.Dequeue();
        RectTransform overlay = CreateRect("UnlockPresentPanel", parent);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        Image dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.82f);

        RectTransform panel = CreateRect("Content", overlay);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(560f, 320f);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.13f, 0.15f, 1f);
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        CreateText("Header", panel, "\u65b0\u89e3\u9501", 30, FontStyles.Bold, 52f);
        CreateText("Title", panel, payload.Title, 38, FontStyles.Bold, 64f);
        CreateText("Description", panel, payload.Description, 22, FontStyles.Normal, 76f);
        Button close = CreateButton(panel, "\u786e\u5b9a", 56f);
        close.onClick.AddListener(() =>
        {
            UnityEngine.Object.Destroy(overlay.gameObject);
            ShowNext(parent);
        });
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string value,
        int size,
        FontStyles style,
        float preferredHeight)
    {
        RectTransform rect = CreateRect(name, parent);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.enableWordWrapping = true;
        return text;
    }

    private static Button CreateButton(Transform parent, string label, float height)
    {
        RectTransform rect = CreateRect("Confirm", parent);
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.9f, 0.48f, 0.08f, 1f);
        Button button = rect.gameObject.AddComponent<Button>();
        TextMeshProUGUI text = CreateText("Label", rect, label, 24, FontStyles.Bold, height);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }
}
