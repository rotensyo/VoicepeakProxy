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
        config.Audio.StartConfirmWindowMs = 1;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 1;

        using CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = new VoicepeakEngine(config, cts, new AppLogger(logger), ui, audio, false);

        ReflectionTestHelper.InvokeCoreInstance(engine, "ExecuteJob", CreateJob("hello"));

        Assert.AreEqual(2, ui.PressPlayCalls);
        Assert.IsFalse(logger.WarnMessages.Exists(m => m.Contains("reason=start_confirm_failed")));
    }

    private static VoicepeakEngine CreateEngine(FakeVoicepeakUiController ui, FakeAudioSessionReader audio, TestLogger logger, CancellationTokenSource cts)
    {
        return new VoicepeakEngine(CreateConfig(), cts, new AppLogger(logger), ui, audio, false);
    }

    private static object CreateJob(string text)
    {
        return JobCompiler.Compile(new SpeakRequest { Text = text, Mode = EnqueueMode.Queue }, new AppConfig(), RequestValidationMode.Strict);
    }

    private static AppConfig CreateConfig()
    {
        AppConfig config = new AppConfig();
        config.Audio.PollIntervalMs = 1;
        config.Audio.StartConfirmWindowMs = 10;
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
