using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HoldProgress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] Image fillImage;   // 或 Slider slider;
    [SerializeField] float chargeSpeed = 0.8f; // 每秒充能
    [SerializeField] float drainSpeed = 1.2f;  // 松手回落
    [SerializeField] bool autoResetOnFull = true;
    [SerializeField] bool allowHold = true;
    [SerializeField] bool requireReleaseAfterFull = false;

    float progress;     // 0-1
    bool pointerHolding;
    bool externalHolding;
    bool lockUntilRelease;
    RectTransform fillRect;
    Vector3 fillBaseScale = Vector3.one;
    public System.Action onFull; // 可从外部赋值

    private void Awake()
    {
        // 兼容：若忘记在 Inspector 里绑定，默认取同物体 Image。
        if (fillImage == null)
            fillImage = GetComponent<Image>();

        if (fillImage != null)
        {
            fillRect = fillImage.rectTransform;
            if (fillRect != null)
                fillBaseScale = fillRect.localScale;
        }
    }

    private void OnEnable()
    {
        pointerHolding = false;
        externalHolding = false;
        lockUntilRelease = false;
        ResetProgress();
    }

    public bool AllowHold
    {
        get => allowHold;
        set
        {
            allowHold = value;
            if (!allowHold)
            {
                pointerHolding = false;
                externalHolding = false;
                lockUntilRelease = false;
            }
        }
    }

    public void SetExternalHolding(bool holding)
    {
        externalHolding = holding;
    }

    public void SetHoldSeconds(float seconds)
    {
        seconds = Mathf.Max(0.01f, seconds);
        chargeSpeed = 1f / seconds;
    }

    public void ResetProgress()
    {
        progress = 0f;
        ApplyVisual(progress);
    }

    void Update()
    {
        bool anyHolding = allowHold && (pointerHolding || externalHolding);
        if (lockUntilRelease && !anyHolding)
            lockUntilRelease = false;

        bool activeHolding = anyHolding && !lockUntilRelease;

        float target = activeHolding ? 1f : 0f;
        float speed = activeHolding ? chargeSpeed : drainSpeed;
        progress = Mathf.MoveTowards(progress, target, speed * Time.deltaTime);

        ApplyVisual(progress);
        // if (slider) slider.value = progress;

        if (progress >= 1f && autoResetOnFull)
        {
            onFull?.Invoke();
            progress = 0f; // 重置后继续可重复触发
            if (requireReleaseAfterFull)
                lockUntilRelease = true;
        }
    }

    public void OnPointerDown(PointerEventData eventData) => pointerHolding = allowHold;
    public void OnPointerUp(PointerEventData eventData) => pointerHolding = false;
    public void OnPointerExit(PointerEventData eventData) => pointerHolding = false; // 指针移出也视为松开

    private void ApplyVisual(float value)
    {
        if (fillImage == null)
            return;

        if (fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = value;
            return;
        }

        if (fillRect == null)
            fillRect = fillImage.rectTransform;
        if (fillRect == null)
            return;

        // 纯色/普通 Image 不支持 fillAmount 时，使用 X 轴缩放模拟进度条。
        fillRect.localScale = new Vector3(fillBaseScale.x * value, fillBaseScale.y, fillBaseScale.z);
    }
}
