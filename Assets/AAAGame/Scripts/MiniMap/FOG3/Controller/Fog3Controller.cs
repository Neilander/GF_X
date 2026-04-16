using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3Controller
    {
        private readonly Dictionary<int, Fog3RevealerData> revealers = new Dictionary<int, Fog3RevealerData>();
        private int nextRevealerId = 1;

        public event Action<Fog3MapData> VisibilityUpdated;

        public Fog3MapData MapData { get; private set; }
        public int RevealerCount => revealers.Count;

        public void Initialize(Fog3TerrainInfo terrainInfo)
        {
            MapData = new Fog3MapData(terrainInfo);
            revealers.Clear();
            nextRevealerId = 1;
        }

        public int RegisterRevealer(Transform target, float visionRadius, int entityId, bool useLineOfSight)
        {
            if (MapData == null)
                return -1;

            int id = nextRevealerId++;
            Vector3 fallbackPosition = target != null ? target.position : Vector3.zero;
            revealers.Add(id, new Fog3RevealerData(id, target, fallbackPosition, visionRadius, entityId, useLineOfSight));
            return id;
        }

        public int RegisterRevealer(Vector3 position, float visionRadius, bool useLineOfSight)
        {
            if (MapData == null)
                return -1;

            int id = nextRevealerId++;
            revealers.Add(id, new Fog3RevealerData(id, null, position, visionRadius, 0, useLineOfSight));
            return id;
        }

        public void UnregisterRevealer(int revealerId)
        {
            revealers.Remove(revealerId);
        }

        public bool TryGetRevealer(int revealerId, out Fog3RevealerData revealer)
        {
            return revealers.TryGetValue(revealerId, out revealer);
        }

        public void UpdateVisibility(LayerMask occluderMask, float eyeHeight, float softEdgeWidth, bool globalLineOfSight)
        {
            if (MapData == null)
                return;

            MapData.ClearCurrentVisibility();

            List<int> deadRevealers = null;
            foreach (KeyValuePair<int, Fog3RevealerData> pair in revealers)
            {
                Fog3RevealerData revealer = pair.Value;
                if (!revealer.IsActive)
                    continue;

                if (!revealer.HasTarget && revealer.EntityId != 0)
                {
                    deadRevealers ??= new List<int>();
                    deadRevealers.Add(pair.Key);
                    continue;
                }

                Reveal(revealer, occluderMask, eyeHeight, softEdgeWidth, globalLineOfSight || revealer.UseLineOfSight);
            }

            if (deadRevealers != null)
            {
                for (int i = 0; i < deadRevealers.Count; i++)
                    revealers.Remove(deadRevealers[i]);
            }

            VisibilityUpdated?.Invoke(MapData);
            MapData.MarkClean();
        }

        public bool IsPositionVisible(Vector3 worldPos)
        {
            return MapData != null && MapData.IsPositionVisible(worldPos);
        }

        public bool IsPositionExplored(Vector3 worldPos)
        {
            return MapData != null && MapData.IsPositionExplored(worldPos);
        }

        public void ResetExploration()
        {
            MapData?.ResetExploration();
        }

        private void Reveal(Fog3RevealerData revealer, LayerMask occluderMask, float eyeHeight, float softEdgeWidth, bool useLineOfSight)
        {
            Vector3 position = revealer.Position;
            if (!MapData.WorldToGrid(position, out int centerX, out int centerY))
                return;

            int range = Mathf.CeilToInt(revealer.VisionRadius / MapData.CellSize);
            float radius = revealer.VisionRadius;
            float radiusSqr = radius * radius;
            float innerRadius = Mathf.Max(0f, radius - softEdgeWidth);
            float innerRadiusSqr = innerRadius * innerRadius;
            Vector3 from = position + Vector3.up * eyeHeight;

            for (int y = centerY - range; y <= centerY + range; y++)
            {
                for (int x = centerX - range; x <= centerX + range; x++)
                {
                    if (!MapData.IsValidCell(x, y) || !MapData.IsWalkable(x, y))
                        continue;

                    Vector3 cellCenter = MapData.GridToWorldCenter(x, y);
                    Vector2 delta = new Vector2(cellCenter.x - position.x, cellCenter.z - position.z);
                    float distSqr = delta.sqrMagnitude;
                    if (distSqr > radiusSqr)
                        continue;

                    if (useLineOfSight && IsBlocked(from, cellCenter + Vector3.up * eyeHeight, occluderMask))
                        continue;

                    float intensity = 1f;
                    if (softEdgeWidth > 0.01f && distSqr > innerRadiusSqr)
                    {
                        float distance = Mathf.Sqrt(distSqr);
                        intensity = 1f - Mathf.InverseLerp(innerRadius, radius, distance);
                    }

                    MapData.AddVisibility(x, y, intensity);
                }
            }
        }

        private static bool IsBlocked(Vector3 from, Vector3 to, LayerMask occluderMask)
        {
            if (occluderMask.value == 0)
                return false;

            return Physics.Linecast(from, to, occluderMask, QueryTriggerInteraction.Ignore);
        }
    }
}
