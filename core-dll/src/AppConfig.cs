using System;
using System.Collections.Generic;

namespace VoicepeakProxyCore;

// 実行時設定のルート
public sealed class AppConfig
{
    public QueueConfig Queue { get; set; } = new QueueConfig();
    public AudioConfig Audio { get; set; } = new AudioConfig();
    public StartupConfig Startup { get; set; } = new StartupConfig();
    public HookConfig Hook { get; set; } = new HookConfig();
    public UiConfig Ui { get; set; } = new UiConfig();
    public InputTimingConfig InputTiming { get; set; } = new InputTimingConfig();
    public TextConfig Text { get; set; } = new TextConfig();
    public ValidationConfig Validation { get; set; } = new ValidationConfig();
    public DebugConfig Debug { get; set; } = new DebugConfig();
}

// 検証方針の設定
public sealed class ValidationConfig
{
    public BootValidationMode BootValidation { get; set; } = BootValidationMode.Required;
    public RequestValidationMode RequestValidation { get; set; } = RequestValidationMode.Strict;
}

// キュー関連設定
public sealed class QueueConfig
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

// 起動時処理関連設定
public sealed class StartupConfig
{
    public string BootValidationText { get; set; } = "初期化完了";
    public int BootValidationMaxRetries { get; set; } = 2;
    public int BootValidationRetryIntervalMs { get; set; } = 1000;
    public bool ClickAtValidationEnabled { get; set; } = true;
    public bool ClickBeforeTextFocusWhenUninitializedEnabled { get; set; } = false;
    public bool ClickOnStartTimeoutRetryEnabled { get; set; } = false;
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
}

// 入力タイミング関連設定
public sealed class InputTimingConfig
{
    public int CharDelayBaseMs { get; set; } = 0;
    public int ActionDelayMs { get; set; } = 5;
    public int PostTypeWaitPerCharMs { get; set; } = 5;
    public int PostTypeWaitMinMs { get; set; } = 300;
    public int SequentialMoveToStartKeyDelayBaseMs { get; set; } = 5;
    public int DeleteKeyDelayBaseMs { get; set; } = 0;
    public int ClearInputMaxPasses { get; set; } = 10;
}

// テキスト処理設定
public sealed class TextConfig
{
    public bool SendEnterAfterSentenceBreak { get; set; } = false;
    public List<string> SentenceBreakTriggers { get; set; } = new List<string> { "。", "！", "？", "!", "?" };
    public List<ReplaceRule> ReplaceRules { get; set; } = new List<ReplaceRule>();
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
        if (config == null)
        {
            throw new InvalidOperationException("config は null にできません");
        }

        if (config.Queue == null)
        {
            throw new InvalidOperationException("queue は null にできません");
        }

        if (config.Audio == null)
        {
            throw new InvalidOperationException("audio は null にできません");
        }

        if (config.Startup == null)
        {
            throw new InvalidOperationException("startup は null にできません");
        }

        if (config.Hook == null)
        {
            throw new InvalidOperationException("hook は null にできません");
        }

        if (config.Ui == null)
        {
            throw new InvalidOperationException("ui は null にできません");
        }

        if (config.InputTiming == null)
        {
            throw new InvalidOperationException("inputTiming は null にできません");
        }

        if (config.Text == null)
        {
            throw new InvalidOperationException("text は null にできません");
        }

        if (config.Debug == null)
        {
            throw new InvalidOperationException("debug は null にできません");
        }

        if (config.Validation == null)
        {
            throw new InvalidOperationException("validation は null にできません");
        }

        if (config.Queue.MaxQueuedJobs < 0)
        {
            throw new InvalidOperationException("queue.maxQueuedJobs は 0 以上で指定してください");
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

        if (config.Startup.BootValidationText == null)
        {
            throw new InvalidOperationException("startup.bootValidationText は null にできません");
        }

        if (config.Startup.BootValidationMaxRetries < 0)
        {
            throw new InvalidOperationException("startup.bootValidationMaxRetries は 0 以上で指定してください");
        }

        if (config.Startup.BootValidationRetryIntervalMs < 0)
        {
            throw new InvalidOperationException("startup.bootValidationRetryIntervalMs は 0 以上で指定してください");
        }

        if (config.Hook.HookCommandTimeoutMs <= 0)
        {
            throw new InvalidOperationException("hook.hookCommandTimeoutMs は 1 以上で指定してください");
        }

        if (config.Hook.HookConnectTimeoutMs <= 0)
        {
            throw new InvalidOperationException("hook.hookConnectTimeoutMs は 1 以上で指定してください");
        }

        if (config.Hook.HookConnectTotalWaitMs <= 0)
        {
            throw new InvalidOperationException("hook.hookConnectTotalWaitMs は 1 以上で指定してください");
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

        if (config.InputTiming.ActionDelayMs < 0)
        {
            throw new InvalidOperationException("inputTiming.actionDelayMs は 0 以上で指定してください");
        }

        if (config.InputTiming.PostTypeWaitPerCharMs < 0)
        {
            throw new InvalidOperationException("inputTiming.postTypeWaitPerCharMs は 0 以上で指定してください");
        }

        if (config.InputTiming.PostTypeWaitMinMs < 0)
        {
            throw new InvalidOperationException("inputTiming.postTypeWaitMinMs は 0 以上で指定してください");
        }

        if (config.InputTiming.SequentialMoveToStartKeyDelayBaseMs < 0)
        {
            throw new InvalidOperationException("inputTiming.sequentialMoveToStartKeyDelayBaseMs は 0 以上で指定してください");
        }

        if (config.InputTiming.DeleteKeyDelayBaseMs < 0)
        {
            throw new InvalidOperationException("inputTiming.deleteKeyDelayBaseMs は 0 以上で指定してください");
        }

        if (config.InputTiming.ClearInputMaxPasses <= 0)
        {
            throw new InvalidOperationException("inputTiming.clearInputMaxPasses は 1 以上で指定してください");
        }

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
    }
}
