using UnityEngine;
using UnityEngine.UI;

namespace AAAGame.MiniMap
{
    /// <summary>
    /// 小地图摄像机视野框组件
    /// 独立管理视野框的显示，确保可见性和边界限制
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class MinimapCameraFrame : MonoBehaviour
    {
        [Header("视野框样式")]
        [SerializeField] private Color borderColor = Color.white; // 边框颜色
        [SerializeField] private float borderAlpha = 1f; // 边框透明度
        [SerializeField] private float borderWidth = 1f; // 边框宽度
        [SerializeField] private bool hollowCenter = true; // 中间镂空（推荐）
        
        [Header("边界限制")]
        [SerializeField] private bool clampToBounds = true;
        [SerializeField] private RectTransform minimapBounds; // 小地图边界
        
        private RectTransform rectTransform;
        private Image image;
        private Image topEdgeImage;
        private Image bottomEdgeImage;
        private Image leftEdgeImage;
        private Image rightEdgeImage;
        
        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            
            // 确保 Image 组件正确配置
            InitializeImage();

            // 使用四条边线绘制空心框，避免 Outline 把整块区域染色
            InitializeBorderEdges();
        }
        
        private void InitializeImage()
        {
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
            }
            
            if (hollowCenter)
            {
                // 镂空效果：中间完全透明
                Color color = Color.clear; // 完全透明
                image.color = color;
            }
            else
            {
                // 半透明填充效果
                Color color = borderColor;
                color.a = 0.2f; // 半透明填充
                image.color = color;
            }
            
            // 禁用 Raycast（不阻挡鼠标事件）
            image.raycastTarget = false;
        }

        private void InitializeBorderEdges()
        {
            RemoveLegacyOutlines();

            topEdgeImage = EnsureEdgeImage("Top");
            bottomEdgeImage = EnsureEdgeImage("Bottom");
            leftEdgeImage = EnsureEdgeImage("Left");
            rightEdgeImage = EnsureEdgeImage("Right");

            ApplyBorderStyle();
        }

        private void RemoveLegacyOutlines()
        {
            var outlines = GetComponents<Outline>();
            for (int i = 0; i < outlines.Length; i++)
            {
                if (Application.isPlaying)
                {
                    Destroy(outlines[i]);
                }
                else
                {
                    DestroyImmediate(outlines[i]);
                }
            }
        }

        private Image EnsureEdgeImage(string edgeName)
        {
            Transform edgeTransform = transform.Find(edgeName);
            GameObject edgeObject;
            if (edgeTransform == null)
            {
                edgeObject = new GameObject(edgeName, typeof(RectTransform), typeof(Image));
                edgeObject.transform.SetParent(transform, false);
            }
            else
            {
                edgeObject = edgeTransform.gameObject;
            }

            Image edgeImage = edgeObject.GetComponent<Image>();
            if (edgeImage == null)
            {
                edgeImage = edgeObject.AddComponent<Image>();
            }

            edgeImage.raycastTarget = false;
            return edgeImage;
        }

        private void ApplyBorderStyle()
        {
            if (!TryCacheBorderEdgeImages())
            {
                return;
            }

            float thickness = Mathf.Max(0f, borderWidth);
            Color edgeColor = new Color(borderColor.r, borderColor.g, borderColor.b, borderAlpha);

            LayoutHorizontalEdge(topEdgeImage.rectTransform, true, thickness);
            LayoutHorizontalEdge(bottomEdgeImage.rectTransform, false, thickness);
            LayoutVerticalEdge(leftEdgeImage.rectTransform, true, thickness);
            LayoutVerticalEdge(rightEdgeImage.rectTransform, false, thickness);

            topEdgeImage.color = edgeColor;
            bottomEdgeImage.color = edgeColor;
            leftEdgeImage.color = edgeColor;
            rightEdgeImage.color = edgeColor;

            bool active = thickness > 0f;
            topEdgeImage.enabled = active;
            bottomEdgeImage.enabled = active;
            leftEdgeImage.enabled = active;
            rightEdgeImage.enabled = active;
        }

        private bool TryCacheBorderEdgeImages()
        {
            if (topEdgeImage == null)
            {
                topEdgeImage = FindEdgeImage("Top");
            }

            if (bottomEdgeImage == null)
            {
                bottomEdgeImage = FindEdgeImage("Bottom");
            }

            if (leftEdgeImage == null)
            {
                leftEdgeImage = FindEdgeImage("Left");
            }

            if (rightEdgeImage == null)
            {
                rightEdgeImage = FindEdgeImage("Right");
            }

            return topEdgeImage != null && bottomEdgeImage != null && leftEdgeImage != null && rightEdgeImage != null;
        }

        private Image FindEdgeImage(string edgeName)
        {
            Transform edgeTransform = transform.Find(edgeName);
            return edgeTransform != null ? edgeTransform.GetComponent<Image>() : null;
        }

        private void LayoutHorizontalEdge(RectTransform edgeRect, bool top, float thickness)
        {
            float anchorY = top ? 1f : 0f;
            float pivotY = top ? 1f : 0f;
            edgeRect.anchorMin = new Vector2(0f, anchorY);
            edgeRect.anchorMax = new Vector2(1f, anchorY);
            edgeRect.pivot = new Vector2(0.5f, pivotY);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = new Vector2(0f, thickness);
        }

        private void LayoutVerticalEdge(RectTransform edgeRect, bool left, float thickness)
        {
            float anchorX = left ? 0f : 1f;
            float pivotX = left ? 0f : 1f;
            edgeRect.anchorMin = new Vector2(anchorX, 0f);
            edgeRect.anchorMax = new Vector2(anchorX, 1f);
            edgeRect.pivot = new Vector2(pivotX, 0.5f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = new Vector2(thickness, 0f);
        }
        
        private void Start()
        {
            // 确保在最上层
            transform.SetAsLastSibling();
            
            // 如果没有指定边界，尝试自动查找
            if (minimapBounds == null && transform.parent != null)
            {
                minimapBounds = transform.parent.GetComponent<RectTransform>();
            }
        }
        
        /// <summary>
        /// 更新视野框位置和大小（由 MinimapUI 调用）
        /// </summary>
        public void UpdateFrame(Vector2 position, Vector2 size)
        {
            if (rectTransform == null) return;
            
            // 限制大小（不超过小地图边界）
            if (clampToBounds && minimapBounds != null)
            {
                Vector2 maxSize = minimapBounds.rect.size;
                size.x = Mathf.Min(size.x, maxSize.x);
                size.y = Mathf.Min(size.y, maxSize.y);
            }
            
            // 限制位置（不超出小地图边界）
            if (clampToBounds && minimapBounds != null)
            {
                Vector2 halfSize = size * 0.5f;
                Vector2 boundsHalfSize = minimapBounds.rect.size * 0.5f;
                
                position.x = Mathf.Clamp(position.x, -boundsHalfSize.x + halfSize.x, boundsHalfSize.x - halfSize.x);
                position.y = Mathf.Clamp(position.y, -boundsHalfSize.y + halfSize.y, boundsHalfSize.y - halfSize.y);
            }
            
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;
        }
        
        /// <summary>
        /// 设置边框颜色（不影响中间填充）
        /// </summary>
        public void SetBorderColor(Color color)
        {
            borderColor = color;
            ApplyBorderStyle();
            
            // 如果不是镂空模式，也更新 Image 颜色
            if (!hollowCenter && image != null)
            {
                Color fillColor = color;
                fillColor.a = 0.2f; // 半透明填充
                image.color = fillColor;
            }
        }
        
        /// <summary>
        /// 设置边框透明度
        /// </summary>
        public void SetBorderAlpha(float alpha)
        {
            borderAlpha = alpha;
            ApplyBorderStyle();
        }
        
        /// <summary>
        /// 设置边框宽度
        /// </summary>
        public void SetBorderWidth(float width)
        {
            borderWidth = width;
            ApplyBorderStyle();
        }
        
        /// <summary>
        /// 设置是否镂空
        /// </summary>
        public void SetHollow(bool hollow)
        {
            hollowCenter = hollow;
            
            if (image != null)
            {
                if (hollow)
                {
                    // 镂空：完全透明
                    image.color = Color.clear;
                }
                else
                {
                    // 半透明填充
                    Color color = borderColor;
                    color.a = 0.2f;
                    image.color = color;
                }
            }

            ApplyBorderStyle();
        }
        
        /// <summary>
        /// 强制刷新显示
        /// </summary>
        public void ForceRefresh()
        {
            if (image != null)
            {
                image.enabled = false;
                image.enabled = true;
            }

            if (topEdgeImage != null)
            {
                topEdgeImage.enabled = false;
                topEdgeImage.enabled = borderWidth > 0f;
            }

            if (bottomEdgeImage != null)
            {
                bottomEdgeImage.enabled = false;
                bottomEdgeImage.enabled = borderWidth > 0f;
            }

            if (leftEdgeImage != null)
            {
                leftEdgeImage.enabled = false;
                leftEdgeImage.enabled = borderWidth > 0f;
            }

            if (rightEdgeImage != null)
            {
                rightEdgeImage.enabled = false;
                rightEdgeImage.enabled = borderWidth > 0f;
            }
        }
        
        private void OnEnable()
        {
            ForceRefresh();
        }
        
        private void OnValidate()
        {
            // 在 Editor 中修改参数时自动更新
            if (Application.isPlaying)
            {
                if (image == null)
                {
                    image = GetComponent<Image>();
                }

                if (image != null)
                {
                    if (hollowCenter)
                    {
                        image.color = Color.clear; // 镂空
                    }
                    else
                    {
                        Color color = borderColor;
                        color.a = 0.2f; // 半透明填充
                        image.color = color;
                    }
                }

                ApplyBorderStyle();
            }
        }
    }
}
