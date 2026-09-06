using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class LogicMoveExecutorTests
{
    [Test]
    public void NavigationConstraintAndCollisionMobility_AreIndependent()
    {
        var executor = new LogicMoveExecutor();
        executor.SetNavigationConstrained(false);
        executor.SetInputFixed(new FixVector2(Fix64.One, Fix64.Zero));

        executor.PrepareLogicFrame(1, LogicFrameRuntime.FixedDeltaTime, true);

        Assert.IsTrue(executor.PreparedCollisionMovable);
        Assert.IsFalse(executor.PreparedNavigationConstraintEnabled);
        Assert.AreNotEqual(FixVector2.Zero, executor.PreparedResolvedHorizontalDisplacement);
    }

    [Test]
    public void NextFrameConstraintBypass_IsConsumedExactlyOnce()
    {
        var executor = new LogicMoveExecutor();
        executor.SetConstraintBypassForNextFrame();

        executor.PrepareLogicFrame(1, LogicFrameRuntime.FixedDeltaTime, true);
        Assert.IsFalse(executor.PreparedNavigationConstraintEnabled);
        executor.CommitPreparedLogicFrame(1);

        executor.PrepareLogicFrame(2, LogicFrameRuntime.FixedDeltaTime, true);
        Assert.IsTrue(executor.PreparedNavigationConstraintEnabled);
    }

    [Test]
    public void PersistentConstraintBypass_EndsOnlyWhenConstraintIsReenabled()
    {
        var executor = new LogicMoveExecutor();
        executor.EnableNavigationConstraintBypass();

        executor.PrepareLogicFrame(1, LogicFrameRuntime.FixedDeltaTime, true);
        Assert.IsFalse(executor.PreparedNavigationConstraintEnabled);
        executor.CommitPreparedLogicFrame(1);
        executor.PrepareLogicFrame(2, LogicFrameRuntime.FixedDeltaTime, true);
        Assert.IsFalse(executor.PreparedNavigationConstraintEnabled);
        executor.CommitPreparedLogicFrame(2);

        executor.SetNavigationConstrained(true);
        executor.PrepareLogicFrame(3, LogicFrameRuntime.FixedDeltaTime, true);
        Assert.IsTrue(executor.PreparedNavigationConstraintEnabled);
    }

    [Test]
    public void HardLocked_DoesNotApplyInputOrExternalVelocity()
    {
        var executor = new LogicMoveExecutor();
        executor.SetMovementMode(MovementMode.HardLocked);
        executor.SetInputFixed(new FixVector2(Fix64.One, Fix64.Zero));
        executor.SetExternalFixed(new FixVector2(Fix64.Zero, Fix64.One));

        executor.PrepareLogicFrame(1, LogicFrameRuntime.FixedDeltaTime, true);

        Assert.AreEqual(FixVector2.Zero, executor.PreparedResolvedHorizontalDisplacement);
    }

    [Test]
    public void PlayerInputSlideMode_IsPreparedOnlyForNormalInputMovement()
    {
        var executor = new LogicMoveExecutor();
        executor.SetInputFixed(new FixVector2(Fix64.One, Fix64.One), preserveSpeedOnStaticSlide: true);

        executor.PrepareLogicFrame(1, LogicFrameRuntime.FixedDeltaTime, true);

        Assert.IsTrue(executor.PreparedPreserveSpeedOnStaticSlide);
        executor.CommitPreparedLogicFrame(1);

        executor.SetInputFixed(new FixVector2(Fix64.One, Fix64.One), preserveSpeedOnStaticSlide: true);
        executor.SetExternalFixed(new FixVector2(Fix64.One, Fix64.Zero));
        executor.PrepareLogicFrame(2, LogicFrameRuntime.FixedDeltaTime, true);

        Assert.IsFalse(executor.PreparedPreserveSpeedOnStaticSlide);
    }

    [Test]
    public void MoveComponents_UseCommittedResolvedDisplacementForMovingState()
    {
        var characterMove = new CharacterMoveComp();
        var playerMove = new PlayerMoveComp();
        var actualDisplacement = new FixVector2(Fix64.FromRaw(128), Fix64.Zero);

        characterMove.CommitResolvedDisplacement(actualDisplacement);
        playerMove.CommitResolvedDisplacement(actualDisplacement);
        Assert.IsTrue(characterMove.IsMoving);
        Assert.IsTrue(playerMove.IsMoving);

        characterMove.CommitResolvedDisplacement(FixVector2.Zero);
        playerMove.CommitResolvedDisplacement(FixVector2.Zero);
        Assert.IsFalse(characterMove.IsMoving, "碰撞截断为零位移后不得保留计划移动状态");
        Assert.IsFalse(playerMove.IsMoving, "英雄撞墙被截断后不得保留计划移动状态");
    }

    [Test]
    public void ViewMoveExecutor_DoesNotOwnGameplayRegionRules()
    {
        string viewSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/GeneralCreature/MoveExecutor.cs"));
        string logicSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Movement/LogicAgentCollisionShadowService.cs"));

        StringAssert.DoesNotContain("Fog3Manager", viewSource);
        StringAssert.DoesNotContain("Fog3CellState", viewSource);
        StringAssert.DoesNotContain("GetStrongholdAtWorldPosition", viewSource);
        StringAssert.DoesNotContain("TutorialManager", viewSource);
        StringAssert.DoesNotContain("InGameDataModel.GetValue(IngameValueType.Phase)", viewSource);
        StringAssert.Contains("LogicMovementRegionConstraintService.ResolvePosition", logicSource);
    }

    [Test]
    public void TutorialLogicDetection_OwnsMovementBoundaryWithoutPhysXTrigger()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame/Scripts");
        string tutorialManager = File.ReadAllText(Path.Combine(scriptsRoot, "MeiyouUtility/TutorialManager.cs"));
        string tutorialTrigger = File.ReadAllText(Path.Combine(scriptsRoot, "MeiyouUtility/TutorialTriggerCollider.cs"));
        string runtime = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures/RuntimeProcedureBase.cs"));

        StringAssert.Contains("LogicStrongholdMap.TryResolveStrongholdId", tutorialManager);
        StringAssert.Contains("SetTutorialStrongholdBoundary", tutorialManager);
        StringAssert.DoesNotContain("OnTriggerEnter", tutorialTrigger);
        StringAssert.DoesNotContain("ILogicFrameUpdate", tutorialTrigger);
        StringAssert.DoesNotContain("FindObjectOfType<TutorialManager>", tutorialTrigger);
        StringAssert.Contains("LogicMovementRegionConstraintService.ApplyFrame(frame)", runtime);
    }

    [Test]
    public void PresentationAndAdapterScripts_DoNotReacquireMigratedGameplayAuthority()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame/Scripts");
        string buildingView = File.ReadAllText(Path.Combine(scriptsRoot, "Entity/BuildingEntity.cs"));
        string projectileView = File.ReadAllText(Path.Combine(scriptsRoot, "Projectile/Projectile.cs"));
        string productionAdapter = File.ReadAllText(Path.Combine(scriptsRoot, "GameClass/ProductionConditionManager.cs"));
        string inGameUi = File.ReadAllText(Path.Combine(scriptsRoot, "UI/InGameUIForm.cs"));
        string moveExecutorView = File.ReadAllText(Path.Combine(scriptsRoot, "GeneralCreature/MoveExecutor.cs"));
        string regionAuthority = File.ReadAllText(Path.Combine(scriptsRoot, "Movement/LogicMovementRegionConstraintService.cs"));
        string fogAuthority = File.ReadAllText(Path.Combine(scriptsRoot, "Card/LogicCardPlacementAuthority.cs"));
        string fogControllerView = File.ReadAllText(Path.Combine(scriptsRoot, "MiniMap/FOG3/Controller/Fog3Controller.cs"));
        string fogManagerView = File.ReadAllText(Path.Combine(scriptsRoot, "MiniMap/FOG3/Fog3Manager.cs"));

        StringAssert.DoesNotContain("BeginConstructionEscape", buildingView);
        StringAssert.DoesNotContain("Physics.Overlap", buildingView);
        StringAssert.DoesNotContain("EnableNavigationConstraintBypassUntilLegalPoint", buildingView);

        StringAssert.DoesNotContain("DamageHelper", projectileView);
        StringAssert.DoesNotContain("TakeDamage", projectileView);
        StringAssert.DoesNotContain("HitTarget", projectileView);

        StringAssert.DoesNotContain("void Update(", productionAdapter);
        StringAssert.DoesNotContain("OnSoldierDead", productionAdapter);
        StringAssert.DoesNotContain("OnCreatureHealthChanged", productionAdapter);
        StringAssert.DoesNotContain("OnIngamePhaseChanged", productionAdapter);
        StringAssert.DoesNotContain("EntityRegistry.AllEntities", productionAdapter);

        StringAssert.DoesNotContain("InGameDataModel.TryModifyValue(IngameValueType.Coin", inGameUi);
        StringAssert.DoesNotContain("InGameDataModel.TryModifyValue(IngameValueType.MaxSupply", inGameUi);
        StringAssert.Contains("LogicInGameValueCommandService.ScheduleDeltaForNextFrame", inGameUi);

        StringAssert.DoesNotContain("Fog3Manager", moveExecutorView);
        StringAssert.DoesNotContain("IsNonVisibleBlocked", moveExecutorView);
        StringAssert.Contains("LogicCardPlacementAuthority.IsVisibleFromCurrentLogicRevealers", regionAuthority);
        StringAssert.Contains("LogicMovementRegionConstraintFailure.NotVisible", regionAuthority);
        StringAssert.Contains("ConsumeVisibilityDirty", fogAuthority);
        StringAssert.DoesNotContain("RebuildVisibilityFromCurrentEntities", fogAuthority);
        StringAssert.Contains("s_MapData.MarkExplored", fogAuthority);
        StringAssert.Contains("entity.IsHeroEntity", fogAuthority);
        StringAssert.DoesNotContain("entity is IHeroLogicContext", fogAuthority);
        StringAssert.DoesNotContain("entity is HeroEntity", fogAuthority);
        StringAssert.Contains("PublishAuthoritativeVisibility", fogControllerView);
        StringAssert.DoesNotContain("MapData.ClearCurrentVisibility", fogControllerView);
        StringAssert.DoesNotContain("MapData.AddVisibility", fogControllerView);
        StringAssert.DoesNotContain("Physics.Linecast", fogControllerView);
        StringAssert.Contains("controller.PublishAuthoritativeVisibility", fogManagerView);
        StringAssert.DoesNotContain("controller.UpdateVisibility", fogManagerView);
        StringAssert.Contains("mapData.GetCellState(logicEntity.PositionFixed)", fogManagerView);
        StringAssert.DoesNotContain("ResolveEntityFogCellState(mapData, entity.transform.position)", fogManagerView);
    }

    [Test]
    public void RuntimeTimelineServices_AreHashedOrExplicitlyNonAuthority()
    {
        string scriptsRoot = Path.Combine(Application.dataPath, "AAAGame/Scripts");
        string runtimeSource = File.ReadAllText(Path.Combine(scriptsRoot, "Procedures/RuntimeProcedureBase.cs"));
        string hasherSource = File.ReadAllText(Path.Combine(scriptsRoot, "GameClass/LogicGameplayStateHasher.cs"));
        MatchCollection services = Regex.Matches(
            runtimeSource,
            @"^\s*([A-Za-z_][A-Za-z0-9_]*)\.BeginTimeline\(\);",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.IsNotEmpty(services);
        for (int i = 0; i < services.Count; i++)
        {
            string service = services[i].Groups[1].Value;
            switch (service)
            {
                case "LogicTimeControlService":
                    StringAssert.Contains("ComputeTimeControlHash", File.ReadAllText(Path.Combine(scriptsRoot, "GameClass/LogicReplay.cs")));
                    break;
                case "LogicEntityLifecycleService":
                    StringAssert.Contains("AddLifecycle(hasher, frame)", hasherSource);
                    break;
                case "LogicEntityViewSpawnQueue":
                case "ProjectilePresentationService":
                    StringAssert.DoesNotContain(service, hasherSource);
                    break;
                case "LogicEntityFrameSnapshotService":
                    StringAssert.Contains("LogicEntityFrameSnapshotService.Current", hasherSource);
                    break;
                case "MAEntityLogicFrameSystem":
                    StringAssert.Contains("MAEntityLogicFrameSystem.LastCompletedFrame", hasherSource);
                    StringAssert.Contains("AddDamageEvents(hasher)", hasherSource);
                    StringAssert.Contains("AddProjectiles(hasher)", hasherSource);
                    break;
                default:
                    StringAssert.Contains(service + ".WriteDeterministicState(", hasherSource);
                    break;
            }
        }
    }

    [Test]
    public void RuntimeTimelineServices_HaveSymmetricBeginAndEndLifecycle()
    {
        string runtimeSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "AAAGame/Scripts/Procedures/RuntimeProcedureBase.cs"));
        MatchCollection begins = Regex.Matches(
            runtimeSource,
            @"^\s*([A-Za-z_][A-Za-z0-9_]*)\.BeginTimeline\(\);",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        MatchCollection ends = Regex.Matches(
            runtimeSource,
            @"^\s*([A-Za-z_][A-Za-z0-9_]*)\.EndTimeline\(\);",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var beginServices = new System.Collections.Generic.HashSet<string>();
        var endServices = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < begins.Count; i++)
            beginServices.Add(begins[i].Groups[1].Value);
        for (int i = 0; i < ends.Count; i++)
            endServices.Add(ends[i].Groups[1].Value);

        CollectionAssert.AreEquivalent(beginServices, endServices);
    }
}
