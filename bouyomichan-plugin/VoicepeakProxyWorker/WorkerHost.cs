using System;
using System.Collections.Generic;
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
    private readonly WorkerFileLogger _logger;
    private readonly WorkerSettingsProvider _settingsProvider;
    private readonly object _queueLock = new object();
    private readonly Queue<WorkerSpeakRequest> _queue = new Queue<WorkerSpeakRequest>();
    private readonly AutoResetEvent _queueSignal = new AutoResetEvent(false);

    private bool _running;
    private Thread _consumeThread;

    public WorkerHost(string pipeName, string settingsPath, WorkerFileLogger logger)
    {
        _pipeName = string.IsNullOrEmpty(pipeName) ? "voicepeak_proxycore_bridge" : pipeName;
        _settingsPath = settingsPath;
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

        _consumeThread = new Thread(ConsumeThreadMain);
        _consumeThread.IsBackground = true;
        _consumeThread.Name = "VoicepeakProxyWorkerConsume";
        _consumeThread.Start();

        while (_running)
        {
            ServeSingleConnection();
        }

        return true;
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
                        _queueSignal.Set();
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

                    WorkerSpeakResponse queued = TryEnqueue(request);
                    if (queued.Accepted)
                    {
                        _logger.Info("queue_accept taskId=" + request.TaskId + " length=" + request.Text.Length);
                    }
                    PipeFraming.WriteFrame(writer, serializer.Serialize(queued));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("serve_connection_failed detail=" + ex.Message);
            Thread.Sleep(250);
        }
    }

    // リクエストをキューへ投入
    private WorkerSpeakResponse TryEnqueue(WorkerSpeakRequest request)
    {
        PluginSettingsFile settings = _settingsProvider.GetCurrent();
        int maxQueue = settings.Plugin.MaxQueueLength <= 0 ? 200 : settings.Plugin.MaxQueueLength;

        lock (_queueLock)
        {
            if (_queue.Count >= maxQueue)
            {
                WorkerSpeakResponse full = new WorkerSpeakResponse();
                full.Accepted = false;
                full.ErrorMessage = "worker_queue_full";
                return full;
            }

            _queue.Enqueue(request);
            _queueSignal.Set();
        }

        WorkerSpeakResponse accepted = new WorkerSpeakResponse();
        accepted.Accepted = true;
        return accepted;
    }

    // 実行スレッド本体
    private void ConsumeThreadMain()
    {
        while (_running)
        {
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

            ExecuteSpeak(request);
        }
    }

    // 読み上げを実行
    private void ExecuteSpeak(WorkerSpeakRequest request)
    {
        try
        {
            PluginSettingsFile settings = _settingsProvider.GetCurrent();
            AppConfig config = AppConfigMapper.Map(settings.AppConfig);
            _logger.Info("speak_start taskId=" + request.TaskId + " length=" + request.Text.Length);

            SpeakOnceRequest speakRequest = new SpeakOnceRequest();
            speakRequest.Text = request.Text;

            SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(config, speakRequest, _logger);
            if (!result.Succeeded)
            {
                _logger.Warn("speak_failed taskId=" + request.TaskId + " status=" + result.Status + " error=" + result.ErrorMessage);
                return;
            }

            _logger.Info("speak_ok taskId=" + request.TaskId + " segments=" + result.SegmentsExecuted);
        }
        catch (Exception ex)
        {
            _logger.Error("execute_speak_failed detail=" + ex.Message);
        }
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

    public void Dispose()
    {
    }
}
