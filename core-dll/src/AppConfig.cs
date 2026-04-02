using System;
using System.Collections.Generic;

namespace VoicepeakProxyCore;

// 実行時設定のルート
public sealed class AppConfig
{
    public StartupConfig Startup { get; set; } = new StartupConfig();
    public HookConfig Hook { get; set; } = new HookConfig();
    public UiConfig Ui { get; set; } = new UiConfig();
    public InputTimingConfig InputTiming { get; set; } = new InputTimingConfig();
    public AudioConfig Audio { get; set; } = new AudioConfig();
    public TextConfig Text { get; set; } = new TextConfig();
    public QueueConfig Queue { get; set; } = new QueueConfig();
    public ValidationConfig Validation { get; set; } = new ValidationConfig();
    public DebugConfig Debug { get; set; } = new DebugConfig();
}

// 起動時処理関連設定
public sealed class StartupConfig
{
    public string BootValidationText { get; set; } = "初期化完了";
    public int BootValidationMaxRetries { get; set; } = 2;
    public int BootValidationRetryIntervalMs { get; set; } = 1000;
    public bool ClickAtValidationEnabled { get; set; } = true;
    public bool ClickBeforeTextFocusWhenUninitializedEnabled { get; set; } = false;
}

// 修飾キーフック関連設定
public sealed class HookConfig
{
    public int HookCommandTimeoutMs { get; set; } = 500;
    public int HookConnectTimeoutMs { get; set; } = 300;
    public int HookConnectTotalWaitMs { get; set; } = 8000;
}

// UI操作関連設定
public sealed class UiConfig
{
    public string MoveToStartShortcut { get; set; } = "Ctrl+Up";
    public string PlayShortcut { get; set; } = "Space";
    public int DelayBeforePlayShortcutMs { get; set; } = 60;
    public bool ClickOnInputFailureRetryEnabled { get; set; } = false;
}

// 入力タイミング関連設定
public sealed class InputTimingConfig
{
    public int CharDelayBaseMs { get; set; } = 0;
    public int DeleteKeyDelayBaseMs { get; set; } = 0;
    public int ActionDelayMs { get; set; } = 5;
    public int SequentialMoveToStartKeyDelayBaseMs { get; set; } = 5;
    public int PostTypeWaitPerCharMs { get; set; } = 5;
    public int PostTypeWaitMinMs { get; set; } = 300;
    public int ClearInputMaxPasses { get; set; } = 10;
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

// テキスト処理設定
public sealed class TextConfig
{
    public bool SendEnterAfterSentenceBreak { get; set; } = false;
    public List<string> SentenceBreakTriggers { get; set; } = new List<string> { "。", "！", "？", "!", "?" };
    public List<ReplaceRule> ReplaceRules { get; set; } = new List<ReplaceRule>();
}

// キュー関連設定
public sealed class QueueConfig
{
    public int MaxQueuedJobs { get; set; } = 500;
}

// 検証方針の設定
public sealed class ValidationConfig
{
    public BootValidationMode BootValidation { get; set; } = BootValidationMode.Required;
}

// デバッグ関連設定
public sealed class DebugConfig
{
    public bool LogTextCandidates { get; set; } = false;
    public bool LogModifierHookStats { get; set; } = false;
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
        EnsureNotNull(config, "config は null にできません");
        EnsureNotNull(config.Startup, "startup は null にできません");
        EnsureNotNull(config.Hook, "hook は null にできません");
        EnsureNotNull(config.Ui, "ui は null にできません");
        EnsureNotNull(config.InputTiming, "inputTiming は null にできません");
        EnsureNotNull(config.Audio, "audio は null にできません");
        EnsureNotNull(config.Text, "text は null にできません");
        EnsureNotNull(config.Queue, "queue は null にできません");
        EnsureNotNull(config.Validation, "validation は null にできません");
        EnsureNotNull(config.Debug, "debug は null にできません");

        EnsureNotNull(config.Startup.BootValidationText, "startup.bootValidationText は null にできません");
        EnsureNonNegative(config.Startup.BootValidationMaxRetries, "startup.bootValidationMaxRetries は 0 以上で指定してください");
        EnsureNonNegative(config.Startup.BootValidationRetryIntervalMs, "startup.bootValidationRetryIntervalMs は 0 以上で指定してください");

        EnsurePositive(config.Hook.HookCommandTimeoutMs, "hook.hookCommandTimeoutMs は 1 以上で指定してください");
        EnsurePositive(config.Hook.HookConnectTimeoutMs, "hook.hookConnectTimeoutMs は 1 以上で指定してください");
        EnsurePositive(config.Hook.HookConnectTotalWaitMs, "hook.hookConnectTotalWaitMs は 1 以上で指定してください");

        EnsureNonNegative(config.Ui.DelayBeforePlayShortcutMs, "ui.delayBeforePlayShortcutMs は 0 以上で指定してください");

        if (!VoicepeakUiController.IsValidMoveToStartShortcut(config.Ui.MoveToStartShortcut))
        {
            throw new InvalidOperationException("ui.moveToStartShortcut は null/空文字/空白にできません");
        }

        if (!VoicepeakUiController.IsValidPlayShortcut(config.Ui.PlayShortcut))
        {
            throw new InvalidOperationException("ui.playShortcut は修飾なしキーのみ指定できます（例: F3, Space, Home）");
        }

        EnsureNonNegative(config.InputTiming.ActionDelayMs, "inputTiming.actionDelayMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.PostTypeWaitPerCharMs, "inputTiming.postTypeWaitPerCharMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.PostTypeWaitMinMs, "inputTiming.postTypeWaitMinMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.SequentialMoveToStartKeyDelayBaseMs, "inputTiming.sequentialMoveToStartKeyDelayBaseMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.DeleteKeyDelayBaseMs, "inputTiming.deleteKeyDelayBaseMs は 0 以上で指定してください");
        EnsurePositive(config.InputTiming.ClearInputMaxPasses, "inputTiming.clearInputMaxPasses は 1 以上で指定してください");

        EnsurePositive(config.Audio.PollIntervalMs, "audio.pollIntervalMs は 1 以上で指定してください");
        EnsurePositive(config.Audio.StartConfirmTimeoutMs, "audio.startConfirmTimeoutMs は 1 以上で指定してください");
        EnsureNonNegative(config.Audio.StartConfirmMaxRetries, "audio.startConfirmMaxRetries は 0 以上で指定してください");
        EnsurePositive(config.Audio.StopConfirmMs, "audio.stopConfirmMs は 1 以上で指定してください");

        if (config.Text.SentenceBreakTriggers == null)
        {
            throw new InvalidOperationException("text.sentenceBreakTriggers は null にできません");
        }

        for (int i = 0; i < config.Text.SentenceBreakTriggers.Count; i++)
        {
            string token = config.Text.SentenceBreakTriggers[i];
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException($"text.sentenceBreakTriggers[{i}] は空文字を指定できません");
            }
        }

        if (config.Text.ReplaceRules == null)
        {
            throw new InvalidOperationException("text.replaceRules は null にできません");
        }
        EnsureNonNegative(config.Queue.MaxQueuedJobs, "queue.maxQueuedJobs は 0 以上で指定してください");
    }

    // null禁止を検証
    private static void EnsureNotNull(object value, string message)
    {
        if (value == null)
        {
            throw new InvalidOperationException(message);
        }
    }

    // 0以上を検証
    private static void EnsureNonNegative(int value, string message)
    {
        if (value < 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    // 1以上を検証
    private static void EnsurePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException(message);
        }
    }
}
