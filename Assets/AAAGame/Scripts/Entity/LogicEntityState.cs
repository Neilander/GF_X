using System;
using System.Collections.Generic;
using UnityEngine;
using AAAGame.Scripts.BuffSystem;
using UnityGameFramework.Runtime;

public readonly struct LogicEntitySpawnDescriptor
{
    public LogicEntitySpawnDescriptor(
        FixVector2 position,
        FixVector2 forward,
        SideType side,
        string characterKey,
        string sourceStrongholdId = null)
    {
        FixVector2 normalizedForward = forward.GetNormalized();
        if (FixVector2.SqrMagnitude(normalizedForward) == Fix64.Zero)
            throw new ArgumentException("Logic entity spawn forward must be non-zero.", nameof(forward));
        if (string.IsNullOrWhiteSpace(characterKey))
            throw new ArgumentException("Logic entity spawn character key is empty.", nameof(characterKey));

        Position = position;
        Forward = normalizedForward;
        Side = side;
        CharacterKey = characterKey;
        SourceStrongholdId = string.IsNullOrWhiteSpace(sourceStrongholdId) ? null : sourceStrongholdId;
    }

    public FixVector2 Position { get; }
    public FixVector2 Forward { get; }
    public SideType Side { get; }
    public string CharacterKey { get; }
    public string SourceStrongholdId { get; }

    internal static LogicEntitySpawnDescriptor CreateUnspecified()
    {
        return new LogicEntitySpawnDescriptor(
            FixVector2.Zero,
            new FixVector2(Fix64.Zero, Fix64.One),
            SideType.NoSide,
            "Unspecified");
    }
}

public readonly struct LogicEntityHealthChange
{
    public LogicEntityHealthChange(Fix64 current, Fix64 max, Fix64 delta)
    {
        Current = current;
        Max = max;
        Delta = delta;
    }

    public Fix64 Current { get; }
    public Fix64 Max { get; }
    public Fix64 Delta { get; }
}

public static class LogicBuildingOwnershipEventService
{
    public static event Action<IBuildingLogicContext, int, int> OwnerFactionChanged;

    internal static void Publish(IBuildingLogicContext building, int oldFactionId, int newFactionId)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (oldFactionId == newFactionId)
            throw new InvalidOperationException("Logic building ownership event requires a faction change.");
        OwnerFactionChanged?.Invoke(building, oldFactionId, newFactionId);
    }
}

public static class LogicBuildingDisabledEventService
{
    public static event Action<IBuildingLogicContext, IEntityContext> BuildingDisabled;

    internal static void Publish(IBuildingLogicContext building, IEntityContext attacker)
    {
        if (building == null)
            throw new ArgumentNullException(nameof(building));
        if (!building.IsDisabled)
            throw new InvalidOperationException("Logic building disabled event requires a disabled building.");
        BuildingDisabled?.Invoke(building, attacker);
    }
}

public static class LogicUnitDeathEventService
{
    public static event Action<IEntityContext> UnitDied;

    internal static void Publish(IEntityContext victim)
    {
        if (victim == null)
            throw new ArgumentNullException(nameof(victim));
        if (victim.Alive)
            throw new InvalidOperationException($"LogicUnitDeathEventService cannot publish a living unit. entity={victim.LogicEntityId.Value}.");

        UnitDied?.Invoke(victim);
    }
}

public sealed class LogicEntityState : ILogicFrameEntity, ISkillCompHost, IBuildingLogicContext, IHeroLogicContext
{
    private const string ArmyForcePropertyId = "Building_ArmyForce";
    private const string ArmySupplyPerUnitPropertyId = "Building_ArmySupplyPerUnit";
    private static readonly ICapability DisabledCapabilityLocker = new StateCapabilityLocker();
    private static readonly ICapability GhostCapabilityLocker = new StateCapabilityLocker();
    private const string HeroGhostBuffId = "hero_ghost_state";
    private readonly Dictionary<ICapability, List<ICapability>> m_CapabilityLockers = new();
    private readonly HashSet<string> m_InvincibleSources = new();
    private readonly List<string> m_DeterministicStringValues = new List<string>();
    private static readonly Comparison<string> s_DeterministicStringComparison = string.CompareOrdinal;
    private readonly LogicMoveExecutor m_MoveExecutor = new();
    private CreaturePropertyManager m_CreatureProperties;
    private CharacterDataDetail m_CharacterData;
    private IControlBrain m_Brain;
    private IMoveComp m_MoveComp;
    private IAtkComp m_AtkComp;
    private ITargetingComp m_TargetingComp;
    private CharacterBuffComp m_BuffComp;
    private IDurationMoveEffectComp m_DurationMoveEffectComp;
    private WeaponComp m_WeaponComp;
    private ISkillComp m_SkillComp;
    private bool m_IsConfigured;
    private bool m_FrameActive;
    private MAEntityLogicFramePhase m_NextPhase;
    private Fix64 m_CombatClock;
    private Fix64 m_OutOfCombatStart;
    private bool m_CombatCapabilitiesLockedForDisabled;
    private bool m_CombatCapabilitiesLockedForGhost;
    private BuildingData m_BuildingData;
    private string m_BuildingInstanceId;
    private string m_StrongholdId;
    private string m_SourceStrongholdId;
    private BuildingExtraProps m_ProductionProps;
    private BaseValueProperty m_ArmyForceProperty;
    private BaseValueProperty m_ArmySupplyPerUnitProperty;
    private int m_OwnerFactionId = -1;
    private LogicCombatShape m_BuildingCombatShape;
    private IReadOnlyList<LogicCombatShape> m_LogicObstacleShapes = Array.Empty<LogicCombatShape>();
    private IReadOnlyList<LogicInteractionOptionDescriptor> m_InteractionOptions = Array.Empty<LogicInteractionOptionDescriptor>();
    private readonly List<int> m_RegisteredObstacleIds = new List<int>();

    internal LogicEntityState(LogicEntityId entityId, LogicEntitySpawnDescriptor descriptor)
    {
        if (!entityId.IsValid)
            throw new ArgumentException("Logic entity state requires a valid id.", nameof(entityId));

        EntityId = entityId;
        Position = descriptor.Position;
        Forward = descriptor.Forward;
        Side = descriptor.Side;
        CharacterKey = descriptor.CharacterKey;
        m_SourceStrongholdId = descriptor.SourceStrongholdId;
        Alive = true;
    }

    public LogicEntityId EntityId { get; }
    public LogicEntityId LogicEntityId => EntityId;
    public FixVector2 Position { get; private set; }
    public FixVector2 Forward { get; private set; }
    public SideType Side { get; internal set; }
    public string CharacterKey { get; }
    public bool IsSpawnCommitted { get; internal set; }
    public bool IsDespawnCommitted { get; internal set; }
    public int BoundViewEntityId { get; internal set; }
    public bool HasBoundView => BoundViewEntityId > 0;
    public bool IsConfigured => m_IsConfigured;
    public bool IsLogicActive => IsSpawnCommitted;
    public bool Alive { get; private set; }
    public int NavigationAgentTypeId { get; private set; }
    public bool AllowsZeroCollisionRadius { get; private set; }
    public bool UsesFlowNavigationAgent { get; private set; }
    public bool IsPlayerEntity { get; private set; }
    public bool IsHeroEntity { get; private set; }
    public int TauntLevel { get; set; } = 1;
    public bool IsGhostState { get; private set; }
    public bool IsBuildingEntity => m_BuildingData != null;
    public BuildingData BuildingData => m_BuildingData;
    public BuildingExtraProps ProductionProps => m_ProductionProps;
    public string BuildingInstanceId => m_BuildingInstanceId;
    public string StrongholdId => m_StrongholdId;
    public string SourceStrongholdId => m_SourceStrongholdId;
    public int OwnerFactionId => m_OwnerFactionId;
    public IReadOnlyList<LogicInteractionOptionDescriptor> InteractionOptions => m_InteractionOptions;
    public bool IsDisabled { get; private set; }
    public bool IsPhaseProtected { get; private set; }
    public bool IsPermanentStealth { get; private set; }
    public bool HasPermanentNoAttackCapability { get; private set; }
    public bool BlocksLogicMovement { get; private set; }
    public bool IsGameEndConditionBuilding { get; private set; }
    public bool IsNavigationStaticBaked { get; private set; }
    public IReadOnlyList<LogicCombatShape> LogicObstacleShapes => m_LogicObstacleShapes;
    public bool HasPreparedLogicMove => m_MoveExecutor.HasPreparedLogicMove;
    public ulong PreparedLogicFrame => m_MoveExecutor.PreparedLogicFrame;
    public bool PreparedCollisionMovable => m_MoveExecutor.PreparedCollisionMovable;
    public bool PreparedNavigationConstraintEnabled => m_MoveExecutor.PreparedNavigationConstraintEnabled;
    public bool PreparedPreserveSpeedOnStaticSlide => m_MoveExecutor.PreparedPreserveSpeedOnStaticSlide;
    public uint AgentCollisionMask => m_DurationMoveEffectComp?.IsInLossOfBalance == true
        ? 0u
        : LogicAgentCollisionFilter.ResolveMask(Side, IsGhostState);
    public FixVector2 PreparedResolvedHorizontalDisplacement => m_MoveExecutor.PreparedResolvedHorizontalDisplacement;
    public FixVector2 PositionFixed => Position;
    public FixVector2 ForwardFixed => Forward;
    public LogicCombatShape CombatShape
    {
        get
        {
            if (IsBuildingEntity)
                return m_BuildingCombatShape;
            Fix64 radius = m_CreatureProperties != null
                ? DistanceUnitConverter.ConvertToWorld(m_CreatureProperties.GetProperty(CreatureMainProperty.CollisionRadius))
                : Fix64.Zero;
            return LogicCombatShape.Circle(Position, Fix64.Max(Fix64.Zero, radius));
        }
    }
    Vector3 IEntityContext.Position => new Vector3((float)Position.x, 0f, (float)Position.y);
    public Quaternion Rotation => Quaternion.LookRotation(new Vector3((float)Forward.x, 0f, (float)Forward.y));
    public GameObject Gmo => null;
    public CharacterDataDetail CharacterData => m_CharacterData;
    public CreaturePropertyManager CreatureProperties => m_CreatureProperties;
    public Fix64 HealthValue => RequireProperties().GetProperty(CreatureCurrentProperty.HealthCurrent);
    public IControlBrain Brain => m_Brain;
    public IMoveExecutor MoveExecutor => m_MoveExecutor;
    public IMoveComp MoveComp => m_MoveComp;
    public IAtkComp AtkComp => m_AtkComp;
    public ITargetingComp TargetComp => m_TargetingComp;
    public IBuffComp BuffComp => m_BuffComp;
    public WeaponComp WeaponComp => m_WeaponComp;
    public IDurationMoveEffectComp DurationMoveEffectComp => m_DurationMoveEffectComp;
    public ISkillComp skillComp => m_SkillComp;
    public ISkillComp SkillComp => m_SkillComp;
    public bool IsOutOfCombat { get; private set; }
    public Fix64 OutOfCombatElapsedLogicTime => IsOutOfCombat
        ? Fix64.Max(Fix64.Zero, m_CombatClock - m_OutOfCombatStart)
        : Fix64.Zero;
    public float OutOfCombatElapsedSeconds => (float)OutOfCombatElapsedLogicTime;
    public event Action<LogicEntityHealthChange> HealthChanged;
    public event Action<IEntityContext> UnitDied;
    public event Action<bool, IEntityContext> BuildingDisabledChanged;
    public event Action<bool> GhostStateChanged;
    public event Action<bool> CollisionBlockingChanged;
    public event Action<bool> PermanentStealthChanged;
    public event Action<bool> PhaseProtectionChanged;
    public event Action<int, int> OwnerFactionChanged;

    public void Configure(
        CharacterDataDetail characterData,
        CreaturePropertyManager creatureProperties,
        int navigationAgentTypeId,
        bool allowsZeroCollisionRadius,
        IControlBrain brain,
        bool usesFlowNavigationAgent = true,
        bool isPlayerEntity = false,
        bool isHeroEntity = false)
    {
        if (m_IsConfigured)
            throw new InvalidOperationException($"LogicEntityState.Configure failed: entity {EntityId.Value} is already configured.");
        m_CharacterData = characterData;
        m_CreatureProperties = creatureProperties ?? throw new ArgumentNullException(nameof(creatureProperties));
        NavigationAgentTypeId = navigationAgentTypeId;
        AllowsZeroCollisionRadius = allowsZeroCollisionRadius;
        UsesFlowNavigationAgent = usesFlowNavigationAgent;
        IsPlayerEntity = isPlayerEntity;
        IsHeroEntity = isHeroEntity;
        m_Brain = brain;
        m_DurationMoveEffectComp = new DurationMoveEffectComp();
        m_DurationMoveEffectComp.Init(this);
        m_BuffComp = new CharacterBuffComp();
        m_BuffComp.Init(this);
        IsOutOfCombat = true;
        m_IsConfigured = true;
    }

    public void ConfigureBuilding(
        BuildingData buildingData,
        string buildingInstanceId,
        string strongholdId,
        int ownerFactionId,
        LogicCombatShape combatShape,
        IReadOnlyList<LogicCombatShape> obstacleShapes,
        IReadOnlyList<LogicInteractionOptionDescriptor> interactionOptions,
        bool hasPermanentNoAttackCapability,
        int? armySupplyPerUnit = null,
        bool isGameEndConditionBuilding = false,
        bool isNavigationStaticBaked = false)
    {
        if (!m_IsConfigured)
            throw new InvalidOperationException($"LogicEntityState.ConfigureBuilding failed: entity {EntityId.Value} is not configured.");
        if (IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.ConfigureBuilding failed: entity {EntityId.Value} is already a building.");
        if (buildingData == null)
            throw new ArgumentNullException(nameof(buildingData));
        if (string.IsNullOrWhiteSpace(buildingInstanceId))
            throw new ArgumentException("Building instance id is empty.", nameof(buildingInstanceId));
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        if (combatShape.Kind != LogicCombatShapeKind.AxisAlignedBox)
            throw new ArgumentException("Building combat shape must be an axis-aligned box.", nameof(combatShape));
        if (obstacleShapes == null)
            throw new ArgumentNullException(nameof(obstacleShapes));
        if (interactionOptions == null)
            throw new ArgumentNullException(nameof(interactionOptions));

        var copiedShapes = new LogicCombatShape[obstacleShapes.Count];
        for (int i = 0; i < obstacleShapes.Count; i++)
        {
            LogicCombatShape shape = obstacleShapes[i];
            if (shape.Kind != LogicCombatShapeKind.AxisAlignedBox)
                throw new ArgumentException($"Building obstacle shape {i} is not an axis-aligned box.", nameof(obstacleShapes));
            copiedShapes[i] = shape;
        }

        m_BuildingData = buildingData;
        TauntLevel = 0;
        m_BuildingInstanceId = buildingInstanceId;
        m_StrongholdId = strongholdId;
        m_ProductionProps = LogicBuildingExtraPropsStore.GetOrCreate(buildingInstanceId);
        ConfigureArmyCardProperties(buildingData, armySupplyPerUnit);
        m_OwnerFactionId = ownerFactionId;
        m_BuildingCombatShape = combatShape;
        m_LogicObstacleShapes = copiedShapes;
        var copiedOptions = new LogicInteractionOptionDescriptor[interactionOptions.Count];
        for (int i = 0; i < interactionOptions.Count; i++)
        {
            LogicInteractionOptionDescriptor option = interactionOptions[i];
            if (option.TargetEntityId != EntityId
                || !string.Equals(option.TargetBuildingInstanceId, buildingInstanceId, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Building interaction option {i} target does not match entity {EntityId.Value}.", nameof(interactionOptions));
            }
            copiedOptions[i] = option;
        }
        m_InteractionOptions = copiedOptions;
        HasPermanentNoAttackCapability = hasPermanentNoAttackCapability;
        BlocksLogicMovement = copiedShapes.Length > 0;
        IsGameEndConditionBuilding = isGameEndConditionBuilding;
        IsNavigationStaticBaked = isNavigationStaticBaked;
    }

    public int GetArmyForce()
    {
        Fix64 baseValue = (Fix64)GetArmyForceWithoutRuntimeRules();
        GlobalBuffManager manager = GameEntry.GetComponent<GlobalBuffManager>();
        Fix64 runtimeBonus = manager != null
            ? manager.CalculateRuntimeArmyForceBonus(this)
            : LevelTagRuntime.CalculateArmyForceBonus(this);
        return Math.Max(0, (int)(baseValue + runtimeBonus));
    }

    public int GetArmyForceWithoutRuntimeRules()
    {
        if (m_ArmyForceProperty == null)
            return 0;

        Fix64 extra = m_ProductionProps != null ? m_ProductionProps.ArmyForce : Fix64.Zero;
        return Math.Max(0, (int)(m_ArmyForceProperty.GetValue() + extra));
    }

    public int GetArmySupplyPerUnit()
    {
        if (m_ArmySupplyPerUnitProperty == null)
            return 0;

        int value = (int)m_ArmySupplyPerUnitProperty.GetValue()
                    + LevelTagRuntime.CalculateArmySupplyPerUnitBonus(this);
        return Math.Max(0, value);
    }

    public int GetArmyOccupiedSupply()
    {
        long occupied = (long)GetArmyForce() * GetArmySupplyPerUnit();
        if (occupied <= 0)
            return 0;
        return occupied >= int.MaxValue ? int.MaxValue : (int)occupied;
    }

    public void SetArmyForceBase(int value)
    {
        RequireArmyProperty(m_ArmyForceProperty, ArmyForcePropertyId).SetBaseValue((Fix64)Math.Max(0, value));
    }

    public void SetArmySupplyPerUnitBase(int value)
    {
        RequireArmyProperty(m_ArmySupplyPerUnitProperty, ArmySupplyPerUnitPropertyId)
            .SetBaseValue((Fix64)Math.Max(0, value));
    }

    public void ModifyArmyForce(IPropertyModifier modifier, bool ifAdd = true)
    {
        ModifyArmyProperty(RequireArmyProperty(m_ArmyForceProperty, ArmyForcePropertyId), modifier, ifAdd);
    }

    public void ModifyArmySupplyPerUnit(IPropertyModifier modifier, bool ifAdd = true)
    {
        ModifyArmyProperty(
            RequireArmyProperty(m_ArmySupplyPerUnitProperty, ArmySupplyPerUnitPropertyId),
            modifier,
            ifAdd);
    }

    private void ConfigureArmyCardProperties(BuildingData buildingData, int? armySupplyPerUnit)
    {
        if (buildingData.Type != BuilType.Army)
        {
            if (armySupplyPerUnit.HasValue)
                throw new ArgumentException("Non-army building cannot define army supply.", nameof(armySupplyPerUnit));
            return;
        }
        if (!armySupplyPerUnit.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(buildingData.UnitID))
            {
                throw new ArgumentException(
                    $"Army building '{buildingData.Identifier}' requires an explicit supply-per-unit value.",
                    nameof(armySupplyPerUnit));
            }
            armySupplyPerUnit = 0;
        }

        PropertyManager propertyManager = RequireProperties().propertyManager;
        m_ArmyForceProperty = PropertyHelper.CreateBaseProperty(ArmyForcePropertyId, propertyManager);
        m_ArmySupplyPerUnitProperty = PropertyHelper.CreateBaseProperty(ArmySupplyPerUnitPropertyId, propertyManager);
        m_ArmyForceProperty.SetBaseValue((Fix64)Math.Max(0, buildingData.Production));
        m_ArmySupplyPerUnitProperty.SetBaseValue((Fix64)Math.Max(0, armySupplyPerUnit.Value));
    }

    private static BaseValueProperty RequireArmyProperty(BaseValueProperty property, string propertyId)
    {
        return property ?? throw new InvalidOperationException(
            $"Army property '{propertyId}' is unavailable on a non-army logic building.");
    }

    private static void ModifyArmyProperty(BaseValueProperty property, IPropertyModifier modifier, bool ifAdd)
    {
        if (modifier == null)
            throw new ArgumentNullException(nameof(modifier));
        if (ifAdd)
            property.AddModifier(modifier);
        else
            property.RemoveModifier(modifier);
    }

    public void SetCollisionBlockingByBuff(bool blocksMovement)
    {
        if (!IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.SetCollisionBlockingByBuff failed: entity {EntityId.Value} is not a building.");
        if (BlocksLogicMovement == blocksMovement)
            return;
        BlocksLogicMovement = blocksMovement;
        if (!IsLogicActive)
            return;

        if (!IsNavigationStaticBaked)
        {
            if (blocksMovement)
                ScheduleObstacleAdds(false);
            else
                ScheduleObstacleRemovals(false);
        }
        CollisionBlockingChanged?.Invoke(blocksMovement);
    }

    public void SetPermanentStealthByBuff(bool enabled)
    {
        if (!IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.SetPermanentStealthByBuff failed: entity {EntityId.Value} is not a building.");
        if (IsPermanentStealth == enabled)
            return;
        IsPermanentStealth = enabled;
        PermanentStealthChanged?.Invoke(enabled);
    }

    public void SetPhaseProtectionByBuff(bool enabled)
    {
        if (!IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.SetPhaseProtectionByBuff failed: entity {EntityId.Value} is not a building.");
        IsPhaseProtected = enabled;
        if (enabled && m_TargetingComp != null)
            m_TargetingComp.CurrentTarget = null;
        PhaseProtectionChanged?.Invoke(enabled);
    }

    public void SetGhostStateByBuff(bool enabled)
    {
        if (!IsHeroEntity)
            throw new InvalidOperationException($"LogicEntityState.SetGhostStateByBuff failed: entity {EntityId.Value} is not a hero.");
        if (IsGhostState == enabled)
            return;

        IsGhostState = enabled;
        Alive = true;
        m_TargetingComp.CurrentTarget = null;
        if (enabled)
        {
            LockComp(m_AtkComp, GhostCapabilityLocker);
            LockComp(m_TargetingComp, GhostCapabilityLocker);
            m_CombatCapabilitiesLockedForGhost = true;
        }
        else if (m_CombatCapabilitiesLockedForGhost)
        {
            ResumeComp(m_AtkComp, GhostCapabilityLocker);
            ResumeComp(m_TargetingComp, GhostCapabilityLocker);
            m_CombatCapabilitiesLockedForGhost = false;
        }
        GhostStateChanged?.Invoke(enabled);
    }

    public void RestoreFromGhostState()
    {
        SetGhostStateByBuff(false);
        RestoreHealthToFull();
    }

    public void RestoreBuildingToFullHealth()
    {
        if (!IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.RestoreBuildingToFullHealth failed: entity {EntityId.Value} is not a building.");

        bool wasDisabled = IsDisabled;
        IsDisabled = false;
        Alive = true;
        RestoreHealthToFull();
        if (m_CombatCapabilitiesLockedForDisabled)
        {
            ResumeComp(m_AtkComp, DisabledCapabilityLocker);
            ResumeComp(m_TargetingComp, DisabledCapabilityLocker);
            m_CombatCapabilitiesLockedForDisabled = false;
        }
        m_TargetingComp.CurrentTarget = null;
        if (wasDisabled)
            BuildingDisabledChanged?.Invoke(false, null);
    }

    public void SetBrain(IControlBrain brain) => m_Brain = brain;
    public void SetBuildingOwnerFaction(int ownerFactionId, SideType side)
    {
        if (!IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.SetBuildingOwnerFaction failed: entity {EntityId.Value} is not a building.");
        if (ownerFactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(ownerFactionId));
        int oldOwnerFactionId = m_OwnerFactionId;
        m_OwnerFactionId = ownerFactionId;
        Side = side;
        if (oldOwnerFactionId != ownerFactionId)
        {
            OwnerFactionChanged?.Invoke(oldOwnerFactionId, ownerFactionId);
            LogicBuildingOwnershipEventService.Publish(this, oldOwnerFactionId, ownerFactionId);
        }
    }
    public void SetOwnerFaction(int ownerFactionId) =>
        SetBuildingOwnerFaction(
            ownerFactionId,
            EntitySideHelper.ToSide(EntityCombatTeamHelper.ResolveTeamIdByFaction(ownerFactionId)));
    public void SetUnitSide(SideType side)
    {
        if (IsBuildingEntity)
            throw new InvalidOperationException($"LogicEntityState.SetUnitSide failed: entity {EntityId.Value} is a building.");
        SideType oldSide = Side;
        if (oldSide == side)
            return;
        Side = side;
        FlowFieldCrowdMovementSystem.SetAgentSide(EntityId.Value, side);
        if (m_Brain is IBrainSideChangeHandler sideChangeHandler)
            sideChangeHandler.OnSideChanged(this, oldSide, side);
    }
    public void SetMoveComp(IMoveComp moveComp) => m_MoveComp = moveComp ?? throw new ArgumentNullException(nameof(moveComp));
    public void SetAtkComp(IAtkComp atkComp) => m_AtkComp = atkComp ?? throw new ArgumentNullException(nameof(atkComp));
    public void SetTargetingComp(ITargetingComp targetingComp) => m_TargetingComp = targetingComp ?? throw new ArgumentNullException(nameof(targetingComp));
    public void SetWeaponComp(WeaponComp weaponComp) => m_WeaponComp = weaponComp ?? throw new ArgumentNullException(nameof(weaponComp));
    public void SetSkillComp(ISkillComp skillComp) => m_SkillComp = skillComp ?? throw new ArgumentNullException(nameof(skillComp));
    public void CancelRunningSkills() => m_SkillComp?.CancelSkills();
    public Fix64 GetProperty(CreatureMainProperty prop) => RequireProperties().GetProperty(prop);

    internal void ActivateRuntime()
    {
        if (!IsSpawnCommitted)
            throw new InvalidOperationException($"LogicEntityState.ActivateRuntime failed: entity {EntityId.Value} is not spawn-committed.");

        if (IsPlayerEntity)
            EntityRegistry.RegisterAsPlayer(this);
        else
            EntityRegistry.Register(this);
        LogicPhaseCommandService.PhaseApplied += OnLogicPhaseApplied;

        if (UsesFlowNavigationAgent)
        {
            if (!GroupMoveManager.HasInstance)
                throw new InvalidOperationException($"LogicEntityState.ActivateRuntime failed: GroupMoveManager is unavailable. entity={EntityId.Value}.");
            GroupMoveManager.Instance.RegisterAgent(this);
        }

        if (IsBuildingEntity && BlocksLogicMovement && !IsNavigationStaticBaked)
            ScheduleObstacleAdds(true);
    }

    internal void ValidateReadyForSpawn()
    {
        EnsureConfigured();
        if (m_MoveComp == null)
            throw new InvalidOperationException($"LogicEntityState spawn validation failed: move component is missing. entity={EntityId.Value}.");
        if (m_AtkComp == null)
            throw new InvalidOperationException($"LogicEntityState spawn validation failed: attack component is missing. entity={EntityId.Value}.");
        if (m_TargetingComp == null)
            throw new InvalidOperationException($"LogicEntityState spawn validation failed: targeting component is missing. entity={EntityId.Value}.");
    }

    internal void DeactivateRuntime(bool isShutdown = false)
    {
        if (!IsSpawnCommitted)
            return;

        LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;

        if (IsBuildingEntity && m_RegisteredObstacleIds.Count > 0)
        {
            if (isShutdown)
                m_RegisteredObstacleIds.Clear();
            else
                ScheduleObstacleRemovals(true);
        }

        if (UsesFlowNavigationAgent && GroupMoveManager.HasInstance)
            GroupMoveManager.Instance.UnregisterAgent(this);
        EntityRegistry.Unregister(this);
    }

    internal void ShutdownRuntime()
    {
        if (LogicPhaseCommandService.IsActive)
            LogicPhaseCommandService.PhaseApplied -= OnLogicPhaseApplied;
        m_SkillComp?.CancelSkills();
        m_BuffComp?.ShutDown();
        m_CreatureProperties?.Dispose();
        m_CreatureProperties = null;
        m_RegisteredObstacleIds.Clear();
    }

    public void TakeDamage(Fix64 damage, HealthModifyType modType, IEntityContext attacker = null)
    {
        if (!Alive || damage <= Fix64.Zero)
            return;
        if (m_InvincibleSources.Count > 0)
            return;

        if (IsBuildingEntity && modType == HealthModifyType.reduce)
            damage = Fix64.Max(Fix64.Zero, damage - Fix64.Max(Fix64.Zero, GetProperty(CreatureMainProperty.Def)));
        if (damage <= Fix64.Zero)
            return;

        ModifyHealth(-damage);
        if (IsBuildingEntity)
            LogicProductionConditionState.RecordBuildingDamaged(this);
        m_OutOfCombatStart = m_CombatClock;
        m_TargetingComp?.NotifyDamageTaken(attacker);
        if (HealthValue <= Fix64.Zero)
        {
            if (IsBuildingEntity)
                EnterBuildingDisabledState(attacker);
            else if (IsHeroEntity)
                HandleHeroZeroHealth();
            else
                EnterUnitDeath(attacker);
        }
    }

    public void Heal(Fix64 amount)
    {
        if (!Alive || amount <= Fix64.Zero)
            return;
        CreaturePropertyManager properties = RequireProperties();
        Fix64 actual = Fix64.Min(amount, properties.GetProperty(CreatureMainProperty.Health) - HealthValue);
        if (actual <= Fix64.Zero)
            return;
        Fix64 healed = ModifyHealth(actual);
        m_BuffComp.OnHealed(healed);
    }

    public bool RegisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Invincible source id is empty.", nameof(sourceId));
        if (!m_InvincibleSources.Add(sourceId))
            return false;
        EnsureSharedInvincibleBuff();
        return true;
    }

    public bool UnregisterInvincibleSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Invincible source id is empty.", nameof(sourceId));
        if (!m_InvincibleSources.Remove(sourceId))
            return false;
        if (m_InvincibleSources.Count == 0)
            m_BuffComp.RemoveBuff(InvincibleStateBuff.BuffId);
        return true;
    }

    public bool CanRun(ICapability capability) => capability != null && !m_CapabilityLockers.ContainsKey(capability);

    public void LockComp(ICapability toLock, ICapability locker)
    {
        if (toLock == null)
            throw new ArgumentNullException(nameof(toLock));
        if (locker == null)
            throw new ArgumentNullException(nameof(locker));
        if (!m_CapabilityLockers.TryGetValue(toLock, out List<ICapability> lockers))
        {
            lockers = new List<ICapability>();
            m_CapabilityLockers.Add(toLock, lockers);
            toLock.ShutDown();
        }
        if (!lockers.Contains(locker))
            lockers.Add(locker);
    }

    public void ResumeComp(ICapability toResume, ICapability locker)
    {
        if (toResume == null)
            throw new ArgumentNullException(nameof(toResume));
        if (locker == null)
            throw new ArgumentNullException(nameof(locker));
        if (!m_CapabilityLockers.TryGetValue(toResume, out List<ICapability> lockers) || !lockers.Remove(locker))
            throw new InvalidOperationException("LogicEntityState.ResumeComp failed: locker is not registered.");
        if (lockers.Count == 0)
        {
            m_CapabilityLockers.Remove(toResume);
            toResume.Resume();
        }
    }

    internal void WriteDeterministicControlState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));

        m_DeterministicStringValues.Clear();
        foreach (string source in m_InvincibleSources)
            m_DeterministicStringValues.Add(source);
        m_DeterministicStringValues.Sort(s_DeterministicStringComparison);
        hasher.Add(m_DeterministicStringValues.Count);
        for (int i = 0; i < m_DeterministicStringValues.Count; i++)
            hasher.Add(m_DeterministicStringValues[i]);

        int writtenCapabilityCount = 0;
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "Move", m_MoveComp);
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "Attack", m_AtkComp);
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "Targeting", m_TargetingComp);
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "DurationMove", m_DurationMoveEffectComp);
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "Weapon", m_WeaponComp);
        writtenCapabilityCount += WriteCapabilityLockState(hasher, "Skill", m_SkillComp);
        if (writtenCapabilityCount != m_CapabilityLockers.Count)
        {
            throw new InvalidOperationException(
                $"LogicEntityState deterministic control state has an unsupported capability key. entity={EntityId.Value}.");
        }
    }

    private int WriteCapabilityLockState(LogicStateHasher hasher, string slot, ICapability capability)
    {
        hasher.Add(slot);
        if (capability == null || !m_CapabilityLockers.TryGetValue(capability, out List<ICapability> lockers))
        {
            hasher.Add(0);
            return 0;
        }

        m_DeterministicStringValues.Clear();
        for (int i = 0; i < lockers.Count; i++)
        {
            ICapability locker = lockers[i]
                ?? throw new InvalidOperationException(
                    $"LogicEntityState capability locker is null. entity={EntityId.Value}, slot={slot}, index={i}.");
            m_DeterministicStringValues.Add(locker.GetType().FullName);
        }
        m_DeterministicStringValues.Sort(s_DeterministicStringComparison);
        hasher.Add(m_DeterministicStringValues.Count);
        for (int i = 0; i < m_DeterministicStringValues.Count; i++)
            hasher.Add(m_DeterministicStringValues[i]);
        return 1;
    }

    public void BeginLogicFrame(Fix64 deltaTime)
    {
        EnsureConfigured();
        if (!IsLogicActive || m_FrameActive || deltaTime != LogicFrameRuntime.FixedDeltaTime)
            throw new InvalidOperationException($"LogicEntityState.BeginLogicFrame failed. entity={EntityId.Value}.");
        m_FrameActive = true;
        m_NextPhase = MAEntityLogicFramePhase.BaseAndBuffs;
    }

    public void ExecuteLogicFramePhase(MAEntityLogicFramePhase phase, Fix64 deltaTime)
    {
        if (!m_FrameActive || phase != m_NextPhase)
            throw new InvalidOperationException($"LogicEntityState phase mismatch. entity={EntityId.Value}, expected={m_NextPhase}, actual={phase}.");

        switch (phase)
        {
            case MAEntityLogicFramePhase.BaseAndBuffs:
                m_CombatClock += deltaTime;
                if (CanRun(m_BuffComp))
                    m_BuffComp.UpdateBuff(deltaTime);
                break;
            case MAEntityLogicFramePhase.NavigationSync:
                if (UsesFlowNavigationAgent && GroupMoveManager.HasInstance)
                    GroupMoveManager.Instance.UpdateAgentPosition(this);
                break;
            case MAEntityLogicFramePhase.Brain:
                if (m_Brain is ITickBrain tickBrain)
                    tickBrain.Tick(this, deltaTime);
                break;
            case MAEntityLogicFramePhase.Targeting:
                if (CanRun(m_TargetingComp))
                    m_TargetingComp.UpdateTargeting(deltaTime);
                break;
            case MAEntityLogicFramePhase.Attack:
                if (CanRun(m_AtkComp))
                    m_AtkComp.Attack(deltaTime);
                break;
            case MAEntityLogicFramePhase.MoveIntent:
                if (Alive && CanRun(m_DurationMoveEffectComp))
                    m_DurationMoveEffectComp.ApplyEffect(deltaTime);
                if (Alive && CanRun(m_MoveComp))
                    m_MoveComp.Move(deltaTime);
                break;
            case MAEntityLogicFramePhase.MoveResolve:
                m_MoveExecutor.PrepareLogicFrame(LogicFrameRuntime.CurrentFrame, deltaTime, Alive);
                break;
            case MAEntityLogicFramePhase.MoveCommit:
                FixVector2 resolved = LogicAgentCollisionShadowService.GetRequiredResolvedPosition(EntityId, LogicFrameRuntime.CurrentFrame);
                if (m_DurationMoveEffectComp.IsInLossOfBalance)
                {
                    LogicAgentCollisionShadowState collisionState = LogicAgentCollisionShadowService.GetRequiredState(
                        EntityId,
                        LogicFrameRuntime.CurrentFrame);
                    m_DurationMoveEffectComp.CommitStaticCollision(collisionState.FirstHitNormal);
                }
                FixVector2 displacement = resolved - Position;
                Position = resolved;
                if (FixVector2.SqrMagnitude(displacement) > Fix64.Zero)
                    Forward = displacement.GetNormalized();
                m_MoveComp.CommitResolvedDisplacement(displacement);
                m_MoveExecutor.CommitPreparedLogicFrame(LogicFrameRuntime.CurrentFrame);
                break;
            case MAEntityLogicFramePhase.PostUpdate:
                if (CanRun(m_SkillComp))
                    m_SkillComp.Skill(deltaTime);
                break;
        }

        RefreshOutOfCombat();
        m_NextPhase = (MAEntityLogicFramePhase)((int)phase + 1);
    }

    public void CompleteLogicFrame(Fix64 deltaTime)
    {
        if (!m_FrameActive || m_NextPhase != MAEntityLogicFramePhase.Count)
            throw new InvalidOperationException($"LogicEntityState.CompleteLogicFrame failed: entity {EntityId.Value} phase={m_NextPhase}.");
        m_FrameActive = false;
    }

    public bool CanBeSelected() => Alive;
    public void InSelection(ISelector selector) { }
    public void DeSelection() { }

    private void RefreshOutOfCombat()
    {
        bool outOfCombat = Alive
                           && !(m_TargetingComp?.CurrentTarget?.IsAttackTargetable() ?? false)
                           && !(m_AtkComp?.IsAttacking ?? false);
        if (outOfCombat != IsOutOfCombat)
            m_OutOfCombatStart = m_CombatClock;
        IsOutOfCombat = outOfCombat;
    }

    private void OnLogicPhaseApplied(GamePhase oldPhase, GamePhase newPhase)
    {
        if (oldPhase == newPhase)
            return;

        if (IsHeroEntity)
        {
            if (InGameDataModel.IsBuildPhase(newPhase))
                CancelRunningSkills();
            if (m_BuffComp.HasBuff(HeroGhostBuffId))
                m_BuffComp.RemoveBuff(HeroGhostBuffId);
            else
                RestoreHealthToFull();
        }

        if (IsBuildingEntity
            && m_OwnerFactionId == EntitySideHelper.PlayerFactionId
            && InGameDataModel.IsBuildPhase(newPhase))
        {
            RestoreBuildingToFullHealth();
        }
    }

    private CreaturePropertyManager RequireProperties()
    {
        return m_CreatureProperties
               ?? throw new InvalidOperationException($"LogicEntityState properties are not configured. entity={EntityId.Value}.");
    }

    private void EnterBuildingDisabledState(IEntityContext attacker)
    {
        ClampHealthToZero();
        IsDisabled = true;
        Alive = false;
        m_TargetingComp.CurrentTarget = null;
        LockComp(m_AtkComp, DisabledCapabilityLocker);
        LockComp(m_TargetingComp, DisabledCapabilityLocker);
        m_CombatCapabilitiesLockedForDisabled = true;
        m_BuffComp.OnHostDead();
        BuildingDisabledChanged?.Invoke(true, attacker);
        LogicBuildingDisabledEventService.Publish(this, attacker);
    }

    private void HandleHeroZeroHealth()
    {
        if (LevelTagRuntime.TryConsumeHeroRevive(this))
        {
            RestoreHealthToFull();
            return;
        }

        ClampHealthToZero();
        m_DurationMoveEffectComp.StopAllMove();

        if (m_BuffComp.HasBuff(HeroGhostBuffId))
            return;
        m_BuffComp.AddBuff(
            BuffData.Create(
                HeroGhostBuffId,
                Fix64.Zero,
                true,
                1,
                new List<BuffCallback> { new HeroGhostBuff() }),
            this);
    }

    private void EnterUnitDeath(IEntityContext attacker)
    {
        ClampHealthToZero();
        Alive = false;
        m_DurationMoveEffectComp.StopAllMove();
        LogicProductionConditionState.RecordUnitDeath(this);
        LogicUnitDeathEventService.Publish(this);
        m_BuffComp.OnHostDead();
        attacker?.BuffComp?.OnKill(this);
        UnitDied?.Invoke(attacker);
        LogicEntityLifecycleService.RequestDespawn(EntityId);
    }

    private void RestoreHealthToFull()
    {
        CreaturePropertyManager properties = RequireProperties();
        Fix64 max = properties.GetProperty(CreatureMainProperty.Health);
        Fix64 delta = max - HealthValue;
        if (delta != Fix64.Zero)
            ModifyHealth(delta);
        Alive = true;
    }

    private Fix64 ModifyHealth(Fix64 delta)
    {
        if (delta == Fix64.Zero)
            return Fix64.Zero;

        CreaturePropertyManager properties = RequireProperties();
        Fix64 before = HealthValue;
        properties.ModifyCurrentProperty(
            CreatureCurrentProperty.HealthCurrent,
            PropertyIrreversibleAdditiveModifier.Create(delta),
            true);
        Fix64 current = HealthValue;
        Fix64 actualDelta = current - before;
        if (actualDelta != Fix64.Zero)
        {
            HealthChanged?.Invoke(new LogicEntityHealthChange(
                current,
                properties.GetProperty(CreatureMainProperty.Health),
                actualDelta));
        }
        return actualDelta;
    }

    private void ClampHealthToZero()
    {
        Fix64 current = HealthValue;
        if (current < Fix64.Zero)
            ModifyHealth(-current);
    }

    private void EnsureSharedInvincibleBuff()
    {
        if (m_BuffComp == null)
            throw new InvalidOperationException($"LogicEntityState.EnsureSharedInvincibleBuff failed: BuffComp is missing. entity={EntityId.Value}.");
        if (m_BuffComp.HasBuff(InvincibleStateBuff.BuffId))
            return;

        m_BuffComp.AddBuff(
            BuffData.Create(
                InvincibleStateBuff.BuffId,
                Fix64.Zero,
                true,
                1,
                new List<BuffCallback> { new InvincibleStateBuff() }),
            this);
    }

    private void ScheduleObstacleAdds(bool currentLifecycleFrame)
    {
        if (m_RegisteredObstacleIds.Count > 0)
            throw new InvalidOperationException($"LogicEntityState.ScheduleObstacleAdds failed: entity {EntityId.Value} already has registered obstacles.");

        for (int i = 0; i < m_LogicObstacleShapes.Count; i++)
        {
            LogicCombatShape shape = m_LogicObstacleShapes[i];
            int obstacleId = LogicEntityObstacleId.FromBuildingCollider(EntityId, i);
            if (currentLifecycleFrame)
                LogicObstacleCommandService.ScheduleBoxForCurrentLifecycleFrame(obstacleId, shape.Center, shape.HalfExtents);
            else
                LogicObstacleCommandService.ScheduleBoxForNextFrame(obstacleId, shape.Center, shape.HalfExtents);
            m_RegisteredObstacleIds.Add(obstacleId);
        }
    }

    private void ScheduleObstacleRemovals(bool currentLifecycleFrame)
    {
        for (int i = 0; i < m_RegisteredObstacleIds.Count; i++)
        {
            int obstacleId = m_RegisteredObstacleIds[i];
            if (currentLifecycleFrame)
                LogicObstacleCommandService.ScheduleRemoveForCurrentLifecycleFrame(obstacleId);
            else
                LogicObstacleCommandService.ScheduleRemoveForNextFrame(obstacleId);
        }
        m_RegisteredObstacleIds.Clear();
    }

    private void EnsureConfigured()
    {
        if (!m_IsConfigured)
            throw new InvalidOperationException($"LogicEntityState is not configured. entity={EntityId.Value}.");
    }

    private sealed class StateCapabilityLocker : ICapability
    {
        public void ShutDown() { }
        public void Resume() { }
    }
}

public static class LogicEntityStateStore
{
    private static readonly Dictionary<int, LogicEntityState> s_States =
        new Dictionary<int, LogicEntityState>();
    private static readonly List<int> s_DeterministicIds = new List<int>();
    private static readonly Comparison<int> s_DeterministicIdComparison = CompareInts;
    private static readonly Comparison<LogicEntityState> s_EntityStateComparison = CompareEntityStatesById;

    public static bool IsActive { get; private set; }
    public static int Count => s_States.Count;

    public static void BeginTimeline()
    {
        if (IsActive)
            throw new InvalidOperationException("LogicEntityStateStore.BeginTimeline failed: store is already active.");
        if (s_States.Count != 0)
            throw new InvalidOperationException("LogicEntityStateStore.BeginTimeline failed: stale states remain.");

        IsActive = true;
    }

    public static void EndTimeline()
    {
        EnsureActive();
        ShutdownAllStates();
        s_States.Clear();
        IsActive = false;
    }

    public static void ResetForWorldTransition()
    {
        EnsureActive();
        ShutdownAllStates();
        s_States.Clear();
    }

    private static void ShutdownAllStates()
    {
        foreach (LogicEntityState state in s_States.Values)
            state.ShutdownRuntime();
    }

    public static LogicEntityState Create(LogicEntityId entityId, LogicEntitySpawnDescriptor descriptor)
    {
        EnsureActive();
        var state = new LogicEntityState(entityId, descriptor);
        if (s_States.ContainsKey(entityId.Value))
            throw new InvalidOperationException($"LogicEntityStateStore.Create failed: duplicate entity {entityId.Value}.");

        s_States.Add(entityId.Value, state);
        return state;
    }

    public static LogicEntityState GetRequired(LogicEntityId entityId)
    {
        EnsureActive();
        if (!entityId.IsValid)
            throw new ArgumentException("Logic entity state lookup requires a valid id.", nameof(entityId));
        if (!s_States.TryGetValue(entityId.Value, out LogicEntityState state))
            throw new InvalidOperationException($"LogicEntityStateStore does not contain entity {entityId.Value}.");
        return state;
    }

    public static void BindView(LogicEntityId entityId, int viewEntityId)
    {
        if (viewEntityId <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewEntityId));

        LogicEntityState state = GetRequired(entityId);
        if (state.HasBoundView)
            throw new InvalidOperationException($"LogicEntityStateStore.BindView failed: entity {entityId.Value} already has view {state.BoundViewEntityId}.");
        state.BoundViewEntityId = viewEntityId;
    }

    public static void UnbindView(LogicEntityId entityId, int viewEntityId)
    {
        LogicEntityState state = GetRequired(entityId);
        if (state.BoundViewEntityId != viewEntityId)
        {
            throw new InvalidOperationException(
                $"LogicEntityStateStore.UnbindView failed: entity {entityId.Value} view mismatch. expected={state.BoundViewEntityId}, actual={viewEntityId}.");
        }
        state.BoundViewEntityId = 0;
    }

    public static void RemoveDespawned(LogicEntityId entityId)
    {
        LogicEntityState state = GetRequired(entityId);
        if (state.IsSpawnCommitted)
            throw new InvalidOperationException($"LogicEntityStateStore.RemoveDespawned failed: entity {entityId.Value} is still committed.");
        if (state.HasBoundView)
            throw new InvalidOperationException($"LogicEntityStateStore.RemoveDespawned failed: entity {entityId.Value} still has view {state.BoundViewEntityId}.");
        state.ShutdownRuntime();
        if (!s_States.Remove(entityId.Value))
            throw new InvalidOperationException($"LogicEntityStateStore.RemoveDespawned failed: entity {entityId.Value} disappeared.");
    }

    public static void CommitSpawn(LogicEntityId entityId)
    {
        LogicEntityState state = GetRequired(entityId);
        if (state.IsSpawnCommitted)
            throw new InvalidOperationException($"LogicEntityStateStore.CommitSpawn failed: entity {entityId.Value} is already committed.");
        if (state.IsDespawnCommitted)
            throw new InvalidOperationException($"LogicEntityStateStore.CommitSpawn failed: entity {entityId.Value} was already despawned.");
        state.ValidateReadyForSpawn();
        state.IsSpawnCommitted = true;
    }

    public static void CommitDespawn(LogicEntityId entityId)
    {
        LogicEntityState state = GetRequired(entityId);
        if (!state.IsSpawnCommitted)
            throw new InvalidOperationException($"LogicEntityStateStore.CommitDespawn failed: entity {entityId.Value} is not committed.");
        state.IsSpawnCommitted = false;
        state.IsDespawnCommitted = true;
    }

    public static void WriteDeterministicState(LogicStateHasher hasher)
    {
        if (hasher == null)
            throw new ArgumentNullException(nameof(hasher));
        EnsureActive();

        s_DeterministicIds.Clear();
        foreach (KeyValuePair<int, LogicEntityState> pair in s_States)
        {
            if (!pair.Value.IsDespawnCommitted)
                s_DeterministicIds.Add(pair.Key);
        }
        s_DeterministicIds.Sort(s_DeterministicIdComparison);
        hasher.Add(s_DeterministicIds.Count);
        for (int i = 0; i < s_DeterministicIds.Count; i++)
        {
            LogicEntityState state = s_States[s_DeterministicIds[i]];
            hasher.Add(state.EntityId.Value);
            hasher.Add(state.Position.x.RawValue);
            hasher.Add(state.Position.y.RawValue);
            hasher.Add(state.Forward.x.RawValue);
            hasher.Add(state.Forward.y.RawValue);
            hasher.Add((int)state.Side);
            hasher.Add(state.CharacterKey);
            hasher.Add(state.SourceStrongholdId);
            hasher.Add(state.IsSpawnCommitted);
            state.WriteDeterministicControlState(hasher);
            hasher.Add(state.IsBuildingEntity);
            if (state.IsBuildingEntity)
            {
                hasher.Add(state.BuildingData.Identifier);
                hasher.Add(state.BuildingInstanceId);
                hasher.Add(state.StrongholdId);
                hasher.Add(state.OwnerFactionId);
                hasher.Add(state.IsGameEndConditionBuilding);
                hasher.Add(state.IsNavigationStaticBaked);
                LogicInteractionOptionService.WriteDeterministicState(hasher, state.InteractionOptions);
            }
        }
    }

    internal static LogicEntityState[] CapturePendingSpawnStates()
    {
        var pending = new List<LogicEntityState>();
        CapturePendingSpawnStates(pending);
        return pending.ToArray();
    }

    internal static void CapturePendingSpawnStates(List<LogicEntityState> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        EnsureActive();

        destination.Clear();
        foreach (KeyValuePair<int, LogicEntityState> pair in s_States)
        {
            if (!pair.Value.IsSpawnCommitted && !pair.Value.IsDespawnCommitted)
                destination.Add(pair.Value);
        }
        destination.Sort(s_EntityStateComparison);
    }

    private static int CompareEntityStatesById(LogicEntityState left, LogicEntityState right)
    {
        if (left == null || right == null)
            throw new InvalidOperationException("LogicEntityStateStore contains a null state.");
        return left.EntityId.Value.CompareTo(right.EntityId.Value);
    }

    private static int CompareInts(int left, int right)
    {
        return left.CompareTo(right);
    }

    internal static LogicEntityState[] CaptureStageBuildingStates()
    {
        EnsureActive();
        var ids = new List<int>();
        foreach (KeyValuePair<int, LogicEntityState> pair in s_States)
        {
            if (!pair.Value.IsDespawnCommitted && pair.Value.IsBuildingEntity)
                ids.Add(pair.Key);
        }

        ids.Sort();
        var states = new LogicEntityState[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            LogicEntityState state = s_States[ids[i]];
            state.ValidateReadyForSpawn();
            states[i] = state;
        }
        return states;
    }

    private static void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("LogicEntityStateStore operation failed: store is not active.");
    }
}
