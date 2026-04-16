using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图摄像机视野框修复工具
    /// 自动检查并修复视野框的父对象和配置问题
    /// </summary>
    public class MinimapCameraFrameFixer : MonoBehaviour
    {
        [Header("自动修复")]
        [SerializeField] private bool autoFixOnStart = true;

        [Header("引用")]
        [SerializeField] private RectTransform minimapContainer;
        [SerializeField] private RectTransform cameraViewFrame;

        private void Start()
        {
            if (autoFixOnStart)
            {
                FixCameraFrame();
            }
        }

        [ContextMenu("修复摄像机视野框")]
        public void FixCameraFrame()
        {
            if (cameraViewFrame == null)
            {
                Log.Error("[MinimapFixer] CameraViewFrame is null! Please assign it in Inspector.");
                return;
            }

            if (minimapContainer == null)
            {
                Log.Error("[MinimapFixer] MinimapContainer is null! Please assign it in Inspector.");
                return;
            }

            Log.Info($"[MinimapFixer] Starting fix...");
            Log.Info($"[MinimapFixer] CameraViewFrame current parent: {(cameraViewFrame.parent != null ? cameraViewFrame.parent.name : "null")}");
            Log.Info($"[MinimapFixer] MinimapContainer: {minimapContainer.name}");

            // 检查 1: 父对象是否正确
            if (cameraViewFrame.parent != minimapContainer)
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong parent! Moving CameraViewFrame to MinimapContainer...");
                cameraViewFrame.SetParent(minimapContainer, false); // false = 保持本地坐标
                Log.Info($"[MinimapFixer] ✅ Parent fixed!");
            }
            else
            {
                Log.Info($"[MinimapFixer] ✅ Parent is correct");
            }

            // 检查 2: RectTransform 配置
            FixRectTransform();

            // 检查 3: Image 组件
            FixImageComponent();

            // 检查 4: 层级顺序
            FixSiblingIndex();

            Log.Info($"[MinimapFixer] Fix completed!");
        }

        private void FixRectTransform()
        {
            // 设置正确的锚点和轴点
            if (cameraViewFrame.anchorMin != new Vector2(0.5f, 0.5f) ||
                cameraViewFrame.anchorMax != new Vector2(0.5f, 0.5f))
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong anchors! Fixing...");
                cameraViewFrame.anchorMin = new Vector2(0.5f, 0.5f);
                cameraViewFrame.anchorMax = new Vector2(0.5f, 0.5f);
                Log.Info($"[MinimapFixer] ✅ Anchors fixed to center");
            }

            if (cameraViewFrame.pivot != new Vector2(0.5f, 0.5f))
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong pivot! Fixing...");
                cameraViewFrame.pivot = new Vector2(0.5f, 0.5f);
                Log.Info($"[MinimapFixer] ✅ Pivot fixed to center");
            }

            if (cameraViewFrame.localScale != Vector3.one)
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong scale! Fixing...");
                cameraViewFrame.localScale = Vector3.one;
                Log.Info($"[MinimapFixer] ✅ Scale fixed to (1,1,1)");
            }

            if (cameraViewFrame.localRotation != Quaternion.identity)
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong rotation! Fixing...");
                cameraViewFrame.localRotation = Quaternion.identity;
                Log.Info($"[MinimapFixer] ✅ Rotation fixed to (0,0,0)");
            }
        }

        private void FixImageComponent()
        {
            MinimapCameraFrame newCameraFrame = cameraViewFrame.GetComponent<MinimapCameraFrame>();
            if (newCameraFrame != null)
            {
                Log.Info("[MinimapFixer] Detected MinimapCameraFrame, skipping legacy alpha/outline fixes.");
                return;
            }

            Image image = cameraViewFrame.GetComponent<Image>();
            if (image == null)
            {
                Log.Warning($"[MinimapFixer] ❌ No Image component! Adding...");
                image = cameraViewFrame.gameObject.AddComponent<Image>();
                Log.Info($"[MinimapFixer] ✅ Image component added");
            }

            // 检查颜色
            if (image.color.a < 0.1f)
            {
                Log.Warning($"[MinimapFixer] ❌ Image alpha too low ({image.color.a})! Fixing...");
                Color color = Color.white;
                color.a = 0.3f; // 半透明
                image.color = color;
                Log.Info($"[MinimapFixer] ✅ Image color fixed to white with alpha=0.3");
            }

            // 添加 Outline
            Outline outline = cameraViewFrame.GetComponent<Outline>();
            if (outline == null)
            {
                Log.Warning($"[MinimapFixer] ❌ No Outline component! Adding...");
                outline = cameraViewFrame.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2, 2);
                Log.Info($"[MinimapFixer] ✅ Outline component added");
            }
        }

        private void FixSiblingIndex()
        {
            // 确保在最后（最上层）
            int currentIndex = cameraViewFrame.GetSiblingIndex();
            int lastIndex = minimapContainer.childCount - 1;

            if (currentIndex != lastIndex)
            {
                Log.Warning($"[MinimapFixer] ❌ Wrong sibling index ({currentIndex}/{lastIndex})! Moving to last...");
                cameraViewFrame.SetAsLastSibling();
                Log.Info($"[MinimapFixer] ✅ Moved to last sibling");
            }
            else
            {
                Log.Info($"[MinimapFixer] ✅ Sibling index is correct (last)");
            }
        }

        [ContextMenu("打印调试信息")]
        public void PrintDebugInfo()
        {
            if (cameraViewFrame == null)
            {
                Log.Error("[MinimapFixer] CameraViewFrame is null!");
                return;
            }

            Log.Info($"========== CameraViewFrame Debug Info ==========");
            Log.Info($"Name: {cameraViewFrame.name}");
            Log.Info($"Parent: {(cameraViewFrame.parent != null ? cameraViewFrame.parent.name : "null")}");
            Log.Info($"Active: {cameraViewFrame.gameObject.activeSelf}");
            Log.Info($"Position: {cameraViewFrame.anchoredPosition}");
            Log.Info($"Size: {cameraViewFrame.sizeDelta}");
            Log.Info($"Anchors: Min={cameraViewFrame.anchorMin}, Max={cameraViewFrame.anchorMax}");
            Log.Info($"Pivot: {cameraViewFrame.pivot}");
            Log.Info($"Scale: {cameraViewFrame.localScale}");
            Log.Info($"Rotation: {cameraViewFrame.localRotation.eulerAngles}");
            Log.Info($"Sibling Index: {cameraViewFrame.GetSiblingIndex()}/{(cameraViewFrame.parent != null ? cameraViewFrame.parent.childCount - 1 : 0)}");

            Image image = cameraViewFrame.GetComponent<Image>();
            if (image != null)
            {
                Log.Info($"Image Color: {image.color}");
                Log.Info($"Image Enabled: {image.enabled}");
            }
            else
            {
                Log.Warning($"No Image component!");
            }

            Outline outline = cameraViewFrame.GetComponent<Outline>();
            if (outline != null)
            {
                Log.Info($"Outline Color: {outline.effectColor}");
                Log.Info($"Outline Distance: {outline.effectDistance}");
            }
            else
            {
                Log.Warning($"No Outline component!");
            }

            Log.Info($"================================================");
        }
    }
}
