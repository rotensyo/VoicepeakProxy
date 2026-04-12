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
    private readonly UiConfig _ui;
    private readonly InputTimingConfig _inputTiming;
    private readonly HookConfig _hook;
    private readonly TextConfig _text;
    private readonly DebugConfig _debug;
    private readonly AppLogger _log;
    private readonly VoicepeakTargetResolver _targetResolver;
    private readonly ModifierIsolationCoordinator _modifierIsolationCoordinator;
    // 単一操作経路前提のためロックは設けない
    private int _cachedVoicepeakPid;  // テスト互換のため保持する解決キャッシュ
    private bool _modifierIsolationSessionActive;  // テスト互換のため保持するセッション状態
    private uint _modifierIsolationSessionProcessId;  // テスト互換のため保持する対象pid

    // UI設定とロガーを保持
    public VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log)
        : this(ui, new InputTimingConfig(), new HookConfig(), new TextConfig(), debug, log, new DefaultVoicepeakProcessApi())
    {
    }

    public VoicepeakUiController(UiConfig ui, InputTimingConfig inputTiming, HookConfig hook, TextConfig text, DebugConfig debug, AppLogger log)
        : this(ui, inputTiming, hook, text, debug, log, new DefaultVoicepeakProcessApi())
    {
    }

    internal VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log, IVoicepeakProcessApi processApi)
        : this(ui, new InputTimingConfig(), new HookConfig(), new TextConfig(), debug, log, processApi)
    {
    }

    internal VoicepeakUiController(UiConfig ui, InputTimingConfig inputTiming, HookConfig hook, TextConfig text, DebugConfig debug, AppLogger log, IVoicepeakProcessApi processApi)
    {
        IVoicepeakProcessApi resolvedProcessApi = processApi ?? new DefaultVoicepeakProcessApi();
        _ui = ui ?? new UiConfig();
        _inputTiming = inputTiming ?? new InputTimingConfig();
        _hook = hook ?? new HookConfig();
        _text = text ?? new TextConfig();
        _debug = debug ?? new DebugConfig();
        _log = log;
        _targetResolver = new VoicepeakTargetResolver(resolvedProcessApi);
        ModifierKeyHookController modifierKeyHookController = new ModifierKeyHookController(
            _hook.HookCommandTimeoutMs,
            _hook.HookConnectTimeoutMs,
            _hook.HookConnectTotalWaitMs);
        _modifierIsolationCoordinator = new ModifierIsolationCoordinator(modifierKeyHookController, _log);
    }

    // 対象プロセスとメインウィンドウを解決
    public bool TryResolveTarget(out Process process, out IntPtr mainHwnd)
    {
        ResolveTargetResult resolved = TryResolveTargetDetailed();
        process = resolved.Process;
        mainHwnd = resolved.MainHwnd;
        return resolved.Success;
    }

    // 対象解決と失敗理由を同時に返す
    public ResolveTargetResult TryResolveTargetDetailed()
    {
        _targetResolver.CachedVoicepeakPid = _cachedVoicepeakPid;
        ResolveTargetResult result = _targetResolver.TryResolveTargetDetailed();
        _cachedVoicepeakPid = _targetResolver.CachedVoicepeakPid;
        return result;
    }

    // 対象プロセスとウィンドウをpidから取得
    public bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd)
    {
        _targetResolver.CachedVoicepeakPid = _cachedVoicepeakPid;
        bool resolved = _targetResolver.TryResolveTargetByPid(pid, out process, out mainHwnd);
        _cachedVoicepeakPid = _targetResolver.CachedVoicepeakPid;
        return resolved;
    }

    public int GetVoicepeakProcessCount()
    {
        return _targetResolver.GetVoicepeakProcessCount();
    }

    public bool IsAlive(Process process)
    {
        return process != null && !process.HasExited;
    }

    public bool PrepareForTextInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        return MoveToStart(mainHwnd, actionDelayMs);
    }

    public bool PrepareForPlayback(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        return MoveToStart(mainHwnd, actionDelayMs);
    }

    // 入力欄をクリア
    public bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "clear_input", action: () =>
        {
            if (!ShortcutParser.TryParseClearInputSelectAllKey(_ui.ClearInputSelectAllKey, out VirtualKey selectAllKey))
            {
                return false;
            }

            if (!ShortcutParser.TryParseClearInputSelectAllModifier(_ui.ClearInputSelectAllModifier, out ModifierOverrideMode selectAllMode, out bool useSelectAllModifier))
            {
                return false;
            }

            SleepActionDelay(actionDelayMs);
            if (!SendMoveToStartShortcut(mainHwnd))
            {
                return false;
            }

            SleepKeyStrokeInterval();

            int clearInputMaxPasses = Math.Max(1, _inputTiming.ClearInputMaxPasses);
            for (int pass = 0; pass < clearInputMaxPasses; pass++)
            {
                ClearInputState before = ReadClearInputState(mainHwnd);
                if (IsClearCompleted(before.Read, before.VisibleBlockCount))
                {
                    return true;
                }

                int visibleBlockCount = Math.Max(1, before.VisibleBlockCount);
                if (!RunSelectAllDeleteCycle(mainHwnd, selectAllKey, selectAllMode, useSelectAllModifier, visibleBlockCount))
                {
                    return false;
                }
            }

            return LogIncompleteClearInputAndReturnResult(ReadClearInputState(mainHwnd));
        });
    }

    // 全選択後にDeleteを二回送信するサイクル
    private bool RunSelectAllDeleteCycle(IntPtr mainHwnd, VirtualKey selectAllKey, ModifierOverrideMode selectAllMode, bool useSelectAllModifier, int visibleBlockCount)
    {
        int cycleCount = Math.Max(1, visibleBlockCount);
        for (int i = 0; i < cycleCount; i++)
        {
            if (!SendShortcutWithOptionalModifier(mainHwnd, "clear_input_select_all", selectAllKey, selectAllMode, useSelectAllModifier, "clear_input_select_all_override_reset_failed"))
            {
                return false;
            }

            SleepKeyStrokeInterval();

            if (!PressDeleteCore(mainHwnd))
            {
                return false;
            }

            if (!PressDeleteCore(mainHwnd))
            {
                return false;
            }
        }

        return true;
    }

    // 可視入力欄数を返す
    public int GetVisibleInputBlockCount(IntPtr mainHwnd)
    {
        return EstimateVisibleBlockCount(mainHwnd);
    }

    // 完全削除判定を共通化
    internal static bool IsClearCompleted(ReadInputResult read, int visibleBlockCount)
    {
        return read.Success && read.TotalLength == 0 && visibleBlockCount == 1;
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

    // 可視状態の入力ブロック数を取得
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
                _modifierIsolationCoordinator.BeginStatsProbe();
                statsProbeStarted = true;
            }

            if (!TryTypeTextByWindowMessages(mainHwnd, send, charDelayMs))
            {
                _log.Warn($"type_text_target_send_failed reason=wm_char_failed hwnd=0x{mainHwnd.ToInt64():X}");
                return false;
            }

            _log.Info("type_text_route_selected route=wm_char_only");
            return true;
        }
        finally
        {
            if (statsProbeEnabled && statsProbeStarted)
            {
                _modifierIsolationCoordinator.EndStatsProbeAndLog(statsProbeEnabled);
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
        ApplyLegacyModifierIsolationSessionState();
        bool enabled = _modifierIsolationCoordinator.TryEnableModifierKeyIsolation(targetHwnd, operationName);
        SyncLegacyModifierIsolationSessionState();
        return enabled;
    }

    // 修飾キー中立化フックを無効化
    private void DisableModifierKeyIsolation(string operationName)
    {
        ApplyLegacyModifierIsolationSessionState();
        _modifierIsolationCoordinator.DisableModifierKeyIsolation(operationName);
        SyncLegacyModifierIsolationSessionState();
    }

    // 修飾キー中立化セッション開始
    public bool BeginModifierIsolationSession(int voicepeakProcessId, string operationName)
    {
        ApplyLegacyModifierIsolationSessionState();
        bool ok = _modifierIsolationCoordinator.BeginModifierIsolationSession(voicepeakProcessId, operationName);
        SyncLegacyModifierIsolationSessionState();
        return ok;
    }

    // 修飾キー中立化セッション終了
    public bool EndModifierIsolationSession(string operationName)
    {
        ApplyLegacyModifierIsolationSessionState();
        bool ok = _modifierIsolationCoordinator.EndModifierIsolationSession(operationName);
        SyncLegacyModifierIsolationSessionState();
        return ok;
    }

    // 対象pidでセッション中か判定
    private bool IsModifierIsolationSessionActive(uint processId)
    {
        return _modifierIsolationCoordinator.IsModifierIsolationSessionActive(processId);
    }

    // いずれかのセッションが有効か判定
    private bool IsAnyModifierIsolationSessionActive()
    {
        return _modifierIsolationCoordinator.IsAnyModifierIsolationSessionActive();
    }

    // 指定操作を修飾キー中立化フックで保護
    private bool ExecuteWithModifierIsolation(IntPtr targetHwnd, string operationName, Func<bool> action)
    {
        return _modifierIsolationCoordinator.ExecuteWithModifierIsolation(targetHwnd, operationName, action);
    }

    // フック統計をデバッグ時のみ出力
    private void LogModifierHookStatsIfEnabled()
    {
        _modifierIsolationCoordinator.LogStatsIfEnabled(_debug.LogModifierHookStats);
    }

    // 互換フィールドをcoordinatorへ反映
    private void ApplyLegacyModifierIsolationSessionState()
    {
        _modifierIsolationCoordinator.SetSessionStateForCompatibility(
            _modifierIsolationSessionActive,
            _modifierIsolationSessionProcessId);
    }

    // coordinator状態を互換フィールドへ反映
    private void SyncLegacyModifierIsolationSessionState()
    {
        _modifierIsolationSessionActive = _modifierIsolationCoordinator.SessionActive;
        _modifierIsolationSessionProcessId = _modifierIsolationCoordinator.SessionProcessId;
    }

    // WM_CHARと改行キー送信で入力
    private bool TryTypeTextByWindowMessages(IntPtr targetHwnd, string send, int charDelayMs)
    {
        for (int i = 0; i < send.Length; i++)
        {
            char current = send[i];
            bool sent = current == '\n' || current == '\r'
                ? SendNewline(targetHwnd)
                : SendChar(targetHwnd, current);
            if (!sent)
            {
                return false;
            }

            if (charDelayMs > 0)
            {
                Thread.Sleep(charDelayMs);
            }
        }
        return true;
    }

    // 改行送信方式を設定に応じて切替
    private bool SendNewline(IntPtr targetHwnd)
    {
        if (_text.SplitInputBlockOnNewline)
        {
            return SendKey(targetHwnd, VirtualKey.Return);
        }

        return SendShortcutWithOptionalModifier(
            targetHwnd,
            "type_text_newline_enter",
            VirtualKey.Return,
            ModifierOverrideMode.Shift,
            useModifier: true,
            "type_text_newline_override_reset_failed");
    }

    // 再生ショートカットを送信
    public bool PressPlay(IntPtr mainHwnd)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "press_play", action: () =>
        {
            if (!ShortcutParser.TryParsePlayShortcutKey(_ui.PlayShortcutKey, out VirtualKey key))
            {
                return false;
            }

            if (!ShortcutParser.TryParsePlayShortcutModifier(_ui.PlayShortcutModifier, out ModifierOverrideMode mode, out bool useModifier))
            {
                return false;
            }

            if (!KillFocusCore(mainHwnd))
            {
                return false;
            }

            if (_ui.DelayBeforePlayShortcutMs > 0)
            {
                Thread.Sleep(_ui.DelayBeforePlayShortcutMs);
            }

            return SendShortcutWithOptionalModifier(mainHwnd, "press_play", key, mode, useModifier, "press_play_override_reset_failed");
        });
    }

    public bool MoveToStart(IntPtr mainHwnd, int actionDelayMs)
    {
        return ExecuteWithModifierIsolation(mainHwnd, "move_to_start", action: () =>
        {
            SleepActionDelay(actionDelayMs);
            return SendMoveToStartShortcut(mainHwnd);
        });
    }

    // 先頭移動ショートカットを送信
    private bool SendMoveToStartShortcut(IntPtr mainHwnd)
    {
        if (!ShortcutParser.TryParseMoveToStartKey(_ui.MoveToStartKey, out VirtualKey key))
        {
            return false;
        }

        if (!ShortcutParser.TryParseMoveToStartModifier(_ui.MoveToStartModifier, out ModifierOverrideMode mode, out bool useModifier))
        {
            return false;
        }

        return SendShortcutWithOptionalModifier(mainHwnd, "move_to_start", key, mode, useModifier, "move_to_start_override_reset_failed");
    }

    // 修飾子付きショートカット送信を共通化
    private bool SendShortcutWithOptionalModifier(IntPtr mainHwnd, string operationName, VirtualKey key, ModifierOverrideMode mode, bool useModifier, string resetFailedLog)
    {
        if (!useModifier)
        {
            return SendKey(mainHwnd, key);
        }

        if (!_modifierIsolationCoordinator.SetModifierOverride(mainHwnd, operationName, mode))
        {
            return false;
        }

        bool sent = false;
        bool resetOk = true;
        try
        {
            sent = SendKey(mainHwnd, key);
        }
        finally
        {
            resetOk = _modifierIsolationCoordinator.SetModifierOverride(mainHwnd, operationName, ModifierOverrideMode.Neutralize);
            if (!resetOk)
            {
                _log.Warn(resetFailedLog);
            }
        }

        return sent && resetOk;
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

        SleepKeyStrokeInterval();

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

    // 再生ショートカット修飾子の妥当性を判定
    internal static bool IsValidPlayShortcutModifier(string raw)
    {
        return ShortcutParser.IsValidPlayShortcutModifier(raw);
    }

    // 再生ショートカットキーの妥当性を判定
    internal static bool IsValidPlayShortcutKey(string raw)
    {
        return ShortcutParser.IsValidPlayShortcutKey(raw);
    }

    internal static bool IsValidMoveToStartModifier(string raw)
    {
        return ShortcutParser.IsValidMoveToStartModifier(raw);
    }

    internal static bool IsValidMoveToStartKey(string raw)
    {
        return ShortcutParser.IsValidMoveToStartKey(raw);
    }

    internal static bool IsValidClearInputSelectAllModifier(string raw)
    {
        return ShortcutParser.IsValidClearInputSelectAllModifier(raw);
    }

    internal static bool IsValidClearInputSelectAllKey(string raw)
    {
        return ShortcutParser.IsValidClearInputSelectAllKey(raw);
    }

    // ショートカット解析を集約
    private static class ShortcutParser
    {
        // 再生ショートカット修飾子の妥当性を判定
        public static bool IsValidPlayShortcutModifier(string raw)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAltShift, out _, out _);
        }

        // 再生ショートカットキーの妥当性を判定
        public static bool IsValidPlayShortcutKey(string raw)
        {
            return TryParseShortcutKey(raw, out _);
        }

        // 先頭移動修飾子の妥当性を判定
        public static bool IsValidMoveToStartModifier(string raw)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out _, out _);
        }

        // 先頭移動キーの妥当性を判定
        public static bool IsValidMoveToStartKey(string raw)
        {
            return TryParseShortcutKey(raw, out _);
        }

        // 全選択修飾子の妥当性を判定
        public static bool IsValidClearInputSelectAllModifier(string raw)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out _, out _);
        }

        // 全選択キーの妥当性を判定
        public static bool IsValidClearInputSelectAllKey(string raw)
        {
            return TryParseShortcutKey(raw, out _);
        }

        // 再生ショートカット修飾子を解析
        public static bool TryParsePlayShortcutModifier(string raw, out ModifierOverrideMode mode, out bool useModifier)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAltShift, out mode, out useModifier);
        }

        // 再生ショートカットキーを解析
        public static bool TryParsePlayShortcutKey(string raw, out VirtualKey key)
        {
            return TryParseShortcutKey(raw, out key);
        }

        // 先頭移動修飾子を解析
        public static bool TryParseMoveToStartModifier(string raw, out ModifierOverrideMode mode, out bool useModifier)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out mode, out useModifier);
        }

        // 先頭移動キーを解析
        public static bool TryParseMoveToStartKey(string raw, out VirtualKey key)
        {
            return TryParseShortcutKey(raw, out key);
        }

        // 全選択修飾子を解析
        public static bool TryParseClearInputSelectAllModifier(string raw, out ModifierOverrideMode mode, out bool useModifier)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out mode, out useModifier);
        }

        // 全選択キーを解析
        public static bool TryParseClearInputSelectAllKey(string raw, out VirtualKey key)
        {
            return TryParseShortcutKey(raw, out key);
        }

        // 共通ショートカット修飾子を解析
        private static bool TryParseShortcutModifier(string raw, ModifierAllowance allowance, out ModifierOverrideMode mode, out bool useModifier)
        {
            mode = ModifierOverrideMode.Neutralize;
            useModifier = false;
            if (raw == null)
            {
                return false;
            }

            string normalized = raw.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            if (normalized == "ctrl" && (allowance & ModifierAllowance.Ctrl) != 0)
            {
                mode = ModifierOverrideMode.Ctrl;
                useModifier = true;
                return true;
            }

            if (normalized == "alt" && (allowance & ModifierAllowance.Alt) != 0)
            {
                mode = ModifierOverrideMode.Alt;
                useModifier = true;
                return true;
            }

            if (normalized == "shift" && (allowance & ModifierAllowance.Shift) != 0)
            {
                mode = ModifierOverrideMode.Shift;
                useModifier = true;
                return true;
            }

            return false;
        }

        // 共通ショートカットキーを解析
        private static bool TryParseShortcutKey(string raw, out VirtualKey key)
        {
            key = 0;
            string normalized = NormalizeShortcutKey(raw);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            switch (normalized)
            {
                case "spacebar":
                    key = VirtualKey.Space;
                    return true;
                case "home":
                    key = VirtualKey.Home;
                    return true;
                case "end":
                    key = VirtualKey.End;
                    return true;
                case "cursor up":
                    key = VirtualKey.Up;
                    return true;
                case "cursor down":
                    key = VirtualKey.Down;
                    return true;
                case "cursor left":
                    key = VirtualKey.Left;
                    return true;
                case "cursor right":
                    key = VirtualKey.Right;
                    return true;
            }

            if (normalized.Length <= 3 && normalized.StartsWith("f", StringComparison.Ordinal))
            {
                if (int.TryParse(normalized.Substring(1), out int fn) && fn >= 1 && fn <= 12)
                {
                    key = (VirtualKey)((int)VirtualKey.F1 + (fn - 1));
                    return true;
                }
            }

            if (normalized.Length == 1)
            {
                char c = normalized[0];
                if (c >= 'a' && c <= 'z')
                {
                    key = (VirtualKey)((int)'A' + (c - 'a'));
                    return true;
                }

                if (c >= '0' && c <= '9')
                {
                    key = (VirtualKey)c;
                    return true;
                }

                short vkScan = VkKeyScan(c);
                if (vkScan != -1)
                {
                    key = (VirtualKey)(vkScan & 0xFF);
                    return true;
                }
            }

            return false;
        }

        // ショートカットキー文字列を正規化
        private static string NormalizeShortcutKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string[] parts = raw.Trim().ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(" ", parts);
        }

        [Flags]
        private enum ModifierAllowance
        {
            Ctrl = 1,
            Alt = 2,
            Shift = 4,
            CtrlAlt = Ctrl | Alt,
            CtrlAltShift = Ctrl | Alt | Shift
        }
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

    // キー操作間隔を待機
    private void SleepKeyStrokeInterval()
    {
        if (_inputTiming.KeyStrokeIntervalMs > 0)
        {
            Thread.Sleep(_inputTiming.KeyStrokeIntervalMs);
        }
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
    private static extern short VkKeyScan(char ch);

    private enum VirtualKey
    {
        Back = 0x08,
        Return = 0x0D,
        Delete = 0x2E,
        Space = 0x20,
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
        Down = 0x28,
        Left = 0x25,
        Right = 0x27,
        Control = 0x11,
        Shift = 0x10,
        Menu = 0x12
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
