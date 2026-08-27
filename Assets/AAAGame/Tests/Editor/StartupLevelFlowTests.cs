using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class StartupLevelFlowTests
{
    [Test]
    public void GoalUI_OpensOnlyAfterLoadingPresentationCompletes()
    {
        string generalSetup = File.ReadAllText(
            Path.Combine(Application.dataPath, "AAAGame", "Scripts", "UTManagers", "GeneralSetup.cs"));
        int completionHandler = generalSetup.IndexOf("private void OnLevelLoadCompleted()", System.StringComparison.Ordinal);
        int openGoal = generalSetup.IndexOf(
            "GF.UI.OpenUIForm(UIViews.GoalUIForm);",
            System.StringComparison.Ordinal);

        Assert.That(generalSetup, Does.Contain("LevelSelectionService.LevelLoadCompleted += OnLevelLoadCompleted;"));
        Assert.That(completionHandler, Is.GreaterThanOrEqualTo(0));
        Assert.That(openGoal, Is.GreaterThan(completionHandler));
        Assert.That(
            generalSetup.LastIndexOf("GF.UI.OpenUIForm(UIViews.GoalUIForm);", System.StringComparison.Ordinal),
            Is.EqualTo(openGoal));
    }

    [Test]
    public void InPlaceLevelSwitch_UsesLevelSwitchLoadingWithoutWorldVisibilityOverrides()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string runtime = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        string fog = File.ReadAllText(Path.Combine(scriptsRoot, "MiniMap", "FOG3", "Fog3Manager.cs"));
        string levelSelection = File.ReadAllText(Path.Combine(scriptsRoot, "GameClass", "LevelSelectionService.cs"));
        string generalSetup = File.ReadAllText(Path.Combine(scriptsRoot, "UTManagers", "GeneralSetup.cs"));
        string levelEntity = File.ReadAllText(Path.Combine(scriptsRoot, "Entity", "LevelEntity.cs"));
        string healthBar = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "HealthBarComp.cs"));
        string levelSwitchPrefab = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame",
            "Prefabs",
            "UI",
            "LevelSwitchUIForm.prefab"));

        int transitionStart = runtime.IndexOf(
            "private async UniTaskVoid EnterRuntimeLevelInPlaceAsync",
            System.StringComparison.Ordinal);
        int shutdownPreviousPipeline = runtime.IndexOf(
            "m_RuntimeInitPipeline?.Shutdown();",
            transitionStart,
            System.StringComparison.Ordinal);
        int startRuntimePipeline = runtime.IndexOf(
            "new RuntimeInitPipeline(RuntimeInitLogTag, RuntimeLevelIdentifier, RequiredRuntimeSystems)",
            shutdownPreviousPipeline,
            System.StringComparison.Ordinal);
        int completePresentation = runtime.IndexOf(
            "m_RuntimeInitPipeline.CompleteLoadingPresentation();",
            startRuntimePipeline,
            System.StringComparison.Ordinal);

        Assert.That(shutdownPreviousPipeline, Is.GreaterThan(transitionStart));
        Assert.That(startRuntimePipeline, Is.GreaterThan(shutdownPreviousPipeline));
        Assert.That(completePresentation, Is.GreaterThan(startRuntimePipeline));
        Assert.That(levelSwitchPrefab, Does.Contain("m_Color: {r: 0, g: 0, b: 0, a: 1}"));
        Assert.That(levelSelection, Does.Not.Contain("HideEntityRenderersDuringLoad"));
        Assert.That(levelSelection, Does.Not.Contain("RestoreHiddenLoadingRenderers"));
        Assert.That(generalSetup, Does.Not.Contain("HideEntityRenderersDuringLoad"));
        Assert.That(levelEntity, Does.Not.Contain("m_HiddenDuringRuntimeInitialization"));
        Assert.That(healthBar, Does.Not.Contain("_visibleByLevelLoad"));
        Assert.That(fog, Does.Not.Contain("SetWorldOverlayPresentationVisible"));
    }

    [Test]
    public void DeprecatedBuiltinLoadingChain_IsRemoved()
    {
        string gameRoot = Path.Combine(Application.dataPath, "AAAGame");
        string runtimeScripts = Path.Combine(gameRoot, "Scripts");
        string builtinScripts = Path.Combine(gameRoot, "ScriptsBuiltin", "Runtime");
        string combined = string.Concat(
            File.ReadAllText(Path.Combine(runtimeScripts, "Procedures", "PreloadProcedure.cs")),
            File.ReadAllText(Path.Combine(runtimeScripts, "Procedures", "ChangeSceneProcedure.cs")),
            File.ReadAllText(Path.Combine(runtimeScripts, "Procedures", "RuntimeProcedureBase.cs")),
            File.ReadAllText(Path.Combine(runtimeScripts, "Procedures", "MenuProcedure.cs")),
            File.ReadAllText(Path.Combine(builtinScripts, "Extension", "BuiltinViewComponent.cs")),
            File.ReadAllText(Path.Combine(builtinScripts, "Procedures", "UpdateResourcesProcedure.cs")),
            File.ReadAllText(Path.Combine(builtinScripts, "Procedures", "LoadHotfixDllProcedure.cs")));
        string launchScene = File.ReadAllText(Path.Combine(gameRoot, "Scene", "Launch.unity"));

        Assert.That(combined, Does.Not.Contain("ShowLoadingProgress"));
        Assert.That(combined, Does.Not.Contain("HideLoadingProgress"));
        Assert.That(combined, Does.Not.Contain("SetLoadingProgress"));
        Assert.That(combined, Does.Not.Contain("SuppressNextBuiltinLoadingProgress"));
        Assert.That(launchScene, Does.Not.Contain("m_Name: LoadingView"));
        Assert.That(launchScene, Does.Not.Contain("loadingProgressNode:"));
    }

    [Test]
    public void LevelEntryDialog_HasOpaqueFullscreenBackdrop()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/LvEnterDialog.prefab");

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.transform.childCount, Is.GreaterThan(0));

        Transform backdropTransform = prefab.transform.GetChild(0);
        RectTransform backdrop = backdropTransform as RectTransform;
        Image image = backdropTransform.GetComponent<Image>();

        Assert.That(backdropTransform.name, Is.EqualTo("Backdrop"));
        Assert.That(backdrop, Is.Not.Null);
        Assert.That(backdrop.gameObject.activeSelf, Is.True);
        Assert.That(backdrop.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(backdrop.anchorMax, Is.EqualTo(Vector2.one));
        Assert.That(backdrop.offsetMin, Is.EqualTo(Vector2.zero));
        Assert.That(backdrop.offsetMax, Is.EqualTo(Vector2.zero));
        Assert.That(image, Is.Not.Null);
        Assert.That(image.color, Is.EqualTo(Color.black));
        Assert.That(image.raycastTarget, Is.True);
    }

    [Test]
    public void LaunchStartup_UsesRuntimeOwnedLevelSelectionChain()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string preload = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "PreloadProcedure.cs"));
        string runtime = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        string levelSwitch = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "LevelSwitchUIForm.cs"));
        string levelDialog = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "LvEnterDialog.cs"));
        string levelSelection = File.ReadAllText(Path.Combine(scriptsRoot, "GameClass", "LevelSelectionService.cs"));
        string appConfigs = File.ReadAllText(
            Path.Combine(Application.dataPath, "AAAGame", "ScriptableAssets", "Core", "AppConfigs.asset"));
        string retiredStartupProcedure =
            Path.Combine(scriptsRoot, "Procedures", "StartupLevelSelectProcedure.cs");

        int careerPrepare = preload.IndexOf("CareerConfigRuntime.Prepare()", System.StringComparison.Ordinal);
        int runtimeSceneChange = preload.IndexOf(
            "ChangeState<ChangeSceneProcedure>(procedureOwner)",
            System.StringComparison.Ordinal);
        Assert.That(careerPrepare, Is.GreaterThanOrEqualTo(0));
        Assert.That(runtimeSceneChange, Is.GreaterThan(careerPrepare));
        Assert.That(preload, Does.Contain("ChangeState<ChangeSceneProcedure>(procedureOwner)"));
        Assert.That(preload, Does.Not.Contain("ChangeState<StartupLevelSelectProcedure>"));
        Assert.That(runtime, Does.Contain("LevelSelectionService.OpenLevelSwitch(true)"));
        Assert.That(runtime, Does.Contain("internal bool TryEnterPreparedCareerRun"));
        Assert.That(runtime, Does.Contain("TryValidatePreparedCareerRunLevel"));
        Assert.That(runtime, Does.Not.Contain("public bool TryEnterRuntimeLevel"));
        Assert.That(runtime, Does.Not.Contain("StartRuntimeInitPipeline(RuntimeLevelIdentifier, true)"));
        Assert.That(levelSwitch, Does.Contain("LvEnterDialog.Open"));
        Assert.That(levelSwitch, Does.Not.Contain("TryLoadLevel"));
        Assert.That(levelSwitch, Does.Not.Contain("StartupLevelSelectProcedure"));
        Assert.That(levelDialog, Does.Contain("CareerRunSettings.BeginRun"));
        Assert.That(levelDialog, Does.Contain("LevelSelectionService.TryEnterPreparedCareerRun"));
        Assert.That(levelDialog, Does.Not.Contain("StartupLevelSelectProcedure"));
        Assert.That(levelSelection, Does.Not.Contain("public static bool TryEnterLevel("));
        Assert.That(levelSelection, Does.Not.Contain("TryEnterLevelInPlace"));
        Assert.That(appConfigs, Does.Not.Contain("StartupLevelSelectProcedure"));
        Assert.That(File.Exists(retiredStartupProcedure), Is.False);
    }

    [Test]
    public void EditorRuntimeLevelEntryLauncher_UsesLaunchAndPreparedCareerChainWithoutUiAutomation()
    {
        string source = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame",
            "Scripts",
            "Editor",
            "EditorRuntimeLevelEntryLauncher.cs"));

        Assert.That(source, Does.Contain("Assets/AAAGame/Scene/Launch.unity"));
        Assert.That(source, Does.Contain("EditorSceneManager.OpenScene"));
        Assert.That(source, Does.Contain("EditorRuntimeLevelEntry.TryEnterWithDefaultCareer"));
        Assert.That(source, Does.Contain("runtimeProcedure.IsEditorStressRuntimeReady"));
        Assert.That(source, Does.Contain("Runtime level identifier cannot be empty."));
        Assert.That(source, Does.Not.Contain("LevelSwitchUIForm"));
        Assert.That(source, Does.Not.Contain("LvEnterDialog"));
        Assert.That(source, Does.Not.Contain("SetForegroundWindow"));
        Assert.That(source, Does.Not.Contain("SendKeys"));
        Assert.That(source, Does.Not.Contain("mouse_event"));
    }

    [Test]
    public void LaunchStartup_PreloadsLevelEntryDialogBeforeRuntimeScene()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string preload = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "PreloadProcedure.cs"));
        string levelDialog = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "LvEnterDialog.cs"));
        int preloadMethod = preload.IndexOf("private async void PreloadAndInitData()", System.StringComparison.Ordinal);
        int dialogPreload = preload.IndexOf("PreloadLvEnterDialogAsset();", System.StringComparison.Ordinal);
        int completionGate = preload.IndexOf("if (loadedProgress >= totalProgress", System.StringComparison.Ordinal);
        int runtimeSceneChange = preload.IndexOf(
            "ChangeState<ChangeSceneProcedure>(procedureOwner)",
            System.StringComparison.Ordinal);

        Assert.That(dialogPreload, Is.GreaterThan(preloadMethod));
        Assert.That(runtimeSceneChange, Is.GreaterThan(completionGate));
        Assert.That(preload, Does.Contain("appConfig.Configs.Length + 9"));
        Assert.That(preload, Does.Contain("LvEnterDialog.RetainPreloadedAsset(asset);"));
        Assert.That(levelDialog, Does.Contain("GF.Resource.UnloadAsset(s_PreloadedAsset);"));
    }

    [Test]
    public void PreparedCareerEntry_RejectsMissingStartingIndustry()
    {
        CareerRunSettings.CancelRun();

        bool entered = LevelSelectionService.TryEnterPreparedCareerRun(out string errorMessage);

        Assert.That(entered, Is.False);
        Assert.That(errorMessage, Does.Contain("starting industry"));
    }

    [Test]
    public void LevelBriefing_HasNoCommunicationReplayOrCollapseControls()
    {
        string briefing = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame",
            "Scripts",
            "UI",
            "LvEnterDialog.Briefing.cs"));

        Assert.That(briefing, Does.Not.Contain("LvEnter.Replay"));
        Assert.That(briefing, Does.Not.Contain("LvEnter.Collapse"));
        Assert.That(briefing, Does.Not.Contain("ToggleCommunicationCard"));
    }
}
