using System.Collections.Generic;
using BouyomiVoicepeakBridge.Shared;
using VoicepeakProxyCore;

namespace VoicepeakProxyWorker;

// JSON設定をコア設定へ変換
internal static class AppConfigMapper
{
    // AppConfigDataをAppConfigへ変換
    public static AppConfig Map(AppConfigData source)
    {
        AppConfigData data = source ?? new AppConfigData();
        data.Normalize();

        AppConfig config = new AppConfig();
        // core既定値を欠損補完に利用
        HookConfig defaults = new AppConfig().Hook;
        UiConfig uiDefaults = new AppConfig().Ui;

        config.Validation.ValidationText = data.Validation.ValidationText ?? string.Empty;
        config.Validation.ValidationMaxRetries = data.Validation.ValidationMaxRetries;
        config.Validation.ValidationRetryIntervalMs = data.Validation.ValidationRetryIntervalMs;

        config.Hook.HookCommandTimeoutMs = data.Hook.HookCommandTimeoutMs > 0
            ? data.Hook.HookCommandTimeoutMs
            : defaults.HookCommandTimeoutMs;
        config.Hook.HookConnectTimeoutMs = data.Hook.HookConnectTimeoutMs > 0
            ? data.Hook.HookConnectTimeoutMs
            : defaults.HookConnectTimeoutMs;
        config.Hook.HookConnectTotalWaitMs = data.Hook.HookConnectTotalWaitMs > 0
            ? data.Hook.HookConnectTotalWaitMs
            : defaults.HookConnectTotalWaitMs;

        config.Ui.MoveToStartModifier = data.Ui.MoveToStartModifier ?? uiDefaults.MoveToStartModifier;
        config.Ui.MoveToStartKey = data.Ui.MoveToStartKey ?? uiDefaults.MoveToStartKey;
        config.Ui.ClearInputSelectAllModifier = data.Ui.ClearInputSelectAllModifier ?? uiDefaults.ClearInputSelectAllModifier;
        config.Ui.ClearInputSelectAllKey = data.Ui.ClearInputSelectAllKey ?? uiDefaults.ClearInputSelectAllKey;
        config.Ui.PlayShortcutModifier = data.Ui.PlayShortcutModifier ?? uiDefaults.PlayShortcutModifier;
        config.Ui.PlayShortcutKey = data.Ui.PlayShortcutKey ?? uiDefaults.PlayShortcutKey;
        config.Ui.DelayBeforePlayShortcutMs = data.Ui.DelayBeforePlayShortcutMs;

        config.InputTiming.KeyStrokeIntervalMs = data.InputTiming.KeyStrokeIntervalMs;
        config.InputTiming.TypeTextRetryWaitMs = data.InputTiming.TypeTextRetryWaitMs;
        config.InputTiming.TypeTextRetryMaxRetries = data.InputTiming.TypeTextRetryMaxRetries;
        config.InputTiming.ClearInputRetryWaitMs = data.InputTiming.ClearInputRetryWaitMs;
        config.InputTiming.ClearInputRetryMaxRetries = data.InputTiming.ClearInputRetryMaxRetries;
        config.InputTiming.ActionDelayMs = data.InputTiming.ActionDelayMs;
        config.InputTiming.PostTypeWaitPerCharMs = data.InputTiming.PostTypeWaitPerCharMs;
        config.InputTiming.PostTypeWaitMinMs = data.InputTiming.PostTypeWaitMinMs;
        config.InputTiming.ClearInputMaxPasses = data.InputTiming.ClearInputMaxPasses;

        config.Audio.PeakThreshold = data.Audio.PeakThreshold;
        config.Audio.PollIntervalMs = data.Audio.PollIntervalMs;
        config.Audio.StartConfirmTimeoutMs = data.Audio.StartConfirmTimeoutMs;
        config.Audio.StartConfirmMaxRetries = data.Audio.StartConfirmMaxRetries;
        config.Audio.StopConfirmMs = data.Audio.StopConfirmMs;
        config.Audio.MaxSpeakingDurationSec = data.Audio.MaxSpeakingDurationSec;

        config.Text.ReplaceRules = new List<ReplaceRule>();
        config.Text.SplitInputBlockOnNewline = data.Text.SplitInputBlockOnNewline;
        for (int i = 0; i < data.Text.ReplaceRules.Count; i++)
        {
            ReplaceRuleData rule = data.Text.ReplaceRules[i] ?? new ReplaceRuleData();
            config.Text.ReplaceRules.Add(new ReplaceRule
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        config.Runtime.MaxQueuedJobs = data.Runtime.MaxQueuedJobs;
        config.Runtime.BootValidation = MapBootValidation(data.Runtime.BootValidation);

        config.Debug.LogTextCandidates = data.Debug.LogTextCandidates;
        config.Debug.LogModifierHookStats = data.Debug.LogModifierHookStats;
        config.Debug.LogMinimumLevel = data.Debug.LogMinimumLevel ?? "warn";

        return config;
    }

    // core既定設定をJSONモデルへ変換
    public static AppConfigData MapFromCoreDefaults(AppConfig source)
    {
        AppConfig config = source ?? new AppConfig();
        AppConfigData data = new AppConfigData();

        data.Validation.ValidationText = config.Validation.ValidationText ?? string.Empty;
        data.Validation.ValidationMaxRetries = config.Validation.ValidationMaxRetries;
        data.Validation.ValidationRetryIntervalMs = config.Validation.ValidationRetryIntervalMs;

        data.Hook.HookCommandTimeoutMs = config.Hook.HookCommandTimeoutMs;
        data.Hook.HookConnectTimeoutMs = config.Hook.HookConnectTimeoutMs;
        data.Hook.HookConnectTotalWaitMs = config.Hook.HookConnectTotalWaitMs;

        data.Ui.MoveToStartModifier = config.Ui.MoveToStartModifier ?? string.Empty;
        data.Ui.MoveToStartKey = config.Ui.MoveToStartKey ?? string.Empty;
        data.Ui.ClearInputSelectAllModifier = config.Ui.ClearInputSelectAllModifier ?? string.Empty;
        data.Ui.ClearInputSelectAllKey = config.Ui.ClearInputSelectAllKey ?? string.Empty;
        data.Ui.PlayShortcutModifier = config.Ui.PlayShortcutModifier ?? string.Empty;
        data.Ui.PlayShortcutKey = config.Ui.PlayShortcutKey ?? string.Empty;
        data.Ui.DelayBeforePlayShortcutMs = config.Ui.DelayBeforePlayShortcutMs;

        data.InputTiming.KeyStrokeIntervalMs = config.InputTiming.KeyStrokeIntervalMs;
        data.InputTiming.TypeTextRetryWaitMs = config.InputTiming.TypeTextRetryWaitMs;
        data.InputTiming.TypeTextRetryMaxRetries = config.InputTiming.TypeTextRetryMaxRetries;
        data.InputTiming.ClearInputRetryWaitMs = config.InputTiming.ClearInputRetryWaitMs;
        data.InputTiming.ClearInputRetryMaxRetries = config.InputTiming.ClearInputRetryMaxRetries;
        data.InputTiming.ActionDelayMs = config.InputTiming.ActionDelayMs;
        data.InputTiming.PostTypeWaitPerCharMs = config.InputTiming.PostTypeWaitPerCharMs;
        data.InputTiming.PostTypeWaitMinMs = config.InputTiming.PostTypeWaitMinMs;
        data.InputTiming.ClearInputMaxPasses = config.InputTiming.ClearInputMaxPasses;

        data.Audio.PeakThreshold = config.Audio.PeakThreshold;
        data.Audio.PollIntervalMs = config.Audio.PollIntervalMs;
        data.Audio.StartConfirmTimeoutMs = config.Audio.StartConfirmTimeoutMs;
        data.Audio.StartConfirmMaxRetries = config.Audio.StartConfirmMaxRetries;
        data.Audio.StopConfirmMs = config.Audio.StopConfirmMs;
        data.Audio.MaxSpeakingDurationSec = config.Audio.MaxSpeakingDurationSec;

        data.Text.ReplaceRules = new List<ReplaceRuleData>();
        data.Text.SplitInputBlockOnNewline = config.Text.SplitInputBlockOnNewline;
        for (int i = 0; i < config.Text.ReplaceRules.Count; i++)
        {
            ReplaceRule rule = config.Text.ReplaceRules[i] ?? new ReplaceRule();
            data.Text.ReplaceRules.Add(new ReplaceRuleData
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        data.Runtime.MaxQueuedJobs = config.Runtime.MaxQueuedJobs;

        data.Runtime.BootValidation = MapBootValidationBack(config.Runtime.BootValidation);

        data.Debug.LogTextCandidates = config.Debug.LogTextCandidates;
        data.Debug.LogModifierHookStats = config.Debug.LogModifierHookStats;
        data.Debug.LogMinimumLevel = config.Debug.LogMinimumLevel ?? "warn";

        data.Normalize();
        return data;
    }

    // 起動時検証方針を変換
    private static BootValidationMode MapBootValidation(BootValidationModeOption mode)
    {
        switch (mode)
        {
            case BootValidationModeOption.Optional:
                return BootValidationMode.Optional;
            case BootValidationModeOption.Disabled:
                return BootValidationMode.Disabled;
            default:
                return BootValidationMode.Required;
        }
    }

    // 起動時検証方針を逆変換
    private static BootValidationModeOption MapBootValidationBack(BootValidationMode mode)
    {
        switch (mode)
        {
            case BootValidationMode.Optional:
                return BootValidationModeOption.Optional;
            case BootValidationMode.Disabled:
                return BootValidationModeOption.Disabled;
            default:
                return BootValidationModeOption.Required;
        }
    }

}
