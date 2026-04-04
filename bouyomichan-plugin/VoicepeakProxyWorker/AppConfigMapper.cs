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

        config.Startup.BootValidationText = data.Startup.BootValidationText ?? string.Empty;
        config.Startup.BootValidationMaxRetries = data.Startup.BootValidationMaxRetries;
        config.Startup.BootValidationRetryIntervalMs = data.Startup.BootValidationRetryIntervalMs;

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

        config.Deprecated.EnableLegacyPrimeInputClick = data.Deprecated.EnableLegacyPrimeInputClick;
        config.Deprecated.LegacyPrimeClickAtValidationEnabled = data.Deprecated.LegacyPrimeClickAtValidationEnabled;
        config.Deprecated.LegacyPrimeClickBeforeTextFocusWhenUninitializedEnabled = data.Deprecated.LegacyPrimeClickBeforeTextFocusWhenUninitializedEnabled;
        config.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled = data.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled;

        config.InputTiming.CharDelayBaseMs = data.InputTiming.CharDelayBaseMs;
        config.InputTiming.DeleteKeyDelayBaseMs = data.InputTiming.DeleteKeyDelayBaseMs;
        config.InputTiming.ActionDelayMs = data.InputTiming.ActionDelayMs;
        config.InputTiming.SequentialMoveToStartKeyDelayBaseMs = data.InputTiming.SequentialMoveToStartKeyDelayBaseMs;
        config.InputTiming.PostTypeWaitPerCharMs = data.InputTiming.PostTypeWaitPerCharMs;
        config.InputTiming.PostTypeWaitMinMs = data.InputTiming.PostTypeWaitMinMs;
        config.InputTiming.ClearInputMaxPasses = data.InputTiming.ClearInputMaxPasses;

        config.Audio.PeakThreshold = data.Audio.PeakThreshold;
        config.Audio.PollIntervalMs = data.Audio.PollIntervalMs;
        config.Audio.StartConfirmTimeoutMs = data.Audio.StartConfirmTimeoutMs;
        config.Audio.StartConfirmMaxRetries = data.Audio.StartConfirmMaxRetries;
        config.Audio.StopConfirmMs = data.Audio.StopConfirmMs;
        config.Audio.MaxSpeakingDurationSec = data.Audio.MaxSpeakingDurationSec;

        config.Text.SendEnterAfterSentenceBreak = data.Text.SendEnterAfterSentenceBreak;
        config.Text.SentenceBreakTriggers = new List<string>();
        for (int i = 0; i < data.Text.SentenceBreakTriggers.Count; i++)
        {
            string token = data.Text.SentenceBreakTriggers[i] ?? string.Empty;
            config.Text.SentenceBreakTriggers.Add(token);
        }

        config.Text.ReplaceRules = new List<ReplaceRule>();
        for (int i = 0; i < data.Text.ReplaceRules.Count; i++)
        {
            ReplaceRuleData rule = data.Text.ReplaceRules[i] ?? new ReplaceRuleData();
            config.Text.ReplaceRules.Add(new ReplaceRule
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        config.Queue.MaxQueuedJobs = data.Queue.MaxQueuedJobs;
        config.Validation.BootValidation = MapBootValidation(data.Validation.BootValidation);

        config.Debug.LogTextCandidates = data.Debug.LogTextCandidates;
        config.Debug.LogModifierHookStats = data.Debug.LogModifierHookStats;

        return config;
    }

    // core既定設定をJSONモデルへ変換
    public static AppConfigData MapFromCoreDefaults(AppConfig source)
    {
        AppConfig config = source ?? new AppConfig();
        AppConfigData data = new AppConfigData();

        data.Startup.BootValidationText = config.Startup.BootValidationText ?? string.Empty;
        data.Startup.BootValidationMaxRetries = config.Startup.BootValidationMaxRetries;
        data.Startup.BootValidationRetryIntervalMs = config.Startup.BootValidationRetryIntervalMs;

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

        data.Deprecated.EnableLegacyPrimeInputClick = config.Deprecated.EnableLegacyPrimeInputClick;
        data.Deprecated.LegacyPrimeClickAtValidationEnabled = config.Deprecated.LegacyPrimeClickAtValidationEnabled;
        data.Deprecated.LegacyPrimeClickBeforeTextFocusWhenUninitializedEnabled = config.Deprecated.LegacyPrimeClickBeforeTextFocusWhenUninitializedEnabled;
        data.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled = config.Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled;

        data.InputTiming.CharDelayBaseMs = config.InputTiming.CharDelayBaseMs;
        data.InputTiming.DeleteKeyDelayBaseMs = config.InputTiming.DeleteKeyDelayBaseMs;
        data.InputTiming.ActionDelayMs = config.InputTiming.ActionDelayMs;
        data.InputTiming.SequentialMoveToStartKeyDelayBaseMs = config.InputTiming.SequentialMoveToStartKeyDelayBaseMs;
        data.InputTiming.PostTypeWaitPerCharMs = config.InputTiming.PostTypeWaitPerCharMs;
        data.InputTiming.PostTypeWaitMinMs = config.InputTiming.PostTypeWaitMinMs;
        data.InputTiming.ClearInputMaxPasses = config.InputTiming.ClearInputMaxPasses;

        data.Audio.PeakThreshold = config.Audio.PeakThreshold;
        data.Audio.PollIntervalMs = config.Audio.PollIntervalMs;
        data.Audio.StartConfirmTimeoutMs = config.Audio.StartConfirmTimeoutMs;
        data.Audio.StartConfirmMaxRetries = config.Audio.StartConfirmMaxRetries;
        data.Audio.StopConfirmMs = config.Audio.StopConfirmMs;
        data.Audio.MaxSpeakingDurationSec = config.Audio.MaxSpeakingDurationSec;

        data.Text.SendEnterAfterSentenceBreak = config.Text.SendEnterAfterSentenceBreak;
        data.Text.SentenceBreakTriggers = new List<string>();
        for (int i = 0; i < config.Text.SentenceBreakTriggers.Count; i++)
        {
            data.Text.SentenceBreakTriggers.Add(config.Text.SentenceBreakTriggers[i] ?? string.Empty);
        }

        data.Text.ReplaceRules = new List<ReplaceRuleData>();
        for (int i = 0; i < config.Text.ReplaceRules.Count; i++)
        {
            ReplaceRule rule = config.Text.ReplaceRules[i] ?? new ReplaceRule();
            data.Text.ReplaceRules.Add(new ReplaceRuleData
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        data.Queue.MaxQueuedJobs = config.Queue.MaxQueuedJobs;

        data.Validation.BootValidation = MapBootValidationBack(config.Validation.BootValidation);

        data.Debug.LogTextCandidates = config.Debug.LogTextCandidates;
        data.Debug.LogModifierHookStats = config.Debug.LogModifierHookStats;

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
