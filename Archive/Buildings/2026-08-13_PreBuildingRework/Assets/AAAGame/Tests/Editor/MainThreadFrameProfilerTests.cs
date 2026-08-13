using NUnit.Framework;
using UnityGameFramework.Runtime;

public sealed class MainThreadFrameProfilerTests
{
    [Test]
    public void Logging_IsDisabledByDefault()
    {
        Assert.IsFalse(MainThreadFrameProfiler.LoggingEnabled);
    }
}
