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
        public StartupTab P02Startup;
        public HookTab P03Hook;
        public UiTab P04Ui;
        public InputTimingTab P05InputTiming;
        public AudioTab P06Audio;
        public TextTab P07Text;
        public QueueTab P08Queue;
        public ValidationTab P09Validation;
        public DebugTab P10Debug;

        public PluginSettingFormData(PluginSettingsState state)
        {
            _state = state;
            P01General = new GeneralTab(_state);
            P02Startup = new StartupTab(_state);
            P03Hook = new HookTab(_state);
            P04Ui = new UiTab(_state);
            P05InputTiming = new InputTimingTab(_state);
            P06Audio = new AudioTab(_state);
            P07Text = new TextTab(_state);
            P08Queue = new QueueTab(_state);
            P09Validation = new ValidationTab(_state);
            P10Debug = new DebugTab(_state);
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

    }

    // Queue設定タブ
    internal sealed class QueueTab : TabBase
    {
        public QueueTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Queue";
        }

        [Category("Queue")]
        [DisplayName("01)maxQueuedJobs")]
        public int MaxQueuedJobs
        {
            get { return State.Settings.AppConfig.Queue.MaxQueuedJobs; }
            set { State.Settings.AppConfig.Queue.MaxQueuedJobs = value; }
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

    // Startup設定タブ
    internal sealed class StartupTab : TabBase
    {
        public StartupTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Startup";
        }

        [Category("Startup")]
        [DisplayName("01)bootValidationText")]
        public string BootValidationText
        {
            get { return State.Settings.AppConfig.Startup.BootValidationText; }
            set { State.Settings.AppConfig.Startup.BootValidationText = value ?? string.Empty; }
        }

        [Category("Startup")]
        [DisplayName("02)bootValidationMaxRetries")]
        public int BootValidationMaxRetries
        {
            get { return State.Settings.AppConfig.Startup.BootValidationMaxRetries; }
            set { State.Settings.AppConfig.Startup.BootValidationMaxRetries = value; }
        }

        [Category("Startup")]
        [DisplayName("03)bootValidationRetryIntervalMs")]
        public int BootValidationRetryIntervalMs
        {
            get { return State.Settings.AppConfig.Startup.BootValidationRetryIntervalMs; }
            set { State.Settings.AppConfig.Startup.BootValidationRetryIntervalMs = value; }
        }

        [Category("Startup")]
        [DisplayName("04)clickAtValidationEnabled")]
        public bool ClickAtValidationEnabled
        {
            get { return State.Settings.AppConfig.Startup.ClickAtValidationEnabled; }
            set { State.Settings.AppConfig.Startup.ClickAtValidationEnabled = value; }
        }

        [Category("Startup")]
        [DisplayName("05)clickBeforeTextFocusWhenUninitializedEnabled")]
        public bool ClickBeforeTextFocusWhenUninitializedEnabled
        {
            get { return State.Settings.AppConfig.Startup.ClickBeforeTextFocusWhenUninitializedEnabled; }
            set { State.Settings.AppConfig.Startup.ClickBeforeTextFocusWhenUninitializedEnabled = value; }
        }

        [Category("Startup")]
        [DisplayName("06)clickOnStartTimeoutRetryEnabled")]
        public bool ClickOnStartTimeoutRetryEnabled
        {
            get { return State.Settings.AppConfig.Startup.ClickOnStartTimeoutRetryEnabled; }
            set { State.Settings.AppConfig.Startup.ClickOnStartTimeoutRetryEnabled = value; }
        }
    }

    // Hook設定タブ
    internal sealed class HookTab : TabBase
    {
        public HookTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Hook";
        }

        [Category("Hook")]
        [DisplayName("01)hookCommandTimeoutMs")]
        public int HookCommandTimeoutMs
        {
            get { return State.Settings.AppConfig.Hook.HookCommandTimeoutMs; }
            set { State.Settings.AppConfig.Hook.HookCommandTimeoutMs = value; }
        }

        [Category("Hook")]
        [DisplayName("02)hookConnectTimeoutMs")]
        public int HookConnectTimeoutMs
        {
            get { return State.Settings.AppConfig.Hook.HookConnectTimeoutMs; }
            set { State.Settings.AppConfig.Hook.HookConnectTimeoutMs = value; }
        }

        [Category("Hook")]
        [DisplayName("03)hookConnectTotalWaitMs")]
        public int HookConnectTotalWaitMs
        {
            get { return State.Settings.AppConfig.Hook.HookConnectTotalWaitMs; }
            set { State.Settings.AppConfig.Hook.HookConnectTotalWaitMs = value; }
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
            return "Ui";
        }

        [Category("Ui")]
        [DisplayName("01)moveToStartShortcut")]
        public string MoveToStartShortcut
        {
            get { return State.Settings.AppConfig.Ui.MoveToStartShortcut; }
            set { State.Settings.AppConfig.Ui.MoveToStartShortcut = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("02)playShortcut")]
        public string PlayShortcut
        {
            get { return State.Settings.AppConfig.Ui.PlayShortcut; }
            set { State.Settings.AppConfig.Ui.PlayShortcut = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("03)delayBeforePlayShortcutMs")]
        public int DelayBeforePlayShortcutMs
        {
            get { return State.Settings.AppConfig.Ui.DelayBeforePlayShortcutMs; }
            set { State.Settings.AppConfig.Ui.DelayBeforePlayShortcutMs = value; }
        }
    }

    // InputTiming設定タブ
    internal sealed class InputTimingTab : TabBase
    {
        public InputTimingTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "InputTiming";
        }

        [Category("InputTiming")]
        [DisplayName("01)charDelayBaseMs")]
        public int CharDelayBaseMs
        {
            get { return State.Settings.AppConfig.InputTiming.CharDelayBaseMs; }
            set { State.Settings.AppConfig.InputTiming.CharDelayBaseMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("02)deleteKeyDelayBaseMs")]
        public int DeleteKeyDelayBaseMs
        {
            get { return State.Settings.AppConfig.InputTiming.DeleteKeyDelayBaseMs; }
            set { State.Settings.AppConfig.InputTiming.DeleteKeyDelayBaseMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("03)actionDelayMs")]
        public int ActionDelayMs
        {
            get { return State.Settings.AppConfig.InputTiming.ActionDelayMs; }
            set { State.Settings.AppConfig.InputTiming.ActionDelayMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("04)sequentialMoveToStartKeyDelayBaseMs")]
        public int SequentialMoveToStartKeyDelayBaseMs
        {
            get { return State.Settings.AppConfig.InputTiming.SequentialMoveToStartKeyDelayBaseMs; }
            set { State.Settings.AppConfig.InputTiming.SequentialMoveToStartKeyDelayBaseMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("05)postTypeWaitPerCharMs")]
        public int PostTypeWaitPerCharMs
        {
            get { return State.Settings.AppConfig.InputTiming.PostTypeWaitPerCharMs; }
            set { State.Settings.AppConfig.InputTiming.PostTypeWaitPerCharMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("06)postTypeWaitMinMs")]
        public int PostTypeWaitMinMs
        {
            get { return State.Settings.AppConfig.InputTiming.PostTypeWaitMinMs; }
            set { State.Settings.AppConfig.InputTiming.PostTypeWaitMinMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("07)clearInputMaxPasses")]
        public int ClearInputMaxPasses
        {
            get { return State.Settings.AppConfig.InputTiming.ClearInputMaxPasses; }
            set { State.Settings.AppConfig.InputTiming.ClearInputMaxPasses = value; }
        }
    }

    // Text設定タブ
    internal sealed class TextTab : TabBase
    {
        public TextTab(PluginSettingsState state) : base(state)
        {
        }

        public override string GetName()
        {
            return "Text";
        }

        [Category("Text")]
        [DisplayName("01)sendEnterAfterSentenceBreak")]
        public bool SendEnterAfterSentenceBreak
        {
            get { return State.Settings.AppConfig.Text.SendEnterAfterSentenceBreak; }
            set { State.Settings.AppConfig.Text.SendEnterAfterSentenceBreak = value; }
        }

        [Category("Text")]
        [DisplayName("02)sentenceBreakTriggers(改行区切り)")]
        [Description("1行に1トリガーを指定します")]
        [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string SentenceBreakTriggersText
        {
            get
            {
                return TabTextHelper.JoinLines(State.Settings.AppConfig.Text.SentenceBreakTriggers);
            }
            set
            {
                State.Settings.AppConfig.Text.SentenceBreakTriggers = TabTextHelper.SplitLines(value);
            }
        }

        [Category("Text")]
        [DisplayName("03)replaceRules(from=>to,改行区切り)")]
        [Description("例: 。=>。　。")]
        [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string ReplaceRulesText
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                List<ReplaceRuleData> rules = State.Settings.AppConfig.Text.ReplaceRules;
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

                State.Settings.AppConfig.Text.ReplaceRules = rules;
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

        [Category("Debug")]
        [DisplayName("02)logModifierHookStats")]
        public bool LogModifierHookStats
        {
            get { return State.Settings.AppConfig.Debug.LogModifierHookStats; }
            set { State.Settings.AppConfig.Debug.LogModifierHookStats = value; }
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
