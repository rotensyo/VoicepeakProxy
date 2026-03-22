using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using BouyomiVoicepeakBridge.Shared;
using FNF.XmlSerializerSetting;

namespace Plugin_VoicepeakProxy
{
    // 設定状態を保持
    public sealed class PluginSettingsState : SettingsBase
    {
        private string _settingsPath;
        private FileLogger _logger;

        public PluginSettingsFile Settings;

        // XmlSerializer互換の既定コンストラクタ
        public PluginSettingsState()
        {
            _settingsPath = string.Empty;
            _logger = null;
            Settings = new PluginSettingsFile();
            Settings.Normalize();
        }

        // 実行時の保存先とロガーを設定
        internal void Configure(string settingsPath, FileLogger logger)
        {
            _settingsPath = settingsPath ?? string.Empty;
            _logger = logger;
        }

        // JSONから設定を読み込む
        public void LoadFromJson()
        {
            try
            {
                Settings = JsonFileStore.LoadOrDefault<PluginSettingsFile>(_settingsPath, CreateDefault);
                Settings.Normalize();
            }
            catch (Exception ex)
            {
                Settings = CreateDefault();
                if (_logger != null)
                {
                    _logger.Error("settings_load_failed", ex.Message);
                }
            }
        }

        // JSONへ設定を保存
        public void SaveToJson()
        {
            try
            {
                Settings.Normalize();
                JsonFileStore.Save(_settingsPath, Settings);
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.Error("settings_save_failed", ex.Message);
                }
            }
        }

        // 設定を既定値へ戻す
        public void ResetToDefaults()
        {
            Settings = CreateDefault();
        }

        public override void ReadSettings()
        {
            Settings.Normalize();
        }

        public override void WriteSettings()
        {
            Settings.Normalize();
            SaveToJson();
        }

        private static PluginSettingsFile CreateDefault()
        {
            PluginSettingsFile file = new PluginSettingsFile();
            file.Normalize();
            return file;
        }
    }

    // 設定画面データを提供
    internal sealed class PluginSettingFormData : ISettingFormData
    {
        private readonly PluginSettingsState _state;

        public string Title { get { return "VoicepeakProxyCore連携"; } }

        public bool ExpandAll { get { return false; } }

        public SettingsBase Setting { get { return _state; } }

        public GeneralTab P01General;
        public ServerTab P02Server;
        public AudioTab P03Audio;
        public PrepareTab P04Prepare;
        public UiTab P05Ui;
        public TextTransformTab P06TextTransform;
        public ValidationTab P07Validation;
        public DebugTab P08Debug;

        public PluginSettingFormData(PluginSettingsState state)
        {
            _state = state;
            P01General = new GeneralTab(_state);
            P02Server = new ServerTab(_state);
            P03Audio = new AudioTab(_state);
            P04Prepare = new PrepareTab(_state);
            P05Ui = new UiTab(_state);
            P06TextTransform = new TextTransformTab(_state);
            P07Validation = new ValidationTab(_state);
            P08Debug = new DebugTab(_state);
        }
    }

    // 共通タブ基底
    internal abstract class TabBase : ISettingPropertyGrid
    {
        protected readonly PluginSettingsState State;

        protected TabBase(PluginSettingsState state)
        {
            State = state;
        }

        public abstract string GetName();
    }

    // 一般設定タブ
    internal sealed class GeneralTab : TabBase
    {
        public GeneralTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "General";
        }

        [Category("Plugin")]
        [DisplayName("01)Pipe名")]
        [Description("PluginとWorkerの接続に使用するPipe名です")]
        public string PipeName
        {
            get { return State.Settings.Plugin.PipeName; }
            set { State.Settings.Plugin.PipeName = value ?? string.Empty; }
        }

        [Category("Plugin")]
        [DisplayName("02)Pipe接続タイムアウト(ms)")]
        [Description("Worker接続待ち時間をミリ秒で指定します")]
        public int PipeConnectTimeoutMs
        {
            get { return State.Settings.Plugin.PipeConnectTimeoutMs; }
            set { State.Settings.Plugin.PipeConnectTimeoutMs = value; }
        }

        [Category("Plugin")]
        [DisplayName("03)キュー上限")]
        [Description("Plugin内部キューの最大件数を指定します")]
        public int MaxQueueLength
        {
            get { return State.Settings.Plugin.MaxQueueLength; }
            set { State.Settings.Plugin.MaxQueueLength = value; }
        }

        [Category("Plugin")]
        [DisplayName("04)Worker実行ファイル")]
        [Description("空の場合はPluginと同じフォルダ配下のVoicepeakProxyWorker\\VoicepeakProxyWorker.exeを使用します")]
        public string WorkerExePath
        {
            get { return State.Settings.Plugin.WorkerExePath; }
            set { State.Settings.Plugin.WorkerExePath = value ?? string.Empty; }
        }
    }

    // Server設定タブ
    internal sealed class ServerTab : TabBase
    {
        public ServerTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Server";
        }

        [Category("Server")]
        [DisplayName("01)maxQueuedJobs")]
        public int MaxQueuedJobs
        {
            get { return State.Settings.AppConfig.Server.MaxQueuedJobs; }
            set { State.Settings.AppConfig.Server.MaxQueuedJobs = value; }
        }
    }

    // Audio設定タブ
    internal sealed class AudioTab : TabBase
    {
        public AudioTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Audio";
        }

        [Category("Audio")]
        [DisplayName("01)peakThreshold")]
        public float PeakThreshold
        {
            get { return State.Settings.AppConfig.Audio.PeakThreshold; }
            set { State.Settings.AppConfig.Audio.PeakThreshold = value; }
        }

        [Category("Audio")]
        [DisplayName("02)pollIntervalMs")]
        public int PollIntervalMs
        {
            get { return State.Settings.AppConfig.Audio.PollIntervalMs; }
            set { State.Settings.AppConfig.Audio.PollIntervalMs = value; }
        }

        [Category("Audio")]
        [DisplayName("03)startConfirmTimeoutMs")]
        public int StartConfirmTimeoutMs
        {
            get { return State.Settings.AppConfig.Audio.StartConfirmTimeoutMs; }
            set { State.Settings.AppConfig.Audio.StartConfirmTimeoutMs = value; }
        }

        [Category("Audio")]
        [DisplayName("04)startConfirmMaxRetries")]
        public int StartConfirmMaxRetries
        {
            get { return State.Settings.AppConfig.Audio.StartConfirmMaxRetries; }
            set { State.Settings.AppConfig.Audio.StartConfirmMaxRetries = value; }
        }

        [Category("Audio")]
        [DisplayName("05)stopConfirmMs")]
        public int StopConfirmMs
        {
            get { return State.Settings.AppConfig.Audio.StopConfirmMs; }
            set { State.Settings.AppConfig.Audio.StopConfirmMs = value; }
        }

        [Category("Audio")]
        [DisplayName("06)maxSpeakingDurationSec")]
        public int MaxSpeakingDurationSec
        {
            get { return State.Settings.AppConfig.Audio.MaxSpeakingDurationSec; }
            set { State.Settings.AppConfig.Audio.MaxSpeakingDurationSec = value; }
        }
    }

    // Prepare設定タブ
    internal sealed class PrepareTab : TabBase
    {
        public PrepareTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Prepare";
        }

        [Category("Prepare")]
        [DisplayName("01)bootValidationText")]
        public string BootValidationText
        {
            get { return State.Settings.AppConfig.Prepare.BootValidationText; }
            set { State.Settings.AppConfig.Prepare.BootValidationText = value ?? string.Empty; }
        }

        [Category("Prepare")]
        [DisplayName("02)bootValidationMaxRetries")]
        public int BootValidationMaxRetries
        {
            get { return State.Settings.AppConfig.Prepare.BootValidationMaxRetries; }
            set { State.Settings.AppConfig.Prepare.BootValidationMaxRetries = value; }
        }

        [Category("Prepare")]
        [DisplayName("03)bootValidationRetryIntervalMs")]
        public int BootValidationRetryIntervalMs
        {
            get { return State.Settings.AppConfig.Prepare.BootValidationRetryIntervalMs; }
            set { State.Settings.AppConfig.Prepare.BootValidationRetryIntervalMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("04)charDelayBaseMs")]
        public int CharDelayBaseMs
        {
            get { return State.Settings.AppConfig.Prepare.CharDelayBaseMs; }
            set { State.Settings.AppConfig.Prepare.CharDelayBaseMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("05)actionDelayMs")]
        public int ActionDelayMs
        {
            get { return State.Settings.AppConfig.Prepare.ActionDelayMs; }
            set { State.Settings.AppConfig.Prepare.ActionDelayMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("06)postTypeWaitPerCharMs")]
        public int PostTypeWaitPerCharMs
        {
            get { return State.Settings.AppConfig.Prepare.PostTypeWaitPerCharMs; }
            set { State.Settings.AppConfig.Prepare.PostTypeWaitPerCharMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("07)postTypeWaitMinMs")]
        public int PostTypeWaitMinMs
        {
            get { return State.Settings.AppConfig.Prepare.PostTypeWaitMinMs; }
            set { State.Settings.AppConfig.Prepare.PostTypeWaitMinMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("08)sequentialMoveToStartKeyDelayBaseMs")]
        public int SequentialMoveToStartKeyDelayBaseMs
        {
            get { return State.Settings.AppConfig.Prepare.SequentialMoveToStartKeyDelayBaseMs; }
            set { State.Settings.AppConfig.Prepare.SequentialMoveToStartKeyDelayBaseMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("09)deleteKeyDelayBaseMs")]
        public int DeleteKeyDelayBaseMs
        {
            get { return State.Settings.AppConfig.Prepare.DeleteKeyDelayBaseMs; }
            set { State.Settings.AppConfig.Prepare.DeleteKeyDelayBaseMs = value; }
        }

        [Category("Prepare")]
        [DisplayName("10)clearInputMaxPasses")]
        public int ClearInputMaxPasses
        {
            get { return State.Settings.AppConfig.Prepare.ClearInputMaxPasses; }
            set { State.Settings.AppConfig.Prepare.ClearInputMaxPasses = value; }
        }
    }

    // UI設定タブ
    internal sealed class UiTab : TabBase
    {
        public UiTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "UI";
        }

        [Category("UI")]
        [DisplayName("01)moveToStartShortcut")]
        public string MoveToStartShortcut
        {
            get { return State.Settings.AppConfig.Ui.MoveToStartShortcut; }
            set { State.Settings.AppConfig.Ui.MoveToStartShortcut = value ?? string.Empty; }
        }

        [Category("UI")]
        [DisplayName("02)playShortcut")]
        public string PlayShortcut
        {
            get { return State.Settings.AppConfig.Ui.PlayShortcut; }
            set { State.Settings.AppConfig.Ui.PlayShortcut = value ?? string.Empty; }
        }

        [Category("UI")]
        [DisplayName("03)delayBeforePlayShortcutMs")]
        public int DelayBeforePlayShortcutMs
        {
            get { return State.Settings.AppConfig.Ui.DelayBeforePlayShortcutMs; }
            set { State.Settings.AppConfig.Ui.DelayBeforePlayShortcutMs = value; }
        }

        [Category("UI")]
        [DisplayName("04)clickAtValidationEnabled")]
        public bool ClickAtValidationEnabled
        {
            get { return State.Settings.AppConfig.Ui.ClickAtValidationEnabled; }
            set { State.Settings.AppConfig.Ui.ClickAtValidationEnabled = value; }
        }

        [Category("UI")]
        [DisplayName("05)clickBeforeTextFocusWhenUninitializedEnabled")]
        public bool ClickBeforeTextFocusWhenUninitializedEnabled
        {
            get { return State.Settings.AppConfig.Ui.ClickBeforeTextFocusWhenUninitializedEnabled; }
            set { State.Settings.AppConfig.Ui.ClickBeforeTextFocusWhenUninitializedEnabled = value; }
        }

        [Category("UI")]
        [DisplayName("06)clickOnStartTimeoutRetryEnabled")]
        public bool ClickOnStartTimeoutRetryEnabled
        {
            get { return State.Settings.AppConfig.Ui.ClickOnStartTimeoutRetryEnabled; }
            set { State.Settings.AppConfig.Ui.ClickOnStartTimeoutRetryEnabled = value; }
        }

        [Category("UI")]
        [DisplayName("07)sendEnterAfterSentenceBreak")]
        public bool SendEnterAfterSentenceBreak
        {
            get { return State.Settings.AppConfig.Ui.SendEnterAfterSentenceBreak; }
            set { State.Settings.AppConfig.Ui.SendEnterAfterSentenceBreak = value; }
        }

        [Category("UI")]
        [DisplayName("08)sentenceBreakTriggers(改行区切り)")]
        [Description("1行に1トリガーを指定します")]
        [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string SentenceBreakTriggersText
        {
            get
            {
                return TabTextHelper.JoinLines(State.Settings.AppConfig.Ui.SentenceBreakTriggers);
            }
            set
            {
                State.Settings.AppConfig.Ui.SentenceBreakTriggers = TabTextHelper.SplitLines(value);
            }
        }
    }

    // 置換設定タブ
    internal sealed class TextTransformTab : TabBase
    {
        public TextTransformTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "TextTransform";
        }

        [Category("TextTransform")]
        [DisplayName("01)replaceRules(from=>to,改行区切り)")]
        [Description("例: 。=>。　。")]
        [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string ReplaceRulesText
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                List<ReplaceRuleData> rules = State.Settings.AppConfig.TextTransform.ReplaceRules;
                for (int i = 0; i < rules.Count; i++)
                {
                    ReplaceRuleData rule = rules[i] ?? new ReplaceRuleData();
                    if (builder.Length > 0)
                    {
                        builder.Append(Environment.NewLine);
                    }

                    builder.Append(rule.From ?? string.Empty);
                    builder.Append("=>");
                    builder.Append(rule.To ?? string.Empty);
                }

                return builder.ToString();
            }
            set
            {
                List<ReplaceRuleData> rules = new List<ReplaceRuleData>();
                string[] lines = TabTextHelper.SplitRawLines(value);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    int sep = line.IndexOf("=>", StringComparison.Ordinal);
                    if (sep < 0)
                    {
                        ReplaceRuleData plainRule = new ReplaceRuleData();
                        plainRule.From = line;
                        plainRule.To = string.Empty;
                        rules.Add(plainRule);
                        continue;
                    }

                    ReplaceRuleData rule = new ReplaceRuleData();
                    rule.From = line.Substring(0, sep);
                    rule.To = line.Substring(sep + 2);
                    rules.Add(rule);
                }

                State.Settings.AppConfig.TextTransform.ReplaceRules = rules;
            }
        }
    }

    // 検証設定タブ
    internal sealed class ValidationTab : TabBase
    {
        public ValidationTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Validation";
        }

        [Category("Validation")]
        [DisplayName("01)bootValidation")]
        public BootValidationModeOption BootValidation
        {
            get { return State.Settings.AppConfig.Validation.BootValidation; }
            set { State.Settings.AppConfig.Validation.BootValidation = value; }
        }

        [Category("Validation")]
        [DisplayName("02)requestValidation")]
        public RequestValidationModeOption RequestValidation
        {
            get { return State.Settings.AppConfig.Validation.RequestValidation; }
            set { State.Settings.AppConfig.Validation.RequestValidation = value; }
        }
    }

    // デバッグ設定タブ
    internal sealed class DebugTab : TabBase
    {
        public DebugTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Debug";
        }

        [Category("Debug")]
        [DisplayName("01)logTextCandidates")]
        public bool LogTextCandidates
        {
            get { return State.Settings.AppConfig.Debug.LogTextCandidates; }
            set { State.Settings.AppConfig.Debug.LogTextCandidates = value; }
        }
    }

    // 文字列表現の変換を提供
    internal static class TabTextHelper
    {
        // 行リストを改行結合
        internal static string JoinLines(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(Environment.NewLine);
                }

                builder.Append(values[i] ?? string.Empty);
            }

            return builder.ToString();
        }

        // 改行文字列を行リストへ変換
        internal static List<string> SplitLines(string value)
        {
            List<string> list = new List<string>();
            string[] lines = SplitRawLines(value);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                list.Add(line);
            }

            return list;
        }

        // 改行分割を共通化
        internal static string[] SplitRawLines(string value)
        {
            string text = value ?? string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }
}
