using System;
using NUnit.Framework;

[TestFixture]
public class DeterministicAgentCollisionTests
{
    private const uint UnitCategory = 1u;
    private const uint UnitMask = 1u;

    [Test]
    public void EqualMassPair_SplitsCorrectionEvenly()
    {
        LogicAgentCollisionSolveResult result = Solve(
            Body(1, 0f, 0f, 1f, 1f),
            Body(2, 1f, 0f, 1f, 1f));

        Assert.IsTrue(result.Success);
        Assert.AreEqual((Fix64)(-0.5f), result.States[0].Position.x);
        Assert.AreEqual((Fix64)1.5f, result.States[1].Position.x);
        Assert.AreEqual(Fix64.Zero, result.States[0].Position.y);
        Assert.AreEqual(Fix64.Zero, result.States[1].Position.y);
    }

    [Test]
    public void ImmovableBody_MakesMovableBodyTakeFullCorrection()
    {
        LogicAgentCollisionSolveResult result = Solve(
            Body(1, 0f, 0f, 1f, 0f),
            Body(2, 1f, 0f, 1f, 1f));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(Fix64.Zero, result.States[0].Position.x);
        Assert.AreEqual((Fix64)2f, result.States[1].Position.x);
    }

    [Test]
    public void InputOrder_DoesNotChangeRawResult()
    {
        LogicAgentCollisionBody first = Body(10, 0f, 0f, 1f, 1f);
        LogicAgentCollisionBody second = Body(20, 1f, 0f, 1f, 1f);
        LogicAgentCollisionBody third = Body(30, 2f, 0f, 1f, 1f);

        LogicAgentCollisionSolveResult forward = DeterministicAgentCollisionSolver.Solve(
            new[] { first, second, third },
            8,
            Fix64.FromRaw(1));
        LogicAgentCollisionSolveResult reversed = DeterministicAgentCollisionSolver.Solve(
            new[] { third, second, first },
            8,
            Fix64.FromRaw(1));

        Assert.AreEqual(forward.States.Count, reversed.States.Count);
        for (int i = 0; i < forward.States.Count; i++)
        {
            Assert.AreEqual(forward.States[i].EntityId, reversed.States[i].EntityId);
            Assert.AreEqual(forward.States[i].Position.x.RawValue, reversed.States[i].Position.x.RawValue);
            Assert.AreEqual(forward.States[i].Position.y.RawValue, reversed.States[i].Position.y.RawValue);
        }
        Assert.AreEqual(forward.ResidualOverlapCount, reversed.ResidualOverlapCount);
        Assert.AreEqual(forward.MaxResidualPenetration.RawValue, reversed.MaxResidualPenetration.RawValue);
    }

    [Test]
    public void ExactOverlap_UsesStablePairNormal()
    {
        LogicAgentCollisionSolveResult firstRun = Solve(
            Body(11, 0f, 0f, 1f, 1f),
            Body(29, 0f, 0f, 1f, 1f));
        LogicAgentCollisionSolveResult secondRun = DeterministicAgentCollisionSolver.Solve(
            new[]
            {
                Body(29, 0f, 0f, 1f, 1f),
                Body(11, 0f, 0f, 1f, 1f),
            },
            1,
            Fix64.Zero);

        Assert.IsTrue(firstRun.Success);
        Assert.AreEqual(firstRun.States[0].Position, secondRun.States[0].Position);
        Assert.AreEqual(firstRun.States[1].Position, secondRun.States[1].Position);
        Assert.AreEqual((Fix64)2f, FixVector2.Distance(firstRun.States[0].Position, firstRun.States[1].Position));
    }

    [Test]
    public void CollisionMask_DisablesPair()
    {
        var first = new LogicAgentCollisionBody(
            new LogicEntityId(1),
            FixVector2.Zero,
            (Fix64)1f,
            Fix64.One,
            1u,
            1u);
        var second = new LogicAgentCollisionBody(
            new LogicEntityId(2),
            FixVector2.Zero,
            (Fix64)1f,
            Fix64.One,
            2u,
            2u);

        LogicAgentCollisionSolveResult result = DeterministicAgentCollisionSolver.Solve(
            new[] { first, second },
            4,
            Fix64.Zero);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.CandidatePairCount);
        Assert.AreEqual(FixVector2.Zero, result.States[0].Position);
        Assert.AreEqual(FixVector2.Zero, result.States[1].Position);
    }

    [Test]
    public void FarSeparatedBodies_AreExcludedByStableBroadphase()
    {
        LogicAgentCollisionSolveResult result = Solve(
            Body(1, -100f, 0f, 0.5f, 1f),
            Body(2, 100f, 0f, 0.5f, 1f));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.CandidatePairCount);
        Assert.AreEqual((Fix64)(-100f), result.States[0].Position.x);
        Assert.AreEqual((Fix64)100f, result.States[1].Position.x);
    }

    [Test]
    public void OverlapAcrossNegativeCellBoundary_IsStillResolved()
    {
        LogicAgentCollisionSolveResult result = Solve(
            Body(1, -0.1f, 0f, 0.6f, 1f),
            Body(2, 0.9f, 0f, 0.6f, 1f));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.CandidatePairCount);
        Fix64 expectedDistance = (Fix64)0.6f * (Fix64)2;
        Fix64 actualDistance = FixVector2.Distance(result.States[0].Position, result.States[1].Position);
        Assert.LessOrEqual(Math.Abs(expectedDistance.RawValue - actualDistance.RawValue), 1L);
    }

    [Test]
    public void TwoImmovableBodies_ReportResidualOverlap()
    {
        LogicAgentCollisionSolveResult result = Solve(
            Body(1, 0f, 0f, 1f, 0f),
            Body(2, 1f, 0f, 1f, 0f));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, result.ResidualOverlapCount);
        Assert.AreEqual((Fix64)1f, result.MaxResidualPenetration);
    }

    [Test]
    public void DuplicateId_IsRejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Solve(
                Body(3, 0f, 0f, 1f, 1f),
                Body(3, 1f, 0f, 1f, 1f)));

        StringAssert.Contains("duplicate entity id 3", exception.Message);
    }

    private static LogicAgentCollisionSolveResult Solve(params LogicAgentCollisionBody[] bodies)
    {
        return DeterministicAgentCollisionSolver.Solve(bodies, 1, Fix64.Zero);
    }

    private static LogicAgentCollisionBody Body(
        int id,
        float x,
        float y,
        float radius,
        float inverseMass)
    {
        return new LogicAgentCollisionBody(
            new LogicEntityId(id),
            new FixVector2((Fix64)x, (Fix64)y),
            (Fix64)radius,
            (Fix64)inverseMass,
            UnitCategory,
            UnitMask);
    }
}
