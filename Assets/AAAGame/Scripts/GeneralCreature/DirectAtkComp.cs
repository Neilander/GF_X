using UnityEngine;
using System;
using System.Collections.Generic;
using AAAGame.Scripts.BuffSystem;

public readonly struct DirectAttackDeterministicState
{
    public DirectAttackDeterministicState(
        DirectAtkComp.AtkState state,
        int attackCount,
        int activeWeaponIndex,
        bool hasSchedule,
        ulong startFrame,
        ulong hitFrame,
        ulong recoveryEndFrame,
        ulong readyFrame,
        bool hitCommitted,
        bool recoveryCommitted,
        bool hasAttackStartFrame,
        ulong lastAttackStartFrame,
        bool movementLockedByThisAttack,
        int lockedTargetId,
        int[] lockedTargetIds)
    {
        State = state;
        AttackCount = attackCount;
        ActiveWeaponIndex = activeWeaponIndex;
        HasSchedule = hasSchedule;
        StartFrame = startFrame;
        HitFrame = hitFrame;
        RecoveryEndFrame = recoveryEndFrame;
        ReadyFrame = readyFrame;
        HitCommitted = hitCommitted;
        RecoveryCommitted = recoveryCommitted;
        HasAttackStartFrame = hasAttackStartFrame;
        LastAttackStartFrame = lastAttackStartFrame;
        MovementLockedByThisAttack = movementLockedByThisAttack;
        LockedTargetId = lockedTargetId;
        LockedTargetIds = lockedTargetIds != null ? (int[])lockedTargetIds.Clone() : Array.Empty<int>();
    }

    public DirectAtkComp.AtkState State { get; }
    public int AttackCount { get; }
    public int ActiveWeaponIndex { get; }
    public bool HasSchedule { get; }
    public ulong StartFrame { get; }
    public ulong HitFrame { get; }
    public ulong RecoveryEndFrame { get; }
    public ulong ReadyFrame { get; }
    public bool HitCommitted { get; }
    public bool RecoveryCommitted { get; }
    public bool HasAttackStartFrame { get; }
    public ulong LastAttackStartFrame { get; }
    public bool MovementLockedByThisAttack { get; }
    public int LockedTargetId { get; }
    public int[] LockedTargetIds { get; }
    public int LockedTargetCount => LockedTargetIds.Length;
}

/// <summary>
/// 直接攻击组件：选定目标后直接造成伤害，不使用攻击盒。
/// 适合大量小兵的战斗场景。
///
/// 攻击流程：
/// 1. 检测 Brain.Attack 且有目标在攻击范围内
/// 2. 进入前摇阶段（WindUp），锁定移动
/// 3. 前摇结束时对目标造成伤害,
/// 4. 进入后摇阶段（WindDown）,
/// 5. 后摇结束，解锁移动，进入冷却
///
/// 纯逻辑实现，不依赖 Animator/HitBox/BasicAction。
/// </summary>
public class DirectAtkComp : IAtkComp
{
    private static readonly List<BuffCallback> EmptyBuffModuleSnapshot = new List<BuffCallback>(0);

    private readonly struct AttackSchedule
    {
        public AttackSchedule(ulong startFrame, ulong hitFrame, ulong recoveryEndFrame, ulong readyFrame)
        {
            StartFrame = startFrame;
            HitFrame = hitFrame;
            RecoveryEndFrame = recoveryEndFrame;
            ReadyFrame = readyFrame;
        }

        public ulong StartFrame { get; }
        public ulong HitFrame { get; }
        public ulong RecoveryEndFrame { get; }
        public ulong ReadyFrame { get; }
        public ulong NextAttackFrame => ReadyFrame > RecoveryEndFrame ? ReadyFrame : RecoveryEndFrame;
    }

    public enum AtkState
    {
        Idle,
        WindUp,
        WindDown,
        Cooldown
    }

    private IEntityContext _ctx;
    private Weapon _weapon;
    private Weapon[] _weapons;
    private int _activeWeaponIndex;
    private BaseWeaponSO _weaponSO;
    private Animator _animator;

    public BaseWeaponSO WeaponSO => _weaponSO;

    public void SetWeaponSO(BaseWeaponSO so) => _weaponSO = so;

    public AtkState State { get; private set; } = AtkState.Idle;
    public int AttackCount { get; private set; }
    public bool IsAttacking => State == AtkState.WindUp || State == AtkState.WindDown;

    private AttackSchedule _schedule;
    private bool _hasSchedule;
    private bool _hitCommitted;
    private bool _recoveryCommitted;
    private bool _hasAttackStartFrame;
    private ulong _lastAttackStartFrame;
    private IEntityContext _lockedTarget;
    private readonly List<IEntityContext> _lockedTargets = new List<IEntityContext>();
    private bool _movementLockedByThisAttack;

    public DirectAttackDeterministicState CaptureDeterministicState()
    {
        var lockedTargetIds = new int[_lockedTargets.Count];
        for (int i = 0; i < _lockedTargets.Count; i++)
        {
            IEntityContext target = _lockedTargets[i];
            if (target == null || !target.LogicEntityId.IsValid)
                throw new InvalidOperationException($"DirectAtkComp snapshot contains an invalid locked target. index={i}.");
            lockedTargetIds[i] = target.LogicEntityId.Value;
        }

        return new DirectAttackDeterministicState(
            State,
            AttackCount,
            _activeWeaponIndex,
            _hasSchedule,
            _hasSchedule ? _schedule.StartFrame : 0,
            _hasSchedule ? _schedule.HitFrame : 0,
            _hasSchedule ? _schedule.RecoveryEndFrame : 0,
            _hasSchedule ? _schedule.ReadyFrame : 0,
            _hitCommitted,
            _recoveryCommitted,
            _hasAttackStartFrame,
            _lastAttackStartFrame,
            _movementLockedByThisAttack,
            _lockedTarget != null && _lockedTarget.LogicEntityId.IsValid ? _lockedTarget.LogicEntityId.Value : 0,
            lockedTargetIds);
    }

    public void RestoreDeterministicState(DirectAttackDeterministicState snapshot)
    {
        if (_ctx == null || _weapons == null || _weapons.Length == 0)
            throw new InvalidOperationException("DirectAtkComp.RestoreDeterministicState failed: component is not initialized.");
        if (snapshot.ActiveWeaponIndex < 0 || snapshot.ActiveWeaponIndex >= _weapons.Length)
            throw new InvalidOperationException($"DirectAtkComp.RestoreDeterministicState failed: weapon index {snapshot.ActiveWeaponIndex} is invalid.");
        if (!snapshot.HasSchedule && snapshot.State != AtkState.Idle)
            throw new InvalidOperationException($"DirectAtkComp.RestoreDeterministicState failed: state {snapshot.State} has no schedule.");
        if (snapshot.HasSchedule && snapshot.ReadyFrame < snapshot.StartFrame)
            throw new InvalidOperationException("DirectAtkComp.RestoreDeterministicState failed: attack schedule is invalid.");

        var targetsById = new Dictionary<int, IEntityContext>();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
            targetsById.Add(entities[i].LogicEntityId.Value, entities[i]);

        var restoredTargets = new List<IEntityContext>(snapshot.LockedTargetIds.Length);
        var uniqueTargetIds = new HashSet<int>();
        for (int i = 0; i < snapshot.LockedTargetIds.Length; i++)
        {
            int targetId = snapshot.LockedTargetIds[i];
            if (!uniqueTargetIds.Add(targetId) || !targetsById.TryGetValue(targetId, out IEntityContext target))
                throw new InvalidOperationException($"DirectAtkComp.RestoreDeterministicState failed: locked target {targetId} is missing or duplicated.");
            restoredTargets.Add(target);
        }

        IEntityContext primaryTarget = null;
        if (snapshot.LockedTargetId != 0 && !targetsById.TryGetValue(snapshot.LockedTargetId, out primaryTarget))
            throw new InvalidOperationException($"DirectAtkComp.RestoreDeterministicState failed: primary target {snapshot.LockedTargetId} is missing.");
        if (restoredTargets.Count > 0 && !ReferenceEquals(primaryTarget, restoredTargets[0]))
            throw new InvalidOperationException("DirectAtkComp.RestoreDeterministicState failed: primary target does not match the first locked target.");

        if (_movementLockedByThisAttack && !snapshot.MovementLockedByThisAttack)
            ReleaseMovementLock();
        else if (!_movementLockedByThisAttack && snapshot.MovementLockedByThisAttack)
            _ctx.LockComp(_ctx.MoveComp, this);

        _activeWeaponIndex = snapshot.ActiveWeaponIndex;
        _weapon = _weapons[_activeWeaponIndex];
        if (!ReferenceEquals(_ctx.WeaponComp?.Data, _weapon))
            _ctx.WeaponComp?.SwapWeapon(_weapon);
        State = snapshot.State;
        AttackCount = snapshot.AttackCount;
        _hasSchedule = snapshot.HasSchedule;
        _schedule = snapshot.HasSchedule
            ? new AttackSchedule(snapshot.StartFrame, snapshot.HitFrame, snapshot.RecoveryEndFrame, snapshot.ReadyFrame)
            : default;
        _hitCommitted = snapshot.HitCommitted;
        _recoveryCommitted = snapshot.RecoveryCommitted;
        _hasAttackStartFrame = snapshot.HasAttackStartFrame;
        _lastAttackStartFrame = snapshot.LastAttackStartFrame;
        _movementLockedByThisAttack = snapshot.MovementLockedByThisAttack;
        _lockedTarget = primaryTarget;
        _lockedTargets.Clear();
        _lockedTargets.AddRange(restoredTargets);
    }

    private string GetWeaponSOAddress(WeaponType index)
    {
        switch (index)
        {
            case WeaponType.Melee:
                return "soldier_default";

            case WeaponType.Projectile:
            case WeaponType.HealProjectile:
                return "ranged_default";

            case WeaponType.HealMelee:
                return "soldier_default";

            default:
                return "soldier_default";

        }
    }

    private string GetWeaponSOPath()
    {
        string overridePath = UnitWeaponSOOverrideResolver.GetWeaponSOPath(_ctx.CharacterKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return $"Assets/AAAGame/SOs/Weapon/{GetWeaponSOAddress(_weapon.Type)}.asset";
    }

    public void Init(IEntityContext ctx)
    {
        _ctx = ctx;
        State = AtkState.Idle;
        ResetSchedule();
        AttackCount = 0;
        _hasAttackStartFrame = false;
        _lastAttackStartFrame = 0;
        _lockedTarget = null;
        _lockedTargets.Clear();
        _movementLockedByThisAttack = false;

        // 获取Animator组件
        var entity = _ctx as MAEntity;
        if (entity != null)
        {
            _animator = entity.animator;
        }

        if (ctx.WeaponComp?.Data != null)
        {
            SetWeapons(new[] { ctx.WeaponComp.Data });
        }
        else
        {
            string id = ctx.CharacterKey;
            var row = ctx.CharacterData;
            Fix64 level = Fix64.One;
            if (ctx is MAEntity ownerEntity && ownerEntity.CreaturePropertyManager != null)
            {
                level = ownerEntity.CreaturePropertyManager.GetLevel();
            }

            WeaponData[] weaponDatas = CharacterDataDetailAccessor.GetWeaponDatas(row, level);
            if (weaponDatas == null || weaponDatas.Length == 0)
            {
                throw new InvalidOperationException($"角色 {id} 缺少武器数据");
            }

            SetWeapons(weaponDatas);
        }

        _activeWeaponIndex = 0;
        _weapon = _weapons[_activeWeaponIndex];

        WeaponHelper.LoadWeapon(GetWeaponSOPath(), this);

        // 创建 WeaponComp 并挂载到 Entity
        var wc = new WeaponComp(_weapon);
        var maEntity = _ctx as MAEntity;
        if (maEntity != null)
        {
            maEntity.SetWeaponComp(wc);
        }
        else
        {
        }
    }

    public void UpdateWeaponData(WeaponData weaponData)
    {
        if (weaponData == null)
            return;

        SetWeapons(new[] { weaponData });
        _activeWeaponIndex = 0;
        _weapon = _weapons[_activeWeaponIndex];

        var maEntity = _ctx as MAEntity;
        if (maEntity != null)
        {
            if (maEntity.weaponComp == null)
                maEntity.SetWeaponComp(new WeaponComp(_weapon));
            else
                maEntity.weaponComp.SwapWeapon(_weapon);
        }

        WeaponHelper.LoadWeapon(GetWeaponSOPath(), this);
        ShutDown();
    }

    private void SetWeapons(WeaponData[] weaponDatas)
    {
        PropertyManager ownerManager = (_ctx as MAEntity)?.CreaturePropertyManager?.propertyManager;
        _weapons = new Weapon[weaponDatas.Length];
        for (int i = 0; i < weaponDatas.Length; i++)
        {
            _weapons[i] = weaponDatas[i].ToWeapon($"{_ctx.CharacterKey}_Weapon{i + 1}", ownerManager);
        }
    }

    private void SetWeapons(Weapon[] weapons)
    {
        _weapons = weapons;
    }

    public void SelectWeapon(int weaponIndex)
    {
        if (_weapons == null || _weapons.Length == 0 || weaponIndex < 0 || weaponIndex >= _weapons.Length)
        {
            GF.LogError($"请求的武器索引 {weaponIndex} 超出范围，返回默认武器");
            weaponIndex = 0;
        }
        _weapon = _weapons[weaponIndex];
        _ctx.WeaponComp.SwapWeapon(_weapon);
    }

public void Attack(Fix64 deltaTime)
    {
        if (_ctx == null) return;
        _ = deltaTime;

        ulong currentFrame = LogicFrameRuntime.CurrentFrame;
        if (!_hasSchedule)
        {
            TryStartAttack(currentFrame);
            if (_hasSchedule)
                AdvanceAttackSchedule(currentFrame, false);
            return;
        }

        AdvanceAttackSchedule(currentFrame, true);
    }

    private void AdvanceAttackSchedule(ulong currentFrame, bool allowRestart)
    {
        if (!_hitCommitted && currentFrame >= _schedule.HitFrame)
        {
            _hitCommitted = true;
            DealDamage();
            EnterState(AtkState.WindDown);
        }

        if (!_recoveryCommitted && currentFrame >= _schedule.RecoveryEndFrame)
        {
            _recoveryCommitted = true;
            ReleaseMovementLock();
            NotifyAttackCompleted(_lockedTarget);
            EnterState(AtkState.Cooldown);
        }

        if (currentFrame < _schedule.NextAttackFrame)
            return;

        EnterState(AtkState.Idle);
        ResetSchedule();
        _lockedTarget = null;
        _lockedTargets.Clear();

        if (!allowRestart)
            return;

        TryStartAttack(currentFrame);
        if (_hasSchedule)
            AdvanceAttackSchedule(currentFrame, false);
    }

    private void ReleaseMovementLock()
    {
        if (!_movementLockedByThisAttack)
            return;

        _ctx.ResumeComp(_ctx.MoveComp, this);
        _movementLockedByThisAttack = false;
    }

    private static ulong DurationToTicks(Fix64 duration)
    {
        if (duration <= Fix64.Zero)
            return 0;

        // WeaponData still stores seconds. Convert once at attack start using integer raw data;
        // the content migration can replace this compatibility boundary with authored tick counts.
        long scaledRaw = checked(duration.RawValue * LogicFrameRuntime.FrameRate);
        long roundedTicks = checked(scaledRaw + Fix64.One.RawValue / 2) / Fix64.One.RawValue;
        return checked((ulong)roundedTicks);
    }

    private static ulong AddFrames(ulong frame, ulong durationTicks)
    {
        return checked(frame + durationTicks);
    }

    private static AttackSchedule CreateSchedule(ulong startFrame, Weapon weapon)
    {
        ulong windUpTicks = DurationToTicks(weapon.WindUp);
        ulong windDownTicks = DurationToTicks(weapon.WindDown);
        ulong intervalTicks = DurationToTicks(weapon.Interval);
        ulong hitFrame = AddFrames(startFrame, windUpTicks);
        ulong recoveryEndFrame = AddFrames(hitFrame, windDownTicks);
        ulong readyFrame = AddFrames(startFrame, intervalTicks);
        return new AttackSchedule(startFrame, hitFrame, recoveryEndFrame, readyFrame);
    }

    private void ResetSchedule()
    {
        _schedule = default;
        _hasSchedule = false;
        _hitCommitted = false;
        _recoveryCommitted = false;
    }

    /// <summary>
    /// 获取当前生效的武器数据：优先从 WeaponComp 拿最新的，没有则用初始化时的 _weapon。
    /// </summary>
    private Weapon GetActiveWeapon()
    {
        return _ctx.WeaponComp?.Data ?? _weapon;
    }

    private Fix64 GetCurrentDamage()
    {
        return GetActiveWeapon().Atk;
    }

    private Fix64 GetCurrentAttackRange()
    {
        return GetActiveWeapon().Range;
    }

    private Fix64 GetCurrentWindUp()
    {
        return GetActiveWeapon().WindUp;
    }

    private Fix64 GetCurrentProjectileSpeed()
    {
        return GetActiveWeapon().ProjectileSpeed;
    }

    private Fix64 GetCurrentSplashRadius()
    {
        return GetActiveWeapon().SplashRadius;
    }

    private Fix64 GetCurrentAmmunitionCapacity()
    {
        return GetActiveWeapon().AmmunitionCapacity;
    }

    private void TryStartAttack(ulong currentFrame)
    {
        if (_hasAttackStartFrame && _lastAttackStartFrame == currentFrame)
            return;

        if (_ctx.Brain == null)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.CharacterKey}] TryStart: Brain=null");
            return;
        }

        bool manualAttack = _ctx.Brain.Attack;
        bool playerAutoAttack = _ctx.Brain is AAAGame.Scripts.Entity.PlayerBrain && _ctx.TargetComp?.CurrentTarget != null;
        if (!manualAttack && !playerAutoAttack)
        {
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.CharacterKey}] TryStart: Brain.Attack=false");
            return;
        }

        Weapon activeWeapon = GetActiveWeapon();
        var target = _ctx.TargetComp?.CurrentTarget;
        if (!WeaponTargetRules.IsValidTargetForCurrentWeapon(_ctx, target))
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] TryStart: 无目标 targetComp={(_ctx.TargetComp != null ? "有" : "null")} target={target} alive={target?.Alive}");
            return;
        }

        if (_ctx.WeaponComp != null && !_ctx.WeaponComp.HasAmmoToAttack)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] TryStart: 弹药耗尽 ammo={_ctx.WeaponComp.CurrentAmmo}/{_ctx.WeaponComp.MaxAmmo}");
            return;
        }

        Fix64 dist = _ctx.LogicFrameDistanceToTargetSurfaceFixed(target);
        Fix64 wpnRange = DistanceUnitConverter.ConvertToWorld(activeWeapon.Range);

        // 统一判定：攻击者中心到目标碰撞体边缘的 XZ 距离，和武器射程直接比较。
        Fix64 range = wpnRange;

        if (dist > range)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] TryStart: 超距 dist={(float)dist:F2} range={(float)range:F2} (wpn={(float)wpnRange:F2})");
            return;
        }

        LockAttackTargets(target, activeWeapon, range);
        if (_lockedTargets.Count == 0)
            return;

        _lockedTarget = _lockedTargets[0];
        _schedule = CreateSchedule(currentFrame, activeWeapon);
        _hasSchedule = true;
        _hitCommitted = false;
        _recoveryCommitted = false;
        _hasAttackStartFrame = true;
        _lastAttackStartFrame = currentFrame;
        AttackCount++;
        _movementLockedByThisAttack = ShouldLockMoveDuringAttack();
        if (_movementLockedByThisAttack)
            _ctx.LockComp(_ctx.MoveComp, this);
        FlowFieldCrowdMovementSystem.LogCombatClusterDiagnostic(_ctx, _lockedTarget, _movementLockedByThisAttack, (float)range);

        EnterState(AtkState.WindUp);
        NotifyAttackStarted(_lockedTarget);

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] → WindUp 第{AttackCount}次攻击 目标={target.CharacterKey} dist={(float)dist:F2} range={(float)range:F2} " +
            $"frames={_schedule.StartFrame}/{_schedule.HitFrame}/{_schedule.RecoveryEndFrame}/{_schedule.ReadyFrame}");
    }

    private void DealDamage()
    {
        Weapon activeWeapon = GetActiveWeapon();
        if (_lockedTargets.Count == 0)
            _lockedTargets.Add(_lockedTarget);

        RemoveInvalidLockedTargets(activeWeapon);
        if (_lockedTargets.Count == 0)
        {
            GameDebugSettings.Log(DebugCategory.Attack,
                $"[{_ctx.CharacterKey}] DealDamage: 目标丢失或已死 target={_lockedTarget} alive={_lockedTarget?.Alive}");
            return;
        }

        Fix64 damage = GetCurrentDamage();
        WeaponData snapshot = CreateWeaponSnapshot(activeWeapon);

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] DealDamage: 对 {_lockedTarget.CharacterKey} 造成 {damage} 伤害");

        bool missed = AttackMissUtility.ShouldMissAttack(_ctx);
        int targetCount = _lockedTargets.Count;
        if (missed)
        {
            ConsumeAmmoForLockedTargets(targetCount);
            GameDebugSettings.Log(DebugCategory.Attack, $"[{_ctx.CharacterKey}] DealDamage: 攻击落空 targetCount={targetCount}");
            return;
        }

        using (DamageHelper.BeginAttackHitSequence(_ctx, targetCount))
        {
            for (int i = 0; i < targetCount; i++)
            {
                IEntityContext target = _lockedTargets[i];
                if (_ctx.WeaponComp != null && !_ctx.WeaponComp.TryConsumeAmmo(1))
                {
                    GameDebugSettings.Log(DebugCategory.Attack,
                        $"[{_ctx.CharacterKey}] DealDamage: 弹药耗尽 ammo={_ctx.WeaponComp.CurrentAmmo}/{_ctx.WeaponComp.MaxAmmo}");
                    break;
                }

                ExecuteWeaponEffect(activeWeapon, target, snapshot);
            }
        }

        // 普通攻击造成伤害的音效；远程武器在这里只是创建子弹（命中是子弹的事），跳过
        bool isRanged = _weaponSO is RangedWeaponSO;
        if (!isRanged && AudioManager.Instance != null)
            AudioManager.Instance.Play("basicAttack");
    }

    private void ExecuteWeaponEffect(Weapon activeWeapon, IEntityContext target, WeaponData snapshot)
    {
        if (activeWeapon.Type == WeaponType.HealMelee)
        {
            HealingWeaponEffect.Execute(_ctx, target, snapshot);
        }
        else if (activeWeapon.Type == WeaponType.CleaveMelee || activeWeapon.Type == WeaponType.CleaveRanged)
        {
            AreaWeaponDamage.DealCleave(_ctx, target, snapshot);
        }
        else if (activeWeapon.Type == WeaponType.SelfAoE)
        {
            AreaWeaponDamage.DealSelfAoE(_ctx, target, snapshot);
        }
        else if (activeWeapon.Type == WeaponType.Special && _ctx is BuildingEntity building && BuildingAbilityIds.IsBuilding(building.buildingData, BuildingAbilityIds.Monitor))
        {
            MonitorWeaponEffect.Execute(_ctx, target, snapshot);
        }
        // 优先委托武器 SO 执行伤害
        else if (_weaponSO != null)
        {
            _weaponSO.Execute(_ctx, target, snapshot);
        }
        else
        {
            // 降级 fallback：走 DamageHelper 统一走 buff 钩子链路
            var dmg = new Damage(_ctx as ITargetable, snapshot.Damage, HealthModifyType.reduce);
            DamageHelper.DoDamage(target as ITargetable, dmg, _ctx);
        }
    }

    private void LockAttackTargets(IEntityContext mainTarget, Weapon activeWeapon, Fix64 attackRange)
    {
        _lockedTargets.Clear();
        int maxTargets = ResolveAttackTargetCount(activeWeapon);
        if (_ctx.WeaponComp != null && _ctx.WeaponComp.HasAmmunition)
            maxTargets = Mathf.Min(maxTargets, _ctx.WeaponComp.CurrentAmmo);

        AddLockedTargetIfValid(mainTarget, activeWeapon, attackRange, maxTargets);
        if (maxTargets <= 1 || !(_ctx.TargetComp is IMultiTargetingComp multiTargeting))
            return;

        IReadOnlyList<IEntityContext> targets = multiTargeting.CurrentTargets;
        if (targets == null)
            return;

        for (int i = 0; i < targets.Count && _lockedTargets.Count < maxTargets; i++)
            AddLockedTargetIfValid(targets[i], activeWeapon, attackRange, maxTargets);
    }

    private void AddLockedTargetIfValid(IEntityContext target, Weapon activeWeapon, Fix64 attackRange, int maxTargets)
    {
        if (_lockedTargets.Count >= maxTargets || target == null)
            return;
        if (_lockedTargets.Contains(target))
            return;
        if (!WeaponTargetRules.IsValidTargetForWeapon(_ctx, target, activeWeapon.Type))
            return;
        if (_ctx.LogicFrameDistanceToTargetSurfaceFixed(target) > attackRange)
            return;

        _lockedTargets.Add(target);
    }

    private void RemoveInvalidLockedTargets(Weapon activeWeapon)
    {
        for (int i = _lockedTargets.Count - 1; i >= 0; i--)
        {
            if (!WeaponTargetRules.IsValidTargetForWeapon(_ctx, _lockedTargets[i], activeWeapon.Type))
                _lockedTargets.RemoveAt(i);
        }
    }

    private int ResolveAttackTargetCount(Weapon activeWeapon)
    {
        if (activeWeapon == null)
            return 1;

        int count = (int)activeWeapon.ProjectileCount;
        return Mathf.Max(1, count);
    }

    private void ConsumeAmmoForLockedTargets(int targetCount)
    {
        if (_ctx.WeaponComp == null || !_ctx.WeaponComp.HasAmmunition)
            return;

        for (int i = 0; i < targetCount; i++)
        {
            if (!_ctx.WeaponComp.TryConsumeAmmo(1))
                return;
        }
    }

    private static WeaponData CreateWeaponSnapshot(Weapon activeWeapon)
    {
        return new WeaponData(
            activeWeapon.Type,
            activeWeapon.Atk,
            activeWeapon.Interval,
            activeWeapon.Range,
            activeWeapon.ProjectileSpeed,
            activeWeapon.WindUp,
            activeWeapon.WindDown,
            activeWeapon.SplashRadius,
            activeWeapon.SplitAngle,
            activeWeapon.SplitDist,
            activeWeapon.ProjectileCount,
            activeWeapon.AmmunitionCapacity,
            Array.Empty<Fix64>());
    }

    private void EnterState(AtkState newState)
    {
        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx.CharacterKey}] 状态 {State} → {newState}");
        State = newState;

        // 通过参数驱动 Animator Controller
        if (_animator != null && newState == AtkState.WindUp)
        {
            _animator.SetTrigger("Attack");
        }

        if (newState == AtkState.WindUp && !WeaponTargetRules.IsProjectileLikeWeapon(GetActiveWeapon().Type))
        {
            float trailDuration = Mathf.Max(0.08f, (float)GetCurrentWindUp() + 0.08f);
            WeaponAttackTrailEffect.Play(_ctx, trailDuration);
        }
    }

    public void InterruptAttack(AttackInterruptReason reason = AttackInterruptReason.Forced)
    {
        bool wasAttacking = IsAttacking;
        IEntityContext interruptedTarget = _lockedTarget;

        if (_ctx != null)
            ReleaseMovementLock();

        if (wasAttacking)
            NotifyAttackInterrupted(reason, interruptedTarget);

        WeaponAttackTrailEffect.Stop(_ctx, true);
        ResetPendingAttackAnimationTrigger();

        _movementLockedByThisAttack = false;
        State = AtkState.Idle;
        ResetSchedule();
        _lockedTarget = null;
        _lockedTargets.Clear();

        GameDebugSettings.Log(DebugCategory.Attack,
            $"[{_ctx?.CharacterKey}] InterruptAttack reason={reason}");
    }

    public void ShutDown()
    {
        InterruptAttack(AttackInterruptReason.CapabilityLocked);
    }

    protected virtual bool ShouldLockMoveDuringAttack()
    {
        return true;
    }

    private void NotifyAttackStarted(IEntityContext target)
    {
        foreach (BuffCallback module in GetBuffModuleSnapshot())
            module.OnAttackStarted(target);
    }

    private void NotifyAttackCompleted(IEntityContext target)
    {
        foreach (BuffCallback module in GetBuffModuleSnapshot())
            module.OnAttackCompleted(target);
    }

    private void NotifyAttackInterrupted(AttackInterruptReason reason, IEntityContext target)
    {
        foreach (BuffCallback module in GetBuffModuleSnapshot())
            module.OnAttackInterrupted(reason, target);
    }

    private List<BuffCallback> GetBuffModuleSnapshot()
    {
        if (!(_ctx is MAEntity entity) || !(entity.BuffComp is CharacterBuffComp buffComp))
            return EmptyBuffModuleSnapshot;

        var result = new List<BuffCallback>();
        foreach (BuffCallback module in buffComp.EnumerateAllModules())
            result.Add(module);

        return result;
    }

    private void ResetPendingAttackAnimationTrigger()
    {
        if (_animator == null)
            return;

        AnimatorControllerParameter[] parameters = _animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger && parameters[i].name == "Attack")
            {
                _animator.ResetTrigger("Attack");
                return;
            }
        }
    }

    public void Resume() { }
}
