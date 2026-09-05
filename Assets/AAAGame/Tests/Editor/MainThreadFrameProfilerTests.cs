using NUnit.Framework;
using UnityGameFramework.Runtime;

public sealed class MainThreadFrameProfilerTests
{
    [Test]
    public void Logging_IsDisabledByDefault()
    {
        Assert.IsFalse(MainThreadFrameProfiler.LoggingEnabled);
    }

    [Test]
    public void ScopeCapture_PreservesEachInvocationAndListenerWithinItsLogicTick()
    {
        MainThreadFrameProfiler.ResetPerformanceWindow();
        MainThreadFrameProfiler.LoggingEnabled = true;
        MainThreadFrameProfiler.BeginScopeRecordCapture();
        try
        {
            MainThreadFrameProfiler.BeginLogicTick(71);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStableGoal, 100);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStableGoal, 20);
            MainThreadFrameProfiler.RecordLogicFrameListener(typeof(MainThreadFrameProfilerTests), 150);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.LogicFrameTick, 200);
            MainThreadFrameProfiler.RecordLogicTickDuration(200);
            MainThreadFrameProfiler.BeginLogicTick(72);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.FlowPrepareStableGoal, 10);
            MainThreadFrameProfiler.Record(MainThreadPerfScope.LogicFrameTick, 30);
            MainThreadFrameProfiler.RecordLogicTickDuration(30);
            MainThreadFrameProfiler.EndScopeRecordCapture();

            var records = MainThreadFrameProfiler.CapturedScopeRecords;
            Assert.AreEqual(6, records.Count);
            Assert.AreEqual(100, records[0].Ticks);
            Assert.AreEqual(20, records[1].Ticks);
            Assert.AreEqual(71ul, records[1].LogicFrame);
            Assert.AreEqual(typeof(MainThreadFrameProfilerTests), records[2].ListenerType);
            Assert.AreEqual(72ul, records[4].LogicFrame);
            Assert.AreEqual(10, records[4].Ticks);
            Assert.GreaterOrEqual(records[5].RecordedAt, records[0].RecordedAt);
        }
        finally
        {
            MainThreadFrameProfiler.RecordLogicTickDuration(1);
            MainThreadFrameProfiler.EndScopeRecordCapture();
            MainThreadFrameProfiler.LoggingEnabled = false;
            MainThreadFrameProfiler.ResetPerformanceWindow();
        }
    }

    [Test]
    public void ScopeCapture_RejectsChangingCaptureWithinAnActiveTick()
    {
        MainThreadFrameProfiler.ResetPerformanceWindow();
        MainThreadFrameProfiler.LoggingEnabled = true;
        try
        {
            MainThreadFrameProfiler.BeginLogicTick(9);
            Assert.Throws<System.InvalidOperationException>(() => MainThreadFrameProfiler.BeginScopeRecordCapture());
            Assert.Throws<System.InvalidOperationException>(() => MainThreadFrameProfiler.EndScopeRecordCapture());
        }
        finally
        {
            MainThreadFrameProfiler.RecordLogicTickDuration(1);
            MainThreadFrameProfiler.EndScopeRecordCapture();
            MainThreadFrameProfiler.LoggingEnabled = false;
            MainThreadFrameProfiler.ResetPerformanceWindow();
        }
    }
}
