using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;
using Newtonsoft.Json;
using UnityEngine;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        private const string EnemyStrongholdFriendlyUnitDeployRadiusConfigKey = "EnemyStrongholdFriendlyUnitDeployRadius";
        private static readonly List<LogicCombatShape> s_StaticForbiddenShapes = new();
        private static readonly Dictionary<long, CachedVisibilitySource> s_VisibilitySources = new();
        private static readonly HashSet<int> s_DirtyEntityIds = new();
        private static readonly HashSet<int> s_ContinuouslyMovedEntityIds = new();
        private static readonly HashSet<int> s_SeenEntityIds = new();
        private static readonly HashSet<long> s_SeenStationarySourceKeys = new();
        private static readonly List<long> s_RemovedVisibilitySourceKeys = new();
        private static readonly List<Fog3VisibilityRowInterval> s_GeometryIntervals = new();
        private static readonly List<Fog3VisibilityRowInterval> s_NextVisibilityIntervals = new();
        private static Fog3MapData s_MapData;
        private static Fix64 s_HeroVisionRadius;
        private static Fix64 s_UnitVisionRadius;
        private static Fix64 s_BuildingVisionRadius;
        private static Fix64 s_EnemyStrongholdFriendlyUnitDeployRadius;
        private static bool s_BlockHiddenRevealByEnemyStronghold;
        private static ulong s_StaticForbiddenHash;
        private static ulong s_ObservedVisibilityResetVersion;
        private static bool s_AllEntityVisibilityDirty;
        private static bool s_StationaryVisibilityDirty;

        private readonly struct VisibilitySourceState : IEquatable<VisibilitySourceState>
        {
            public VisibilitySourceState(
                FixVector2 position,
                Fix64 radius,
                int viewerHeight,
                int requiredHeight,
                bool canExploreHiddenFog)
            {
                Position = position;
                Radius = radius;
                ViewerHeight = viewerHeight;
                RequiredHeight = requiredHeight;
                CanExploreHiddenFog = canExploreHiddenFog;
            }

            public FixVector2 Position { get; }
            public Fix64 Radius { get; }
            public int ViewerHeight { get; }
            public int RequiredHeight { get; }
            public bool CanExploreHiddenFog { get; }

            public bool Equals(VisibilitySourceState other)
            {
                return Position.x == other.Position.x
                       && Position.y == other.Position.y
                       && Radius == other.Radius
                       && ViewerHeight == other.ViewerHeight
                       && RequiredHeight == other.RequiredHeight
                       && CanExploreHiddenFog == other.CanExploreHiddenFog;
            }
        }

        private sealed class CachedVisibilitySource
        {
            public VisibilitySourceState State;
            public readonly List<Fog3VisibilityRowInterval> Intervals = new();
            public double SnapshotLogicTime;
            public bool HasState;
        }

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
                ResolvePositiveConfig(EnemyStrongholdFriendlyUnitDeployRadiusConfigKey),
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

            ConsumeVisibilityDirty();
            LastAppliedFrame = frameId;
        }

        private static void ConsumeVisibilityDirty()
        {
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            if (s_ObservedVisibilityResetVersion != s_MapData.VisibilityResetVersion)
            {
                s_VisibilitySources.Clear();
                s_ObservedVisibilityResetVersion = s_MapData.VisibilityResetVersion;
                s_AllEntityVisibilityDirty = true;
                s_StationaryVisibilityDirty = true;
            }
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityReset,
                    Stopwatch.GetTimestamp() - stageStartTicks);
            }

            if (!s_AllEntityVisibilityDirty && s_DirtyEntityIds.Count == 0 && !s_StationaryVisibilityDirty)
                return;

            ulong explorationVersionBeforeUpdate = s_MapData.ExplorationVersion;
            if (s_AllEntityVisibilityDirty)
            {
                stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                SynchronizeAllEntityVisibilitySources();
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.LogicFrameCommandCardPlacementAllEntities,
                        Stopwatch.GetTimestamp() - stageStartTicks);
                }
                s_AllEntityVisibilityDirty = false;
                s_DirtyEntityIds.Clear();
                s_ContinuouslyMovedEntityIds.Clear();
            }
            else
            {
                stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                SynchronizeDirtyEntityVisibilitySources();
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.LogicFrameCommandCardPlacementDirtyEntities,
                        Stopwatch.GetTimestamp() - stageStartTicks);
                }
                s_DirtyEntityIds.Clear();
                s_ContinuouslyMovedEntityIds.Clear();
            }

            if (s_StationaryVisibilityDirty)
            {
                stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                SynchronizeStationaryVisibilitySources();
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.LogicFrameCommandCardPlacementStationary,
                        Stopwatch.GetTimestamp() - stageStartTicks);
                }
                s_StationaryVisibilityDirty = false;
            }
            if (s_MapData.ExplorationVersion != explorationVersionBeforeUpdate)
            {
                stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                RefreshExplorationDependentVisibilitySources();
                if (profile)
                {
                    MainThreadFrameProfiler.Record(
                        MainThreadPerfScope.LogicFrameCommandCardPlacementExploration,
                        Stopwatch.GetTimestamp() - stageStartTicks);
                }
            }
            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            s_MapData.ResolveVisibilityCoverageChanges(ResolveCurrentVisibilitySourceLogicTime());
            if (profile)
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementCoverage,
                    Stopwatch.GetTimestamp() - stageStartTicks);
            }
        }

        private static void SynchronizeAllEntityVisibilitySources()
        {
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            bool hasGhostHero = HasPlayerGhostHero(entities);
            s_SeenEntityIds.Clear();
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i]
                    ?? throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!entity.LogicEntityId.IsValid)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found an invalid entity id at registry index {i}.");
                s_SeenEntityIds.Add(entity.LogicEntityId.Value);
                SynchronizeEntityVisibilitySource(entity, hasGhostHero, false);
            }

            s_RemovedVisibilitySourceKeys.Clear();
            foreach (KeyValuePair<long, CachedVisibilitySource> pair in s_VisibilitySources)
            {
                if (pair.Key >= 0 && !s_SeenEntityIds.Contains((int)(pair.Key >> 1)))
                    s_RemovedVisibilitySourceKeys.Add(pair.Key);
            }
            RemoveVisibilitySources(s_RemovedVisibilitySourceKeys);
        }

        private static void SynchronizeDirtyEntityVisibilitySources()
        {
            bool hasGhostHero = HasPlayerGhostHero(EntityRegistry.AllEntities);
            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long lookupTicks = 0L;
            foreach (int entityId in s_DirtyEntityIds)
            {
                long lookupStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                var logicEntityId = new LogicEntityId(entityId);
                bool found = EntityRegistry.TryGet(logicEntityId, out IEntityContext entity);
                if (profile)
                    lookupTicks += Stopwatch.GetTimestamp() - lookupStartTicks;
                if (found)
                    SynchronizeEntityVisibilitySource(
                        entity,
                        hasGhostHero,
                        s_ContinuouslyMovedEntityIds.Contains(entityId));
                else
                    RemoveEntityVisibilitySources(entityId);
            }
            if (profile && lookupTicks > 0L)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementDirtyLookup,
                    lookupTicks);
        }

        private static void SynchronizeEntityVisibilitySource(
            IEntityContext entity,
            bool hasGhostHero,
            bool continuousMovement)
        {
            if (!TryGetRevealRadius(entity, out Fix64 radius))
            {
                RemoveEntityVisibilitySources(entity.LogicEntityId.Value);
                return;
            }
            UpdateEntityVisibilitySources(
                entity,
                radius,
                CanExploreHiddenFog(entity, hasGhostHero),
                continuousMovement);
        }

        private static void SynchronizeStationaryVisibilitySources()
        {
            s_SeenStationarySourceKeys.Clear();
            LogicFactionVisionService.VisitStationaryReveals(
                SideType.PlayerSide,
                UpdateDamageAlertVisibilitySource);
            s_RemovedVisibilitySourceKeys.Clear();
            foreach (KeyValuePair<long, CachedVisibilitySource> pair in s_VisibilitySources)
            {
                if (pair.Key < 0 && !s_SeenStationarySourceKeys.Contains(pair.Key))
                    s_RemovedVisibilitySourceKeys.Add(pair.Key);
            }
            RemoveVisibilitySources(s_RemovedVisibilitySourceKeys);
        }

        private static void UpdateDamageAlertVisibilitySource(int revealId, FixVector2 position, Fix64 radius, int sourceHeight)
        {
            if (revealId <= 0)
                throw new ArgumentOutOfRangeException(nameof(revealId));
            long sourceKey = long.MinValue + revealId;
            s_SeenStationarySourceKeys.Add(sourceKey);
            UpdateVisibilitySource(
                sourceKey,
                CreateVisibilitySourceState(position, radius, sourceHeight, sourceHeight, true),
                false);
        }

        private static void UpdateEntityVisibilitySources(
            IEntityContext entity,
            Fix64 radius,
            bool canExploreHiddenFog,
            bool continuousMovement)
        {
            if (!TryWorldToCell(entity.PositionFixed, out int x, out int y))
            {
                RemoveEntityVisibilitySources(entity.LogicEntityId.Value);
                return;
            }

            long baseKey = checked((long)entity.LogicEntityId.Value << 1);
            int viewerHeight = s_MapData.GetVisionHeight(entity.PositionFixed);
            UpdateVisibilitySource(
                baseKey,
                CreateVisibilitySourceState(entity.PositionFixed, radius, viewerHeight, -1, canExploreHiddenFog),
                false,
                continuousMovement);

            if (entity.IsLogicBuilding()
                || !s_MapData.IsSlope(x, y))
            {
                long upperKey = baseKey | 1L;
                if (s_VisibilitySources.TryGetValue(upperKey, out CachedVisibilitySource upperCached))
                {
                    ChangeVisibilityIntervals(upperCached.Intervals, -1);
                    s_VisibilitySources.Remove(upperKey);
                }
                return;
            }

            Fix64 upperRadius = ResolveVisionRadius(LogicFactionVisionService.SlopeUpperVisionRadiusConfigKey);
            int upperHeight = viewerHeight + 1;
            UpdateVisibilitySource(
                baseKey | 1L,
                CreateVisibilitySourceState(entity.PositionFixed, upperRadius, upperHeight, upperHeight, canExploreHiddenFog),
                false,
                continuousMovement);
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

            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!entity.Alive || !entity.TryGetLogicBuilding(out IBuildingLogicContext building))
                    continue;
                if (!GeneratesEnemyBuildingForbiddenZone(building))
                    continue;
                if (building.CombatShape.DistanceToSurface(position) <= placementRadius)
                    return LogicCardPlacementInvalidReason.EnemyBuildingForbiddenArea;
            }

            if (LogicStrongholdMap.TryResolveStrongholdId(position, out string strongholdId)
                && LogicStrongholdMap.GetOwnerFactionIdRequired(strongholdId) != EntitySideHelper.PlayerFactionId
                && !IsWithinFriendlyUnitDeployRadius(position, entities))
            {
                return LogicCardPlacementInvalidReason.EnemyStrongholdForbiddenArea;
            }

            return LogicCardPlacementInvalidReason.None;
        }

        private static bool IsWithinFriendlyUnitDeployRadius(
            FixVector2 position,
            IList<IEntityContext> entities)
        {
            Fix64 radiusSquared = s_EnemyStrongholdFriendlyUnitDeployRadius
                                  * s_EnemyStrongholdFriendlyUnitDeployRadius;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!entity.Alive
                    || entity.Side != SideType.PlayerSide
                    || entity.IsLogicBuilding()
                    || entity.TryGetLogicHero(out IHeroLogicContext hero) && hero.IsGhostState)
                {
                    continue;
                }

                if (FixVector2.SqrMagnitude(entity.PositionFixed - position) <= radiusSquared)
                    return true;
            }

            return false;
        }

        public static bool GeneratesEnemyBuildingForbiddenZone(IBuildingLogicContext building)
        {
            if (building == null)
                throw new ArgumentNullException(nameof(building));
            if (building.OwnerFactionId < 0)
            {
                throw new InvalidOperationException(
                    $"Logic building {building.LogicEntityId.Value} has an invalid owner faction {building.OwnerFactionId}.");
            }

            return building.BuildingData.Lv > 0
                   && building.OwnerFactionId != EntitySideHelper.PlayerFactionId
                   && !building.IsStealthed;
        }

        public static bool IsInsideStealthedBuildingCollision(FixVector2 position)
        {
            IList<IEntityContext> entities = EntityRegistry.AllEntities;
            for (int i = 0; i < entities.Count; i++)
            {
                IEntityContext entity = entities[i];
                if (entity == null)
                    throw new InvalidOperationException($"LogicCardPlacementAuthority found a null registry entity at index {i}.");
                if (!entity.Alive
                    || !entity.TryGetLogicBuilding(out IBuildingLogicContext building)
                    || !building.IsStealthed
                    || !building.BlocksLogicMovement)
                {
                    continue;
                }

                IReadOnlyList<LogicCombatShape> obstacleShapes = building.LogicObstacleShapes;
                for (int shapeIndex = 0; shapeIndex < obstacleShapes.Count; shapeIndex++)
                {
                    if (obstacleShapes[shapeIndex].DistanceToSurface(position) <= Fix64.Zero)
                        return true;
                }
            }

            return false;
        }

        public static bool IsVisibleFromCurrentLogicRevealers(FixVector2 position)
        {
            EnsureBound();
            return s_MapData.GetCellState(position) == Fog3CellState.Visible;
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
            hasher.Add(s_EnemyStrongholdFriendlyUnitDeployRadius.RawValue);
            hasher.Add(s_BlockHiddenRevealByEnemyStronghold);
        }

#if UNITY_EDITOR
        public static void BindWorldForTests(
            Fog3MapData mapData,
            IReadOnlyList<LogicCombatShape> staticForbiddenShapes,
            Fix64 heroVisionRadius,
            Fix64 unitVisionRadius,
            Fix64 buildingVisionRadius,
            bool blockHiddenRevealByEnemyStronghold = false,
            Fix64 enemyStrongholdFriendlyUnitDeployRadius = default)
        {
            if (enemyStrongholdFriendlyUnitDeployRadius == Fix64.Zero)
                enemyStrongholdFriendlyUnitDeployRadius = (Fix64)3;
            BindWorld(
                mapData,
                staticForbiddenShapes,
                heroVisionRadius,
                unitVisionRadius,
                buildingVisionRadius,
                enemyStrongholdFriendlyUnitDeployRadius,
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
            Fix64 enemyStrongholdFriendlyUnitDeployRadius,
            bool blockHiddenRevealByEnemyStronghold)
        {
            EnsureActive();
            if (IsWorldBound)
                throw new InvalidOperationException("LogicCardPlacementAuthority.BindWorld failed: a world is already bound.");
            if (mapData == null)
                throw new ArgumentNullException(nameof(mapData));
            if (staticForbiddenShapes == null)
                throw new ArgumentNullException(nameof(staticForbiddenShapes));
            if (heroVisionRadius <= Fix64.Zero
                || unitVisionRadius <= Fix64.Zero
                || buildingVisionRadius <= Fix64.Zero
                || enemyStrongholdFriendlyUnitDeployRadius <= Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heroVisionRadius),
                    "All card-placement radii must be positive.");
            }
            if (blockHiddenRevealByEnemyStronghold && !LogicStrongholdMap.IsInitialized)
            {
                throw new InvalidOperationException(
                    "LogicCardPlacementAuthority requires LogicStrongholdMap when enemy strongholds block hidden reveal.");
            }

            s_MapData = mapData;
            LogicFactionVisionService.BindMap(mapData);
            EntityRegistry.Registered += OnEntityRegistered;
            EntityRegistry.Unregistered += OnEntityUnregistered;
            LogicFactionVisionService.EntityVisibilityInputChanged += OnEntityVisibilityInputChanged;
            LogicFactionVisionService.AllEntityVisibilityInputsChanged += OnAllEntityVisibilityInputsChanged;
            LogicFactionVisionService.StationaryRevealsChanged += OnStationaryRevealsChanged;
            s_MapData.VisibilityReset += OnVisibilityReset;
            s_HeroVisionRadius = heroVisionRadius;
            s_UnitVisionRadius = unitVisionRadius;
            s_BuildingVisionRadius = buildingVisionRadius;
            s_EnemyStrongholdFriendlyUnitDeployRadius = enemyStrongholdFriendlyUnitDeployRadius;
            s_BlockHiddenRevealByEnemyStronghold = blockHiddenRevealByEnemyStronghold;
            s_StaticForbiddenShapes.Clear();
            for (int i = 0; i < staticForbiddenShapes.Count; i++)
                s_StaticForbiddenShapes.Add(staticForbiddenShapes[i]);
            s_StaticForbiddenShapes.Sort(CompareShapes);
            s_StaticForbiddenHash = ComputeStaticForbiddenHash(s_StaticForbiddenShapes);
            s_VisibilitySources.Clear();
            s_ObservedVisibilityResetVersion = s_MapData.VisibilityResetVersion;
            s_AllEntityVisibilityDirty = true;
            s_StationaryVisibilityDirty = true;
            LastAppliedFrame = LogicTimeControlService.CurrentFrame;
            if (LastAppliedFrame != 0)
                ConsumeVisibilityDirty();
        }

        private static Fix64 ResolveVisionRadius(string configKey)
        {
            Fix64 radius = ResolvePositiveConfig(configKey);
            if (radius <= Fix64.Zero)
                throw new InvalidOperationException($"Logic card-placement vision config '{configKey}' converted to {radius.RawValue} raw.");
            return radius;
        }

        private static Fix64 ResolvePositiveConfig(string configKey)
        {
            return FixedConfigReader.ReadRequiredPositiveFixedConfig(configKey);
        }

        private static void RevealCircle(FixVector2 position, Fix64 radius)
        {
            if (radius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!TryWorldToCell(position, out int centerX, out int centerY))
                return;

            int viewerHeight = s_MapData.GetVisionHeight(position);
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
                    if (s_MapData.IsVisionBlockedByHigherPlatform(centerX, centerY, viewerHeight, x, y))
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

        private static VisibilitySourceState CreateVisibilitySourceState(
            FixVector2 position,
            Fix64 radius,
            int viewerHeight,
            int requiredHeight,
            bool canExploreHiddenFog)
        {
            if (radius <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!TryWorldToCell(position, out int centerX, out int centerY))
                throw new ArgumentOutOfRangeException(nameof(position), $"Fog visibility source {position} is outside the map.");
            if (viewerHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(viewerHeight));
            if (requiredHeight < -1)
                throw new ArgumentOutOfRangeException(nameof(requiredHeight));

            return new VisibilitySourceState(
                position,
                radius,
                viewerHeight,
                requiredHeight,
                canExploreHiddenFog);
        }

        private static void UpdateVisibilitySource(
            long sourceKey,
            VisibilitySourceState state,
            bool forceRefresh,
            bool continuousMovement = false)
        {
            if (!s_VisibilitySources.TryGetValue(sourceKey, out CachedVisibilitySource cached))
            {
                cached = new CachedVisibilitySource();
                s_VisibilitySources.Add(sourceKey, cached);
            }
            else if (!forceRefresh && cached.State.Equals(state))
            {
                return;
            }

            bool profile = MainThreadFrameProfiler.LoggingEnabled;
            long stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            s_NextVisibilityIntervals.Clear();
            s_MapData.CollectVisibleIntervals(
                state.Position,
                state.Radius,
                state.ViewerHeight,
                state.RequiredHeight >= 0 ? state.RequiredHeight : null,
                s_GeometryIntervals);
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityCollect,
                    Stopwatch.GetTimestamp() - stageStartTicks);

            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            FilterAndExploreVisibilityIntervals(state, s_NextVisibilityIntervals);
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityFilter,
                    Stopwatch.GetTimestamp() - stageStartTicks);

            double currentSnapshotLogicTime = ResolveCurrentVisibilitySourceLogicTime();
            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            if (continuousMovement
                && cached.HasState
                && cached.State.Position != state.Position
                && cached.State.Radius == state.Radius
                && cached.State.ViewerHeight == state.ViewerHeight
                && cached.State.RequiredHeight == state.RequiredHeight
                && cached.State.CanExploreHiddenFog == state.CanExploreHiddenFog)
            {
                RecordMovingVisibilityTransitionTimes(
                    cached.Intervals,
                    s_NextVisibilityIntervals,
                    cached.State,
                    state,
                    currentSnapshotLogicTime - 1d / LogicFrameRuntime.FrameRate,
                    currentSnapshotLogicTime);
            }

            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityTransition,
                    Stopwatch.GetTimestamp() - stageStartTicks);

            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            ChangeVisibilityIntervals(cached.Intervals, -1);
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityRemoveCoverage,
                    Stopwatch.GetTimestamp() - stageStartTicks);

            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            ChangeVisibilityIntervals(s_NextVisibilityIntervals, 1);
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityAddCoverage,
                    Stopwatch.GetTimestamp() - stageStartTicks);

            stageStartTicks = profile ? Stopwatch.GetTimestamp() : 0L;
            cached.Intervals.Clear();
            cached.Intervals.AddRange(s_NextVisibilityIntervals);
            s_NextVisibilityIntervals.Clear();
            cached.State = state;
            cached.SnapshotLogicTime = currentSnapshotLogicTime;
            cached.HasState = true;
            if (profile)
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.LogicFrameCommandCardPlacementVisibilityPublish,
                    Stopwatch.GetTimestamp() - stageStartTicks);
        }

        private static double ResolveCurrentVisibilitySourceLogicTime()
        {
            return LogicTimeControlService.CurrentFrame / (double)LogicFrameRuntime.FrameRate;
        }

        private static void RecordMovingVisibilityTransitionTimes(
            List<Fog3VisibilityRowInterval> previousIntervals,
            List<Fog3VisibilityRowInterval> currentIntervals,
            VisibilitySourceState previousState,
            VisibilitySourceState currentState,
            double previousLogicTime,
            double currentLogicTime)
        {
            RecordIntervalDifferenceTransitionTimes(
                currentIntervals,
                previousIntervals,
                previousState.Position,
                currentState.Position,
                currentState.Radius,
                true,
                previousLogicTime,
                currentLogicTime);
            RecordIntervalDifferenceTransitionTimes(
                previousIntervals,
                currentIntervals,
                previousState.Position,
                currentState.Position,
                currentState.Radius,
                false,
                previousLogicTime,
                currentLogicTime);
        }

        private static void RecordIntervalDifferenceTransitionTimes(
            List<Fog3VisibilityRowInterval> source,
            List<Fog3VisibilityRowInterval> exclusion,
            FixVector2 previousPosition,
            FixVector2 currentPosition,
            Fix64 radius,
            bool becomingVisible,
            double previousLogicTime,
            double currentLogicTime)
        {
            int exclusionIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
            {
                Fog3VisibilityRowInterval interval = source[sourceIndex];
                int cursor = interval.MinimumX;
                while (exclusionIndex < exclusion.Count
                       && (exclusion[exclusionIndex].Y < interval.Y
                           || (exclusion[exclusionIndex].Y == interval.Y
                               && exclusion[exclusionIndex].MaximumX < cursor)))
                {
                    exclusionIndex++;
                }

                int candidateIndex = exclusionIndex;
                while (candidateIndex < exclusion.Count
                       && exclusion[candidateIndex].Y == interval.Y
                       && exclusion[candidateIndex].MinimumX <= interval.MaximumX)
                {
                    Fog3VisibilityRowInterval covered = exclusion[candidateIndex];
                    if (covered.MinimumX > cursor)
                    {
                        RecordVisibilityTransitionSegment(
                            cursor,
                            System.Math.Min(interval.MaximumX, covered.MinimumX - 1),
                            interval.Y,
                            previousPosition,
                            currentPosition,
                            radius,
                            becomingVisible,
                            previousLogicTime,
                            currentLogicTime);
                    }
                    cursor = System.Math.Max(cursor, covered.MaximumX + 1);
                    if (cursor > interval.MaximumX)
                        break;
                    candidateIndex++;
                }

                if (cursor <= interval.MaximumX)
                {
                    RecordVisibilityTransitionSegment(
                        cursor,
                        interval.MaximumX,
                        interval.Y,
                        previousPosition,
                        currentPosition,
                        radius,
                        becomingVisible,
                        previousLogicTime,
                        currentLogicTime);
                }
            }
        }

        private static void RecordVisibilityTransitionSegment(
            int minimumX,
            int maximumX,
            int y,
            FixVector2 previousPosition,
            FixVector2 currentPosition,
            Fix64 radius,
            bool becomingVisible,
            double previousLogicTime,
            double currentLogicTime)
        {
            double startX = (double)previousPosition.x;
            double startY = (double)previousPosition.y;
            double deltaX = (double)(currentPosition.x - previousPosition.x);
            double deltaY = (double)(currentPosition.y - previousPosition.y);
            double squaredDisplacement = deltaX * deltaX + deltaY * deltaY;
            double radiusValue = (double)radius;
            double squaredRadius = radiusValue * radiusValue;
            for (int x = minimumX; x <= maximumX; x++)
            {
                FixVector2 cellCenter = s_MapData.GetCellCenterFixed(x, y);
                double offsetX = (double)cellCenter.x - startX;
                double offsetY = (double)cellCenter.y - startY;
                double projection = offsetX * deltaX + offsetY * deltaY;
                double discriminant = projection * projection
                                      - squaredDisplacement * (offsetX * offsetX + offsetY * offsetY - squaredRadius);
                if (discriminant < 0d)
                    continue;

                double root = System.Math.Sqrt(discriminant);
                double fraction = becomingVisible
                    ? (projection - root) / squaredDisplacement
                    : (projection + root) / squaredDisplacement;
                if (fraction < 0d || fraction > 1d)
                    continue;

                s_MapData.RecordVisibilityChangeLogicTimeCandidate(
                    x,
                    y,
                    becomingVisible,
                    previousLogicTime + (currentLogicTime - previousLogicTime) * fraction);
            }
        }

        private static void FilterAndExploreVisibilityIntervals(
            VisibilitySourceState state,
            List<Fog3VisibilityRowInterval> destination)
        {
            if (!s_MapData.WorldToGrid(state.Position, out int sourceX, out int sourceY))
                throw new InvalidOperationException($"Cached Fog visibility source {state.Position} is outside the map.");

            for (int intervalIndex = 0; intervalIndex < s_GeometryIntervals.Count; intervalIndex++)
            {
                Fog3VisibilityRowInterval interval = s_GeometryIntervals[intervalIndex];
                int visibleStart = -1;
                for (int x = interval.MinimumX; x <= interval.MaximumX; x++)
                {
                    bool explored = s_MapData.IsExplored(x, interval.Y);
                    bool include = explored;
                    if (!explored && state.CanExploreHiddenFog)
                    {
                        include = !s_BlockHiddenRevealByEnemyStronghold
                                  || !IsHiddenRevealBlockedByEnemyStronghold(sourceX, sourceY, x, interval.Y);
                        if (include)
                            s_MapData.MarkExplored(x, interval.Y);
                    }

                    if (include)
                    {
                        if (visibleStart < 0)
                            visibleStart = x;
                    }
                    else if (visibleStart >= 0)
                    {
                        destination.Add(new Fog3VisibilityRowInterval(interval.Y, visibleStart, x - 1));
                        visibleStart = -1;
                    }
                }

                if (visibleStart >= 0)
                {
                    destination.Add(
                        new Fog3VisibilityRowInterval(interval.Y, visibleStart, interval.MaximumX));
                }
            }
        }

        private static void ChangeVisibilityIntervals(
            List<Fog3VisibilityRowInterval> intervals,
            int delta)
        {
            for (int i = 0; i < intervals.Count; i++)
                s_MapData.ChangeVisibilityCoverage(intervals[i], delta);
        }

        private static void RemoveEntityVisibilitySources(int entityId)
        {
            s_RemovedVisibilitySourceKeys.Clear();
            long baseKey = checked((long)entityId << 1);
            if (s_VisibilitySources.ContainsKey(baseKey))
                s_RemovedVisibilitySourceKeys.Add(baseKey);
            if (s_VisibilitySources.ContainsKey(baseKey | 1L))
                s_RemovedVisibilitySourceKeys.Add(baseKey | 1L);
            RemoveVisibilitySources(s_RemovedVisibilitySourceKeys);
        }

        private static void RemoveVisibilitySources(List<long> sourceKeys)
        {
            for (int i = 0; i < sourceKeys.Count; i++)
            {
                long sourceKey = sourceKeys[i];
                CachedVisibilitySource cached = s_VisibilitySources[sourceKey];
                ChangeVisibilityIntervals(cached.Intervals, -1);
                s_VisibilitySources.Remove(sourceKey);
            }
        }

        private static void RefreshExplorationDependentVisibilitySources()
        {
            foreach (KeyValuePair<long, CachedVisibilitySource> pair in s_VisibilitySources)
            {
                VisibilitySourceState state = pair.Value.State;
                if (!state.CanExploreHiddenFog || s_BlockHiddenRevealByEnemyStronghold)
                    UpdateVisibilitySource(pair.Key, state, true);
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

            if (entity is LogicEntityState { IsBuildingEntity: true, BuildingData: null })
            {
                throw new InvalidOperationException(
                    $"LogicCardPlacementAuthority found a building without BuildingData. entity={entity.LogicEntityId.Value}.");
            }

            if (entity.TryGetLogicBuilding(out IBuildingLogicContext building))
            {
                if (building.BuildingData.Lv == 0)
                    return false;

                radius = s_BuildingVisionRadius;
                return true;
            }

            bool isHero = IsLogicHero(entity);
            radius = isHero ? s_HeroVisionRadius : s_UnitVisionRadius;
            return true;
        }

        private static bool CanExploreHiddenFog(IEntityContext entity, bool hasGhostHero)
        {
            bool isHero = IsLogicHero(entity);
            bool isBuilding = entity.TryGetLogicBuilding(out _);
            return !(isHero && IsGhostHero(entity))
                   && !(hasGhostHero && !isHero && !isBuilding);
        }

        private static bool IsLogicHero(IEntityContext entity)
        {
            return entity != null && entity.IsHeroEntity;
        }

        private static bool IsGhostHero(IEntityContext entity)
        {
            return entity.TryGetLogicHero(out IHeroLogicContext hero) && hero.IsGhostState;
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

        private static void OnEntityRegistered(IEntityContext entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            OnEntityVisibilityInputChanged(entity.LogicEntityId, false);
        }

        private static void OnEntityUnregistered(IEntityContext entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            OnEntityVisibilityInputChanged(entity.LogicEntityId, false);
        }

        private static void OnEntityVisibilityInputChanged(LogicEntityId entityId, bool continuousMovement)
        {
            if (!entityId.IsValid)
                throw new ArgumentException("Fog visibility dirty event requires a valid entity id.", nameof(entityId));
            s_DirtyEntityIds.Add(entityId.Value);
            if (continuousMovement)
                s_ContinuouslyMovedEntityIds.Add(entityId.Value);
            else
                s_ContinuouslyMovedEntityIds.Remove(entityId.Value);
        }

        private static void OnAllEntityVisibilityInputsChanged()
        {
            s_AllEntityVisibilityDirty = true;
        }

        private static void OnStationaryRevealsChanged()
        {
            s_StationaryVisibilityDirty = true;
        }

        private static void OnVisibilityReset()
        {
            s_AllEntityVisibilityDirty = true;
            s_StationaryVisibilityDirty = true;
        }

        private static void ClearWorld()
        {
            EntityRegistry.Registered -= OnEntityRegistered;
            EntityRegistry.Unregistered -= OnEntityUnregistered;
            LogicFactionVisionService.EntityVisibilityInputChanged -= OnEntityVisibilityInputChanged;
            LogicFactionVisionService.AllEntityVisibilityInputsChanged -= OnAllEntityVisibilityInputsChanged;
            LogicFactionVisionService.StationaryRevealsChanged -= OnStationaryRevealsChanged;
            if (s_MapData != null)
                s_MapData.VisibilityReset -= OnVisibilityReset;
            s_MapData = null;
            LogicFactionVisionService.UnbindMap();
            s_HeroVisionRadius = Fix64.Zero;
            s_UnitVisionRadius = Fix64.Zero;
            s_BuildingVisionRadius = Fix64.Zero;
            s_EnemyStrongholdFriendlyUnitDeployRadius = Fix64.Zero;
            s_BlockHiddenRevealByEnemyStronghold = false;
            s_StaticForbiddenHash = 0;
            s_ObservedVisibilityResetVersion = 0;
            s_StaticForbiddenShapes.Clear();
            s_VisibilitySources.Clear();
            s_DirtyEntityIds.Clear();
            s_ContinuouslyMovedEntityIds.Clear();
            s_SeenEntityIds.Clear();
            s_SeenStationarySourceKeys.Clear();
            s_RemovedVisibilitySourceKeys.Clear();
            s_GeometryIntervals.Clear();
            s_NextVisibilityIntervals.Clear();
            s_AllEntityVisibilityDirty = false;
            s_StationaryVisibilityDirty = false;
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
