using UnityEngine;
using System.IO;

/// <summary>
/// 卡牌系统诊断工具
/// 用于检查配置是否正确
/// </summary>
public class CardSystemDiagnostic : MonoBehaviour
{
    [ContextMenu("运行诊断")]
    public void RunDiagnostic()
    {
        Debug.Log("========== 卡牌系统诊断开始 ==========");
        
        CheckGFBuiltin();
        CheckUITable();
        CheckUIViews();
        CheckPrefab();
        CheckAssetBundle();
        
        Debug.Log("========== 卡牌系统诊断完成 ==========");
    }

    private void CheckGFBuiltin()
    {
        Debug.Log("\n【1. 检查 GFBuiltin】");
        
        if (GFBuiltin.Event == null)
        {
            Debug.LogError("❌ GFBuiltin.Event 未初始化");
        }
        else
        {
            Debug.Log("✓ GFBuiltin.Event 已初始化");
        }
        
        if (GFBuiltin.UI == null)
        {
            Debug.LogError("❌ GFBuiltin.UI 未初始化");
        }
        else
        {
            Debug.Log("✓ GFBuiltin.UI 已初始化");
        }
        
        if (GFBuiltin.DataTable == null)
        {
            Debug.LogError("❌ GFBuiltin.DataTable 未初始化");
        }
        else
        {
            Debug.Log("✓ GFBuiltin.DataTable 已初始化");
        }
    }

    private void CheckUITable()
    {
        Debug.Log("\n【2. 检查 UITable】");
        
        string uiTablePath = "Assets/AAAGame/DataTable/Core/UITable.bytes";
        if (File.Exists(uiTablePath))
        {
            FileInfo fileInfo = new FileInfo(uiTablePath);
            Debug.Log($"✓ UITable.bytes 存在");
            Debug.Log($"  文件大小: {fileInfo.Length} bytes");
            Debug.Log($"  修改时间: {fileInfo.LastWriteTime}");
        }
        else
        {
            Debug.LogError("❌ UITable.bytes 不存在");
            Debug.LogError("  请执行: Unity 菜单 → AAAGame → Generate DataTables");
        }
        
        // 检查 UITable.txt
        string uiTableTxtPath = "Assets/AAAGame/DataTable/Core/UITable.txt";
        if (File.Exists(uiTableTxtPath))
        {
            string content = File.ReadAllText(uiTableTxtPath);
            if (content.Contains("CardUIForm"))
            {
                Debug.Log("✓ UITable.txt 包含 CardUIForm 条目");
            }
            else
            {
                Debug.LogError("❌ UITable.txt 不包含 CardUIForm 条目");
            }
        }
    }

    private void CheckUIViews()
    {
        Debug.Log("\n【3. 检查 UIViews 枚举】");
        
        try
        {
            //var cardUIFormValue = (int)UIViews.CardUIForm;
           // Debug.Log($"✓ UIViews.CardUIForm 存在，值为: {cardUIFormValue}");
        }
        catch
        {
            Debug.LogError("❌ UIViews.CardUIForm 不存在");
            Debug.LogError("  请检查 Assets/AAAGame/Scripts/UI/Core/UIViews.cs");
        }
    }

    private void CheckPrefab()
    {
        Debug.Log("\n【4. 检查 CardUIForm 预制体】");
        
        string prefabPath = "Assets/AAAGame/Prefabs/UI/CardUIForm.prefab";
        if (File.Exists(prefabPath))
        {
            Debug.Log("✓ CardUIForm.prefab 存在");
#if UNITY_EDITOR
            // 尝试加载预制体
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                Debug.Log("✓ 预制体可以加载");

                // 检查脚本组件
                var cardUIForm = prefab.GetComponent<AAAGame.Card.CardUIForm>();
                if (cardUIForm != null)
                {
                    Debug.Log("✓ CardUIForm 脚本组件存在");
                }
                else
                {
                    Debug.LogError("❌ CardUIForm 脚本组件不存在");
                }
            }
            else
            {
                Debug.LogError("❌ 预制体无法加载");
            }
#else
            Debug.Log("运行时环境跳过 AssetDatabase 预制体加载检查");
#endif
        }
        else
        {
            Debug.LogError("❌ CardUIForm.prefab 不存在");
            Debug.LogError("  请创建预制体: Assets/AAAGame/Prefabs/UI/CardUIForm.prefab");
        }
    }

    private void CheckAssetBundle()
    {
        Debug.Log("\n【5. 检查 AssetBundle 配置】");
        
        string prefabPath = "Assets/AAAGame/Prefabs/UI/CardUIForm.prefab";
        if (File.Exists(prefabPath))
        {
#if UNITY_EDITOR
            var importer = UnityEditor.AssetImporter.GetAtPath(prefabPath);
            if (importer != null)
            {
                string assetBundleName = importer.assetBundleName;
                if (string.IsNullOrEmpty(assetBundleName))
                {
                    Debug.LogWarning("⚠️ AssetBundle 标签未设置");
                    Debug.LogWarning("  请选中预制体，在 Inspector 底部设置 AssetBundle 为 'ui'");
                }
                else
                {
                    Debug.Log($"✓ AssetBundle 标签已设置: {assetBundleName}");
                }
            }
#else
            Debug.Log("运行时环境跳过 AssetBundle 标签检查");
#endif
        }
    }

    void Start()
    {
        // 延迟 2 秒后自动运行诊断
        Invoke(nameof(RunDiagnostic), 2f);
    }
}
