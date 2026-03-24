using System;
using System.Collections.Generic;

namespace VoicepeakProxyCore;

// 実行時設定のルート
public sealed class AppConfig
{
    public ServerConfig Server { get; set; } = new ServerConfig();
    public AudioConfig Audio { get; set; } = new AudioConfig();
    public PrepareConfig Prepare { get; set; } = new PrepareConfig();
    public UiConfig Ui { get; set; } = new UiConfig();
    public TextTransformConfig TextTransform { get; set; } = new TextTransformConfig();
    public DebugConfig Debug { get; set; } = new DebugConfig();
    public ValidationConfig Validation { get; set; } = new ValidationConfig();
}

// 検証方針の設定
public sealed class ValidationConfig
{
    public BootValidationMode BootValidation { get; set; } = BootValidationMode.Required;
    public RequestValidationMode RequestValidation { get; set; } = RequestValidationMode.Strict;
}

// サーバ関連設定
public sealed class ServerConfig
{
    public int MaxQueuedJobs { get; set; } = 500;
}

// 音声監視関連設定
public sealed class AudioConfig
{
    public float PeakThreshold { get; set; } = 0.000000001f;
    public int PollIntervalMs { get; set; } = 50;
    public int StartConfirmTimeoutMs { get; set; } = 1000;
    public int StartConfirmMaxRetries { get; set; } = 0;
    public int StopConfirmMs { get; set; } = 300;
    public int MaxSpeakingDurationSec { get; set; } = 300;
}

// 入力準備関連設定
public sealed class PrepareConfig
{
    public string BootValidationText { get; set; } = "初期化完了";
    public int BootValidationMaxRetries { get; set; } = 2;
    public int BootValidationRetryIntervalMs { get; set; } = 1000;
    public int CharDelayBaseMs { get; set; } = 1;
    public int ActionDelayMs { get; set; } = 5;
    public int PostTypeWaitPerCharMs { get; set; } = 4;
    public int PostTypeWaitMinMs { get; set; } = 100;
    public int SequentialMoveToStartKeyDelayBaseMs { get; set; } = 5;
    public int DeleteKeyDelayBaseMs { get; set; } = 1;
    public int ClearInputMaxPasses { get; set; } = 20;
}

// UI操作関連設定
public sealed class UiConfig
{
    public string MoveToStartShortcut { get; set; } = "Ctrl+Up";
    public string PlayShortcut { get; set; } = "Space";
    public int DelayBeforePlayShortcutMs { get; set; } = 60;
    public bool ClickAtValidationEnabled { get; set; } = true;
    public bool ClickBeforeTextFocusWhenUninitializedEnabled { get; set; } = false;
    public bool ClickOnStartTimeoutRetryEnabled { get; set; } = false;
    public bool SendEnterAfterSentenceBreak { get; set; } = false;
    public List<string> SentenceBreakTriggers { get; set; } = new List<string> { "。", "！", "？", "!", "?" };
}

// デバッグ関連設定
public sealed class DebugConfig
{
    public bool LogTextCandidates { get; set; } = false;
}

// 文字列変換設定
public sealed class TextTransformConfig
{
    public List<ReplaceRule> ReplaceRules { get; set; } = new List<ReplaceRule>();
}

// 置換ルール定義
public sealed class ReplaceRule
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

// 設定値の整合性を検証
internal static class AppConfigValidator
{
    // 起動前に設定値を検証
    public static void Validate(AppConfig config)
    {
        if (config == null)
        {
            throw new InvalidOperationException("config は null にできません");
        }

        if (config.Server == null)
        {
            throw new InvalidOperationException("server は null にできません");
        }

        if (config.Audio == null)
        {
            throw new InvalidOperationException("audio は null にできません");
        }

        if (config.Prepare == null)
        {
            throw new InvalidOperationException("prepare は null にできません");
        }

        if (config.Ui == null)
        {
            throw new InvalidOperationException("ui は null にできません");
        }

        if (config.TextTransform == null)
        {
            throw new InvalidOperationException("textTransform は null にできません");
        }

        if (config.Validation == null)
        {
            throw new InvalidOperationException("validation は null にできません");
        }

        if (config.Server.MaxQueuedJobs < 0)
        {
            throw new InvalidOperationException("server.maxQueuedJobs は 0 以上で指定してください");
        }

        if (config.Audio.PollIntervalMs <= 0)
        {
            throw new InvalidOperationException("audio.pollIntervalMs は 1 以上で指定してください");
        }

        if (config.Audio.StartConfirmTimeoutMs <= 0)
        {
            throw new InvalidOperationException("audio.startConfirmTimeoutMs は 1 以上で指定してください");
        }

        if (config.Audio.StartConfirmMaxRetries < 0)
        {
            throw new InvalidOperationException("audio.startConfirmMaxRetries は 0 以上で指定してください");
        }

        if (config.Audio.StopConfirmMs <= 0)
        {
            throw new InvalidOperationException("audio.stopConfirmMs は 1 以上で指定してください");
        }

        if (config.Prepare.ActionDelayMs < 0)
        {
            throw new InvalidOperationException("prepare.actionDelayMs は 0 以上で指定してください");
        }

        if (config.Prepare.BootValidationText == null)
        {
            throw new InvalidOperationException("prepare.bootValidationText は null にできません");
        }

        if (config.Prepare.BootValidationMaxRetries < 0)
        {
            throw new InvalidOperationException("prepare.bootValidationMaxRetries は 0 以上で指定してください");
        }

        if (config.Prepare.BootValidationRetryIntervalMs < 0)
        {
            throw new InvalidOperationException("prepare.bootValidationRetryIntervalMs は 0 以上で指定してください");
        }

        if (config.Prepare.PostTypeWaitPerCharMs < 0)
        {
            throw new InvalidOperationException("prepare.postTypeWaitPerCharMs は 0 以上で指定してください");
        }

        if (config.Prepare.PostTypeWaitMinMs < 0)
        {
            throw new InvalidOperationException("prepare.postTypeWaitMinMs は 0 以上で指定してください");
        }

        if (config.Prepare.SequentialMoveToStartKeyDelayBaseMs < 0)
        {
            throw new InvalidOperationException("prepare.sequentialMoveToStartKeyDelayBaseMs は 0 以上で指定してください");
        }

        if (config.Prepare.DeleteKeyDelayBaseMs < 0)
        {
            throw new InvalidOperationException("prepare.deleteKeyDelayBaseMs は 0 以上で指定してください");
        }

        if (config.Prepare.ClearInputMaxPasses <= 0)
        {
            throw new InvalidOperationException("prepare.clearInputMaxPasses は 1 以上で指定してください");
        }

        if (config.Ui.DelayBeforePlayShortcutMs < 0)
        {
            throw new InvalidOperationException("ui.delayBeforePlayShortcutMs は 0 以上で指定してください");
        }

        if (!VoicepeakUiController.IsValidMoveToStartShortcut(config.Ui.MoveToStartShortcut))
        {
            throw new InvalidOperationException("ui.moveToStartShortcut は null/空文字/空白にできません");
        }

        if (!VoicepeakUiController.IsValidPlayShortcut(config.Ui.PlayShortcut))
        {
            throw new InvalidOperationException("ui.playShortcut は修飾なしキーのみ指定できます（例: F3, Space, Home）");
        }

        if (config.Ui.SentenceBreakTriggers == null)
        {
            throw new InvalidOperationException("ui.sentenceBreakTriggers は null にできません");
        }

        for (int i = 0; i < config.Ui.SentenceBreakTriggers.Count; i++)
        {
            string token = config.Ui.SentenceBreakTriggers[i];
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException($"ui.sentenceBreakTriggers[{i}] は空文字を指定できません");
            }
        }

        if (config.TextTransform.ReplaceRules == null)
        {
            throw new InvalidOperationException("textTransform.replaceRules は null にできません");
        }
    }
}
