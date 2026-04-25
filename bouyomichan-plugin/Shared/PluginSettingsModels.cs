using System.Collections.Generic;

namespace BouyomiVoicepeakBridge.Shared
{
    // 設定ファイル全体を保持
    public sealed class PluginSettingsFile
    {
        public PluginRuntimeConfig Plugin { get; set; }
        public AppConfigData AppConfig { get; set; }

        public PluginSettingsFile()
        {
            Plugin = new PluginRuntimeConfig();
            AppConfig = new AppConfigData();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (Plugin == null)
            {
                Plugin = new PluginRuntimeConfig();
            }

            Plugin.Normalize();

            if (AppConfig == null)
            {
                AppConfig = new AppConfigData();
            }

            AppConfig.Normalize();
        }
    }

    // プラグインとワーカーの連携設定
    public sealed class PluginRuntimeConfig
    {
        public string PipeName { get; set; }
        public int PipeConnectTimeoutMs { get; set; }
        public string VoicepeakExePath { get; set; }
        public string VoicepeakTemplatePath { get; set; }

        public PluginRuntimeConfig()
        {
            PipeName = "voicepeak_proxy_bridge";
            PipeConnectTimeoutMs = 1500;
            VoicepeakExePath = string.Empty;
            VoicepeakTemplatePath = string.Empty;
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (PipeName == null)
            {
                PipeName = string.Empty;
            }

            if (VoicepeakExePath == null)
            {
                VoicepeakExePath = string.Empty;
            }

            if (VoicepeakTemplatePath == null)
            {
                VoicepeakTemplatePath = string.Empty;
            }
        }
    }

    // コア設定のルート
    public sealed class AppConfigData
    {
        public ValidationConfigData Validation { get; set; }
        public UiConfigData Ui { get; set; }
        public InputTimingConfigData InputTiming { get; set; }
        public TextConfigData Text { get; set; }
        public AudioConfigData Audio { get; set; }
        public RuntimeConfigData Runtime { get; set; }
        public HookConfigData Hook { get; set; }
        public DebugConfigData Debug { get; set; }

        public AppConfigData()
        {
            Validation = new ValidationConfigData();
            Ui = new UiConfigData();
            InputTiming = new InputTimingConfigData();
            Text = new TextConfigData();
            Audio = new AudioConfigData();
            Runtime = new RuntimeConfigData();
            Hook = new HookConfigData();
            Debug = new DebugConfigData();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (Validation == null)
            {
                Validation = new ValidationConfigData();
            }

            if (Ui == null)
            {
                Ui = new UiConfigData();
            }

            if (InputTiming == null)
            {
                InputTiming = new InputTimingConfigData();
            }

            if (Audio == null)
            {
                Audio = new AudioConfigData();
            }

            if (Text == null)
            {
                Text = new TextConfigData();
            }

            if (Runtime == null)
            {
                Runtime = new RuntimeConfigData();
            }

            if (Hook == null)
            {
                Hook = new HookConfigData();
            }

            if (Debug == null)
            {
                Debug = new DebugConfigData();
            }

            Validation.Normalize();
            Text.Normalize();
            Debug.Normalize();
        }
    }

    // 起動時検証方針
    public enum BootValidationModeOption
    {
        Required,
        Optional,
        Disabled
    }

    // 検証関連設定
    public sealed class ValidationConfigData
    {
        public string ValidationText { get; set; }
        public int ValidationMaxRetries { get; set; }
        public int ValidationRetryIntervalMs { get; set; }

        public ValidationConfigData()
        {
            ValidationText = "初期化完了";
            ValidationMaxRetries = 2;
            ValidationRetryIntervalMs = 1000;
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (ValidationText == null)
            {
                ValidationText = string.Empty;
            }
        }
    }

    // フック関連設定
    public sealed class HookConfigData
    {
        public int HookCommandTimeoutMs { get; set; }
        public int HookConnectTimeoutMs { get; set; }
        public int HookConnectTotalWaitMs { get; set; }
    }

    // UI関連設定
public sealed class UiConfigData
{
    public string MoveToStartModifier { get; set; }
    public string MoveToStartKey { get; set; }
    public string ClearInputSelectAllModifier { get; set; }
    public string ClearInputSelectAllKey { get; set; }
    public string PasteShortcutModifier { get; set; }
    public string PasteShortcutKey { get; set; }
    public string PlayShortcutModifier { get; set; }
    public string PlayShortcutKey { get; set; }
    public int DelayBeforePlayShortcutMs { get; set; }
}

    // 入力タイミング関連設定
    public sealed class InputTimingConfigData
    {
        public int TypeTextRetryWaitMs { get; set; }
        public int TypeTextRetryMaxRetries { get; set; }
        public int ClearInputRetryWaitMs { get; set; }
        public int ClearInputRetryMaxRetries { get; set; }
        public int ActionDelayMs { get; set; }
        public int PostTypeWaitPerCharMs { get; set; }
        public int PostTypeWaitMinMs { get; set; }
        public int ClearInputMaxPasses { get; set; }
    }

    // 音声監視関連設定
    public sealed class AudioConfigData
    {
        public float PeakThreshold { get; set; }
        public int PollIntervalMs { get; set; }
        public int StartConfirmTimeoutMs { get; set; }
        public int StartConfirmMaxRetries { get; set; }
        public int StopConfirmMs { get; set; }
        public int MaxSpeakingDurationSec { get; set; }
    }

    // テキスト処理設定
    public sealed class TextConfigData
    {
        public List<ReplaceRuleData> ReplaceRules { get; set; }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (ReplaceRules == null)
            {
                ReplaceRules = new List<ReplaceRuleData>();
            }
        }
    }

    // 実行制御関連設定
    public sealed class RuntimeConfigData
    {
        public int MaxQueuedJobs { get; set; }
        public BootValidationModeOption BootValidation { get; set; }
    }

    // デバッグ設定
    public sealed class DebugConfigData
    {
        // 旧設定互換のため受理するが無視する
        public bool LogTextCandidates { get; set; }
        public bool LogModifierHookStats { get; set; }
        public int UiaProbeRecycleIntervalSec { get; set; }
        public string LogMinimumLevel { get; set; }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            string raw = LogMinimumLevel ?? string.Empty;
            string normalized = raw.Trim().ToLowerInvariant();
            if (normalized == "debug"
                || normalized == "info"
                || normalized == "warn"
                || normalized == "error")
            {
                LogMinimumLevel = normalized;
                return;
            }

            LogMinimumLevel = string.Empty;
        }
    }

    // 置換ルール
    public sealed class ReplaceRuleData
    {
        public string From { get; set; }
        public string To { get; set; }

        public ReplaceRuleData()
        {
            From = string.Empty;
            To = string.Empty;
        }
    }
}
