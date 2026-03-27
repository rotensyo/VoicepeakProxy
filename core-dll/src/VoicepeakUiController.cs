using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace VoicepeakProxyCore;

// VOICEPEAKのUI操作を担当
internal sealed class VoicepeakUiController : IVoicepeakUiController
{
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint WmKillFocus = 0x0008;
    private const uint WmSetFocus = 0x0007;
    private const uint WmActivate = 0x0006;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const int MkLButton = 0x0001;
    private readonly UiConfig _ui;
    private readonly PrepareConfig _prepare;
    private readonly DebugConfig _debug;
    private readonly Dictionary<char, List<SentenceBreakTrigger>> _sentenceBreakTriggerIndex;
    private readonly AppLogger _log;
    private readonly IVoicepeakProcessApi _processApi;
    private readonly ModifierKeyHookController _modifierKeyHookController;
    private readonly object _inputPrimeGate = new object();
    private readonly object _modifierIsolationSessionGate = new object();
    // 単一操作経路前提のためロックは設けない
    private int _cachedVoicepeakPid;
    private int _primedProcessId;
    private IntPtr _primedMainHwnd;
    private int _lastInjectedEnterCount;
    private int _modifierIsolationSessionDepth;
    private uint _modifierIsolationSessionProcessId;

    // UI設定とロガーを保持
    public VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log)
        : this(ui, new PrepareConfig(), debug, log, new DefaultVoicepeakProcessApi())
    {
    }

    public VoicepeakUiController(UiConfig ui, PrepareConfig prepare, DebugConfig debug, AppLogger log)
        : this(ui, prepare, debug, log, new DefaultVoicepeakProcessApi())
    {
    }

    internal VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log, IVoicepeakProcessApi processApi)
        : this(ui, new PrepareConfig(), debug, log, processApi)
    {
    }

    internal VoicepeakUiController(UiConfig ui, PrepareConfig prepare, DebugConfig debug, AppLogger log, IVoicepeakProcessApi processApi)
    {
        _ui = ui;
        _prepare = prepare ?? new PrepareConfig();
        _debug = debug ?? new DebugConfig();
        _log = log;
        _processApi = processApi ?? new DefaultVoicepeakProcessApi();
        _modifierKeyHookController = new ModifierKeyHookController(
            _prepare.HookCommandTimeoutMs,
            _prepare.HookConnectTimeoutMs,
            _prepare.HookConnectTotalWaitMs);
        _sentenceBreakTriggerIndex = BuildSentenceBreakTriggerIndex(ui);
        WarnIfMoveToStartShortcutWillUseSequentialFallback(_ui.MoveToStartShortcut);
    }

    // 対象プロセスとメインウィンドウを解決
    public bool TryResolveTarget(out Process process, out IntPtr mainHwnd)
    {
        process = null;
        mainHwnd = IntPtr.Zero;

        if (!TryResolveVoicepeakPid(out int pid))
        {
            return false;
        }

        return TryResolveTargetByPid(pid, out process, out mainHwnd);
    }

    public bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd)
    {
        process = null;
        mainHwnd = IntPtr.Zero;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            process = _processApi.GetProcessById(pid);
        }
        catch
        {
            return false;
        }

        try
        {
            if (process == null || process.HasExited)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        mainHwnd = _processApi.WaitMainWindowHandle(process, 3000);
        if (mainHwnd == IntPtr.Zero)
        {
            return false;
        }

        _cachedVoicepeakPid = process.Id;
        return true;
    }

    public int GetVoicepeakProcessCount()
    {
        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        return matches.Length;
    }

    // PIDキャッシュを確認し必要時に更新
    private bool TryResolveVoicepeakPid(out int pid)
    {
        pid = 0;

        int cachedPid = _cachedVoicepeakPid;
        if (cachedPid > 0)
        {
            if (IsValidVoicepeakProcess(cachedPid, out Process cachedProcess))
            {
                pid = cachedProcess.Id;
                return true;
            }

            _cachedVoicepeakPid = 0;
        }

        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        if (matches == null || matches.Length != 1)
        {
            return false;
        }

        Process process = matches[0];
        if (process == null)
        {
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                return false;
            }

            pid = process.Id;
            _cachedVoicepeakPid = pid;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // キャッシュpidの妥当性を確認
    private bool IsValidVoicepeakProcess(int pid, out Process process)
    {
        process = null;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            process = _processApi.GetProcessById(pid);
            if (process == null || process.HasExited)
            {
                return false;
            }

            Process[] matches = _processApi.GetProcessesByName("voicepeak");
            if (matches == null || matches.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < matches.Length; i++)
            {
                Process candidate = matches[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.Id == pid)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool IsAlive(Process process)
    {
        return process != null && !process.HasExited;
    }

    public bool TryPrimeInputContext(Process process, IntPtr mainHwnd, InputContextPrimeReason reason)
    {
        if (process == null || mainHwnd == IntPtr.Zero)
        {
            return false;
        }

        lock (_inputPrimeGate)
        {
            if (reason != InputContextPrimeReason.StartTimeoutRetry && IsInputContextPrimed(process, mainHwnd))
            {
                return true;
            }

            if (!TryPrimeInputContextWithForeground(process, mainHwnd))
            {
                return false;
            }

            MarkInputContextPrimed(process, mainHwnd);
            return true;
        }
    }

    public bool PrepareForTextInput(Process process, IntPtr mainHwnd, int actionDelayMs, bool allowCompositePrimeBeforeTextFocusWhenUnprimed)
    {
        if (allowCompositePrimeBeforeTextFocusWhenUnprimed
            && ShouldAttemptPrimeInputContext(process, mainHwnd, InputContextPrimeReason.BeforeTextFocusWhenUnprimed))
        {
            TryPrimeInputContext(process, mainHwnd, InputContextPrimeReason.BeforeTextFocusWhenUnprimed);
        }

        return MoveToStart(mainHwnd, actionDelayMs);
    }

    public bool PrepareForPlayback(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        return MoveToStart(mainHwnd, actionDelayMs);
    }

    // 入力欄をクリア
    public bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs, bool allowCompositePrimeBeforeTextFocusWhenUnprimed)
    {
        int clearInputMaxPasses = Math.Max(1, _prepare.ClearInputMaxPasses);
        bool compositeMoveToStart = UsesSequentialMoveToStartFallback(_ui.MoveToStartShortcut);
        if (compositeMoveToStart)
        {
            for (int pass = 0; pass < clearInputMaxPasses; pass++)
            {
                ClearInputState before = ReadClearInputState(mainHwnd);
                if (IsClearCompleted(before.Read, before.VisibleBlockCount))
                {
                    return true;
                }

                int pairCount = Math.Max(1, before.VisibleBlockCount + 1);
                int deleteSteps = ComputeCompositeDeleteSteps(before.Read, before.VisibleBlockCount);
                if (!ExecuteWithModifierIsolation(mainHwnd, "composite_clear_cycle", action: () =>
                {
                    return RunCompositeClearCycleCore(mainHwnd, pairCount, deleteSteps, actionDelayMs);
                }))
                {
                    return false;
                }

                ClearInputState after = ReadClearInputState(mainHwnd);
                if (IsClearCompleted(after.Read, after.VisibleBlockCount))
                {
                    return true;
                }
            }

            return LogIncompleteClearInputAndReturnResult(ReadClearInputState(mainHwnd));
        }

        for (int pass = 0; pass < clearInputMaxPasses; pass++)
        {
            ClearInputState before = ReadClearInputState(mainHwnd);
            if (IsClearCompleted(before.Read, before.VisibleBlockCount))
            {
                return true;
            }

            int clearSteps = ComputeNonCompositeDeleteSteps(before.Read, before.VisibleBlockCount);
            if (!ExecuteWithModifierIsolation(mainHwnd, "clear_input_delete_loop", action: () =>
            {
                for (int i = 0; i < clearSteps; i++)
                {
                    if (!PressDeleteCore(mainHwnd))
                    {
                        return false;
                    }
                }

                return true;
            }))
            {
                return false;
            }

            ClearInputState after = ReadClearInputState(mainHwnd);
            if (IsClearCompleted(after.Read, after.VisibleBlockCount))
            {
                return true;
            }

            if (!PrepareForTextInput(process, mainHwnd, actionDelayMs, allowCompositePrimeBeforeTextFocusWhenUnprimed))
            {
                return false;
            }
        }

        return LogIncompleteClearInputAndReturnResult(ReadClearInputState(mainHwnd));
    }

    // 完全削除判定を共通化
    internal static bool IsClearCompleted(ReadInputResult read, int visibleBlockCount)
    {
        return read.Success && read.TotalLength == 0 && visibleBlockCount == 1;
    }

    internal static bool IsCompositeClearCompleted(ReadInputResult read, int visibleBlockCount)
    {
        return IsClearCompleted(read, visibleBlockCount);
    }

    private bool LogIncompleteClearInputAndReturnResult(ClearInputState state)
    {
        _log.Warn("clear_input_incomplete " +
            $"success={state.Read.Success} totalLength={state.Read.TotalLength} source={state.Read.Source} visibleBlockCount={state.VisibleBlockCount}");
        return IsClearCompleted(state.Read, state.VisibleBlockCount);
    }

    private ClearInputState ReadClearInputState(IntPtr mainHwnd)
    {
        ReadInputResult read = ReadInputTextDetailed(mainHwnd);
        int visibleBlockCount = EstimateVisibleBlockCount(mainHwnd);
        return new ClearInputState(read, visibleBlockCount);
    }

    internal static int ComputeCompositeDeleteSteps(ReadInputResult read, int visibleBlockCount)
    {
        int baseLength = read.Success ? Math.Max(0, read.TotalLength) : 0;
        int inputBoxCount = Math.Max(0, visibleBlockCount);
        return baseLength + inputBoxCount + 10;
    }

    internal static int ComputeNonCompositeDeleteSteps(ReadInputResult read, int visibleBlockCount)
    {
        int baseLength = read.Success ? Math.Max(0, read.TotalLength) : 0;
        int inputBoxCount = Math.Max(0, visibleBlockCount);
        return Math.Max(10, baseLength + 10 + inputBoxCount);
    }

    private bool RunCompositeClearCycleCore(IntPtr mainHwnd, int pairCount, int deleteSteps, int actionDelayMs)
    {
        if (!FocusInputForKeyboardIfNeeded(mainHwnd, actionDelayMs))
        {
            return false;
        }

        int actualPairs = Math.Max(1, pairCount);
        for (int i = 0; i < actualPairs; i++)
        {
            if (!SendKey(mainHwnd, VirtualKey.PageUp))
            {
                return false;
            }

            if (_prepare.SequentialMoveToStartKeyDelayBaseMs > 0)
            {
                Thread.Sleep(_prepare.SequentialMoveToStartKeyDelayBaseMs);
            }

            if (!SendKey(mainHwnd, VirtualKey.Up))
            {
                return false;
            }

            if (_prepare.SequentialMoveToStartKeyDelayBaseMs > 0)
            {
                Thread.Sleep(_prepare.SequentialMoveToStartKeyDelayBaseMs);
            }
        }

        int actualDeletes = Math.Max(0, deleteSteps);
        for (int i = 0; i < actualDeletes; i++)
        {
            if (!PressDeleteCore(mainHwnd))
            {
                return false;
            }
        }

        return true;
    }

    private static int EstimateVisibleBlockCount(IntPtr mainHwnd)
    {
        if (mainHwnd == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            AutomationElement root = AutomationElement.FromHandle(mainHwnd);
            List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount: 200);
            return candidates.Count;
        }
        catch
        {
            return 0;
        }
    }

    // 文字列を1文字ずつ送信
    public bool TypeText(IntPtr mainHwnd, string text, int charDelayMs)
    {
        string send = text ?? string.Empty;
        HashSet<int> enterPositions = ComputeSentenceBreakEnterPositions(send);
        if (mainHwnd == IntPtr.Zero)
        {
            _log.Warn("type_text_target_resolve_failed reason=main_hwnd_zero");
            return false;
        }

        bool modifierIsolationEnabled = false;
        bool statsProbeEnabled = _debug.LogModifierHookStats;
        bool statsProbeStarted = false;
        try
        {
            modifierIsolationEnabled = TryEnableModifierKeyIsolation(mainHwnd, "type_text");
            if (!modifierIsolationEnabled)
            {
                _log.Warn("modifier_guard_unavailable op=type_text");
                return false;
            }

            if (statsProbeEnabled && modifierIsolationEnabled)
            {
                _modifierKeyHookController.BeginStatsProbe(_log);
                statsProbeStarted = true;
            }

            if (!TryTypeTextByWindowMessages(mainHwnd, send, enterPositions, charDelayMs, out int messageEnterCount))
            {
                _log.Warn($"type_text_target_send_failed reason=wm_char_failed hwnd=0x{mainHwnd.ToInt64():X}");
                return false;
            }

            _lastInjectedEnterCount = messageEnterCount;
            _log.Info($"type_text_route_selected route=wm_char_only enterCount={messageEnterCount}");
            return true;
        }
        finally
        {
            if (statsProbeEnabled && statsProbeStarted)
            {
                _modifierKeyHookController.EndStatsProbe(_log);
                LogModifierHookStatsIfEnabled();
            }

            if (modifierIsolationEnabled)
            {
                DisableModifierKeyIsolation("type_text");
            }
        }
    }

    // 修飾キー中立化フックを有効化
    private bool TryEnableModifierKeyIsolation(IntPtr targetHwnd, string operationName)
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

    // 修飾キー中立化フックを無効化
    private void DisableModifierKeyIsolation(string operationName)
    {
        if (IsAnyModifierIsolationSessionActive())
        {
            return;
        }

        _modifierKeyHookController.SetEnabled(false, _log);
        _log.Info($"modifier_isolation_disabled op={SanitizeForLog(operationName)}");
    }

    // 修飾キー中立化セッション開始
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

        lock (_modifierIsolationSessionGate)
        {
            if (_modifierIsolationSessionDepth > 0)
            {
                if (_modifierIsolationSessionProcessId != (uint)voicepeakProcessId)
                {
                    _log.Warn($"modifier_session_begin_failed reason=process_mismatch op={SanitizeForLog(operationName)}");
                    return false;
                }

                _modifierIsolationSessionDepth++;
                _log.Info($"modifier_session_reused op={SanitizeForLog(operationName)} depth={_modifierIsolationSessionDepth}");
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

            _modifierIsolationSessionProcessId = (uint)voicepeakProcessId;
            _modifierIsolationSessionDepth = 1;
            _log.Info($"modifier_session_begin op={SanitizeForLog(operationName)} pid={voicepeakProcessId}");
            return true;
        }
    }

    // 修飾キー中立化セッション終了
    public void EndModifierIsolationSession(string operationName)
    {
        lock (_modifierIsolationSessionGate)
        {
            if (_modifierIsolationSessionDepth <= 0)
            {
                return;
            }

            _modifierIsolationSessionDepth--;
            if (_modifierIsolationSessionDepth > 0)
            {
                _log.Info($"modifier_session_keep op={SanitizeForLog(operationName)} depth={_modifierIsolationSessionDepth}");
                return;
            }

            _modifierIsolationSessionProcessId = 0;
            _modifierKeyHookController.SetEnabled(false, _log);
            _log.Info($"modifier_session_end op={SanitizeForLog(operationName)}");
        }
    }

    // 対象pidでセッション中か判定
    private bool IsModifierIsolationSessionActive(uint processId)
    {
        lock (_modifierIsolationSessionGate)
        {
            return _modifierIsolationSessionDepth > 0 && _modifierIsolationSessionProcessId == processId;
        }
    }

    // いずれかのセッションが有効か判定
    private bool IsAnyModifierIsolationSessionActive()
    {
        lock (_modifierIsolationSessionGate)
        {
            return _modifierIsolationSessionDepth > 0;
        }
    }

    // pidがvoicepeakかを確認
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

    // 指定操作を修飾キー中立化フックで保護
    private bool ExecuteWithModifierIsolation(IntPtr targetHwnd, string operationName, Func<bool> action)
    {
        bool modifierIsolationEnabled = false;
        try
        {
            modifierIsolationEnabled = TryEnableModifierKeyIsolation(targetHwnd, operationName);
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

    // フック統計をデバッグ時のみ出力
    private void LogModifierHookStatsIfEnabled()
    {
        if (!_debug.LogModifierHookStats)
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

    // WM_CHARとEnter送信で入力
    private bool TryTypeTextByWindowMessages(IntPtr targetHwnd, string send, HashSet<int> enterPositions, int charDelayMs, out int injectedEnterCount)
    {
        injectedEnterCount = 0;
        for (int i = 0; i < send.Length; i++)
        {
            bool isLastCharInSegment = i == send.Length - 1;
            if (!SendChar(targetHwnd, send[i]))
            {
                return false;
            }

            if (charDelayMs > 0)
            {
                Thread.Sleep(charDelayMs);
            }

            if (!isLastCharInSegment && enterPositions.Contains(i))
            {
                if (!SendKey(targetHwnd, VirtualKey.Return))
                {
                    return false;
                }

                injectedEnterCount++;
                if (charDelayMs > 0)
                {
                    Thread.Sleep(charDelayMs);
                }
            }
        }
        return true;
    }

    // 再生ショートカットを送信
    public bool PressPlay(IntPtr mainHwnd)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "press_play", action: () =>
        {
            if (!KillFocusCore(mainHwnd))
            {
                return false;
            }

            if (_ui.DelayBeforePlayShortcutMs > 0)
            {
                Thread.Sleep(_ui.DelayBeforePlayShortcutMs);
            }

            return SendShortcut(mainHwnd, _ui.PlayShortcut);
        });
    }

    public bool MoveToStart(IntPtr mainHwnd, int actionDelayMs)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "move_to_start", action: () =>
        {
            SleepActionDelay(actionDelayMs);
            if (IsFunctionKeyMoveToStartShortcut(_ui.MoveToStartShortcut))
            {
                return SendShortcut(mainHwnd, _ui.MoveToStartShortcut);
            }

            return SendCompositeMoveToStart(mainHwnd, actionDelayMs);
        });
    }

    public bool PressDelete(IntPtr mainHwnd)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "press_delete", action: () =>
            PressDeleteCore(mainHwnd));
    }

    // Deleteキー送信の共通実体
    private bool PressDeleteCore(IntPtr mainHwnd)
    {
        bool sent = SendKey(mainHwnd, VirtualKey.Delete);
        if (!sent)
        {
            return false;
        }

        if (_prepare.DeleteKeyDelayBaseMs > 0)
        {
            Thread.Sleep(_prepare.DeleteKeyDelayBaseMs);
        }

        return true;
    }

    public bool KillFocus(IntPtr mainHwnd)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "kill_focus", action: () =>
            KillFocusCore(mainHwnd));
    }

    // KillFocus送信の共通実体
    private bool KillFocusCore(IntPtr mainHwnd)
    {
        return SendWindowMessage(mainHwnd, WmKillFocus, IntPtr.Zero, IntPtr.Zero);
    }

    internal static bool IsValidShortcut(string raw)
    {
        return ShortcutSpec.TryParse(raw, out _);
    }

    // 再生ショートカットは修飾なしキーのみ許可
    internal static bool IsValidPlayShortcut(string raw)
    {
        if (!ShortcutSpec.TryParse(raw, out ShortcutSpec spec))
        {
            return false;
        }

        return !spec.Control && !spec.Shift && !spec.Alt;
    }

    internal static bool IsValidMoveToStartShortcut(string raw)
    {
        return !string.IsNullOrWhiteSpace(raw);
    }

    internal static bool IsFunctionKeyMoveToStartShortcut(string raw)
    {
        return ShortcutSpec.TryParse(raw, out ShortcutSpec spec) && spec.IsFunctionKeyOnly;
    }

    private static bool UsesSequentialMoveToStartFallback(string raw)
    {
        return !IsFunctionKeyMoveToStartShortcut(raw);
    }

    public ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(mainHwnd);
            List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount: 200);
            if (_debug.LogTextCandidates)
            {
                LogTextCandidates(candidates);
            }

            AutomationElement bestInput = FindBestInputBox(candidates);
            if (bestInput == null)
            {
                return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
            }

            string primary = TryGetElementTextOrWindowTextSafe(bestInput);
            string normalizedPrimary = NormalizeForPanelCompare(primary);
            int totalLength = EstimateTotalInputTextLength(root, bestInput);
            if (totalLength < normalizedPrimary.Length)
            {
                totalLength = normalizedPrimary.Length;
            }

            return ReadInputResult.Ok(normalizedPrimary, totalLength, ReadInputSource.PrimaryUiA);
        }
        catch
        {
            return ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);
        }
    }

    private bool SendShortcut(IntPtr hwnd, string shortcut)
    {
        if (!ShortcutSpec.TryParse(shortcut, out ShortcutSpec spec))
        {
            return false;
        }

        if (spec.Control || spec.Shift || spec.Alt)
        {
            return false;
        }

        return SendKeyDown(hwnd, spec.Key) && SendKeyUp(hwnd, spec.Key);
    }

    private bool SendKey(IntPtr hwnd, VirtualKey key)
    {
        return SendKeyDown(hwnd, key) && SendKeyUp(hwnd, key);
    }

    private bool SendKeyDown(IntPtr hwnd, VirtualKey key)
    {
        return SendWindowMessage(hwnd, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
    }

    private bool SendKeyUp(IntPtr hwnd, VirtualKey key)
    {
        return SendWindowMessage(hwnd, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
    }

    private bool SendChar(IntPtr hwnd, char c)
    {
        return SendWindowMessage(hwnd, WmChar, (IntPtr)c, IntPtr.Zero);
    }

    private static bool SendWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr result;
        IntPtr ok = SendMessageTimeout(hWnd, msg, wParam, lParam, SmtoAbortIfHung, 1000, out result);
        return ok != IntPtr.Zero;
    }

    private bool FocusInputForKeyboardIfNeeded(IntPtr mainHwnd, int actionDelayMs)
    {
        if (!UsesSequentialMoveToStartFallback(_ui.MoveToStartShortcut))
        {
            return true;
        }

        IntPtr voicePeakHwnd = FindWindow(null, "VOICEPEAK");
        if (voicePeakHwnd == IntPtr.Zero)
        {
            voicePeakHwnd = mainHwnd;
        }

        if (voicePeakHwnd == IntPtr.Zero)
        {
            return false;
        }

        bool sent = false;
        IntPtr juceHwnd = FindWindow("JUCEWindow", null);
        if (juceHwnd != IntPtr.Zero)
        {
            sent |= SendWindowMessage(juceHwnd, WmActivate, new IntPtr(2), IntPtr.Zero);
        }
        else
        {
            sent |= SendWindowMessage(voicePeakHwnd, WmActivate, new IntPtr(2), IntPtr.Zero);
        }

        sent |= SendWindowMessage(voicePeakHwnd, WmKillFocus, IntPtr.Zero, IntPtr.Zero);
        SleepFocusTransitionDelay(actionDelayMs);
        sent |= SendWindowMessage(voicePeakHwnd, WmSetFocus, IntPtr.Zero, IntPtr.Zero);
        SleepFocusTransitionDelay(actionDelayMs);
        return sent;
    }

    private bool PrimeKeyboardInputForComposite(IntPtr mainHwnd, int actionDelayMs)
    {
        return FocusInputForKeyboardIfNeeded(mainHwnd, actionDelayMs);
    }

    private bool TryPrimeInputContextWithForeground(Process process, IntPtr mainHwnd)
    {
        if (!TryGetBestInputBox(mainHwnd, out AutomationElement inputBox))
        {
            _log.Warn("input_context_prime_failed reason=no_input_box");
            return false;
        }

        IntPtr previousForeground = GetForegroundWindow();
        bool foregroundChanged = false;
        try
        {
            if (previousForeground != mainHwnd)
            {
                foregroundChanged = TrySetForegroundWindow(mainHwnd);
                if (!foregroundChanged)
                {
                    _log.Warn("input_context_prime_failed reason=set_foreground_failed");
                    return false;
                }
            }

            if (!TryClickElementByWindowMessages(mainHwnd, inputBox))
            {
                _log.Warn("input_context_prime_failed reason=click_input_box_failed");
                return false;
            }

            Thread.Sleep(30);
            _log.Info($"input_context_primed pid={process.Id}");
            return true;
        }
        finally
        {
            if (foregroundChanged && previousForeground != IntPtr.Zero && previousForeground != mainHwnd)
            {
                TrySetForegroundWindow(previousForeground);
            }
        }
    }

    public bool ShouldAttemptPrimeInputContext(Process process, IntPtr mainHwnd, InputContextPrimeReason reason)
    {
        if (!UsesSequentialMoveToStartFallback(_ui.MoveToStartShortcut))
        {
            return false;
        }

        switch (reason)
        {
            case InputContextPrimeReason.Validation:
                return _ui.ClickAtValidationEnabled && !IsInputContextPrimed(process, mainHwnd);
            case InputContextPrimeReason.BeforeTextFocusWhenUnprimed:
                return _ui.ClickBeforeTextFocusWhenUninitializedEnabled && !IsInputContextPrimed(process, mainHwnd);
            case InputContextPrimeReason.StartTimeoutRetry:
                return _ui.ClickOnStartTimeoutRetryEnabled;
            default:
                return false;
        }
    }

    private bool IsInputContextPrimed(Process process, IntPtr mainHwnd)
    {
        return process != null && mainHwnd != IntPtr.Zero && _primedProcessId == process.Id && _primedMainHwnd == mainHwnd;
    }

    private void MarkInputContextPrimed(Process process, IntPtr mainHwnd)
    {
        _primedProcessId = process.Id;
        _primedMainHwnd = mainHwnd;
    }

    private static bool TryGetBestInputBox(IntPtr mainHwnd, out AutomationElement inputBox)
    {
        inputBox = null;
        if (mainHwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            AutomationElement root = AutomationElement.FromHandle(mainHwnd);
            List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount: 200);
            inputBox = FindBestInputBox(candidates);
            return inputBox != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr currentForeground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(hWnd, out _);
        uint foregroundThread = currentForeground != IntPtr.Zero ? GetWindowThreadProcessId(currentForeground, out _) : 0;

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(foregroundThread, currentThread, true);
        }

        if (targetThread != 0 && targetThread != currentThread)
        {
            AttachThreadInput(targetThread, currentThread, true);
        }

        bool ok = SetForegroundWindow(hWnd);
        if (!ok)
        {
            SendAltTap();
            Thread.Sleep(20);
            ok = SetForegroundWindow(hWnd);
        }

        if (targetThread != 0 && targetThread != currentThread)
        {
            AttachThreadInput(targetThread, currentThread, false);
        }

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(foregroundThread, currentThread, false);
        }

        return ok || GetForegroundWindow() == hWnd;
    }

    private static void SendAltTap()
    {
        keybd_event((byte)VirtualKey.Menu, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VirtualKey.Menu, 0, 0x0002, UIntPtr.Zero);
    }

    private static bool TryClickElementByWindowMessages(IntPtr mainHwnd, AutomationElement element)
    {
        if (mainHwnd == IntPtr.Zero || element == null)
        {
            return false;
        }

        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
        {
            return false;
        }

        // クリック位置、入力欄のヘッダを避けるため中央下寄りをクリックする
        int screenX = (int)(rect.Left + (rect.Width / 2.0));
        int screenY = (int)(rect.Top + (rect.Height / 3.0 * 2.0));
        POINT point = new POINT { X = screenX, Y = screenY };
        if (!ScreenToClient(mainHwnd, ref point))
        {
            return false;
        }

        IntPtr lParam = MakeLParam(point.X, point.Y);
        bool sent = true;
        sent &= SendWindowMessage(mainHwnd, WmMouseMove, IntPtr.Zero, lParam);
        sent &= SendWindowMessage(mainHwnd, WmLButtonDown, new IntPtr(MkLButton), lParam);
        sent &= SendWindowMessage(mainHwnd, WmLButtonUp, IntPtr.Zero, lParam);
        return sent;
    }

    private bool SendCompositeMoveToStart(IntPtr hwnd, int actionDelayMs)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!PrimeKeyboardInputForComposite(hwnd, actionDelayMs))
        {
            return false;
        }

        int pairCount = Math.Max(1, _lastInjectedEnterCount + 1);
        return SendCompositePageUpUpPairs(hwnd, pairCount);
    }

    private void WarnIfMoveToStartShortcutWillUseSequentialFallback(string shortcut)
    {
        if (IsFunctionKeyMoveToStartShortcut(shortcut))
        {
            return;
        }

        if (ShortcutSpec.TryParse(shortcut, out _) || ShortcutSpec.TryParseCompositeMoveToStart(shortcut, out _))
        {
            return;
        }

        _log.Warn($"move_to_start_shortcut_unrecognized_fallback_to_pageup value={SanitizeForLog(shortcut)}");
    }

    private bool SendCompositePageUpUpPairs(IntPtr mainHwnd, int pairCount)
    {
        int actualPairs = Math.Max(1, pairCount);
        for (int i = 0; i < actualPairs; i++)
        {
            if (!SendKey(mainHwnd, VirtualKey.PageUp))
            {
                return false;
            }

            if (_prepare.SequentialMoveToStartKeyDelayBaseMs > 0)
            {
                Thread.Sleep(_prepare.SequentialMoveToStartKeyDelayBaseMs);
            }

            if (!SendKey(mainHwnd, VirtualKey.Up))
            {
                return false;
            }

            if (_prepare.SequentialMoveToStartKeyDelayBaseMs > 0)
            {
                Thread.Sleep(_prepare.SequentialMoveToStartKeyDelayBaseMs);
            }
        }

        return true;
    }

    private static IntPtr MakeLParam(int low, int high)
    {
        int combined = (high << 16) | (low & 0xFFFF);
        return new IntPtr(combined);
    }

    private static AutomationElement FindBestInputBox(List<TextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        AutomationElement best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            TextCandidateInfo candidate = candidates[i];
            if (candidate.Score > bestScore)
            {
                bestScore = candidate.Score;
                best = candidate.Element;
            }
        }

        return best;
    }

    private static int EstimateTotalInputTextLength(AutomationElement mainWindow, AutomationElement fallbackInputBox)
    {
        if (mainWindow != null)
        {
            List<AutomationElement> candidates = CollectTextLikeElements(mainWindow, maxCount: 200);
            int sum = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                AutomationElement e = candidates[i];
                string t = NormalizeForLength(TryGetElementTextOrWindowTextSafe(e));
                if (t.Length == 0)
                {
                    continue;
                }

                sum += t.Length;
            }

            if (sum > 0)
            {
                return sum;
            }
        }

        string fallback = NormalizeForLength(TryGetElementTextOrWindowTextSafe(fallbackInputBox));
        return fallback.Length;
    }

    private static List<AutomationElement> CollectTextLikeElements(AutomationElement root, int maxCount)
    {
        List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount);
        List<AutomationElement> result = new List<AutomationElement>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            result.Add(candidates[i].Element);
        }

        return result;
    }

    private static List<TextCandidateInfo> CollectTextCandidates(AutomationElement root, int maxCount)
    {
        List<TextCandidateInfo> result = new List<TextCandidateInfo>();
        if (root == null || maxCount <= 0)
        {
            return result;
        }

        Queue<AutomationElement> queue = new Queue<AutomationElement>();
        queue.Enqueue(root);

        while (queue.Count > 0 && result.Count < maxCount)
        {
            AutomationElement e = queue.Dequeue();
            if (TryBuildTextCandidate(e, out TextCandidateInfo candidate))
            {
                result.Add(candidate);
            }

            AutomationElementCollection children = e.FindAll(TreeScope.Children, Condition.TrueCondition);
            for (int i = 0; i < children.Count; i++)
            {
                queue.Enqueue(children[i]);
            }
        }

        return result;
    }

    private static bool TryBuildTextCandidate(AutomationElement element, out TextCandidateInfo candidate)
    {
        candidate = default(TextCandidateInfo);
        if (element == null)
        {
            return false;
        }

        ControlType controlType = element.Current.ControlType;
        string name = element.Current.Name;
        if (!IsCollectTextCandidateTarget(controlType, name))
        {
            return false;
        }

        var rect = element.Current.BoundingRectangle;
        double score = rect.Width * rect.Height;
        if (controlType == ControlType.Edit)
        {
            score += 10000;
        }

        candidate = new TextCandidateInfo(element, score);
        return true;
    }

    internal static bool IsCollectTextCandidateTarget(ControlType controlType, string name)
    {
        bool allowedControlType = controlType == ControlType.Edit
                                  || controlType == ControlType.Document
                                  || controlType == ControlType.Text;
        return allowedControlType && name != null && name.Length == 0;
    }

    private void LogTextCandidates(List<TextCandidateInfo> candidates)
    {
        int count = candidates != null ? candidates.Count : 0;
        _log.Debug($"text_candidates_begin count={count}");
        if (candidates == null)
        {
            _log.Debug("text_candidates_end");
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            TextCandidateInfo c = candidates[i];
            AutomationElement e = c.Element;
            var r = e.Current.BoundingRectangle;
            bool hasTextPattern = e.TryGetCurrentPattern(TextPattern.Pattern, out _);
            bool hasValuePattern = e.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            string text = TryGetElementTextOrWindowTextSafe(e);
            _log.Debug(
                "text_candidate " +
                $"index={i} " +
                $"controlType={e.Current.ControlType.ProgrammaticName} " +
                $"name=\"{SanitizeForLog(e.Current.Name)}\" " +
                $"automationId=\"{SanitizeForLog(e.Current.AutomationId)}\" " +
                $"className=\"{SanitizeForLog(e.Current.ClassName)}\" " +
                $"hasTextPattern={hasTextPattern} " +
                $"hasValuePattern={hasValuePattern} " +
                $"rect=({r.Left:F1},{r.Top:F1},{r.Width:F1},{r.Height:F1}) " +
                $"score={c.Score:F1} " +
                $"text=\"{SanitizeForLog(text)}\"");
        }

        _log.Debug("text_candidates_end");
    }

    private static string NormalizeForPanelCompare(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static string NormalizeForLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private static string GetElementText(AutomationElement element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
        {
            return ((ValuePattern)valuePatternObj).Current.Value ?? string.Empty;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj))
        {
            return (((TextPattern)textPatternObj).DocumentRange.GetText(-1) ?? string.Empty).TrimEnd('\r', '\n');
        }

        return string.Empty;
    }

    private static string TryGetElementTextOrWindowTextSafe(AutomationElement element)
    {
        try
        {
            return GetElementTextOrWindowText(element);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetElementTextOrWindowText(AutomationElement element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        string text = GetElementText(element);
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        int nativeHandle = element.Current.NativeWindowHandle;
        if (nativeHandle == 0)
        {
            return string.Empty;
        }

        IntPtr hWnd = new IntPtr(nativeHandle);
        int length = (int)SendMessageTimeout(hWnd, WmGetTextLength, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 500, out _);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(length + 1);
        SendMessageTimeout(hWnd, WmGetText, new IntPtr(sb.Capacity), sb, SmtoAbortIfHung, 500, out _);
        return sb.ToString();
    }

    private static string SanitizeForLog(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }

    private static IntPtr WaitMainWindowHandle(Process process, int timeoutMs)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (process.HasExited)
            {
                return IntPtr.Zero;
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }

    private static void SleepActionDelay(int actionDelayMs)
    {
        if (actionDelayMs > 0)
        {
            Thread.Sleep(actionDelayMs);
        }
    }

    private static void SleepFocusTransitionDelay(int actionDelayMs)
    {
        if (actionDelayMs > 0)
        {
            Thread.Sleep(actionDelayMs);
        }
    }

    private static Dictionary<char, List<SentenceBreakTrigger>> BuildSentenceBreakTriggerIndex(UiConfig ui)
    {
        var index = new Dictionary<char, List<SentenceBreakTrigger>>();
        if (ui?.SentenceBreakTriggers == null)
        {
            return index;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        int order = 0;

        for (int i = 0; i < ui.SentenceBreakTriggers.Count; i++)
        {
            string token = ui.SentenceBreakTriggers[i];
            if (!string.IsNullOrEmpty(token))
            {
                if (used.Add(token))
                {
                    char key = token[0];
                    if (!index.TryGetValue(key, out List<SentenceBreakTrigger> bucket))
                    {
                        bucket = new List<SentenceBreakTrigger>();
                        index[key] = bucket;
                    }

                    bucket.Add(new SentenceBreakTrigger(token, order));
                    order++;
                }
            }
        }

        foreach (List<SentenceBreakTrigger> bucket in index.Values)
        {
            // 最長一致優先
            // 同長は設定順優先
            bucket.Sort((a, b) =>
            {
                int len = b.Text.Length.CompareTo(a.Text.Length);
                if (len != 0)
                {
                    return len;
                }

                return a.Order.CompareTo(b.Order);
            });
        }

        return index;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        StringBuilder lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private enum VirtualKey
    {
        Back = 0x08,
        Return = 0x0D,
        Delete = 0x2E,
        Space = 0x20,
        PageUp = 0x21,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        Home = 0x24,
        End = 0x23,
        Up = 0x26,
        Control = 0x11,
        Shift = 0x10,
        Menu = 0x12
    }

    // 改行挿入位置を事前計算
    private HashSet<int> ComputeSentenceBreakEnterPositions(string text)
    {
        HashSet<int> positions = new HashSet<int>();
        if (!_ui.SendEnterAfterSentenceBreak || string.IsNullOrEmpty(text))
        {
            return positions;
        }

        int index = 0;
        while (index < text.Length)
        {
            SentenceBreakTrigger firstMatched = FindLongestSentenceBreakTrigger(text, index);
            if (firstMatched == null)
            {
                index++;
                continue;
            }

            int runEnd = index + firstMatched.Text.Length;
            while (runEnd < text.Length)
            {
                SentenceBreakTrigger nextMatched = FindLongestSentenceBreakTrigger(text, runEnd);
                if (nextMatched == null)
                {
                    break;
                }

                runEnd += nextMatched.Text.Length;
            }

            positions.Add(runEnd - 1);
            index = runEnd;
        }

        return positions;
    }

    private SentenceBreakTrigger FindLongestSentenceBreakTrigger(string text, int index)
    {
        if (!_sentenceBreakTriggerIndex.TryGetValue(text[index], out List<SentenceBreakTrigger> candidates))
        {
            return null;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            SentenceBreakTrigger candidate = candidates[i];
            if (MatchesTrigger(text, index, candidate.Text))
            {
                return candidate;
            }
        }

        return null;
    }

    // 指定位置でトークン一致を確認
    private static bool MatchesTrigger(string text, int startIndex, string trigger)
    {
        if (string.IsNullOrEmpty(trigger))
        {
            return false;
        }

        if (startIndex < 0 || startIndex + trigger.Length > text.Length)
        {
            return false;
        }

        return string.Compare(text, startIndex, trigger, 0, trigger.Length, StringComparison.Ordinal) == 0;
    }

    // 改行トリガー情報を保持
    private sealed class SentenceBreakTrigger
    {
        public SentenceBreakTrigger(string text, int order)
        {
            Text = text;
            Order = order;
        }

        public string Text { get; }
        public int Order { get; }
    }

    private sealed class ShortcutSpec
    {
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public VirtualKey Key { get; set; }
        public bool IsCtrlUpOnly => Control && !Shift && !Alt && Key == VirtualKey.Up;
        public bool IsFunctionKeyOnly => !Control && !Shift && !Alt && Key >= VirtualKey.F1 && Key <= VirtualKey.F12;

        public static bool TryParse(string raw, out ShortcutSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] parts = raw.Split('+');
            ShortcutSpec temp = new ShortcutSpec();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Control = true;
                    continue;
                }

                if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Shift = true;
                    continue;
                }

                if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Alt = true;
                    continue;
                }

                if (!TryParseKey(p, out VirtualKey key))
                {
                    return false;
                }

                temp.Key = key;
            }

            if (temp.Key == 0)
            {
                return false;
            }

            spec = temp;
            return true;
        }

        public static bool TryParseCompositeMoveToStart(string raw, out ShortcutSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] parts = raw.Split('+');
            ShortcutSpec temp = new ShortcutSpec();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Control = true;
                    continue;
                }

                if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Shift = true;
                    continue;
                }

                if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Alt = true;
                    continue;
                }

                if (!p.Equals("Up", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                temp.Key = VirtualKey.Up;
            }

            if (!temp.IsCtrlUpOnly)
            {
                return false;
            }

            spec = temp;
            return true;
        }

        private static bool TryParseKey(string text, out VirtualKey key)
        {
            key = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (text.Equals("Space", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.Space;
                return true;
            }

            if (text.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.Home;
                return true;
            }

            if (text.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.End;
                return true;
            }

            if (text.Length <= 3 && text.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(text.Substring(1), out int fn) && fn >= 1 && fn <= 12)
                {
                    key = (VirtualKey)((int)VirtualKey.F1 + (fn - 1));
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct TextCandidateInfo
    {
        public AutomationElement Element { get; }
        public double Score { get; }

        public TextCandidateInfo(AutomationElement element, double score)
        {
            Element = element;
            Score = score;
        }
    }

    private readonly struct ClearInputState
    {
        public ReadInputResult Read { get; }
        public int VisibleBlockCount { get; }

        public ClearInputState(ReadInputResult read, int visibleBlockCount)
        {
            Read = read;
            VisibleBlockCount = visibleBlockCount;
        }
    }

    private sealed class DefaultVoicepeakProcessApi : IVoicepeakProcessApi
    {
        public Process[] GetProcessesByName(string processName) => Process.GetProcessesByName(processName);

        public Process GetProcessById(int pid) => Process.GetProcessById(pid);

        public IntPtr WaitMainWindowHandle(Process process, int timeoutMs) => VoicepeakUiController.WaitMainWindowHandle(process, timeoutMs);
    }
}
