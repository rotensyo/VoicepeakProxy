using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class RuntimeAndOneShotTests
{
    [TestMethod]
    public void Start_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakRuntime.Start(null));
    }

    [TestMethod]
    public void Start_InvalidConfig_Throws()
    {
        AppConfig config = new AppConfig();
        config.Ui.MoveToStartShortcut = "   ";

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.Start(config, new TestLogger()));
    }

    [TestMethod]
    public void Stop_PreventsFurtherEnqueue()
    {
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(CreateRuntimeConfig(), new TestLogger());
        runtime.Stop();

        Assert.ThrowsException<InvalidOperationException>(() =>
            runtime.Enqueue(new SpeakRequest { Text = "hello", Mode = EnqueueMode.Queue }));
    }

    [TestMethod]
    public void Dispose_PreventsFurtherEnqueue()
    {
        VoicepeakRuntime runtime = VoicepeakRuntime.Start(CreateRuntimeConfig(), new TestLogger());
        runtime.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            runtime.Enqueue(new SpeakRequest { Text = "hello", Mode = EnqueueMode.Queue }));
    }

    [TestMethod]
    public void Enqueue_QueueFull_ReturnsQueueFullStatus()
    {
        AppConfig config = CreateRuntimeConfig();
        config.Queue.MaxQueuedJobs = 0;
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { Text = "hello", Mode = EnqueueMode.Queue });

        Assert.AreEqual(EnqueueStatus.QueueFull, result.Status);
        StringAssert.Contains(result.ErrorMessage, "キューが上限");
    }

    [TestMethod]
    public void Enqueue_ValidRequest_ReturnsAcceptedStatus()
    {
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(CreateRuntimeConfig(), new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { Text = "hello", Mode = EnqueueMode.Queue });

        Assert.AreEqual(EnqueueStatus.Accepted, result.Status);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.JobId));
    }

    [TestMethod]
    public void SpeakOnceWait_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.SpeakOnceWait(null, new SpeakOnceRequest()));
    }

    [TestMethod]
    public void SpeakOnceWait_NullRequest_ReturnsInvalidRequest()
    {
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(new AppConfig(), null, new TestLogger());

        Assert.AreEqual(SpeakOnceStatus.InvalidRequest, result.Status);
        StringAssert.Contains(result.ErrorMessage, "request は null");
    }

    [TestMethod]
    public void SpeakOnce_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.SpeakOnce(null, new SpeakOnceRequest()));
    }

    [TestMethod]
    public void SpeakOnce_NullRequest_ReturnsInvalidRequest()
    {
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnce(new AppConfig(), null, new TestLogger());

        Assert.AreEqual(SpeakOnceStatus.InvalidRequest, result.Status);
        StringAssert.Contains(result.ErrorMessage, "request は null");
    }

    [TestMethod]
    public void Logger_IsIsolatedPerRuntimeInstance()
    {
        AppConfig config = CreateRuntimeConfig();
        TestLogger first = new TestLogger();
        TestLogger second = new TestLogger();

        using VoicepeakRuntime runtime1 = VoicepeakRuntime.Start(config, first);
        using VoicepeakRuntime runtime2 = VoicepeakRuntime.Start(config, second);

        Assert.AreEqual(1, first.InfoMessages.Count(m => m.Contains("runtime_started")));
        Assert.AreEqual(1, second.InfoMessages.Count(m => m.Contains("runtime_started")));
    }

    [TestMethod]
    public void StartCore_EngineFactoryReturnsNull_ThrowsExplicitException()
    {
        AppConfig config = new AppConfig();
        CancellationTokenSource captured = null;

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.StartCore(
            config,
            new TestLogger(),
            (cfg, cts, log) =>
            {
                captured = cts;
                return null;
            }));

        StringAssert.Contains(ex.Message, "engineFactory returned null.");
        Assert.ThrowsException<ObjectDisposedException>(() => captured.Cancel());
    }

    private static AppConfig CreateRuntimeConfig()
    {
        AppConfig config = new AppConfig();
        config.Validation.BootValidation = BootValidationMode.Disabled;
        config.Queue.MaxQueuedJobs = 10;
        return config;
    }
}
