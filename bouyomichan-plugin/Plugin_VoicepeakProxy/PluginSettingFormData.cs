using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.IO;
using System.Text;
using System.Windows.Forms;
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

        public string Title { get { return "VoicepeakProxy設定"; } }

        public bool ExpandAll { get { return false; } }

        public SettingsBase Setting { get { return _state; } }

        public GeneralTab P01General;
        public StartupTab P02Startup;
        public HookTab P03Hook;
        public UiTab P04Ui;
        public InputTimingTab P05InputTiming;
        public AudioTab P06Audio;
        public TextTab P07Text;
        // public QueueTab P08Queue;
        // public ValidationTab P09Validation;
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
            // P08Queue = new QueueTab(_state);
            // P09Validation = new ValidationTab(_state);
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
        [DisplayName("01)VOICEPEAK 自動起動用本体パス")]
        [Description("VOICEPEAK自動起動に使用するvoicepeak.exeのパスです。空文字の場合は自動起動は行いません。")]
        [Editor(typeof(VoicepeakExePathEditor), typeof(UITypeEditor))]
        public string VoicepeakExePath
        {
            get { return State.Settings.Plugin.VoicepeakExePath; }
            set { State.Settings.Plugin.VoicepeakExePath = value ?? string.Empty; }
        }

        [Category("Plugin")]
        [DisplayName("02)VOICEPEAK 自動起動用.vppファイルパス")]
        [Description("VOICEPEAK自動起動時に開く.vppファイルのパスです。空文字の場合は何も指定せずに起動します。")]
        [Editor(typeof(VoicepeakTemplatePathEditor), typeof(UITypeEditor))]
        public string VoicepeakTemplatePath
        {
            get { return State.Settings.Plugin.VoicepeakTemplatePath; }
            set { State.Settings.Plugin.VoicepeakTemplatePath = value ?? string.Empty; }
        }

        [Category("Plugin")]
        [DisplayName("03)Pipe名")]
        [Description("PluginとWorkerの接続に使用するPipe名です。通常は設定を変更する必要はありません。")]
        public string PipeName
        {
            get { return State.Settings.Plugin.PipeName; }
            set { State.Settings.Plugin.PipeName = value ?? string.Empty; }
        }

        [Category("Plugin")]
        [DisplayName("04)Pipe接続タイムアウト(ms)")]
        [Description("Worker接続待ち時間をミリ秒で指定します。")]
        public int PipeConnectTimeoutMs
        {
            get { return State.Settings.Plugin.PipeConnectTimeoutMs; }
            set { State.Settings.Plugin.PipeConnectTimeoutMs = value; }
        }
    }

    // Queue設定タブ
    // internal sealed class QueueTab : TabBase
    // {
    //     public QueueTab(PluginSettingsState state) : base(state)
    //     {
    //     }

    //     public override string GetName()
    //     {
    //         return "Queue";
    //     }

    //     [Category("Queue")]
    //     [DisplayName("01)maxQueuedJobs")]
    //     public int MaxQueuedJobs
    //     {
    //         get { return State.Settings.AppConfig.Queue.MaxQueuedJobs; }
    //         set { State.Settings.AppConfig.Queue.MaxQueuedJobs = value; }
    //     }
    // }

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
        [Description("発話中判定に使う音量の閾値です。")]
        public float PeakThreshold
        {
            get { return State.Settings.AppConfig.Audio.PeakThreshold; }
            set { State.Settings.AppConfig.Audio.PeakThreshold = value; }
        }

        [Category("Audio")]
        [DisplayName("02)pollIntervalMs")]
        [Description("音声監視を行う間隔をミリ秒で指定します。")]
        public int PollIntervalMs
        {
            get { return State.Settings.AppConfig.Audio.PollIntervalMs; }
            set { State.Settings.AppConfig.Audio.PollIntervalMs = value; }
        }

        [Category("Audio")]
        [DisplayName("03)startConfirmTimeoutMs")]
        [Description("再生実行から発話開始を待つ最大時間をミリ秒で指定します。")]
        public int StartConfirmTimeoutMs
        {
            get { return State.Settings.AppConfig.Audio.StartConfirmTimeoutMs; }
            set { State.Settings.AppConfig.Audio.StartConfirmTimeoutMs = value; }
        }

        [Category("Audio")]
        [DisplayName("04)startConfirmMaxRetries")]
        [Description("発話開始が確認されなかった際に再生をリトライする回数です。")]
        public int StartConfirmMaxRetries
        {
            get { return State.Settings.AppConfig.Audio.StartConfirmMaxRetries; }
            set { State.Settings.AppConfig.Audio.StartConfirmMaxRetries = value; }
        }

        [Category("Audio")]
        [DisplayName("05)stopConfirmMs")]
        [Description("発話開始後、この時間(ミリ秒)だけ無音が続いたら発話終了と判定します。")]
        public int StopConfirmMs
        {
            get { return State.Settings.AppConfig.Audio.StopConfirmMs; }
            set { State.Settings.AppConfig.Audio.StopConfirmMs = value; }
        }

        [Category("Audio")]
        [DisplayName("06)maxSpeakingDurationSec")]
        [Description("発話開始後、この秒数を超えても終了しない場合はエラーとします。")]
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
        [Description("起動時に入力/再生の確認を行う文字列です。")]
        public string BootValidationText
        {
            get { return State.Settings.AppConfig.Startup.BootValidationText; }
            set { State.Settings.AppConfig.Startup.BootValidationText = value ?? string.Empty; }
        }

        [Category("Startup")]
        [DisplayName("02)bootValidationMaxRetries")]
        [Description("起動時の入力検証失敗時の再試行回数です。")]
        public int BootValidationMaxRetries
        {
            get { return State.Settings.AppConfig.Startup.BootValidationMaxRetries; }
            set { State.Settings.AppConfig.Startup.BootValidationMaxRetries = value; }
        }

        [Category("Startup")]
        [DisplayName("03)bootValidationRetryIntervalMs")]
        [Description("起動時入力検証の再試行待機時間をミリ秒で指定します。")]
        public int BootValidationRetryIntervalMs
        {
            get { return State.Settings.AppConfig.Startup.BootValidationRetryIntervalMs; }
            set { State.Settings.AppConfig.Startup.BootValidationRetryIntervalMs = value; }
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
        [Description("修飾キー中立化フックへのコマンド送信タイムアウトをミリ秒で指定します。")]
        public int HookCommandTimeoutMs
        {
            get { return State.Settings.AppConfig.Hook.HookCommandTimeoutMs; }
            set { State.Settings.AppConfig.Hook.HookCommandTimeoutMs = value; }
        }

        [Category("Hook")]
        [DisplayName("02)hookConnectTimeoutMs")]
        [Description("修飾キー中立化フックの接続試行1回あたりのタイムアウトをミリ秒で指定します。")]
        public int HookConnectTimeoutMs
        {
            get { return State.Settings.AppConfig.Hook.HookConnectTimeoutMs; }
            set { State.Settings.AppConfig.Hook.HookConnectTimeoutMs = value; }
        }

        [Category("Hook")]
        [DisplayName("03)hookConnectTotalWaitMs")]
        [Description("修飾キー中立化フックの接続待機総最大時間をミリ秒で指定します。")]
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
        [DisplayName("01)moveToStartModifier")]
        [Description("「先頭に移動」ショートカットの修飾子キーです。VOICEPEAKの設定値に応じて、空文字/ctrl/altのいずれかを指定してください。shiftや、ctrlとaltの複合は現在非対応です。")]
        public string MoveToStartModifier
        {
            get { return State.Settings.AppConfig.Ui.MoveToStartModifier; }
            set { State.Settings.AppConfig.Ui.MoveToStartModifier = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("02)moveToStartKey")]
        [Description("「先頭に移動」ショートカットのキーです。VOICEPEAKの設定値と同じものを指定してください。例: cursor up, F3, home")]
        public string MoveToStartKey
        {
            get { return State.Settings.AppConfig.Ui.MoveToStartKey; }
            set { State.Settings.AppConfig.Ui.MoveToStartKey = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("03)clearInputSelectAllModifier")]
        [Description("「すべてを選択」ショートカットの修飾子キーです。VOICEPEAKの設定値に応じて、空文字/ctrl/altのいずれかを指定してください。")]
        public string ClearInputSelectAllModifier
        {
            get { return State.Settings.AppConfig.Ui.ClearInputSelectAllModifier; }
            set { State.Settings.AppConfig.Ui.ClearInputSelectAllModifier = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("04)clearInputSelectAllKey")]
        [Description("「すべてを選択」ショートカットのキーです。VOICEPEAKの設定値と同じものを指定してください。例: a")]
        public string ClearInputSelectAllKey
        {
            get { return State.Settings.AppConfig.Ui.ClearInputSelectAllKey; }
            set { State.Settings.AppConfig.Ui.ClearInputSelectAllKey = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("05)playShortcutModifier")]
        [Description("「再生/停止」ショートカットの修飾子キーです。VOICEPEAKの設定値に応じて、空文字/ctrl/alt/shiftのいずれかを指定してください。")]
        public string PlayShortcutModifier
        {
            get { return State.Settings.AppConfig.Ui.PlayShortcutModifier; }
            set { State.Settings.AppConfig.Ui.PlayShortcutModifier = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("06)playShortcutKey")]
        [Description("「再生/停止」ショートカットのキーです。VOICEPEAKの設定値と同じものを指定してください。例: spacebar, F3, home")]
        public string PlayShortcutKey
        {
            get { return State.Settings.AppConfig.Ui.PlayShortcutKey; }
            set { State.Settings.AppConfig.Ui.PlayShortcutKey = value ?? string.Empty; }
        }

        [Category("Ui")]
        [DisplayName("07)delayBeforePlayShortcutMs")]
        [Description("再生ボタンを押す前の待機時間をミリ秒で指定します。")]
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
        [DisplayName("01)keyStrokeIntervalMs")]
        [Description("キー操作ごとの待機時間をミリ秒で指定します。0の場合は待機しません。")]
        public int KeyStrokeIntervalMs
        {
            get { return State.Settings.AppConfig.InputTiming.KeyStrokeIntervalMs; }
            set { State.Settings.AppConfig.InputTiming.KeyStrokeIntervalMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("02)actionDelayMs")]
        [Description("文字入力欄フォーカスなどのUIアクション時の待機時間をミリ秒で指定します。")]
        public int ActionDelayMs
        {
            get { return State.Settings.AppConfig.InputTiming.ActionDelayMs; }
            set { State.Settings.AppConfig.InputTiming.ActionDelayMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("03)postTypeWaitPerCharMs")]
        [Description("文字入力後の待機時間算出に使用する倍率です。文字入力完了後に再生失敗する場合は、値を増やして再生前の待機時間を伸ばしてみてください。")]
        public int PostTypeWaitPerCharMs
        {
            get { return State.Settings.AppConfig.InputTiming.PostTypeWaitPerCharMs; }
            set { State.Settings.AppConfig.InputTiming.PostTypeWaitPerCharMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("04)postTypeWaitMinMs")]
        [Description("文字入力後待機時間の最小値です。文字入力完了後に再生失敗する場合は、値を増やして再生前の待機時間を伸ばしてみてください。")]
        public int PostTypeWaitMinMs
        {
            get { return State.Settings.AppConfig.InputTiming.PostTypeWaitMinMs; }
            set { State.Settings.AppConfig.InputTiming.PostTypeWaitMinMs = value; }
        }

        [Category("InputTiming")]
        [DisplayName("05)clearInputMaxPasses")]
        [Description("入力クリア処理の最大繰り返し回数です。入力文字が削除しきれていない場合は増やしてみてください。")]
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
        [Description("trueにすると Text.sentenceBreakTriggers で指定した句点等の改行区切りで入力ブロックを分割します。")]
        public bool SendEnterAfterSentenceBreak
        {
            get { return State.Settings.AppConfig.Text.SendEnterAfterSentenceBreak; }
            set { State.Settings.AppConfig.Text.SendEnterAfterSentenceBreak = value; }
        }

        [Category("Text")]
        [DisplayName("02)sentenceBreakTriggers(改行区切り)")]
        [Description("入力ブロックの分割対象文字列です。1行に1トリガーを指定してください。")]
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
        [Description("ポーズ調整等に利用可能な入力文字の置換ルールです。1行に1ルールを指定してください。(例: 。=>。　。)")]
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

        [Category("Debug")]
        [DisplayName("03)logMinimumLevel")]
        public string LogMinimumLevel
        {
            get { return State.Settings.AppConfig.Debug.LogMinimumLevel; }
            set { State.Settings.AppConfig.Debug.LogMinimumLevel = (value ?? string.Empty).Trim().ToLowerInvariant(); }
        }
    }

    // ファイル選択エディタ共通基底
    internal abstract class FilePathEditorBase : UITypeEditor
    {
        // モーダルダイアログ編集を指定
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }

        // ファイル選択ダイアログで値を編集
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            string current = (value as string) ?? string.Empty;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = GetDialogTitle();
                dialog.Filter = GetFilter();
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        if (File.Exists(current))
                        {
                            dialog.InitialDirectory = Path.GetDirectoryName(current) ?? string.Empty;
                            dialog.FileName = Path.GetFileName(current);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(current) ?? string.Empty;
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            {
                                dialog.InitialDirectory = dir;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    return dialog.FileName ?? string.Empty;
                }
            }

            return current;
        }

        // ダイアログタイトルを取得
        protected abstract string GetDialogTitle();

        // ダイアログフィルタを取得
        protected abstract string GetFilter();
    }

    // voicepeak.exe選択エディタ
    internal sealed class VoicepeakExePathEditor : FilePathEditorBase
    {
        protected override string GetDialogTitle()
        {
            return "voicepeak.exeを選択";
        }

        protected override string GetFilter()
        {
            return "VOICEPEAK executable (voicepeak.exe)|voicepeak.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*";
        }
    }

    // テンプレートvpp選択エディタ
    internal sealed class VoicepeakTemplatePathEditor : FilePathEditorBase
    {
        protected override string GetDialogTitle()
        {
            return "テンプレート.vppを選択";
        }

        protected override string GetFilter()
        {
            return "VOICEPEAK project (*.vpp)|*.vpp|All files (*.*)|*.*";
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
