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

    public bool EnsureInjected(int pid, AppLogger log)
    {
        lock (_gate)
        {
            if (_pid == pid && IsConnected() && TryPing())
            {
                log.Info($"modifier_hook_reused pid={pid}");
                return true;
            }

            DisposePipe();

            if (TryConnectExisting(pid, log, _hookConnectTimeoutMs))
            {
                _pid = pid;
                log.Info($"modifier_hook_reused pid={pid}");
                return true;
            }

            bool injected = TryInject(pid, log);
            if (!injected && TryConnectExisting(pid, log, _hookConnectTimeoutMs))
            {
                _pid = pid;
                log.Info($"modifier_hook_reused pid={pid}");
                return true;
            }

            if (!WaitForPipeReady(pid, log, _hookConnectTotalWaitMs))
            {
                log.Warn($"modifier_hook_connect_failed pid={pid} reason=pipe_not_ready");
                return false;
            }

            _pid = pid;
            log.Info(injected ? $"modifier_hook_injected pid={pid}" : $"modifier_hook_reused pid={pid}");
            return true;
        }
    }

    public bool SetEnabled(bool enabled, AppLogger log)
    {
        lock (_gate)
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
    }

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

    private bool TryPing()
    {
        if (!SendCommand("PING", out string response))
        {
            return false;
        }

        return string.Equals(response, "PONG", StringComparison.Ordinal);
    }

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

    private bool IsConnected()
    {
        return _connection != null && _connection.IsConnected;
    }

    private void DisposePipe()
    {
        if (_connection != null)
        {
            _connection.Dispose();
            _connection = null;
        }

        _pid = 0;
    }

    private static string GetPipeName(int pid)
    {
        return $"vp_modhook_{pid}";
    }

    private static long ParseLong(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return value;
        }

        return 0;
    }

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

    public NamedPipeModifierHookConnection(NamedPipeClientStream pipe)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _reader = new StreamReader(_pipe, Utf8NoBom, false, 1024, true);
        _writer = new StreamWriter(_pipe, Utf8NoBom, 1024, true) { AutoFlush = true };
    }

    public bool IsConnected => _pipe != null && _pipe.IsConnected;

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
    private volatile bool _statsEnabled;
    private readonly object _statsGate = new object();
    private long _getKeyStateCalls;
    private long _getAsyncKeyStateCalls;
    private long _getKeyboardStateCalls;
    private long _modifierQueries;
    private long _neutralizedCalls;
    private readonly Dictionary<int, int> _threadCalls = new Dictionary<int, int>();

    public bool IsEnabled()
    {
        return _enabled;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void SetStatsEnabled(bool enabled)
    {
        _statsEnabled = enabled;
    }

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

    public ModifierHookPipeServer(string pipeName, ModifierHookRuntimeState state)
    {
        _pipeName = pipeName;
        _state = state;
    }

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

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    private readonly ModifierHookRuntimeState _state = new ModifierHookRuntimeState();
    private readonly ModifierHookPipeServer _pipeServer;
    private readonly GetKeyStateDelegate _getKeyStateOriginal;
    private readonly GetAsyncKeyStateDelegate _getAsyncKeyStateOriginal;
    private readonly GetKeyboardStateDelegate _getKeyboardStateOriginal;
    private readonly LocalHook _getKeyStateHook;
    private readonly LocalHook _getAsyncKeyStateHook;
    private readonly LocalHook _getKeyboardStateHook;

    public ModifierKeyHookEntryPoint(RemoteHooking.IContext context, string pipeName)
    {
        _pipeServer = new ModifierHookPipeServer(pipeName, _state);

        IntPtr getKeyStateAddress = LocalHook.GetProcAddress("user32.dll", "GetKeyState");
        IntPtr getAsyncKeyStateAddress = LocalHook.GetProcAddress("user32.dll", "GetAsyncKeyState");
        IntPtr getKeyboardStateAddress = LocalHook.GetProcAddress("user32.dll", "GetKeyboardState");

        _getKeyStateOriginal = Marshal.GetDelegateForFunctionPointer<GetKeyStateDelegate>(getKeyStateAddress);
        _getAsyncKeyStateOriginal = Marshal.GetDelegateForFunctionPointer<GetAsyncKeyStateDelegate>(getAsyncKeyStateAddress);
        _getKeyboardStateOriginal = Marshal.GetDelegateForFunctionPointer<GetKeyboardStateDelegate>(getKeyboardStateAddress);

        _getKeyStateHook = LocalHook.Create(getKeyStateAddress, new GetKeyStateDelegate(GetKeyStateHooked), this);
        _getAsyncKeyStateHook = LocalHook.Create(getAsyncKeyStateAddress, new GetAsyncKeyStateDelegate(GetAsyncKeyStateHooked), this);
        _getKeyboardStateHook = LocalHook.Create(getKeyboardStateAddress, new GetKeyboardStateDelegate(GetKeyboardStateHooked), this);

        // 全スレッドへ適用
        // 注入初期化スレッドのみ除外
        _getKeyStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _getAsyncKeyStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
        _getKeyboardStateHook.ThreadACL.SetExclusiveACL(new[] { 0 });
    }

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

    private short GetKeyStateHooked(int vKey)
    {
        try
        {
            bool neutralized = ShouldNeutralize(vKey);
            _state.RecordCall(ModifierHookApi.GetKeyState, vKey, GetCurrentThreadId(), neutralized);
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

    private short GetAsyncKeyStateHooked(int vKey)
    {
        try
        {
            bool neutralized = ShouldNeutralize(vKey);
            _state.RecordCall(ModifierHookApi.GetAsyncKeyState, vKey, GetCurrentThreadId(), neutralized);
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

    private bool GetKeyboardStateHooked(IntPtr lpKeyState)
    {
        try
        {
            bool ok = _getKeyboardStateOriginal(lpKeyState);
            bool neutralized = false;
            if (ok && _state.IsEnabled() && lpKeyState != IntPtr.Zero)
            {
                NeutralizeKeyboardState(lpKeyState);
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

    private static void NeutralizeKeyboardState(IntPtr lpKeyState)
    {
        WriteKeyboardState(lpKeyState, VkShift, 0);
        WriteKeyboardState(lpKeyState, VkControl, 0);
        WriteKeyboardState(lpKeyState, VkMenu, 0);
        WriteKeyboardState(lpKeyState, VkLShift, 0);
        WriteKeyboardState(lpKeyState, VkRShift, 0);
        WriteKeyboardState(lpKeyState, VkLControl, 0);
        WriteKeyboardState(lpKeyState, VkRControl, 0);
        WriteKeyboardState(lpKeyState, VkLMenu, 0);
        WriteKeyboardState(lpKeyState, VkRMenu, 0);
    }

    private static void WriteKeyboardState(IntPtr lpKeyState, int index, byte value)
    {
        Marshal.WriteByte(lpKeyState, index, value);
    }
}
