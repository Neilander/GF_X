using System.IO;
using NUnit.Framework;
using UnityEngine;

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
    public void PreparedCareerEntry_RejectsMissingStartingIndustry()
    {
        CareerRunSettings.CancelRun();

        bool entered = LevelSelectionService.TryEnterPreparedCareerRun(out string errorMessage);

        Assert.That(entered, Is.False);
        Assert.That(errorMessage, Does.Contain("starting industry"));
    }
}
