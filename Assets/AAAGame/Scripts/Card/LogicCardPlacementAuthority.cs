using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using Newtonsoft.Json;
using UnityEngine;

namespace AAAGame.Card
{
    public enum LogicCardPlacementInvalidReason
    {
        None = 0,
        Unexplored = 1,
        StaticForbiddenArea = 2,
        EnemyBuildingForbiddenArea = 3,
        EnemyStrongholdForbiddenArea = 4,
    }

    public static class LogicCardPlacementAuthority
    {
        private const string HeroVisionRadiusConfigKey = "HeroVisionRadius";
        private const string UnitVisionRadiusConfigKey = "UnitVisionRadius";
        private const string BuildingVisionRadiusConfigKey = "BuildingVisionRadius";
        private static readonly Fix64 s_EnemyBuildingBasePadding = (Fix64)3;
        private static readonly List<LogicCombatShape> s_StaticForbiddenShapes = new();
        private static Fog3MapData s_MapData;
        private static Fix64 s_HeroVisionRadius;
        private static Fix64 s_UnitVisionRadius;
        private static Fix64 s_BuildingVisionRadius;
        private static bool s_BlockHiddenRevealByEnemyStronghold;
        private static ulong s_StaticForbiddenHash;

        public static bool IsActive { get; private set; }
        public static bool IsWorldBound => s_MapData != null;
        public static ulong LastAppliedFrame { get; private set; }

        public static bool IsBoundTo(Fog3MapData mapData)
        {
            if (mapData == null)
                throw new ArgumentNullException(nameof(mapData));
            return ReferenceEquals(s_MapData, mapData);
        }

        public static void BeginTimeline()
        {
            if (IsActive)
                throw new InvalidOperationException("LogicCardPlacementAuthority.BeginTimeline failed: service is already active.");
            if (!LogicTimeControlService.IsActive)
                throw new InvalidOperationException("LogicCardPlacementAuthority.BeginTimeline failed: logic time control is not active.");
            IsActive = true;
            ClearWorld();
        }

        public static void EndTimeline()
        {
            EnsureActive();
            IsActive = false;
            ClearWorld();
        }

        public static void BindRuntimeWorld(
            Fog3MapData mapData,
            string levelIdentifier,
            bool blockHiddenRevealByEnemyStronghold)
        {
            if (GF.Config == null)
                throw new InvalidOperationException("LogicCardPlacementAuthority requires GF.Config before binding a world.");

            BindWorld(
                mapData,
                CardStaticForbiddenShapeCatalog.LoadRequired().ResolveRequired(levelIdentifier),
                ResolveVisionRadius(HeroVisionRadiusConfigKey),
                ResolveVisionRadius(UnitVisionRadiusConfigKey),
                ResolveVisionRadius(BuildingVisionRadiusConfigKey),
                blockHiddenRevealByEnemyStronghold);
        }

        public static void UnbindWorld()
        {
            EnsureActive();
            ClearWorld();
        }

        public static void ResetForWorldTransition()
        {
            EnsureActive();
            if (!LogicTimeControlService.IsPaused)
                throw new InvalidOperationException("LogicCardPlacementAuthority.ResetForWorldTransition failed: logic time is not paused.");
            ClearWorld();
        }

        public static void ResetFrameTimeline()
        {
            EnsureActive();
            if (LogicTimeControlService.CurrentFrame != 0)
                throw new InvalidOperationException("LogicCardPlacementAuthority.ResetFrameTimeline failed: time-control frame is not zero.");
            LastAppliedFrame = 0;
        }

        public static void ApplyFrame(ulong frameId)
        {
            EnsureBound();
            if (frameId == 0 || frameId != LogicTimeControlService.CurrentFrame)
            {
                throw new InvalidOperationException(
                    $"LogicCardPlacementAuthority.ApplyFrame frame mismatch. requested={frameId}, current={LogicTimeControlService.CurrentFrame}.");
            }
            if (frameId != LastAppliedFrame + 1)
            {
                throw new InvalidOperationException(
                    $"LogicCardPlacementAuthority.ApplyFrame requires contiguous frames. previous={LastAppliedFrame}, current={frameId}.");
            }

            RebuildVisibilityFromCurrentEntities();
            LastAppliedFrame = frameId;
        }

        private static void RebuildVisibilityFromCurrentEntities()
        {
            s_MapData.ClearCurrentVisibility();
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            bool hasGhostHero = HasPlayerGhostHero(entities);
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!TryGetRevealRadius(entity, out Fix64 radius)
                    || !CanRevealHidden(entity, hasGhostHero))
                    continue;
                RevealCircle(entity.PositionFixed, radius);
            }

            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!TryGetRevealRadius(entity, out Fix64 radius))
                    continue;
                AddCurrentVisibilityCircle(entity.PositionFixed, radius, CanRevealHidden(entity, hasGhostHero));
            }
        }

        public static LogicCardPlacementInvalidReason Evaluate(
            FixVector2 position,
            Fix64 placementRadius,
            GamePhase phase)
        {
            EnsureBound();
            if (placementRadius < Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(placementRadius));
            if (!Enum.IsDefined(typeof(GamePhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown game phase.");
            if (!TryWorldToCell(position, out int cellX, out int cellY)
                || !s_MapData.IsExplored(cellX, cellY))
            {
                return LogicCardPlacementInvalidReason.Unexplored;
            }

            if (phase == GamePhase.Defend
                && !LogicStrongholdMap.IsCircleClearOfForeignStrongholds(
                    position,
                    placementRadius,
                    EntitySideHelper.PlayerFactionId))
            {
                return LogicCardPlacementInvalidReason.EnemyStrongholdForbiddenArea;
            }

            for (int i = 0; i < s_StaticForbiddenShapes.Count; i++)
            {
                if (s_StaticForbiddenShapes[i].DistanceToSurface(position) <= placementRadius)
                    return LogicCardPlacementInvalidReason.StaticForbiddenArea;
            }

            Fix64 enemyPadding = ResolveEnemyBuildingPadding() + placementRadius;
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!entity.Alive || !entity.TryGetLogicBuilding(out IBuildingLogicContext building))
                    continue;
                if (building.BuildingData.Lv == 0 || building.OwnerFactionId == EntitySideHelper.PlayerFactionId)
                    continue;
                if (building.OwnerFactionId < 0)
                {
                    throw new InvalidOperationException(
                        $"Logic building {building.LogicEntityId.Value} has an invalid owner faction {building.OwnerFactionId}.");
                }
                if (building.CombatShape.DistanceToSurface(position) <= enemyPadding)
                    return LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea;
            }

            return LogicCardPlacementInvalidReason.None;
        }

        public static bool IsVisibleFromCurrentLogicRevealers(FixVector2 position)
        {
            EnsureBound();
            return s_MapData.GetCellState(position) == Fog3CellState.Visible;
        }

        public static Fix64 ResolveEnemyBuildingPadding()
        {
            return s_EnemyBuildingBasePadding * LevelTagRuntime.GetEnemyBuildingForbiddenZonePaddingMultiplier();
        }

        public static void WriteDeterministicState(LogicStateHasher hasher)
        {
            EnsureActive();
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));
            hasher.Add(IsWorldBound);
            if (!IsWorldBound)
                return;
            hasher.Add(LastAppliedFrame);
            hasher.Add(s_MapData.TerrainHash);
            hasher.Add(s_MapData.ExploredCellCount);
            hasher.Add(s_MapData.ExplorationXorDigest);
            hasher.Add(s_MapData.ExplorationSumDigest);
            hasher.Add(s_StaticForbiddenHash);
            hasher.Add(s_HeroVisionRadius.RawValue);
            hasher.Add(s_UnitVisionRadius.RawValue);
            hasher.Add(s_BuildingVisionRadius.RawValue);
            hasher.Add(ResolveEnemyBuildingPadding().RawValue);
            hasher.Add(s_BlockHiddenRevealByEnemyStronghold);
        }

#if UNITY_EDITOR
        public static void BindWorldForTests(
            Fog3MapData mapData,
            IReadOnlyList<LogicCombatShape> staticForbiddenShapes,
            Fix64 heroVisionRadius,
            Fix64 unitVisionRadius,
            Fix64 buildingVisionRadius,
            bool blockHiddenRevealByEnemyStronghold = false)
        {
            BindWorld(
                mapData,
                staticForbiddenShapes,
                heroVisionRadius,
                unitVisionRadius,
                buildingVisionRadius,
                blockHiddenRevealByEnemyStronghold);
        }

        public static void RevealCircleForTests(FixVector2 position, Fix64 radius)
        {
            EnsureBound();
            RevealCircle(position, radius);
        }
#endif

        private static void BindWorld(
            Fog3MapData mapData,
            IReadOnlyList<LogicCombatShape> staticForbiddenShapes,
            Fix64 heroVisionRadius,
            Fix64 unitVisionRadius,
            Fix64 buildingVisionRadius,
            bool blockHiddenRevealByEnemyStronghold)
        {
            EnsureActive();
            if (IsWorldBound)
                throw new InvalidOperationException("LogicCardPlacementAuthority.BindWorld failed: a world is already bound.");
            if (mapData == null)
                throw new ArgumentNullException(nameof(mapData));
            if (staticForbiddenShapes == null)
                throw new ArgumentNullException(nameof(staticForbiddenShapes));
            if (heroVisionRadius <= Fix64.Zero || unitVisionRadius <= Fix64.Zero || buildingVisionRadius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(heroVisionRadius), "All card-placement vision radii must be positive.");
            if (blockHiddenRevealByEnemyStronghold && !LogicStrongholdMap.IsInitialized)
            {
                throw new InvalidOperationException(
                    "LogicCardPlacementAuthority requires LogicStrongholdMap when enemy strongholds block hidden reveal.");
            }

            s_MapData = mapData;
            s_HeroVisionRadius = heroVisionRadius;
            s_UnitVisionRadius = unitVisionRadius;
            s_BuildingVisionRadius = buildingVisionRadius;
            s_BlockHiddenRevealByEnemyStronghold = blockHiddenRevealByEnemyStronghold;
            s_StaticForbiddenShapes.Clear();
            for (int i = 0; i < staticForbiddenShapes.Count; i++)
                s_StaticForbiddenShapes.Add(staticForbiddenShapes[i]);
            s_StaticForbiddenShapes.Sort(CompareShapes);
            s_StaticForbiddenHash = ComputeStaticForbiddenHash(s_StaticForbiddenShapes);
            LastAppliedFrame = LogicTimeControlService.CurrentFrame;
            if (LastAppliedFrame != 0)
                RebuildVisibilityFromCurrentEntities();
        }

        private static Fix64 ResolveVisionRadius(string configKey)
        {
            Fix64 configured = DistanceUnitConverter.ReadRequiredPositiveFixedConfig(configKey);
            Fix64 radius = DistanceUnitConverter.ConvertToWorld(configured);
            if (radius <= Fix64.Zero)
                throw new InvalidOperationException($"Logic card-placement vision config '{configKey}' converted to {radius.RawValue} raw.");
            return radius;
        }

        private static void RevealCircle(FixVector2 position, Fix64 radius)
        {
            if (radius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!TryWorldToCell(position, out int centerX, out int centerY))
                return;

            int range = s_MapData.GetCellRangeForRadius(radius);
            Fix64 radiusSquared = radius * radius;
            for (int y = centerY - range; y <= centerY + range; y++)
            {
                for (int x = centerX - range; x <= centerX + range; x++)
                {
                    if (!s_MapData.IsWalkable(x, y))
                        continue;
                    FixVector2 cellCenter = s_MapData.GetCellCenterFixed(x, y);
                    if (FixVector2.SqrMagnitude(cellCenter - position) > radiusSquared)
                        continue;
                    if (s_BlockHiddenRevealByEnemyStronghold
                        && IsHiddenRevealBlockedByEnemyStronghold(centerX, centerY, x, y))
                    {
                        continue;
                    }
                    s_MapData.MarkExplored(x, y);
                }
            }
        }

        private static void AddCurrentVisibilityCircle(
            FixVector2 position,
            Fix64 radius,
            bool canRevealHidden)
        {
            if (radius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!TryWorldToCell(position, out int centerX, out int centerY))
                return;

            int range = s_MapData.GetCellRangeForRadius(radius);
            Fix64 radiusSquared = radius * radius;
            for (int y = centerY - range; y <= centerY + range; y++)
            {
                for (int x = centerX - range; x <= centerX + range; x++)
                {
                    if (!s_MapData.IsWalkable(x, y))
                        continue;
                    FixVector2 cellCenter = s_MapData.GetCellCenterFixed(x, y);
                    if (FixVector2.SqrMagnitude(cellCenter - position) > radiusSquared)
                        continue;

                    bool targetExplored = s_MapData.IsExplored(x, y);
                    if (!targetExplored && !canRevealHidden)
                        continue;
                    if (!targetExplored
                        && s_BlockHiddenRevealByEnemyStronghold
                        && IsHiddenRevealBlockedByEnemyStronghold(centerX, centerY, x, y))
                    {
                        continue;
                    }

                    s_MapData.MarkVisible(x, y);
                }
            }
        }

        private static bool TryWorldToCell(FixVector2 position, out int x, out int y)
        {
            return s_MapData.WorldToGrid(position, out x, out y);
        }

        private static bool IsHiddenRevealBlockedByEnemyStronghold(int fromX, int fromY, int targetX, int targetY)
        {
            int x = fromX;
            int y = fromY;
            int dx = Math.Abs(targetX - fromX);
            int dy = Math.Abs(targetY - fromY);
            int stepX = fromX < targetX ? 1 : -1;
            int stepY = fromY < targetY ? 1 : -1;
            int error = dx - dy;
            string activeStrongholdId = ResolveEnemyStrongholdId(x, y);

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

                string currentStrongholdId = ResolveEnemyStrongholdId(x, y);
                if (activeStrongholdId != null)
                {
                    if (!string.Equals(currentStrongholdId, activeStrongholdId, StringComparison.Ordinal))
                        return true;
                }
                else if (currentStrongholdId != null)
                {
                    activeStrongholdId = currentStrongholdId;
                }
            }

            return false;
        }

        private static string ResolveEnemyStrongholdId(int x, int y)
        {
            if (!LogicStrongholdMap.TryGetStrongholdIdAtCell(x, y, out string strongholdId))
                return null;
            return LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) == EntitySideHelper.PlayerFactionId
                ? null
                : strongholdId;
        }

        private static bool HasPlayerGhostHero(IList<IEntityContext> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity != null && entity.Alive && entity.Side == SideType.PlayerSide && IsGhostHero(entity))
                    return true;
            }
            return false;
        }

        private static bool TryGetRevealRadius(IEntityContext entity, out Fix64 radius)
        {
            radius = Fix64.Zero;
            if (!entity.Alive || entity.Side != SideType.PlayerSide)
                return false;

            bool isBuilding = entity.TryGetLogicBuilding(out _);
            bool isHero = IsLogicHero(entity);
            radius = isBuilding
                ? s_BuildingVisionRadius
                : isHero
                    ? s_HeroVisionRadius
                    : s_UnitVisionRadius;
            return true;
        }

        private static bool CanRevealHidden(IEntityContext entity, bool hasGhostHero)
        {
            bool isHero = IsLogicHero(entity);
            bool isBuilding = entity.TryGetLogicBuilding(out _);
            return !(isHero && IsGhostHero(entity))
                   && !(hasGhostHero && !isHero && !isBuilding);
        }

        private static bool IsLogicHero(IEntityContext entity)
        {
            return entity is IHeroLogicContext hero && hero.IsHeroEntity;
        }

        private static bool IsGhostHero(IEntityContext entity)
        {
            return IsLogicHero(entity) && entity is IHeroLogicContext hero && hero.IsGhostState;
        }

        private static int CompareShapes(LogicCombatShape left, LogicCombatShape right)
        {
            int result = left.Center.x.RawValue.CompareTo(right.Center.x.RawValue);
            if (result != 0) return result;
            result = left.Center.y.RawValue.CompareTo(right.Center.y.RawValue);
            if (result != 0) return result;
            result = ((int)left.Kind).CompareTo((int)right.Kind);
            if (result != 0) return result;
            result = left.Radius.RawValue.CompareTo(right.Radius.RawValue);
            if (result != 0) return result;
            result = left.HalfExtents.x.RawValue.CompareTo(right.HalfExtents.x.RawValue);
            return result != 0 ? result : left.HalfExtents.y.RawValue.CompareTo(right.HalfExtents.y.RawValue);
        }

        private static ulong ComputeStaticForbiddenHash(IReadOnlyList<LogicCombatShape> shapes)
        {
            var hasher = new LogicStateHasher();
            hasher.Add(0x43415244464F5242UL);
            hasher.Add(shapes.Count);
            for (int i = 0; i < shapes.Count; i++)
            {
                LogicCombatShape shape = shapes[i];
                hasher.Add((int)shape.Kind);
                hasher.Add(shape.Center.x.RawValue);
                hasher.Add(shape.Center.y.RawValue);
                hasher.Add(shape.Radius.RawValue);
                hasher.Add(shape.HalfExtents.x.RawValue);
                hasher.Add(shape.HalfExtents.y.RawValue);
            }
            return hasher.Hash;
        }

        private static void ClearWorld()
        {
            s_MapData = null;
            s_HeroVisionRadius = Fix64.Zero;
            s_UnitVisionRadius = Fix64.Zero;
            s_BuildingVisionRadius = Fix64.Zero;
            s_BlockHiddenRevealByEnemyStronghold = false;
            s_StaticForbiddenHash = 0;
            s_StaticForbiddenShapes.Clear();
            LastAppliedFrame = 0;
        }

        private static void EnsureActive()
        {
            if (!IsActive || !LogicTimeControlService.IsActive)
                throw new InvalidOperationException("LogicCardPlacementAuthority operation failed: service is not active.");
        }

        private static void EnsureBound()
        {
            EnsureActive();
            if (!IsWorldBound)
                throw new InvalidOperationException("LogicCardPlacementAuthority operation failed: no world is bound.");
        }
    }

    public sealed class CardStaticForbiddenShapeCatalog
    {
        [Serializable]
        public sealed class BoxEntry
        {
            public long CenterXRaw;
            public long CenterZRaw;
            public long HalfExtentXRaw;
            public long HalfExtentZRaw;
        }

        [Serializable]
        public sealed class LevelEntry
        {
            public string LevelIdentifier;
            public List<BoxEntry> Boxes;
        }

        private const string ResourcePath = "CardStaticForbiddenShapeCatalogData";
        private static CardStaticForbiddenShapeCatalog s_Cached;
        private Dictionary<string, LevelEntry> m_ByLevelIdentifier;

        public List<LevelEntry> Entries { get; private set; }

        public static void PrepareRuntimeDependencies()
        {
            if (LogicFrameRuntime.IsExecutingFrame)
                throw new InvalidOperationException("Card static forbidden-shape catalog cannot be prepared during a logic frame.");
            if (s_Cached != null)
                return;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Card static forbidden-shape catalog is missing at Resources/{ResourcePath}.json.");
            }
            List<LevelEntry> entries = JsonConvert.DeserializeObject<List<LevelEntry>>(asset.text);
            if (entries == null)
                throw new InvalidOperationException("Card static forbidden-shape catalog JSON did not contain an entry array.");
            s_Cached = new CardStaticForbiddenShapeCatalog { Entries = entries };
            s_Cached.EnsureIndex();
        }

        public static CardStaticForbiddenShapeCatalog LoadRequired()
        {
            if (s_Cached == null)
            {
                if (LogicFrameRuntime.IsExecutingFrame)
                    throw new InvalidOperationException("Card static forbidden-shape catalog was not prepared before the logic frame.");
                PrepareRuntimeDependencies();
            }
            s_Cached.EnsureIndex();
            return s_Cached;
        }

        public IReadOnlyList<LogicCombatShape> ResolveRequired(string levelIdentifier)
        {
            if (string.IsNullOrWhiteSpace(levelIdentifier))
                throw new ArgumentException("Card static forbidden-shape lookup requires a level identifier.", nameof(levelIdentifier));
            EnsureIndex();
            if (!m_ByLevelIdentifier.TryGetValue(levelIdentifier, out LevelEntry entry))
                throw new InvalidOperationException($"Card static forbidden shapes are not authored for level '{levelIdentifier}'.");
            if (entry.Boxes == null)
                throw new InvalidOperationException($"Card static forbidden-shape entry '{levelIdentifier}' has a null box list.");

            var result = new LogicCombatShape[entry.Boxes.Count];
            for (int i = 0; i < entry.Boxes.Count; i++)
            {
                BoxEntry box = entry.Boxes[i]
                               ?? throw new InvalidOperationException(
                                   $"Card static forbidden-shape entry '{levelIdentifier}' contains a null box at index {i}.");
                FixVector2 halfExtents = new FixVector2(
                    Fix64.FromRaw(box.HalfExtentXRaw),
                    Fix64.FromRaw(box.HalfExtentZRaw));
                if (halfExtents.x <= Fix64.Zero || halfExtents.y <= Fix64.Zero)
                {
                    throw new InvalidOperationException(
                        $"Card static forbidden-shape entry '{levelIdentifier}' box {i} has invalid half extents.");
                }
                result[i] = LogicCombatShape.AxisAlignedBox(
                    new FixVector2(Fix64.FromRaw(box.CenterXRaw), Fix64.FromRaw(box.CenterZRaw)),
                    halfExtents);
            }
            return result;
        }

        private void EnsureIndex()
        {
            if (m_ByLevelIdentifier != null)
                return;
            if (Entries == null)
                throw new InvalidOperationException("Card static forbidden-shape catalog entries are null.");
            m_ByLevelIdentifier = new Dictionary<string, LevelEntry>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                LevelEntry entry = Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.LevelIdentifier))
                    throw new InvalidOperationException($"Card static forbidden-shape catalog entry {i} is invalid.");
                if (!m_ByLevelIdentifier.TryAdd(entry.LevelIdentifier, entry))
                    throw new InvalidOperationException($"Duplicate card static forbidden-shape level '{entry.LevelIdentifier}'.");
            }
        }
    }
}
