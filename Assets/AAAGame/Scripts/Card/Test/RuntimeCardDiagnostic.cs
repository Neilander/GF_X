using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AAAGame.Card.UI;

namespace AAAGame.Card.Test
{
    /// <summary>
    /// 运行时卡牌诊断工具
    /// 在游戏运行时检查卡牌的实际状态
    /// </summary>
    public class RuntimeCardDiagnostic : MonoBehaviour
    {
        [Header("自动诊断")]
        [SerializeField] private bool autoRunOnStart = true;
        [SerializeField] private float autoRunDelay = 2f; // 延迟2秒，等待卡牌生成

        void Start()
        {
            if (autoRunOnStart)
            {
                Invoke(nameof(RunFullDiagnostic), autoRunDelay);
            }
        }

        [ContextMenu("运行完整诊断")]
        public void RunFullDiagnostic()
        {
            Debug.Log("========== 运行时卡牌诊断开始 ==========\n");

            CheckEventSystem();
            CheckCanvas();
            CheckCardUISystem();
            CheckCards();
            CheckMouseInput();

            Debug.Log("\n========== 运行时卡牌诊断结束 ==========");
        }

        /// <summary>
        /// 检查 EventSystem
        /// </summary>
        private void CheckEventSystem()
        {
            Debug.Log("--- EventSystem 检查 ---");

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                Debug.Log($"✅ EventSystem 存在: {eventSystem.name}");
                Debug.Log($"  - Enabled: {eventSystem.enabled}");
                Debug.Log($"  - GameObject Active: {eventSystem.gameObject.activeInHierarchy}");
                
                // 检查是否有其他 EventSystem
                EventSystem[] allEventSystems = FindObjectsOfType<EventSystem>();
                if (allEventSystems.Length > 1)
                {
                    Debug.LogWarning($"⚠️ 场景中有 {allEventSystems.Length} 个 EventSystem！应该只有一个");
                    foreach (var es in allEventSystems)
                    {
                        Debug.Log($"  - {es.name} (Active: {es.gameObject.activeInHierarchy})");
                    }
                }
            }
            else
            {
                Debug.LogError("❌ EventSystem 不存在！");
            }
        }

        /// <summary>
        /// 检查 Canvas
        /// </summary>
        private void CheckCanvas()
        {
            Debug.Log("\n--- Canvas 检查 ---");

            Canvas[] canvases = FindObjectsOfType<Canvas>();
            Debug.Log($"找到 {canvases.Length} 个 Canvas");

            foreach (var canvas in canvases)
            {
                Debug.Log($"\nCanvas: {canvas.name}");
                Debug.Log($"  - Render Mode: {canvas.renderMode}");
                Debug.Log($"  - Sort Order: {canvas.sortingOrder}");
                Debug.Log($"  - Active: {canvas.gameObject.activeInHierarchy}");

                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    Debug.Log($"  - ✅ 有 GraphicRaycaster (Enabled: {raycaster.enabled})");
                }
                else
                {
                    Debug.LogWarning($"  - ⚠️ 缺少 GraphicRaycaster");
                }
            }
        }

        /// <summary>
        /// 检查 CardUISystem
        /// </summary>
        private void CheckCardUISystem()
        {
            Debug.Log("\n--- CardUISystem 检查 ---");

            CardUISystem uiSystem = FindObjectOfType<CardUISystem>();
            if (uiSystem != null)
            {
                Debug.Log($"✅ CardUISystem 存在: {uiSystem.name}");
                Debug.Log($"  - Enabled: {uiSystem.enabled}");
                Debug.Log($"  - GameObject Active: {uiSystem.gameObject.activeInHierarchy}");
            }
            else
            {
                Debug.LogWarning("⚠️ CardUISystem 不存在");
            }
        }

        /// <summary>
        /// 检查卡牌实例
        /// </summary>
        private void CheckCards()
        {
            Debug.Log("\n--- 卡牌实例检查 ---");

            CardHandUI[] cards = FindObjectsOfType<CardHandUI>();
            Debug.Log($"找到 {cards.Length} 个卡牌实例\n");

            if (cards.Length == 0)
            {
                Debug.LogWarning("⚠️ 没有找到任何卡牌实例！");
                return;
            }

            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                Debug.Log($"=== 卡牌 {i + 1}: {card.name} ===");

                // 基本信息
                Debug.Log($"  GameObject Active: {card.gameObject.activeInHierarchy}");
                Debug.Log($"  Component Enabled: {card.enabled}");

                // 检查组件
                RectTransform rt = card.GetComponent<RectTransform>();
                Image img = card.GetComponent<Image>();
                CanvasGroup cg = card.GetComponent<CanvasGroup>();
                Button btn = card.GetComponent<Button>();
                Canvas canvas = card.GetComponent<Canvas>();

                Debug.Log($"\n  组件检查:");
                Debug.Log($"    RectTransform: {rt != null}");
                Debug.Log($"    Image: {img != null}");
                Debug.Log($"    CanvasGroup: {cg != null}");
                Debug.Log($"    Button: {btn != null} {(btn != null ? "❌ 应该删除" : "✅")}");
                Debug.Log($"    Canvas: {canvas != null} {(canvas != null ? "❌ 应该删除" : "✅")}");

                // Image 详细检查
                if (img != null)
                {
                    Debug.Log($"\n  Image 详细:");
                    Debug.Log($"    Raycast Target: {img.raycastTarget} {(img.raycastTarget ? "✅" : "❌")}");
                    Debug.Log($"    Enabled: {img.enabled}");
                    Debug.Log($"    Color: {img.color}");
                    Debug.Log($"    Alpha: {img.color.a:F2} {(img.color.a > 0 ? "✅" : "❌")}");
                }

                // CanvasGroup 详细检查
                if (cg != null)
                {
                    Debug.Log($"\n  CanvasGroup 详细:");
                    Debug.Log($"    Interactable: {cg.interactable} {(cg.interactable ? "✅" : "❌")}");
                    Debug.Log($"    BlocksRaycasts: {cg.blocksRaycasts} {(cg.blocksRaycasts ? "✅" : "❌")}");
                    Debug.Log($"    Alpha: {cg.alpha:F2} {(cg.alpha > 0 ? "✅" : "❌")}");
                }

                // 检查父级 Canvas
                Canvas parentCanvas = card.GetComponentInParent<Canvas>();
                if (parentCanvas != null)
                {
                    Debug.Log($"\n  父级 Canvas: {parentCanvas.name}");
                    Debug.Log($"    Render Mode: {parentCanvas.renderMode}");
                    
                    GraphicRaycaster raycaster = parentCanvas.GetComponent<GraphicRaycaster>();
                    Debug.Log($"    有 GraphicRaycaster: {raycaster != null} {(raycaster != null ? "✅" : "❌")}");
                    
                    if (raycaster != null)
                    {
                        Debug.Log($"    GraphicRaycaster Enabled: {raycaster.enabled}");
                    }
                }
                else
                {
                    Debug.LogError("  ❌ 没有父级 Canvas！");
                }

                // 检查接口实现
                Debug.Log($"\n  接口实现:");
                Debug.Log($"    IBeginDragHandler: {card is IBeginDragHandler}");
                Debug.Log($"    IDragHandler: {card is IDragHandler}");
                Debug.Log($"    IEndDragHandler: {card is IEndDragHandler}");
                Debug.Log($"    IPointerEnterHandler: {card is IPointerEnterHandler}");
                Debug.Log($"    IPointerExitHandler: {card is IPointerExitHandler}");

                // 检查层级遮挡
                Debug.Log($"\n  层级信息:");
                Debug.Log($"    Sibling Index: {card.transform.GetSiblingIndex()}");
                Debug.Log($"    Parent: {card.transform.parent?.name}");
                
                Debug.Log(""); // 空行分隔
            }
        }

        /// <summary>
        /// 检查鼠标输入
        /// </summary>
        private void CheckMouseInput()
        {
            Debug.Log("\n--- 鼠标输入检查 ---");

            if (EventSystem.current == null)
            {
                Debug.LogError("❌ EventSystem 不存在，无法检查鼠标输入");
                return;
            }

            // 检查鼠标位置
            Vector2 mousePos = Input.mousePosition;
            Debug.Log($"鼠标位置: {mousePos}");

            // Raycast 检查
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = mousePos
            };

            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            Debug.Log($"\nRaycast 结果: 找到 {results.Count} 个对象");
            
            if (results.Count > 0)
            {
                Debug.Log("前 5 个对象:");
                for (int i = 0; i < Mathf.Min(5, results.Count); i++)
                {
                    var result = results[i];
                    Debug.Log($"  {i + 1}. {result.gameObject.name} (Distance: {result.distance:F2})");
                    
                    // 检查是否是卡牌
                    CardHandUI card = result.gameObject.GetComponent<CardHandUI>();
                    if (card != null)
                    {
                        Debug.Log($"     ✅ 这是一张卡牌！");
                    }
                }
            }
            else
            {
                Debug.LogWarning("⚠️ Raycast 没有检测到任何对象");
                Debug.LogWarning("可能的原因:");
                Debug.LogWarning("  1. 鼠标不在 UI 上");
                Debug.LogWarning("  2. Canvas 的 GraphicRaycaster 被禁用");
                Debug.LogWarning("  3. 所有 UI 的 Raycast Target 都未勾选");
            }
        }

        /// <summary>
        /// 测试特定卡牌的事件
        /// </summary>
        [ContextMenu("测试第一张卡牌的事件")]
        public void TestFirstCardEvents()
        {
            CardHandUI[] cards = FindObjectsOfType<CardHandUI>();
            if (cards.Length == 0)
            {
                Debug.LogWarning("没有找到任何卡牌");
                return;
            }

            var card = cards[0];
            Debug.Log($"测试卡牌: {card.name}");

            // 模拟 PointerEnter
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            
            if (card is IPointerEnterHandler enterHandler)
            {
                Debug.Log("调用 OnPointerEnter...");
                enterHandler.OnPointerEnter(eventData);
            }
            else
            {
                Debug.LogError("卡牌没有实现 IPointerEnterHandler");
            }
        }
    }
}
