using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class DeterministicStaticCollisionGeometryTests
{
    private static readonly Fix64 GroundBoundaryZ = Fix64.FromRaw(154419);

    [Test]
    public void ExactPointEightGap_AllowsHeroCircleToPassAtFullRadius()
    {
        LogicStaticCollisionWorld world = CreateLvTestGapWorld();
        Fix64 heroRadius = ReadConfiguredCollisionRadius("MediumUnitCollisionRadius");
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -43009,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2((Fix64)65, (Fix64)40),
                new FixVector2((Fix64)1.5f, (Fix64)1.5f),
                Fix64.Zero),
        };
        FixVector2 position = new FixVector2(Fix64.FromRaw(258472), Fix64.FromRaw(156244));
        FixVector2 worldInput = ResolveCameraRelativeInput(new Vector2(1f, 1f));
        Assert.Greater(worldInput.x.RawValue, 0);
        Assert.LessOrEqual(Math.Abs(worldInput.y.RawValue), 1);

        FixVector2 requestedPerTick = worldInput * (Fix64)0.25f;
        for (int tick = 0; tick < 32; tick++)
        {
            FixVector2 tickStart = position;
            LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
                world,
                position,
                requestedPerTick,
                heroRadius,
                obstacles,
                LogicStaticCollisionSlideMode.PreserveRemainingDistance);
            Assert.IsTrue(result.Success, $"tick={tick} failure={result.Failure} position={position}");
            position += result.ResolvedDisplacement;
            Assert.IsTrue(
                DeterministicStaticCollisionSolver.IsCircleClear(world, position, heroRadius, obstacles),
                $"tick={tick} start={tickStart} recovered={result.RecoveredStart} position={position} " +
                $"resolved={result.ResolvedDisplacement} startedOverlapping={result.StartedOverlapping} contacts={result.ContactCount} " +
                $"firstKey={result.FirstHitStableKey} firstNormal={result.FirstHitNormal}");
        }

        Assert.Greater(position.x.RawValue, ((Fix64)66.5f).RawValue, $"position={position}");
        Assert.GreaterOrEqual(position.y.RawValue, (GroundBoundaryZ + heroRadius - Fix64.FromRaw(1)).RawValue);
        Assert.LessOrEqual(position.y.RawValue, ((Fix64)38.5f - heroRadius + Fix64.FromRaw(1)).RawValue);
    }

    [TestCase(0, false, -1)]
    [TestCase(0, false, 0)]
    [TestCase(0, false, 1)]
    [TestCase(0, true, -1)]
    [TestCase(0, true, 0)]
    [TestCase(0, true, 1)]
    [TestCase(1, false, -1)]
    [TestCase(1, false, 0)]
    [TestCase(1, false, 1)]
    [TestCase(1, true, -1)]
    [TestCase(1, true, 0)]
    [TestCase(1, true, 1)]
    [TestCase(2, false, -1)]
    [TestCase(2, false, 0)]
    [TestCase(2, false, 1)]
    [TestCase(2, true, -1)]
    [TestCase(2, true, 0)]
    [TestCase(2, true, 1)]
    [TestCase(3, false, -1)]
    [TestCase(3, false, 0)]
    [TestCase(3, false, 1)]
    [TestCase(3, true, -1)]
    [TestCase(3, true, 0)]
    [TestCase(3, true, 1)]
    public void ExactPointEightGap_IsDirectionallySymmetricOnFixedPointLattice(
        int quarterTurns,
        bool reverse,
        int lateralTailRaw)
    {
        FixVector2 pivot = new FixVector2((Fix64)65, (Fix64)40);
        LogicStaticCollisionWorld world = CreateRotatedLvTestGapWorld(quarterTurns, pivot);
        Fix64 heroRadius = ReadConfiguredCollisionRadius("MediumUnitCollisionRadius");
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -43009,
                LogicStaticCollisionObstacleKind.Box,
                pivot,
                new FixVector2((Fix64)1.5f, (Fix64)1.5f),
                Fix64.Zero),
        };
        long startXRaw = reverse
            ? checked(pivot.x.RawValue * 2 - 258472L)
            : 258472L;
        FixVector2 localPosition = new FixVector2(Fix64.FromRaw(startXRaw), Fix64.FromRaw(156244));
        FixVector2 position = RotateQuarterTurns(localPosition, pivot, quarterTurns);
        FixVector2 localRequested = new FixVector2(
            Fix64.FromRaw(reverse ? -1024L : 1024L),
            Fix64.FromRaw(lateralTailRaw));
        FixVector2 requestedPerTick = RotateQuarterTurns(localRequested, FixVector2.Zero, quarterTurns);
        var trace = new System.Text.StringBuilder();

        for (int tick = 0; tick < 32; tick++)
        {
            LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
                world,
                position,
                requestedPerTick,
                heroRadius,
                obstacles,
                LogicStaticCollisionSlideMode.PreserveRemainingDistance);
            trace.Append("tick=").Append(tick)
                .Append(" startRaw=(").Append(position.x.RawValue).Append(',').Append(position.y.RawValue).Append(')')
                .Append(" recoveredRaw=(").Append(result.RecoveredStart.x.RawValue).Append(',').Append(result.RecoveredStart.y.RawValue).Append(')')
                .Append(" resolvedRaw=(").Append(result.ResolvedDisplacement.x.RawValue).Append(',').Append(result.ResolvedDisplacement.y.RawValue).Append(')')
                .Append(" contacts=").Append(result.ContactCount)
                .Append(" key=").Append(result.FirstHitStableKey)
                .Append(" normalRaw=(").Append(result.FirstHitNormal.x.RawValue).Append(',').Append(result.FirstHitNormal.y.RawValue).Append(')')
                .AppendLine();
            for (int contactIndex = 0;
                 contactIndex < result.ContactCount && contactIndex < LogicStaticCollisionSolveResult.MaxRecordedContactCount;
                 contactIndex++)
            {
                LogicStaticCollisionContactTrace contact = result.GetContactTrace(contactIndex);
                trace.Append("  contact=").Append(contactIndex)
                    .Append(" key=").Append(contact.StableKey)
                    .Append(" timeRaw=").Append(contact.Time.RawValue)
                    .Append(" positionRaw=(").Append(contact.Position.x.RawValue).Append(',').Append(contact.Position.y.RawValue).Append(')')
                    .Append(" incomingRaw=(").Append(contact.Incoming.x.RawValue).Append(',').Append(contact.Incoming.y.RawValue).Append(')')
                    .Append(" leftoverRaw=(").Append(contact.Leftover.x.RawValue).Append(',').Append(contact.Leftover.y.RawValue).Append(')')
                    .AppendLine();
            }
            Assert.IsTrue(
                result.Success,
                $"rotation={quarterTurns} reverse={reverse} tailRaw={lateralTailRaw} tick={tick} " +
                $"failure={result.Failure} position={position} recovered={result.RecoveredStart} " +
                $"resolved={result.ResolvedDisplacement} contacts={result.ContactCount} " +
                $"firstKey={result.FirstHitStableKey} firstNormal={result.FirstHitNormal}\n{trace}");
            position += result.ResolvedDisplacement;
            Assert.IsTrue(
                DeterministicStaticCollisionSolver.IsCircleClear(world, position, heroRadius, obstacles),
                $"rotation={quarterTurns} reverse={reverse} tailRaw={lateralTailRaw} tick={tick} position={position}\n{trace}");
        }

        FixVector2 finalLocal = RotateQuarterTurns(position, pivot, (4 - quarterTurns) & 3);
        if (reverse)
            Assert.Less(finalLocal.x.RawValue, ((Fix64)63.5f).RawValue, $"final={finalLocal}\n{trace}");
        else
            Assert.Greater(finalLocal.x.RawValue, ((Fix64)66.5f).RawValue, $"final={finalLocal}\n{trace}");
    }

    [Test]
    public void GapSmallerThanDiameter_IsRejectedWithoutRadiusCompensation()
    {
        LogicStaticCollisionWorld world = CreateLvTestGapWorld();
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -43009,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2((Fix64)65, (Fix64)40),
                new FixVector2((Fix64)1.5f, (Fix64)1.5f),
                Fix64.Zero),
        };
        Fix64 tooLargeRadius = (Fix64)0.401f;
        FixVector2 gapCenter = new FixVector2((Fix64)65, (GroundBoundaryZ + (Fix64)38.5f) / (Fix64)2);

        Assert.IsFalse(DeterministicStaticCollisionSolver.IsCircleClear(world, gapCenter, tooLargeRadius, obstacles));
    }

    [Test]
    public void BoundarySweep_CannotLeaveAuthoredGround()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld((Fix64)0, (Fix64)0, (Fix64)10, (Fix64)10);
        Fix64 radius = (Fix64)0.4f;
        FixVector2 start = new FixVector2((Fix64)5, (Fix64)2);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            new FixVector2(Fix64.Zero, (Fix64)(-5)),
            radius);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        long resolvedCenterYRaw = (start.y + result.ResolvedDisplacement.y).RawValue;
        Assert.GreaterOrEqual(resolvedCenterYRaw, radius.RawValue);
        Assert.LessOrEqual(resolvedCenterYRaw - radius.RawValue, 3);
    }

    [Test]
    public void OneRawBoundarySegment_IsSolvedAsDegenerateCapsule()
    {
        FixVector2[] vertices =
        {
            new FixVector2((Fix64)0, (Fix64)0),
            new FixVector2((Fix64)5, (Fix64)0),
            new FixVector2((Fix64)5, Fix64.FromRaw(1)),
            new FixVector2((Fix64)10, Fix64.FromRaw(1)),
            new FixVector2((Fix64)10, (Fix64)10),
            new FixVector2((Fix64)0, (Fix64)10),
        };
        LogicStaticCollisionWorld world = CreateWorld(10, 10, vertices, new[] { 0, 6 });
        Fix64 radius = (Fix64)0.4f;

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            new FixVector2((Fix64)5, (Fix64)2),
            new FixVector2(Fix64.Zero, (Fix64)(-3)),
            radius);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            result.Start + result.ResolvedDisplacement,
            radius));
    }

    [Test]
    public void RoundedBoxCornerSweep_DetectsLv3ShortFixedDisplacement()
    {
        FixVector2 boxCenter = new FixVector2(Fix64.FromRaw(263810), Fix64.FromRaw(45880));
        FixVector2 boxHalfExtents = new FixVector2(Fix64.FromRaw(2867), Fix64.FromRaw(2867));
        Fix64 radius = Fix64.FromRaw(1352);
        FixVector2 start = new FixVector2(Fix64.FromRaw(259590), Fix64.FromRaw(42835));
        FixVector2 requested = new FixVector2(Fix64.FromRaw(128), Fix64.Zero);
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                1,
                LogicStaticCollisionObstacleKind.Box,
                boxCenter,
                boxHalfExtents,
                Fix64.Zero),
        };

        LogicStaticCollisionSolveResult result =
            DeterministicStaticCollisionSolver.SolveCircleAgainstObstacles(
                start,
                requested,
                radius,
                obstacles);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        Assert.Greater(result.ContactCount, 0,
            $"start={start} requested={requested} radiusRaw={radius.RawValue} box={boxCenter}/{boxHalfExtents}");
        Assert.Greater(result.ResolvedDisplacement.x.RawValue, 0);
        Assert.Less(result.ResolvedDisplacement.x.RawValue, requested.x.RawValue);
        Assert.Less(result.ResolvedDisplacement.y.RawValue, 0,
            "A horizontal sweep into the lower-left rounded corner must retain the collision tangent instead of stopping at the contact point.");
        Assert.IsTrue(DeterministicStaticCollisionSolver.SolveCircleAgainstObstacles(
            start + result.ResolvedDisplacement,
            FixVector2.Zero,
            radius,
            obstacles).Success);
    }

    [Test]
    public void TangentialWallMotion_PreservesRequestedDistance()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld((Fix64)0, (Fix64)0, (Fix64)10, (Fix64)10);
        Fix64 radius = (Fix64)0.4f;
        FixVector2 start = new FixVector2((Fix64)2, radius);
        FixVector2 requested = new FixVector2((Fix64)3, Fix64.Zero);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            requested,
            radius,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        Assert.AreEqual(requested.x.RawValue, result.ResolvedDisplacement.x.RawValue);
        Assert.AreEqual(0, result.ResolvedDisplacement.y.RawValue);
    }

    [Test]
    public void CornerSweep_ResolvesBothBoundaryContacts()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld((Fix64)0, (Fix64)0, (Fix64)10, (Fix64)10);
        Fix64 radius = (Fix64)0.4f;
        FixVector2 start = new FixVector2((Fix64)2, (Fix64)2);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            new FixVector2((Fix64)(-3), (Fix64)(-3)),
            radius);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        FixVector2 end = start + result.ResolvedDisplacement;
        Assert.GreaterOrEqual(end.x.RawValue, radius.RawValue);
        Assert.GreaterOrEqual(end.y.RawValue, radius.RawValue);
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(world, end, radius));
    }

    [Test]
    public void RuntimeBoxAndAuthoredBoundaryPinch_StopsAtContactManifold()
    {
        Fix64 radius = ReadConfiguredCollisionRadius("MediumUnitCollisionRadius");
        Fix64 boxTop = Fix64.FromRaw(219136);
        Fix64 boundaryTop = boxTop + radius * (Fix64)2 - Fix64.FromRaw(2);
        LogicStaticCollisionWorld world = CreateRectangleWorld(
            Fix64.Zero,
            Fix64.Zero,
            (Fix64)20,
            boundaryTop);
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -49153,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(Fix64.FromRaw(51200), Fix64.FromRaw(210944)),
                new FixVector2(Fix64.FromRaw(8192), Fix64.FromRaw(8192)),
                Fix64.Zero),
        };

        FixVector2 start = new FixVector2(Fix64.FromRaw(59869), boundaryTop - radius);
        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            new FixVector2(Fix64.FromRaw(-2340), Fix64.Zero),
            radius,
            obstacles,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);

        var trace = new System.Text.StringBuilder()
            .Append("failure=").Append(result.Failure)
            .Append(" resolvedRaw=(").Append(result.ResolvedDisplacement.x.RawValue).Append(',')
            .Append(result.ResolvedDisplacement.y.RawValue).AppendLine(")");
        for (int contactIndex = 0;
             contactIndex < result.ContactCount && contactIndex < LogicStaticCollisionSolveResult.MaxRecordedContactCount;
             contactIndex++)
        {
            LogicStaticCollisionContactTrace contact = result.GetContactTrace(contactIndex);
            trace.Append("contact=").Append(contactIndex)
                .Append(" key=").Append(contact.StableKey)
                .Append(" timeRaw=").Append(contact.Time.RawValue)
                .Append(" normalRaw=(").Append(contact.Normal.x.RawValue).Append(',')
                .Append(contact.Normal.y.RawValue).Append(')')
                .Append(" positionRaw=(").Append(contact.Position.x.RawValue).Append(',')
                .Append(contact.Position.y.RawValue).Append(')')
                .Append(" incomingRaw=(").Append(contact.Incoming.x.RawValue).Append(',')
                .Append(contact.Incoming.y.RawValue).Append(')')
                .Append(" leftoverRaw=(").Append(contact.Leftover.x.RawValue).Append(',')
                .Append(contact.Leftover.y.RawValue).AppendLine(")");
        }

        Assert.IsTrue(result.Success, trace.ToString());
        Assert.GreaterOrEqual(result.ContactCount, 1);
        Assert.Less(result.ResolvedDisplacement.x.RawValue, 0);
        Assert.AreEqual(0, result.ResolvedDisplacement.y.RawValue);
        FixVector2 end = start + result.ResolvedDisplacement;
        Assert.IsTrue(
            DeterministicStaticCollisionSolver.IsCircleClear(world, end, radius, obstacles),
            $"start={start} recovered={result.RecoveredStart} resolved={result.ResolvedDisplacement} end={end} " +
            $"contacts={result.ContactCount} firstKey={result.FirstHitStableKey} firstNormal={result.FirstHitNormal}");
    }

    [Test]
    public void Lv2ResearchCenterAndGroundBoundary_OneRawQuantizationGap_AllowsTangentialMotion()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld(
            Fix64.Zero,
            Fix64.Zero,
            (Fix64)20,
            Fix64.FromRaw(222413));
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -49153,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(Fix64.FromRaw(51200), Fix64.FromRaw(210944)),
                new FixVector2(Fix64.FromRaw(8192), Fix64.FromRaw(8192)),
                Fix64.Zero),
        };
        FixVector2 start = new FixVector2(Fix64.FromRaw(48373), Fix64.FromRaw(220773));
        FixVector2 desired = new FixVector2(Fix64.FromRaw(2244), Fix64.FromRaw(-1));
        Fix64 radius = Fix64.FromRaw(1639);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            desired,
            radius,
            obstacles,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);

        Assert.IsTrue(
            result.Success,
            $"failure={result.Failure} start={start} recovered={result.RecoveredStart} resolved={result.ResolvedDisplacement}");
        Assert.AreEqual(start.x.RawValue, result.RecoveredStart.x.RawValue);
        Assert.AreEqual(desired.x.RawValue, result.ResolvedDisplacement.x.RawValue);
        Assert.IsTrue(
            DeterministicStaticCollisionSolver.IsCircleClear(
                world,
                start + result.ResolvedDisplacement,
                radius,
                obstacles));
    }

    [Test]
    public void Lv2ResearchCenterAndGroundBoundary_TwoRawNarrowerThanDiameter_RemainsBlocked()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld(
            Fix64.Zero,
            Fix64.Zero,
            (Fix64)20,
            Fix64.FromRaw(222412));
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -49153,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(Fix64.FromRaw(51200), Fix64.FromRaw(210944)),
                new FixVector2(Fix64.FromRaw(8192), Fix64.FromRaw(8192)),
                Fix64.Zero),
        };

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            new FixVector2(Fix64.FromRaw(48373), Fix64.FromRaw(220773)),
            new FixVector2(Fix64.FromRaw(2244), Fix64.FromRaw(-1)),
            Fix64.FromRaw(1639),
            obstacles,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(LogicStaticCollisionFailure.StartOverlapUnresolved, result.Failure);
    }

    [Test]
    public void Lv2ResearchCenterRoundedCorner_SubSquaredRawDiagonal_RecoversWithoutZeroNormal()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld(
            Fix64.Zero,
            Fix64.Zero,
            (Fix64)20,
            Fix64.FromRaw(222413));
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -49153,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(Fix64.FromRaw(51200), Fix64.FromRaw(210944)),
                new FixVector2(Fix64.FromRaw(8192), Fix64.FromRaw(8192)),
                Fix64.Zero),
        };
        FixVector2 start = new FixVector2(Fix64.FromRaw(59443), Fix64.FromRaw(219186));
        Fix64 radius = Fix64.FromRaw(1639);

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            FixVector2.Zero,
            radius,
            obstacles,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);

        Assert.IsTrue(result.Success, result.Failure.ToString());
        Assert.IsTrue(result.StartedOverlapping);
        Assert.AreNotEqual(start, result.RecoveredStart);
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            start + result.ResolvedDisplacement,
            radius,
            obstacles));
    }

    [Test]
    public void Lv2ResearchCenterRoundedCorner_OneRawPinchPreservesGapEntryMotion()
    {
        LogicStaticCollisionWorld world = CreateRectangleWorld(
            Fix64.Zero,
            Fix64.Zero,
            (Fix64)20,
            Fix64.FromRaw(222413));
        var obstacles = new[]
        {
            new LogicStaticCollisionObstacle(
                -49153,
                LogicStaticCollisionObstacleKind.Box,
                new FixVector2(Fix64.FromRaw(51200), Fix64.FromRaw(210944)),
                new FixVector2(Fix64.FromRaw(8192), Fix64.FromRaw(8192)),
                Fix64.Zero),
        };
        FixVector2 start = new FixVector2(Fix64.FromRaw(59472), Fix64.FromRaw(220773));
        FixVector2 desired = new FixVector2(Fix64.FromRaw(-1588), Fix64.FromRaw(1587));
        Fix64 radius = ReadConfiguredCollisionRadius("MediumUnitCollisionRadius");

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            start,
            desired,
            radius,
            obstacles,
            LogicStaticCollisionSlideMode.PreserveTangentialComponent);
        var trace = new System.Text.StringBuilder()
            .Append("startRaw=(").Append(start.x.RawValue).Append(',').Append(start.y.RawValue).Append(") ")
            .Append("recoveredRaw=(").Append(result.RecoveredStart.x.RawValue).Append(',')
            .Append(result.RecoveredStart.y.RawValue).Append(") ")
            .Append("resolvedRaw=(").Append(result.ResolvedDisplacement.x.RawValue).Append(',')
            .Append(result.ResolvedDisplacement.y.RawValue).AppendLine(")");
        for (int contactIndex = 0;
             contactIndex < result.ContactCount && contactIndex < LogicStaticCollisionSolveResult.MaxRecordedContactCount;
             contactIndex++)
        {
            LogicStaticCollisionContactTrace contact = result.GetContactTrace(contactIndex);
            trace.Append("contact=").Append(contactIndex)
                .Append(" key=").Append(contact.StableKey)
                .Append(" timeRaw=").Append(contact.Time.RawValue)
                .Append(" normalRaw=(").Append(contact.Normal.x.RawValue).Append(',')
                .Append(contact.Normal.y.RawValue).Append(')')
                .Append(" positionRaw=(").Append(contact.Position.x.RawValue).Append(',')
                .Append(contact.Position.y.RawValue).Append(')')
                .Append(" incomingRaw=(").Append(contact.Incoming.x.RawValue).Append(',')
                .Append(contact.Incoming.y.RawValue).Append(')')
                .Append(" leftoverRaw=(").Append(contact.Leftover.x.RawValue).Append(',')
                .Append(contact.Leftover.y.RawValue).AppendLine(")");
        }

        Assert.IsTrue(
            result.Success,
            $"failure={result.Failure}\n{trace}");
        Assert.IsFalse(result.StartedOverlapping, $"one-raw pinch must not invent overlap recovery; recovered={result.RecoveredStart}");
        Assert.AreEqual(start, result.RecoveredStart, "clear one-raw pinch start must remain unchanged");
        Assert.AreEqual(
            desired.x.RawValue,
            result.ResolvedDisplacement.x.RawValue,
            trace.ToString());
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            start + result.ResolvedDisplacement,
            radius,
            obstacles), $"final={start + result.ResolvedDisplacement}");
    }

    [Test]
    public void HoleAndOutsideStart_AreNotLegalGround()
    {
        FixVector2[] vertices =
        {
            new FixVector2((Fix64)0, (Fix64)0),
            new FixVector2((Fix64)10, (Fix64)0),
            new FixVector2((Fix64)10, (Fix64)10),
            new FixVector2((Fix64)0, (Fix64)10),
            new FixVector2((Fix64)4, (Fix64)4),
            new FixVector2((Fix64)6, (Fix64)4),
            new FixVector2((Fix64)6, (Fix64)6),
            new FixVector2((Fix64)4, (Fix64)6),
        };
        LogicStaticCollisionWorld world = CreateWorld(10, 10, vertices, new[] { 0, 4, 8 });
        Fix64 radius = (Fix64)0.2f;

        Assert.IsFalse(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            new FixVector2((Fix64)5, (Fix64)5),
            radius));
        Assert.IsFalse(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            new FixVector2((Fix64)(-1), (Fix64)5),
            radius));

        LogicStaticCollisionSolveResult result = DeterministicStaticCollisionSolver.SolveCircle(
            world,
            new FixVector2((Fix64)(-1), (Fix64)5),
            new FixVector2((Fix64)1, Fix64.Zero),
            radius);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LogicStaticCollisionFailure.StartOverlapUnresolved, result.Failure);
    }

    private static LogicStaticCollisionWorld CreateLvTestGapWorld()
    {
        return CreateRectangleWorld((Fix64)55, GroundBoundaryZ, (Fix64)75, (Fix64)50);
    }

    private static LogicStaticCollisionWorld CreateRotatedLvTestGapWorld(int quarterTurns, FixVector2 pivot)
    {
        FixVector2[] vertices =
        {
            RotateQuarterTurns(new FixVector2((Fix64)55, GroundBoundaryZ), pivot, quarterTurns),
            RotateQuarterTurns(new FixVector2((Fix64)75, GroundBoundaryZ), pivot, quarterTurns),
            RotateQuarterTurns(new FixVector2((Fix64)75, (Fix64)50), pivot, quarterTurns),
            RotateQuarterTurns(new FixVector2((Fix64)55, (Fix64)50), pivot, quarterTurns),
        };
        Fix64 minX = vertices[0].x;
        Fix64 minY = vertices[0].y;
        Fix64 maxX = vertices[0].x;
        Fix64 maxY = vertices[0].y;
        for (int i = 1; i < vertices.Length; i++)
        {
            minX = Fix64.Min(minX, vertices[i].x);
            minY = Fix64.Min(minY, vertices[i].y);
            maxX = Fix64.Max(maxX, vertices[i].x);
            maxY = Fix64.Max(maxY, vertices[i].y);
        }
        int width = Math.Max(1, (int)Fix64.Ceiling(maxX - minX));
        int height = Math.Max(1, (int)Fix64.Ceiling(maxY - minY));
        return CreateWorld(width, height, vertices, new[] { 0, 4 }, new FixVector2(minX, minY));
    }

    private static FixVector2 RotateQuarterTurns(FixVector2 value, FixVector2 pivot, int quarterTurns)
    {
        FixVector2 offset = value - pivot;
        switch (quarterTurns & 3)
        {
            case 0: return value;
            case 1: return pivot + new FixVector2(-offset.y, offset.x);
            case 2: return pivot - offset;
            default: return pivot + new FixVector2(offset.y, -offset.x);
        }
    }

    private static LogicStaticCollisionWorld CreateRectangleWorld(Fix64 minX, Fix64 minY, Fix64 maxX, Fix64 maxY)
    {
        FixVector2[] vertices =
        {
            new FixVector2(minX, minY),
            new FixVector2(maxX, minY),
            new FixVector2(maxX, maxY),
            new FixVector2(minX, maxY),
        };
        int width = Math.Max(1, (int)(maxX - minX));
        int height = Math.Max(1, (int)(maxY - minY));
        return CreateWorld(width, height, vertices, new[] { 0, 4 }, new FixVector2(minX, minY));
    }

    private static LogicStaticCollisionWorld CreateWorld(
        int width,
        int height,
        FixVector2[] vertices,
        int[] pathStarts,
        FixVector2 origin = default)
    {
        bool[] walkable = new bool[checked(width * height)];
        Array.Fill(walkable, true);
        return new LogicStaticCollisionWorld(
            0,
            1,
            width,
            height,
            checked(Fix64.One.RawValue << 20),
            checked(origin.x.RawValue << 20),
            checked(origin.y.RawValue << 20),
            walkable,
            null,
            vertices,
            pathStarts);
    }

    private static FixVector2 ResolveCameraRelativeInput(Vector2 deviceInput)
    {
        GameObject cameraObject = new GameObject("StaticCollisionInputCamera");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            return InputDirTranslator.TranslateAndQuantize(deviceInput, camera);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static Fix64 ReadConfiguredCollisionRadius(string configKey)
    {
        string configPath = Path.Combine(Application.dataPath, "AAAGame", "Config", "GameConfig.txt");
        string[] lines = File.ReadAllLines(configPath);
        for (int i = 0; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length >= 4 && string.Equals(columns[1], configKey, StringComparison.Ordinal))
                return FixedConfigReader.ParseFixedConfigText(configKey, columns[3]);
        }

        throw new InvalidOperationException($"Required fixed config '{configKey}' is missing from {configPath}.");
    }
}
