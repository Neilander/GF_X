using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 拖拽放置时，动态显示并维护敌方建筑的不可部署范围。
    /// </summary>
    public sealed class EnemyBuildingForbiddenZoneController
    {
        private const string RootObjectName = "EnemyBuildingForbiddenZones";
        private const float FootprintScale = 2f;
        private const int CircleSegmentCount = 24;
        private const float MinTriangleArea = 0.0001f;
        private const float ZoneVisualHeight = 0.04f;
        private const float ZoneYOffset = 0.03f;

        private readonly List<ZoneData> m_ActiveZones = new List<ZoneData>();
        private readonly List<ZoneData> m_ReusedZoneBuffer = new List<ZoneData>();
        private readonly Dictionary<int, ZoneVisual> m_ZoneVisuals = new Dictionary<int, ZoneVisual>();
        private readonly HashSet<int> m_CurrentZoneKeys = new HashSet<int>();
        private readonly List<int> m_RemoveKeys = new List<int>();

        private Transform m_RootTransform;
        private Material m_ZoneMaterial;
        private bool m_IsActive;

        private sealed class ZoneVisual
        {
            public GameObject GameObject;
            public Mesh Mesh;
        }

        private struct ZoneData
        {
            public int Key;
            public Vector2[] Vertices2D;
            public Vector3[] RenderVertices;
            public int[] Triangles;
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;

            public ZoneData(int key, Vector2[] vertices2D, Vector3[] renderVertices, int[] triangles,
                float minX, float maxX, float minZ, float maxZ)
            {
                Key = key;
                Vertices2D = vertices2D;
                RenderVertices = renderVertices;
                Triangles = triangles;
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
            }
        }

        public void BeginPlacement()
        {
            m_IsActive = true;
            RefreshZones();
        }

        public void EndPlacement()
        {
            m_IsActive = false;
            m_ActiveZones.Clear();
            HideAllVisuals();
        }

        public void RefreshZones()
        {
            if (!m_IsActive)
                return;

            m_ActiveZones.Clear();
            m_CurrentZoneKeys.Clear();

            var dataModel = GF.DataModel != null ? GF.DataModel.GetDataModel<InGameDataModel>() : null;
            if (dataModel == null || dataModel.Buildings == null)
            {
                HideAllVisuals();
                return;
            }

            foreach (var building in dataModel.Buildings)
            {
                if (!ShouldBlockPlacement(building))
                    continue;

                m_ReusedZoneBuffer.Clear();
                if (!TryCollectZones(building, m_ReusedZoneBuffer))
                    continue;

                for (int i = 0; i < m_ReusedZoneBuffer.Count; i++)
                {
                    ZoneData zone = m_ReusedZoneBuffer[i];
                    m_CurrentZoneKeys.Add(zone.Key);
                    m_ActiveZones.Add(zone);
                    UpdateOrCreateZoneVisual(zone);
                }
            }

            HideUnusedVisuals();
        }

        public bool IsPositionBlocked(Vector3 worldPosition, float probeRadius)
        {
            if (!m_IsActive)
                return false;

            float checkRadius = Mathf.Max(0f, probeRadius);
            Vector2 point = new Vector2(worldPosition.x, worldPosition.z);

            for (int i = 0; i < m_ActiveZones.Count; i++)
            {
                ZoneData zone = m_ActiveZones[i];

                if (point.x < zone.MinX - checkRadius || point.x > zone.MaxX + checkRadius)
                    continue;
                if (point.y < zone.MinZ - checkRadius || point.y > zone.MaxZ + checkRadius)
                    continue;

                if (IsPointInZone(point, zone))
                    return true;

                if (checkRadius > 0f && DistanceToZoneEdges(point, zone) <= checkRadius)
                    return true;
            }

            return false;
        }

        public void Shutdown()
        {
            m_IsActive = false;
            m_ActiveZones.Clear();
            m_ReusedZoneBuffer.Clear();
            m_CurrentZoneKeys.Clear();

            foreach (var pair in m_ZoneVisuals)
            {
                if (pair.Value == null)
                    continue;

                if (pair.Value.Mesh != null)
                {
                    Object.Destroy(pair.Value.Mesh);
                    pair.Value.Mesh = null;
                }

                if (pair.Value.GameObject != null)
                {
                    Object.Destroy(pair.Value.GameObject);
                }
            }
            m_ZoneVisuals.Clear();

            if (m_RootTransform != null)
            {
                Object.Destroy(m_RootTransform.gameObject);
                m_RootTransform = null;
            }

            if (m_ZoneMaterial != null)
            {
                Object.Destroy(m_ZoneMaterial);
                m_ZoneMaterial = null;
            }
        }

        private static bool ShouldBlockPlacement(BuildingEntity building)
        {
            if (building == null || !building.Alive)
                return false;

            IEntityContext player = EntityRegistry.Player;
            if (player != null)
                return EntityCombatTeamHelper.IsEnemy(player, building);

            return building.OwnerFactionID >= 0 && building.OwnerFactionID != EntitySideHelper.PlayerFactionId;
        }

        private static bool TryCollectZones(BuildingEntity building, List<ZoneData> output)
        {
            if (building == null || output == null)
                return false;

            Collider[] colliders = building.GetComponentsInChildren<Collider>(true);
            bool hasValidZone = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (TryCreateZoneFromCollider(collider, out ZoneData zone))
                {
                    output.Add(zone);
                    hasValidZone = true;
                }
            }

            if (hasValidZone)
                return true;

            return false;
        }

        private static bool TryCreateZoneFromCollider(Collider collider, out ZoneData zone)
        {
            if (collider is MeshCollider meshCollider)
                return TryCreateMeshColliderZone(meshCollider, out zone);

            if (collider is BoxCollider boxCollider)
                return TryCreateBoxColliderZone(boxCollider, out zone);

            if (collider is SphereCollider sphereCollider)
                return TryCreateSphereColliderZone(sphereCollider, out zone);

            if (collider is CapsuleCollider capsuleCollider)
                return TryCreateCapsuleColliderZone(capsuleCollider, out zone);

            return TryCreateBoundsZone(collider.GetInstanceID(), collider.bounds, out zone);
        }

        private static bool TryCreateMeshColliderZone(MeshCollider meshCollider, out ZoneData zone)
        {
            zone = default;

            Mesh mesh = meshCollider.sharedMesh;
            if (mesh == null)
                return false;

            int[] triangles = mesh.triangles;
            if (triangles == null || triangles.Length < 3)
                return false;

            Vector3[] meshVertices = mesh.vertices;
            if (meshVertices == null || meshVertices.Length < 3)
                return false;

            Vector3[] worldVertices = new Vector3[meshVertices.Length];
            Transform meshTransform = meshCollider.transform;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                worldVertices[i] = meshTransform.TransformPoint(meshVertices[i]);
            }

            float displayY = ResolveDisplayY(meshCollider.bounds.min.y, meshCollider.transform.root.position.y);
            return TryCreateZoneFromVertices(meshCollider.GetInstanceID(), worldVertices, triangles, displayY, out zone);
        }

        private static bool TryCreateBoxColliderZone(BoxCollider boxCollider, out ZoneData zone)
        {
            zone = default;

            Vector3 center = boxCollider.center;
            Vector3 size = boxCollider.size;
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;

            Vector3[] local = new Vector3[4];
            local[0] = new Vector3(center.x - halfX, center.y, center.z - halfZ);
            local[1] = new Vector3(center.x + halfX, center.y, center.z - halfZ);
            local[2] = new Vector3(center.x + halfX, center.y, center.z + halfZ);
            local[3] = new Vector3(center.x - halfX, center.y, center.z + halfZ);

            Vector3[] world = new Vector3[4];
            Transform t = boxCollider.transform;
            for (int i = 0; i < 4; i++)
            {
                world[i] = t.TransformPoint(local[i]);
            }

            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            float displayY = ResolveDisplayY(boxCollider.bounds.min.y, boxCollider.transform.root.position.y);
            return TryCreateZoneFromVertices(boxCollider.GetInstanceID(), world, triangles, displayY, out zone);
        }

        private static bool TryCreateSphereColliderZone(SphereCollider sphereCollider, out ZoneData zone)
        {
            zone = default;

            Vector3 center = sphereCollider.transform.TransformPoint(sphereCollider.center);
            Vector3 lossyScale = sphereCollider.transform.lossyScale;
            float radius = sphereCollider.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
            if (radius <= 0.0001f)
                return false;

            Vector3[] vertices = new Vector3[CircleSegmentCount + 1];
            int[] triangles = new int[CircleSegmentCount * 3];
            vertices[0] = center;

            for (int i = 0; i < CircleSegmentCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / CircleSegmentCount;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices[i + 1] = center + new Vector3(x, 0f, z);

                int triOffset = i * 3;
                triangles[triOffset] = 0;
                triangles[triOffset + 1] = i + 1;
                triangles[triOffset + 2] = (i + 1) % CircleSegmentCount + 1;
            }

            float displayY = ResolveDisplayY(sphereCollider.bounds.min.y, sphereCollider.transform.root.position.y);
            return TryCreateZoneFromVertices(sphereCollider.GetInstanceID(), vertices, triangles, displayY, out zone);
        }

        private static bool TryCreateCapsuleColliderZone(CapsuleCollider capsuleCollider, out ZoneData zone)
        {
            return TryCreateBoundsZone(capsuleCollider.GetInstanceID(), capsuleCollider.bounds, out zone,
                capsuleCollider.transform.root.position.y);
        }

        private static bool TryCreateBoundsZone(int key, Bounds bounds, out ZoneData zone, float referenceY = float.NaN)
        {
            zone = default;

            Vector3[] vertices = new Vector3[4];
            float y = bounds.center.y;
            vertices[0] = new Vector3(bounds.min.x, y, bounds.min.z);
            vertices[1] = new Vector3(bounds.max.x, y, bounds.min.z);
            vertices[2] = new Vector3(bounds.max.x, y, bounds.max.z);
            vertices[3] = new Vector3(bounds.min.x, y, bounds.max.z);

            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            float displayY = ResolveDisplayY(bounds.min.y, referenceY);
            return TryCreateZoneFromVertices(key, vertices, triangles, displayY, out zone);
        }

        private static bool TryCreateZoneFromVertices(int key, Vector3[] worldVertices, int[] triangles,
            float displayY, out ZoneData zone)
        {
            zone = default;

            if (worldVertices == null || worldVertices.Length < 3)
                return false;
            if (triangles == null || triangles.Length < 3)
                return false;

            Vector2 pivot = CalculatePivot(worldVertices);

            Vector2[] vertices2D = new Vector2[worldVertices.Length];
            Vector3[] renderVertices = new Vector3[worldVertices.Length];

            for (int i = 0; i < worldVertices.Length; i++)
            {
                Vector3 scaled = ScaleFootprintPoint(worldVertices[i], pivot);
                vertices2D[i] = new Vector2(scaled.x, scaled.z);

                scaled.y = displayY;
                renderVertices[i] = scaled;
            }

            if (!TryBuildProjectedTriangles(vertices2D, triangles, out int[] projectedTriangles))
                return false;

            ComputeAabb(vertices2D, out float minX, out float maxX, out float minZ, out float maxZ);

            zone = new ZoneData(key, vertices2D, renderVertices, projectedTriangles, minX, maxX, minZ, maxZ);
            return true;
        }

        private static float ResolveDisplayY(float minY, float referenceY)
        {
            if (float.IsNaN(referenceY) || float.IsInfinity(referenceY))
                return minY + ZoneYOffset;

            return Mathf.Max(minY, referenceY) + ZoneYOffset;
        }

        private static bool TryBuildProjectedTriangles(Vector2[] vertices2D, int[] sourceTriangles, out int[] projectedTriangles)
        {
            projectedTriangles = System.Array.Empty<int>();

            if (vertices2D == null || sourceTriangles == null || sourceTriangles.Length < 3)
                return false;

            List<int> valid = new List<int>(sourceTriangles.Length);
            for (int i = 0; i <= sourceTriangles.Length - 3; i += 3)
            {
                int i0 = sourceTriangles[i];
                int i1 = sourceTriangles[i + 1];
                int i2 = sourceTriangles[i + 2];

                if (i0 < 0 || i0 >= vertices2D.Length
                    || i1 < 0 || i1 >= vertices2D.Length
                    || i2 < 0 || i2 >= vertices2D.Length)
                {
                    continue;
                }

                Vector2 a = vertices2D[i0];
                Vector2 b = vertices2D[i1];
                Vector2 c = vertices2D[i2];
                float area2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                if (area2 <= MinTriangleArea)
                    continue;

                valid.Add(i0);
                valid.Add(i1);
                valid.Add(i2);
            }

            if (valid.Count == 0)
                return false;

            projectedTriangles = valid.ToArray();
            return true;
        }

        private static Vector2 CalculatePivot(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
                return Vector2.zero;

            float sumX = 0f;
            float sumZ = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                sumX += vertices[i].x;
                sumZ += vertices[i].z;
            }

            float inv = 1f / vertices.Length;
            return new Vector2(sumX * inv, sumZ * inv);
        }

        private static Vector3 ScaleFootprintPoint(Vector3 point, Vector2 pivot)
        {
            float x = pivot.x + (point.x - pivot.x) * FootprintScale;
            float z = pivot.y + (point.z - pivot.y) * FootprintScale;
            return new Vector3(x, point.y, z);
        }

        private static void ComputeAabb(Vector2[] vertices, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            minZ = float.PositiveInfinity;
            maxZ = float.NegativeInfinity;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 v = vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minZ) minZ = v.y;
                if (v.y > maxZ) maxZ = v.y;
            }
        }

        private void UpdateOrCreateZoneVisual(ZoneData zone)
        {
            EnsureRoot();

            if (!m_ZoneVisuals.TryGetValue(zone.Key, out ZoneVisual visual)
                || visual == null
                || visual.GameObject == null)
            {
                visual = CreateZoneVisual(zone.Key);
                if (visual == null || visual.GameObject == null)
                    return;

                m_ZoneVisuals[zone.Key] = visual;
            }

            if (visual.Mesh == null)
            {
                visual.Mesh = new Mesh();
                visual.Mesh.name = $"EnemyForbiddenZoneMesh_{zone.Key}";
                visual.Mesh.MarkDynamic();

                MeshFilter meshFilter = visual.GameObject.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = visual.Mesh;
                }
            }

            visual.Mesh.Clear();
            visual.Mesh.vertices = zone.RenderVertices;
            visual.Mesh.triangles = BuildDoubleSidedTriangles(zone.Triangles);
            visual.Mesh.RecalculateBounds();

            if (!visual.GameObject.activeSelf)
            {
                visual.GameObject.SetActive(true);
            }
        }

        private void EnsureRoot()
        {
            if (m_RootTransform != null)
                return;

            var rootObject = new GameObject(RootObjectName);
            rootObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            m_RootTransform = rootObject.transform;
        }

        private ZoneVisual CreateZoneVisual(int key)
        {
            GameObject zoneObject = new GameObject($"EnemyForbiddenZone_{key}");
            zoneObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            zoneObject.transform.SetParent(m_RootTransform, false);

            MeshFilter meshFilter = zoneObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = zoneObject.AddComponent<MeshRenderer>();

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material material = GetOrCreateZoneMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            var zoneVisual = new ZoneVisual
            {
                GameObject = zoneObject,
                Mesh = null
            };

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }

            return zoneVisual;
        }

        private Material GetOrCreateZoneMaterial()
        {
            if (m_ZoneMaterial != null)
                return m_ZoneMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return null;

            m_ZoneMaterial = new Material(shader);

            Color color = new Color(1f, 0.15f, 0.15f, 0.28f);
            if (m_ZoneMaterial.HasProperty("_BaseColor"))
                m_ZoneMaterial.SetColor("_BaseColor", color);
            if (m_ZoneMaterial.HasProperty("_Color"))
                m_ZoneMaterial.SetColor("_Color", color);
            if (m_ZoneMaterial.HasProperty("_Surface"))
                m_ZoneMaterial.SetFloat("_Surface", 1f);
            if (m_ZoneMaterial.HasProperty("_Cull"))
                m_ZoneMaterial.SetFloat("_Cull", (float)CullMode.Off);
            if (m_ZoneMaterial.HasProperty("_ZWrite"))
                m_ZoneMaterial.SetFloat("_ZWrite", 0f);

            if (shader.name == "Standard")
            {
                m_ZoneMaterial.SetFloat("_Mode", 3f);
                m_ZoneMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m_ZoneMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m_ZoneMaterial.SetInt("_ZWrite", 0);
                m_ZoneMaterial.DisableKeyword("_ALPHATEST_ON");
                m_ZoneMaterial.EnableKeyword("_ALPHABLEND_ON");
                m_ZoneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m_ZoneMaterial.renderQueue = (int)RenderQueue.Transparent;
            }

            if (m_ZoneMaterial.renderQueue < (int)RenderQueue.Transparent)
                m_ZoneMaterial.renderQueue = (int)RenderQueue.Transparent;

            return m_ZoneMaterial;
        }

        private static int[] BuildDoubleSidedTriangles(int[] sourceTriangles)
        {
            if (sourceTriangles == null || sourceTriangles.Length == 0)
                return System.Array.Empty<int>();

            int[] output = new int[sourceTriangles.Length * 2];
            for (int i = 0; i < sourceTriangles.Length; i++)
            {
                output[i] = sourceTriangles[i];
            }

            int offset = sourceTriangles.Length;
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                output[offset + i] = sourceTriangles[i];
                output[offset + i + 1] = sourceTriangles[i + 2];
                output[offset + i + 2] = sourceTriangles[i + 1];
            }

            return output;
        }

        private void HideUnusedVisuals()
        {
            m_RemoveKeys.Clear();

            foreach (var pair in m_ZoneVisuals)
            {
                if (pair.Value == null || pair.Value.GameObject == null)
                {
                    m_RemoveKeys.Add(pair.Key);
                    continue;
                }

                if (!m_CurrentZoneKeys.Contains(pair.Key))
                {
                    pair.Value.GameObject.SetActive(false);
                }
            }

            for (int i = 0; i < m_RemoveKeys.Count; i++)
            {
                m_ZoneVisuals.Remove(m_RemoveKeys[i]);
            }
        }

        private void HideAllVisuals()
        {
            foreach (var pair in m_ZoneVisuals)
            {
                if (pair.Value != null && pair.Value.GameObject != null)
                {
                    pair.Value.GameObject.SetActive(false);
                }
            }
        }

        private static bool IsPointInZone(Vector2 point, ZoneData zone)
        {
            int[] triangles = zone.Triangles;
            Vector2[] vertices = zone.Vertices2D;

            for (int i = 0; i <= triangles.Length - 3; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 < 0 || i0 >= vertices.Length
                    || i1 < 0 || i1 >= vertices.Length
                    || i2 < 0 || i2 >= vertices.Length)
                {
                    continue;
                }

                if (IsPointInTriangle(point, vertices[i0], vertices[i1], vertices[i2]))
                    return true;
            }

            return false;
        }

        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static float DistanceToZoneEdges(Vector2 point, ZoneData zone)
        {
            float minDistance = float.PositiveInfinity;
            int[] triangles = zone.Triangles;
            Vector2[] vertices = zone.Vertices2D;

            for (int i = 0; i <= triangles.Length - 3; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 < 0 || i0 >= vertices.Length
                    || i1 < 0 || i1 >= vertices.Length
                    || i2 < 0 || i2 >= vertices.Length)
                {
                    continue;
                }

                Vector2 a = vertices[i0];
                Vector2 b = vertices[i1];
                Vector2 c = vertices[i2];

                float d0 = DistancePointToSegment(point, a, b);
                float d1 = DistancePointToSegment(point, b, c);
                float d2 = DistancePointToSegment(point, c, a);

                if (d0 < minDistance) minDistance = d0;
                if (d1 < minDistance) minDistance = d1;
                if (d2 < minDistance) minDistance = d2;
            }

            return minDistance;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abSqr = Vector2.Dot(ab, ab);
            if (abSqr <= 0.000001f)
                return Vector2.Distance(point, a);

            float t = Vector2.Dot(point - a, ab) / abSqr;
            t = Mathf.Clamp01(t);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }
    }
}
