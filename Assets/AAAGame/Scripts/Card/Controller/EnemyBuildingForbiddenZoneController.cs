using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AAAGame.Card
{
    public sealed class EnemyBuildingForbiddenZoneController
    {
        private const string ForbiddenZoneShaderAssetPath = "Assets/AAAGame/Scripts/Card/Material/CardForbiddenZoneOverlay.shader";
        private const string RootObjectName = "EnemyBuildingForbiddenZones";
        private const float ZoneYOffset = 0.12f;
        private const int ZoneRenderQueue = (int)RenderQueue.Transparent + 50;

        private readonly Dictionary<int, ZoneVisual> m_ZoneVisuals = new();
        private readonly HashSet<int> m_CurrentZoneKeys = new();
        private readonly List<int> m_RemoveKeys = new();
        private Transform m_RootTransform;
        private Material m_ZoneMaterial;
        private bool m_IsActive;

        private sealed class ZoneVisual
        {
            public GameObject GameObject;
            public Mesh Mesh;
        }

        public void BeginPlacement()
        {
            m_IsActive = true;
            RefreshZones();
        }

        public void EndPlacement()
        {
            m_IsActive = false;
            HideAllVisuals();
        }

        public void RefreshZones()
        {
            if (!m_IsActive)
                return;

            m_CurrentZoneKeys.Clear();
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"Enemy forbidden-zone presenter found null registry entity at index {i}.");
                if (!ShouldPresent(entity, out IBuildingLogicContext building))
                    continue;

                int key = building.LogicEntityId.Value;
                m_CurrentZoneKeys.Add(key);
                UpdateOrCreateZoneVisual(key, building);
            }

            HideUnusedVisuals();
        }

        public void Shutdown()
        {
            m_IsActive = false;
            m_CurrentZoneKeys.Clear();

            foreach (KeyValuePair<int, ZoneVisual> pair in m_ZoneVisuals)
            {
                ZoneVisual visual = pair.Value;
                if (visual == null)
                    continue;
                if (visual.Mesh != null)
                    UnityEngine.Object.Destroy(visual.Mesh);
                if (visual.GameObject != null)
                    UnityEngine.Object.Destroy(visual.GameObject);
            }
            m_ZoneVisuals.Clear();

            if (m_RootTransform != null)
            {
                UnityEngine.Object.Destroy(m_RootTransform.gameObject);
                m_RootTransform = null;
            }
            if (m_ZoneMaterial != null)
            {
                UnityEngine.Object.Destroy(m_ZoneMaterial);
                m_ZoneMaterial = null;
            }
        }

        private static bool ShouldPresent(IEntityContext entity, out IBuildingLogicContext building)
        {
            building = null;
            if (!entity.Alive || !entity.TryGetLogicBuilding(out building))
                return false;
            return LogicCardPlacementAuthority.GeneratesEnemyBuildingForbiddenZone(building);
        }

        private void UpdateOrCreateZoneVisual(int key, IBuildingLogicContext building)
        {
            EnsureRoot();
            if (!m_ZoneVisuals.TryGetValue(key, out ZoneVisual visual)
                || visual == null
                || visual.GameObject == null)
            {
                visual = CreateZoneVisual(key);
                m_ZoneVisuals[key] = visual;
            }

            LogicCombatShape shape = building.CombatShape;
            if (shape.Kind != LogicCombatShapeKind.AxisAlignedBox)
            {
                throw new InvalidOperationException(
                    $"Enemy building {building.LogicEntityId.Value} requires an axis-aligned logic combat shape.");
            }

            Fix64 halfX = shape.HalfExtents.x;
            Fix64 halfZ = shape.HalfExtents.y;
            float minX = (float)(shape.Center.x - halfX);
            float maxX = (float)(shape.Center.x + halfX);
            float minZ = (float)(shape.Center.y - halfZ);
            float maxZ = (float)(shape.Center.y + halfZ);
            float displayY = ResolveDisplayY(building);

            visual.Mesh.Clear();
            visual.Mesh.vertices = new[]
            {
                new Vector3(minX, displayY, minZ),
                new Vector3(maxX, displayY, minZ),
                new Vector3(maxX, displayY, maxZ),
                new Vector3(minX, displayY, maxZ),
            };
            visual.Mesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                0, 2, 1,
                0, 3, 2,
            };
            visual.Mesh.RecalculateBounds();
            if (!visual.GameObject.activeSelf)
                visual.GameObject.SetActive(true);
        }

        private static float ResolveDisplayY(IBuildingLogicContext building)
        {
            if (LogicEntityLifecycleService.TryGetBoundView(building.LogicEntityId, out MAEntity view)
                && view != null)
            {
                return view.Position.y + ZoneYOffset;
            }
            return ZoneYOffset;
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
            var zoneObject = new GameObject($"EnemyForbiddenZone_{key}");
            zoneObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            zoneObject.transform.SetParent(m_RootTransform, false);
            MeshFilter meshFilter = zoneObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = zoneObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = GetOrCreateZoneMaterial();

            var mesh = new Mesh
            {
                name = $"EnemyForbiddenZoneMesh_{key}",
            };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
            return new ZoneVisual
            {
                GameObject = zoneObject,
                Mesh = mesh,
            };
        }

        private Material GetOrCreateZoneMaterial()
        {
            if (m_ZoneMaterial != null)
                return m_ZoneMaterial;

            Shader shader = AAAGame.Effect.EffectShaderAssetLoader.TryGet(ForbiddenZoneShaderAssetPath);
            if (shader == null)
                throw new InvalidOperationException($"Enemy forbidden-zone shader is not ready: {ForbiddenZoneShaderAssetPath}.");

            m_ZoneMaterial = new Material(shader);
            Color color = new Color(1f, 0.15f, 0.15f, 0.28f);
            if (m_ZoneMaterial.HasProperty("_BaseColor"))
                m_ZoneMaterial.SetColor("_BaseColor", color);
            if (m_ZoneMaterial.HasProperty("_Color"))
                m_ZoneMaterial.SetColor("_Color", color);
            if (m_ZoneMaterial.HasProperty("_Surface"))
                m_ZoneMaterial.SetFloat("_Surface", 1f);
            if (m_ZoneMaterial.HasProperty("_Blend"))
                m_ZoneMaterial.SetFloat("_Blend", 0f);
            if (m_ZoneMaterial.HasProperty("_AlphaClip"))
                m_ZoneMaterial.SetFloat("_AlphaClip", 0f);
            if (m_ZoneMaterial.HasProperty("_SrcBlend"))
                m_ZoneMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (m_ZoneMaterial.HasProperty("_DstBlend"))
                m_ZoneMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m_ZoneMaterial.HasProperty("_Cull"))
                m_ZoneMaterial.SetFloat("_Cull", (float)CullMode.Off);
            if (m_ZoneMaterial.HasProperty("_ZWrite"))
                m_ZoneMaterial.SetFloat("_ZWrite", 0f);
            if (m_ZoneMaterial.HasProperty("_ZTest"))
                m_ZoneMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);

            m_ZoneMaterial.DisableKeyword("_ALPHATEST_ON");
            m_ZoneMaterial.EnableKeyword("_ALPHABLEND_ON");
            m_ZoneMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m_ZoneMaterial.renderQueue = ZoneRenderQueue;
            return m_ZoneMaterial;
        }

        private void HideUnusedVisuals()
        {
            m_RemoveKeys.Clear();
            foreach (KeyValuePair<int, ZoneVisual> pair in m_ZoneVisuals)
            {
                ZoneVisual visual = pair.Value;
                if (visual == null || visual.GameObject == null)
                {
                    m_RemoveKeys.Add(pair.Key);
                    continue;
                }
                if (!m_CurrentZoneKeys.Contains(pair.Key))
                    visual.GameObject.SetActive(false);
            }
            for (int i = 0; i < m_RemoveKeys.Count; i++)
                m_ZoneVisuals.Remove(m_RemoveKeys[i]);
        }

        private void HideAllVisuals()
        {
            foreach (KeyValuePair<int, ZoneVisual> pair in m_ZoneVisuals)
            {
                if (pair.Value?.GameObject != null)
                    pair.Value.GameObject.SetActive(false);
            }
        }
    }
}
