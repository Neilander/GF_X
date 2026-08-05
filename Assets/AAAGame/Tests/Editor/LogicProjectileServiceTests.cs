using NUnit.Framework;

public class LogicProjectileServiceTests
{
    [TearDown]
    public void TearDown()
    {
        if (LogicProjectileService.IsActive)
            LogicProjectileService.EndTimeline();
    }

    [Test]
    public void AdvanceToward_ReachesTargetWithoutOvershoot()
    {
        FixVector2 result = LogicProjectileService.AdvanceToward(
            FixVector2.Zero,
            new FixVector2((Fix64)3, (Fix64)4),
            (Fix64)5,
            out bool arrived);

        Assert.IsTrue(arrived);
        Assert.AreEqual(new FixVector2((Fix64)3, (Fix64)4), result);
    }

    [Test]
    public void AdvanceToward_UsesFixedPointDistanceStep()
    {
        FixVector2 result = LogicProjectileService.AdvanceToward(
            FixVector2.Zero,
            new FixVector2((Fix64)10, Fix64.Zero),
            (Fix64)2,
            out bool arrived);

        Assert.IsFalse(arrived);
        Assert.AreEqual((Fix64)2, result.x);
        Assert.AreEqual(Fix64.Zero, result.y);
    }

    [Test]
    public void EmptySnapshotRestore_PreservesAllocatorAndFrameState()
    {
        LogicProjectileService.BeginTimeline();
        LogicProjectileSnapshot snapshot = LogicProjectileService.CaptureSnapshot();

        LogicProjectileService.RestoreSnapshot(snapshot);

        Assert.AreEqual(0, LogicProjectileService.ActiveCount);
        Assert.AreEqual(0, LogicProjectileService.RetainedViewStateCount);
        Assert.AreEqual(0UL, LogicProjectileService.LastCompletedFrame);
    }

    [Test]
    public void ProjectileSubmissionSource_DoesNotRequireBoundViewInsideLogicTick()
    {
        string root = UnityEngine.Application.dataPath;
        string rangedWeaponSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "AAAGame/Scripts/GeneralCreature/RangedWeaponSO.cs"));
        string directAttackSource = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "AAAGame/Scripts/GeneralCreature/DirectAtkComp.cs"));

        StringAssert.DoesNotContain("TryGetBoundView", rangedWeaponSource);
        StringAssert.DoesNotContain("RequireProjectileOrigin", rangedWeaponSource);
        StringAssert.Contains("ProjectilePresentationService.Publish", rangedWeaponSource);
        StringAssert.Contains("ProjectilePresentationService.Publish", directAttackSource);
    }
}
