using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoicepeakProxyCore;

// 修飾キー中立化を管理
internal sealed class ModifierIsolationCoordinator
{
    private readonly ModifierKeyHookController _modifierKeyHookController;
    private readonly AppLogger _log;
    private readonly object _sessionGate = new object();
    private bool _sessionActive;
    private uint _sessionProcessId;

    // 依存を保持
    public ModifierIsolationCoordinator(ModifierKeyHookController modifierKeyHookController, AppLogger log)
    {
        _modifierKeyHookController = modifierKeyHookController;
        _log = log;
    }

    // セッション状態を返す
    public bool SessionActive
    {
        get
        {
            lock (_sessionGate)
            {
                return _sessionActive;
            }
        }
    }

    // セッション対象pidを返す
    public uint SessionProcessId
    {
        get
        {
            lock (_sessionGate)
            {
                return _sessionProcessId;
            }
        }
    }

    // 互換用にセッション状態を上書き
    public void SetSessionStateForCompatibility(bool active, uint processId)
    {
        lock (_sessionGate)
        {
            _sessionActive = active;
            _sessionProcessId = processId;
        }
    }

    // 統計計測を開始
    public void BeginStatsProbe()
    {
        _modifierKeyHookController.BeginStatsProbe(_log);
    }

    // 統計計測を終了して出力
    public void EndStatsProbeAndLog(bool logStats)
    {
        _modifierKeyHookController.EndStatsProbe(_log);
        LogModifierHookStatsIfEnabled(logStats);
    }

    // 統計を必要時に出力
    public void LogStatsIfEnabled(bool logStats)
    {
        LogModifierHookStatsIfEnabled(logStats);
    }

    // 一時中立化を有効化
    public bool TryEnableModifierKeyIsolation(IntPtr targetHwnd, string operationName)
    {
        return TryEnableModifierKeyIsolationCore(targetHwnd, operationName);
    }

    // 一時中立化を無効化
    public void DisableModifierKeyIsolation(string operationName)
    {
        DisableModifierKeyIsolationCore(operationName);
    }

    // 操作を修飾キー中立化で保護
    public bool ExecuteWithModifierIsolation(IntPtr targetHwnd, string operationName, Func<bool> action)
    {
        bool modifierIsolationEnabled = false;
        try
        {
            modifierIsolationEnabled = TryEnableModifierKeyIsolationCore(targetHwnd, operationName);
            if (!modifierIsolationEnabled)
            {
                _log.Warn($"modifier_guard_unavailable op={SanitizeForLog(operationName)}");
                return false;
            }

            return action();
        }
        finally
        {
            if (modifierIsolationEnabled)
            {
                DisableModifierKeyIsolation(operationName);
            }
        }
    }

    // 修飾キー上書きを設定
    public bool SetModifierOverride(IntPtr targetHwnd, string operationName, ModifierOverrideMode mode)
    {
        return TrySetModifierOverride(targetHwnd, operationName, mode);
    }

    // 修飾キー中立化セッションを開始
    public bool BeginModifierIsolationSession(int voicepeakProcessId, string operationName)
    {
        if (voicepeakProcessId <= 0)
        {
            _log.Warn($"modifier_session_begin_failed reason=target_pid_zero op={SanitizeForLog(operationName)}");
            return false;
        }

        if (!IsVoicepeakProcessId((uint)voicepeakProcessId))
        {
            _log.Warn($"modifier_session_begin_failed reason=target_process_invalid op={SanitizeForLog(operationName)}");
            return false;
        }

        lock (_sessionGate)
        {
            if (_sessionActive)
            {
                if (_sessionProcessId != (uint)voicepeakProcessId)
                {
                    _log.Warn($"modifier_session_begin_failed reason=process_mismatch op={SanitizeForLog(operationName)}");
                    return false;
                }

                _log.Info($"modifier_session_reused op={SanitizeForLog(operationName)}");
                return true;
            }

            if (!_modifierKeyHookController.EnsureInjected(voicepeakProcessId, _log))
            {
                _log.Warn($"modifier_session_begin_failed reason=ensure_injected_failed op={SanitizeForLog(operationName)}");
                return false;
            }

            if (!_modifierKeyHookController.SetEnabled(true, _log))
            {
                _log.Warn($"modifier_session_begin_failed reason=set_enabled_failed op={SanitizeForLog(operationName)}");
                return false;
            }

            _sessionProcessId = (uint)voicepeakProcessId;
            _sessionActive = true;
            _log.Info($"modifier_session_begin op={SanitizeForLog(operationName)} pid={voicepeakProcessId}");
            return true;
        }
    }

    // 修飾キー中立化セッションを終了
    public bool EndModifierIsolationSession(string operationName)
    {
        lock (_sessionGate)
        {
            if (!_sessionActive)
            {
                return true;
            }

            if (!_modifierKeyHookController.SetEnabled(false, _log))
            {
                _log.Warn($"modifier_session_end_failed reason=set_enabled_failed op={SanitizeForLog(operationName)}");
                _sessionProcessId = 0;
                _sessionActive = false;
                return false;
            }

            _sessionProcessId = 0;
            _sessionActive = false;
            _log.Info($"modifier_session_end op={SanitizeForLog(operationName)}");
            return true;
        }
    }

    // 対象pidでセッション有効か判定
    public bool IsModifierIsolationSessionActive(uint processId)
    {
        lock (_sessionGate)
        {
            return _sessionActive && _sessionProcessId == processId;
        }
    }

    // セッション有効か判定
    public bool IsAnyModifierIsolationSessionActive()
    {
        lock (_sessionGate)
        {
            return _sessionActive;
        }
    }

    // 修飾キー中立化を有効化
    private bool TryEnableModifierKeyIsolationCore(IntPtr targetHwnd, string operationName)
    {
        if (targetHwnd == IntPtr.Zero)
        {
            _log.Warn($"modifier_hook_enable_failed reason=target_hwnd_zero op={SanitizeForLog(operationName)}");
            return false;
        }

        GetWindowThreadProcessId(targetHwnd, out uint processId);
        if (processId == 0)
        {
            _log.Warn($"modifier_hook_enable_failed reason=target_pid_zero op={SanitizeForLog(operationName)}");
            return false;
        }

        if (!IsVoicepeakProcessId(processId))
        {
            return true;
        }

        if (IsModifierIsolationSessionActive(processId))
        {
            return true;
        }

        if (!_modifierKeyHookController.EnsureInjected((int)processId, _log))
        {
            return false;
        }

        if (!_modifierKeyHookController.SetEnabled(true, _log))
        {
            return false;
        }

        _log.Info($"modifier_isolation_enabled op={SanitizeForLog(operationName)} pid={processId}");
        return true;
    }

    // 修飾キー中立化を無効化
    private void DisableModifierKeyIsolationCore(string operationName)
    {
        if (IsAnyModifierIsolationSessionActive())
        {
            return;
        }

        if (!_modifierKeyHookController.SetEnabled(false, _log))
        {
            _log.Warn($"modifier_isolation_disable_failed op={SanitizeForLog(operationName)}");
            return;
        }

        _log.Info($"modifier_isolation_disabled op={SanitizeForLog(operationName)}");
    }

    // フック統計をデバッグ時のみ出力
    private void LogModifierHookStatsIfEnabled(bool logStats)
    {
        if (!logStats)
        {
            return;
        }

        ModifierHookStatsSnapshot snapshot = _modifierKeyHookController.GetStatsSnapshot(_log);
        if (snapshot == null)
        {
            _log.Debug("modifier_hook_stats unavailable=true");
            return;
        }

        _log.Debug(
            "modifier_hook_stats " +
            $"get_key_state={snapshot.GetKeyStateCalls} " +
            $"get_async_key_state={snapshot.GetAsyncKeyStateCalls} " +
            $"get_keyboard_state={snapshot.GetKeyboardStateCalls} " +
            $"modifier_queries={snapshot.ModifierQueries} " +
            $"neutralized={snapshot.NeutralizedCalls} " +
            $"thread_calls={snapshot.ThreadCallSummary}");
    }

    // pidがvoicepeakか判定
    private static bool IsVoicepeakProcessId(uint processId)
    {
        if (processId == 0)
        {
            return false;
        }

        try
        {
            Process process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "voicepeak", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // 修飾キー上書きモードを設定
    private bool TrySetModifierOverride(IntPtr targetHwnd, string operationName, ModifierOverrideMode mode)
    {
        if (targetHwnd == IntPtr.Zero)
        {
            _log.Warn($"modifier_hook_override_failed reason=target_hwnd_zero op={SanitizeForLog(operationName)}");
            return false;
        }

        GetWindowThreadProcessId(targetHwnd, out uint processId);
        if (processId == 0)
        {
            _log.Warn($"modifier_hook_override_failed reason=target_pid_zero op={SanitizeForLog(operationName)}");
            return false;
        }

        if (!IsVoicepeakProcessId(processId))
        {
            return true;
        }

        if (!_modifierKeyHookController.EnsureInjected((int)processId, _log))
        {
            _log.Warn($"modifier_hook_override_failed reason=ensure_injected_failed op={SanitizeForLog(operationName)} mode={mode}");
            return false;
        }

        if (!_modifierKeyHookController.SetModifierOverride(mode, _log))
        {
            _log.Warn($"modifier_hook_override_failed reason=set_override_failed op={SanitizeForLog(operationName)} mode={mode}");
            return false;
        }

        return true;
    }

    // ログ用に文字列を正規化
    private static string SanitizeForLog(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
