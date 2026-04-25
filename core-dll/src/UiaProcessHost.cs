using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoicepeakProxyCore;

// UIAサブプロセス管理を担当
internal sealed class UiaProcessHost : IDisposable
{
    private const int ConnectTimeoutMs = 1500;
    private const int ReadyTimeoutMs = 1500;
    private const int RequestTimeoutMs = 1500;
    private const int RecycleWaitTimeoutMs = 1500;
    private readonly object _gate = new object();
    private readonly AppLogger _log;
    private readonly int _recycleIntervalMs;
    private readonly ManualResetEventSlim _readyEvent = new ManualResetEventSlim(false);
    private ProbeSession _session;
    private HostState _state;
    private int _generation;
    private DateTime _sessionStartedUtc;
    private int _inFlight;
    private bool _pendingRecycle;
    private bool _recycleWorkerRunning;
    private bool _disposed;

    // 設定を保持
    public UiaProcessHost(int recycleIntervalSec, AppLogger log)
    {
        _recycleIntervalMs = Math.Max(1, recycleIntervalSec) * 1000;
        _log = log;
        _state = HostState.Stopped;
    }

    // UIA読み取り要求を実行
    public ReadInputSnapshot ReadInputSnapshot(IntPtr mainHwnd)
    {
        try
        {
            if (!TryAcquireRunningSession(out ProbeSession session))
            {
                return BuildProbeFailureSnapshot();
            }

            ReadInputSnapshot snapshot;
            bool requestOk = TryRequestWithSingleRetry(session, mainHwnd, out snapshot);
            if (!requestOk)
            {
                return BuildProbeFailureSnapshot();
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log?.Warn("uia_probe_unhandled_exception detail=" + ex.GetType().Name);
            return BuildProbeFailureSnapshot();
        }
    }

    // 発話開始後safe pointで再起動を予約
    public void NotifyPlaybackSafePoint()
    {
        bool shouldStartWorker;
        lock (_gate)
        {
            if (_disposed || _state != HostState.Running || _session == null || _inFlight != 0)
            {
                return;
            }

            TryMarkPendingRecycleByElapsedUnsafe();
            if (!_pendingRecycle || _recycleWorkerRunning)
            {
                return;
            }

            _state = HostState.Recycling;
            _readyEvent.Reset();
            _recycleWorkerRunning = true;
            shouldStartWorker = true;
        }

        if (!shouldStartWorker)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => RunBackgroundRecycle("playback_safe_point"));
    }

    // リソースを破棄
    public void Dispose()
    {
        ProbeSession session;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = HostState.Stopped;
            session = _session;
            _session = null;
            _readyEvent.Set();
        }

        session?.Dispose();
        _readyEvent.Dispose();
    }

    // 稼働中セッションを取得
    private bool TryAcquireRunningSession(out ProbeSession session)
    {
        session = null;
        while (true)
        {
            bool waitReady;
            bool shouldStart;
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                if (_state == HostState.Running && _session != null)
                {
                    _inFlight++;
                    session = _session;
                    return true;
                }

                if (_state == HostState.Recycling)
                {
                    waitReady = true;
                    shouldStart = false;
                }
                else
                {
                    _state = HostState.Recycling;
                    _readyEvent.Reset();
                    waitReady = false;
                    shouldStart = true;
                }
            }

            if (shouldStart)
            {
                if (!StartSessionCore())
                {
                    return false;
                }

                continue;
            }

            if (waitReady)
            {
                if (!_readyEvent.Wait(RecycleWaitTimeoutMs))
                {
                    _log?.Warn($"uia_probe_ready_timeout timeoutMs={RecycleWaitTimeoutMs}");
                    return false;
                }
            }
        }
    }

    // 要求を1回再試行で実行
    private bool TryRequestWithSingleRetry(ProbeSession session, IntPtr mainHwnd, out ReadInputSnapshot snapshot)
    {
        snapshot = default(ReadInputSnapshot);
        try
        {
            if (TryRequestCore(session, mainHwnd, out snapshot))
            {
                return true;
            }
        }
        finally
        {
            ReleaseInFlight();
        }

        _log?.Warn("uia_probe_request_failed retry=1");
        BeginRecycleForFailure();
        if (!TryAcquireRunningSession(out ProbeSession retriedSession))
        {
            return false;
        }

        try
        {
            return TryRequestCore(retriedSession, mainHwnd, out snapshot);
        }
        finally
        {
            ReleaseInFlight();
        }
    }

    // 1回要求を送受信
    private bool TryRequestCore(ProbeSession session, IntPtr mainHwnd, out ReadInputSnapshot snapshot)
    {
        snapshot = default(ReadInputSnapshot);
        if (session == null)
        {
            return false;
        }

        string request = string.Concat("READ\t", mainHwnd.ToInt64().ToString());
        if (!session.TryRequest(request, RequestTimeoutMs, out string response))
        {
            return false;
        }

        return TryParseResponse(response, out snapshot);
    }

    // 経過時間で再起動予約を更新
    private void TryMarkPendingRecycleByElapsedUnsafe()
    {
        if (_pendingRecycle)
        {
            return;
        }

        double elapsedMs = (DateTime.UtcNow - _sessionStartedUtc).TotalMilliseconds;
        if (elapsedMs >= _recycleIntervalMs)
        {
            _pendingRecycle = true;
            _log?.Info("uia_probe_recycle_pending reason=elapsed_interval");
        }
    }

    // in-flight要求数を解放
    private void ReleaseInFlight()
    {
        lock (_gate)
        {
            if (_inFlight > 0)
            {
                _inFlight--;
            }
        }
    }

    // バックグラウンド再起動を実行
    private void RunBackgroundRecycle(string reason)
    {
        try
        {
            RecycleCore(reason);
        }
        finally
        {
            lock (_gate)
            {
                _recycleWorkerRunning = false;
            }
        }
    }

    // 障害時の再起動を実行
    private void BeginRecycleForFailure()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_state != HostState.Recycling)
            {
                _state = HostState.Recycling;
                _readyEvent.Reset();
            }
        }

        RecycleCore("request_failed");
    }

    // セッション再起動を実行
    private void RecycleCore(string reason)
    {
        _log?.Info($"uia_probe_recycle_start reason={reason}");
        ProbeSession oldSession;
        lock (_gate)
        {
            oldSession = _session;
            _session = null;
        }

        if (oldSession != null)
        {
            try
            {
                oldSession.Dispose();
            }
            catch (Exception ex)
            {
                _log?.Warn("uia_probe_dispose_failed detail=" + ex.GetType().Name);
            }
        }

        bool started = StartSessionCore();
        if (started)
        {
            _log?.Info($"uia_probe_recycled reason={reason}");
        }
    }

    // 新規セッションを起動
    private bool StartSessionCore()
    {
        if (!ProbeSession.TryStart(ConnectTimeoutMs, ReadyTimeoutMs, out ProbeSession session, out string reason, out int generation))
        {
            lock (_gate)
            {
                _state = HostState.Stopped;
                _readyEvent.Set();
            }

            _log?.Warn($"uia_probe_start_failed reason={reason}");
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                session.Dispose();
                return false;
            }

            _session = session;
            _state = HostState.Running;
            _generation = generation;
            _pendingRecycle = false;
            _sessionStartedUtc = DateTime.UtcNow;
            _readyEvent.Set();
        }

        _log?.Info($"uia_probe_ready generation={generation}");
        return true;
    }

    // 応答文字列を契約型へ変換
    private static bool TryParseResponse(string response, out ReadInputSnapshot snapshot)
    {
        snapshot = default(ReadInputSnapshot);
        if (string.IsNullOrEmpty(response))
        {
            return false;
        }

        string[] parts = response.Split('\t');
        if (parts.Length < 6 || !string.Equals(parts[0], "OK", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int successFlag)
            || !int.TryParse(parts[2], out int visibleBlockCount)
            || !int.TryParse(parts[3], out int totalLength)
            || !int.TryParse(parts[4], out int sourceValue))
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(parts[5]));
        }
        catch
        {
            return false;
        }

        ReadInputSource source = Enum.IsDefined(typeof(ReadInputSource), sourceValue)
            ? (ReadInputSource)sourceValue
            : ReadInputSource.Exception;
        ReadInputResult read = successFlag != 0
            ? ReadInputResult.Ok(text, totalLength, source)
            : ReadInputResult.Fail(source, text, totalLength);
        snapshot = new ReadInputSnapshot(read, visibleBlockCount);
        return true;
    }

    // 失敗契約を返却
    private static ReadInputSnapshot BuildProbeFailureSnapshot()
    {
        return new ReadInputSnapshot(ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0), 0);
    }

    // ホスト状態
    private enum HostState
    {
        Stopped,
        Recycling,
        Running
    }

    // サブプロセス接続を保持
    private sealed class ProbeSession : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Process _process;
        private readonly IntPtr _jobHandle;
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private ProbeSession(Process process, IntPtr jobHandle, NamedPipeClientStream pipe)
        {
            _process = process;
            _jobHandle = jobHandle;
            _pipe = pipe;
            _reader = new StreamReader(_pipe, Utf8NoBom, false, 1024, true);
            _writer = new StreamWriter(_pipe, Utf8NoBom, 1024, true) { AutoFlush = true };
        }

        // プロセスを起動してREADYまで待機
        public static bool TryStart(int connectTimeoutMs, int readyTimeoutMs, out ProbeSession session, out string reason, out int generation)
        {
            session = null;
            reason = string.Empty;
            generation = 0;
            string probePath = ResolveProbePath();
            if (!File.Exists(probePath))
            {
                reason = "probe_exe_not_found path=" + probePath;
                return false;
            }

            string pipeName = "voicepeak_uia_probe_" + Guid.NewGuid().ToString("N");
            Process process = null;
            NamedPipeClientStream pipe = null;
            IntPtr jobHandle = IntPtr.Zero;

            // 起動失敗時の後始末を共通化
            void CleanupStartFailure()
            {
                try
                {
                    pipe?.Dispose();
                }
                catch
                {
                }

                if (jobHandle != IntPtr.Zero)
                {
                    try
                    {
                        CloseHandle(jobHandle);
                    }
                    catch
                    {
                    }
                }

                if (process != null)
                {
                    TryKill(process);
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }

            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = probePath,
                    Arguments = "--pipe " + pipeName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process = Process.Start(psi);
                if (process == null)
                {
                    reason = "probe_process_start_failed";
                    CleanupStartFailure();
                    return false;
                }

                jobHandle = CreateKillOnCloseJob();
                if (jobHandle == IntPtr.Zero || !AssignProcessToJobObject(jobHandle, process.Handle))
                {
                    reason = "job_object_assign_failed";
                    CleanupStartFailure();
                    return false;
                }

                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
                pipe.Connect(Math.Max(1, connectTimeoutMs));

                using (StreamReader reader = new StreamReader(pipe, Utf8NoBom, false, 1024, true))
                {
                    var readyTask = reader.ReadLineAsync();
                    if (!readyTask.Wait(Math.Max(1, readyTimeoutMs)))
                    {
                        reason = "probe_ready_timeout";
                        CleanupStartFailure();
                        return false;
                    }

                    string readyLine = readyTask.Result ?? string.Empty;
                    if (!TryParseReadyLine(readyLine, out generation))
                    {
                        reason = "probe_ready_invalid";
                        CleanupStartFailure();
                        return false;
                    }
                }

                session = new ProbeSession(process, jobHandle, pipe);
                process = null;
                pipe = null;
                jobHandle = IntPtr.Zero;
                return true;
            }
            catch (Exception ex)
            {
                reason = "probe_start_exception:" + ex.GetType().Name;
                CleanupStartFailure();
                return false;
            }
        }

        // 1往復要求を実行
        public bool TryRequest(string request, int timeoutMs, out string response)
        {
            response = string.Empty;
            try
            {
                _writer.WriteLine(request ?? string.Empty);
                var readTask = _reader.ReadLineAsync();
                if (!readTask.Wait(Math.Max(1, timeoutMs)))
                {
                    return false;
                }

                response = readTask.Result ?? string.Empty;
                return !string.IsNullOrEmpty(response);
            }
            catch
            {
                return false;
            }
        }

        // 接続を終了
        public void Dispose()
        {
            try
            {
                _writer?.WriteLine("QUIT");
            }
            catch
            {
            }

            try
            {
                _writer?.Dispose();
            }
            catch
            {
            }

            try
            {
                _reader?.Dispose();
            }
            catch
            {
            }

            try
            {
                _pipe?.Dispose();
            }
            catch
            {
            }

            TryKill(_process);
            try
            {
                _process?.Dispose();
            }
            catch
            {
            }

            if (_jobHandle != IntPtr.Zero)
            {
                try
                {
                    CloseHandle(_jobHandle);
                }
                catch
                {
                }
            }
        }

        // READY応答を解析
        private static bool TryParseReadyLine(string line, out int generation)
        {
            generation = 0;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 2 || !string.Equals(parts[0], "READY", StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(parts[1], out generation);
        }

        // プローブパスをdeps固定で解決
        private static string ResolveProbePath()
        {
            string asmDir = Path.GetDirectoryName(typeof(UiaProcessHost).Assembly.Location) ?? string.Empty;
            return Path.Combine(asmDir, "VoicepeakProxyCore.deps", "VoicepeakProxyCore.UiaProbe.exe");
        }

        // 子プロセスをkill
        private static void TryKill(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Kill();
                process.WaitForExit(500);
            }
            catch
            {
            }
        }

        // kill-on-closeのjobを作成
        private static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                bool ok = SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)length);
                if (!ok)
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }

                return job;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;

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
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
