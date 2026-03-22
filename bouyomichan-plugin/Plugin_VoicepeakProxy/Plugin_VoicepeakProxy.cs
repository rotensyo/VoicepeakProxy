using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using FNF.BouyomiChanApp;
using FNF.Utility;
using BouyomiVoicepeakBridge.Shared;

namespace Plugin_VoicepeakProxy
{
    // 棒読みちゃんとVOICEPEAKを中継
    public sealed class Plugin_VoicepeakProxy : IPlugin
    {
        private readonly object _queueLock = new object();
        private readonly Queue<WorkerSpeakRequest> _queue = new Queue<WorkerSpeakRequest>();
        private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);

        private PluginSettingsState _settingsState;
        private PluginSettingFormData _settingFormData;
        private string _settingsPath;
        private string _logPath;
        private FileLogger _logger;
        private Thread _senderThread;
        private bool _running;
        private bool _stopping;
        private BouyomiChan _bouyomi;

        public string Name { get { return "VoicepeakProxyCore連携"; } }

        public string Version { get { return "2026/03/22"; } }

        public string Caption
        {
            get
            {
                return "棒読みちゃんの読み上げ文字列をVOICEPEAKへ転送します。\n" +
                       "棒読みちゃん既定音声は常に抑止し、失敗時は無音スキップします。";
            }
        }

        public FNF.XmlSerializerSetting.ISettingFormData SettingFormData
        {
            get { return _settingFormData; }
        }

        // プラグイン開始
        public void Begin()
        {
            _stopping = false;
            string baseDir = ResolveBaseDirectory();
            _settingsPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_setting.json");
            _logPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_plugin.log");
            _logger = new FileLogger(_logPath);

            WorkerProcessManager.EnsureSettingsFileInitialized(_settingsPath, _logger);

            _settingsState = new PluginSettingsState();
            _settingsState.Configure(_settingsPath, _logger);
            _settingsState.LoadFromJson();
            _settingFormData = new PluginSettingFormData(_settingsState);

            WorkerBridgeClient.EnsureWorkerStarted(_settingsState.Settings.Plugin, _settingsPath, _logger);

            _running = true;
            _senderThread = new Thread(SenderThreadMain);
            _senderThread.IsBackground = true;
            _senderThread.Name = "VoicepeakProxySender";
            _senderThread.Start();

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
            _stopping = true;

            if (_bouyomi != null)
            {
                _bouyomi.TalkTaskStarted -= OnTalkTaskStarted;
                _bouyomi = null;
            }

            _running = false;
            _queueSignal.Set();
            if (_senderThread != null)
            {
                _senderThread.Join(2000);
                _senderThread = null;
            }

            if (_settingsState != null)
            {
                WorkerBridgeClient.SendShutdown(_settingsState.Settings.Plugin, _logger);
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
                if (_stopping)
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

                EnqueueTalk(taskId, text);
            }
            catch (Exception ex)
            {
                _logger.Error("on_talk_task_started_error", ex.Message);
            }
        }

        // テキストをキューへ投入
        private void EnqueueTalk(int taskId, string text)
        {
            PluginRuntimeConfig runtime = _settingsState.Settings.Plugin;
            int maxQueueLength = runtime.MaxQueueLength <= 0 ? 200 : runtime.MaxQueueLength;

            lock (_queueLock)
            {
                if (_queue.Count >= maxQueueLength)
                {
                    _logger.Warn("queue_overflow_drop_new taskId=" + taskId);
                    return;
                }

                WorkerSpeakRequest request = new WorkerSpeakRequest();
                request.Command = "speak";
                request.TaskId = taskId;
                request.Text = text;
                _queue.Enqueue(request);

                _logger.Info("enqueue taskId=" + taskId + " length=" + text.Length);
            }

            _queueSignal.Set();
        }

        // 送信スレッド本体
        private void SenderThreadMain()
        {
            try
            {
                while (true)
                {
                    if (!_running)
                    {
                        break;
                    }

                    WorkerSpeakRequest request = null;
                    lock (_queueLock)
                    {
                        if (_queue.Count > 0)
                        {
                            request = _queue.Dequeue();
                        }
                    }

                    if (request == null)
                    {
                        _queueSignal.WaitOne(500);
                        continue;
                    }

                    PluginSettingsState state = _settingsState;
                    PluginRuntimeConfig runtime = state != null && state.Settings != null ? state.Settings.Plugin : null;
                    if (runtime == null)
                    {
                        runtime = new PluginRuntimeConfig();
                    }

                    FileLogger logger = _logger;
                    WorkerSpeakResponse response = WorkerBridgeClient.Send(
                        runtime,
                        request);
                    if (!response.Accepted && logger != null)
                    {
                        throw new InvalidOperationException("Workerが異常終了したため、プラグインを停止しました。詳細はPlugin_VoicepeakProxy_worker.logを確認してください。 taskId=" + request.TaskId + " error=" + response.ErrorMessage);
                    }
                    else if (logger != null)
                    {
                        logger.Info("worker_accepted taskId=" + request.TaskId);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger logger = _logger;
                if (logger != null)
                {
                    logger.Error("sender_thread_fatal", ex.Message);
                }

                _running = false;
                _stopping = true;
                _queueSignal.Set();
                throw;
            }
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

        // Bouyomiインスタンスへイベント購読
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

            _bouyomi.EnableTalkTaskStarted = true;
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

    // Worker連携の調停を提供
    internal static class WorkerBridgeClient
    {
        // Workerの起動状態を保証
        public static void EnsureWorkerStarted(PluginRuntimeConfig runtime, string settingsPath, FileLogger logger)
        {
            WorkerProcessManager.EnsureStarted(runtime, settingsPath, logger);
        }

        // ワーカー停止を要求
        public static void SendShutdown(PluginRuntimeConfig runtime, FileLogger logger)
        {
            PluginRuntimeConfig shutdownRuntime = CloneRuntimeForShutdown(runtime);
            WorkerSpeakRequest request = new WorkerSpeakRequest();
            request.Command = "shutdown";
            WorkerSpeakResponse response = WorkerIpcClient.TrySend(shutdownRuntime, request);
            if (!response.Accepted)
            {
                logger.Warn("worker_shutdown_request_failed error=" + response.ErrorMessage);
            }

            WorkerProcessManager.TryStopWithTimeout(runtime, logger, 5000);
        }

        // Workerへ発話要求を送信
        public static WorkerSpeakResponse Send(
            PluginRuntimeConfig runtime,
            WorkerSpeakRequest request)
        {
            WorkerSpeakResponse response = WorkerIpcClient.TrySend(runtime, request);
            return response;
        }

        // 停止用設定を作成
        private static PluginRuntimeConfig CloneRuntimeForShutdown(PluginRuntimeConfig runtime)
        {
            PluginRuntimeConfig copied = new PluginRuntimeConfig();
            copied.PipeName = runtime.PipeName;
            copied.WorkerExePath = runtime.WorkerExePath;
            copied.PipeConnectTimeoutMs = 300;
            copied.MaxQueueLength = runtime.MaxQueueLength;
            return copied;
        }

    }

    // Workerプロセスの起動停止を管理
    internal static class WorkerProcessManager
    {
        // 設定ファイルを初期化
        public static void EnsureSettingsFileInitialized(string settingsPath, FileLogger logger)
        {
            if (File.Exists(settingsPath))
            {
                return;
            }

            string basePath = ResolveBasePath();
            string workerPath = ResolveWorkerPath(new PluginRuntimeConfig(), basePath);
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
            string workerPath = ResolveWorkerPath(runtime, basePath);
            if (!File.Exists(workerPath))
            {
                throw new InvalidOperationException("worker_not_found path=" + workerPath);
            }

            Process startedProcess = null;
            if (IsRunning(workerPath))
            {
                logger.Info("worker_already_running path=" + workerPath);
            }
            else
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = workerPath;
                psi.WorkingDirectory = Path.GetDirectoryName(workerPath);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                string logPath = Path.Combine(basePath, "Plugin_VoicepeakProxy_worker.log");
                psi.Arguments = "--pipe \"" + runtime.PipeName + "\" --settings \"" + settingsPath + "\" --log \"" + logPath + "\"";
                startedProcess = Process.Start(psi);
                if (startedProcess == null)
                {
                    throw new InvalidOperationException("worker_start_failed path=" + workerPath);
                }

                logger.Info("worker_started path=" + workerPath + " pid=" + startedProcess.Id);
            }

            WaitForReady(runtime, startedProcess, logger, 30000, 500);
        }

        // Worker停止を補助
        public static void TryStopWithTimeout(PluginRuntimeConfig runtime, FileLogger logger, int gracefulWaitMs)
        {
            string workerPath = ResolveWorkerPath(runtime, ResolveBasePath());
            string processName = Path.GetFileNameWithoutExtension(workerPath);
            int waitMs = gracefulWaitMs < 0 ? 0 : gracefulWaitMs;
            Process[] processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    bool pathMatched = false;
                    try
                    {
                        pathMatched = string.Equals(p.MainModule.FileName, workerPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        pathMatched = true;
                    }

                    if (!pathMatched || p.HasExited)
                    {
                        continue;
                    }

                    if (waitMs > 0)
                    {
                        bool exited = p.WaitForExit(waitMs);
                        if (exited)
                        {
                            logger.Info("worker_stopped_gracefully pid=" + p.Id);
                            continue;
                        }

                        logger.Warn("worker_stop_timeout_force_kill pid=" + p.Id + " waitMs=" + waitMs);
                    }

                    p.Kill();
                    p.WaitForExit(1000);
                    logger.Info("worker_force_stopped pid=" + p.Id);
                }
                catch (Exception ex)
                {
                    logger.Warn("worker_stop_failed pid=" + p.Id + " error=" + ex.Message);
                }
            }
        }

        // 実行中判定
        private static bool IsRunning(string workerPath)
        {
            string processName = Path.GetFileNameWithoutExtension(workerPath);
            Process[] processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (string.Equals(p.MainModule.FileName, workerPath, StringComparison.OrdinalIgnoreCase))
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
        private static string ResolveWorkerPath(PluginRuntimeConfig runtime, string basePath)
        {
            string workerPath = runtime.WorkerExePath;
            if (string.IsNullOrEmpty(workerPath))
            {
                workerPath = Path.Combine(Path.Combine(basePath, "VoicepeakProxyWorker"), "VoicepeakProxyWorker.exe");
            }

            return workerPath;
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
                    throw new InvalidOperationException("worker_ready_timeout error=" + response.ErrorMessage + " waitedMs=" + elapsed);
                }

                Thread.Sleep(pollMs);
            }
        }

        // 待機確認用設定を作成
        private static PluginRuntimeConfig CloneRuntimeForPing(PluginRuntimeConfig runtime)
        {
            PluginRuntimeConfig copied = new PluginRuntimeConfig();
            copied.PipeName = runtime.PipeName;
            copied.WorkerExePath = runtime.WorkerExePath;
            copied.PipeConnectTimeoutMs = 200;
            copied.MaxQueueLength = runtime.MaxQueueLength;
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
        private readonly object _sync = new object();
        private readonly string _filePath;

        public FileLogger(string filePath)
        {
            _filePath = filePath;
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message, string detail)
        {
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

        public void Dispose()
        {
        }
    }
}
