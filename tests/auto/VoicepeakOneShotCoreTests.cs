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
        config.Audio.StartConfirmWindowMs = 1;
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
        config.Ui.CompositeRecoveryClickOnStartTimeoutRetryEnabled = true;
        config.Audio.StartConfirmWindowMs = 1;
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
        config.Ui.CompositePrimeBeforeTextFocusWhenUnprimedEnabled = true;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWaitCore(config, new SpeakOnceRequest { Text = "A" }, new AppLogger(new TestLogger()), RequestValidationMode.Strict, ui, audio);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
        CollectionAssert.AreEqual(new[] { false }, ui.PrepareForTextInputCompositePrimeFlags);
        Assert.AreEqual(0, ui.TryPrimeInputContextCalls);
    }

    private static AppConfig CreateConfig()
    {
        AppConfig config = new AppConfig();
        config.Audio.PollIntervalMs = 1;
        config.Audio.StartConfirmWindowMs = 10;
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
