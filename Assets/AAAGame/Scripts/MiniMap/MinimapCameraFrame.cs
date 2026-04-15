using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

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
        [SerializeField] private float borderWidth = 2f; // 边框宽度
        [SerializeField] private bool hollowCenter = true; // 中间镂空（推荐）
        
        [Header("边界限制")]
        [SerializeField] private bool clampToBounds = true;
        [SerializeField] private RectTransform minimapBounds; // 小地图边界
        
        private RectTransform rectTransform;
        private Image image;
        private Outline outline;
        
        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            
            // 确保 Image 组件正确配置
            InitializeImage();
            
            // 添加 Outline 组件（白色边框）
            InitializeOutline();
            
            Log.Info($"[MinimapCameraFrame] Initialized at {gameObject.name}");
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
                Log.Info($"[MinimapCameraFrame] Image configured: HOLLOW (transparent center)");
            }
            else
            {
                // 半透明填充效果
                Color color = borderColor;
                color.a = 0.2f; // 半透明填充
                image.color = color;
                Log.Info($"[MinimapCameraFrame] Image configured: color={image.color}");
            }
            
            // 禁用 Raycast（不阻挡鼠标事件）
            image.raycastTarget = false;
        }
        
        private void InitializeOutline()
        {
            // 移除旧的 Outline
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                Destroy(outline);
            }
            
            // 添加新的 Outline（边框）
            outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, borderAlpha);
            outline.effectDistance = new Vector2(borderWidth, borderWidth);
            outline.useGraphicAlpha = false; // 不使用 Image 的 Alpha，独立控制
            
            Log.Info($"[MinimapCameraFrame] Outline configured: color={outline.effectColor}, width={borderWidth}");
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
            
            Log.Info($"[MinimapCameraFrame] Started, bounds={(minimapBounds != null ? minimapBounds.name : "none")}");
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
            
            if (outline != null)
            {
                outline.effectColor = new Color(color.r, color.g, color.b, borderAlpha);
            }
            
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
            
            if (outline != null)
            {
                Color color = outline.effectColor;
                color.a = alpha;
                outline.effectColor = color;
            }
        }
        
        /// <summary>
        /// 设置边框宽度
        /// </summary>
        public void SetBorderWidth(float width)
        {
            borderWidth = width;
            
            if (outline != null)
            {
                outline.effectDistance = new Vector2(width, width);
            }
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
            
            if (outline != null)
            {
                outline.enabled = false;
                outline.enabled = true;
            }
            
            Log.Info($"[MinimapCameraFrame] Force refreshed, hollow={hollowCenter}, active={gameObject.activeSelf}");
        }
        
        private void OnEnable()
        {
            Log.Info($"[MinimapCameraFrame] OnEnable called");
            ForceRefresh();
        }
        
        private void OnValidate()
        {
            // 在 Editor 中修改参数时自动更新
            if (Application.isPlaying)
            {
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
                
                if (outline != null)
                {
                    outline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, borderAlpha);
                    outline.effectDistance = new Vector2(borderWidth, borderWidth);
                }
            }
        }
    }
}
