using System;
using System.Collections.Generic;

namespace VoicepeakProxyCore;

// 実行時設定のルート
public sealed class AppConfig
{
    public ValidationConfig Validation { get; set; } = new ValidationConfig();
    public UiConfig Ui { get; set; } = new UiConfig();
    public InputTimingConfig InputTiming { get; set; } = new InputTimingConfig();
    public TextConfig Text { get; set; } = new TextConfig();
    public AudioConfig Audio { get; set; } = new AudioConfig();
    public RuntimeConfig Runtime { get; set; } = new RuntimeConfig();
    public HookConfig Hook { get; set; } = new HookConfig();
    public DebugConfig Debug { get; set; } = new DebugConfig();
}

// 入力検証関連設定
public sealed class ValidationConfig
{
    public string ValidationText { get; set; } = "初期化完了";
    public int ValidationMaxRetries { get; set; } = 2;
    public int ValidationRetryIntervalMs { get; set; } = 1000;
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
    public string MoveToStartModifier { get; set; } = "ctrl";
    public string MoveToStartKey { get; set; } = "cursor up";
    public string ClearInputSelectAllModifier { get; set; } = "ctrl";
    public string ClearInputSelectAllKey { get; set; } = "a";
    public string PlayShortcutModifier { get; set; } = string.Empty;
    public string PlayShortcutKey { get; set; } = "spacebar";
    public int DelayBeforePlayShortcutMs { get; set; } = 60;
}

// 入力タイミング関連設定
public sealed class InputTimingConfig
{
    public int TypeTextRetryWaitMs { get; set; } = 1000;
    public int TypeTextRetryMaxRetries { get; set; } = 2;
    public int ClearInputRetryWaitMs { get; set; } = 1000;
    public int ClearInputRetryMaxRetries { get; set; } = 2;
    public int ActionDelayMs { get; set; } = 5;
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
    public bool SplitInputBlockOnNewline { get; set; } = false;
    public List<ReplaceRule> ReplaceRules { get; set; } = new List<ReplaceRule>();
}

// 実行制御関連設定
public sealed class RuntimeConfig
{
    public int MaxQueuedJobs { get; set; } = 500;
    public BootValidationMode BootValidation { get; set; } = BootValidationMode.Required;
}

// デバッグ関連設定
public sealed class DebugConfig
{
    public bool LogTextCandidates { get; set; } = false;
    public bool LogModifierHookStats { get; set; } = false;
    public string LogMinimumLevel { get; set; } = "warn";
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
        EnsureNotNull(config.Validation, "validation は null にできません");
        EnsureNotNull(config.Ui, "ui は null にできません");
        EnsureNotNull(config.InputTiming, "inputTiming は null にできません");
        EnsureNotNull(config.Text, "text は null にできません");
        EnsureNotNull(config.Audio, "audio は null にできません");
        EnsureNotNull(config.Runtime, "runtime は null にできません");
        EnsureNotNull(config.Hook, "hook は null にできません");
        EnsureNotNull(config.Debug, "debug は null にできません");
        EnsureNotNull(config.Debug.LogMinimumLevel, "debug.logMinimumLevel は null にできません");

        EnsureNotNull(config.Validation.ValidationText, "validation.validationText は null にできません");
        EnsureNonNegative(config.Validation.ValidationMaxRetries, "validation.validationMaxRetries は 0 以上で指定してください");
        EnsureNonNegative(config.Validation.ValidationRetryIntervalMs, "validation.validationRetryIntervalMs は 0 以上で指定してください");

        EnsureNonNegative(config.Ui.DelayBeforePlayShortcutMs, "ui.delayBeforePlayShortcutMs は 0 以上で指定してください");

        if (!VoicepeakUiController.IsValidMoveToStartModifier(config.Ui.MoveToStartModifier))
        {
            throw new InvalidOperationException("ui.moveToStartModifier は空文字/ctrl/alt のいずれかを指定してください");
        }

        if (!VoicepeakUiController.IsValidMoveToStartKey(config.Ui.MoveToStartKey))
        {
            throw new InvalidOperationException("ui.moveToStartKey は有効なキーを指定してください（例: cursor up, F3, home）");
        }

        if (!VoicepeakUiController.IsValidClearInputSelectAllModifier(config.Ui.ClearInputSelectAllModifier))
        {
            throw new InvalidOperationException("ui.clearInputSelectAllModifier は空文字/ctrl/alt のいずれかを指定してください");
        }

        if (!VoicepeakUiController.IsValidClearInputSelectAllKey(config.Ui.ClearInputSelectAllKey))
        {
            throw new InvalidOperationException("ui.clearInputSelectAllKey は有効なキーを指定してください（例: a, @, home）");
        }

        if (!VoicepeakUiController.IsValidPlayShortcutModifier(config.Ui.PlayShortcutModifier))
        {
            throw new InvalidOperationException("ui.playShortcutModifier は空文字/ctrl/alt/shift のいずれかを指定してください");
        }

        if (!VoicepeakUiController.IsValidPlayShortcutKey(config.Ui.PlayShortcutKey))
        {
            throw new InvalidOperationException("ui.playShortcutKey は有効なキーを指定してください（例: spacebar, F3, home）");
        }

        EnsureNonNegative(config.InputTiming.ActionDelayMs, "inputTiming.actionDelayMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.TypeTextRetryWaitMs, "inputTiming.typeTextRetryWaitMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.TypeTextRetryMaxRetries, "inputTiming.typeTextRetryMaxRetries は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.ClearInputRetryWaitMs, "inputTiming.clearInputRetryWaitMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.ClearInputRetryMaxRetries, "inputTiming.clearInputRetryMaxRetries は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.PostTypeWaitPerCharMs, "inputTiming.postTypeWaitPerCharMs は 0 以上で指定してください");
        EnsureNonNegative(config.InputTiming.PostTypeWaitMinMs, "inputTiming.postTypeWaitMinMs は 0 以上で指定してください");
        EnsurePositive(config.InputTiming.ClearInputMaxPasses, "inputTiming.clearInputMaxPasses は 1 以上で指定してください");

        EnsurePositive(config.Audio.PollIntervalMs, "audio.pollIntervalMs は 1 以上で指定してください");
        EnsurePositiveFloat(config.Audio.PeakThreshold, "audio.peakThreshold は 0 より大きい値を指定してください");
        EnsurePositive(config.Audio.StartConfirmTimeoutMs, "audio.startConfirmTimeoutMs は 1 以上で指定してください");
        EnsureNonNegative(config.Audio.StartConfirmMaxRetries, "audio.startConfirmMaxRetries は 0 以上で指定してください");
        EnsurePositive(config.Audio.StopConfirmMs, "audio.stopConfirmMs は 1 以上で指定してください");
        EnsurePositive(config.Audio.MaxSpeakingDurationSec, "audio.maxSpeakingDurationSec は 1 以上で指定してください");

        EnsurePositive(config.Hook.HookCommandTimeoutMs, "hook.hookCommandTimeoutMs は 1 以上で指定してください");
        EnsurePositive(config.Hook.HookConnectTimeoutMs, "hook.hookConnectTimeoutMs は 1 以上で指定してください");
        EnsurePositive(config.Hook.HookConnectTotalWaitMs, "hook.hookConnectTotalWaitMs は 1 以上で指定してください");

        if (config.Text.ReplaceRules == null)
        {
            throw new InvalidOperationException("text.replaceRules は null にできません");
        }

        if (!IsValidLogMinimumLevel(config.Debug.LogMinimumLevel))
        {
            throw new InvalidOperationException("debug.logMinimumLevel は debug/info/warn/error のいずれかを指定してください");
        }

        EnsureNonNegative(config.Runtime.MaxQueuedJobs, "runtime.maxQueuedJobs は 0 以上で指定してください");
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

    // 0より大きい浮動小数を検証
    private static void EnsurePositiveFloat(float value, string message)
    {
        if (value <= 0f)
        {
            throw new InvalidOperationException(message);
        }
    }

    // ログ最小レベルの許容値を検証
    private static bool IsValidLogMinimumLevel(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized == "debug"
            || normalized == "info"
            || normalized == "warn"
            || normalized == "error";
    }
}
