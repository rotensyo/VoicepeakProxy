using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VoicepeakOneShotCoreTests
{
    [TestMethod]
    public void SpeakOnceCore_ProcessNotFound_ReturnsExpectedReason()
    {
        // 対象未起動を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0
        };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("process_not_found", result.Reason);
        Assert.AreEqual(0, result.SegmentsExecuted);
    }

    [TestMethod]
    public void SpeakOnceCore_MultipleProcesses_ReturnsExpectedReason()
    {
        // 複数起動を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 2
        };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("multiple_processes", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_TargetNotFound_ReturnsExpectedReason()
    {
        // 対象解決失敗を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("target_not_found", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_PrepareFailure_ReturnsExpectedReason()
    {
        // 入力準備失敗を返却
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ClearInputHandler = () => false;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("prepare_failed", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_MoveToStartFailure_ReturnsExpectedReason()
    {
        // 先頭移動失敗を返却
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.MoveToStartHandler = (_, _) => false;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("move_to_start_failed", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_PlayFailure_ReturnsExpectedReason()
    {
        // 再生失敗を返却
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.PressPlayHandler = _ => false;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("play_failed", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_StartTimeout_ReturnsExpectedReason()
    {
        // 開始確認失敗を返却
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = CreateConfig();
        config.Audio.StartConfirmWindowMs = 1;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            config,
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("start_confirm_failed", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_MaxDuration_ReturnsExpectedReason()
    {
        // 最大発話超過を返却
        FakeVoicepeakUiController ui = CreateResolvedUi();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };
        AppConfig config = CreateConfig();
        config.Audio.MaxSpeakingDurationSec = 1;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            config,
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("max_speaking_duration", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_ProcessLostAfterPrepare_ReturnsExpectedReason()
    {
        // 再生前消失を返却
        bool alive = true;
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.IsAliveHandler = _ => alive;
        ui.MoveToStartHandler = (_, _) =>
        {
            alive = false;
            return true;
        };

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            CreateConfig(),
            new SpeakOnceRequest { Text = "hello" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            new FakeAudioSessionReader());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("process_lost", result.Reason);
    }

    [TestMethod]
    public void SpeakOnceCore_Success_ReturnsCompletedAndExecutedCount()
    {
        // 全セグメント成功を返却
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
        config.Audio.StopConfirmMs = 2;

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceCore(
            config,
            new SpeakOnceRequest { Text = "A[[pause:10]]B" },
            new AppLogger(new TestLogger()),
            RequestValidationMode.Strict,
            ui,
            audio);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("completed", result.Reason);
        Assert.AreEqual(2, result.SegmentsExecuted);
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
            MoveToEndHandler = (_, _) => true,
            PressDeleteHandler = _ => true,
            PressBackspaceHandler = _ => true,
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
