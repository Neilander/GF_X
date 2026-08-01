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
        long selectScreenXRaw,
        long selectScreenYRaw,
        long selectWorldXRaw,
        long selectWorldYRaw,
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
        SelectScreenXRaw = selectScreenXRaw;
        SelectScreenYRaw = selectScreenYRaw;
        SelectWorldXRaw = selectWorldXRaw;
        SelectWorldYRaw = selectWorldYRaw;
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
    public long SelectScreenXRaw { get; }
    public long SelectScreenYRaw { get; }
    public long SelectWorldXRaw { get; }
    public long SelectWorldYRaw { get; }
    public ulong InputHash { get; }
    public ulong TimeControlHash { get; }
    public ulong FullHash { get; }
}

public static class LogicDeterminismCorpus
{
    public const string CorpusVersion = "v81";
    public const int GoldenProtocolVersion = 81;
    public const string GoldenContentVersion = "Avenge-30Hz-v81";
    public const int GoldenEventCount = 6;
    public const uint GoldenInputChecksum = 44622919u;
    public const ulong GoldenInputHash = 2238831199762417194ul;
    public const ulong GoldenTimeControlHash = 4660981641562439902ul;
    public const ulong GoldenFullHash = 7334454495593281045ul;

    private const ulong GameplayPayload = 0x123456789ABCDEF0UL;

    public static LogicDeterminismCorpusResult EvaluateV81()
    {
        if (LogicTimeControlService.IsActive)
        {
            throw new InvalidOperationException(
                "LogicDeterminismCorpus.EvaluateV81 failed: LogicTimeControlService must be inactive.");
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
            frame.SelectScreenPosition.x.RawValue,
            frame.SelectScreenPosition.y.RawValue,
            frame.SelectWorldPosition.x.RawValue,
            frame.SelectWorldPosition.y.RawValue,
            inputHash,
            timeControlHash,
            fullHash);
    }

    public static LogicDeterminismCorpusResult ValidateV81()
    {
        LogicDeterminismCorpusResult result = EvaluateV81();

        RequireEqual("ProtocolVersion", GoldenProtocolVersion, result.ProtocolVersion);
        RequireEqual("ContentVersion", GoldenContentVersion, result.ContentVersion);
        RequireEqual("EventCount", GoldenEventCount, result.EventCount);
        RequireEqual("InputChecksum", GoldenInputChecksum, result.InputChecksum);
        RequireEqual("WorldMove.X.RawValue", 777L, result.WorldMoveXRaw);
        RequireEqual("WorldMove.Y.RawValue", -888L, result.WorldMoveYRaw);
        RequireEqual("SelectScreenPosition.X.RawValue", 999L, result.SelectScreenXRaw);
        RequireEqual("SelectScreenPosition.Y.RawValue", 1111L, result.SelectScreenYRaw);
        RequireEqual("SelectWorldPosition.X.RawValue", -2222L, result.SelectWorldXRaw);
        RequireEqual("SelectWorldPosition.Y.RawValue", 3333L, result.SelectWorldYRaw);
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
            LogicInputTimeline.GetButtonBit(LogicInputButton.InteractionPrimary),
            new FixVector2(Fix64.FromRaw(333), Fix64.FromRaw(444)),
            true,
            new FixVector2(Fix64.FromRaw(555), Fix64.FromRaw(-666)));
        timeline.EnqueueWorldMove(100.01d, new FixVector2(Fix64.FromRaw(777), Fix64.FromRaw(-888)));
        timeline.EnqueueSelectScreenPosition(100.011d, new FixVector2(Fix64.FromRaw(999), Fix64.FromRaw(1111)));
        timeline.EnqueueSelectWorldPosition(100.011d, new FixVector2(Fix64.FromRaw(-2222), Fix64.FromRaw(3333)));
        timeline.EnqueueButtonPulse(100.012d, LogicInputButton.Skill3);
        timeline.EnqueueButtonPressed(100.013d, LogicInputButton.SkillConfirm);
        timeline.EnqueueButtonReleased(100.014d, LogicInputButton.SkillConfirm);
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
