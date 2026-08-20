using NUnit.Framework;

[TestFixture]
public sealed class LogicInteractionHoldServiceTests
{
    [TestCase(0f)]
    [TestCase(372f)]
    public void BuildingPanelPlacement_AvoidsProjectedTargetAndStaysOnScreen(float reservedRightWidth)
    {
        var parentObject = new UnityEngine.GameObject("BuildingPanelParent", typeof(UnityEngine.RectTransform));
        var panelObject = new UnityEngine.GameObject("BuildingPanel", typeof(UnityEngine.RectTransform));
        try
        {
            var parent = parentObject.GetComponent<UnityEngine.RectTransform>();
            parent.sizeDelta = new UnityEngine.Vector2(1920f, 1080f);
            var panel = panelObject.GetComponent<UnityEngine.RectTransform>();
            panel.SetParent(parent, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            panel.sizeDelta = new UnityEngine.Vector2(624f, 394f);
            var target = new UnityEngine.Rect(-60f, -50f, 120f, 100f);

            BuildingPanelScreenClamp.PlaceBesideTargetBounds(
                panel,
                parent,
                target,
                new UnityEngine.Vector2(0f, 80f),
                reservedRightWidth);

            UnityEngine.Bounds bounds = UnityEngine.RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
            var panelRect = UnityEngine.Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.y,
                bounds.max.x + reservedRightWidth,
                bounds.max.y);
            Assert.IsFalse(panelRect.Overlaps(target), $"panel={panelRect}, target={target}");
            Assert.GreaterOrEqual(panelRect.xMin, parent.rect.xMin + 8f - 1e-4f);
            Assert.LessOrEqual(panelRect.xMax, parent.rect.xMax - 8f + 1e-4f);
            Assert.GreaterOrEqual(panelRect.yMin, parent.rect.yMin + 8f - 1e-4f);
            Assert.LessOrEqual(panelRect.yMax, parent.rect.yMax - 8f + 1e-4f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panelObject);
            UnityEngine.Object.DestroyImmediate(parentObject);
        }
    }

    [Test]
    public void BuildingPanelPlacement_WithNarrowDetailPanel_FitsBesideCenteredTarget()
    {
        var parentObject = new UnityEngine.GameObject("BuildingPanelParent", typeof(UnityEngine.RectTransform));
        var panelObject = new UnityEngine.GameObject("BuildingPanel", typeof(UnityEngine.RectTransform));
        try
        {
            var parent = parentObject.GetComponent<UnityEngine.RectTransform>();
            parent.sizeDelta = new UnityEngine.Vector2(1920f, 1080f);
            var panel = panelObject.GetComponent<UnityEngine.RectTransform>();
            panel.SetParent(parent, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            panel.sizeDelta = new UnityEngine.Vector2(624f, 464f);
            var target = new UnityEngine.Rect(-80f, -79f, 160f, 158f);

            BuildingPanelScreenClamp.PlaceBesideTargetBounds(
                panel,
                parent,
                target,
                new UnityEngine.Vector2(-BuildingInfoItem.DetailPanelCenterOffset, 80f),
                BuildingInfoItem.DetailPanelWidth);

            UnityEngine.Bounds bounds = UnityEngine.RectTransformUtility.CalculateRelativeRectTransformBounds(parent, panel);
            var completePanelRect = UnityEngine.Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.y,
                bounds.max.x + BuildingInfoItem.DetailPanelWidth,
                bounds.max.y);
            Assert.IsFalse(completePanelRect.Overlaps(target), $"panel={completePanelRect}, target={target}");
            Assert.GreaterOrEqual(completePanelRect.xMin, parent.rect.xMin + 8f - 1e-4f);
            Assert.LessOrEqual(completePanelRect.xMax, parent.rect.xMax - 8f + 1e-4f);
            Assert.GreaterOrEqual(completePanelRect.yMin, parent.rect.yMin + 8f - 1e-4f);
            Assert.LessOrEqual(completePanelRect.yMax, parent.rect.yMax - 8f + 1e-4f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panelObject);
            UnityEngine.Object.DestroyImmediate(parentObject);
        }
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
        Assert.AreEqual(
            expectedSeconds,
            BuildingInteractionHoldPresentation.ResolveDurationSeconds(starCount, 1f),
            1e-4f);
    }

    [TestCase(1f / 30f, 60)]
    [TestCase(1f / 60f, 120)]
    [TestCase(1f / 120f, 240)]
    public void BuildingInfoRecycleHold_UsesRenderDeltaInsteadOfLogicFrames(float deltaTime, int frameCount)
    {
        var method = typeof(BuildingInfoTips).GetMethod(
            "AdvanceRecycleHoldProgress",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        float progress = 0f;
        for (int i = 0; i < frameCount; i++)
            progress = (float)method.Invoke(null, new object[] { progress, true, deltaTime });

        Assert.AreEqual(1f, progress, 1e-5f);
        progress = (float)method.Invoke(null, new object[] { progress, false, 1f });
        Assert.AreEqual(0.5f, progress, 1e-5f);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void BuildingAndUpgradeHolds_CompleteByRenderTimeAtAnyRenderRate(int renderRate)
    {
        const int starCount = 4;
        float duration = BuildingInteractionHoldPresentation.ResolveDurationSeconds(starCount, 1f);
        int frameCount = UnityEngine.Mathf.RoundToInt(duration * renderRate);
        float deltaTime = 1f / renderRate;

        AssertPanelHoldCompletesAndReleases(starCount, deltaTime, frameCount, duration);
    }

    [TestCase(0, false, false, 0)]
    [TestCase(0, false, true, 1)]
    [TestCase(1, true, false, 0)]
    [TestCase(1, false, false, 1)]
    [TestCase(1, true, true, 1)]
    public void UpgradeTargetTransition_PrefersSingleContinuouslyHeldBranchKey(
        int rememberedIndex,
        bool firstHeld,
        bool secondHeld,
        int expectedIndex)
    {
        var method = typeof(BuildingUpgradeTips).GetMethod(
            "ResolveDefaultOptionIndex",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        int actual = (int)method.Invoke(null, new object[]
        {
            rememberedIndex,
            new[] { firstHeld, secondHeld },
        });

        Assert.AreEqual(expectedIndex, actual);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void BuildingRecycleHolds_CompleteByRenderTimeAtAnyRenderRate(int renderRate)
    {
        const float durationSeconds = 2f;
        int frameCount = UnityEngine.Mathf.RoundToInt(durationSeconds * renderRate);
        float deltaTime = 1f / renderRate;

        AssertRecycleHoldCompletesAndReleases(
            typeof(BuildingInfoTips),
            deltaTime,
            frameCount);
        AssertRecycleHoldCompletesAndReleases(
            typeof(BuildingUpgradeTips),
            deltaTime,
            frameCount);
    }

    [Test]
    public void BuildingInfoRecycleHold_DoesNotConsumeLogicInputOrPanelHoldService()
    {
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                UnityEngine.Application.dataPath,
                "AAAGame/Scripts/UI/BuildingInfoTips.cs"));

        StringAssert.DoesNotContain("LogicInteractionHoldService", source);
        StringAssert.DoesNotContain("LogicInputFrame", source);
        StringAssert.Contains("Time.deltaTime", source);
        StringAssert.Contains("IsPrimaryPointerPressed", source);
    }

    [Test]
    public void BuildingPanelsKeepHoldPresentationOnRenderFramesAndSubmitFinalCommandsOnly()
    {
        string assetsPath = UnityEngine.Application.dataPath;
        string buildTips = System.IO.File.ReadAllText(System.IO.Path.Combine(
            assetsPath,
            "AAAGame/Scripts/UI/BuildingBuildTips.cs"));
        string upgradeTips = System.IO.File.ReadAllText(System.IO.Path.Combine(
            assetsPath,
            "AAAGame/Scripts/UI/BuildingUpgradeTips.cs"));
        string infoTips = System.IO.File.ReadAllText(System.IO.Path.Combine(
            assetsPath,
            "AAAGame/Scripts/UI/BuildingInfoTips.cs"));
        string buildManager = System.IO.File.ReadAllText(System.IO.Path.Combine(
            assetsPath,
            "AAAGame/Scripts/Build/BuildManager.cs"));
        string techManager = System.IO.File.ReadAllText(System.IO.Path.Combine(
            assetsPath,
            "AAAGame/Scripts/Build/Tech/TechManager.cs"));

        AssertRenderFramePanelBoundary(buildTips);
        AssertRenderFramePanelBoundary(upgradeTips);
        AssertRenderFramePanelBoundary(infoTips);
        StringAssert.Contains("LogicInteractionCommandService.Submit", buildManager);
        StringAssert.Contains("LogicInteractionActionKind.ConstructBuilding", buildManager);
        StringAssert.Contains("LogicInteractionActionKind.RecycleBuilding", buildManager);
        StringAssert.Contains("LogicInteractionCommandService.Submit", techManager);
        StringAssert.Contains("LogicInteractionActionKind.UpgradeBuilding", techManager);
    }

    [TestCase(LogicInteractionOptionKind.ConstructBuilding)]
    [TestCase(LogicInteractionOptionKind.UpgradeBuilding)]
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

    private static void AssertPanelHoldCompletesAndReleases(
        int starCount,
        float deltaTime,
        int frameCount,
        float duration)
    {
        float progress = 0f;
        for (int i = 0; i < frameCount; i++)
        {
            progress = BuildingInteractionHoldPresentation.AdvanceProgressStars(
                progress,
                true,
                starCount,
                deltaTime,
                duration);
        }
        Assert.AreEqual(starCount, progress, 1e-4f);

        for (int i = 0; i < frameCount; i++)
        {
            progress = BuildingInteractionHoldPresentation.AdvanceProgressStars(
                progress,
                false,
                starCount,
                deltaTime,
                duration);
        }
        Assert.AreEqual(0f, progress, 1e-4f);
    }

    private static void AssertRecycleHoldCompletesAndReleases(
        System.Type panelType,
        float deltaTime,
        int frameCount)
    {
        var method = panelType.GetMethod(
            "AdvanceRecycleHoldProgress",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{panelType.Name} is missing AdvanceRecycleHoldProgress.");

        float progress = 0f;
        for (int i = 0; i < frameCount; i++)
            progress = (float)method.Invoke(null, new object[] { progress, true, deltaTime });
        Assert.AreEqual(1f, progress, 1e-4f, panelType.Name);

        for (int i = 0; i < frameCount; i++)
            progress = (float)method.Invoke(null, new object[] { progress, false, deltaTime });
        Assert.AreEqual(0f, progress, 1e-4f, panelType.Name);
    }

    private static void AssertRenderFramePanelBoundary(string source)
    {
        StringAssert.Contains("Time.deltaTime", source);
        StringAssert.DoesNotContain("LogicInputFrame", source);
        StringAssert.DoesNotContain("LogicInteractionHoldService", source);
    }
}
