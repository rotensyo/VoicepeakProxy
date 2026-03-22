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
        public string WorkerExePath { get; set; }
        public int PipeConnectTimeoutMs { get; set; }
        public int MaxQueueLength { get; set; }

        public PluginRuntimeConfig()
        {
            PipeName = "voicepeak_proxycore_bridge";
            WorkerExePath = string.Empty;
            PipeConnectTimeoutMs = 1500;
            MaxQueueLength = 200;
        }
    }

    // コア設定のルート
    public sealed class AppConfigData
    {
        public ServerConfigData Server { get; set; }
        public AudioConfigData Audio { get; set; }
        public PrepareConfigData Prepare { get; set; }
        public UiConfigData Ui { get; set; }
        public TextTransformConfigData TextTransform { get; set; }
        public DebugConfigData Debug { get; set; }
        public ValidationConfigData Validation { get; set; }

        public AppConfigData()
        {
            Server = new ServerConfigData();
            Audio = new AudioConfigData();
            Prepare = new PrepareConfigData();
            Ui = new UiConfigData();
            TextTransform = new TextTransformConfigData();
            Debug = new DebugConfigData();
            Validation = new ValidationConfigData();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (Server == null)
            {
                Server = new ServerConfigData();
            }

            if (Audio == null)
            {
                Audio = new AudioConfigData();
            }

            if (Prepare == null)
            {
                Prepare = new PrepareConfigData();
            }

            if (Ui == null)
            {
                Ui = new UiConfigData();
            }

            if (TextTransform == null)
            {
                TextTransform = new TextTransformConfigData();
            }

            if (Debug == null)
            {
                Debug = new DebugConfigData();
            }

            if (Validation == null)
            {
                Validation = new ValidationConfigData();
            }

            Ui.Normalize();
            TextTransform.Normalize();
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

    // 検証関連設定
    public sealed class ValidationConfigData
    {
        public BootValidationModeOption BootValidation { get; set; }
        public RequestValidationModeOption RequestValidation { get; set; }
    }

    // サーバ関連設定
    public sealed class ServerConfigData
    {
        public int MaxQueuedJobs { get; set; }
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

    // 入力準備関連設定
    public sealed class PrepareConfigData
    {
        public string BootValidationText { get; set; }
        public int BootValidationMaxRetries { get; set; }
        public int BootValidationRetryIntervalMs { get; set; }
        public int CharDelayBaseMs { get; set; }
        public int ActionDelayMs { get; set; }
        public int PostTypeWaitPerCharMs { get; set; }
        public int PostTypeWaitMinMs { get; set; }
        public int SequentialMoveToStartKeyDelayBaseMs { get; set; }
        public int DeleteKeyDelayBaseMs { get; set; }
        public int ClearInputMaxPasses { get; set; }
    }

    // UI関連設定
    public sealed class UiConfigData
    {
        public string MoveToStartShortcut { get; set; }
        public string PlayShortcut { get; set; }
        public int DelayBeforePlayShortcutMs { get; set; }
        public bool ClickAtValidationEnabled { get; set; }
        public bool ClickBeforeTextFocusWhenUninitializedEnabled { get; set; }
        public bool ClickOnStartTimeoutRetryEnabled { get; set; }
        public bool SendEnterAfterSentenceBreak { get; set; }
        public List<string> SentenceBreakTriggers { get; set; }

        public UiConfigData()
        {
            SentenceBreakTriggers = new List<string>();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (SentenceBreakTriggers == null)
            {
                SentenceBreakTriggers = new List<string>();
            }
        }
    }

    // デバッグ設定
    public sealed class DebugConfigData
    {
        public bool LogTextCandidates { get; set; }
    }

    // 置換設定
    public sealed class TextTransformConfigData
    {
        public List<ReplaceRuleData> ReplaceRules { get; set; }

        public TextTransformConfigData()
        {
            ReplaceRules = new List<ReplaceRuleData>();
        }

        // 不足項目を既定値で補完
        public void Normalize()
        {
            if (ReplaceRules == null)
            {
                ReplaceRules = new List<ReplaceRuleData>();
            }
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
