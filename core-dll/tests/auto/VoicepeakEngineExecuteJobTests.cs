using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VoicepeakEngineExecuteJobTests
{
    [TestMethod]
    public void ExecuteJob_TargetResolveFailure_DropsProcessLostAndRequestsShutdown()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui, new FakeAudioSessionReader(), logger, cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.IsTrue(engine.IsShutdownRequested);
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("reason=process_lost")));
    }

    [TestMethod]
    public void ExecuteJob_PrepareFailure_DropsJobAndClearsInput()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ClearInputHandler = () => ui.ClearInputCalls > 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui, new FakeAudioSessionReader(), logger, cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("reason=prepare_failed")));
        Assert.IsTrue(ui.ClearInputCalls >= 1);
        Assert.AreEqual(0, ui.KillFocusCalls);
    }

    [TestMethod]
    public void ExecuteJob_StartTimeout_RetriesAndCompletes()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, audio, false);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.IsTrue(ui.PressPlayCalls >= 1);
        Assert.IsTrue(ui.ClearInputCalls >= 2);
        Assert.AreEqual(1, ui.KillFocusCalls);
        Assert.IsFalse(logger.WarnMessages.Exists(m => m.Contains("reason=start_confirm_failed")));
    }

    [TestMethod]
    public void ExecuteJob_StartTimeoutRetry_DoesNotCallTryPrimeInputContext()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Ui.MoveToStartModifier = "ctrl";
        config.Ui.MoveToStartKey = "cursor up";
        config.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled = true;
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 2;
        config.Audio.StopConfirmMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, audio, false);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.AreEqual(3, ui.PressPlayCalls);
        Assert.AreEqual(0, ui.TryPrimeInputContextCalls);
    }

    [TestMethod]
    public void ExecuteJob_StartTimeoutRetry_FunctionShortcut_DoesNotCallTryPrimeInputContext()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Ui.MoveToStartModifier = string.Empty;
        config.Ui.MoveToStartKey = "F3";
        config.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled = true;
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, audio, false);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.AreEqual(0, ui.TryPrimeInputContextCalls);
    }

    [TestMethod]
    public void ExecuteJob_UsesPrepareForPlaybackBeforePlay()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StopConfirmMs = 2;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, audio, false);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.IsTrue(ui.CallLog.IndexOf("prepare_playback") >= 0);
        Assert.IsTrue(ui.CallLog.IndexOf("prepare_playback") < ui.CallLog.IndexOf("press_play"));
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ExecuteJob_DelayOnlyJob_SkipsInputAndPlayback()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui, new FakeAudioSessionReader(), logger, cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("[[pause:1]]"));

        Assert.AreEqual(0, ui.TryResolveTargetDetailedCalls);
        Assert.AreEqual(0, ui.PrepareForTextInputCalls);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.AreEqual(0, ui.BeginModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ExecuteJob_BeginModifierIsolationSessionFails_RequestsShutdown()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.BeginModifierIsolationSessionHandler = (_, _) => false;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui, new FakeAudioSessionReader(), logger, cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("reason=modifier_guard_unavailable_fatal")));
        Assert.IsTrue(logger.ErrorMessages.Exists(m => m.Contains("runtime_fatal reason=modifier_guard_unavailable_fatal")));
        Assert.AreEqual(0, ui.PrepareForTextInputCalls);
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(0, ui.EndModifierIsolationSessionCalls);
        Assert.IsTrue(engine.IsShutdownRequested);
        Assert.IsTrue(cts.IsCancellationRequested);
    }

    [TestMethod]
    public void ExecuteJob_EndModifierIsolationSessionFails_RequestsShutdown()
    {
        TestLogger logger = new TestLogger();
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.EndModifierIsolationSessionHandler = _ => false;
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = CreateEngine(ui, audio, logger, cts);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
        Assert.IsTrue(logger.ErrorMessages.Exists(m => m.Contains("runtime_fatal reason=modifier_guard_unavailable_fatal")));
        Assert.IsTrue(engine.IsShutdownRequested);
        Assert.IsTrue(cts.IsCancellationRequested);
    }

    private static VoicepeakEngine CreateEngine(FakeVoicepeakUiController ui, FakeAudioSessionReader audio, TestLogger logger, CancellationTokenSource cts)
    {
        return new VoicepeakEngine(CreateConfig(), cts, new AppLogger(logger), ui, audio, false);
    }

    private static object CreateJob(string text)
    {
        return JobCompiler.Compile(new SpeakRequest { Text = text, Mode = EnqueueMode.Queue }, new AppConfig());
    }

    private static AppConfig CreateConfig()
    {
        AppConfig config = new AppConfig();
        config.Audio.PollIntervalMs = 1;
        config.Audio.StartConfirmTimeoutMs = 10;
        config.Audio.StopConfirmMs = 1;
        config.Audio.PeakThreshold = 0.5f;
        config.Audio.MaxSpeakingDurationSec = 0;
        return config;
    }

    private static FakeVoicepeakUiController CreateResolvedUi()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ResolveTargetHandler = () => (true, Process.GetCurrentProcess(), new IntPtr(123)),
            ClearInputHandler = () => true,
            MoveToStartHandler = (_, _) => true,
            PressDeleteHandler = _ => true,
            PressPlayHandler = _ => true,
            IsAliveHandler = _ => true
        };

        ui.ReadInputHandler = _ =>
        {
            string text = ui.TypedTexts.Count > 0 ? ui.TypedTexts[ui.TypedTexts.Count - 1] : string.Empty;
            return ReadInputResult.Ok(text, text.Length, ReadInputSource.PrimaryUiA);
        };

        return ui;
    }
}
