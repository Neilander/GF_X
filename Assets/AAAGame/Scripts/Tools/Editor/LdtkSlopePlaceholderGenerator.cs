using System;
using UnityEditor;
using UnityEngine;

namespace AAAGame.Tools.Editor
{
    public static class LdtkSlopePlaceholderGenerator
    {
        private const string OutputFolder = "Assets/AAAGame/Models/SlopePlaceholder";
        private const string MaterialSourcePrefab = "Assets/AAAGame/Models/\u5730\u5757/TopFill.prefab";

        [MenuItem("Tools/TileWorldCreator/Generate Slope Placeholder Prefabs", false, 220)]
        public static void Generate()
        {
            EnsureFolder(OutputFolder);
            Material material = LoadTerrainMaterial();
            CreateOrReplaceRamp("Ramp45", 1f, material);
            CreateOrReplaceRamp("Ramp2x1", 2f, material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LDtk Slope] Generated placeholder ramp prefabs in " + OutputFolder);
        }

        private static Material LoadTerrainMaterial()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(MaterialSourcePrefab);
            MeshRenderer renderer = source != null ? source.GetComponentInChildren<MeshRenderer>(true) : null;
            if (renderer == null || renderer.sharedMaterial == null)
            {
                throw new InvalidOperationException("Terrain material source is missing: " + MaterialSourcePrefab);
            }

            return renderer.sharedMaterial;
        }

        private static void CreateOrReplaceRamp(string name, float length, Material material)
        {
            string meshPath = OutputFolder + "/" + name + "Mesh.asset";
            string prefabPath = OutputFolder + "/" + name + ".prefab";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = name + "Mesh" };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            WriteWedgeMesh(mesh, length);
            var root = new GameObject(name);
            try
            {
                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                MeshCollider collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void WriteWedgeMesh(Mesh mesh, float length)
        {
            float halfWidth = 0.5f;
            float halfLength = length * 0.5f;
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfLength),
                new Vector3(halfWidth, 0f, -halfLength),
                new Vector3(-halfWidth, 0f, halfLength),
                new Vector3(halfWidth, 0f, halfLength),
                new Vector3(-halfWidth, 1f, halfLength),
                new Vector3(halfWidth, 1f, halfLength)
            };
            mesh.triangles = new[]
            {
                0, 1, 3, 0, 3, 2,
                0, 5, 1, 0, 4, 5,
                2, 3, 5, 2, 5, 4,
                0, 2, 4,
                1, 5, 3
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
