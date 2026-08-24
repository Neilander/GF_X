using System;
using System.Collections.Generic;
using AAAGame.MiniMap.FOG3;

// Vision/alert/propagation contract: AIDoc/当前仇恨与索敌系统.md. Keep the document in sync.
public static class LogicFactionVisionService
{
    public const string AggroOuterRangeConfigKey = "AggroOuterRange";
    public const string DamageAlertVisionDurationConfigKey = "DamageAlertVisionDuration";
    public const string DamageAlertVisionRadiusConfigKey = "DamageAlertVisionRadius";
    public const string AggroCandidatePropagationRadiusConfigKey = "AggroCandidatePropagationRadius";
    public const string DamageAlertTargetDurationConfigKey = "DamageAlertTargetDuration";
    public const string MinimumAggroCandidateRangeConfigKey = "MinimumAggroCandidateRange";
    public const string SlopeUpperVisionRadiusConfigKey = "SlopeUpperVisionRadius";

    private const string HeroVisionRadiusConfigKey = "HeroVisionRadius";
    private const string UnitVisionRadiusConfigKey = "UnitVisionRadius";
    private const string BuildingVisionRadiusConfigKey = "BuildingVisionRadius";

    private readonly struct StationaryReveal
    {
        public StationaryReveal(SideType side, FixVector2 position, int height, Fix64 remaining)
        {
            Side = side;
            Position = position;
            Height = height;
            Remaining = remaining;
        }

        public SideType Side { get; }
        public FixVector2 Position { get; }
        public int Height { get; }
        public Fix64 Remaining { get; }
        public StationaryReveal WithRemaining(Fix64 remaining) => new StationaryReveal(Side, Position, Height, remaining);
    }

    private static readonly List<StationaryReveal> s_StationaryReveals = new List<StationaryReveal>();
    private static readonly Dictionary<int, bool> s_PlayerVisibilityCache = new Dictionary<int, bool>();
    private static readonly Dictionary<int, bool> s_EnemyVisibilityCache = new Dictionary<int, bool>();
    private static Fog3MapData s_MapData;
    private static bool s_TargetingPhaseCacheActive;

    public static void BindMap(Fog3MapData mapData)
    {
        s_MapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        s_StationaryReveals.Clear();
        ClearTargetingPhaseCache();
    }

    public static void UnbindMap()
    {
        s_MapData = null;
        s_StationaryReveals.Clear();
        ClearTargetingPhaseCache();
    }

    public static void BeginTargetingPhase()
    {
        if (s_TargetingPhaseCacheActive)
            throw new InvalidOperationException("Faction visibility targeting-phase cache is already active.");
        LogicTargetingSpatialIndexService.BeginTargetingPhase();
        s_PlayerVisibilityCache.Clear();
        s_EnemyVisibilityCache.Clear();
        s_TargetingPhaseCacheActive = true;
    }

    public static void EndTargetingPhase()
    {
        if (!s_TargetingPhaseCacheActive)
            throw new InvalidOperationException("Faction visibility targeting-phase cache is not active.");
        LogicTargetingSpatialIndexService.EndTargetingPhase();
        s_TargetingPhaseCacheActive = false;
    }

    public static void Advance(Fix64 deltaTime)
    {
        if (deltaTime <= Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        for (int i = s_StationaryReveals.Count - 1; i >= 0; i--)
        {
            Fix64 remaining = s_StationaryReveals[i].Remaining - deltaTime;
            if (remaining <= Fix64.Zero)
                s_StationaryReveals.RemoveAt(i);
            else
                s_StationaryReveals[i] = s_StationaryReveals[i].WithRemaining(remaining);
        }
    }

    public static void AddDamageReveal(SideType receivingSide, FixVector2 sourcePosition)
    {
        if (receivingSide != SideType.PlayerSide && receivingSide != SideType.EnemySide)
            throw new ArgumentOutOfRangeException(nameof(receivingSide), receivingSide, "Damage reveal requires a combat side.");
        s_StationaryReveals.Add(new StationaryReveal(
            receivingSide,
            sourcePosition,
            ResolveHeight(sourcePosition),
            ReadPositiveConfig(DamageAlertVisionDurationConfigKey)));
    }

    public static void HandleSuccessfulDamage(IEntityContext victim, IEntityContext attacker)
    {
        if (victim == null)
            throw new ArgumentNullException(nameof(victim));
        if (attacker == null || !attacker.Alive || !EntityCombatTeamHelper.IsEnemy(victim, attacker))
            return;

        Fix64 outerRange = ReadWorldDistance(AggroOuterRangeConfigKey);
        if (victim.LogicFrameDistanceToTargetSurfaceFixed(attacker) > outerRange)
            return;

        AddDamageReveal(victim.Side, attacker.LogicFramePositionFixed());
        PropagateDamageAlert(victim.Side, victim.LogicFramePositionFixed(), attacker);
    }

    public static void HandleAggroAcquired(IEntityContext source, IEntityContext target)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (!source.Alive || !EntityCombatTeamHelper.IsEnemy(source, target))
            throw new InvalidOperationException(
                $"LogicFactionVisionService.HandleAggroAcquired requires an alive source and enemy target. " +
                $"source={source.CharacterKey}, sourceAlive={source.Alive}, target={target.CharacterKey}.");

        PropagateAggroCandidate(source.Side, source.LogicFramePositionFixed(), target);
    }

    private static void PropagateDamageAlert(
        SideType sourceSide,
        FixVector2 sourcePosition,
        IEntityContext target)
    {
        Fix64 allyRadius = ReadWorldDistance(AggroCandidatePropagationRadiusConfigKey);
        Fix64 allyRadiusSquared = allyRadius * allyRadius;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext ally = entities[i]
                ?? throw new InvalidOperationException($"LogicFactionVisionService found a null registry entity at index {i}.");
            if (!ally.Alive || ally.Side != sourceSide)
                continue;
            if (FixVector2.SqrMagnitude(ally.LogicFramePositionFixed() - sourcePosition) > allyRadiusSquared)
                continue;
            if (ally.TargetComp is IAlertTargetingComp alertTargeting)
                alertTargeting.NotifyAllyFoundEnemy(target);
        }
    }

    private static void PropagateAggroCandidate(
        SideType sourceSide,
        FixVector2 sourcePosition,
        IEntityContext target)
    {
        Fix64 allyRadius = ReadWorldDistance(AggroCandidatePropagationRadiusConfigKey);
        Fix64 allyRadiusSquared = allyRadius * allyRadius;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext ally = entities[i]
                ?? throw new InvalidOperationException($"LogicFactionVisionService found a null registry entity at index {i}.");
            if (!ally.Alive || ally.Side != sourceSide || ReferenceEquals(ally.TargetComp?.CurrentTarget, target))
                continue;
            if (FixVector2.SqrMagnitude(ally.LogicFramePositionFixed() - sourcePosition) > allyRadiusSquared)
                continue;
            if (ally.TargetComp is IAggroCandidateTargetingComp candidateTargeting)
                candidateTargeting.TryNotifyAllyAggroCandidate(target);
        }
    }

    public static void VisitStationaryReveals(
        SideType side,
        Action<FixVector2, Fix64, int> visitor)
    {
        if (visitor == null)
            throw new ArgumentNullException(nameof(visitor));
        Fix64 radius = ReadWorldDistance(DamageAlertVisionRadiusConfigKey);
        for (int i = 0; i < s_StationaryReveals.Count; i++)
        {
            StationaryReveal reveal = s_StationaryReveals[i];
            if (reveal.Side == side)
                visitor(reveal.Position, radius, reveal.Height);
        }
    }

    public static bool IsEntityVisibleToSide(SideType viewerSide, IEntityContext target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (viewerSide != SideType.PlayerSide && viewerSide != SideType.EnemySide)
            throw new ArgumentOutOfRangeException(nameof(viewerSide), viewerSide, "Visibility requires a combat side.");

        if (!s_TargetingPhaseCacheActive)
            return ComputeEntityVisibilityToSide(viewerSide, target);
        if (!target.LogicEntityId.IsValid)
            throw new InvalidOperationException("Targeting-phase visibility requires a valid logic entity id.");

        Dictionary<int, bool> cache = viewerSide == SideType.PlayerSide
            ? s_PlayerVisibilityCache
            : s_EnemyVisibilityCache;
        int targetId = target.LogicEntityId.Value;
        if (cache.TryGetValue(targetId, out bool visible))
            return visible;
        visible = ComputeEntityVisibilityToSide(viewerSide, target);
        cache.Add(targetId, visible);
        return visible;
    }

    private static bool ComputeEntityVisibilityToSide(SideType viewerSide, IEntityContext target)
    {
        if (!target.Alive)
            return false;
        if (target.Side == viewerSide)
            return true;

        if (target.IsStealthed)
            return false;
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        bool targetInHiddenFog = IsPositionInHiddenPlayerFog(viewerSide, target.PositionFixed);
        bool hasGhostHero = targetInHiddenFog && HasGhostHero(viewerSide, entities);
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext revealer = entities[i]
                ?? throw new InvalidOperationException($"LogicFactionVisionService found a null registry entity at index {i}.");
            if (!revealer.Alive || revealer.Side != viewerSide)
                continue;
            if (targetInHiddenFog && !CanExploreHiddenFog(revealer, hasGhostHero))
                continue;
            if (!TryResolveEntityVisionRadius(revealer, out Fix64 radius))
                continue;
            Fix64 distanceSquared = FixVector2.SqrMagnitude(target.PositionFixed - revealer.PositionFixed);
            if (distanceSquared <= radius * radius
                && HasLineOfSight(revealer.PositionFixed, target.PositionFixed))
            {
                return true;
            }
            if (HasSlopeUpperVision(revealer, target.PositionFixed, distanceSquared))
                return true;
        }

        Fix64 alertRadius = ReadWorldDistance(DamageAlertVisionRadiusConfigKey);
        int targetHeight = ResolveHeight(target.PositionFixed);
        for (int i = 0; i < s_StationaryReveals.Count; i++)
        {
            StationaryReveal reveal = s_StationaryReveals[i];
            if (reveal.Side == viewerSide
                && reveal.Height == targetHeight
                && FixVector2.SqrMagnitude(target.PositionFixed - reveal.Position) <= alertRadius * alertRadius)
            {
                return true;
            }
        }
        return false;
    }

    private static void ClearTargetingPhaseCache()
    {
        s_TargetingPhaseCacheActive = false;
        LogicTargetingSpatialIndexService.Reset();
        s_PlayerVisibilityCache.Clear();
        s_EnemyVisibilityCache.Clear();
    }

    public static Fix64 ReadWorldDistance(string configKey) =>
        (ReadPositiveConfig(configKey));

    public static Fix64 ReadPositiveConfig(string configKey) =>
        FixedConfigReader.ReadRequiredPositiveFixedConfig(configKey);

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        hasher.Add(s_MapData != null);
        hasher.Add(s_StationaryReveals.Count);
        for (int i = 0; i < s_StationaryReveals.Count; i++)
        {
            StationaryReveal reveal = s_StationaryReveals[i];
            hasher.Add((int)reveal.Side);
            hasher.Add(reveal.Position.x.RawValue);
            hasher.Add(reveal.Position.y.RawValue);
            hasher.Add(reveal.Height);
            hasher.Add(reveal.Remaining.RawValue);
        }
    }

    private static bool TryResolveEntityVisionRadius(IEntityContext entity, out Fix64 radius)
    {
        radius = Fix64.Zero;
        if (entity.TryGetLogicBuilding(out IBuildingLogicContext building) && building.BuildingData.Lv == 0)
            return false;
        string key = entity.IsLogicBuilding()
            ? BuildingVisionRadiusConfigKey
            : entity.IsHeroEntity
                ? HeroVisionRadiusConfigKey
                : UnitVisionRadiusConfigKey;
        radius = ReadWorldDistance(key);
        return true;
    }

    private static bool HasGhostHero(SideType side, IList<IEntityContext> entities)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i]
                ?? throw new InvalidOperationException($"LogicFactionVisionService found a null registry entity at index {i}.");
            if (entity.Alive
                && entity.Side == side
                && entity.TryGetLogicHero(out IHeroLogicContext hero)
                && hero.IsGhostState)
                return true;
        }
        return false;
    }

    private static bool CanExploreHiddenFog(IEntityContext revealer, bool hasGhostHero)
    {
        bool isHero = revealer.IsHeroEntity;
        bool isGhostHero = revealer.TryGetLogicHero(out IHeroLogicContext hero) && hero.IsGhostState;
        return !isGhostHero && !(hasGhostHero && !isHero && !revealer.IsLogicBuilding());
    }

    private static bool IsPositionInHiddenPlayerFog(SideType viewerSide, FixVector2 position)
    {
        return viewerSide == SideType.PlayerSide
               && s_MapData != null
               && s_MapData.WorldToGrid(position, out int x, out int y)
               && !s_MapData.IsExplored(x, y);
    }

    private static bool HasLineOfSight(FixVector2 viewer, FixVector2 target)
    {
        if (s_MapData == null)
            return true;
        if (!s_MapData.WorldToGrid(target, out int targetX, out int targetY))
            return false;
        return !s_MapData.IsVisionBlockedByHigherPlatform(viewer, targetX, targetY);
    }

    private static bool HasSlopeUpperVision(
        IEntityContext revealer,
        FixVector2 targetPosition,
        Fix64 distanceSquared)
    {
        if (s_MapData == null || revealer.IsLogicBuilding())
            return false;
        FixVector2 revealerPosition = revealer.PositionFixed;
        if (!s_MapData.WorldToGrid(revealerPosition, out int viewerX, out int viewerY)
            || !s_MapData.IsSlope(viewerX, viewerY)
            || !s_MapData.WorldToGrid(targetPosition, out _, out _))
            return false;
        int viewerHeight = s_MapData.GetVisionHeight(revealerPosition);
        if (s_MapData.GetVisionHeight(targetPosition) != viewerHeight + 1)
            return false;
        Fix64 radius = ReadWorldDistance(SlopeUpperVisionRadiusConfigKey);
        return distanceSquared <= radius * radius;
    }

    private static int ResolveHeight(FixVector2 position) => s_MapData != null ? s_MapData.GetVisionHeight(position) : 0;
}
