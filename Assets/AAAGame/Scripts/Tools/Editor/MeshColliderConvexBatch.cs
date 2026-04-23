#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class MeshColliderConvexBatch
{
    [MenuItem("Tools/Mesh Collider/Set Children Convex = ON")]
    private static void SetConvexOn() => Apply(true);

    [MenuItem("Tools/Mesh Collider/Set Children Convex = OFF")]
    private static void SetConvexOff() => Apply(false);

    [MenuItem("Tools/Mesh Collider/Set Children Convex = ON", true)]
    [MenuItem("Tools/Mesh Collider/Set Children Convex = OFF", true)]
    private static bool Validate() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

    private static void Apply(bool convex)
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            Debug.LogWarning("[MeshColliderConvexBatch] 没选中任何物体");
            return;
        }

        int totalScanned = 0;
        int totalChanged = 0;
        int prefabsSaved = 0;

        foreach (var root in roots)
        {
            if (root == null) continue;

            bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(root);

            if (isPrefabAsset)
            {
                // Project 窗口的 prefab asset：必须走 LoadPrefabContents / SaveAsPrefabAsset 才会落盘
                string path = AssetDatabase.GetAssetPath(root);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning($"[MeshColliderConvexBatch] {root.name} 无法取得 asset path，跳过");
                    continue;
                }

                var contentsRoot = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int scanned, changed;
                    ApplyOnRoot(contentsRoot, convex, recordUndo: false, out scanned, out changed);
                    totalScanned += scanned;
                    totalChanged += changed;

                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contentsRoot, path);
                        prefabsSaved++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }
            else
            {
                // Hierarchy 里的场景实例或 prefab 实例：直接改即可，Undo 能记录
                int scanned, changed;
                ApplyOnRoot(root, convex, recordUndo: true, out scanned, out changed);
                totalScanned += scanned;
                totalChanged += changed;
            }
        }

        if (prefabsSaved > 0)
            AssetDatabase.SaveAssets();

        Debug.Log($"[MeshColliderConvexBatch] roots={roots.Length} scanned={totalScanned} changed={totalChanged} convex={convex} prefabsSaved={prefabsSaved}");
    }

    private static void ApplyOnRoot(GameObject root, bool convex, bool recordUndo, out int scanned, out int changed)
    {
        var colliders = root.GetComponentsInChildren<MeshCollider>(true);
        scanned = colliders.Length;
        changed = 0;
        foreach (var mc in colliders)
        {
            if (mc.convex == convex) continue;
            if (recordUndo) Undo.RecordObject(mc, "Batch Set MeshCollider.convex");
            mc.convex = convex;
            EditorUtility.SetDirty(mc);
            changed++;
        }
    }
}
#endif
