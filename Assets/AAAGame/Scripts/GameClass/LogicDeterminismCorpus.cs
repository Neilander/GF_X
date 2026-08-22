using System;

public readonly struct LogicDeterminismCorpusResult
{
    public LogicDeterminismCorpusResult(
        int protocolVersion,
        string contentVersion,
        int eventCount,
        uint inputChecksum,
        long worldMoveXRaw,
        long worldMoveYRaw,
        ulong inputHash,
        ulong timeControlHash,
        ulong fullHash)
    {
        ProtocolVersion = protocolVersion;
        ContentVersion = contentVersion;
        EventCount = eventCount;
        InputChecksum = inputChecksum;
        WorldMoveXRaw = worldMoveXRaw;
        WorldMoveYRaw = worldMoveYRaw;
        InputHash = inputHash;
        TimeControlHash = timeControlHash;
        FullHash = fullHash;
    }

    public int ProtocolVersion { get; }
    public string ContentVersion { get; }
    public int EventCount { get; }
    public uint InputChecksum { get; }
    public long WorldMoveXRaw { get; }
    public long WorldMoveYRaw { get; }
    public ulong InputHash { get; }
    public ulong TimeControlHash { get; }
    public ulong FullHash { get; }
}

public static class LogicDeterminismCorpus
{
    public const string CorpusVersion = "v102";
    public const int GoldenProtocolVersion = 102;
    public const string GoldenContentVersion = "Avenge-30Hz-v102";
    public const int GoldenEventCount = 4;
    public const uint GoldenInputChecksum = 565150261u;
    public const ulong GoldenInputHash = 13863065662157724554ul;
    public const ulong GoldenTimeControlHash = 4660981641562439902ul;
    public const ulong GoldenFullHash = 1464893562985008626ul;

    private const ulong GameplayPayload = 0x123456789ABCDEF0UL;

    public static LogicDeterminismCorpusResult EvaluateV102()
    {
        if (LogicTimeControlService.IsActive)
        {
            throw new InvalidOperationException(
                "LogicDeterminismCorpus.EvaluateV102 failed: LogicTimeControlService must be inactive.");
        }

        LogicInputFrame frame = BuildInputFrame();
        LogicTimeControlSnapshot snapshot;

        LogicTimeControlService.BeginTimeline();
        try
        {
            LogicTimeControlService.SetBulletTimeScaleForLogicTicks(10, 2750, 12);
            LogicTimeControlService.AcquirePause(42);
            snapshot = LogicTimeControlService.CaptureSnapshot();
        }
        finally
        {
            if (LogicTimeControlService.IsActive)
                LogicTimeControlService.EndTimeline();
        }

        ulong inputHash = LogicStateHasher.ComputeInputHash(frame);
        ulong timeControlHash = LogicStateHasher.ComputeTimeControlHash(snapshot);
        ulong fullHash = LogicStateHasher.ComputeFrameHash(
            frame.FrameId,
            inputHash,
            timeControlHash,
            GameplayPayload);

        return new LogicDeterminismCorpusResult(
            LogicReplayLog.CurrentProtocolVersion,
            LogicReplayLog.CurrentContentVersion,
            frame.Events.Count,
            frame.Checksum,
            frame.WorldMove.x.RawValue,
            frame.WorldMove.y.RawValue,
            inputHash,
            timeControlHash,
            fullHash);
    }

    public static LogicDeterminismCorpusResult ValidateV102()
    {
        LogicDeterminismCorpusResult result = EvaluateV102();

        RequireEqual("ProtocolVersion", GoldenProtocolVersion, result.ProtocolVersion);
        RequireEqual("ContentVersion", GoldenContentVersion, result.ContentVersion);
        RequireEqual("EventCount", GoldenEventCount, result.EventCount);
        RequireEqual("InputChecksum", GoldenInputChecksum, result.InputChecksum);
        RequireEqual("WorldMove.X.RawValue", 777L, result.WorldMoveXRaw);
        RequireEqual("WorldMove.Y.RawValue", -888L, result.WorldMoveYRaw);
        RequireEqual("InputHash", GoldenInputHash, result.InputHash);
        RequireEqual("TimeControlHash", GoldenTimeControlHash, result.TimeControlHash);
        RequireEqual("FullHash", GoldenFullHash, result.FullHash);

        return result;
    }

    private static LogicInputFrame BuildInputFrame()
    {
        var timeline = new LogicInputTimeline(7);
        timeline.Begin(
            100d,
            new FixVector2(Fix64.FromRaw(111), Fix64.FromRaw(-222)),
            LogicInputTimeline.GetButtonBit(LogicInputButton.InteractionPrimary));
        timeline.EnqueueWorldMove(100.01d, new FixVector2(Fix64.FromRaw(777), Fix64.FromRaw(-888)));
        timeline.EnqueueButtonPulse(100.012d, LogicInputButton.InteractionSecondary);
        timeline.EnqueueButtonPressed(100.013d, LogicInputButton.PlayerAttack);
        timeline.EnqueueButtonReleased(100.014d, LogicInputButton.PlayerAttack);
        return timeline.Seal(1, 100d + 1d / LogicFrameRuntime.FrameRate);
    }

    private static void RequireEqual<T>(string field, T expected, T actual)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Logic determinism corpus {CorpusVersion} diverged at {field}: expected={expected}, actual={actual}.");
        }
    }
}
