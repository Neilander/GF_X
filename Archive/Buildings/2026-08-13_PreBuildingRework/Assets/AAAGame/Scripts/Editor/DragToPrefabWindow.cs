#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AAAGame.EditorTools
{
    /// <summary>
    /// 拖入物体生成 Prefab 的工具窗口。
    /// 菜单：Tools > Drag to Prefab。
    /// 把场景里的 GameObject 拖到下方区域，会按其完整结构（含子物体）保存为 Prefab。
    /// 编辑模式 / Play 模式 都能用。
    /// </summary>
    public class DragToPrefabWindow : EditorWindow
    {
        private const string PrefKeyFolder = "DragToPrefabWindow.SaveFolder";
        private const string PrefKeyOverwrite = "DragToPrefabWindow.Overwrite";
        private const string DefaultSaveFolder = "Assets/Generated/Prefabs";

        private string m_SaveFolder;
        private bool m_OverwriteExisting;
        private string m_LastResultMessage = string.Empty;
        private MessageType m_LastResultType = MessageType.Info;

        [MenuItem("Tools/Drag to Prefab")]
        public static void ShowWindow()
        {
            var win = GetWindow<DragToPrefabWindow>("Drag to Prefab");
            win.minSize = new Vector2(360, 200);
        }

        private void OnEnable()
        {
            m_SaveFolder = EditorPrefs.GetString(PrefKeyFolder, DefaultSaveFolder);
            m_OverwriteExisting = EditorPrefs.GetBool(PrefKeyOverwrite, false);
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefKeyFolder, m_SaveFolder);
            EditorPrefs.SetBool(PrefKeyOverwrite, m_OverwriteExisting);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("拖物体到下方区域生成 Prefab（含所有子物体）", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // 保存路径行
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newFolder = EditorGUILayout.TextField("保存目录", m_SaveFolder);
                if (EditorGUI.EndChangeCheck())
                {
                    m_SaveFolder = NormalizePath(newFolder);
                }

                if (GUILayout.Button("浏览", GUILayout.Width(60)))
                {
                    string selected = EditorUtility.OpenFolderPanel("选择保存目录", "Assets", string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string projectRoot = Application.dataPath;
                        if (selected.StartsWith(projectRoot))
                        {
                            m_SaveFolder = "Assets" + selected.Substring(projectRoot.Length);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("路径不在 Assets 内",
                                "Prefab 必须保存在项目 Assets 目录下。", "知道了");
                        }
                    }
                }
            }

            m_OverwriteExisting = EditorGUILayout.Toggle(
                new GUIContent("覆盖同名 Prefab", "勾上=同名直接覆盖；不勾=自动追加 (1) (2) 后缀"),
                m_OverwriteExisting);

            EditorGUILayout.Space(8);

            // 拖放区域
            Rect dropArea = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
            DrawDropArea(dropArea);
            HandleDragAndDrop(dropArea);

            EditorGUILayout.Space(8);

            // 结果显示
            if (!string.IsNullOrEmpty(m_LastResultMessage))
            {
                EditorGUILayout.HelpBox(m_LastResultMessage, m_LastResultType);
            }
        }

        private void DrawDropArea(Rect rect)
        {
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 0.85f, 1f, 0.6f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = prevColor;

            var labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                wordWrap = true
            };
            GUI.Label(rect, "把场景里的 GameObject 拖到这里\n（保留所有子物体结构）", labelStyle);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event evt = Event.current;
            if (!dropArea.Contains(evt.mousePosition))
            {
                return;
            }

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        int created = 0;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is GameObject go)
                            {
                                if (CreatePrefabFromGameObject(go))
                                {
                                    created++;
                                }
                            }
                        }

                        if (created == 0)
                        {
                            SetResult("没有 GameObject 被拖入（只接受 Hierarchy / Scene 里的对象）", MessageType.Warning);
                        }
                    }
                    evt.Use();
                    break;
            }
        }

        private bool CreatePrefabFromGameObject(GameObject source)
        {
            if (source == null)
            {
                return false;
            }

            // 确保保存目录存在
            string folder = NormalizePath(m_SaveFolder);
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets"))
            {
                SetResult($"保存目录非法: {m_SaveFolder}", MessageType.Error);
                return false;
            }

            EnsureFolderExists(folder);

            // 生成目标路径
            string baseName = SanitizeFileName(source.name);
            string targetPath = $"{folder}/{baseName}.prefab";
            if (!m_OverwriteExisting)
            {
                targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            }

            // 保存 Prefab。SaveAsPrefabAsset 默认包含所有子物体，子物体的 Transform/组件全保留。
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, targetPath, out bool success);

            if (success && prefab != null)
            {
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(prefab);
                SetResult($"✓ 已生成: {targetPath}", MessageType.Info);
                Debug.Log($"[DragToPrefab] 生成成功: {targetPath}");
                return true;
            }
            else
            {
                SetResult($"✗ 生成失败: {source.name}", MessageType.Error);
                Debug.LogError($"[DragToPrefab] 生成失败: {source.name} -> {targetPath}");
                return false;
            }
        }

        private static void EnsureFolderExists(string folder)
        {
            folder = NormalizePath(folder);
            if (string.IsNullOrEmpty(folder) || folder == "Assets" || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder);
            if (parent == null)
            {
                return;
            }
            parent = NormalizePath(parent);
            string name = Path.GetFileName(folder);

            EnsureFolderExists(parent);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string SanitizeFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "NewPrefab";
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(c, '_');
            }
            return raw.Trim();
        }

        private void SetResult(string msg, MessageType type)
        {
            m_LastResultMessage = msg;
            m_LastResultType = type;
            Repaint();
        }
    }
}
#endif
