using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// GameFramework 诊断工具
/// 用于检查 GameFramework 对象的配置是否正确
/// 使用方法：将此脚本添加到场景中的任意对象上，运行游戏查看 Console 输出
/// </summary>
public class GameFrameworkDiagnostic : MonoBehaviour
{
    void Start()
    {
        Debug.Log("=== GameFramework 诊断开始 ===");
        
        // 检查 GameEntry 是否存在
        var gameEntry = GameObject.Find("GameFramework");
        if (gameEntry == null)
        {
            Debug.LogError("❌ 场景中未找到 GameFramework 对象！");
            Debug.LogError("   解决方案：从 Assets/Plugins/UnityGameFramework/Prefabs/GameFramework.prefab 拖入场景");
            return;
        }
        
        Debug.Log("✓ 找到 GameFramework 对象");
        
        // 检查所有必需的组件
        CheckComponent<BaseComponent>(gameEntry, "Base Component", true);
        CheckComponent<EventComponent>(gameEntry, "Event Component", true);
        CheckComponent<FsmComponent>(gameEntry, "Fsm Component", true);
        CheckComponent<ProcedureComponent>(gameEntry, "Procedure Component", true);
        CheckComponent<ResourceComponent>(gameEntry, "Resource Component", true);
        CheckComponent<SettingComponent>(gameEntry, "Setting Component", true);
        CheckComponent<UIComponent>(gameEntry, "UI Component", true);
        CheckComponent<ConfigComponent>(gameEntry, "Config Component", true);
        CheckComponent<DataTableComponent>(gameEntry, "Data Table Component", true);
        CheckComponent<DataNodeComponent>(gameEntry, "Data Node Component", true);
        CheckComponent<LocalizationComponent>(gameEntry, "Localization Component", true);
        
        // 检查推荐的组件
        CheckComponent<EntityComponent>(gameEntry, "Entity Component", false);
        CheckComponent<SoundComponent>(gameEntry, "Sound Component", false);
        CheckComponent<SceneComponent>(gameEntry, "Scene Component", false);
        CheckComponent<ObjectPoolComponent>(gameEntry, "Object Pool Component", false);
        CheckComponent<DownloadComponent>(gameEntry, "Download Component", false);
        CheckComponent<WebRequestComponent>(gameEntry, "Web Request Component", false);
        CheckComponent<DebuggerComponent>(gameEntry, "Debugger Component", false);
        
        // 检查 GFBuiltin 组件
        var gfBuiltin = gameEntry.GetComponent<GFBuiltin>();
        if (gfBuiltin == null)
        {
            Debug.LogError("❌ GameFramework 对象缺少 GFBuiltin 组件！");
            Debug.LogError("   解决方案：在 GameFramework 对象上添加 GFBuiltin 脚本");
        }
        else
        {
            Debug.Log("✓ 找到 GFBuiltin 组件");
        }
        
        // 检查 BuiltinViewComponent
        var builtinView = gameEntry.GetComponent<BuiltinViewComponent>();
        if (builtinView == null)
        {
            Debug.LogError("❌ GameFramework 对象缺少 Builtin View Component！");
            Debug.LogError("   解决方案：在 GameFramework 对象上添加 Builtin View Component");
        }
        else
        {
            Debug.Log("✓ 找到 Builtin View Component");
        }
        
        // 检查 Procedure Component 配置
        var procedureComponent = gameEntry.GetComponent<ProcedureComponent>();
        if (procedureComponent != null)
        {
            Debug.Log("检查 Procedure Component 配置...");
            // 注意：无法直接访问 Available Procedures 和 Entrance Procedure Name
            // 需要在 Inspector 中手动检查
            Debug.LogWarning("⚠️ 请在 Inspector 中检查 Procedure Component：");
            Debug.LogWarning("   1. Entrance Procedure Name 必须设置为 'LaunchProcedure'");
            Debug.LogWarning("   2. Available Procedures 必须包含所有必要的 Procedure 类");
        }
        
        // 检查 UI Component 配置
        var uiComponent = gameEntry.GetComponent<UIComponent>();
        if (uiComponent != null)
        {
            Debug.Log("检查 UI Component 配置...");
            
            // 检查 UI Camera
            var uiCamera = GameObject.Find("UICamera");
            if (uiCamera == null)
            {
                Debug.LogWarning("⚠️ 场景中未找到 UICamera 对象");
            }
            else
            {
                Debug.Log("✓ 找到 UICamera");
            }
            
            // 检查 Canvas
            var canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogWarning("⚠️ 场景中未找到 Canvas 对象");
            }
            else
            {
                Debug.Log("✓ 找到 Canvas");
            }
        }
        
        Debug.Log("=== GameFramework 诊断完成 ===");
        Debug.Log("如果有 ❌ 错误，请按照提示修复");
        Debug.Log("如果有 ⚠️ 警告，建议检查配置");
    }
    
    private void CheckComponent<T>(GameObject gameObject, string componentName, bool required) where T : Component
    {
        var component = gameObject.GetComponent<T>();
        if (component == null)
        {
            if (required)
            {
                Debug.LogError($"❌ 缺少必需组件: {componentName}");
            }
            else
            {
                Debug.LogWarning($"⚠️ 缺少推荐组件: {componentName}");
            }
        }
        else
        {
            Debug.Log($"✓ 找到 {componentName}");
        }
    }
}
