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

    protected override void OnInit()
    {
        base.OnInit();
        m_RectTransform = GetComponent<RectTransform>();
    }

    private void OnDisable()
    {
        KillTweens();
    }

    public void SetData(string title, string content)
    {
        varTitle.text = title;
        varContent.text = content;
    }

    public void Play(string title, string content, float duration, Action<TipsItem> onComplete)
    {
        SetData(title, content);
        KillTweens();
        m_OnComplete = onComplete;

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

    private void StartCloseDelay(float duration)
    {
        if (duration <= 0f)
        {
            PlayCloseAnimation();
            return;
        }

        m_DelayTween = DOVirtual.DelayedCall(duration, PlayCloseAnimation, true);
    }

    private void PlayCloseAnimation()
    {
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
        m_OnComplete?.Invoke(this);
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
    }
}
