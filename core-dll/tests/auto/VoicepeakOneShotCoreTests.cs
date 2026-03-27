using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VoicepeakOneShotCoreTests
{
    [TestMethod]
    public void SpeakOnceWaitCore_ProcessNotFound_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController { ProcessCountHandler = () => 0 };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(CreateConfig(), new SpeakOnceRequest { Text = "hello" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, new FakeAudioSessionReader());

        Assert.AreEqual(SpeakOnceStatus.ProcessNotFound, result.Status);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_InvalidRequest_ReturnsStatusAndMessage()
    {
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(CreateConfig(), null, new AppLogger(new TestLogger()), RequestValidationMode.Strict, new FakeVoicepeakUiController(), new FakeAudioSessionReader());

        Assert.AreEqual(SpeakOnceStatus.InvalidRequest, result.Status);
        StringAssert.Contains(result.ErrorMessage, "request は null");
    }

    [TestMethod]
    public void SpeakOnceWaitCore_Success_ReturnsCompleted()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StopConfirmMs = 2;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.SegmentsExecuted);
        Assert.IsTrue(ui.CallLog.IndexOf("prepare_playback") >= 0);
        Assert.IsTrue(ui.CallLog.IndexOf("prepare_playback") < ui.CallLog.IndexOf("press_play"));
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_BeginModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.BeginModifierIsolationSessionHandler = (_, _) => false;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "A" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(SpeakOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_unavailable_fatal");
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(0, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_StartTimeout_RetriesAndSucceeds()
    {
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

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        Assert.AreEqual(2, ui.PressPlayCalls);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_StartTimeoutRetry_CompositeRecoveryClick_CallsTryPrimeInputContextOnce()
    {
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
        ui.ShouldAttemptPrimeInputContextHandler = (_, _, reason) => reason == InputContextPrimeReason.StartTimeoutRetry;
        AppConfig config = CreateConfig();
        config.Ui.MoveToStartShortcut = "Ctrl+Up";
        config.Ui.ClickOnStartTimeoutRetryEnabled = true;
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 2;
        config.Audio.StopConfirmMs = 1;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { InputContextPrimeReason.StartTimeoutRetry }, ui.PrimeReasons);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_IgnoresCompositePrimeBeforeTextFocusSetting()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Ui.MoveToStartShortcut = "Ctrl+Up";
        config.Ui.ClickBeforeTextFocusWhenUninitializedEnabled = true;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { false }, ui.PrepareForTextInputCompositePrimeFlags);
        Assert.AreEqual(0, ui.TryPrimeInputContextCalls);
    }

    [TestMethod]
    public void SpeakOnceCore_Success_ReturnsCompleted_WhenStartConfirmed()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };
        AppConfig config = CreateConfig();
        config.Audio.StartConfirmTimeoutMs = 200;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        Assert.AreEqual(1, result.SegmentsExecuted);
        Assert.AreEqual(1, ui.PressPlayCalls);
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void SpeakOnceCore_BeginModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.BeginModifierIsolationSessionHandler = (_, _) => false;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "A" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(SpeakOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_unavailable_fatal");
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(0, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_EndModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.EndModifierIsolationSessionHandler = _ => false;
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "A" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.AreEqual(SpeakOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_release_failed_fatal");
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void SpeakOnceCore_StartTimeout_DoesNotRetryEvenWhenRetryConfigured()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 3;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.StartConfirmTimeout, result.Status);
        Assert.AreEqual(1, ui.PressPlayCalls);
    }

    [TestMethod]
    public void SpeakOnceCore_PauseTokens_AreRemovedAndExecutedAsSingleSegment()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };
        AppConfig config = CreateConfig();
        config.Audio.StartConfirmTimeoutMs = 200;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            config,
            new SpeakOnceRequest { Text = "A[[pause:100]]B[[pause:200]]C" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        Assert.AreEqual(1, result.SegmentsExecuted);
        Assert.AreEqual(1, ui.PressPlayCalls);
        CollectionAssert.AreEqual(new[] { "ABC" }, ui.TypedTexts);
    }

    [TestMethod]
    public void SpeakOnceWaitCore_PauseTokens_KeepSegmentSplit()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StopConfirmMs = 1;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(
            config,
            new SpeakOnceRequest { Text = "A[[pause:100]]B" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        Assert.AreEqual(2, result.SegmentsExecuted);
        Assert.AreEqual(2, ui.PressPlayCalls);
        CollectionAssert.AreEqual(new[] { "A", "B" }, ui.TypedTexts);
    }

    private static AppConfig CreateConfig()
    {
        AppConfig config = new AppConfig();
        config.Audio.PollIntervalMs = 1;
        config.Audio.StartConfirmTimeoutMs = 10;
        config.Audio.StopConfirmMs = 1;
        config.Audio.MaxSpeakingDurationSec = 0;
        config.Audio.PeakThreshold = 0.5f;
        return config;
    }

    private static FakeVoicepeakUiController CreateResolvedUi()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (true, Process.GetCurrentProcess(), new IntPtr(123)),
            TypeTextHandler = (_, _, _) => true,
            ClearInputHandler = () => true,
            MoveToStartHandler = (_, _) => true,
            PressDeleteHandler = _ => true,
            PressPlayHandler = _ => true,
            IsAliveHandler = _ => true
        };

        ui.ReadInputHandler = _ =>
        {
            string text = ui.TypedTexts.LastOrDefault() ?? string.Empty;
            return ReadInputResult.Ok(text, text.Length, ReadInputSource.PrimaryUiA);
        };

        return ui;
    }
}
