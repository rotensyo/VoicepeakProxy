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
        config.Runtime.BootValidation = BootValidationMode.Required;
        config.Validation.ValidationText = "フォーカス確認です。";

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
    public void CtrlUpMoveToStart_SpeakOnceWait_CompletesSuccessfully()
    {
        // UI安定化待機
        PauseForUiStabilization();

        // 単発読み上げを実行
        AppConfig config = CreateManualConfig();
        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(
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

    [TestMethod]
    [TestCategory("Manual")]
    public void CtrlUpMoveToStart_ClearInput_UsesSelectAllAndDeleteLoop()
    {
        // UI安定化待機
        PauseForUiStabilization();

        AppConfig config = CreateManualConfig();
        MessageBox.Show(
            "VOICEPEAK入力欄へ複数ブロックの既存文字列を入れた状態でOKを押してください。\n" +
            "実行後に既存文字列が削除され、新しい読み上げ文字列のみになることを目視確認してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(
            config,
            new SpeakOnceRequest { Text = "削除処理確認用の単発読み上げです。" },
            new ConsoleAppLogger());

        MessageBox.Show(
            "削除処理で全選択ショートカット送信後にDelete2回が繰り返し実行され、旧文字列が消えていることを確認してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        Assert.AreEqual(SpeakOnceStatus.Completed, result.Status);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public void CtrlUpMoveToStart_InterruptPlayback_StopsThenClearsInput()
    {
        // UI安定化待機
        PauseForUiStabilization();

        AppConfig config = CreateManualConfig();
        using VoicepeakRuntime runtime = VoicepeakRuntime.Start(config, new ConsoleAppLogger());

        MessageBox.Show(
            "再生中割込みを確認します。\n" +
            "OK後に少なくとも3秒以上再生される長文を再生し、その後で割込みジョブを投入します。\n" +
            "再生中はSpaceで停止した後にフォーカス投入と削除処理が走ることを目視確認してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        EnqueueResult first = runtime.Enqueue(new SpeakRequest
        {
            Text = "これは再生中割込み確認用の長文です。ゆっくり読み上げられていることを確認してください。これは再生中割込み確認用の長文です。3秒以上の再生時間を確保するために文章量を増やしています。",
            Mode = EnqueueMode.Queue,
            Interrupt = false
        });

        Thread.Sleep(3000);

        EnqueueResult second = runtime.Enqueue(new SpeakRequest
        {
            Text = "割込み後の確認読み上げです。",
            Mode = EnqueueMode.Next,
            Interrupt = true
        });

        Thread.Sleep(3000);

        MessageBox.Show(
            "再生中に停止してから削除処理が実行されることを確認できたらOKを押してください。",
            "VoicepeakProxyCore Manual Test",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        Assert.AreEqual(EnqueueStatus.Accepted, first.Status);
        Assert.AreEqual(EnqueueStatus.Accepted, second.Status);
        Assert.IsFalse(runtime.IsShutdownRequested);
    }

    private static AppConfig CreateManualConfig()
    {
        // 手動確認向け設定
        AppConfig config = new AppConfig();
        config.Ui.MoveToStartModifier = "ctrl";
        config.Ui.MoveToStartKey = "cursor up";
        config.Audio.StartConfirmTimeoutMs = 2000;
        config.Audio.StartConfirmMaxRetries = 1;
        config.Audio.StopConfirmMs = 300;
        config.Audio.MaxSpeakingDurationSec = 30;
        config.InputTiming.ActionDelayMs = 20;
        config.InputTiming.PostTypeWaitPerCharMs = 4;
        config.InputTiming.PostTypeWaitMinMs = 100;
        return config;
    }

    private static void PauseForUiStabilization()
    {
        // UIメッセージ反映待機
        Thread.Sleep(200);
    }
}
