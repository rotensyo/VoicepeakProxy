using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using EasyHook;

namespace VoicepeakProxyCore;

[Serializable]
public sealed class ModifierHookStatsSnapshot
{
    public long GetKeyStateCalls { get; set; }
    public long GetAsyncKeyStateCalls { get; set; }
    public long GetKeyboardStateCalls { get; set; }
    public long ModifierQueries { get; set; }
    public long NeutralizedCalls { get; set; }
    public string ThreadCallSummary { get; set; } = string.Empty;
}

public enum ModifierHookApi
{
    GetKeyState,
    GetAsyncKeyState,
    GetKeyboardState
}

internal enum ModifierOverrideMode
{
    Neutralize,
    Ctrl,
    Alt,
    Shift
}

// 修飾キー中立化フックの制御
internal sealed class ModifierKeyHookController
{
    private readonly object _gate = new object();
    private readonly int _hookCommandTimeoutMs;
    private readonly int _hookConnectTimeoutMs;
    private readonly int _hookConnectTotalWaitMs;
    private readonly IModifierHookPlatform _platform;
    private IModifierHookConnection _connection;
    private int _pid;

    public ModifierKeyHookController(int hookCommandTimeoutMs, int hookConnectTimeoutMs, int hookConnectTotalWaitMs)
        : this(hookCommandTimeoutMs, hookConnectTimeoutMs, hookConnectTotalWaitMs, new DefaultModifierHookPlatform())
    {
    }

    internal ModifierKeyHookController(int hookCommandTimeoutMs, int hookConnectTimeoutMs, int hookConnectTotalWaitMs, IModifierHookPlatform platform)
    {
        _hookCommandTimeoutMs = Math.Max(1, hookCommandTimeoutMs);
        _hookConnectTimeoutMs = Math.Max(1, hookConnectTimeoutMs);
        _hookConnectTotalWaitMs = Math.Max(1, hookConnectTotalWaitMs);
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    // フック注入済み接続を保証
    public bool EnsureInjected(int pid, AppLogger log)
    {
        lock (_gate)
        {
            if (_pid == pid && IsConnected() && TryPing())
            {
                log.Debug($"modifier_hook_reused pid={pid}");
                return true;
            }

            DisposePipe();

            if (TryConnectExisting(pid, log, _hookConnectTimeoutMs))
            {
                _pid = pid;
                log.Debug($"modifier_hook_reused pid={pid}");
                return true;
            }

            bool injected = TryInject(pid, log);
            if (!injected && TryConnectExisting(pid, log, _hookConnectTimeoutMs))
            {
                _pid = pid;
                log.Debug($"modifier_hook_reused pid={pid}");
                return true;
            }

            if (!WaitForPipeReady(pid, log, _hookConnectTotalWaitMs))
            {
                log.Warn($"modifier_hook_connect_failed pid={pid} reason=pipe_not_ready");
                return false;
            }

            _pid = pid;
            if (injected)
            {
                log.Info($"modifier_hook_injected pid={pid}");
            }
            else
            {
                log.Debug($"modifier_hook_reused pid={pid}");
            }
            return true;
        }
    }

    // フック有効状態を設定
    public bool SetEnabled(bool enabled, AppLogger log)
    {
        lock (_gate)
        {
            int targetPid = _pid;
            if (TrySetEnabledOnce(enabled, log))
            {
                return true;
            }

            if (targetPid <= 0)
            {
                return false;
            }

            DisposeConnection(resetPid: false);
            if (!TryConnectExisting(targetPid, log, _hookConnectTimeoutMs))
            {
                log.Warn($"modifier_hook_state_retry_failed enabled={enabled} reason=reconnect_failed");
                return false;
            }

            if (!TrySetEnabledOnce(enabled, log))
            {
                log.Warn($"modifier_hook_state_retry_failed enabled={enabled} reason=retry_send_failed");
                return false;
            }

            return true;
        }
    }

    // ENABLEコマンドを1回送信
    private bool TrySetEnabledOnce(bool enabled, AppLogger log)
    {
        if (!SendCommand($"ENABLE|{(enabled ? 1 : 0)}", out string response))
        {
            log.Warn($"modifier_hook_state_failed enabled={enabled} reason=pipe_send_failed");
            return false;
        }

        if (!string.Equals(response, "OK", StringComparison.Ordinal))
        {
            log.Warn($"modifier_hook_state_failed enabled={enabled} reason={Sanitize(response)}");
            return false;
        }

        log.Info($"modifier_hook_state enabled={enabled}");
        return true;
    }

    // OVERRIDEコマンドを1回送信
    private bool TrySetModifierOverrideOnce(ModifierOverrideMode mode, AppLogger log)
    {
        string token;
        switch (mode)
        {
            case ModifierOverrideMode.Ctrl:
                token = "CTRL";
                break;
            case ModifierOverrideMode.Alt:
                token = "ALT";
                break;
            case ModifierOverrideMode.Shift:
                token = "SHIFT";
                break;
            default:
                token = "NONE";
                break;
        }

        if (!SendCommand($"OVERRIDE|{token}", out string response))
        {
            log.Warn($"modifier_hook_override_failed mode={mode} reason=pipe_send_failed");
            return false;
        }

        if (!string.Equals(response, "OK", StringComparison.Ordinal))
        {
            log.Warn($"modifier_hook_override_failed mode={mode} reason={Sanitize(response)}");
            return false;
        }

        log.Debug($"modifier_hook_override mode={mode}");
        return true;
    }

    // 修飾キー上書きモードを設定
    public bool SetModifierOverride(ModifierOverrideMode mode, AppLogger log)
    {
        lock (_gate)
        {
            int targetPid = _pid;
            if (TrySetModifierOverrideOnce(mode, log))
            {
                return true;
            }

            if (targetPid <= 0)
            {
                return false;
            }

            DisposeConnection(resetPid: false);
            if (!TryConnectExisting(targetPid, log, _hookConnectTimeoutMs))
            {
                log.Warn($"modifier_hook_override_retry_failed mode={mode} reason=reconnect_failed");
                return false;
            }

            if (!TrySetModifierOverrideOnce(mode, log))
            {
                log.Warn($"modifier_hook_override_retry_failed mode={mode} reason=retry_send_failed");
                return false;
            }

            return true;
        }
    }

    // 統計プローブを開始
    public void BeginStatsProbe(AppLogger log)
    {
        lock (_gate)
        {
            if (!SendCommand("STATS_RESET", out _))
            {
                log.Warn("modifier_hook_stats_probe_begin_failed reason=stats_reset_failed");
                return;
            }

            if (!SendCommand("STATS_ENABLE|1", out _))
            {
                log.Warn("modifier_hook_stats_probe_begin_failed reason=stats_enable_failed");
                return;
            }

            log.Debug("modifier_hook_stats_probe_begin");
        }
    }

    // 統計プローブを終了
    public void EndStatsProbe(AppLogger log)
    {
        lock (_gate)
        {
            if (!SendCommand("STATS_ENABLE|0", out _))
            {
                log.Warn("modifier_hook_stats_probe_end_failed reason=stats_disable_failed");
                return;
            }

            log.Debug("modifier_hook_stats_probe_end");
        }
    }

    // 統計スナップショットを取得
    public ModifierHookStatsSnapshot GetStatsSnapshot(AppLogger log)
    {
        lock (_gate)
        {
            if (!SendCommand("STATS_GET", out string response))
            {
                log.Warn("modifier_hook_stats_snapshot_failed reason=stats_get_failed");
                return null;
            }

            if (string.IsNullOrEmpty(response) || !response.StartsWith("STATS|", StringComparison.Ordinal))
            {
                log.Warn($"modifier_hook_stats_snapshot_failed reason={Sanitize(response)}");
                return null;
            }

            string[] parts = response.Split(new[] { '|' }, 7);
            if (parts.Length < 7)
            {
                log.Warn("modifier_hook_stats_snapshot_failed reason=invalid_stats_format");
                return null;
            }

            return new ModifierHookStatsSnapshot
            {
                GetKeyStateCalls = ParseLong(parts[1]),
                GetAsyncKeyStateCalls = ParseLong(parts[2]),
                GetKeyboardStateCalls = ParseLong(parts[3]),
                ModifierQueries = ParseLong(parts[4]),
                NeutralizedCalls = ParseLong(parts[5]),
                ThreadCallSummary = parts[6] ?? string.Empty
            };
        }
    }

    // 対象プロセスへフック注入
    private bool TryInject(int pid, AppLogger log)
    {
        try
        {
            string injectionLibrary = Assembly.GetExecutingAssembly().Location;
            string pipeName = GetPipeName(pid);
            log.Debug($"modifier_hook_inject_start pid={pid}");
            if (!_platform.Inject(pid, injectionLibrary, pipeName))
            {
                return false;
            }

            log.Debug($"modifier_hook_inject_done pid={pid}");
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"modifier_hook_inject_failed pid={pid} reason={Sanitize(ex.GetType().Name)}");
            return false;
        }
    }

    // 既存パイプへ接続
    private bool TryConnectExisting(int pid, AppLogger log, int timeoutMs)
    {
        string pipeName = GetPipeName(pid);
        if (_platform.TryConnect(pipeName, timeoutMs, out IModifierHookConnection connection, out Exception error))
        {
            _connection = connection;
            return true;
        }

        if (error != null)
        {
            log.Debug($"modifier_hook_connect_retry pid={pid} reason={Sanitize(error.GetType().Name)} message={Sanitize(error.Message)}");
        }

        return false;
    }

    // パイプ接続可能まで待機
    private bool WaitForPipeReady(int pid, AppLogger log, int totalWaitMs)
    {
        int intervalMs = Math.Min(200, _hookConnectTimeoutMs);
        if (intervalMs <= 0)
        {
            intervalMs = 1;
        }

        int waited = 0;
        while (waited < totalWaitMs)
        {
            if (TryConnectExisting(pid, log, _hookConnectTimeoutMs))
            {
                return true;
            }

            _platform.Sleep(intervalMs);
            waited += intervalMs;
        }

        return false;
    }

    // 接続生存確認
    private bool TryPing()
    {
        if (!SendCommand("PING", out string response))
        {
            return false;
        }

        return string.Equals(response, "PONG", StringComparison.Ordinal);
    }

    // コマンド送信と応答取得
    private bool SendCommand(string command, out string response)
    {
        response = string.Empty;
        if (!IsConnected() || _connection == null)
        {
            return false;
        }

        try
        {
            if (!_connection.Send(command ?? string.Empty, _hookCommandTimeoutMs, out string line))
            {
                DisposePipe();
                return false;
            }

            response = line ?? string.Empty;
            return true;
        }
        catch
        {
            DisposePipe();
            return false;
        }
    }

    // パイプ接続状態を判定
    private bool IsConnected()
    {
        return _connection != null && _connection.IsConnected;
    }

    // 接続破棄とPIDリセット
    private void DisposePipe()
    {
        DisposeConnection(resetPid: true);
    }

    // 接続を破棄
    private void DisposeConnection(bool resetPid)
    {
        if (_connection != null)
        {
            _connection.Dispose();
            _connection = null;
        }

        if (resetPid)
        {
            _pid = 0;
        }
    }

    // PIDからパイプ名を生成
    private static string GetPipeName(int pid)
    {
        return $"vp_modhook_{pid}";
    }

    // 文字列をlongへ変換
    private static long ParseLong(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return value;
        }

        return 0;
    }

    // ログ出力値を正規化
    private static string Sanitize(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}

internal interface IModifierHookConnection : IDisposable
{
    bool IsConnected { get; }
    bool Send(string command, int timeoutMs, out string response);
}

internal interface IModifierHookPlatform
{
    bool Inject(int pid, string injectionLibraryPath, string pipeName);
    bool TryConnect(string pipeName, int timeoutMs, out IModifierHookConnection connection, out Exception error);
    void Sleep(int milliseconds);
}

internal sealed class DefaultModifierHookPlatform : IModifierHookPlatform
{
    // EasyHookで対象へ注入
    public bool Inject(int pid, string injectionLibraryPath, string pipeName)
    {
        RemoteHooking.Inject(
            pid,
            InjectionOptions.DoNotRequireStrongName,
            injectionLibraryPath,
            injectionLibraryPath,
            pipeName);
        return true;
    }

    // 名前付きパイプへ接続
    public bool TryConnect(string pipeName, int timeoutMs, out IModifierHookConnection connection, out Exception error)
    {
        connection = null;
        error = null;
        NamedPipeClientStream candidate = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
        try
        {
            candidate.Connect(timeoutMs);
            if (!candidate.IsConnected)
            {
                candidate.Dispose();
                return false;
            }

            connection = new NamedPipeModifierHookConnection(candidate);
            return true;
        }
        catch (Exception ex)
        {
            candidate.Dispose();
            error = ex;
            return false;
        }
    }

    // 待機を実行
    public void Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }
}

internal sealed class NamedPipeModifierHookConnection : IModifierHookConnection
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    // パイプ接続ラッパーを初期化
    public NamedPipeModifierHookConnection(NamedPipeClientStream pipe)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _reader = new StreamReader(_pipe, Utf8NoBom, false, 1024, true);
        _writer = new StreamWriter(_pipe, Utf8NoBom, 1024, true) { AutoFlush = true };
    }

    // パイプ接続状態を返す
    public bool IsConnected => _pipe != null && _pipe.IsConnected;

    // 1往復コマンドを送受信
    public bool Send(string command, int timeoutMs, out string response)
    {
        response = string.Empty;
        try
        {
            _writer.WriteLine(command ?? string.Empty);
            var readTask = _reader.ReadLineAsync();
            if (!readTask.Wait(Math.Max(1, timeoutMs)))
            {
                return false;
            }

            response = readTask.Result ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // パイプ関連リソースを解放
    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
        _pipe.Dispose();
    }
}

// 注入先の状態管理
internal sealed class ModifierHookRuntimeState
{
    private volatile bool _enabled;
    private volatile bool _beepSuppressed;
    private volatile bool _statsEnabled;
    private volatile ModifierOverrideMode _overrideMode = ModifierOverrideMode.Neutralize;
    private readonly object _statsGate = new object();
    private long _getKeyStateCalls;
    private long _getAsyncKeyStateCalls;
    private long _getKeyboardStateCalls;
    private long _modifierQueries;
    private long _neutralizedCalls;
    private readonly Dictionary<int, int> _threadCalls = new Dictionary<int, int>();

    // 有効状態を取得
    public bool IsEnabled()
    {
        return _enabled;
    }

    // 有効状態を設定
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _beepSuppressed = enabled;
        if (!enabled)
        {
            _overrideMode = ModifierOverrideMode.Neutralize;
        }
    }

    // 修飾キー上書きモードを設定
    public void SetModifierOverrideMode(ModifierOverrideMode mode)
    {
        _overrideMode = mode;
    }

    // 修飾キー上書きモードを取得
    public ModifierOverrideMode GetModifierOverrideMode()
    {
        return _overrideMode;
    }

    // 警告音抑止状態を取得
    public bool IsBeepSuppressed()
    {
        return _beepSuppressed;
    }

    // 統計収集状態を設定
    public void SetStatsEnabled(bool enabled)
    {
        _statsEnabled = enabled;
    }

    // 統計カウンタを初期化
    public void ResetStats()
    {
        lock (_statsGate)
        {
            _getKeyStateCalls = 0;
            _getAsyncKeyStateCalls = 0;
            _getKeyboardStateCalls = 0;
            _modifierQueries = 0;
            _neutralizedCalls = 0;
            _threadCalls.Clear();
        }
    }

    // API呼び出し統計を記録
    public void RecordCall(ModifierHookApi api, int vKey, int threadId, bool neutralized)
    {
        if (!_statsEnabled)
        {
            return;
        }

        lock (_statsGate)
        {
            switch (api)
            {
                case ModifierHookApi.GetKeyState:
                    _getKeyStateCalls++;
                    break;
                case ModifierHookApi.GetAsyncKeyState:
                    _getAsyncKeyStateCalls++;
                    break;
                case ModifierHookApi.GetKeyboardState:
                    _getKeyboardStateCalls++;
                    break;
            }

            if (IsModifierVKey(vKey))
            {
                _modifierQueries++;
            }

            if (neutralized)
            {
                _neutralizedCalls++;
            }

            if (threadId > 0)
            {
                if (_threadCalls.TryGetValue(threadId, out int count))
                {
                    _threadCalls[threadId] = count + 1;
                }
                else
                {
                    _threadCalls[threadId] = 1;
                }
            }
        }
    }

    // 統計スナップショットを取得
    public ModifierHookStatsSnapshot Snapshot()
    {
        lock (_statsGate)
        {
            return new ModifierHookStatsSnapshot
            {
                GetKeyStateCalls = _getKeyStateCalls,
                GetAsyncKeyStateCalls = _getAsyncKeyStateCalls,
                GetKeyboardStateCalls = _getKeyboardStateCalls,
                ModifierQueries = _modifierQueries,
                NeutralizedCalls = _neutralizedCalls,
                ThreadCallSummary = BuildThreadSummary(_threadCalls)
            };
        }
    }

    // 修飾キー判定
    private static bool IsModifierVKey(int vKey)
    {
        return vKey == ModifierKeyHookEntryPoint.VkShift
               || vKey == ModifierKeyHookEntryPoint.VkControl
               || vKey == ModifierKeyHookEntryPoint.VkMenu
               || vKey == ModifierKeyHookEntryPoint.VkLShift
               || vKey == ModifierKeyHookEntryPoint.VkRShift
               || vKey == ModifierKeyHookEntryPoint.VkLControl
               || vKey == ModifierKeyHookEntryPoint.VkRControl
               || vKey == ModifierKeyHookEntryPoint.VkLMenu
               || vKey == ModifierKeyHookEntryPoint.VkRMenu;
    }

    // スレッド別集計を文字列化
    private static string BuildThreadSummary(Dictionary<int, int> source)
    {
        if (source == null || source.Count == 0)
        {
            return string.Empty;
        }

        var pairs = new List<KeyValuePair<int, int>>(source);
        pairs.Sort((a, b) => b.Value.CompareTo(a.Value));
        var sb = new StringBuilder();
        int take = Math.Min(5, pairs.Count);
        for (int i = 0; i < take; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(pairs[i].Key);
            sb.Append(':');
            sb.Append(pairs[i].Value);
        }

        return sb.ToString();
    }
}

// 注入先の制御パイプ
internal sealed class ModifierHookPipeServer
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string _pipeName;
    private readonly ModifierHookRuntimeState _state;

    // 制御パイプサーバーを初期化
    public ModifierHookPipeServer(string pipeName, ModifierHookRuntimeState state)
    {
        _pipeName = pipeName;
        _state = state;
    }

    // 制御コマンド受信ループ
    public void RunLoop()
    {
        while (true)
        {
            try
            {
                using (var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None))
                {
                    pipe.WaitForConnection();
                    using (var reader = new StreamReader(pipe, Utf8NoBom, false, 1024, true))
                    using (var writer = new StreamWriter(pipe, Utf8NoBom, 1024, true) { AutoFlush = true })
                    while (pipe.IsConnected)
                    {
                        string request = reader.ReadLine();
                        if (request == null)
                        {
                            break;
                        }

                        writer.WriteLine(HandleRequest(request));
                    }
                }
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
    }

    // 受信コマンドを処理
    private string HandleRequest(string request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return "ERR|empty_command";
        }

        string[] parts = request.Split('|');
        string command = parts[0];
        switch (command)
        {
            case "PING":
                return "PONG";
            case "ENABLE":
                _state.SetEnabled(parts.Length >= 2 && parts[1] == "1");
                return "OK";
            case "OVERRIDE":
                if (parts.Length < 2)
                {
                    return "ERR|override_missing_mode";
                }

                if (string.Equals(parts[1], "CTRL", StringComparison.OrdinalIgnoreCase))
                {
                    _state.SetModifierOverrideMode(ModifierOverrideMode.Ctrl);
                    return "OK";
                }

                if (string.Equals(parts[1], "ALT", StringComparison.OrdinalIgnoreCase))
                {
                    _state.SetModifierOverrideMode(ModifierOverrideMode.Alt);
                    return "OK";
                }

                if (string.Equals(parts[1], "SHIFT", StringComparison.OrdinalIgnoreCase))
                {
                    _state.SetModifierOverrideMode(ModifierOverrideMode.Shift);
                    return "OK";
                }

                if (string.Equals(parts[1], "NONE", StringComparison.OrdinalIgnoreCase))
                {
                    _state.SetModifierOverrideMode(ModifierOverrideMode.Neutralize);
                    return "OK";
                }

                return "ERR|override_invalid_mode";
            case "STATS_ENABLE":
                _state.SetStatsEnabled(parts.Length >= 2 && parts[1] == "1");
                return "OK";
            case "STATS_RESET":
                _state.ResetStats();
                return "OK";
            case "STATS_GET":
                ModifierHookStatsSnapshot snapshot = _state.Snapshot();
                return string.Join("|", new[]
                {
                    "STATS",
                    snapshot.GetKeyStateCalls.ToString(CultureInfo.InvariantCulture),
                    snapshot.GetAsyncKeyStateCalls.ToString(CultureInfo.InvariantCulture),
                    snapshot.GetKeyboardStateCalls.ToString(CultureInfo.InvariantCulture),
                    snapshot.ModifierQueries.ToString(CultureInfo.InvariantCulture),
                    snapshot.NeutralizedCalls.ToString(CultureInfo.InvariantCulture),
                    snapshot.ThreadCallSummary ?? string.Empty
                });
            default:
                return "ERR|unknown_command";
        }
    }
}

// 注入先のフック本体
public sealed class ModifierKeyHookEntryPoint : IEntryPoint
{
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;
    internal const int VkLShift = 0xA0;
    internal const int VkRShift = 0xA1;
    internal const int VkLControl = 0xA2;
    internal const int VkRControl = 0xA3;
    internal const int VkLMenu = 0xA4;
    internal const int VkRMenu = 0xA5;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    private delegate short GetKeyStateDelegate(int vKey);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    private delegate short GetAsyncKeyStateDelegate(int vKey);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    private delegate bool GetKeyboardStateDelegate(IntPtr lpKeyState);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    private delegate bool MessageBeepDelegate(uint uType);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    private delegate bool BeepDelegate(uint dwFreq, uint dwDuration);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    private readonly ModifierHookRuntimeState _state = new ModifierHookRuntimeState();
    private readonly ModifierHookPipeServer _pipeServer;
    private readonly GetKeyStateDelegate _getKeyStateOriginal;
    private readonly GetAsyncKeyStateDelegate _getAsyncKeyStateOriginal;
    private readonly GetKeyboardStateDelegate _getKeyboardStateOriginal;
    private readonly MessageBeepDelegate _messageBeepOriginal;
    private readonly BeepDelegate _beepOriginal;
    private readonly LocalHook _getKeyStateHook;
    private readonly LocalHook _getAsyncKeyStateHook;
    private readonly LocalHook _getKeyboardStateHook;
    private readonly LocalHook _messageBeepHook;
    private readonly LocalHook _beepHook;

    // 注入先でフックを初期化
    public ModifierKeyHookEntryPoint(RemoteHooking.IContext context, string pipeName)
    {
        _pipeServer = new ModifierHookPipeServer(pipeName, _state);

        IntPtr getKeyStateAddress = LocalHook.GetProcAddress("user32.dll", "GetKeyState");
        IntPtr getAsyncKeyStateAddress = LocalHook.GetProcAddress("user32.dll", "GetAsyncKeyState");
        IntPtr getKeyboardStateAddress = LocalHook.GetProcAddress("user32.dll", "GetKeyboardState");
        IntPtr messageBeepAddress = LocalHook.GetProcAddress("user32.dll", "MessageBeep");
        IntPtr beepAddress = LocalHook.GetProcAddress("kernel32.dll", "Beep");

        _getKeyStateOriginal = Marshal.GetDelegateForFunctionPointer<GetKeyStateDelegate>(getKeyStateAddress);
        _getAsyncKeyStateOriginal = Marshal.GetDelegateForFunctionPointer<GetAsyncKeyStateDelegate>(getAsyncKeyStateAddress);
        _getKeyboardStateOriginal = Marshal.GetDelegateForFunctionPointer<GetKeyboardStateDelegate>(getKeyboardStateAddress);
        _messageBeepOriginal = Marshal.GetDelegateForFunctionPointer<MessageBeepDelegate>(messageBeepAddress);
        _beepOriginal = Marshal.GetDelegateForFunctionPointer<BeepDelegate>(beepAddress);

        _getKeyStateHook = LocalHook.Create(getKeyStateAddress, new GetKeyStateDelegate(GetKeyStateHooked), this);
        _getAsyncKeyStateHook = LocalHook.Create(getAsyncKeyStateAddress, new GetAsyncKeyStateDelegate(GetAsyncKeyStateHooked), this);
        _getKeyboardStateHook = LocalHook.Create(getKeyboardStateAddress, new GetKeyboardStateDelegate(GetKeyboardStateHooked), this);
        _messageBeepHook = LocalHook.Create(messageBeepAddress, new MessageBeepDelegate(MessageBeepHooked), this);
        _beepHook = LocalHook.Create(beepAddress, new BeepDelegate(BeepHooked), this);

        // 全スレッドへ適用
        // 注入初期化スレッドのみ除外
        _getKeyStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _getAsyncKeyStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _getKeyboardStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _messageBeepHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _beepHook.ThreadACL.SetExclusiveACL(new[] { 0 });
    }

    // 注入先で制御ループを開始
    public void Run(RemoteHooking.IContext context, string pipeName)
    {
        var serverThread = new Thread(_pipeServer.RunLoop)
        {
            IsBackground = true,
            Name = "ModifierHookPipeServer"
        };
        serverThread.Start();

        RemoteHooking.WakeUpProcess();
        while (true)
        {
            Thread.Sleep(50);
        }
    }

    // GetKeyState呼び出しをフック
    private short GetKeyStateHooked(int vKey)
    {
        try
        {
            bool forcedCtrl = ShouldForceCtrl(vKey);
            bool forcedAlt = ShouldForceAlt(vKey);
            bool forcedShift = ShouldForceShift(vKey);
            bool neutralized = !forcedCtrl && !forcedAlt && !forcedShift && ShouldNeutralize(vKey);
            _state.RecordCall(ModifierHookApi.GetKeyState, vKey, GetCurrentThreadId(), neutralized);
            if (forcedCtrl || forcedAlt || forcedShift)
            {
                return unchecked((short)0x8000);
            }

            if (neutralized)
            {
                return 0;
            }

            return _getKeyStateOriginal(vKey);
        }
        catch
        {
            return _getKeyStateOriginal(vKey);
        }
    }

    // GetAsyncKeyState呼び出しをフック
    private short GetAsyncKeyStateHooked(int vKey)
    {
        try
        {
            bool forcedCtrl = ShouldForceCtrl(vKey);
            bool forcedAlt = ShouldForceAlt(vKey);
            bool forcedShift = ShouldForceShift(vKey);
            bool neutralized = !forcedCtrl && !forcedAlt && !forcedShift && ShouldNeutralize(vKey);
            _state.RecordCall(ModifierHookApi.GetAsyncKeyState, vKey, GetCurrentThreadId(), neutralized);
            if (forcedCtrl || forcedAlt || forcedShift)
            {
                return unchecked((short)0x8000);
            }

            if (neutralized)
            {
                return 0;
            }

            return _getAsyncKeyStateOriginal(vKey);
        }
        catch
        {
            return _getAsyncKeyStateOriginal(vKey);
        }
    }

    // GetKeyboardState呼び出しをフック
    private bool GetKeyboardStateHooked(IntPtr lpKeyState)
    {
        try
        {
            bool ok = _getKeyboardStateOriginal(lpKeyState);
            bool neutralized = false;
            if (ok && _state.IsEnabled() && lpKeyState != IntPtr.Zero)
            {
                ApplyKeyboardStateOverride(lpKeyState);
                neutralized = true;
            }

            _state.RecordCall(ModifierHookApi.GetKeyboardState, -1, GetCurrentThreadId(), neutralized);
            return ok;
        }
        catch
        {
            return _getKeyboardStateOriginal(lpKeyState);
        }
    }

    // MessageBeep呼び出しをフック
    private bool MessageBeepHooked(uint uType)
    {
        try
        {
            if (_state.IsEnabled() && _state.IsBeepSuppressed())
            {
                return true;
            }

            return _messageBeepOriginal(uType);
        }
        catch
        {
            return _messageBeepOriginal(uType);
        }
    }

    // Beep呼び出しをフック
    private bool BeepHooked(uint dwFreq, uint dwDuration)
    {
        try
        {
            if (_state.IsEnabled() && _state.IsBeepSuppressed())
            {
                return true;
            }

            return _beepOriginal(dwFreq, dwDuration);
        }
        catch
        {
            return _beepOriginal(dwFreq, dwDuration);
        }
    }

    // 修飾キー中立化対象を判定
    private bool ShouldNeutralize(int vKey)
    {
        if (!_state.IsEnabled())
        {
            return false;
        }

        return vKey == VkShift
               || vKey == VkControl
               || vKey == VkMenu
               || vKey == VkLShift
               || vKey == VkRShift
               || vKey == VkLControl
               || vKey == VkRControl
               || vKey == VkLMenu
               || vKey == VkRMenu;
    }

    // Ctrl上書き対象か判定
    private bool ShouldForceCtrl(int vKey)
    {
        if (!_state.IsEnabled())
        {
            return false;
        }

        if (_state.GetModifierOverrideMode() != ModifierOverrideMode.Ctrl)
        {
            return false;
        }

        return vKey == VkControl || vKey == VkLControl || vKey == VkRControl;
    }

    // Alt上書き対象か判定
    private bool ShouldForceAlt(int vKey)
    {
        if (!_state.IsEnabled())
        {
            return false;
        }

        if (_state.GetModifierOverrideMode() != ModifierOverrideMode.Alt)
        {
            return false;
        }

        return vKey == VkMenu || vKey == VkLMenu || vKey == VkRMenu;
    }

    // Shift上書き対象か判定
    private bool ShouldForceShift(int vKey)
    {
        if (!_state.IsEnabled())
        {
            return false;
        }

        if (_state.GetModifierOverrideMode() != ModifierOverrideMode.Shift)
        {
            return false;
        }

        return vKey == VkShift || vKey == VkLShift || vKey == VkRShift;
    }

    // キーボード状態配列へ上書きを適用
    private void ApplyKeyboardStateOverride(IntPtr lpKeyState)
    {
        WriteKeyboardState(lpKeyState, VkShift, 0);
        WriteKeyboardState(lpKeyState, VkLShift, 0);
        WriteKeyboardState(lpKeyState, VkRShift, 0);

        ModifierOverrideMode mode = _state.GetModifierOverrideMode();
        if (mode == ModifierOverrideMode.Ctrl)
        {
            WriteKeyboardState(lpKeyState, VkControl, 0x80);
            WriteKeyboardState(lpKeyState, VkLControl, 0x80);
            WriteKeyboardState(lpKeyState, VkRControl, 0x80);
            WriteKeyboardState(lpKeyState, VkShift, 0);
            WriteKeyboardState(lpKeyState, VkLShift, 0);
            WriteKeyboardState(lpKeyState, VkRShift, 0);
            WriteKeyboardState(lpKeyState, VkMenu, 0);
            WriteKeyboardState(lpKeyState, VkLMenu, 0);
            WriteKeyboardState(lpKeyState, VkRMenu, 0);
            return;
        }

        if (mode == ModifierOverrideMode.Alt)
        {
            WriteKeyboardState(lpKeyState, VkControl, 0);
            WriteKeyboardState(lpKeyState, VkLControl, 0);
            WriteKeyboardState(lpKeyState, VkRControl, 0);
            WriteKeyboardState(lpKeyState, VkShift, 0);
            WriteKeyboardState(lpKeyState, VkLShift, 0);
            WriteKeyboardState(lpKeyState, VkRShift, 0);
            WriteKeyboardState(lpKeyState, VkMenu, 0x80);
            WriteKeyboardState(lpKeyState, VkLMenu, 0x80);
            WriteKeyboardState(lpKeyState, VkRMenu, 0x80);
            return;
        }

        if (mode == ModifierOverrideMode.Shift)
        {
            WriteKeyboardState(lpKeyState, VkControl, 0);
            WriteKeyboardState(lpKeyState, VkLControl, 0);
            WriteKeyboardState(lpKeyState, VkRControl, 0);
            WriteKeyboardState(lpKeyState, VkShift, 0x80);
            WriteKeyboardState(lpKeyState, VkLShift, 0x80);
            WriteKeyboardState(lpKeyState, VkRShift, 0x80);
            WriteKeyboardState(lpKeyState, VkMenu, 0);
            WriteKeyboardState(lpKeyState, VkLMenu, 0);
            WriteKeyboardState(lpKeyState, VkRMenu, 0);
            return;
        }

        WriteKeyboardState(lpKeyState, VkControl, 0);
        WriteKeyboardState(lpKeyState, VkLControl, 0);
        WriteKeyboardState(lpKeyState, VkRControl, 0);
        WriteKeyboardState(lpKeyState, VkMenu, 0);
        WriteKeyboardState(lpKeyState, VkLMenu, 0);
        WriteKeyboardState(lpKeyState, VkRMenu, 0);
    }

    // キーボード状態配列へ値を書き込み
    private static void WriteKeyboardState(IntPtr lpKeyState, int index, byte value)
    {
        Marshal.WriteByte(lpKeyState, index, value);
    }
}
