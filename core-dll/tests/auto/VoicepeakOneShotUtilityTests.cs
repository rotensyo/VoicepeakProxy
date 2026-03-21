using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VoicepeakOneShotUtilityTests
{
    [TestMethod]
    public void ValidateInputOnce_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.ValidateInputOnce(null));
    }

    [TestMethod]
    public void ValidateInputOnceCore_UsesBootValidationTextAndCanSucceed()
    {
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = string.Empty;
        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            config,
            new AppLogger(new TestLogger()),
            CreateResolvedUi(),
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.Completed, result.Status);
    }

    [TestMethod]
    public void ValidateInputOnceCore_ProcessNotFound_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0
        };

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            new AppConfig(),
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.ProcessNotFound, result.Status);
    }

    [TestMethod]
    public void ValidateInputOnceCore_Success_ReturnsCompleted()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok("A", 1, ReadInputSource.PrimaryUiA);
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = "A";
        config.Audio.StopConfirmMs = 1;

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            config,
            new AppLogger(new TestLogger()),
            ui,
            audio);

        Assert.AreEqual(ValidateInputOnceStatus.Completed, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("A", result.ActualText);
        Assert.AreEqual(1, ui.PressPlayCalls);
        Assert.IsTrue(ui.CallLog.IndexOf("prepare_playback") < ui.CallLog.IndexOf("press_play"));
    }

    [TestMethod]
    public void ValidateInputOnceCore_TextMismatch_ReturnsStatusAndActualText()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok("AX", 2, ReadInputSource.PrimaryUiA);
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = "A";

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            config,
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.TextMismatch, result.Status);
        Assert.AreEqual("AX", result.ActualText);
        StringAssert.Contains(result.ErrorMessage, "text_mismatch");
    }

    [TestMethod]
    public void ValidateInputOnceCore_StartTimeout_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok("A", 1, ReadInputSource.PrimaryUiA);
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = "A";
        config.Audio.StartConfirmTimeoutMs = 1;
        config.Audio.StartConfirmMaxRetries = 0;

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            config,
            new AppLogger(new TestLogger()),
            ui,
            audio);

        Assert.AreEqual(ValidateInputOnceStatus.StartConfirmTimeout, result.Status);
    }

    [TestMethod]
    public void ValidateInputOnceCore_InputValidation_RetriesByBootValidationMaxRetries()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
        ui.ClearInputHandler = () => ui.ClearInputCalls > 1;
        AppConfig config = new AppConfig();
        config.Prepare.BootValidationText = string.Empty;
        config.Prepare.BootValidationMaxRetries = 1;
        config.Prepare.BootValidationRetryIntervalMs = 0;

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            config,
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.Completed, result.Status);
        Assert.AreEqual(2, ui.ClearInputCalls);
    }

    [TestMethod]
    public void ClearInputOnce_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VoicepeakOneShot.ClearInputOnce(null));
    }

    [TestMethod]
    public void ClearInputOnceCore_ProcessNotFound_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 0
        };

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ProcessNotFound, result.Status);
    }

    [TestMethod]
    public void ClearInputOnceCore_ClearInputFailed_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ClearInputHandler = () => false;

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ClearInputFailed, result.Status);
    }

    [TestMethod]
    public void ClearInputOnceCore_Success_ReturnsCompleted()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.Completed, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, ui.ClearInputCalls);
    }

    private static FakeVoicepeakUiController CreateResolvedUi()
    {
        return new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (true, Process.GetCurrentProcess(), new IntPtr(123)),
            IsAliveHandler = _ => true,
            PrepareForTextInputHandler = (_, _, _, _) => true,
            ClearInputHandler = () => true,
            TypeTextHandler = (_, _, _) => true
        };
    }
}
