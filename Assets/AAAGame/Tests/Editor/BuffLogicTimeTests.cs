using System.Collections.Generic;
using GameFramework;
using NUnit.Framework;

[TestFixture]
public sealed class BuffLogicTimeTests
{
    [Test]
    public void OneSecondBuff_ExpiresOnlyFromFixedLogicTicks()
    {
        BuffData buff = BuffData.Create("test_fixed_duration", 1f, false, 1, new List<BuffCallback>());
        try
        {
            for (int i = 0; i < 30; i++)
                Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));

            Assert.IsTrue(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }

    [Test]
    public void PermanentBuff_DoesNotConsumeLogicTime()
    {
        BuffData buff = BuffData.Create("test_forever", 1f, true, 1, new List<BuffCallback>());
        try
        {
            Fix64 before = buff.remainingTime;
            Assert.IsFalse(buff.AdvanceLogicTime(LogicFrameRuntime.FixedDeltaTime));
            Assert.AreEqual(before, buff.remainingTime);
        }
        finally
        {
            ReferencePool.Release(buff);
        }
    }
}
