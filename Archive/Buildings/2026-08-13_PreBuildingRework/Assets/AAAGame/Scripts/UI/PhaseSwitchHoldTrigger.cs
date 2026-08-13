using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

public sealed class PhaseSwitchHoldTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public const float HoldDurationSeconds = 1.5f;
    public const float ReleaseDurationSeconds = 0.18f;
    private const string ShortcutAction = "Player/SwitchPhase";

    private Button m_Button;
    private Image m_FillImage;
    private Action m_Completed;
    private Func<bool> m_CanHold;
    private bool m_PointerHeld;
    private bool m_LockedUntilReleased;
    private float m_Progress;

    public float Progress => m_Progress;

    public void Configure(Button button, Action completed, Func<bool> canHold)
    {
        m_Button = button != null ? button : throw new ArgumentNullException(nameof(button));
        m_Completed = completed ?? throw new ArgumentNullException(nameof(completed));
        m_CanHold = canHold ?? throw new ArgumentNullException(nameof(canHold));
        EnsureFillImage();
        ResetState();
    }

    public void Release()
    {
        m_Completed = null;
        m_CanHold = null;
        ResetState();
    }

    private void Update()
    {
        if (m_Button == null || m_Completed == null || m_CanHold == null)
            return;

        InputManager inputManager = GameEntry.GetComponent<InputManager>();
        bool shortcutHeld = inputManager != null
                            && inputManager.CurState == InputState.Game
                            && inputManager.IsActionPressed(ShortcutAction);
        bool anyInputHeld = m_PointerHeld || shortcutHeld;
        bool canHold = m_Button.interactable && m_CanHold();
        bool charging = canHold && anyInputHeld && !m_LockedUntilReleased;

        m_Progress = AdvanceProgress(m_Progress, charging, Time.unscaledDeltaTime);
        ApplyProgress();

        if (m_LockedUntilReleased)
        {
            if (!anyInputHeld && m_Progress <= 0f)
                m_LockedUntilReleased = false;
            return;
        }

        if (!charging || m_Progress < 1f)
            return;

        m_LockedUntilReleased = true;
        m_Completed.Invoke();
    }

    public static float AdvanceProgress(float current, bool charging, float deltaTime)
    {
        float duration = charging ? HoldDurationSeconds : ReleaseDurationSeconds;
        float target = charging ? 1f : 0f;
        return Mathf.MoveTowards(Mathf.Clamp01(current), target, Mathf.Max(0f, deltaTime) / duration);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData != null && eventData.button == PointerEventData.InputButton.Left)
            m_PointerHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || eventData.button == PointerEventData.InputButton.Left)
            m_PointerHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_PointerHeld = false;
    }

    private void OnDisable()
    {
        ResetState();
    }

    private void EnsureFillImage()
    {
        if (m_FillImage != null)
            return;

        var fillObject = new GameObject("PhaseSwitchHoldFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = fillObject.GetComponent<RectTransform>();
        rect.SetParent(m_Button.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();

        m_FillImage = fillObject.GetComponent<Image>();
        if (m_Button.targetGraphic is Image targetImage)
            m_FillImage.sprite = targetImage.sprite;
        m_FillImage.type = Image.Type.Filled;
        m_FillImage.fillMethod = Image.FillMethod.Horizontal;
        m_FillImage.fillOrigin = 0;
        m_FillImage.color = new Color(1f, 0.72f, 0.16f, 0.72f);
        m_FillImage.raycastTarget = false;
    }

    private void ResetState()
    {
        m_PointerHeld = false;
        m_LockedUntilReleased = false;
        m_Progress = 0f;
        ApplyProgress();
    }

    private void ApplyProgress()
    {
        if (m_FillImage != null)
            m_FillImage.fillAmount = m_Progress;
    }
}
