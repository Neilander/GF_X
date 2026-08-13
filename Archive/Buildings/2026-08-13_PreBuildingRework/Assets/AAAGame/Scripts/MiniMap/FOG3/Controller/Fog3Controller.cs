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

        public int RegisterRevealer(Transform target, float visionRadius, int logicEntityId, bool useLineOfSight, bool allowRevealHidden = true)
        {
            if (MapData == null)
                return -1;

            int id = nextRevealerId++;
            Vector3 fallbackPosition = target != null ? target.position : Vector3.zero;
            revealers.Add(id, new Fog3RevealerData(id, target, fallbackPosition, visionRadius, logicEntityId, useLineOfSight, allowRevealHidden));
            return id;
        }

        public int RegisterRevealer(Vector3 position, float visionRadius, bool useLineOfSight, bool allowRevealHidden = true)
        {
            if (MapData == null)
                return -1;

            int id = nextRevealerId++;
            revealers.Add(id, new Fog3RevealerData(id, null, position, visionRadius, 0, useLineOfSight, allowRevealHidden));
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

        public void PublishAuthoritativeVisibility(bool logPerformanceDiagnostics)
        {
            if (MapData == null)
                return;

            long updateStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            VisibilityUpdated?.Invoke(MapData);
            MapData.MarkClean();

            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - updateStartTicks;
            double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (logPerformanceDiagnostics && elapsedMs >= 2.0)
            {
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    null,
                    "[FOG3Perf] authoritative visibility presentation={0:F3}ms revealers={1}",
                    elapsedMs,
                    revealers.Count);
            }
        }

        public bool IsPositionVisible(Vector3 worldPos)
        {
            return MapData != null && MapData.IsPositionVisible(worldPos);
        }

        public bool IsPositionExplored(Vector3 worldPos)
        {
            return MapData != null && MapData.IsPositionExplored(worldPos);
        }

        public void GetRevealerDiagnostics(
            out int totalCount,
            out int activeCount,
            out int targetCount,
            out int staticCount,
            out float maxRadius,
            out int firstActiveId,
            out int firstActiveEntityId,
            out Vector3 firstActivePosition)
        {
            totalCount = revealers.Count;
            activeCount = 0;
            targetCount = 0;
            staticCount = 0;
            maxRadius = 0f;
            firstActiveId = 0;
            firstActiveEntityId = 0;
            firstActivePosition = Vector3.zero;

            foreach (KeyValuePair<int, Fog3RevealerData> pair in revealers)
            {
                Fog3RevealerData revealer = pair.Value;
                if (revealer == null)
                    continue;

                if (revealer.HasTarget)
                    targetCount++;
                else
                    staticCount++;

                if (!revealer.IsActive)
                    continue;

                activeCount++;
                maxRadius = Mathf.Max(maxRadius, revealer.VisionRadius);
                if (firstActiveId != 0)
                    continue;

                firstActiveId = revealer.Id;
                firstActiveEntityId = revealer.LogicEntityId;
                firstActivePosition = ResolveRevealerPosition(revealer);
            }
        }

        public void ResetExploration()
        {
            MapData?.ResetExploration();
        }

        private static Vector3 ResolveRevealerPosition(Fog3RevealerData revealer)
        {
            if (revealer == null)
                throw new ArgumentNullException(nameof(revealer));
            if (revealer.LogicEntityId == 0)
                return revealer.Position;
            if (revealer.LogicEntityId < 0)
                throw new InvalidOperationException($"Fog3 revealer {revealer.Id} has invalid logic entity id {revealer.LogicEntityId}.");

            var entityId = new LogicEntityId(revealer.LogicEntityId);
            if (!EntityRegistry.TryGet(entityId, out IEntityContext entity))
            {
                throw new InvalidOperationException(
                    $"Fog3 revealer {revealer.Id} references missing logic entity {revealer.LogicEntityId}.");
            }

            Vector3 viewPosition = revealer.Position;
            return new Vector3(
                (float)entity.PositionFixed.x,
                viewPosition.y,
                (float)entity.PositionFixed.y);
        }

    }
}
