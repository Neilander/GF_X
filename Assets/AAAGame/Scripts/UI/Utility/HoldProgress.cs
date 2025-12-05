using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HoldProgress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] Image fillImage;   // 或 Slider slider;
    [SerializeField] float chargeSpeed = 0.8f; // 每秒充能
    [SerializeField] float drainSpeed = 1.2f;  // 松手回落
    [SerializeField] bool autoResetOnFull = true;

    float progress;     // 0-1
    bool isHolding;
    public System.Action onFull; // 可从外部赋值

    void Update()
    {
        float target = isHolding ? 1f : 0f;
        float speed = isHolding ? chargeSpeed : drainSpeed;
        progress = Mathf.MoveTowards(progress, target, speed * Time.deltaTime);

        if (fillImage) fillImage.fillAmount = progress;
        // if (slider) slider.value = progress;

        if (progress >= 1f && autoResetOnFull)
        {
            onFull?.Invoke();
            progress = 0f; // 重置后继续可重复触发
        }
    }

    public void OnPointerDown(PointerEventData eventData) => isHolding = true;
    public void OnPointerUp(PointerEventData eventData) => isHolding = false;
    public void OnPointerExit(PointerEventData eventData) => isHolding = false; // 指针移出也视为松开
}