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
        // Startの引数nullを拒否
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakRuntime.Start(null));
    }

    [TestMethod]
    public void Start_BootValidationDisabled_LogsExpectedMessages()
    {
        // 起動ログ出力を検証
        AppConfig config = CreateRuntimeConfig();
        TestLogger logger = new TestLogger();

        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, logger);

        Assert.IsTrue(logger.InfoMessages.Any(m => m.Contains("boot_start")));
        Assert.IsTrue(logger.InfoMessages.Any(m => m.Contains("boot_validation_skipped mode=disabled")));
        Assert.IsTrue(logger.InfoMessages.Any(m => m.Contains("runtime_started")));
    }

    [TestMethod]
    public void Start_InvalidConfig_Throws()
    {
        // 無効設定は起動前に拒否
        AppConfig config = new AppConfig();
        config.Ui.MoveToStartShortcut = "Delete";

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.Start(config, new TestLogger()));
    }

    [TestMethod]
    public void Start_BootValidationRequiredWithoutProcess_Throws()
    {
        // 必須起動検証失敗で例外
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0,
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };
        AppConfig config = new AppConfig();
        config.Validation.BootValidation = BootValidationMode.Required;

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.StartCore(
            config,
            new TestLogger(),
            (cfg, cts, log) => new VoicepeakEngine(cfg, cts, log, ui, new FakeAudioSessionReader(), false)));
    }

    [TestMethod]
    public void Start_BootValidationOptionalWithoutProcess_StartsShutdownRequested()
    {
        // optional失敗時も起動しshutdown要求を保持
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0,
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };
        AppConfig config = new AppConfig();
        config.Validation.BootValidation = BootValidationMode.Optional;

        using VoicepeakRuntime runtime = VoicepeakRuntime.StartCore(
            config,
            new TestLogger(),
            (cfg, cts, log) => new VoicepeakEngine(cfg, cts, log, ui, new FakeAudioSessionReader(), false));

        Assert.IsTrue(runtime.IsShutdownRequested);
    }

    [TestMethod]
    public void StartCore_EngineFactoryThrows_DisposesCancellationTokenSource()
    {
        // ファクトリ例外時にctsを破棄
        AppConfig config = new AppConfig();
        CancellationTokenSource captured = null;

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.StartCore(
            config,
            new TestLogger(),
            (cfg, cts, log) =>
            {
                captured = cts;
                throw new InvalidOperationException("factory_failed");
            }));

        Assert.IsNotNull(captured);
        Assert.ThrowsException<ObjectDisposedException>(() => captured.Cancel());
    }

    [TestMethod]
    public void StartCore_BootValidateThrows_DisposesCancellationTokenSource()
    {
        // 起動検証例外時にctsを破棄
        AppConfig config = new AppConfig();
        CancellationTokenSource captured = null;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => throw new InvalidOperationException("boot_failed")
        };

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakRuntime.StartCore(
            config,
            new TestLogger(),
            (cfg, cts, log) =>
            {
                captured = cts;
                return new VoicepeakEngine(cfg, cts, log, ui, new FakeAudioSessionReader(), false);
            }));

        Assert.IsNotNull(captured);
        Assert.ThrowsException<ObjectDisposedException>(() => captured.Cancel());
    }

    [TestMethod]
    public void StartCore_EngineFactoryReturnsNull_ThrowsExplicitException()
    {
        // null返却時に明示例外
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
        Assert.IsNotNull(captured);
        Assert.ThrowsException<ObjectDisposedException>(() => captured.Cancel());
    }

    [TestMethod]
    public void Stop_PreventsFurtherEnqueue()
    {
        // Stop後の投入を禁止
        AppConfig config = CreateRuntimeConfig();
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());
        runtime.Stop();

        Assert.ThrowsException<InvalidOperationException>(() => runtime.Enqueue(new SpeakRequest { text = "hello", mode = "queue" }));
    }

    [TestMethod]
    public void Dispose_PreventsFurtherEnqueue()
    {
        // Dispose後の投入を禁止
        AppConfig config = CreateRuntimeConfig();
        VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());
        runtime.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => runtime.Enqueue(new SpeakRequest { text = "hello", mode = "queue" }));
    }

    [TestMethod]
    public void Enqueue_InvalidMode_Returns400()
    {
        // 不正modeは400を返却
        AppConfig config = CreateRuntimeConfig();
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { text = "hello", mode = "bad" });

        Assert.AreEqual(400, result.StatusCode);
        StringAssert.Contains(result.Body, "mode は queue|next|flush");
    }

    [TestMethod]
    public void Enqueue_QueueFull_Returns429()
    {
        // キュー満杯は429を返却
        AppConfig config = CreateRuntimeConfig();
        config.Server.MaxQueuedJobs = 0;
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { text = "hello", mode = "queue" });

        Assert.AreEqual(429, result.StatusCode);
        StringAssert.Contains(result.Body, "queue_full");
    }

    [TestMethod]
    public void Enqueue_ValidationOverride_CanRelaxStrictMode()
    {
        // overrideでstrictを緩和
        AppConfig config = CreateRuntimeConfig();
        config.Validation.RequestValidation = RequestValidationMode.Strict;
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        EnqueueResult result = runtime.Enqueue(new SpeakRequest { text = null, mode = null }, RequestValidationMode.Lenient);

        Assert.AreEqual(202, result.StatusCode);
    }

    [TestMethod]
    public void SpeakOnce_NullConfig_Throws()
    {
        // 単発実行のconfig nullを拒否
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.SpeakOnce(null, new SpeakOnceRequest()));
    }

    [TestMethod]
    public void SpeakOnce_NullRequest_ReturnsInvalidRequest()
    {
        // 単発実行でrequest nullをエラー化
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnce(new AppConfig(), null, new TestLogger(), RequestValidationMode.Strict);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Reason, "invalid_request:request は null にできません");
        Assert.AreEqual(0, result.SegmentsExecuted);
    }

    [TestMethod]
    public void SpeakOnceNoValidation_NullRequest_ReturnsInvalidRequest()
    {
        // 無検証単発でもrequest nullをエラー化
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceNoValidation(new AppConfig(), null, new TestLogger());

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Reason, "invalid_request:request は null にできません");
    }

    [TestMethod]
    public void SpeakOnce_InvalidConfig_Throws()
    {
        // 単発実行も無効設定を拒否
        AppConfig config = new AppConfig();
        config.Ui.PlayShortcut = "Delete";

        Assert.ThrowsException<InvalidOperationException>(() => VoicepeakOneShot.SpeakOnce(config, new SpeakOnceRequest()));
    }

    [TestMethod]
    public void Logger_IsIsolatedPerRuntimeInstance()
    {
        // ランタイム間でロガーを分離
        AppConfig config = CreateRuntimeConfig();
        TestLogger first = new TestLogger();
        TestLogger second = new TestLogger();

        using VoicepeakRuntime runtime1 = VoicepeakRuntime.Start(config, first);
        using VoicepeakRuntime runtime2 = VoicepeakRuntime.Start(config, second);

        runtime1.Stop();
        runtime2.Stop();

        Assert.IsTrue(first.InfoMessages.Any(m => m.Contains("runtime_started")));
        Assert.IsTrue(second.InfoMessages.Any(m => m.Contains("runtime_started")));
        Assert.AreEqual(first.InfoMessages.Count(m => m.Contains("runtime_started")), 1);
        Assert.AreEqual(second.InfoMessages.Count(m => m.Contains("runtime_started")), 1);
    }

    [TestMethod]
    public void Stop_CanBeCalledMultipleTimes()
    {
        // Stop多重呼び出しを許容
        AppConfig config = CreateRuntimeConfig();
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        runtime.Stop();
        runtime.Stop();
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Dispose多重呼び出しを許容
        AppConfig config = CreateRuntimeConfig();
        VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new TestLogger());

        runtime.Dispose();
        runtime.Dispose();
    }

    [TestMethod]
    public void StopAndDispose_LogLifecycleMessages()
    {
        // 停止破棄ログを記録
        AppConfig config = CreateRuntimeConfig();
        TestLogger logger = new TestLogger();
        VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, logger);

        runtime.Stop();
        runtime.Dispose();

        Assert.IsTrue(logger.InfoMessages.Any(m => m.Contains("runtime_stopping")));
        Assert.IsTrue(logger.InfoMessages.Any(m => m.Contains("runtime_disposed")));
    }

    [TestMethod]
    public void BootValidate_Disabled_ReturnsTrue()
    {
        // 起動検証disabledは成功
        TestLogger logger = new TestLogger();
        CancellationTokenSource cts = new CancellationTokenSource();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateRuntimeConfig(), cts, logger);
        try
        {
            bool result = (bool)ReflectionTestHelper.InvokeCoreInstance(engine, "BootValidate", BootValidationMode.Disabled);
            Assert.IsTrue(result);
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
            cts.Dispose();
        }
    }

    private static AppConfig CreateRuntimeConfig()
    {
        // テスト向けruntime設定を作成
        AppConfig config = new AppConfig();
        config.Validation.BootValidation = BootValidationMode.Disabled;
        config.Server.MaxQueuedJobs = 10;
        return config;
    }
}
