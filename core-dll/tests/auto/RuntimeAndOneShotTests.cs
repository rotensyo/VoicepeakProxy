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
        config.Ui.MoveToStartKey = "   ";

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
        config.Runtime.MaxQueuedJobs = 0;
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
    public void Enqueue_EmptyText_ReturnsInvalidRequest()
    {
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(CreateRuntimeConfig(), new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { Text = string.Empty, Mode = EnqueueMode.Queue });

        Assert.AreEqual(EnqueueStatus.InvalidRequest, result.Status);
        StringAssert.Contains(result.ErrorMessage, "text は空文字");
    }

    [TestMethod]
    public void SpeakOnceWait_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.Start(null));
    }

    [TestMethod]
    public void OneShotSession_SpeakOnce_NullRequest_ReturnsInvalidRequest()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());

        SpeakOnceResult result = session.SpeakOnce(null);

        Assert.AreEqual(SpeakOnceStatus.InvalidRequest, result.Status);
    }

    [TestMethod]
    public void OneShotSession_SpeakOnceWait_NullRequest_ReturnsInvalidRequest()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());

        SpeakOnceResult result = session.SpeakOnceWait(null);

        Assert.AreEqual(SpeakOnceStatus.InvalidRequest, result.Status);
    }

    [TestMethod]
    public void OneShotSession_DisposeAfterStart_ThrowsOnUse()
    {
        VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());
        session.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => session.ClearInputOnce());
    }

    [TestMethod]
    public void OneShotSession_UpdateConfig_Null_Throws()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());

        Assert.ThrowsException<ArgumentNullException>(() => session.UpdateConfig(null));
    }

    [TestMethod]
    public void OneShotSession_UpdateConfig_AfterDispose_Throws()
    {
        VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());
        session.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => session.UpdateConfig(new AppConfig()));
    }

    [TestMethod]
    public void OneShotSession_UpdateConfig_ReusesUiaHostAndReplacesUiController()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());
        object uiaHostBefore = ReflectionTestHelper.GetField(session, "_uiaHost");
        object uiBefore = ReflectionTestHelper.GetField(session, "_ui");

        AppConfig updated = new AppConfig();
        updated.Debug.UiaProbeRecycleIntervalSec = 7;

        session.UpdateConfig(updated);

        object uiaHostAfter = ReflectionTestHelper.GetField(session, "_uiaHost");
        object uiAfter = ReflectionTestHelper.GetField(session, "_ui");
        object configAfter = ReflectionTestHelper.GetField(session, "_config");
        int recycleIntervalMs = (int)ReflectionTestHelper.GetField(uiaHostAfter, "_recycleIntervalMs");

        Assert.AreSame(uiaHostBefore, uiaHostAfter);
        Assert.AreNotSame(uiBefore, uiAfter);
        Assert.AreSame(updated, configAfter);
        Assert.AreEqual(7000, recycleIntervalMs);
    }

    [TestMethod]
    public void OneShotSession_UpdateConfig_DoesNotResetUiaSessionStartedTime()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());
        object uiaHost = ReflectionTestHelper.GetField(session, "_uiaHost");
        DateTime expectedStartedUtc = new DateTime(2026, 4, 1, 10, 20, 30, DateTimeKind.Utc);
        ReflectionTestHelper.SetField(uiaHost, "_sessionStartedUtc", expectedStartedUtc);

        AppConfig updated = new AppConfig();
        updated.Debug.UiaProbeRecycleIntervalSec = 3;
        session.UpdateConfig(updated);

        DateTime actualStartedUtc = (DateTime)ReflectionTestHelper.GetField(uiaHost, "_sessionStartedUtc");
        Assert.AreEqual(expectedStartedUtc, actualStartedUtc);
    }

    [TestMethod]
    public void OneShotSession_UpdateConfig_UpdatesCoreLoggerMinimumLevel()
    {
        using VoicepeakOneShotSession session = VoicepeakOneShot.Start(new AppConfig(), new TestLogger());
        object appLogger = ReflectionTestHelper.GetField(session, "_log");

        AppConfig updated = new AppConfig();
        updated.Debug.LogMinimumLevel = "error";
        session.UpdateConfig(updated);

        object minimum = ReflectionTestHelper.GetField(appLogger, "_minimumLevel");
        Assert.AreEqual("Error", minimum.ToString());
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
            (cfg, cts, log, host) =>
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
        config.Runtime.BootValidation = BootValidationMode.Disabled;
        config.Runtime.MaxQueuedJobs = 10;
        config.Debug.LogMinimumLevel = "info";
        return config;
    }
}
