using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using BouyomiVoicepeakBridge.Shared;
using VoicepeakProxyCore;

namespace VoicepeakProxyWorker;

// Workerの受信と実行を管理
internal sealed class WorkerHost
{
    private readonly string _pipeName;
    private readonly string _settingsPath;
    private readonly int _ownerPid;
    private readonly WorkerFileLogger _logger;
    private readonly WorkerSettingsProvider _settingsProvider;

    private bool _running;
    private Thread _ownerWatchThread;

    public WorkerHost(string pipeName, string settingsPath, int ownerPid, WorkerFileLogger logger)
    {
        _pipeName = string.IsNullOrEmpty(pipeName) ? "voicepeak_proxy_bridge" : pipeName;
        _settingsPath = settingsPath;
        _ownerPid = ownerPid;
        _logger = logger;
        _settingsProvider = new WorkerSettingsProvider(settingsPath);
    }

    // Worker処理を開始
    public bool Run()
    {
        if (!TryRunStartupValidation())
        {
            return false;
        }

        _running = true;
        StartOwnerWatchThread();

        while (_running)
        {
            ServeSingleConnection();
        }

        return true;
    }

    // 親プロセス監視スレッドを開始
    private void StartOwnerWatchThread()
    {
        if (_ownerPid <= 0)
        {
            _logger.Warn("owner_watch_skipped ownerPid=" + _ownerPid);
            return;
        }

        _ownerWatchThread = new Thread(OwnerWatchThreadMain);
        _ownerWatchThread.IsBackground = true;
        _ownerWatchThread.Name = "VoicepeakProxyWorkerOwnerWatch";
        _ownerWatchThread.Start();
    }

    // 親プロセス終了を監視
    private void OwnerWatchThreadMain()
    {
        while (_running)
        {
            if (!IsOwnerAlive(_ownerPid))
            {
                _logger.Warn("owner_process_exited ownerPid=" + _ownerPid);
                Environment.Exit(3);
            }

            Thread.Sleep(1000);
        }
    }

    // 親プロセスの生存を確認
    private static bool IsOwnerAlive(int ownerPid)
    {
        try
        {
            Process owner = Process.GetProcessById(ownerPid);
            return owner != null && !owner.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // 接続1件を受理
    private void ServeSingleConnection()
    {
        try
        {
            using (NamedPipeServerStream server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None))
            {
                server.WaitForConnection();

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                using (BinaryReader reader = new BinaryReader(server, Encoding.UTF8, true))
                using (BinaryWriter writer = new BinaryWriter(server, new UTF8Encoding(false), true))
                {
                    string line;
                    if (!PipeFraming.TryReadFrame(reader, out line))
                    {
                        WorkerSpeakResponse emptyResponse = new WorkerSpeakResponse();
                        emptyResponse.Accepted = false;
                        emptyResponse.ErrorMessage = "empty_request";
                        PipeFraming.WriteFrame(writer, serializer.Serialize(emptyResponse));
                        return;
                    }

                    if (string.IsNullOrEmpty(line))
                    {
                        WorkerSpeakResponse emptyResponse = new WorkerSpeakResponse();
                        emptyResponse.Accepted = false;
                        emptyResponse.ErrorMessage = "empty_request";
                        PipeFraming.WriteFrame(writer, serializer.Serialize(emptyResponse));
                        return;
                    }

                    WorkerSpeakRequest request = null;
                    try
                    {
                        line = line.TrimStart('\uFEFF');
                        request = serializer.Deserialize<WorkerSpeakRequest>(line);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("invalid_json_received detail=" + ex.Message);
                        WorkerSpeakResponse invalidResponse = new WorkerSpeakResponse();
                        invalidResponse.Accepted = false;
                        invalidResponse.ErrorMessage = "invalid_json " + ex.Message;
                        PipeFraming.WriteFrame(writer, serializer.Serialize(invalidResponse));
                        return;
                    }

                    if (request == null)
                    {
                        WorkerSpeakResponse invalidRequest = new WorkerSpeakResponse();
                        invalidRequest.Accepted = false;
                        invalidRequest.ErrorMessage = "invalid_request";
                        PipeFraming.WriteFrame(writer, serializer.Serialize(invalidRequest));
                        return;
                    }

                    string command = (request.Command ?? "speak").Trim().ToLowerInvariant();
                    _logger.Info("ipc_command command=" + command + " taskId=" + request.TaskId);
                    if (command == "ping")
                    {
                        WorkerSpeakResponse ok = new WorkerSpeakResponse();
                        ok.Accepted = true;
                        PipeFraming.WriteFrame(writer, serializer.Serialize(ok));
                        return;
                    }

                    if (command == "shutdown")
                    {
                        _running = false;
                        WorkerSpeakResponse ok = new WorkerSpeakResponse();
                        ok.Accepted = true;
                        PipeFraming.WriteFrame(writer, serializer.Serialize(ok));
                        return;
                    }

                    if (command != "speak")
                    {
                        WorkerSpeakResponse invalidCommand = new WorkerSpeakResponse();
                        invalidCommand.Accepted = false;
                        invalidCommand.ErrorMessage = "unknown_command";
                        PipeFraming.WriteFrame(writer, serializer.Serialize(invalidCommand));
                        return;
                    }

                    if (string.IsNullOrEmpty(request.Text))
                    {
                        WorkerSpeakResponse invalidRequest = new WorkerSpeakResponse();
                        invalidRequest.Accepted = false;
                        invalidRequest.ErrorMessage = "text_is_empty";
                        PipeFraming.WriteFrame(writer, serializer.Serialize(invalidRequest));
                        return;
                    }

                    WorkerSpeakResponse response = ExecuteSpeak(request);
                    PipeFraming.WriteFrame(writer, serializer.Serialize(response));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("serve_connection_failed detail=" + ex.Message);
            Thread.Sleep(250);
        }
    }

    // 読み上げを実行
    private WorkerSpeakResponse ExecuteSpeak(WorkerSpeakRequest request)
    {
        try
        {
            PluginSettingsFile settings = _settingsProvider.GetCurrent();
            AppConfig config = AppConfigMapper.Map(settings.AppConfig);
            _logger.Info("speak_start taskId=" + request.TaskId + " length=" + request.Text.Length);

            SpeakOnceRequest speakRequest = new SpeakOnceRequest();
            speakRequest.Text = request.Text;

            SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(config, speakRequest, _logger);
            ClearInputOnceResult clearResult = VoicepeakOneShot.ClearInputOnce(config, _logger);
            if (!clearResult.Succeeded)
            {
                _logger.Warn("post_speak_clear_failed taskId=" + request.TaskId + " status=" + clearResult.Status + " error=" + clearResult.ErrorMessage);
            }
            else
            {
                _logger.Info("post_speak_clear_ok taskId=" + request.TaskId);
            }

            if (!result.Succeeded)
            {
                if (IsNonFatalDropStatus(result.Status))
                {
                    _logger.Warn("speak_dropped_non_fatal taskId=" + request.TaskId + " status=" + result.Status + " error=" + result.ErrorMessage);
                    WorkerSpeakResponse dropped = new WorkerSpeakResponse();
                    dropped.Accepted = true;
                    return dropped;
                }

                _logger.Warn("speak_failed taskId=" + request.TaskId + " status=" + result.Status + " error=" + result.ErrorMessage);
                WorkerSpeakResponse failed = new WorkerSpeakResponse();
                failed.Accepted = false;
                failed.ErrorMessage = "speak_failed status=" + result.Status + " error=" + result.ErrorMessage;
                return failed;
            }

            _logger.Info("speak_ok taskId=" + request.TaskId + " segments=" + result.SegmentsExecuted);
            WorkerSpeakResponse ok = new WorkerSpeakResponse();
            ok.Accepted = true;
            return ok;
        }
        catch (Exception ex)
        {
            _logger.Error("execute_speak_failed detail=" + ex.Message);
            WorkerSpeakResponse failed = new WorkerSpeakResponse();
            failed.Accepted = false;
            failed.ErrorMessage = "execute_speak_failed detail=" + ex.Message;
            return failed;
        }
    }

    // 継続可能なドロップ対象を判定
    private static bool IsNonFatalDropStatus(SpeakOnceStatus status)
    {
        // InvalidRequest: 入力不正はWorker側そのものには悪影響なし
        // StartConfirmTimeout: 再生失敗だが初期化時には入力成功しているはずなのでおそらく設定値が際を攻めすぎているパターン、一応継続
        return status == SpeakOnceStatus.InvalidRequest
            || status == SpeakOnceStatus.StartConfirmTimeout;
    }

    // 起動時検証を実行
    private bool TryRunStartupValidation()
    {
        try
        {
            PluginSettingsFile settings = _settingsProvider.GetCurrent();
            AppConfig config = AppConfigMapper.Map(settings.AppConfig);
            ValidateInputOnceResult validate = VoicepeakOneShot.ValidateInputOnce(config, _logger);
            if (!validate.Succeeded)
            {
                _logger.Warn("startup_validation_failed status=" + validate.Status + " error=" + validate.ErrorMessage);
                return false;
            }

            _logger.Info("startup_validation_ok");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("startup_validation_error detail=" + ex.Message);
            return false;
        }
    }

}

// Worker設定のキャッシュ取得を提供
internal sealed class WorkerSettingsProvider
{
    private readonly string _settingsPath;
    private readonly object _sync = new object();
    private bool _hasCache;
    private DateTime _lastWriteUtc;
    private PluginSettingsFile _cached;

    public WorkerSettingsProvider(string settingsPath)
    {
        _settingsPath = settingsPath;
        _hasCache = false;
        _lastWriteUtc = DateTime.MinValue;
        _cached = null;
    }

    // 最新設定を取得
    public PluginSettingsFile GetCurrent()
    {
        lock (_sync)
        {
            DateTime writeUtc = File.Exists(_settingsPath)
                ? File.GetLastWriteTimeUtc(_settingsPath)
                : DateTime.MinValue;

            if (_hasCache && writeUtc == _lastWriteUtc && _cached != null)
            {
                return _cached;
            }

            PluginSettingsFile loaded = JsonFileStore.LoadOrDefault<PluginSettingsFile>(
                _settingsPath,
                () => SettingsBootstrapper.CreateDefaultSettings());
            loaded.Normalize();

            _cached = loaded;
            _lastWriteUtc = writeUtc;
            _hasCache = true;
            return _cached;
        }
    }
}

// Workerログをファイルへ出力
internal sealed class WorkerFileLogger : IAppLogger, IDisposable
{
    private readonly object _sync = new object();
    private readonly string _logPath;

    public WorkerFileLogger(string logPath)
    {
        _logPath = logPath;
        ResetLogFile();
    }

    public void Debug(string message)
    {
        Write("DEBUG", message);
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    // ログ1行を書き込む
    private void Write(string level, string message)
    {
        lock (_sync)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
            File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    // 起動時にログを初期化
    private void ResetLogFile()
    {
        try
        {
            string directory = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_logPath, string.Empty, Encoding.UTF8);
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
