using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AAAGame.Card.UI;

namespace AAAGame.Card.Test
{
    /// <summary>
    /// 卡牌交互诊断工具
    /// 用于检查卡牌预制体是否正确配置
    /// </summary>
    public class CardInteractionDiagnostic : MonoBehaviour
    {
        [Header("要检查的卡牌预制体")]
        [SerializeField] private GameObject cardPrefab;

        [Header("场景检查")]
        [SerializeField] private bool checkSceneSetup = true;

        [ContextMenu("运行诊断")]
        public void RunDiagnostic()
        {
            Debug.Log("========== 卡牌交互诊断开始 ==========");

            if (checkSceneSetup)
            {
                CheckSceneSetup();
            }

            if (cardPrefab != null)
            {
                CheckCardPrefab();
            }
            else
            {
                Debug.LogError("❌ 未配置卡牌预制体！请在 Inspector 中指定 cardPrefab");
            }

            Debug.Log("========== 卡牌交互诊断结束 ==========");
        }

        /// <summary>
        /// 检查场景配置
        /// </summary>
        private void CheckSceneSetup()
        {
            Debug.Log("\n--- 场景配置检查 ---");

            // 检查 EventSystem
            EventSystem eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem != null)
            {
                Debug.Log("✅ EventSystem 存在");
            }
            else
            {
                Debug.LogError("❌ 场景中没有 EventSystem！请添加：GameObject > UI > Event System");
            }

            // 检查 Canvas
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            if (canvases.Length > 0)
            {
                Debug.Log($"✅ 找到 {canvases.Length} 个 Canvas");

                foreach (var canvas in canvases)
                {
                    // 检查 GraphicRaycaster
                    GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                    if (raycaster != null)
                    {
                        Debug.Log($"  ✅ Canvas '{canvas.name}' 有 GraphicRaycaster");
                    }
                    else
                    {
                        Debug.LogWarning($"  ⚠️ Canvas '{canvas.name}' 缺少 GraphicRaycaster");
                    }

                    // 检查 Render Mode
                    Debug.Log($"  - Render Mode: {canvas.renderMode}");
                }
            }
            else
            {
                Debug.LogError("❌ 场景中没有 Canvas！");
            }

            // 检查 CardUISystem
            CardUISystem uiSystem = FindObjectOfType<CardUISystem>();
            if (uiSystem != null)
            {
                Debug.Log("✅ CardUISystem 存在");
            }
            else
            {
                Debug.LogWarning("⚠️ 场景中没有 CardUISystem");
            }
        }

        /// <summary>
        /// 检查卡牌预制体配置
        /// </summary>
        private void CheckCardPrefab()
        {
            Debug.Log("\n--- 卡牌预制体检查 ---");
            Debug.Log($"预制体名称: {cardPrefab.name}");

            // 检查 RectTransform
            RectTransform rectTransform = cardPrefab.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Debug.Log("✅ 有 RectTransform 组件");
            }
            else
            {
                Debug.LogError("❌ 缺少 RectTransform 组件！这是 UI 元素必需的");
            }

            // 检查 Image
            Image image = cardPrefab.GetComponent<Image>();
            if (image != null)
            {
                Debug.Log("✅ 有 Image 组件");

                if (image.raycastTarget)
                {
                    Debug.Log("  ✅ Raycast Target 已勾选");
                }
                else
                {
                    Debug.LogError("  ❌ Raycast Target 未勾选！卡牌无法接收鼠标事件");
                }

                Color color = image.color;
                if (color.a > 0)
                {
                    Debug.Log($"  ✅ Image Alpha: {color.a:F2}");
                }
                else
                {
                    Debug.LogWarning("  ⚠️ Image Alpha 为 0，卡牌不可见");
                }
            }
            else
            {
                Debug.LogError("❌ 缺少 Image 组件！卡牌无法接收鼠标事件");
                Debug.LogError("   修复方法：Add Component > UI > Image，并勾选 Raycast Target");
            }

            // 检查 CardHandUI
            CardHandUI cardUI = cardPrefab.GetComponent<CardHandUI>();
            if (cardUI != null)
            {
                Debug.Log("✅ 有 CardHandUI 脚本");

                // 检查接口实现
                bool hasBeginDrag = cardUI is IBeginDragHandler;
                bool hasDrag = cardUI is IDragHandler;
                bool hasEndDrag = cardUI is IEndDragHandler;
                bool hasPointerEnter = cardUI is IPointerEnterHandler;
                bool hasPointerExit = cardUI is IPointerExitHandler;

                if (hasBeginDrag && hasDrag && hasEndDrag)
                {
                    Debug.Log("  ✅ 实现了拖拽接口 (IBeginDragHandler, IDragHandler, IEndDragHandler)");
                }
                else
                {
                    Debug.LogError("  ❌ 未正确实现拖拽接口");
                }

                if (hasPointerEnter && hasPointerExit)
                {
                    Debug.Log("  ✅ 实现了悬停接口 (IPointerEnterHandler, IPointerExitHandler)");
                }
                else
                {
                    Debug.LogError("  ❌ 未正确实现悬停接口");
                }
            }
            else
            {
                Debug.LogError("❌ 缺少 CardHandUI 脚本！");
                Debug.LogError("   修复方法：Add Component > CardHandUI");
            }

            // 检查 CanvasGroup
            CanvasGroup canvasGroup = cardPrefab.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                Debug.Log("✅ 有 CanvasGroup 组件");

                if (canvasGroup.interactable)
                {
                    Debug.Log("  ✅ Interactable 已勾选");
                }
                else
                {
                    Debug.LogWarning("  ⚠️ Interactable 未勾选，卡牌可能无法交互");
                }

                if (canvasGroup.blocksRaycasts)
                {
                    Debug.Log("  ✅ Block Raycasts 已勾选");
                }
                else
                {
                    Debug.LogWarning("  ⚠️ Block Raycasts 未勾选");
                }
            }
            else
            {
                Debug.Log("⚠️ 没有 CanvasGroup 组件（会在运行时自动添加）");
            }

            // 检查子对象
            Debug.Log("\n子对象检查:");
            int childCount = cardPrefab.transform.childCount;
            Debug.Log($"  子对象数量: {childCount}");

            for (int i = 0; i < childCount; i++)
            {
                Transform child = cardPrefab.transform.GetChild(i);
                Debug.Log($"  - {child.name}");
            }
        }

        /// <summary>
        /// 运行时诊断（检查实例化的卡牌）
        /// </summary>
        [ContextMenu("运行时诊断（需要运行游戏）")]
        public void RuntimeDiagnostic()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("请先运行游戏再执行运行时诊断");
                return;
            }

            Debug.Log("========== 运行时诊断开始 ==========");

            // 查找所有 CardHandUI 实例
            CardHandUI[] cards = FindObjectsOfType<CardHandUI>();
            Debug.Log($"\n找到 {cards.Length} 个卡牌实例");

            foreach (var card in cards)
            {
                Debug.Log($"\n--- 卡牌: {card.name} ---");

                // 检查组件
                RectTransform rt = card.GetComponent<RectTransform>();
                Image img = card.GetComponent<Image>();
                CanvasGroup cg = card.GetComponent<CanvasGroup>();

                Debug.Log($"  RectTransform: {rt != null}");
                Debug.Log($"  Image: {img != null}");
                Debug.Log($"  CanvasGroup: {cg != null}");

                if (img != null)
                {
                    Debug.Log($"  Raycast Target: {img.raycastTarget}");
                    Debug.Log($"  Image Alpha: {img.color.a:F2}");
                }

                if (cg != null)
                {
                    Debug.Log($"  Interactable: {cg.interactable}");
                    Debug.Log($"  Block Raycasts: {cg.blocksRaycasts}");
                    Debug.Log($"  Alpha: {cg.alpha:F2}");
                }

                // 检查父级 Canvas
                Canvas parentCanvas = card.GetComponentInParent<Canvas>();
                if (parentCanvas != null)
                {
                    Debug.Log($"  父级 Canvas: {parentCanvas.name}");
                    GraphicRaycaster raycaster = parentCanvas.GetComponent<GraphicRaycaster>();
                    Debug.Log($"  Canvas 有 GraphicRaycaster: {raycaster != null}");
                }
                else
                {
                    Debug.LogError("  ❌ 没有父级 Canvas！");
                }
            }

            Debug.Log("========== 运行时诊断结束 ==========");
        }
    }
}
