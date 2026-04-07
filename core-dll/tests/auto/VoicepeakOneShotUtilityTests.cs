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
    public void ValidateInputOnceCore_UsesValidationTextAndCanSucceed()
    {
        AppConfig config = new AppConfig();
        config.Validation.ValidationText = string.Empty;
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
    public void ValidateInputOnceCore_MultipleProcesses_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 2
        };

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            new AppConfig(),
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.MultipleProcesses, result.Status);
    }

    [TestMethod]
    public void ValidateInputOnceCore_TargetResolveFailure_DoesNotRecountProcess()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ResolveTargetDetailedHandler = () => new ResolveTargetResult
            {
                Success = false,
                FailureReason = ResolveTargetFailureReason.ProcessNotFound,
                ProcessCount = 0
            },
            ProcessCountHandler = () => 99
        };

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            new AppConfig(),
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.ProcessNotFound, result.Status);
        Assert.AreEqual(0, ui.GetVoicepeakProcessCountCalls);
        Assert.AreEqual(1, ui.TryResolveTargetDetailedCalls);
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
        config.Validation.ValidationText = "A";
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
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ValidateInputOnceCore_BeginModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.BeginModifierIsolationSessionHandler = (_, _) => false;

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            new AppConfig(),
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_unavailable_fatal");
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(0, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ValidateInputOnceCore_TextMismatch_ReturnsStatusAndActualText()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok("AX", 2, ReadInputSource.PrimaryUiA);
        AppConfig config = new AppConfig();
        config.Validation.ValidationText = "A";

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
        config.Validation.ValidationText = "A";
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
    public void ValidateInputOnceCore_InputValidation_RetriesByValidationMaxRetries()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ReadInputHandler = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
        ui.ClearInputHandler = () => ui.ClearInputCalls > 1;
        AppConfig config = new AppConfig();
        config.Validation.ValidationText = string.Empty;
        config.Validation.ValidationMaxRetries = 1;
        config.Validation.ValidationRetryIntervalMs = 0;

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
    public void ClearInputOnceCore_TargetNotFound_ReturnsStatus()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (false, null, IntPtr.Zero)
        };

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.TargetNotFound, result.Status);
    }

    [TestMethod]
    public void ClearInputOnceCore_TargetResolveFailure_DoesNotRecountProcess()
    {
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ResolveTargetDetailedHandler = () => new ResolveTargetResult
            {
                Success = false,
                FailureReason = ResolveTargetFailureReason.ProcessNotFound,
                ProcessCount = 0
            },
            ProcessCountHandler = () => 99
        };

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ProcessNotFound, result.Status);
        Assert.AreEqual(0, ui.GetVoicepeakProcessCountCalls);
        Assert.AreEqual(1, ui.TryResolveTargetDetailedCalls);
    }

    [TestMethod]
    public void ClearInputOnceCore_ClearInputFailed_ReturnsStatus()
    {
        AppConfig config = new AppConfig();
        config.InputTiming.ClearInputRetryMaxRetries = 1;
        config.InputTiming.ClearInputRetryWaitMs = 0;
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ClearInputHandler = () => false;

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(config, new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ClearInputFailed, result.Status);
        Assert.AreEqual(2, ui.ClearInputCalls);
    }

    [TestMethod]
    public void ClearInputOnceCore_ClearInputRetry_SucceedsWithinConfiguredRetries()
    {
        AppConfig config = new AppConfig();
        config.InputTiming.ClearInputRetryMaxRetries = 2;
        config.InputTiming.ClearInputRetryWaitMs = 0;
        int clearAttempts = 0;
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.ClearInputHandler = () =>
        {
            clearAttempts++;
            return clearAttempts >= 2;
        };

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(config, new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.Completed, result.Status);
        Assert.AreEqual(2, ui.ClearInputCalls);
    }

    [TestMethod]
    public void ClearInputOnceCore_Success_ReturnsCompleted()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.Completed, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, ui.ClearInputCalls);
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ClearInputOnceCore_BeginModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.BeginModifierIsolationSessionHandler = (_, _) => false;

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_unavailable_fatal");
        Assert.AreEqual(1, ui.BeginModifierIsolationSessionCalls);
        Assert.AreEqual(0, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ValidateInputOnceCore_EndModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.EndModifierIsolationSessionHandler = _ => false;

        ValidateInputOnceResult result = VoicepeakOneShot.ValidateInputOnceCore(
            new AppConfig(),
            new AppLogger(new TestLogger()),
            ui,
            new FakeAudioSessionReader());

        Assert.AreEqual(ValidateInputOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_release_failed_fatal");
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    [TestMethod]
    public void ClearInputOnceCore_EndModifierIsolationSessionFails_ReturnsProcessLost()
    {
        FakeVoicepeakUiController ui = CreateResolvedUi();
        ui.EndModifierIsolationSessionHandler = _ => false;

        ClearInputOnceResult result = VoicepeakOneShot.ClearInputOnceCore(new AppConfig(), new AppLogger(new TestLogger()), ui);

        Assert.AreEqual(ClearInputOnceStatus.ProcessLost, result.Status);
        StringAssert.Contains(result.ErrorMessage, "modifier_guard_release_failed_fatal");
        Assert.AreEqual(1, ui.EndModifierIsolationSessionCalls);
    }

    private static FakeVoicepeakUiController CreateResolvedUi()
    {
        return new FakeVoicepeakUiController
        {
            ProcessCountHandler = () => 1,
            ResolveTargetHandler = () => (true, Process.GetCurrentProcess(), new IntPtr(123)),
            IsAliveHandler = _ => true,
            PrepareForTextInputHandler = (_, _, _) => true,
            ClearInputHandler = () => true,
            TypeTextHandler = (_, _, _) => true
        };
    }
}
