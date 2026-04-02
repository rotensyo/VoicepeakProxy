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

        public PluginRuntimeConfig()
        {
            PipeName = "voicepeak_proxy_bridge";
            PipeConnectTimeoutMs = 1500;
        }
    }

    // コア設定のルート
    public sealed class AppConfigData
    {
        public StartupConfigData Startup { get; set; }
        public HookConfigData Hook { get; set; }
        public UiConfigData Ui { get; set; }
        public InputTimingConfigData InputTiming { get; set; }
        public AudioConfigData Audio { get; set; }
        public TextConfigData Text { get; set; }
        public QueueConfigData Queue { get; set; }
        public ValidationConfigData Validation { get; set; }
        public DebugConfigData Debug { get; set; }

        public AppConfigData()
        {
            Startup = new StartupConfigData();
            Hook = new HookConfigData();
            Ui = new UiConfigData();
            InputTiming = new InputTimingConfigData();
            Audio = new AudioConfigData();
            Text = new TextConfigData();
            Queue = new QueueConfigData();
            Validation = new ValidationConfigData();
            Debug = new DebugConfigData();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (Startup == null)
            {
                Startup = new StartupConfigData();
            }

            if (Hook == null)
            {
                Hook = new HookConfigData();
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

            if (Queue == null)
            {
                Queue = new QueueConfigData();
            }

            if (Validation == null)
            {
                Validation = new ValidationConfigData();
            }

            if (Debug == null)
            {
                Debug = new DebugConfigData();
            }

            Text.Normalize();
        }
    }

    // 起動時検証方針
    public enum BootValidationModeOption
    {
        Required,
        Optional,
        Disabled
    }

    // リクエスト検証方針
    public enum RequestValidationModeOption
    {
        Strict,
        Lenient,
        Disabled
    }

    // 起動時処理関連設定
    public sealed class StartupConfigData
    {
        public string BootValidationText { get; set; }
        public int BootValidationMaxRetries { get; set; }
        public int BootValidationRetryIntervalMs { get; set; }
        public bool ClickAtValidationEnabled { get; set; }
        public bool ClickBeforeTextFocusWhenUninitializedEnabled { get; set; }
        public bool ClickOnStartTimeoutRetryEnabled { get; set; }
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
        public string MoveToStartShortcut { get; set; }
        public string PlayShortcut { get; set; }
        public int DelayBeforePlayShortcutMs { get; set; }
    }

    // 入力タイミング関連設定
    public sealed class InputTimingConfigData
    {
        public int CharDelayBaseMs { get; set; }
        public int DeleteKeyDelayBaseMs { get; set; }
        public int ActionDelayMs { get; set; }
        public int SequentialMoveToStartKeyDelayBaseMs { get; set; }
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
        public bool SendEnterAfterSentenceBreak { get; set; }
        public List<string> SentenceBreakTriggers { get; set; }
        public List<ReplaceRuleData> ReplaceRules { get; set; }

        public TextConfigData()
        {
            SentenceBreakTriggers = new List<string>();
            ReplaceRules = new List<ReplaceRuleData>();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (SentenceBreakTriggers == null)
            {
                SentenceBreakTriggers = new List<string>();
            }

            if (ReplaceRules == null)
            {
                ReplaceRules = new List<ReplaceRuleData>();
            }
        }
    }

    // キュー関連設定
    public sealed class QueueConfigData
    {
        public int MaxQueuedJobs { get; set; }
    }

    // 検証関連設定
    public sealed class ValidationConfigData
    {
        public BootValidationModeOption BootValidation { get; set; }
        public RequestValidationModeOption RequestValidation { get; set; }
    }

    // デバッグ設定
    public sealed class DebugConfigData
    {
        public bool LogTextCandidates { get; set; }
        public bool LogModifierHookStats { get; set; }
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
