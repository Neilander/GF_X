using NUnit.Framework;

[TestFixture]
public sealed class LogicInteractionHoldServiceTests
{
    [SetUp]
    public void SetUp()
    {
        if (LogicInteractionHoldService.IsActive)
            LogicInteractionHoldService.EndTimeline();
        LogicInteractionHoldService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        if (LogicInteractionHoldService.IsActive)
            LogicInteractionHoldService.EndTimeline();
    }

    [Test]
    public void HeldInput_CompletesOnExactLogicFrameAndLocksUntilRelease()
    {
        int executionCount = 0;
        LogicInteractionHoldService.RegisterConsumer(_ => true, _ =>
        {
            executionCount++;
            return true;
        });
        LogicInputTimeline timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.001d, LogicInputButton.InteractionPrimary);

        for (ulong frame = 1; frame < LogicInteractionHoldService.HoldThresholdFrames; frame++)
        {
            LogicInputFrame input = timeline.Seal(frame, frame / 30d);
            LogicInteractionHoldService.ProcessFrame(input);
        }

        Assert.AreEqual(0, executionCount);
        Assert.Less(
            LogicInteractionHoldService.GetProgress(InputKey.InteractionPrimary).RawValue,
            Fix64.One.RawValue);

        ulong completionFrame = LogicInteractionHoldService.HoldThresholdFrames;
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(completionFrame, completionFrame / 30d));
        Assert.AreEqual(1, executionCount);
        Assert.AreEqual(Fix64.One, LogicInteractionHoldService.GetProgress(InputKey.InteractionPrimary));

        LogicInteractionHoldService.ProcessFrame(timeline.Seal(completionFrame + 1, (completionFrame + 1) / 30d));
        Assert.AreEqual(1, executionCount);

        timeline.EnqueueButtonReleased((completionFrame + 1.5d) / 30d, LogicInputButton.InteractionPrimary);
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(completionFrame + 2, (completionFrame + 2) / 30d));
        Assert.AreEqual(Fix64.Zero, LogicInteractionHoldService.GetProgress(InputKey.InteractionPrimary));
    }

    [Test]
    public void UnavailableInteraction_DoesNotPrechargeHold()
    {
        bool available = false;
        LogicInteractionHoldService.RegisterConsumer(_ => available, _ => true);
        LogicInputTimeline timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.001d, LogicInputButton.InteractionSecondary);

        for (ulong frame = 1; frame <= 20; frame++)
            LogicInteractionHoldService.ProcessFrame(timeline.Seal(frame, frame / 30d));
        Assert.AreEqual(Fix64.Zero, LogicInteractionHoldService.GetProgress(InputKey.InteractionSecondary));

        available = true;
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(21, 21d / 30d));
        Assert.AreEqual(
            (Fix64)1 / LogicInteractionHoldService.HoldThresholdFrames,
            LogicInteractionHoldService.GetProgress(InputKey.InteractionSecondary));
    }

    [Test]
    public void PanelHold_UsesSealedBuildButtonFramesAndDynamicThreshold()
    {
        int executionCount = 0;
        bool available = true;
        LogicInteractionHoldService.RegisterPanelConsumer(
            frame => available && frame.IsHeld(LogicInputButton.Build1) ? LogicInputButton.Build1 : null,
            () => 3,
            () =>
            {
                executionCount++;
                return true;
            });
        LogicInputTimeline timeline = CreateTimeline();
        timeline.EnqueueButtonPressed(0.001d, LogicInputButton.Build1);

        LogicInteractionHoldService.ProcessFrame(timeline.Seal(1, 1d / 30d));
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(2, 2d / 30d));
        Assert.AreEqual(0, executionCount);
        Assert.AreEqual(((Fix64)2 / 3).RawValue, LogicInteractionHoldService.GetPanelProgress().RawValue);

        LogicInteractionHoldService.ProcessFrame(timeline.Seal(3, 3d / 30d));
        available = false;
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(4, 4d / 30d));
        Assert.AreEqual(1, executionCount);
        available = true;
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(5, 5d / 30d));
        Assert.AreEqual(1, executionCount);

        timeline.EnqueueButtonReleased(5.5d / 30d, LogicInputButton.Build1);
        LogicInteractionHoldService.ProcessFrame(timeline.Seal(6, 6d / 30d));
        Assert.AreEqual(Fix64.Zero.RawValue, LogicInteractionHoldService.GetPanelProgress().RawValue);
    }

    [TestCase(1, 1f)]
    [TestCase(2, 1f)]
    [TestCase(3, 1f)]
    [TestCase(4, 1.2f)]
    [TestCase(7, 2f)]
    [TestCase(8, 2f)]
    [TestCase(16, 2f)]
    [TestCase(22, 2.1f)]
    public void BuildingHoldPresentation_ClampsPerStarSpeedAndTotalDuration(int starCount, float expectedSeconds)
    {
        Assert.AreEqual(expectedSeconds, ResolveHoldDuration(typeof(BuildingBuildTips), starCount), 1e-4f);
        Assert.AreEqual(expectedSeconds, ResolveHoldDuration(typeof(BuildingUpgradeTips), starCount), 1e-4f);
    }

    [TestCase(LogicInteractionOptionKind.ConstructBuilding)]
    [TestCase(LogicInteractionOptionKind.UpgradeBuilding)]
    [TestCase(LogicInteractionOptionKind.ResearchTech)]
    public void DedicatedBuildingPanelOption_DoesNotExposeLogicInputKey(LogicInteractionOptionKind kind)
    {
        var descriptors = new System.Collections.Generic.List<LogicInteractionOptionDescriptor>();
        var method = typeof(LogicInteractionOptionDescriptorFactory).GetMethod(
            "AddDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        method.Invoke(null, new object[]
        {
            descriptors,
            new LogicEntityId(71002),
            "building-panel-option",
            kind,
            "primary-id",
            "secondary-id",
        });

        Assert.AreEqual(1, descriptors.Count);
        Assert.IsFalse(descriptors[0].HasInputKey);
    }

    [Test]
    public void CoinPreview_ClearRestoresRealCoinDisplay()
    {
        const int ownerId = 71001;
        try
        {
            IngameCoinPreviewState.SetPreviewDeduction(ownerId, 3);
            Assert.AreEqual(7, IngameCoinPreviewState.GetDisplayCoinValue(10));

            IngameCoinPreviewState.ClearPreviewDeduction(ownerId);
            Assert.AreEqual(10, IngameCoinPreviewState.GetDisplayCoinValue(10));
        }
        finally
        {
            IngameCoinPreviewState.ClearPreviewDeduction(ownerId);
        }
    }

    private static LogicInputTimeline CreateTimeline()
    {
        var timeline = new LogicInputTimeline();
        timeline.Begin(0d, FixVector2.Zero, 0, FixVector2.Zero, false, FixVector2.Zero);
        return timeline;
    }

    private static float ResolveHoldDuration(System.Type panelType, int starCount)
    {
        var method = panelType.GetMethod(
            "ResolveHoldDurationSeconds",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{panelType.Name} is missing ResolveHoldDurationSeconds.");
        return (float)method.Invoke(null, new object[] { starCount });
    }
}
