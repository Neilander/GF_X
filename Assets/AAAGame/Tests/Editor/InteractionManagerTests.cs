using NUnit.Framework;

[TestFixture]
public sealed class InteractionManagerTests
{
    [Test]
    public void FixedScore_UsesBuildingSurfaceDistanceAtNegativeCoordinates()
    {
        LogicEntityFrameState actor = CreateState(
            1,
            new FixVector2((Fix64)(-5), (Fix64)(-2)),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(new FixVector2((Fix64)(-5), (Fix64)(-2)), Fix64.Zero));
        LogicEntityFrameState target = CreateState(
            2,
            new FixVector2((Fix64)(-2), (Fix64)(-2)),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.AxisAlignedBox(
                new FixVector2((Fix64)(-2), (Fix64)(-2)),
                new FixVector2(Fix64.One, Fix64.One)));

        bool valid = InteractionManager.TryComputeScore(
            actor,
            target,
            (Fix64)4,
            Fix64.One,
            Fix64.Zero,
            out Fix64 score);

        Assert.IsTrue(valid);
        Assert.AreEqual(((Fix64)1 / 2).RawValue, score.RawValue);
    }

    [Test]
    public void FixedScore_DistinguishesFacingWithoutFloatAngle()
    {
        LogicEntityFrameState facing = CreateState(
            1,
            FixVector2.Zero,
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(FixVector2.Zero, Fix64.Zero));
        LogicEntityFrameState target = CreateState(
            2,
            new FixVector2((Fix64)2, Fix64.Zero),
            new FixVector2(Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(new FixVector2((Fix64)2, Fix64.Zero), Fix64.Zero));

        InteractionManager.TryComputeScore(
            facing,
            target,
            (Fix64)4,
            Fix64.Zero,
            Fix64.One,
            out Fix64 facingScore);
        LogicEntityFrameState away = CreateState(
            1,
            FixVector2.Zero,
            new FixVector2(-Fix64.One, Fix64.Zero),
            LogicCombatShape.Circle(FixVector2.Zero, Fix64.Zero));
        InteractionManager.TryComputeScore(
            away,
            target,
            (Fix64)4,
            Fix64.Zero,
            Fix64.One,
            out Fix64 awayScore);

        Assert.AreEqual(Fix64.One.RawValue, facingScore.RawValue);
        Assert.AreEqual(Fix64.Zero.RawValue, awayScore.RawValue);
    }

    [Test]
    public void EqualScores_SelectSmallerLogicEntityId()
    {
        Assert.IsTrue(InteractionManager.IsBetterCandidate(
            (Fix64)1,
            new LogicEntityId(4),
            true,
            (Fix64)1,
            new LogicEntityId(9)));
        Assert.IsFalse(InteractionManager.IsBetterCandidate(
            (Fix64)1,
            new LogicEntityId(12),
            true,
            (Fix64)1,
            new LogicEntityId(9)));
    }

    private static LogicEntityFrameState CreateState(
        int id,
        FixVector2 position,
        FixVector2 forward,
        LogicCombatShape shape)
    {
        return new LogicEntityFrameState(
            new LogicEntityId(id),
            position,
            forward,
            Fix64.Zero,
            shape,
            SideType.PlayerSide,
            true);
    }
}
