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

        config.Server.MaxQueuedJobs = data.Server.MaxQueuedJobs;

        config.Audio.PeakThreshold = data.Audio.PeakThreshold;
        config.Audio.PollIntervalMs = data.Audio.PollIntervalMs;
        config.Audio.StartConfirmTimeoutMs = data.Audio.StartConfirmTimeoutMs;
        config.Audio.StartConfirmMaxRetries = data.Audio.StartConfirmMaxRetries;
        config.Audio.StopConfirmMs = data.Audio.StopConfirmMs;
        config.Audio.MaxSpeakingDurationSec = data.Audio.MaxSpeakingDurationSec;

        config.Prepare.BootValidationText = data.Prepare.BootValidationText ?? string.Empty;
        config.Prepare.BootValidationMaxRetries = data.Prepare.BootValidationMaxRetries;
        config.Prepare.BootValidationRetryIntervalMs = data.Prepare.BootValidationRetryIntervalMs;
        config.Prepare.CharDelayBaseMs = data.Prepare.CharDelayBaseMs;
        config.Prepare.ActionDelayMs = data.Prepare.ActionDelayMs;
        config.Prepare.PostTypeWaitPerCharMs = data.Prepare.PostTypeWaitPerCharMs;
        config.Prepare.PostTypeWaitMinMs = data.Prepare.PostTypeWaitMinMs;
        config.Prepare.SequentialMoveToStartKeyDelayBaseMs = data.Prepare.SequentialMoveToStartKeyDelayBaseMs;
        config.Prepare.DeleteKeyDelayBaseMs = data.Prepare.DeleteKeyDelayBaseMs;
        config.Prepare.ClearInputMaxPasses = data.Prepare.ClearInputMaxPasses;

        config.Ui.MoveToStartShortcut = data.Ui.MoveToStartShortcut ?? string.Empty;
        config.Ui.PlayShortcut = data.Ui.PlayShortcut ?? string.Empty;
        config.Ui.DelayBeforePlayShortcutMs = data.Ui.DelayBeforePlayShortcutMs;
        config.Ui.ClickAtValidationEnabled = data.Ui.ClickAtValidationEnabled;
        config.Ui.ClickBeforeTextFocusWhenUninitializedEnabled = data.Ui.ClickBeforeTextFocusWhenUninitializedEnabled;
        config.Ui.ClickOnStartTimeoutRetryEnabled = data.Ui.ClickOnStartTimeoutRetryEnabled;
        config.Ui.SendEnterAfterSentenceBreak = data.Ui.SendEnterAfterSentenceBreak;
        config.Ui.SentenceBreakTriggers = new List<string>();
        for (int i = 0; i < data.Ui.SentenceBreakTriggers.Count; i++)
        {
            string token = data.Ui.SentenceBreakTriggers[i] ?? string.Empty;
            config.Ui.SentenceBreakTriggers.Add(token);
        }

        config.TextTransform.ReplaceRules = new List<ReplaceRule>();
        for (int i = 0; i < data.TextTransform.ReplaceRules.Count; i++)
        {
            ReplaceRuleData rule = data.TextTransform.ReplaceRules[i] ?? new ReplaceRuleData();
            config.TextTransform.ReplaceRules.Add(new ReplaceRule
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        config.Debug.LogTextCandidates = data.Debug.LogTextCandidates;

        config.Validation.BootValidation = MapBootValidation(data.Validation.BootValidation);
        config.Validation.RequestValidation = MapRequestValidation(data.Validation.RequestValidation);

        return config;
    }

    // core既定設定をJSONモデルへ変換
    public static AppConfigData MapFromCoreDefaults(AppConfig source)
    {
        AppConfig config = source ?? new AppConfig();
        AppConfigData data = new AppConfigData();

        data.Server.MaxQueuedJobs = config.Server.MaxQueuedJobs;

        data.Audio.PeakThreshold = config.Audio.PeakThreshold;
        data.Audio.PollIntervalMs = config.Audio.PollIntervalMs;
        data.Audio.StartConfirmTimeoutMs = config.Audio.StartConfirmTimeoutMs;
        data.Audio.StartConfirmMaxRetries = config.Audio.StartConfirmMaxRetries;
        data.Audio.StopConfirmMs = config.Audio.StopConfirmMs;
        data.Audio.MaxSpeakingDurationSec = config.Audio.MaxSpeakingDurationSec;

        data.Prepare.BootValidationText = config.Prepare.BootValidationText ?? string.Empty;
        data.Prepare.BootValidationMaxRetries = config.Prepare.BootValidationMaxRetries;
        data.Prepare.BootValidationRetryIntervalMs = config.Prepare.BootValidationRetryIntervalMs;
        data.Prepare.CharDelayBaseMs = config.Prepare.CharDelayBaseMs;
        data.Prepare.ActionDelayMs = config.Prepare.ActionDelayMs;
        data.Prepare.PostTypeWaitPerCharMs = config.Prepare.PostTypeWaitPerCharMs;
        data.Prepare.PostTypeWaitMinMs = config.Prepare.PostTypeWaitMinMs;
        data.Prepare.SequentialMoveToStartKeyDelayBaseMs = config.Prepare.SequentialMoveToStartKeyDelayBaseMs;
        data.Prepare.DeleteKeyDelayBaseMs = config.Prepare.DeleteKeyDelayBaseMs;
        data.Prepare.ClearInputMaxPasses = config.Prepare.ClearInputMaxPasses;

        data.Ui.MoveToStartShortcut = config.Ui.MoveToStartShortcut ?? string.Empty;
        data.Ui.PlayShortcut = config.Ui.PlayShortcut ?? string.Empty;
        data.Ui.DelayBeforePlayShortcutMs = config.Ui.DelayBeforePlayShortcutMs;
        data.Ui.ClickAtValidationEnabled = config.Ui.ClickAtValidationEnabled;
        data.Ui.ClickBeforeTextFocusWhenUninitializedEnabled = config.Ui.ClickBeforeTextFocusWhenUninitializedEnabled;
        data.Ui.ClickOnStartTimeoutRetryEnabled = config.Ui.ClickOnStartTimeoutRetryEnabled;
        data.Ui.SendEnterAfterSentenceBreak = config.Ui.SendEnterAfterSentenceBreak;
        data.Ui.SentenceBreakTriggers = new List<string>();
        for (int i = 0; i < config.Ui.SentenceBreakTriggers.Count; i++)
        {
            data.Ui.SentenceBreakTriggers.Add(config.Ui.SentenceBreakTriggers[i] ?? string.Empty);
        }

        data.TextTransform.ReplaceRules = new List<ReplaceRuleData>();
        for (int i = 0; i < config.TextTransform.ReplaceRules.Count; i++)
        {
            ReplaceRule rule = config.TextTransform.ReplaceRules[i] ?? new ReplaceRule();
            data.TextTransform.ReplaceRules.Add(new ReplaceRuleData
            {
                From = rule.From ?? string.Empty,
                To = rule.To ?? string.Empty
            });
        }

        data.Debug.LogTextCandidates = config.Debug.LogTextCandidates;
        data.Validation.BootValidation = MapBootValidationBack(config.Validation.BootValidation);
        data.Validation.RequestValidation = MapRequestValidationBack(config.Validation.RequestValidation);

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

    // リクエスト検証方針を変換
    private static RequestValidationMode MapRequestValidation(RequestValidationModeOption mode)
    {
        switch (mode)
        {
            case RequestValidationModeOption.Lenient:
                return RequestValidationMode.Lenient;
            case RequestValidationModeOption.Disabled:
                return RequestValidationMode.Disabled;
            default:
                return RequestValidationMode.Strict;
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

    // リクエスト検証方針を逆変換
    private static RequestValidationModeOption MapRequestValidationBack(RequestValidationMode mode)
    {
        switch (mode)
        {
            case RequestValidationMode.Lenient:
                return RequestValidationModeOption.Lenient;
            case RequestValidationMode.Disabled:
                return RequestValidationModeOption.Disabled;
            default:
                return RequestValidationModeOption.Strict;
        }
    }
}
