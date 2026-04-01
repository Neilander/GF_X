using UnityEngine;
using UnityEngine.EventSystems;

namespace AAAGame.Card.Test
{
    /// <summary>
    /// 场景配置检查工具 - 检查卡牌系统所需的场景配置
    /// </summary>
    public class SceneSetupChecker : MonoBehaviour
    {
        [Header("自动检查")]
        [SerializeField] private bool checkOnStart = true;

        void Start()
        {
            if (checkOnStart)
            {
                CheckSceneSetup();
            }
        }

        [ContextMenu("检查场景配置")]
        public void CheckSceneSetup()
        {
            Debug.Log("========== 场景配置检查 ==========");

            CheckCamera();
            CheckEventSystem();
            CheckGroundCollider();
            CheckCardUISystem();
            CheckAreas();

            Debug.Log("========== 检查完成 ==========");
        }

        private void CheckCamera()
        {
            Debug.Log("\n--- 检查 Camera ---");

            if (Camera.main == null)
            {
                Debug.LogError("❌ Camera.main 不存在！");
                Debug.LogError("   解决方案：确保场景中有一个 Camera，且 Tag 设置为 'MainCamera'");
            }
            else
            {
                Debug.Log($"✅ Camera.main 存在: {Camera.main.name}");
                Debug.Log($"   位置: {Camera.main.transform.position}");
                Debug.Log($"   旋转: {Camera.main.transform.eulerAngles}");
            }
        }

        private void CheckEventSystem()
        {
            Debug.Log("\n--- 检查 EventSystem ---");

            var eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                Debug.LogError("❌ EventSystem 不存在！");
                Debug.LogError("   解决方案：GameObject > UI > Event System");
            }
            else
            {
                Debug.Log($"✅ EventSystem 存在: {eventSystem.name}");
            }
        }

        private void CheckGroundCollider()
        {
            Debug.Log("\n--- 检查地面 Collider ---");

            var colliders = FindObjectsOfType<Collider>();
            if (colliders.Length == 0)
            {
                Debug.LogError("❌ 场景中没有任何 Collider！");
                Debug.LogError("   解决方案：");
                Debug.LogError("   1. 创建地面：3D Object > Plane");
                Debug.LogError("   2. 确保地面有 Mesh Collider 组件");
                Debug.LogError("   3. 位置建议：(0, 0, 0)，缩放：(10, 1, 10)");
            }
            else
            {
                Debug.Log($"✅ 找到 {colliders.Length} 个 Collider:");
                foreach (var collider in colliders)
                {
                    Debug.Log($"   - {collider.name} ({collider.GetType().Name})");
                }
            }
        }

        private void CheckCardUISystem()
        {
            Debug.Log("\n--- 检查 CardUISystem ---");

            var cardUISystem = FindObjectOfType<UI.CardUISystem>();
            if (cardUISystem == null)
            {
                Debug.LogError("❌ CardUISystem 不存在！");
                Debug.LogError("   解决方案：在 Canvas 下添加 CardUISystem 组件");
            }
            else
            {
                Debug.Log($"✅ CardUISystem 存在: {cardUISystem.name}");
                
                // 使用反射检查私有字段
                var type = cardUISystem.GetType();
                
                var validAreaField = type.GetField("validArea", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var forbiddenAreaField = type.GetField("forbiddenArea", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (validAreaField != null)
                {
                    var validArea = validAreaField.GetValue(cardUISystem) as Transform;
                    if (validArea == null)
                    {
                        Debug.LogWarning("⚠️ ValidArea 未配置（可选）");
                    }
                    else
                    {
                        Debug.Log($"✅ ValidArea 已配置: {validArea.name}");
                        CheckAreaColliders(validArea, "ValidArea");
                    }
                }
                
                if (forbiddenAreaField != null)
                {
                    var forbiddenArea = forbiddenAreaField.GetValue(cardUISystem) as Transform;
                    if (forbiddenArea == null)
                    {
                        Debug.LogWarning("⚠️ ForbiddenArea 未配置（可选）");
                    }
                    else
                    {
                        Debug.Log($"✅ ForbiddenArea 已配置: {forbiddenArea.name}");
                        CheckAreaColliders(forbiddenArea, "ForbiddenArea");
                    }
                }
            }
        }

        private void CheckAreas()
        {
            Debug.Log("\n--- 检查区域配置 ---");

            var validArea = GameObject.Find("ValidArea");
            if (validArea != null)
            {
                Debug.Log($"✅ 找到 ValidArea: {validArea.name}");
                CheckAreaColliders(validArea.transform, "ValidArea");
            }
            else
            {
                Debug.LogWarning("⚠️ 未找到 ValidArea 对象（可选）");
            }

            var forbiddenArea = GameObject.Find("ForbiddenArea");
            if (forbiddenArea != null)
            {
                Debug.Log($"✅ 找到 ForbiddenArea: {forbiddenArea.name}");
                CheckAreaColliders(forbiddenArea.transform, "ForbiddenArea");
            }
            else
            {
                Debug.LogWarning("⚠️ 未找到 ForbiddenArea 对象（可选）");
            }
        }

        private void CheckAreaColliders(Transform area, string areaName)
        {
            var colliders = area.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                Debug.LogWarning($"⚠️ {areaName} 没有 Collider 组件");
                Debug.LogWarning($"   解决方案：在 {areaName} 下添加子对象（Cube），并确保有 Collider");
            }
            else
            {
                Debug.Log($"   {areaName} 有 {colliders.Length} 个 Collider:");
                foreach (var collider in colliders)
                {
                    Debug.Log($"   - {collider.name} ({collider.GetType().Name})");
                }
            }
        }

        [ContextMenu("生成推荐配置")]
        public void GenerateRecommendedSetup()
        {
            Debug.Log("========== 生成推荐配置 ==========");

            // 创建地面
            if (GameObject.Find("Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(10, 1, 10);
                Debug.Log("✅ 创建了 Ground (Plane)");
            }

            // 创建 ValidArea
            if (GameObject.Find("ValidArea") == null)
            {
                var validArea = new GameObject("ValidArea");
                var validCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                validCube.name = "ValidZone";
                validCube.transform.SetParent(validArea.transform);
                validCube.transform.localPosition = new Vector3(0, 0.5f, 5);
                validCube.transform.localScale = new Vector3(5, 1, 5);
                
                // 设置半透明绿色材质
                var renderer = validCube.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = new Color(0, 1, 0, 0.3f);
                    mat.SetFloat("_Mode", 3); // Transparent
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                }
                
                Debug.Log("✅ 创建了 ValidArea");
            }

            // 创建 ForbiddenArea
            if (GameObject.Find("ForbiddenArea") == null)
            {
                var forbiddenArea = new GameObject("ForbiddenArea");
                var forbiddenCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                forbiddenCube.name = "ForbiddenZone";
                forbiddenCube.transform.SetParent(forbiddenArea.transform);
                forbiddenCube.transform.localPosition = new Vector3(0, 0.5f, -5);
                forbiddenCube.transform.localScale = new Vector3(5, 1, 5);
                
                // 设置半透明红色材质
                var renderer = forbiddenCube.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = new Color(1, 0, 0, 0.3f);
                    mat.SetFloat("_Mode", 3); // Transparent
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                }
                
                Debug.Log("✅ 创建了 ForbiddenArea");
            }

            Debug.Log("========== 配置生成完成 ==========");
            Debug.Log("⚠️ 请手动配置 CardUISystem 的 ValidArea 和 ForbiddenArea 引用");
        }
    }
}
