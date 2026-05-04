using System;
using System.Collections.Generic;
using UnityEngine;

namespace AAAGame.MiniMap.FOG3
{
    public sealed class Fog3Controller
    {
        private readonly Dictionary<int, Fog3RevealerData> revealers = new Dictionary<int, Fog3RevealerData>();
        private int[] enemyStrongholdMask;
        private bool hasEnemyStrongholdMask;
        private int enemyStrongholdMaskSignature = int.MinValue;
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

        public int RegisterRevealer(Transform target, float visionRadius, int entityId, bool useLineOfSight, bool allowRevealHidden = true)
        {
            if (MapData == null)
                return -1;

            int id = nextRevealerId++;
            Vector3 fallbackPosition = target != null ? target.position : Vector3.zero;
            revealers.Add(id, new Fog3RevealerData(id, target, fallbackPosition, visionRadius, entityId, useLineOfSight, allowRevealHidden));
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

        public void UpdateVisibility(LayerMask occluderMask, float eyeHeight, float softEdgeWidth, bool globalLineOfSight, bool enableEnemyStrongholdHiddenVisionBlock)
        {
            if (MapData == null)
                return;

            if (enableEnemyStrongholdHiddenVisionBlock)
                RefreshEnemyStrongholdMask();
            else
                hasEnemyStrongholdMask = false;

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
                firstActiveEntityId = revealer.EntityId;
                firstActivePosition = revealer.Position;
            }
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

                    bool isHiddenTargetCell = !MapData.IsExplored(x, y);
                    if (isHiddenTargetCell && !revealer.AllowRevealHidden)
                        continue;

                    if (isHiddenTargetCell && IsHiddenRevealBlockedByEnemyStronghold(centerX, centerY, x, y))
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

        private void RefreshEnemyStrongholdMask()
        {
            int width = MapData.Width;
            int height = MapData.Height;
            int mapLength = width * height;

            if (enemyStrongholdMask == null || enemyStrongholdMask.Length != mapLength)
                enemyStrongholdMask = new int[mapLength];
            else
                Array.Clear(enemyStrongholdMask, 0, mapLength);

            hasEnemyStrongholdMask = false;

            IReadOnlyList<Stronghold> strongholds = InGameDataModel.GetStrongholds();
            if (strongholds == null || strongholds.Count == 0)
            {
                if (enemyStrongholdMaskSignature != 0)
                {
                    enemyStrongholdMaskSignature = 0;
                    Debug.Log("[FOG3] Enemy stronghold vision blockers refreshed. strongholds=0, cells=0");
                }

                return;
            }

            int playerFactionId = EntitySideHelper.PlayerFactionId;
            int enemyStrongholdId = 1;
            int enemyStrongholdCount = 0;
            int blockerCellCount = 0;
            int outOfMapCellCount = 0;
            int signature = 17;

            for (int i = 0; i < strongholds.Count; i++)
            {
                Stronghold stronghold = strongholds[i];
                if (stronghold == null
                    || stronghold.OwnerFactionId == playerFactionId
                    || stronghold.strongholdData == null
                    || stronghold.strongholdData.RangeCells == null
                    || stronghold.strongholdData.RangeCells.Count == 0)
                {
                    continue;
                }

                enemyStrongholdCount++;
                string strongholdId = stronghold.strongholdData.StrongholdId;
                signature = signature * 31 + stronghold.OwnerFactionId;
                signature = signature * 31 + (strongholdId != null ? strongholdId.GetHashCode() : 0);
                signature = signature * 31 + stronghold.strongholdData.RangeCells.Count;

                foreach (Vector2 strongholdCell in stronghold.strongholdData.RangeCells)
                {
                    int x = Mathf.RoundToInt(strongholdCell.x);
                    int y = Mathf.RoundToInt(strongholdCell.y);
                    if (!MapData.IsValidCell(x, y))
                    {
                        outOfMapCellCount++;
                        continue;
                    }

                    int index = x + y * width;
                    if (enemyStrongholdMask[index] != 0)
                        continue;

                    enemyStrongholdMask[index] = enemyStrongholdId;
                    blockerCellCount++;
                }

                enemyStrongholdId++;
            }

            hasEnemyStrongholdMask = blockerCellCount > 0;
            signature = hasEnemyStrongholdMask ? signature * 31 + blockerCellCount : 0;
            if (signature == enemyStrongholdMaskSignature)
                return;

            enemyStrongholdMaskSignature = signature;
            Debug.Log($"[FOG3] Enemy stronghold vision blockers refreshed. strongholds={enemyStrongholdCount}, cells={blockerCellCount}");

            if (enemyStrongholdCount > 0 && blockerCellCount == 0)
                Debug.LogWarning($"[FOG3] Enemy stronghold blocker mapping produced zero cells. outOfMapCells={outOfMapCellCount}");
        }

        private bool IsHiddenRevealBlockedByEnemyStronghold(int fromX, int fromY, int targetX, int targetY)
        {
            if (!hasEnemyStrongholdMask || enemyStrongholdMask == null)
                return false;

            int x = fromX;
            int y = fromY;
            int dx = Mathf.Abs(targetX - fromX);
            int dy = Mathf.Abs(targetY - fromY);
            int stepX = fromX < targetX ? 1 : -1;
            int stepY = fromY < targetY ? 1 : -1;
            int error = dx - dy;

            int activeStrongholdId = GetEnemyStrongholdId(x, y);

            while (x != targetX || y != targetY)
            {
                int twiceError = error << 1;
                if (twiceError > -dy)
                {
                    error -= dy;
                    x += stepX;
                }

                if (twiceError < dx)
                {
                    error += dx;
                    y += stepY;
                }

                int currentStrongholdId = GetEnemyStrongholdId(x, y);
                if (activeStrongholdId > 0)
                {
                    if (currentStrongholdId != activeStrongholdId)
                        return true;
                }
                else if (currentStrongholdId > 0)
                {
                    activeStrongholdId = currentStrongholdId;
                }
            }

            return false;
        }

        private int GetEnemyStrongholdId(int x, int y)
        {
            if (!MapData.IsValidCell(x, y) || enemyStrongholdMask == null)
                return 0;

            return enemyStrongholdMask[x + y * MapData.Width];
        }

        private static bool IsBlocked(Vector3 from, Vector3 to, LayerMask occluderMask)
        {
            if (occluderMask.value == 0)
                return false;

            return Physics.Linecast(from, to, occluderMask, QueryTriggerInteraction.Ignore);
        }
    }
}
