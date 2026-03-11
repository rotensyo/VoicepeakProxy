using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VoicepeakEngineTests
{
    [TestMethod]
    public void BootValidate_Optional_NoProcess_ReturnsTrueAndRequestsShutdown()
    {
        // optionalは対象不在でも継続
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0,
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        bool result = engine.BootValidate(BootValidationMode.Optional);

        Assert.IsTrue(result);
        Assert.IsTrue(engine.IsShutdownRequested);
        Assert.IsFalse(cts.IsCancellationRequested);
    }

    [TestMethod]
    public void BootValidate_Required_MultipleProcesses_ReturnsFalse()
    {
        // 複数プロセス時は失敗
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 2
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsFalse(result);
        Assert.IsFalse(engine.IsShutdownRequested);
    }

    [TestMethod]
    public void BootValidate_EmptyBootText_SkipsSpeechAndReturnsTrue()
    {
        // 空起動文言は音声検証を省略
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
        AppConfig config = CreateEngineConfig();
        config.Prepare.BootValidationText = string.Empty;
        TestLogger logger = new TestLogger();

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, new FakeAudioSessionReader(), false);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsTrue(result);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.IsTrue(logger.InfoMessages.Exists(m => m.Contains("boot_validation_skip_speech")));
    }

    [TestMethod]
    public void BootValidate_PressPlayFailure_ReturnsFalse()
    {
        // 再生失敗で起動検証失敗
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        ui.PressPlayHandler = _ => false;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void BootValidate_StartTimeout_ReturnsFalse()
    {
        // 音声開始未検知で失敗
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateEngineConfig();
        config.Audio.StartConfirmWindowMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(new TestLogger()), ui, audio, false);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void BootValidate_StartTimeout_RetriesAndSucceeds()
    {
        // start timeout時に再試行で成功
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateEngineConfig();
        config.Audio.StartConfirmWindowMs = 1;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(new TestLogger()), ui, audio, false);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsTrue(result);
        Assert.AreEqual(2, ui.PressPlayCalls);
    }

    [TestMethod]
    public void BootValidate_DelayedStart_DoesNotCountTowardMaxDuration()
    {
        // 開始前待機は最大発話時間に含めない
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateEngineConfig();
        config.Audio.StartConfirmWindowMs = 1100;
        config.Audio.MaxSpeakingDurationSec = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(new TestLogger()), ui, audio, false);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsFalse(result);
        Assert.AreEqual(2, ui.MoveToStartCalls);
    }

    [TestMethod]
    public void BootValidate_MaxDuration_ReturnsFalse()
    {
        // 終了未検知で失敗
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };
        AppConfig config = CreateEngineConfig();
        config.Audio.MaxSpeakingDurationSec = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(new TestLogger()), ui, audio, false);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsFalse(result);
        Assert.IsTrue(ui.MoveToStartCalls >= 1);
    }

    [TestMethod]
    public void RunInputValidate_ProcessNotAlive_ReturnsExpectedFailure()
    {
        // 生存確認失敗を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            IsAliveHandler = _ => false
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, false);

        AssertInputValidate(result, false, "process_not_alive", "voicepeak_process_exited_or_unavailable");
    }

    [TestMethod]
    public void RunInputValidate_ClearInputFailure_ReturnsExpectedFailure()
    {
        // クリア失敗を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ClearInputHandler = () => false
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, false);

        AssertInputValidate(result, false, "clear_input_failed", "move_to_start_or_delete_not_applied");
    }

    [TestMethod]
    public void RunInputValidate_TypeFailure_ReturnsExpectedFailure()
    {
        // 入力失敗を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            TypeTextHandler = (_, _, _) => false
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, false);

        AssertInputValidate(result, false, "type_text_failed", "wm_char_input_failed");
    }

    [TestMethod]
    public void RunInputValidate_GuardOperationFailures_ReturnExpectedReasons()
    {
        // ガード除去失敗理由を固定
        AssertInputValidate(
            RunInputValidateWithGuardStep(ui => ui.MoveToStartHandler = (_, _) => false),
            false,
            "move_to_start_failed",
            "shortcut_not_applied_or_context_mismatch");
        AssertInputValidate(
            RunInputValidateWithGuardStep(ui => ui.PressDeleteHandler = _ => false),
            false,
            "delete_failed",
            "key_message_not_applied");
    }

    [TestMethod]
    public void RunInputValidate_ReadFailure_ReturnsExpectedFailure()
    {
        // 読み取り失敗を返却
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        ui.ReadInputHandler = _ => ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, false);

        AssertInputValidate(result, false, "read_input_failed", "read_input_source_Exception");
    }

    [TestMethod]
    public void RunInputValidate_TextMismatch_ReturnsExpectedCause()
    {
        // 不一致理由を返却
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok("AabcX", 5, ReadInputSource.PrimaryUiA);

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, true);

        AssertInputValidate(result, false, "text_mismatch", "leading_guard_remaining_move_to_start_or_delete_issue");
        Assert.AreEqual("AabcX", ReflectionTestHelper.GetProperty(result, "ActualText"));
    }

    [TestMethod]
    public void RunInputValidate_Success_ReturnsOk()
    {
        // 入力検証成功を返却
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);

        object result = ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, true);

        AssertInputValidate(result, true, string.Empty, string.Empty);
        CollectionAssert.AreEqual(new[] { "Aabc" }, ui.TypedTexts);
    }

    [TestMethod]
    public void ConsumeInterruptIfAny_True_ResetsFlagAndLogs()
    {
        // 割込みを消費してログ出力
        TestLogger logger = new TestLogger();
        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(logger: logger, appCts: cts);
        ReflectionTestHelper.SetField(engine, "_interruptRequested", true);

        bool result = (bool)ReflectionTestHelper.InvokeCoreInstance(engine, "ConsumeInterruptIfAny", true);

        Assert.IsTrue(result);
        Assert.IsFalse((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
        Assert.IsTrue(logger.InfoMessages.Exists(m => m.Contains("interrupt_applied")));
    }

    [TestMethod]
    public void OnProcessLost_IsIdempotent()
    {
        // プロセス消失処理は一度だけ実行
        TestLogger logger = new TestLogger();
        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(logger: logger, appCts: cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "OnProcessLost");
        ReflectionTestHelper.InvokeCoreInstance(engine, "OnProcessLost");

        Assert.IsTrue(engine.IsShutdownRequested);
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.AreEqual(1, logger.ErrorMessages.FindAll(m => m.Contains("対象プロセスが終了したため処理を中断しました。")).Count);
    }

    [TestMethod]
    public void BootValidate_PrefersPreferredPidBeforeFallback()
    {
        // 起動検証で優先pidを先に試行
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        Process current = Process.GetCurrentProcess();
        int byPidCalls = 0;
        int fallbackCalls = 0;
        ui.ProcessCountHandler = () => 1;
        ui.ResolveByPidHandler = pid =>
        {
            byPidCalls++;
            return (true, current, new IntPtr(123));
        };
        ui.ResolveTargetHandler = () =>
        {
            fallbackCalls++;
            return (true, current, new IntPtr(456));
        };

        AppConfig config = CreateEngineConfig();
        config.Prepare.BootValidationText = string.Empty;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(new TestLogger()), ui, new FakeAudioSessionReader(), false);
        ReflectionTestHelper.SetField(engine, "_preferredVoicepeakPid", current.Id);

        bool result = engine.BootValidate(BootValidationMode.Required);

        Assert.IsTrue(result);
        Assert.AreEqual(1, byPidCalls);
        Assert.AreEqual(0, fallbackCalls);
    }

    private static VoicepeakEngine CreateEngine(FakeVoicepeakUiController ui = null, FakeAudioSessionReader audio = null, TestLogger logger = null, CancellationTokenSource appCts = null)
    {
        AppConfig config = CreateEngineConfig();
        return new VoicepeakEngine(
            config,
            appCts ?? new CancellationTokenSource(),
            new AppLogger(logger ?? new TestLogger()),
            ui ?? new FakeVoicepeakUiController(),
            audio ?? new FakeAudioSessionReader(),
            false);
    }

    private static AppConfig CreateEngineConfig()
    {
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = "abc";
        config.Prepare.BootValidationMaxRetries = 0;
        config.Prepare.BootValidationRetryIntervalMs = 0;
        config.Audio.PollIntervalMs = 1;
        config.Audio.StartConfirmWindowMs = 10;
        config.Audio.StopConfirmMs = 1;
        config.Audio.MaxSpeakingDurationSec = 0;
        config.Audio.PeakThreshold = 0.5f;
        return config;
    }

    private static FakeVoicepeakUiController CreateSuccessfulBootUi()
    {
        return new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (true, Process.GetCurrentProcess(), new IntPtr(123)),
            ReadInputHandler = _ => ReadInputResult.Ok("abc", 3, ReadInputSource.PrimaryUiA)
        };
    }

    private static object RunInputValidateWithGuardStep(Action<FakeVoicepeakUiController> configure)
    {
        FakeVoicepeakUiController ui = CreateSuccessfulBootUi();
        configure(ui);
        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui: ui, appCts: cts);
        return ReflectionTestHelper.InvokeCoreInstance(engine, "RunInputValidate", Process.GetCurrentProcess(), IntPtr.Zero, "abc", 0, true);
    }

    private static void AssertInputValidate(object result, bool success, string reason, string cause)
    {
        Assert.AreEqual(success, ReflectionTestHelper.GetProperty(result, "Success"));
        Assert.AreEqual(reason, ReflectionTestHelper.GetProperty(result, "Reason"));
        Assert.AreEqual(cause, ReflectionTestHelper.GetProperty(result, "Cause"));
    }
}
