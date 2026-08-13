using System;
using NUnit.Framework;

[TestFixture]
public class DeterministicProgressAccumulatorTests
{
    [Test]
    public void ThirtyPercent_UsesProgressWithoutRandomCalls()
    {
        var accumulator = new DeterministicProgressAccumulator();
        Fix64 increment = (Fix64)30 / (Fix64)100;

        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        Assert.IsTrue(accumulator.AdvanceAndConsume(increment));
    }

    [Test]
    public void SnapshotRestore_ReplaysNextProgressResult()
    {
        var accumulator = new DeterministicProgressAccumulator();
        Fix64 increment = (Fix64)40 / (Fix64)100;
        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        DeterministicProgressSnapshot snapshot = accumulator.CaptureSnapshot();

        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        Assert.IsTrue(accumulator.AdvanceAndConsume(increment));

        accumulator.RestoreSnapshot(snapshot);
        Assert.IsFalse(accumulator.AdvanceAndConsume(increment));
        Assert.IsTrue(accumulator.AdvanceAndConsume(increment));
    }

    [Test]
    public void BlindBuff_PreservesProgressAcrossSnapshotRestore()
    {
        var blind = new BlindAttackMissBuff((Fix64)50);
        Assert.IsFalse(blind.TryConsumeMiss());
        DeterministicProgressSnapshot snapshot = blind.CaptureProgressSnapshot();
        Assert.IsTrue(blind.TryConsumeMiss());

        blind.RestoreProgressSnapshot(snapshot);
        Assert.IsTrue(blind.TryConsumeMiss());
    }

    [Test]
    public void SeededRandomEntry_IsReservedButDisabled()
    {
        Assert.IsFalse(DeterministicRandomService.GameplayRandomEnabled);
        Assert.Throws<InvalidOperationException>(
            () => DeterministicRandomService.NextUInt(DeterministicRandomStreamId.Combat, "test"));
    }

    [Test]
    public void PersistentIdAllocatorSnapshot_ReplaysSameBuildingId()
    {
        LogicPersistentIdAllocator.BeginTimeline();
        try
        {
            string first = LogicPersistentIdAllocator.AllocateBuildingInstanceId();
            LogicPersistentIdAllocatorSnapshot snapshot = LogicPersistentIdAllocator.CaptureSnapshot();
            string second = LogicPersistentIdAllocator.AllocateBuildingInstanceId();

            LogicPersistentIdAllocator.RestoreSnapshot(snapshot);
            string replayedSecond = LogicPersistentIdAllocator.AllocateBuildingInstanceId();

            Assert.AreEqual("building-0000000001", first);
            Assert.AreEqual(second, replayedSecond);
        }
        finally
        {
            LogicPersistentIdAllocator.EndTimeline();
        }
    }
}
