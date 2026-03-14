using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.ManualTests;

[TestClass]
public class CompositeMoveToStartManualTests
{
    [TestMethod]
    [TestCategory("Manual")]
    public void CtrlUpMoveToStart_BootValidation_StartsSuccessfully()
    {
        // UI安定化待機
        PauseForUiStabilization();

        // 起動時検証を有効化
        AppConfig config = CreateManualConfig();
        config.Validation.BootValidation = BootValidationMode.Required;
        config.Prepare.BootValidationText = "フォーカス確認です。";

        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new ConsoleAppLogger());

        MessageBox.Show(
            "VOICEPEAK入力欄が選択され、Ctrl+Upで先頭移動した後に起動時読み上げが成功したことを目視確認してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        Assert.IsFalse(runtime.IsShutdownRequested);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public void CtrlUpMoveToStart_SpeakOnce_CompletesSuccessfully()
    {
        // UI安定化待機
        PauseForUiStabilization();

        // 単発読み上げを実行
        AppConfig config = CreateManualConfig();
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnce(
            config,
            new SpeakOnceRequest { Text = "目視確認用の単発読み上げです。" },
            new ConsoleAppLogger());

        MessageBox.Show(
            "VOICEPEAK入力欄が選択され、Ctrl+Upで先頭移動した後に単発読み上げが成功したことを目視確認してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
    }

    private static AppConfig CreateManualConfig()
    {
        // 手動確認向け設定
        AppConfig config = new AppConfig();
        config.Ui.MoveToStartShortcut = "Ctrl+Up";
        config.Ui.CompositePrimeAtValidationEnabled = true;
        config.Ui.CompositePrimeBeforeTextFocusWhenUnprimedEnabled = true;
        config.Ui.CompositeRecoveryClickOnStartTimeoutRetryEnabled = true;
        config.Audio.StartConfirmWindowMs = 2000;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 300;
        config.Audio.MaxSpeakingDurationSec = 30;
        config.Prepare.ActionDelayMs = 20;
        config.Prepare.CharDelayBaseMs = 1;
        config.Prepare.PostTypeWaitPerCharMs = 4;
        config.Prepare.PostTypeWaitMinMs = 100;
        return config;
    }

    private static void PauseForUiStabilization()
    {
        // UIメッセージ反映待機
        Thread.Sleep(200);
    }
}
