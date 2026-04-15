using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3WorldOverlayView : MonoBehaviour
    {
        private Texture2D fogTexture;
        private Color32[] pixels;
        private Material fogMaterial;
        private Material outsideMaterial;
        private Fog3ViewSettings settings;
        private float overlayHeight;

        public void Build(Fog3TerrainInfo terrainInfo, Fog3ViewSettings viewSettings, float resolvedOverlayHeight)
        {
            settings = viewSettings ?? new Fog3ViewSettings();
            overlayHeight = Mathf.Max(0f, resolvedOverlayHeight);
            transform.SetPositionAndRotation(terrainInfo.Origin, Quaternion.identity);
            transform.localScale = Vector3.one;

            ClearChildren();
            CreateTexture(terrainInfo.Width, terrainInfo.Height);
            CreateFogPlane(terrainInfo);
            CreateOutsideMask(terrainInfo);
        }

        public void Render(Fog3MapData mapData)
        {
            if (mapData == null || fogTexture == null || pixels == null)
                return;

            for (int y = 0; y < mapData.Height; y++)
            {
                for (int x = 0; x < mapData.Width; x++)
                {
                    int index = x + y * mapData.Width;
                    pixels[index] = GetPixelColor(mapData, x, y);
                }
            }

            fogTexture.SetPixels32(pixels);
            fogTexture.Apply(false);
        }

        private Color32 GetPixelColor(Fog3MapData mapData, int x, int y)
        {
            Fog3CellState state = mapData.GetCellState(x, y);
            switch (state)
            {
                case Fog3CellState.Visible:
                    return Color.Lerp(settings.ExploredColor, settings.VisibleColor, mapData.GetVisibility(x, y));
                case Fog3CellState.Explored:
                    return settings.ExploredColor;
                case Fog3CellState.Outside:
                    return settings.OutsideColor;
                default:
                    return settings.HiddenColor;
            }
        }

        private void CreateTexture(int width, int height)
        {
            fogTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = settings.TextureFilterMode
            };
            pixels = new Color32[width * height];
        }

        private void CreateFogPlane(Fog3TerrainInfo terrainInfo)
        {
            GameObject plane = new GameObject("FOG3_WorldOverlay");
            plane.transform.SetParent(transform, false);
            ApplyLayer(plane);

            MeshFilter meshFilter = plane.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = plane.AddComponent<MeshRenderer>();
            Bounds localBounds = CreateLocalTerrainBounds(terrainInfo);
            meshFilter.sharedMesh = CreateQuadMesh(localBounds, overlayHeight, "FOG3_WorldOverlayMesh");

            fogMaterial = CreateTransparentMaterial("FOG3_WorldOverlayMaterial", Color.white);
            SetMainTexture(fogMaterial, fogTexture);
            meshRenderer.sharedMaterial = fogMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void CreateOutsideMask(Fog3TerrainInfo terrainInfo)
        {
            float padding = Mathf.Max(0f, settings.OutsideMaskPadding);
            if (padding <= 0.01f)
                return;

            Bounds bounds = CreateLocalTerrainBounds(terrainInfo);
            float minX = bounds.min.x;
            float maxX = bounds.max.x;
            float minZ = bounds.min.z;
            float maxZ = bounds.max.z;
            float y = overlayHeight + 0.01f;

            outsideMaterial = CreateTransparentMaterial("FOG3_OutsideMaskMaterial", settings.OutsideColor);
            CreateOutsideQuad("FOG3_Outside_North", new Vector3(minX - padding, 0f, maxZ), new Vector3(maxX + padding, 0f, maxZ + padding), y);
            CreateOutsideQuad("FOG3_Outside_South", new Vector3(minX - padding, 0f, minZ - padding), new Vector3(maxX + padding, 0f, minZ), y);
            CreateOutsideQuad("FOG3_Outside_West", new Vector3(minX - padding, 0f, minZ), new Vector3(minX, 0f, maxZ), y);
            CreateOutsideQuad("FOG3_Outside_East", new Vector3(maxX, 0f, minZ), new Vector3(maxX + padding, 0f, maxZ), y);
        }

        private static Bounds CreateLocalTerrainBounds(Fog3TerrainInfo terrainInfo)
        {
            Vector3 size = new Vector3(terrainInfo.Width * terrainInfo.CellSize, 0f, terrainInfo.Height * terrainInfo.CellSize);
            return new Bounds(size * 0.5f, size);
        }

        private void CreateOutsideQuad(string objectName, Vector3 min, Vector3 max, float y)
        {
            Bounds bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, y, min.z), new Vector3(max.x, y, max.z));

            GameObject quad = new GameObject(objectName);
            quad.transform.SetParent(transform, false);
            ApplyLayer(quad);

            MeshFilter meshFilter = quad.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = quad.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = CreateQuadMesh(bounds, y, objectName + "_Mesh");
            meshRenderer.sharedMaterial = outsideMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private Mesh CreateQuadMesh(Bounds bounds, float y, string meshName)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = new[]
            {
                new Vector3(min.x, y, min.z),
                new Vector3(max.x, y, min.z),
                new Vector3(min.x, y, max.z),
                new Vector3(max.x, y, max.z)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateTransparentMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)RenderQueue.Transparent + 100
            };

            SetMaterialColor(material, color);
            ConfigureTransparent(material);
            return material;
        }

        private static void ConfigureTransparent(Material material)
        {
            if (material == null)
                return;

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private static void SetMainTexture(Material material, Texture texture)
        {
            if (material == null || texture == null)
                return;

            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
        }

        private void ApplyLayer(GameObject target)
        {
            if (settings.OverlayLayer >= 0 && settings.OverlayLayer <= 31)
                target.layer = settings.OverlayLayer;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (fogTexture != null)
                Destroy(fogTexture);
            if (fogMaterial != null)
                Destroy(fogMaterial);
            if (outsideMaterial != null)
                Destroy(outsideMaterial);
        }
    }
}
