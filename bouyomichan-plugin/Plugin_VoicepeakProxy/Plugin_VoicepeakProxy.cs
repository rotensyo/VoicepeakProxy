using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FNF.BouyomiChanApp;
using FNF.Utility;
using BouyomiVoicepeakBridge.Shared;

namespace Plugin_VoicepeakProxy
{
    // 棒読みちゃんとVOICEPEAKを中継
    public sealed class Plugin_VoicepeakProxy : IPlugin
    {
        private enum WorkerState
        {
            Starting,
            Ready,
            Failed,
            Stopping
        }

        private readonly object _workerStartSync = new object();
        private PluginSettingsState _settingsState;
        private PluginSettingFormData _settingFormData;
        private string _settingsPath;
        private string _logPath;
        private FileLogger _logger;
        private WorkerState _workerState;
        private string _workerStartError;
        private Thread _workerStartThread;
        private BouyomiChan _bouyomi;

        public string Name { get { return "VoicepeakProxy for 棒読みちゃん"; } }

        public string Version { get { return "2026/04/29版 (core 1.2.0)"; } }

        public string Caption
        {
            get
            {
                return "棒読みちゃんの文字列をVOICEPEAKへ転送します。";
            }
        }

        public FNF.XmlSerializerSetting.ISettingFormData SettingFormData
        {
            get { return _settingFormData; }
        }

        // プラグイン開始
        public void Begin()
        {
            lock (_workerStartSync)
            {
                _workerState = WorkerState.Starting;
                _workerStartError = string.Empty;
            }

            string baseDir = ResolveBaseDirectory();
            _settingsPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_setting.json");
            _logPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_plugin.log");
            _logger = new FileLogger(_logPath);

            WorkerProcessManager.EnsureSettingsFileInitialized(_settingsPath, _logger);

            _settingsState = new PluginSettingsState();
            _settingsState.Configure(_settingsPath, _logger);
            _settingsState.LoadFromJson();
            _logger.SetMinimumLevel(_settingsState.Settings.AppConfig.Debug.LogMinimumLevel);
            _settingFormData = new PluginSettingFormData(_settingsState);

            PluginRuntimeConfig runtime = _settingsState.Settings.Plugin;
            StartWorkerAsync(runtime, _settingsPath, _logger);

            if (!TryAttachTalkTaskStarted())
            {
                _logger.Warn("talk_task_started_hook_failed");
            }
            else
            {
                _logger.Info("talk_task_started_hook_ok");
            }
        }

        // プラグイン終了
        public void End()
        {
            Thread workerStartThread;
            BouyomiChan bouyomi;
            bool shouldShutdownWorker;
            lock (_workerStartSync)
            {
                shouldShutdownWorker = _workerState == WorkerState.Ready;
                _workerState = WorkerState.Stopping;
                workerStartThread = _workerStartThread;
                bouyomi = _bouyomi;
                _bouyomi = null;
            }

            if (bouyomi != null)
            {
                bouyomi.TalkTaskStarted -= OnTalkTaskStarted;
            }

            if (workerStartThread != null)
            {
                workerStartThread.Join(2000);
            }

            if (_settingsState != null && shouldShutdownWorker)
            {
                WorkerProcessManager.SendShutdown(_settingsState.Settings.Plugin, _logger);
            }

            if (_settingsState != null)
            {
                _settingsState.SaveToJson();
            }

            if (_logger != null)
            {
                _logger.Dispose();
            }
        }

        // 読み上げ開始イベントを処理
        private void OnTalkTaskStarted(object sender, BouyomiChan.TalkTaskStartedEventArgs e)
        {
            try
            {
                WorkerState state;
                lock (_workerStartSync)
                {
                    state = _workerState;
                }

                if (state == WorkerState.Stopping)
                {
                    return;
                }

                // 棒読みちゃん既定音声を常に抑止
                e.Cancel = true;

                string text = ResolveTalkText(e);
                if (string.IsNullOrEmpty(text))
                {
                    _logger.Info("skip_empty_text");
                    return;
                }

                int taskId = -1;
                if (e.TalkTask != null)
                {
                    taskId = e.TalkTask.TaskId;
                }

                if (state == WorkerState.Failed)
                {
                    _logger.Warn("drop_after_worker_start_failed taskId=" + taskId);
                    return;
                }

                if (state != WorkerState.Ready)
                {
                    _logger.Warn("drop_during_worker_startup taskId=" + taskId);
                    return;
                }

                SendTalk(taskId, text);
            }
            catch (Exception ex)
            {
                lock (_workerStartSync)
                {
                    _workerState = WorkerState.Stopping;
                }

                _logger.Error("on_talk_task_started_fatal", ex.Message);
                throw;
            }
        }

        // Worker起動を非同期で開始
        private void StartWorkerAsync(PluginRuntimeConfig runtime, string settingsPath, FileLogger logger)
        {
            lock (_workerStartSync)
            {
                _workerState = WorkerState.Starting;
                _workerStartError = string.Empty;

                _workerStartThread = new Thread(() => WorkerStartThreadMain(runtime, settingsPath, logger));
                _workerStartThread.IsBackground = true;
                _workerStartThread.Name = "VoicepeakProxyWorkerStart";
                _workerStartThread.Start();
            }
        }

        // Worker起動スレッド本体
        private void WorkerStartThreadMain(PluginRuntimeConfig runtime, string settingsPath, FileLogger logger)
        {
            try
            {
                // 自動起動待機はバックグラウンドで実行
                WorkerProcessManager.EnsureVoicepeakStartedIfNeeded(runtime, logger);
                WorkerProcessManager.EnsureStarted(runtime, settingsPath, logger);

                bool shouldShutdown;
                lock (_workerStartSync)
                {
                    shouldShutdown = _workerState == WorkerState.Stopping;
                    if (!shouldShutdown)
                    {
                        _workerState = WorkerState.Ready;
                    }
                }

                if (shouldShutdown)
                {
                    logger.Info("worker_started_during_stopping_shutdown");
                    WorkerProcessManager.SendShutdown(runtime, logger);
                    return;
                }

                logger.Info("worker_start_async_ok");
            }
            catch (Exception ex)
            {
                lock (_workerStartSync)
                {
                    if (_workerState == WorkerState.Stopping)
                    {
                        return;
                    }

                    _workerState = WorkerState.Failed;
                    _workerStartError = ex.Message;
                }

                logger.Error("worker_start_async_failed", ex.Message);
                NotifyWorkerStartFailure(ex.Message);
            }
        }

        // Worker起動失敗を通知
        private void NotifyWorkerStartFailure(string detail)
        {
            BouyomiChan bouyomi;
            string capturedReason;
            lock (_workerStartSync)
            {
                _workerState = WorkerState.Stopping;
                bouyomi = _bouyomi;
                _bouyomi = null;
                capturedReason = _workerStartError;
            }

            if (bouyomi != null)
            {
                bouyomi.TalkTaskStarted -= OnTalkTaskStarted;
            }

            string reason = string.IsNullOrEmpty(detail) ? (capturedReason ?? "不明なエラー") : detail;
            string message = "Worker起動に失敗したためプラグインを停止しました。詳細はPlugin_VoicepeakProxy_worker.logを確認してください。 reason=" + reason;
            MessageBox.Show(message, "VoicepeakProxy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Workerへ発話を同期送信
        private void SendTalk(int taskId, string text)
        {
            PluginSettingsState state = _settingsState;
            PluginRuntimeConfig runtime = state != null && state.Settings != null ? state.Settings.Plugin : null;
            if (runtime == null)
            {
                runtime = new PluginRuntimeConfig();
            }

            WorkerSpeakRequest request = new WorkerSpeakRequest();
            request.Command = "speak";
            request.TaskId = taskId;
            request.Text = text;

            WorkerSpeakResponse response = WorkerIpcClient.TrySend(runtime, request);
            if (!response.Accepted)
            {
                string reason = WorkerErrorMessageMapper.Map(response.ErrorMessage);
                throw new InvalidOperationException("Workerが異常終了したため、プラグインを停止しました。詳細はPlugin_VoicepeakProxy_worker.logを確認してください。 taskId=" + taskId + " reason=" + reason);
            }

            _logger.Info("worker_accepted taskId=" + taskId + " length=" + text.Length);
        }

        // TalkTaskStartedから送信文字列を決定
        private static string ResolveTalkText(BouyomiChan.TalkTaskStartedEventArgs e)
        {
            string replacedWord = e.ReplaceWord;
            if (!string.IsNullOrEmpty(replacedWord))
            {
                return replacedWord;
            }

            if (e.TalkTask != null)
            {
                return e.TalkTask.SourceText ?? string.Empty;
            }

            return string.Empty;
        }

        // 棒読みちゃんインスタンスへイベント購読
        private bool TryAttachTalkTaskStarted()
        {
            object formMain = Pub.FormMain;
            if (formMain == null && Pub.Data != null)
            {
                FieldInfo formMainField = Pub.Data.GetType().GetField("FormMain", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (formMainField != null)
                {
                    formMain = formMainField.GetValue(Pub.Data);
                }
            }

            if (formMain == null)
            {
                return false;
            }

            FieldInfo bcField = formMain.GetType().GetField("BC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (bcField == null)
            {
                return false;
            }

            _bouyomi = bcField.GetValue(formMain) as BouyomiChan;
            if (_bouyomi == null)
            {
                return false;
            }

            _bouyomi.TalkTaskStarted += OnTalkTaskStarted;
            return true;
        }

        // プラグイン配置ディレクトリを解決
        private static string ResolveBaseDirectory()
        {
            string path = Base.CallAsmPath;
            if (string.IsNullOrEmpty(path))
            {
                string asmPath = Assembly.GetExecutingAssembly().Location;
                return Path.GetDirectoryName(asmPath) ?? ".";
            }

            return path;
        }
    }

    // Workerプロセスの起動停止を管理
    internal static class WorkerProcessManager
    {
        private const int VoicepeakAutostartDetectTimeoutMs = 30000;
        private const int VoicepeakAutostartPollIntervalMs = 200;
        private const int VoicepeakAutostartReadySettleMs = 500;

        // 必要時のみVOICEPEAK起動を保証
        public static void EnsureVoicepeakStartedIfNeeded(PluginRuntimeConfig runtime, FileLogger logger)
        {
            int processCount = GetVoicepeakProcessCount();
            if (processCount > 0)
            {
                logger.Info("voicepeak_autostart_skipped reason=already_running count=" + processCount);
                return;
            }

            string exePath = (runtime.VoicepeakExePath ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(exePath))
            {
                logger.Info("voicepeak_autostart_skipped reason=exe_path_empty");
                return;
            }

            if (!File.Exists(exePath))
            {
                throw new InvalidOperationException("VOICEPEAK自動起動に失敗しました。voicepeak.exeのパスが見つかりません。 path=" + exePath);
            }

            string templatePath = (runtime.VoicepeakTemplatePath ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(templatePath) && !File.Exists(templatePath))
            {
                throw new InvalidOperationException("VOICEPEAK自動起動に失敗しました。テンプレート.vppのパスが見つかりません。 path=" + templatePath);
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = false;
            psi.Arguments = string.IsNullOrEmpty(templatePath) ? string.Empty : ("\"" + templatePath + "\"");

            Process started = Process.Start(psi);
            if (started == null)
            {
                throw new InvalidOperationException("VOICEPEAK自動起動に失敗しました。voicepeak.exeを開始できませんでした。 path=" + exePath);
            }

            logger.Info("voicepeak_autostart_started pid=" + started.Id + " path=" + exePath);
            WaitForVoicepeakProcess(started, VoicepeakAutostartDetectTimeoutMs, VoicepeakAutostartPollIntervalMs);
            logger.Info("voicepeak_autostart_ok pid=" + started.Id);
        }

        // shutdown送信後に停止を実行
        public static void SendShutdown(PluginRuntimeConfig runtime, FileLogger logger)
        {
            PluginRuntimeConfig shutdownRuntime = CloneRuntimeForShutdown(runtime);
            WorkerSpeakRequest request = new WorkerSpeakRequest();
            request.Command = "shutdown";
            WorkerSpeakResponse response = WorkerIpcClient.TrySend(shutdownRuntime, request);
            bool shutdownFailed = !response.Accepted;
            if (shutdownFailed)
            {
                logger.Warn("worker_shutdown_request_failed error=" + response.ErrorMessage);
            }

            TryStopWithTimeout(logger, 5000, shutdownFailed);
        }

        // 設定ファイルを初期化
        public static void EnsureSettingsFileInitialized(string settingsPath, FileLogger logger)
        {
            if (File.Exists(settingsPath))
            {
                return;
            }

            string basePath = ResolveBasePath();
            string workerPath = ResolveWorkerPath(basePath);
            if (!File.Exists(workerPath))
            {
                logger.Warn("worker_not_found_for_settings_init path=" + workerPath);
                return;
            }

            string workerLogPath = Path.Combine(basePath, "Plugin_VoicepeakProxy_worker.log");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = workerPath;
            psi.WorkingDirectory = Path.GetDirectoryName(workerPath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = "--init-settings --settings \"" + settingsPath + "\" --log \"" + workerLogPath + "\"";

            try
            {
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        bool exited = p.WaitForExit(5000);
                        if (!exited)
                        {
                            try
                            {
                                p.Kill();
                            }
                            catch
                            {
                            }

                            logger.Warn("settings_init_timeout_killed");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn("settings_init_failed error=" + ex.Message);
            }
        }

        // Worker起動を保証
        public static void EnsureStarted(PluginRuntimeConfig runtime, string settingsPath, FileLogger logger)
        {
            string basePath = ResolveBasePath();
            string workerPath = ResolveWorkerPath(basePath);
            if (!File.Exists(workerPath))
            {
                throw new InvalidOperationException("VoicepeakProxyWorker.exeが見つかりません。 expected_path=" + workerPath);
            }

            TryTerminateExistingInstances(workerPath, logger);

            Process startedProcess = null;
            int ownerPid = Process.GetCurrentProcess().Id;
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = workerPath;
            psi.WorkingDirectory = Path.GetDirectoryName(workerPath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            string logPath = Path.Combine(basePath, "Plugin_VoicepeakProxy_worker.log");
            psi.Arguments = "--pipe \"" + runtime.PipeName + "\" --settings \"" + settingsPath + "\" --log \"" + logPath + "\" --owner-pid " + ownerPid;
            startedProcess = Process.Start(psi);
            if (startedProcess == null)
            {
                throw new InvalidOperationException("VoicepeakProxyWorker.exeの起動に失敗しました。 path=" + workerPath);
            }

            WorkerLifetimeManager.AssignToCurrentProcessJob(startedProcess, logger);
            logger.Info("worker_started path=" + workerPath + " pid=" + startedProcess.Id + " ownerPid=" + ownerPid);

            WaitForReady(runtime, startedProcess, logger, 30000, 500);
        }

        // 終了時にWorker停止
        public static void TryStopWithTimeout(FileLogger logger, int gracefulWaitMs, bool forceKillWithoutWait)
        {
            string workerPath = ResolveWorkerPath(ResolveBasePath());
            string processName = Path.GetFileNameWithoutExtension(workerPath);
            int waitMs = forceKillWithoutWait ? 0 : (gracefulWaitMs < 0 ? 0 : gracefulWaitMs);
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes == null || processes.Length == 0)
            {
                logger.Info("worker_not_running_on_shutdown");
                return;
            }

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                if (!IsTargetWorkerProcess(p, workerPath, logger))
                {
                    continue;
                }

                TryStopWorkerProcess(p, logger, waitMs, forceKillWithoutWait);
            }
        }

        // 同梱Worker実体のみ停止対象
        private static bool IsTargetWorkerProcess(Process process, string workerPath, FileLogger logger)
        {
            try
            {
                if (process.HasExited)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                return string.Equals(process.MainModule.FileName, workerPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger.Warn("worker_path_probe_failed pid=" + process.Id + " error=" + ex.Message);
                return false;
            }
        }

        // 停止方針で単一Workerを停止
        private static bool TryStopWorkerProcess(Process process, FileLogger logger, int waitMs, bool forceKillWithoutWait)
        {
            try
            {
                if (waitMs > 0)
                {
                    bool exited = process.WaitForExit(waitMs);
                    if (exited)
                    {
                        logger.Info("worker_stopped_gracefully pid=" + process.Id);
                        return true;
                    }

                    logger.Warn("worker_stop_timeout_force_kill pid=" + process.Id + " waitMs=" + waitMs);
                }
                else if (forceKillWithoutWait)
                {
                    logger.Warn("worker_shutdown_force_kill pid=" + process.Id);
                }

                process.Kill();
                process.WaitForExit(1000);
                logger.Info("worker_force_stopped pid=" + process.Id);
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn("worker_stop_failed pid=" + process.Id + " error=" + ex.Message);
                return false;
            }
        }

        // 起動前に既存Workerを停止
        private static void TryTerminateExistingInstances(string workerPath, FileLogger logger)
        {
            string processName = Path.GetFileNameWithoutExtension(workerPath);
            Process[] processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                if (!IsTargetWorkerProcess(p, workerPath, logger))
                {
                    continue;
                }

                bool stopped = TryStopWorkerProcess(p, logger, 0, false);
                if (stopped)
                {
                    logger.Warn("stale_worker_terminated pid=" + p.Id);
                }
            }
        }

        // 基準ディレクトリを解決
        private static string ResolveBasePath()
        {
            string basePath = Base.CallAsmPath;
            if (string.IsNullOrEmpty(basePath))
            {
                string asmPath = Assembly.GetExecutingAssembly().Location;
                basePath = Path.GetDirectoryName(asmPath) ?? ".";
            }

            return basePath;
        }

        // Worker実行ファイルパスを解決
        private static string ResolveWorkerPath(string basePath)
        {
            return Path.Combine(Path.Combine(basePath, "VoicepeakProxyWorker"), "VoicepeakProxyWorker.exe");
        }

        // VOICEPEAK起動完了を待機
        private static void WaitForVoicepeakProcess(Process startedProcess, int timeoutMs, int pollIntervalMs)
        {
            int startedAt = Environment.TickCount;
            int waitMs = timeoutMs <= 0 ? VoicepeakAutostartDetectTimeoutMs : timeoutMs;
            int pollMs = pollIntervalMs <= 0 ? VoicepeakAutostartPollIntervalMs : pollIntervalMs;

            while (true)
            {
                if (HasReadyVoicepeakMainWindow())
                {
                    Thread.Sleep(VoicepeakAutostartReadySettleMs);
                    return;
                }

                try
                {
                    if (startedProcess != null && startedProcess.HasExited)
                    {
                        throw new InvalidOperationException("VOICEPEAK自動起動に失敗しました。プロセスが早期終了しました。 exitCode=" + startedProcess.ExitCode);
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                }

                int elapsed = unchecked(Environment.TickCount - startedAt);
                if (elapsed >= waitMs)
                {
                    throw new InvalidOperationException("VOICEPEAK自動起動に失敗しました。起動待機がタイムアウトしました。 waitedMs=" + elapsed);
                }

                Thread.Sleep(pollMs);
            }
        }

        // VOICEPEAKプロセス数を取得
        private static int GetVoicepeakProcessCount()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("voicepeak");
                return processes == null ? 0 : processes.Length;
            }
            catch
            {
                return 0;
            }
        }

        // VOICEPEAKのメインウィンドウ生成完了を判定
        private static bool HasReadyVoicepeakMainWindow()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName("voicepeak");
            }
            catch
            {
                return false;
            }

            if (processes == null || processes.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (p.HasExited)
                    {
                        continue;
                    }

                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        // shutdown送信用設定を作成
        private static PluginRuntimeConfig CloneRuntimeForShutdown(PluginRuntimeConfig runtime)
        {
            PluginRuntimeConfig copied = new PluginRuntimeConfig();
            copied.PipeName = runtime.PipeName;
            copied.PipeConnectTimeoutMs = 300;
            return copied;
        }

        // Worker受信ループ開始を待機
        private static void WaitForReady(PluginRuntimeConfig runtime, Process startedProcess, FileLogger logger, int timeoutMs, int pollIntervalMs)
        {
            int waitMs = timeoutMs <= 0 ? 30000 : timeoutMs;
            int pollMs = pollIntervalMs <= 0 ? 500 : pollIntervalMs;
            int startedAt = Environment.TickCount;
            PluginRuntimeConfig pingRuntime = CloneRuntimeForPing(runtime);
            WorkerSpeakRequest ping = new WorkerSpeakRequest();
            ping.Command = "ping";

            while (true)
            {
                if (startedProcess != null)
                {
                    try
                    {
                        if (startedProcess.HasExited)
                        {
                            throw new InvalidOperationException(BuildWorkerExitMessage(startedProcess.ExitCode));
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                WorkerSpeakResponse response = WorkerIpcClient.TrySend(pingRuntime, ping);
                if (response.Accepted)
                {
                    logger.Info("worker_ready");
                    return;
                }

                int elapsed = unchecked(Environment.TickCount - startedAt);
                if (elapsed >= waitMs)
                {
                    string reason = WorkerErrorMessageMapper.Map(response.ErrorMessage);
                    throw new InvalidOperationException("Worker起動待機がタイムアウトしました。詳細はPlugin_VoicepeakProxy_worker.logを確認してください。 waitedMs=" + elapsed + " reason=" + reason);
                }

                Thread.Sleep(pollMs);
            }
        }

        // 待機確認用設定を作成
        private static PluginRuntimeConfig CloneRuntimeForPing(PluginRuntimeConfig runtime)
        {
            PluginRuntimeConfig copied = new PluginRuntimeConfig();
            copied.PipeName = runtime.PipeName;
            copied.PipeConnectTimeoutMs = 200;
            return copied;
        }

        // Worker終了コードに応じた起動失敗メッセージを生成
        private static string BuildWorkerExitMessage(int exitCode)
        {
            if (exitCode == 2)
            {
                return "Worker起動時検証に失敗しました。VOICEPEAKが起動していないか、VOICEPEAKとプラグインのショートカット設定が一致していない可能性があります。";
            }

            return "Workerが起動待機中に終了しました。詳細はPlugin_VoicepeakProxy_worker.logを確認してください。 exitCode=" + exitCode;
        }

    }

    // Worker関連エラー文言を統一
    internal static class WorkerErrorMessageMapper
    {
        // エラーコードを日本語理由へ変換
        public static string Map(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode))
            {
                return "不明なエラー";
            }

            if (string.Equals(errorCode, "pipe_connect_failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Workerとの接続に失敗しました";
            }

            if (string.Equals(errorCode, "pipe_io_failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Workerとの通信中にI/Oエラーが発生しました";
            }

            if (string.Equals(errorCode, "empty_response", StringComparison.OrdinalIgnoreCase))
            {
                return "Workerからの応答が空でした";
            }

            if (string.Equals(errorCode, "invalid_response", StringComparison.OrdinalIgnoreCase))
            {
                return "Workerからの応答が不正でした";
            }

            return errorCode;
        }
    }

    // Worker親子連動をOS機能で管理
    internal static class WorkerLifetimeManager
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private static readonly object Sync = new object();
        private static IntPtr _jobHandle = IntPtr.Zero;

        // Workerを親プロセスのJobへ割り当て
        public static void AssignToCurrentProcessJob(Process process, FileLogger logger)
        {
            if (process == null)
            {
                throw new ArgumentNullException("process");
            }

            lock (Sync)
            {
                if (_jobHandle == IntPtr.Zero)
                {
                    _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                    if (_jobHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Job Objectの作成に失敗しました");
                    }

                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr infoPtr = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(info, infoPtr, false);
                        bool configured = NativeMethods.SetInformationJobObject(
                            _jobHandle,
                            JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                            infoPtr,
                            (uint)length);
                        if (!configured)
                        {
                            throw new InvalidOperationException("Job Objectの設定に失敗しました");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(infoPtr);
                    }
                }

                bool assigned = NativeMethods.AssignProcessToJobObject(_jobHandle, process.Handle);
                if (!assigned)
                {
                    throw new InvalidOperationException("WorkerをJob Objectへ割り当てできませんでした。 pid=" + process.Id);
                }

                logger.Info("worker_job_assigned pid=" + process.Id);
            }
        }

        private enum JOBOBJECTINFOCLASS
        {
            JobObjectExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetInformationJobObject(
                IntPtr hJob,
                JOBOBJECTINFOCLASS jobObjectInfoClass,
                IntPtr lpJobObjectInfo,
                uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        }
    }

    // Worker IPC送受信を提供
    internal static class WorkerIpcClient
    {
        // 1回送信して応答を返す
        public static WorkerSpeakResponse TrySend(PluginRuntimeConfig runtime, WorkerSpeakRequest request)
        {
            WorkerSpeakResponse failed = new WorkerSpeakResponse();
            failed.Accepted = false;
            failed.ErrorMessage = "pipe_connect_failed";

            int timeout = runtime.PipeConnectTimeoutMs <= 0 ? 1500 : runtime.PipeConnectTimeoutMs;
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", runtime.PipeName, PipeDirection.InOut))
            {
                try
                {
                    client.Connect(timeout);
                }
                catch
                {
                    return failed;
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                using (BinaryWriter writer = new BinaryWriter(client, new UTF8Encoding(false)))
                using (BinaryReader reader = new BinaryReader(client, Encoding.UTF8))
                {
                    try
                    {
                        PipeFraming.WriteFrame(writer, serializer.Serialize(request));

                        string line;
                        if (!PipeFraming.TryReadFrame(reader, out line) || string.IsNullOrEmpty(line))
                        {
                            WorkerSpeakResponse empty = new WorkerSpeakResponse();
                            empty.Accepted = false;
                            empty.ErrorMessage = "empty_response";
                            return empty;
                        }

                        WorkerSpeakResponse response = serializer.Deserialize<WorkerSpeakResponse>(line);
                        if (response == null)
                        {
                            WorkerSpeakResponse invalid = new WorkerSpeakResponse();
                            invalid.Accepted = false;
                            invalid.ErrorMessage = "invalid_response";
                            return invalid;
                        }

                        return response;
                    }
                    catch
                    {
                        WorkerSpeakResponse ioFailed = new WorkerSpeakResponse();
                        ioFailed.Accepted = false;
                        ioFailed.ErrorMessage = "pipe_io_failed";
                        return ioFailed;
                    }
                }
            }
        }
    }

    // プラグインログをファイルへ出力
    internal sealed class FileLogger : IDisposable
    {
        private enum LogMinimumLevel
        {
            Debug = 10,
            Info = 20,
            Warn = 30,
            Error = 40
        }

        private readonly object _sync = new object();
        private readonly string _filePath;
        private LogMinimumLevel _minimumLevel;

        public FileLogger(string filePath)
        {
            _filePath = filePath;
            _minimumLevel = LogMinimumLevel.Info;
            ResetLogFile();
        }

        // 最小ログレベルを更新
        public void SetMinimumLevel(string level)
        {
            lock (_sync)
            {
                _minimumLevel = ParseMinimumLevel(level);
            }
        }

        public void Info(string message)
        {
            if (!ShouldWriteInfo())
            {
                return;
            }

            Write("INFO", message);
        }

        public void Warn(string message)
        {
            if (!ShouldWriteWarn())
            {
                return;
            }

            Write("WARN", message);
        }

        public void Error(string message, string detail)
        {
            if (!ShouldWriteError())
            {
                return;
            }

            Write("ERROR", message + " detail=" + detail);
        }

        // ログ1行を書き込む
        private void Write(string level, string message)
        {
            lock (_sync)
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        // INFO出力可否を判定
        private bool ShouldWriteInfo()
        {
            lock (_sync)
            {
                return _minimumLevel <= LogMinimumLevel.Info;
            }
        }

        // WARN出力可否を判定
        private bool ShouldWriteWarn()
        {
            lock (_sync)
            {
                return _minimumLevel <= LogMinimumLevel.Warn;
            }
        }

        // ERROR出力可否を判定
        private bool ShouldWriteError()
        {
            lock (_sync)
            {
                return _minimumLevel <= LogMinimumLevel.Error;
            }
        }

        // 文字列から最小ログレベルへ変換
        private static LogMinimumLevel ParseMinimumLevel(string level)
        {
            string normalized = (level ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "debug")
            {
                return LogMinimumLevel.Debug;
            }

            if (normalized == "info")
            {
                return LogMinimumLevel.Info;
            }

            if (normalized == "warn")
            {
                return LogMinimumLevel.Warn;
            }

            if (normalized == "error")
            {
                return LogMinimumLevel.Error;
            }

            return LogMinimumLevel.Warn;
        }

        // 起動時にログを初期化
        private void ResetLogFile()
        {
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_filePath, string.Empty, Encoding.UTF8);
            }
            catch
            {
                // 初期化失敗時も継続
            }
        }

        public void Dispose()
        {
        }
    }
}
