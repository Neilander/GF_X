using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AAAGame.MiniMap.FOG3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;

[InitializeOnLoad]
public static class StageCheckpointRestoreGateRunner
{
    private const string LaunchScenePath = "Assets/AAAGame/Scene/Launch.unity";
    private const string ResultRelativePath = "Logs/StageCheckpointRestoreGateResult.txt";
    private const string Prefix = "Avenge.StageCheckpointRestoreGate.";
    private const string RunningKey = Prefix + "Running";
    private const string StateKey = Prefix + "State";
    private const string StartedKey = Prefix + "Started";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    private enum RunnerState
    {
        WaitingForPlay = 0,
        WaitingForStartup = 1,
        WaitingForFirstStage = 2,
        WaitingForSecondStage = 3,
        WaitingForRestore = 4,
        Finishing = 5,
    }

    static StageCheckpointRestoreGateRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Logic Frames/Run Stage Checkpoint Restore Gate")]
    public static void Run()
    {
        if (SessionState.GetBool(RunningKey, false))
            throw new InvalidOperationException("Stage checkpoint restore gate is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play mode and wait for compilation before running the restore gate.");
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save or discard the active scene changes before running the restore gate.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LaunchScenePath) == null)
            throw new FileNotFoundException("Launch scene is missing.", LaunchScenePath);

        SessionState.SetBool(RunningKey, true);
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForPlay);
        SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        WriteResult("RESULT=RUNNING" + Environment.NewLine);
        EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        try
        {
            ValidateTimeout();
            RunnerState state = (RunnerState)SessionState.GetInt(StateKey, 0);
            if (!EditorApplication.isPlaying)
            {
                if (state == RunnerState.Finishing)
                {
                    SessionState.SetBool(RunningKey, false);
                    SessionState.EraseInt(StateKey);
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException($"Restore gate left Play mode unexpectedly. state={state}.");
                }
                return;
            }

            switch (state)
            {
                case RunnerState.WaitingForPlay:
                    SessionState.SetInt(StateKey, (int)RunnerState.WaitingForStartup);
                    break;
                case RunnerState.WaitingForStartup:
                    EnterLevelFromLaunch();
                    break;
                case RunnerState.WaitingForFirstStage:
                    RequestNextStage();
                    break;
                case RunnerState.WaitingForSecondStage:
                    RequestRestore();
                    break;
                case RunnerState.WaitingForRestore:
                    ValidateRestoredWorld();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void EnterLevelFromLaunch()
    {
        if (GF.Procedure?.CurrentProcedure == null)
            return;
        if (GF.Procedure.CurrentProcedure is RuntimeProcedureBase)
        {
            SessionState.SetInt(StateKey, (int)RunnerState.WaitingForFirstStage);
            return;
        }
        if (GF.Procedure.CurrentProcedure is not StartupLevelSelectProcedure)
            return;
        if (!StartupLevelSelectProcedure.TryEnterLevel("Lv_2", out string error))
            throw new InvalidOperationException($"Cannot enter Lv_2 from Launch: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForFirstStage);
    }

    private static void RequestNextStage()
    {
        if (GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtime
            || !runtime.IsEditorStressRuntimeReady
            || LogicFrameRuntime.CurrentFrame < 3
            || !StageCheckpointRuntimeCoordinator.IsActive
            || StageCheckpointService.History.Count < 1)
            return;

        StageCheckpoint selected = StageCheckpointService.GetRequired(1);
        if (selected.Buildings.Count == 0 || selected.Strongholds.Count == 0)
            throw new InvalidOperationException("The first stage checkpoint has no persistent world data.");
        PhaseManager.SwitchToNextPhase();
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForSecondStage);
    }

    private static void RequestRestore()
    {
        if (!StageCheckpointRuntimeCoordinator.IsActive
            || StageCheckpointService.History.Count < 2
            || LogicPhaseCommandService.PendingCount != 0)
            return;
        if (!LevelSelectionService.TryRestoreStageStart(1, out string error))
            throw new InvalidOperationException($"Stage restore request failed: {error}");
        SessionState.SetInt(StateKey, (int)RunnerState.WaitingForRestore);
    }

    private static void ValidateRestoredWorld()
    {
        if (LevelSelectionService.IsLevelLoading
            || GF.Procedure?.CurrentProcedure is not RuntimeProcedureBase runtime
            || !runtime.IsEditorStressRuntimeReady
            || LogicFrameRuntime.CurrentFrame < 3
            || !StageCheckpointRuntimeCoordinator.IsActive
            || StageCheckpointRuntimeCoordinator.HasPendingRestore)
            return;

        if (StageCheckpointService.History.Count != 1)
            throw new InvalidOperationException($"Restored history count is {StageCheckpointService.History.Count}, expected 1.");
        StageCheckpoint expected = StageCheckpointService.GetRequired(1);
        if (PhaseManager.CurrentPhase != expected.Phase)
            throw new InvalidOperationException($"Restored phase mismatch. expected={expected.Phase}, actual={PhaseManager.CurrentPhase}.");
        InGameDataCheckpoint actualData = InGameDataModel.CaptureStageCheckpointState();
        if (actualData.ContentHash != expected.InGameData.ContentHash)
            throw new InvalidOperationException($"Restored in-game data hash mismatch. expected={expected.InGameData.ContentHash}, actual={actualData.ContentHash}.");
        ValidateBuildings(expected);
        ValidateStrongholds(expected);
        LogicPersistentIdAllocatorSnapshot allocator = LogicPersistentIdAllocator.CaptureSnapshot();
        if (allocator.LastBuildingInstanceValue != expected.PersistentIdSnapshot.LastBuildingInstanceValue)
            throw new InvalidOperationException("Restored persistent id allocator does not match the checkpoint.");

        Fog3Manager fogManager = Fog3Manager.Instance ?? GameEntry.GetComponent<Fog3Manager>();
        Fog3MapData fog = fogManager?.Controller?.MapData
                          ?? throw new InvalidOperationException("Restored Fog3 map is unavailable.");
        if (fog.CaptureExplorationCheckpoint().ContentHash != expected.FogExploration.ContentHash)
            throw new InvalidOperationException("Restored fog exploration does not match the checkpoint.");
        GameEndManager gameEnd = GameEntry.GetComponent<GameEndManager>()
                                 ?? throw new InvalidOperationException("GameEndManager is unavailable after restore.");
        if (gameEnd.IsGameEnded)
            throw new InvalidOperationException("GameEndManager remained ended after stage restore.");
        if (LogicTimeControlService.IsPaused || LogicTimeControlService.SchedulerScale != 1d)
            throw new InvalidOperationException("Logic time control did not resume normally after stage restore.");

        Pass(expected);
    }

    private static void ValidateBuildings(StageCheckpoint expected)
    {
        var actual = new List<LogicEntityState>();
        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] is LogicEntityState state && state.IsBuildingEntity)
                actual.Add(state);
        }
        if (actual.Count != expected.Buildings.Count)
            throw new InvalidOperationException($"Restored building count mismatch. expected={expected.Buildings.Count}, actual={actual.Count}.");
        actual.Sort((left, right) => string.CompareOrdinal(left.BuildingInstanceId, right.BuildingInstanceId));
        for (int i = 0; i < actual.Count; i++)
        {
            LogicEntityState state = actual[i];
            StageBuildingCheckpoint checkpoint = expected.Buildings[i];
            if (!string.Equals(state.BuildingInstanceId, checkpoint.BuildingInstanceId, StringComparison.Ordinal)
                || !string.Equals(state.BuildingData.Identifier, checkpoint.BuildingIdentifier, StringComparison.Ordinal)
                || state.PositionFixed != checkpoint.Position
                || state.ForwardFixed != checkpoint.Forward
                || state.OwnerFactionId != checkpoint.OwnerFactionId
                || state.IsGameEndConditionBuilding != checkpoint.IsGameEndConditionBuilding
                || state.IsNavigationStaticBaked != checkpoint.IsNavigationStaticBaked)
                throw new InvalidOperationException($"Restored building identity mismatch. index={i}, instance='{checkpoint.BuildingInstanceId}'.");
            BuildingExtraProps props = state.ProductionProps;
            if (props.ArmyForce != checkpoint.ArmyForce
                || props.Production != checkpoint.Production
                || props.DynamicProduction != checkpoint.DynamicProduction
                || props.ProductionCap != checkpoint.ProductionCap
                || props.StoredProduction != checkpoint.StoredProduction
                || props.ProductionTraitFirstDay != checkpoint.ProductionTraitFirstDay
                || props.ConditionCount != checkpoint.ConditionCount
                || props.ProductionType != checkpoint.ProductionType)
                throw new InvalidOperationException($"Restored building persistent properties mismatch. instance='{checkpoint.BuildingInstanceId}'.");
        }
    }

    private static void ValidateStrongholds(StageCheckpoint expected)
    {
        if (LogicStrongholdMap.GetStrongholdIdsOrdered().Count != expected.Strongholds.Count)
            throw new InvalidOperationException("Restored stronghold count does not match the checkpoint.");
        for (int i = 0; i < expected.Strongholds.Count; i++)
        {
            StageStrongholdCheckpoint checkpoint = expected.Strongholds[i];
            int owner = LogicStrongholdMap.GetOwnerFactionIdRequired(checkpoint.StrongholdId);
            if (owner != checkpoint.OwnerFactionId)
                throw new InvalidOperationException($"Restored stronghold owner mismatch. id='{checkpoint.StrongholdId}'.");
        }
    }

    private static void Pass(StageCheckpoint checkpoint)
    {
        WriteResult(
            "RESULT=PASS" + Environment.NewLine
            + "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine
            + "level=" + checkpoint.LevelId + Environment.NewLine
            + "epoch=" + checkpoint.PhaseEpoch + Environment.NewLine
            + "phase=" + checkpoint.Phase + Environment.NewLine
            + "buildings=" + checkpoint.Buildings.Count + Environment.NewLine
            + "strongholds=" + checkpoint.Strongholds.Count + Environment.NewLine
            + "hash=" + checkpoint.ContentHash + Environment.NewLine);
        Debug.Log($"AVENGE_STAGE_CHECKPOINT_RESTORE_PASS level={checkpoint.LevelId} epoch={checkpoint.PhaseEpoch} phase={checkpoint.Phase} hash={checkpoint.ContentHash}");
        Finish();
    }

    private static void Fail(Exception exception)
    {
        WriteResult(
            "RESULT=FAIL" + Environment.NewLine
            + "finishedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine
            + exception + Environment.NewLine);
        Debug.LogError("AVENGE_STAGE_CHECKPOINT_RESTORE_FAIL " + exception);
        Finish();
    }

    private static void Finish()
    {
        SessionState.SetInt(StateKey, (int)RunnerState.Finishing);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    private static void ValidateTimeout()
    {
        string text = SessionState.GetString(StartedKey, string.Empty);
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime started))
            throw new InvalidOperationException("Restore gate start time is invalid.");
        if (DateTime.UtcNow - started > Timeout)
            throw new TimeoutException("Stage checkpoint restore gate timed out.");
    }

    private static void WriteResult(string text)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ResultRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Restore gate result directory is invalid."));
        File.WriteAllText(path, text);
    }
}
