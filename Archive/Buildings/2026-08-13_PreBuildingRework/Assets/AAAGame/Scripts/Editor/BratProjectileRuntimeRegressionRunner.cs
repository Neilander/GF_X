using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
internal static class BratProjectileRuntimeRegressionRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string LevelIdentifier = "LvTest";
    private const string ResultRelativePath = "Logs/BratProjectileRuntimeRegressionResult.txt";
    private const string SessionPrefix = "Avenge.BratProjectileRuntimeRegression.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StateKey = SessionPrefix + "State";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const double StateTimeoutSeconds = 20.0;
    private static Fix64 NearbyRadius => DistanceUnitConverter.ConvertToWorld((Fix64)225);
    private static Fix64 NearbySurfaceDistance => NearbyRadius - Fix64.FromRaw(2048);
    private static readonly TestCapability TargetAttackLocker = new TestCapability();
    private static readonly TestCapability AttackerSetupLocker = new TestCapability();
    private static readonly FieldInfo ProjectileIdField = typeof(Projectile).GetField(
        "_logicProjectileId",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Projectile).FullName, "_logicProjectileId");
    private static readonly FieldInfo CapabilityLockersField = typeof(LogicEntityState).GetField(
        "m_CapabilityLockers",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(LogicEntityState).FullName, "m_CapabilityLockers");

    private static StringBuilder s_Report;
    private static double s_Deadline;
    private static LogicEntityState s_Attacker;
    private static LogicEntityState s_Target;
    private static LogicEntityState s_NearbyTrigger;
    private static DirectAtkComp s_Attack;
    private static Fix64 s_InitialHealth;
    private static ulong s_ObservedProjectileId;
    private static ulong s_TransitionFrame;
    private static ulong s_LastDiagnosticFrame;
    private static int s_CleanupAttackerId;
    private static int s_CleanupTargetId;
    private static bool s_ProjectileViewObserved;

    private enum RunnerState
    {
        WaitingForPlay,
        WaitingForStartupProcedure,
        WaitingForRuntime,
        WaitingForWindUpReady,
        WaitingForWindUp,
        WaitingForWindUpLock,
        WaitingForWindUpStability,
        WaitingForWindUpCleanup,
        WaitingForProjectileReady,
        WaitingForProjectileLaunch,
        WaitingForProjectileLock,
        WaitingForProjectileDamage,
        Finishing,
    }

    static BratProjectileRuntimeRegressionRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Diagnostics/Run Brat Projectile Runtime Regression")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("A Brat projectile runtime regression is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting the Brat projectile runtime regression.");
        if (EditorApplication.isCompiling)
            throw new InvalidOperationException("Wait for script compilation before starting the Brat projectile runtime regression.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LaunchScenePath) == null)
            throw new FileNotFoundException("Launch scene is missing.", LaunchScenePath);

        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (scene.isDirty)
                throw new InvalidOperationException($"Cannot run regression while scene '{scene.path}' has unsaved changes.");
        }

        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedUtcKey, startedUtc);
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine
            + "startedUtc=" + startedUtc + Environment.NewLine
            + "launchScene=" + LaunchScenePath + Environment.NewLine
            + "level=" + LevelIdentifier + Environment.NewLine);

        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, (int)RunnerState.WaitingForPlay);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SetState(RunnerState.WaitingForStartupProcedure);
                    break;
                case RunnerState.WaitingForStartupProcedure:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForRuntime:
                    StartWindUpScenarioWhenReady();
                    break;
                case RunnerState.WaitingForWindUpReady:
                    StartScenarioAttackWhenReady(RunnerState.WaitingForWindUp);
                    break;
                case RunnerState.WaitingForWindUp:
                    ObserveWindUp();
                    break;
                case RunnerState.WaitingForWindUpLock:
                    ObserveWindUpLock();
                    break;
                case RunnerState.WaitingForWindUpStability:
                    ObserveWindUpStability();
                    break;
                case RunnerState.WaitingForWindUpCleanup:
                    ObserveWindUpCleanup();
                    break;
                case RunnerState.WaitingForProjectileReady:
                    StartScenarioAttackWhenReady(RunnerState.WaitingForProjectileLaunch);
                    break;
                case RunnerState.WaitingForProjectileLaunch:
                    ObserveProjectileLaunch();
                    break;
                case RunnerState.WaitingForProjectileLock:
                    ObserveProjectileLock();
                    break;
                case RunnerState.WaitingForProjectileDamage:
                    ObserveProjectileDamage();
                    break;
                case RunnerState.Finishing:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown Brat regression state.");
            }

            if (state != RunnerState.Finishing && EditorApplication.timeSinceStartup >= s_Deadline)
                throw new TimeoutException($"Brat runtime regression timed out in state {state}. {DescribeCurrentScenario()}");
        }
        catch (Exception exception)
        {
            WriteResult(
                (s_Report?.ToString() ?? string.Empty)
                + "RESULT=FAIL" + Environment.NewLine
                + "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine
                + exception + Environment.NewLine);
            Debug.LogException(exception);
            SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
            EditorApplication.isPlaying = false;
        }
    }

    private static void EnterLevelFromLaunch()
    {
        if (GF.Procedure?.CurrentProcedure == null)
            return;
        if (GF.Procedure.CurrentProcedure is not RuntimeProcedureBase)
            return;

        if (!EditorRuntimeLevelEntry.TryEnterWithDefaultCareer(LevelIdentifier, out string errorMessage))
            throw new InvalidOperationException($"Cannot enter {LevelIdentifier} from Launch: {errorMessage}");
        SetState(RunnerState.WaitingForRuntime);
    }

    private static void StartWindUpScenarioWhenReady()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtimeProcedure)
            return;
        if (!runtimeProcedure.IsEditorStressRuntimeReady || LogicFrameRuntime.CurrentFrame < 2)
            return;

        InGameDataModel.SetPhase(GamePhase.Defend, false);
        if ((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase) != GamePhase.Defend)
            throw new InvalidOperationException("Brat runtime regression failed to enter Defend phase.");

        s_Report = new StringBuilder(4096);
        s_Report.AppendLine("RESULT=RUNNING");
        s_Report.Append("startedUtc=").AppendLine(SessionState.GetString(StartedUtcKey, string.Empty));
        s_Report.Append("runtimeReadyFrame=").AppendLine(LogicFrameRuntime.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        SpawnScenario(new FixVector2((Fix64)1000, (Fix64)1000), "windup");
        SetState(RunnerState.WaitingForWindUpReady);
    }

    private static void SpawnScenario(FixVector2 attackerPosition, string name)
    {
        bool projectileScenario = string.Equals(name, "projectile", StringComparison.Ordinal);
        Fix64 attackRange = DistanceUnitConverter.ConvertToWorld((Fix64)650);
        Fix64 targetOffset = projectileScenario
            ? (NearbyRadius + attackRange) / Fix64.FromRaw(8192) + Fix64.FromRaw(2048)
            : NearbyRadius + Fix64.FromRaw(10240);
        FixVector2 targetPosition = attackerPosition + new FixVector2(targetOffset, Fix64.Zero);
        LogicEntityId attackerId = SoldierFactory.ShowSoldierFixed(
            UnitType.Unit_Brat,
            attackerPosition,
            0f,
            SideType.PlayerSide,
            BrainType.SoldierAI);
        LogicEntityId targetId = SoldierFactory.ShowSoldierFixed(
            UnitType.Unit_HiredBodyguard,
            targetPosition,
            0f,
            SideType.EnemySide,
            BrainType.Player);

        LogicEntityId nearbyTriggerId = default;
        if (projectileScenario)
        {
            FixVector2 nearbyTriggerPosition = attackerPosition
                                               + new FixVector2(Fix64.Zero, NearbyRadius + Fix64.FromRaw(10240));
            nearbyTriggerId = SoldierFactory.ShowSoldierFixed(
                UnitType.Unit_HiredBodyguard,
                nearbyTriggerPosition,
                0f,
                SideType.EnemySide,
                BrainType.Player);
        }

        s_Attacker = LogicEntityStateStore.GetRequired(attackerId);
        s_Target = LogicEntityStateStore.GetRequired(targetId);
        s_NearbyTrigger = nearbyTriggerId.IsValid ? LogicEntityStateStore.GetRequired(nearbyTriggerId) : null;
        s_Attack = s_Attacker.AtkComp as DirectAtkComp
                   ?? throw new InvalidOperationException("Spawned Brat has no DirectAtkComp.");
        s_Attacker.MoveExecutor.SetNavigationConstrained(false);
        s_Target.MoveExecutor.SetNavigationConstrained(false);
        s_Target.TauntLevel = projectileScenario ? 2 : 1;
        s_Attacker.LockComp(s_Attacker.AtkComp, AttackerSetupLocker);
        s_Target.LockComp(s_Target.AtkComp, TargetAttackLocker);
        if (s_NearbyTrigger != null)
        {
            s_NearbyTrigger.MoveExecutor.SetNavigationConstrained(false);
            s_NearbyTrigger.TauntLevel = 1;
            s_NearbyTrigger.LockComp(s_NearbyTrigger.AtkComp, TargetAttackLocker);
        }
        s_InitialHealth = s_Target.HealthValue;
        s_ObservedProjectileId = 0;
        s_LastDiagnosticFrame = 0;
        s_ProjectileViewObserved = false;
        s_Report.Append("spawn scenario=").Append(name)
            .Append(" frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" attacker=").Append(attackerId.Value)
            .Append(" target=").Append(targetId.Value)
            .Append(" nearbyTrigger=").Append(nearbyTriggerId.Value)
            .Append(" targetHpRaw=").Append(s_InitialHealth.RawValue)
            .AppendLine();
        WriteResult(s_Report.ToString());
    }

    private static void StartScenarioAttackWhenReady(RunnerState nextState)
    {
        if (!s_Attacker.IsSpawnCommitted || !s_Target.IsSpawnCommitted)
            return;
        if (s_NearbyTrigger != null && !s_NearbyTrigger.IsSpawnCommitted)
            return;
        if (!LogicEntityLifecycleService.TryGetBoundView(s_Attacker.LogicEntityId, out MAEntity attackerView)
            || attackerView == null)
        {
            return;
        }
        if (!LogicEntityLifecycleService.TryGetBoundView(s_Target.LogicEntityId, out MAEntity targetView)
            || targetView == null)
        {
            return;
        }
        if (s_NearbyTrigger != null
            && (!LogicEntityLifecycleService.TryGetBoundView(s_NearbyTrigger.LogicEntityId, out MAEntity nearbyView)
                || nearbyView == null))
        {
            return;
        }
        if (s_Attack.WeaponSO is not RangedWeaponSO)
            return;

        Fix64 distance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(s_Target);
        if (distance <= NearbyRadius || distance > DistanceUnitConverter.ConvertToWorld((Fix64)650))
        {
            throw new InvalidOperationException(
                $"Scenario start distance is invalid. raw={distance.RawValue}, nearbyRaw={NearbyRadius.RawValue}.");
        }
        Fix64 approachingDistance = s_NearbyTrigger != null
            ? s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(s_NearbyTrigger)
            : distance;
        if (approachingDistance <= NearbyRadius)
        {
            throw new InvalidOperationException(
                $"Approaching entity started inside nearby range. raw={approachingDistance.RawValue}, nearbyRaw={NearbyRadius.RawValue}.");
        }

        s_Attacker.TargetComp.CurrentTarget = s_Target;
        s_Attacker.ResumeComp(s_Attacker.AtkComp, AttackerSetupLocker);
        s_Report.Append("ready frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" surfaceRaw=").Append(distance.RawValue)
            .Append(" approachingSurfaceRaw=").Append(approachingDistance.RawValue)
            .Append(" weaponSO=").Append(s_Attack.WeaponSO.GetType().Name)
            .AppendLine();
        WriteResult(s_Report.ToString());
        SetState(nextState);
    }

    private static void ObserveWindUp()
    {
        if (s_Attack.State != DirectAtkComp.AtkState.WindUp)
            return;
        if (s_Attack.AttackCount != 1)
            throw new InvalidOperationException($"Wind-up scenario started {s_Attack.AttackCount} attacks.");
        if (TryGetOwnedProjectileId(s_Attacker.LogicEntityId, out _))
            throw new InvalidOperationException("Wind-up scenario already submitted a projectile.");

        RequestMoveInsideNearbyRadius();
        s_Report.Append("windupObserved frame=").Append(LogicFrameRuntime.CurrentFrame).AppendLine();
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForWindUpLock);
    }

    private static void ObserveWindUpLock()
    {
        if (s_Attacker.CanRun(s_Attacker.AtkComp))
            return;
        Fix64 distance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(s_Target);
        if (distance > NearbyRadius)
            throw new InvalidOperationException($"Wind-up attack locked before the target entered nearby range. raw={distance.RawValue}.");
        if (s_Attack.State != DirectAtkComp.AtkState.Idle)
            throw new InvalidOperationException($"Wind-up attack was not interrupted to Idle. state={s_Attack.State}.");
        if (TryGetOwnedProjectileId(s_Attacker.LogicEntityId, out _))
            throw new InvalidOperationException("Interrupted wind-up submitted a projectile.");

        s_TransitionFrame = LogicFrameRuntime.CurrentFrame;
        s_Report.Append("windupLocked frame=").Append(s_TransitionFrame)
            .Append(" surfaceRaw=").Append(distance.RawValue)
            .AppendLine();
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForWindUpStability);
    }

    private static void ObserveWindUpStability()
    {
        if (LogicFrameRuntime.CurrentFrame < s_TransitionFrame + 30)
            return;
        if (TryGetOwnedProjectileId(s_Attacker.LogicEntityId, out _))
            throw new InvalidOperationException("Interrupted wind-up emitted a delayed projectile.");
        if (s_Target.HealthValue != s_InitialHealth)
            throw new InvalidOperationException("Interrupted wind-up changed target health.");

        s_Report.Append("windupPass frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" targetHpRaw=").Append(s_Target.HealthValue.RawValue)
            .AppendLine();
        s_CleanupAttackerId = s_Attacker.LogicEntityId.Value;
        s_CleanupTargetId = s_Target.LogicEntityId.Value;
        RequestDespawn(s_Attacker);
        RequestDespawn(s_Target);
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForWindUpCleanup);
    }

    private static void ObserveWindUpCleanup()
    {
        var entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            int id = entities[i]?.LogicEntityId.Value ?? 0;
            if (id == s_CleanupAttackerId || id == s_CleanupTargetId)
                return;
        }

        s_Report.Append("windupCleanup frame=").Append(LogicFrameRuntime.CurrentFrame).AppendLine();
        SpawnScenario(new FixVector2((Fix64)1000, (Fix64)1000), "projectile");
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForProjectileReady);
    }

    private static void ObserveProjectileLaunch()
    {
        if (!TryGetOwnedProjectileId(s_Attacker.LogicEntityId, out ulong projectileId))
        {
            AppendWaitingDiagnostic("waitingForProjectile");
            return;
        }
        if (s_Attack.AttackCount != 1)
            throw new InvalidOperationException($"Projectile scenario started {s_Attack.AttackCount} attacks before launch capture.");

        s_ObservedProjectileId = projectileId;
        s_ProjectileViewObserved = HasProjectileView(projectileId);
        RequestMoveInsideNearbyRadius();
        s_Report.Append("projectileSubmitted frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" projectile=").Append(projectileId)
            .Append(" viewObserved=").Append(s_ProjectileViewObserved)
            .AppendLine();
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForProjectileLock);
    }

    private static void ObserveProjectileLock()
    {
        s_ProjectileViewObserved |= HasProjectileView(s_ObservedProjectileId);
        if (s_Attacker.CanRun(s_Attacker.AtkComp))
            return;
        LogicEntityState approachingEntity = s_NearbyTrigger ?? s_Target;
        Fix64 distance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(approachingEntity);
        if (distance > NearbyRadius)
            throw new InvalidOperationException($"Projectile attack locked before nearby entry. raw={distance.RawValue}.");
        if (!IsProjectileActive(s_ObservedProjectileId))
            throw new InvalidOperationException("Projectile completed before nearby attack lock was observed.");

        s_Report.Append("projectileLocked frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" projectileActive=true")
            .Append(" surfaceRaw=").Append(distance.RawValue)
            .Append(" attackState=").Append(s_Attack.State)
            .AppendLine();
        WriteResult(s_Report.ToString());
        SetState(RunnerState.WaitingForProjectileDamage);
    }

    private static void ObserveProjectileDamage()
    {
        s_ProjectileViewObserved |= HasProjectileView(s_ObservedProjectileId);
        if (s_Target.HealthValue < s_InitialHealth)
        {
            if (!s_ProjectileViewObserved)
                throw new InvalidOperationException("Logic damage applied without observing the real Projectile view.");
            if (s_Attacker.CanRun(s_Attacker.AtkComp))
                throw new InvalidOperationException("Brat attack lock was released before projectile damage.");

            Fix64 damage = s_InitialHealth - s_Target.HealthValue;
            s_Report.Append("projectileDamage frame=").Append(LogicFrameRuntime.CurrentFrame)
                .Append(" projectile=").Append(s_ObservedProjectileId)
                .Append(" viewObserved=true")
                .Append(" damageRaw=").Append(damage.RawValue)
                .Append(" targetHpRaw=").Append(s_Target.HealthValue.RawValue)
                .AppendLine();
            s_Report.Append("finishedUtc=").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            s_Report.Replace("RESULT=RUNNING", "RESULT=PASS");
            WriteResult(s_Report.ToString());
            Debug.Log("AVENGE_BRAT_PROJECTILE_RUNTIME_REGRESSION_PASS\n" + s_Report);
            SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
            EditorApplication.isPlaying = false;
            return;
        }

        if (!IsProjectileActive(s_ObservedProjectileId))
            throw new InvalidOperationException("The launched Brat projectile completed without changing target health.");
    }

    private static void RequestMoveInsideNearbyRadius()
    {
        LogicEntityState approachingEntity = s_NearbyTrigger ?? s_Target;
        Fix64 currentDistance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(approachingEntity);
        if (currentDistance <= NearbyRadius)
            throw new InvalidOperationException($"Target was already nearby. raw={currentDistance.RawValue}.");
        Fix64 displacement = currentDistance - NearbySurfaceDistance;
        FixVector2 towardAttacker = (s_Attacker.Position - approachingEntity.Position).GetNormalized();
        approachingEntity.MoveExecutor.SetExternalFixed(
            towardAttacker * (displacement / LogicFrameRuntime.FixedDeltaTime));
    }

    private static bool TryGetOwnedProjectileId(LogicEntityId attackerId, out ulong projectileId)
    {
        var states = LogicProjectileService.CaptureActiveStates();
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].AttackerId != attackerId)
                continue;
            projectileId = states[i].Id;
            return true;
        }

        projectileId = 0;
        return false;
    }

    private static bool IsProjectileActive(ulong projectileId)
    {
        var states = LogicProjectileService.CaptureActiveStates();
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].Id == projectileId)
                return true;
        }
        return false;
    }

    private static bool HasProjectileView(ulong projectileId)
    {
        var group = GF.Entity.GetEntityGroup(Const.EntityGroup.Bullet.ToString());
        if (group == null)
            throw new InvalidOperationException("Bullet entity group is missing.");
        var entities = group.GetAllEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] is not Entity entity || entity.Logic is not Projectile projectile)
                continue;
            if ((ulong)ProjectileIdField.GetValue(projectile) == projectileId)
                return true;
        }
        return false;
    }

    private static void RequestDespawn(LogicEntityState state)
    {
        if (!LogicEntityLifecycleService.TryGetBoundView(state.LogicEntityId, out MAEntity view) || view == null)
            throw new InvalidOperationException($"Cannot despawn unbound test entity {state.LogicEntityId.Value}.");
        view.RequestDespawn();
    }

    private static void AppendWaitingDiagnostic(string label)
    {
        ulong frame = LogicFrameRuntime.CurrentFrame;
        if (s_LastDiagnosticFrame != 0 && frame < s_LastDiagnosticFrame + 30)
            return;
        s_LastDiagnosticFrame = frame;
        s_Report.Append(label).Append(' ').AppendLine(DescribeCurrentScenario());
        WriteResult(s_Report.ToString());
    }

    private static string DescribeCurrentScenario()
    {
        if (s_Attacker == null || s_Target == null || s_Attack == null)
            return "scenario=not-created";

        IEntityContext currentTarget = s_Attacker.TargetComp?.CurrentTarget;
        Fix64 distance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(s_Target);
        return new StringBuilder(384)
            .Append("frame=").Append(LogicFrameRuntime.CurrentFrame)
            .Append(" phase=").Append((GamePhase)InGameDataModel.GetValue(IngameValueType.Phase))
            .Append(" attackerAlive=").Append(s_Attacker.Alive)
            .Append(" targetAlive=").Append(s_Target.Alive)
            .Append(" targetable=").Append(s_Target.IsAttackTargetable())
            .Append(" currentTarget=").Append(currentTarget?.LogicEntityId.Value ?? 0)
            .Append(" expectedTarget=").Append(s_Target.LogicEntityId.Value)
            .Append(" canRun=").Append(s_Attacker.CanRun(s_Attacker.AtkComp))
            .Append(" attackState=").Append(s_Attack.State)
            .Append(" attackCount=").Append(s_Attack.AttackCount)
            .Append(" brainAttack=").Append(s_Attacker.Brain?.Attack ?? false)
            .Append(" surfaceRaw=").Append(distance.RawValue)
            .Append(" lockers=").Append(DescribeAttackLockers())
            .Append(" nearbyEnemies=").Append(DescribeNearbyEnemies())
            .ToString();
    }

    private static string DescribeAttackLockers()
    {
        var capabilityLockers = CapabilityLockersField.GetValue(s_Attacker)
            as Dictionary<ICapability, List<ICapability>>
            ?? throw new InvalidOperationException("LogicEntityState capability locker dictionary is missing.");
        if (!capabilityLockers.TryGetValue(s_Attacker.AtkComp, out List<ICapability> lockers))
            return "<none>";

        var result = new StringBuilder(128);
        for (int i = 0; i < lockers.Count; i++)
        {
            if (i > 0)
                result.Append(',');
            result.Append(lockers[i]?.GetType().Name ?? "<null>");
        }
        return result.ToString();
    }

    private static string DescribeNearbyEnemies()
    {
        var result = new StringBuilder(256);
        var entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext candidate = entities[i];
            if (candidate == null || ReferenceEquals(candidate, s_Attacker))
                continue;
            if (!candidate.IsAttackTargetable() || !EntityCombatTeamHelper.IsEnemy(s_Attacker, candidate))
                continue;

            Fix64 distance = s_Attacker.LogicFrameDistanceToTargetSurfaceFixed(candidate);
            if (distance > NearbyRadius + (Fix64)2)
                continue;
            if (result.Length > 0)
                result.Append(',');
            result.Append(candidate.LogicEntityId.Value)
                .Append(':').Append(candidate.CharacterKey)
                .Append(':').Append(distance.RawValue);
        }
        return result.Length == 0 ? "<none>" : result.ToString();
    }

    private static void SetState(RunnerState state)
    {
        SessionState.SetInt(StateKey, (int)state);
        s_Deadline = EditorApplication.timeSinceStartup + StateTimeoutSeconds;
    }

    private static void WriteResult(string content)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ResultRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private sealed class TestCapability : ICapability
    {
        public void ShutDown() { }
        public void Resume() { }
    }
}
