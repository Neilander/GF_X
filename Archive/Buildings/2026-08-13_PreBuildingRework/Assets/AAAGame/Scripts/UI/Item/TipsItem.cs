using System;
using DG.Tweening;
using UnityEngine;

public partial class TipsItem : UIItemBase
{
    public const float DefaultHeight = 120f;

    [SerializeField] private DOTweenSequence m_OpenAnimation = null;

    private RectTransform m_RectTransform;
    private Tween m_MoveTween;
    private Tween m_OpenTween;
    private Tween m_CloseTween;
    private Tween m_DelayTween;
    private Action<TipsItem> m_OnComplete;
    private bool m_IsClosing;

    protected override void OnInit()
    {
        base.OnInit();
        m_RectTransform = GetComponent<RectTransform>();
    }

    private void OnDisable()
    {
        KillTweens();
    }

    public void SetData(string title, string content, string icon = null)
    {
        string iconMarkup = GetIconMarkup(icon);
        bool hasTitle = !string.IsNullOrEmpty(title);
        varTitle.gameObject.SetActive(hasTitle);
        varContent.gameObject.SetActive(hasTitle);
        
        if (varNoTitleContent != null)
        {
            varNoTitleContent.gameObject.SetActive(!hasTitle);
        }

        if (hasTitle)
        {
            varTitle.text = iconMarkup + title;
            varContent.text = content;
        }
        else if (varNoTitleContent != null)
        {
            varNoTitleContent.text = iconMarkup + content;
        }
    }

    public void Play(string title, string content, string icon, float duration, Action<TipsItem> onComplete)
    {
        SetData(title, content, icon);
        KillTweens();
        m_OnComplete = onComplete;
        m_IsClosing = false;

        m_OpenTween = m_OpenAnimation?.DOPlay();
        if (m_OpenTween != null)
        {
            m_OpenTween.OnComplete(() => StartCloseDelay(duration));
        }
        else
        {
            StartCloseDelay(duration);
        }
    }

    private static string GetIconMarkup(string icon)
    {
        string resolvedIcon = string.IsNullOrWhiteSpace(icon) ? TipsDataModel.DefaultIcon : icon;
        switch (resolvedIcon)
        {
            case "Narrator":
                return "<color=#F4A261>◆</color> ";
            case "Director":
                return "<color=#55D6BE>▣</color> ";
            case "Terminal":
                return "<color=#76A9FF>⌘</color> ";
            default:
                throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown tip speaker icon.");
        }
    }

    public void MoveTo(Vector2 targetPos, float duration)
    {
        m_MoveTween?.Kill();
        if (duration <= 0f)
        {
            var currentPos = m_RectTransform.anchoredPosition;
            currentPos.y = targetPos.y;
            m_RectTransform.anchoredPosition = currentPos;
            return;
        }

        m_MoveTween = m_RectTransform.DOAnchorPosY(targetPos.y, duration).SetEase(Ease.OutQuad);
    }

    public void RequestClose()
    {
        if (m_IsClosing)
        {
            return;
        }

        m_OpenTween?.Kill();
        m_OpenTween = null;

        m_DelayTween?.Kill();
        m_DelayTween = null;

        PlayCloseAnimation();
    }

    private void StartCloseDelay(float duration)
    {
        if (duration < 0f)
        {
            return;
        }

        if (duration <= 0f)
        {
            PlayCloseAnimation();
            return;
        }

        m_DelayTween = DOVirtual.DelayedCall(duration, PlayCloseAnimation, true);
    }

    private void PlayCloseAnimation()
    {
        if (m_IsClosing)
        {
            return;
        }

        m_IsClosing = true;

        m_CloseTween = m_OpenAnimation?.DORewind();
        if (m_CloseTween != null)
        {
            m_CloseTween.OnComplete(NotifyComplete);
            return;
        }

        NotifyComplete();
    }

    private void NotifyComplete()
    {
        var onComplete = m_OnComplete;
        m_OnComplete = null;
        onComplete?.Invoke(this);
    }

    private void KillTweens()
    {
        m_MoveTween?.Kill();
        m_MoveTween = null;

        m_OpenTween?.Kill();
        m_OpenTween = null;

        m_CloseTween?.Kill();
        m_CloseTween = null;

        m_DelayTween?.Kill();
        m_DelayTween = null;

        m_OpenAnimation?.DOKill();

        m_OnComplete = null;
        m_IsClosing = false;
    }
}
