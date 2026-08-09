using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SystemValidationRunner
{
    private const string SessionPrefix = "Avenge.SystemValidation.";
    private const string RunningKey = SessionPrefix + "Running";
    private const string StartedUtcKey = SessionPrefix + "StartedUtc";
    private const string ScenarioReportKey = SessionPrefix + "ScenarioReport";
    private const string ResultRelativePath = "Logs/SystemValidationResult.txt";
    private const string GateResultRelativePath = "Logs/LogicRuntimeLongSessionGateResult.txt";
    private const int TechInjectionTick = 30;
    private const int DisplacementInjectionTick = 60;
    private const int DisplacementVerificationTick = 90;

    private static readonly string[] s_TechCandidates =
    {
        "Tech_Buil_NavStation_Opt1",
        "Tech_Buil_NavStation_Opt2",
        "Tech_Buil_NavStation_Opt3",
        "Tech_Buil_NavStation_Opt4",
    };

    private static bool s_RuntimeInitialized;
    private static bool s_Subscribed;
    private static int s_UnitViewCount;
    private static int s_BuildingViewCount;
    private static int s_PlaceholderViewCount;
    private static int s_VisibilityHiddenViewCount;
    private static int s_TrailEffectCount;
    private static int s_ProjectileLogicPeak;
    private static int s_ProjectileViewPeak;
    private static int s_InitialTechAppliedCount;
    private static string s_TechId;
    private static string s_TechSourceBuildingId;
    private static bool s_TechStackable;
    private static bool s_TechInjected;
    private static bool s_TechApplied;
    private static LogicEntityId s_DisplacementTargetId;
    private static FixVector2 s_DisplacementStartPosition;
    private static bool s_DisplacementInjected;
    private static bool s_DisplacementMoved;
    private static LogicGameplayStateDigest s_StartDigest;
    private static LogicGameplayStateDigest s_TechDigest;
    private static LogicGameplayStateDigest s_DisplacementDigest;
    private static LogicGameplayStateDigest s_FinalDigest;

    static SystemValidationRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/AAAGame/Validation/Run Launch System Validation")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("A system validation run is already active.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before starting system validation.");

        string startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        SessionState.SetBool(RunningKey, true);
        SessionState.SetString(StartedUtcKey, startedUtc);
        SessionState.SetString(ScenarioReportKey, string.Empty);
        ResetRuntimeState();
        WriteResult(
            "RESULT=RUNNING" + Environment.NewLine
            + "startedUtc=" + startedUtc + Environment.NewLine
            + "launchScene=Assets/AAAGame/Scene/Launch.unity" + Environment.NewLine
            + "level=Lv_2" + Environment.NewLine);

        try
        {
            LogicRuntimeLongSessionGateRunner.RunSystemValidationGate();
        }
        catch
        {
            SessionState.SetBool(RunningKey, false);
            throw;
        }
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        if (EditorApplication.isPlaying)
        {
            EnsureSubscribed();
            ObserveProjectileViews();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        FinalizeAfterPlayMode();
    }

    private static void EnsureSubscribed()
    {
        if (s_Subscribed)
            return;
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded -= OnAuthorityFrameRecorded;
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded += OnAuthorityFrameRecorded;
        s_Subscribed = true;
    }

    private static void OnAuthorityFrameRecorded(LogicGameplayStateDigest digest)
    {
        try
        {
            int completedTick = checked(EditorLogicRuntimeStressGate.ProcessedTicks + 1);
            if (!s_RuntimeInitialized)
                InitializeRuntimeScenario(digest);

            ObserveProjectiles();
            if (completedTick == TechInjectionTick)
                InjectTechCommand();
            if (completedTick > TechInjectionTick && !s_TechApplied)
                VerifyTechApplication(digest);
            if (completedTick == DisplacementInjectionTick)
                InjectDisplacement();
            if (completedTick >= DisplacementVerificationTick && !s_DisplacementMoved)
                VerifyDisplacement(digest);

            if (completedTick == EditorLogicRuntimeStressGate.TotalTicks)
            {
                s_FinalDigest = digest;
                ValidateRuntimeScenario();
                SessionState.SetString(ScenarioReportKey, BuildScenarioReport("PASS"));
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(ScenarioReportKey, BuildScenarioReport("FAIL") + exception + Environment.NewLine);
            EditorLogicRuntimeStressGate.Fail(exception);
        }
    }

    private static void InitializeRuntimeScenario(LogicGameplayStateDigest digest)
    {
        s_StartDigest = digest;
        s_InitialTechAppliedCount = LogicTechEffectCommandService.AppliedCount;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState state)
                throw new InvalidOperationException($"System validation found non-authority entity at index {i}.");
            if (!LogicEntityLifecycleService.TryGetBoundView(state.LogicEntityId, out MAEntity view) || view == null)
                throw new InvalidOperationException($"System validation entity has no bound view. entity={state.LogicEntityId.Value}.");

            EntityPresentationBindings bindings = view.PresentationBindings;
            if (bindings == null)
                throw new InvalidOperationException($"System validation view has no presentation binding. entity={state.LogicEntityId.Value}.");
            bindings.ValidateOrThrow(!state.IsBuildingEntity);
            if (!state.IsBuildingEntity)
                bindings.ValidateRuntimeAnimatorContractOrThrow();

            Renderer[] renderers = bindings.DisplayRoot.GetComponentsInChildren<Renderer>(true);
            bool hasEnabledRenderer = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                hasEnabledRenderer |= renderers[rendererIndex].enabled;
            if (!hasEnabledRenderer)
                s_VisibilityHiddenViewCount++;

            if (state.IsBuildingEntity)
                s_BuildingViewCount++;
            else
                s_UnitViewCount++;
            if (bindings.IsPlaceholder)
                s_PlaceholderViewCount++;

            WeaponAttackTrailEffect[] trailEffects = view.GetComponentsInChildren<WeaponAttackTrailEffect>(true);
            if (trailEffects.Length > 1)
                throw new InvalidOperationException($"System validation view has duplicate trail effects. entity={state.LogicEntityId.Value}.");
            if (trailEffects.Length == 1)
            {
                bindings.RequireTrailBinding(out _, out _);
                s_TrailEffectCount++;
            }
        }

        if (s_UnitViewCount <= 0 || s_BuildingViewCount <= 0)
            throw new InvalidOperationException($"System validation requires unit and building views. units={s_UnitViewCount}, buildings={s_BuildingViewCount}.");
        s_RuntimeInitialized = true;
    }

    private static void InjectTechCommand()
    {
        LogicEntityState sourceBuilding = FindPlayerBuilding();
        TechData selectedTech = null;
        for (int i = 0; i < s_TechCandidates.Length; i++)
        {
            TechData candidate = TechDataModel.GetTechData(s_TechCandidates[i]);
            if (candidate == null)
                continue;
            bool unlocked = candidate.IsStackable
                ? InGameDataModel.HasUnlockedTech(candidate.Identifier, sourceBuilding.BuildingInstanceId)
                : InGameDataModel.HasUnlockedTech(candidate.Identifier);
            if (!unlocked)
            {
                selectedTech = candidate;
                break;
            }
        }

        if (selectedTech == null)
            throw new InvalidOperationException("System validation could not find a locked NavStation tech candidate.");

        LogicTechEffectCommandService.ScheduleForNextFrame(
            selectedTech.Identifier,
            selectedTech.IsStackable,
            sourceBuilding.OwnerFactionId,
            sourceBuilding.BuildingInstanceId);
        s_TechId = selectedTech.Identifier;
        s_TechStackable = selectedTech.IsStackable;
        s_TechSourceBuildingId = sourceBuilding.BuildingInstanceId;
        s_TechInjected = true;
    }

    private static void VerifyTechApplication(LogicGameplayStateDigest digest)
    {
        if (LogicTechEffectCommandService.AppliedCount <= s_InitialTechAppliedCount)
            return;
        bool unlocked = s_TechStackable
            ? InGameDataModel.HasUnlockedTech(s_TechId, s_TechSourceBuildingId)
            : InGameDataModel.HasUnlockedTech(s_TechId);
        if (!unlocked)
            throw new InvalidOperationException($"Injected tech was applied but is not unlocked. tech={s_TechId}.");
        s_TechDigest = digest;
        s_TechApplied = true;
    }

    private static void InjectDisplacement()
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is not LogicEntityState state || state.IsBuildingEntity || !state.Alive)
                continue;
            if (!state.DurationMoveEffectComp.TryApplyKnockback(new FixVector2(Fix64.One, Fix64.Zero), (Fix64)3))
                continue;

            s_DisplacementTargetId = state.LogicEntityId;
            s_DisplacementStartPosition = state.PositionFixed;
            s_DisplacementInjected = true;
            return;
        }

        throw new InvalidOperationException("System validation found no live unit that accepts level-3 knockback.");
    }

    private static void VerifyDisplacement(LogicGameplayStateDigest digest)
    {
        if (!EntityRegistry.TryGet(s_DisplacementTargetId, out IEntityContext target) || !target.Alive)
            throw new InvalidOperationException($"Displacement target disappeared before verification. entity={s_DisplacementTargetId.Value}.");
        if (target.PositionFixed == s_DisplacementStartPosition)
            return;
        s_DisplacementDigest = digest;
        s_DisplacementMoved = true;
    }

    private static LogicEntityState FindPlayerBuilding()
    {
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is LogicEntityState state
                && state.IsBuildingEntity
                && state.OwnerFactionId == EntitySideHelper.PlayerFactionId
                && !string.IsNullOrWhiteSpace(state.BuildingInstanceId))
            {
                return state;
            }
        }
        throw new InvalidOperationException("System validation found no player building for tech injection.");
    }

    private static void ObserveProjectiles()
    {
        s_ProjectileLogicPeak = Math.Max(s_ProjectileLogicPeak, LogicProjectileService.ActiveCount);
        ObserveProjectileViews();
    }

    private static void ObserveProjectileViews()
    {
        if (!EditorApplication.isPlaying)
            return;
        int count = UnityEngine.Object.FindObjectsOfType<Projectile>(true).Length;
        s_ProjectileViewPeak = Math.Max(s_ProjectileViewPeak, count);
    }

    private static void ValidateRuntimeScenario()
    {
        if (!s_TechInjected || !s_TechApplied)
            throw new InvalidOperationException("System validation did not complete the tech command scenario.");
        if (!s_DisplacementInjected || !s_DisplacementMoved)
            throw new InvalidOperationException("System validation did not observe authoritative displacement.");
        if (s_ProjectileLogicPeak <= 0 || s_ProjectileViewPeak <= 0)
            throw new InvalidOperationException($"System validation did not observe both projectile authority and view. logicPeak={s_ProjectileLogicPeak}, viewPeak={s_ProjectileViewPeak}.");
        if (s_StartDigest.CommandsHash == s_TechDigest.CommandsHash)
            throw new InvalidOperationException("Tech injection did not change the command-state hash.");
        if (s_TechDigest.EntitiesHash == s_DisplacementDigest.EntitiesHash)
            throw new InvalidOperationException("Displacement did not change the entity-state hash.");
    }

    private static void FinalizeAfterPlayMode()
    {
        Unsubscribe();
        string scenarioReport = SessionState.GetString(ScenarioReportKey, string.Empty);
        string gatePath = GetProjectPath(GateResultRelativePath);
        string gateReport = File.Exists(gatePath) ? File.ReadAllText(gatePath) : string.Empty;
        bool passed = scenarioReport.StartsWith("SCENARIO=PASS", StringComparison.Ordinal)
                      && gateReport.StartsWith("RESULT=PASS", StringComparison.Ordinal);

        WriteResult(
            "RESULT=" + (passed ? "PASS" : "FAIL") + Environment.NewLine
            + "startedUtc=" + SessionState.GetString(StartedUtcKey, string.Empty) + Environment.NewLine
            + "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine
            + "launchScene=Assets/AAAGame/Scene/Launch.unity" + Environment.NewLine
            + "procedure=ChangeSceneProcedure->RuntimeProcedureBase" + Environment.NewLine
            + "level=Lv_2" + Environment.NewLine
            + scenarioReport
            + "LONG_SESSION_GATE" + Environment.NewLine
            + gateReport);

        Debug.Log(passed
            ? "AVENGE_SYSTEM_VALIDATION_PASS " + scenarioReport
            : "AVENGE_SYSTEM_VALIDATION_FAIL " + scenarioReport + gateReport);
        SessionState.SetBool(RunningKey, false);
        SessionState.EraseString(ScenarioReportKey);
        ResetRuntimeState();
    }

    private static string BuildScenarioReport(string result)
    {
        return
            "SCENARIO=" + result + Environment.NewLine
            + "unitViews=" + s_UnitViewCount + Environment.NewLine
            + "buildingViews=" + s_BuildingViewCount + Environment.NewLine
            + "placeholderViews=" + s_PlaceholderViewCount + Environment.NewLine
            + "visibilityHiddenViews=" + s_VisibilityHiddenViewCount + Environment.NewLine
            + "trailEffects=" + s_TrailEffectCount + Environment.NewLine
            + "techId=" + (s_TechId ?? string.Empty) + Environment.NewLine
            + "techInjected=" + s_TechInjected + Environment.NewLine
            + "techApplied=" + s_TechApplied + Environment.NewLine
            + "displacementTarget=" + s_DisplacementTargetId.Value + Environment.NewLine
            + "displacementInjected=" + s_DisplacementInjected + Environment.NewLine
            + "displacementMoved=" + s_DisplacementMoved + Environment.NewLine
            + "projectileLogicPeak=" + s_ProjectileLogicPeak + Environment.NewLine
            + "projectileViewPeak=" + s_ProjectileViewPeak + Environment.NewLine
            + "startGameplayHash=" + s_StartDigest.GameplayStateHash + Environment.NewLine
            + "techCommandsHash=" + s_TechDigest.CommandsHash + Environment.NewLine
            + "displacementEntitiesHash=" + s_DisplacementDigest.EntitiesHash + Environment.NewLine
            + "finalGameplayHash=" + s_FinalDigest.GameplayStateHash + Environment.NewLine;
    }

    private static void ResetRuntimeState()
    {
        Unsubscribe();
        s_RuntimeInitialized = false;
        s_UnitViewCount = 0;
        s_BuildingViewCount = 0;
        s_PlaceholderViewCount = 0;
        s_VisibilityHiddenViewCount = 0;
        s_TrailEffectCount = 0;
        s_ProjectileLogicPeak = 0;
        s_ProjectileViewPeak = 0;
        s_InitialTechAppliedCount = 0;
        s_TechId = null;
        s_TechSourceBuildingId = null;
        s_TechStackable = false;
        s_TechInjected = false;
        s_TechApplied = false;
        s_DisplacementTargetId = default;
        s_DisplacementStartPosition = default;
        s_DisplacementInjected = false;
        s_DisplacementMoved = false;
        s_StartDigest = default;
        s_TechDigest = default;
        s_DisplacementDigest = default;
        s_FinalDigest = default;
    }

    private static void Unsubscribe()
    {
        EditorLogicRuntimeStressGate.AuthorityFrameRecorded -= OnAuthorityFrameRecorded;
        s_Subscribed = false;
    }

    private static void WriteResult(string content)
    {
        string path = GetProjectPath(ResultRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string GetProjectPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
    }
}
