using NUnit.Framework;

public sealed class DeterministicStaticCollisionTests
{
    [Test]
    public void ClearPath_PreservesRequestedDisplacement()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5);

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 1f, 0.25f, 0.2f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 1f, 0.25f);
        Assert.AreEqual(0, result.ContactCount);
    }

    [Test]
    public void HeadOnWall_StopsAtExpandedCellBoundary()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5, (2, 1));

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 1f, 0f, 0.25f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 0.25f, 0f, 0.002f);
        Assert.AreEqual(1, result.ContactCount);
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            result.Start + result.ResolvedDisplacement,
            (Fix64)0.25f));
    }

    [Test]
    public void DiagonalWallHit_SlidesAlongWallWithoutLosingTangentialDistance()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5, (2, 0), (2, 1), (2, 2), (2, 3), (2, 4));

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 1f, 1f, 0.25f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 0.25f, 1f, 0.003f);
        Assert.GreaterOrEqual(result.ContactCount, 1);
    }

    [Test]
    public void BlockedCorner_UsesStableAxisTieBreakAndStopsBothAxes()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5, (2, 2));

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 1f, 1f, 0.25f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 0.25f, 0.25f, 0.003f);
        Assert.AreEqual(2, result.ContactCount);
    }

    [Test]
    public void HighSpeedMove_DoesNotTunnelThroughSingleCellWall()
    {
        LogicStaticCollisionWorld world = CreateWorld(8, 4, (3, 0), (3, 1), (3, 2), (3, 3));

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 5f, 0f, 0.25f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 1.25f, 0f, 0.003f);
    }

    [Test]
    public void WorldBoundary_StopsCircleAndKeepsTangent()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5);

        LogicStaticCollisionSolveResult result = Solve(world, 0.5f, 1.5f, -1f, 0.75f, 0.25f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, -0.25f, 0.75f, 0.003f);
    }

    [Test]
    public void RadiusExactlyHalfWorldHeight_RemainsSolvable()
    {
        LogicStaticCollisionWorld world = CreateWorld(4, 1);

        LogicStaticCollisionSolveResult result = Solve(world, 0.5f, 0.5f, 1f, 0f, 0.5f);

        Assert.IsTrue(result.Success);
        AssertVector(result.ResolvedDisplacement, 1f, 0f);
    }

    [Test]
    public void StartInsideBlockedCell_RecoversUsingStableMinimumPenetration()
    {
        LogicStaticCollisionWorld world = CreateWorld(5, 5, (1, 1));

        LogicStaticCollisionSolveResult result = Solve(world, 1.5f, 1.5f, 0f, 0f, 0.25f);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.StartedOverlapping);
        Assert.Less((float)result.ResolvedDisplacement.x, -0.74f);
        Assert.That((float)result.ResolvedDisplacement.y, Is.EqualTo(0f).Within(0.002f));
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            result.Start + result.ResolvedDisplacement,
            (Fix64)0.25f));
    }

    [Test]
    public void StartInsideSolidBlockedCluster_RecoversToNearestClearAxisDeterministically()
    {
        LogicStaticCollisionWorld world = CreateWorld(
            7,
            7,
            (1, 1), (2, 1), (3, 1),
            (1, 2), (2, 2), (3, 2),
            (1, 3), (2, 3), (3, 3));

        LogicStaticCollisionSolveResult first = Solve(world, 2.5f, 2.5f, 0f, 0f, 0.25f);
        LogicStaticCollisionSolveResult second = Solve(world, 2.5f, 2.5f, 0f, 0f, 0.25f);

        Assert.IsFalse(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            new FixVector2((Fix64)2.5f, (Fix64)2.5f),
            (Fix64)0.25f));
        Assert.IsTrue(first.Success);
        Assert.IsTrue(first.StartedOverlapping);
        AssertVector(first.ResolvedDisplacement, -1.75f, 0f, 0.002f);
        Assert.IsTrue(DeterministicStaticCollisionSolver.IsCircleClear(
            world,
            first.Start + first.ResolvedDisplacement,
            (Fix64)0.25f));
        Assert.AreEqual(first.ResolvedDisplacement.x.RawValue, second.ResolvedDisplacement.x.RawValue);
        Assert.AreEqual(first.ResolvedDisplacement.y.RawValue, second.ResolvedDisplacement.y.RawValue);
    }

    [Test]
    public void RepeatedSolve_ProducesIdenticalRawValues()
    {
        LogicStaticCollisionWorld world = CreateWorld(6, 6, (3, 1), (3, 2), (3, 3));

        LogicStaticCollisionSolveResult first = Solve(world, 1.25f, 2.25f, 3.75f, 1.125f, 0.3f);
        LogicStaticCollisionSolveResult second = Solve(world, 1.25f, 2.25f, 3.75f, 1.125f, 0.3f);

        Assert.AreEqual(first.Success, second.Success);
        Assert.AreEqual(first.Failure, second.Failure);
        Assert.AreEqual(first.ContactCount, second.ContactCount);
        Assert.AreEqual(first.ResolvedDisplacement.x.RawValue, second.ResolvedDisplacement.x.RawValue);
        Assert.AreEqual(first.ResolvedDisplacement.y.RawValue, second.ResolvedDisplacement.y.RawValue);
    }

    [Test]
    public void LongGrid_CellBoundaryDoesNotAccumulateFix64CellSizeError()
    {
        const int width = 700;
        bool[] mask = CreateMask(width, 3);
        mask[636 + width] = false;
        var world = new LogicStaticCollisionWorld(
            0,
            1,
            width,
            3,
            0.089999996f,
            new UnityEngine.Vector3(12.78f, 0f, 0f),
            mask);

        LogicStaticCollisionSolveResult result = Solve(world, 69.9f, 0.135f, 0.5f, 0f, 0.02f);

        Assert.IsTrue(result.Success);
        Assert.That((float)result.ResolvedDisplacement.x, Is.EqualTo(0.1f).Within(0.003f));
        Assert.That((float)result.ResolvedDisplacement.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void World_ClonesWalkableMaskForShadowIsolation()
    {
        bool[] mask = CreateMask(3, 3);
        var world = new LogicStaticCollisionWorld(0, 1, 3, 3, (Fix64)1, FixVector2.Zero, mask);

        mask[4] = false;

        Assert.IsTrue(world.IsWalkable(1, 1));
    }

    [Test]
    public void LogicTransform_WithMotionUpdatesFacingFromFixedVelocity()
    {
        var transform = new LogicTransform(FixVector2.Zero, FixVector2.Zero, new FixVector2(1, 0));

        LogicTransform moved = transform.WithMotion(new FixVector2(2, 3), new FixVector2(0, 2));

        Assert.AreEqual(new FixVector2(2, 3), moved.Position);
        Assert.AreEqual(new FixVector2(0, 2), moved.Velocity);
        Assert.AreEqual(new FixVector2(0, 1), moved.Facing);
    }

    private static LogicStaticCollisionSolveResult Solve(
        LogicStaticCollisionWorld world,
        float startX,
        float startY,
        float moveX,
        float moveY,
        float radius)
    {
        return DeterministicStaticCollisionSolver.SolveCircle(
            world,
            new FixVector2((Fix64)startX, (Fix64)startY),
            new FixVector2((Fix64)moveX, (Fix64)moveY),
            (Fix64)radius);
    }

    private static LogicStaticCollisionWorld CreateWorld(
        int width,
        int height,
        params (int x, int y)[] blockedCells)
    {
        bool[] mask = CreateMask(width, height);
        for (int i = 0; i < blockedCells.Length; i++)
        {
            (int x, int y) cell = blockedCells[i];
            mask[cell.x + cell.y * width] = false;
        }

        return new LogicStaticCollisionWorld(0, 1, width, height, (Fix64)1, FixVector2.Zero, mask);
    }

    private static bool[] CreateMask(int width, int height)
    {
        var mask = new bool[width * height];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = true;
        return mask;
    }

    private static void AssertVector(
        FixVector2 actual,
        float expectedX,
        float expectedY,
        float tolerance = 0.001f)
    {
        Assert.That((float)actual.x, Is.EqualTo(expectedX).Within(tolerance));
        Assert.That((float)actual.y, Is.EqualTo(expectedY).Within(tolerance));
    }
}
