using System;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class DeterministicStaticCollisionGeometryTests
{
    private static readonly Fix64 HeroRadius = Fix64.FromRaw(1623);
    private static readonly Fix64 GroundBoundaryZ = Fix64.FromRaw(154419);

    [Test]
    public void ExactPointEightGap_AllowsHeroCircleToPassAtFullRadius()
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
                HeroRadius,
                obstacles,
                LogicStaticCollisionSlideMode.PreserveRemainingDistance);
            Assert.IsTrue(result.Success, $"tick={tick} failure={result.Failure} position={position}");
            position += result.ResolvedDisplacement;
            Assert.IsTrue(
                DeterministicStaticCollisionSolver.IsCircleClear(world, position, HeroRadius, obstacles),
                $"tick={tick} start={tickStart} recovered={result.RecoveredStart} position={position} " +
                $"resolved={result.ResolvedDisplacement} startedOverlapping={result.StartedOverlapping} contacts={result.ContactCount} " +
                $"firstKey={result.FirstHitStableKey} firstNormal={result.FirstHitNormal}");
        }

        Assert.Greater(position.x.RawValue, ((Fix64)66.5f).RawValue, $"position={position}");
        Assert.GreaterOrEqual(position.y.RawValue, (GroundBoundaryZ + HeroRadius).RawValue);
        Assert.LessOrEqual(position.y.RawValue, ((Fix64)38.5f - HeroRadius).RawValue);
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
}
