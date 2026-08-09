using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class StartupLevelFlowTests
{
    [Test]
    public void LaunchStartup_UsesRuntimeOwnedLevelSelectionChain()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame", "Scripts");
        string preload = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "PreloadProcedure.cs"));
        string runtime = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures", "RuntimeProcedureBase.cs"));
        string levelSwitch = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "LevelSwitchUIForm.cs"));
        string levelDialog = File.ReadAllText(Path.Combine(scriptsRoot, "UI", "LvEnterDialog.cs"));

        int careerPrepare = preload.IndexOf("CareerConfigRuntime.Prepare()", System.StringComparison.Ordinal);
        int runtimeSceneChange = preload.IndexOf(
            "ChangeState<ChangeSceneProcedure>(procedureOwner)",
            System.StringComparison.Ordinal);
        Assert.That(careerPrepare, Is.GreaterThanOrEqualTo(0));
        Assert.That(runtimeSceneChange, Is.GreaterThan(careerPrepare));
        Assert.That(preload, Does.Contain("ChangeState<ChangeSceneProcedure>(procedureOwner)"));
        Assert.That(preload, Does.Not.Contain("ChangeState<StartupLevelSelectProcedure>"));
        Assert.That(runtime, Does.Contain("LevelSelectionService.OpenLevelSwitch(true)"));
        Assert.That(levelSwitch, Does.Contain("LevelSelectionService.TryEnterLevelInPlace"));
        Assert.That(levelSwitch, Does.Not.Contain("StartupLevelSelectProcedure"));
        Assert.That(levelDialog, Does.Contain("LevelSelectionService.TryEnterLevelInPlace"));
        Assert.That(levelDialog, Does.Not.Contain("StartupLevelSelectProcedure"));
    }
}
