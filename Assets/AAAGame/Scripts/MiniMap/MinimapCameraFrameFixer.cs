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

            // 检查 1: 父对象是否正确
            if (cameraViewFrame.parent != minimapContainer)
            {
                cameraViewFrame.SetParent(minimapContainer, false); // false = 保持本地坐标
            }

            // 检查 2: RectTransform 配置
            FixRectTransform();

            // 检查 3: Image 组件
            FixImageComponent();

            // 检查 4: 层级顺序
            FixSiblingIndex();

        }

        private void FixRectTransform()
        {
            // 设置正确的锚点和轴点
            if (cameraViewFrame.anchorMin != new Vector2(0.5f, 0.5f) ||
                cameraViewFrame.anchorMax != new Vector2(0.5f, 0.5f))
            {
                cameraViewFrame.anchorMin = new Vector2(0.5f, 0.5f);
                cameraViewFrame.anchorMax = new Vector2(0.5f, 0.5f);
            }

            if (cameraViewFrame.pivot != new Vector2(0.5f, 0.5f))
            {
                cameraViewFrame.pivot = new Vector2(0.5f, 0.5f);
            }

            if (cameraViewFrame.localScale != Vector3.one)
            {
                cameraViewFrame.localScale = Vector3.one;
            }

            if (cameraViewFrame.localRotation != Quaternion.identity)
            {
                cameraViewFrame.localRotation = Quaternion.identity;
            }
        }

        private void FixImageComponent()
        {
            MinimapCameraFrame newCameraFrame = cameraViewFrame.GetComponent<MinimapCameraFrame>();
            if (newCameraFrame != null)
            {
                return;
            }

            Image image = cameraViewFrame.GetComponent<Image>();
            if (image == null)
            {
                image = cameraViewFrame.gameObject.AddComponent<Image>();
            }

            // 检查颜色
            if (image.color.a < 0.1f)
            {
                Color color = Color.white;
                color.a = 0.3f; // 半透明
                image.color = color;
            }

            // 添加 Outline
            Outline outline = cameraViewFrame.GetComponent<Outline>();
            if (outline == null)
            {
                outline = cameraViewFrame.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2, 2);
            }
        }

        private void FixSiblingIndex()
        {
            // 确保在最后（最上层）
            int currentIndex = cameraViewFrame.GetSiblingIndex();
            int lastIndex = minimapContainer.childCount - 1;

            if (currentIndex != lastIndex)
            {
                cameraViewFrame.SetAsLastSibling();
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
