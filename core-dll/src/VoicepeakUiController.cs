using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoicepeakProxyCore;

// VOICEPEAKのUI操作を担当
internal sealed class VoicepeakUiController : IVoicepeakUiController, IDisposable
{
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmKillFocus = 0x0008;
    private const int UiaControlTypePropertyId = 30003;
    private const int UiaNamePropertyId = 30005;
    private const int UiaValueValuePropertyId = 30045;
    private const int UiaValuePatternId = 10002;
    private const int UiaTextPatternId = 10014;
    private const int UiaControlTypeEdit = 50004;
    private const int UiaControlTypeText = 50020;
    private const int UiaControlTypeDocument = 50030;
    private static readonly Guid ClsidCUIAutomation = new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid ClsidCUIAutomation8 = new Guid("E22AD333-B25F-460C-83D0-0581107395C9");
    private readonly UiConfig _ui;
    private readonly InputTimingConfig _inputTiming;
    private readonly HookConfig _hook;
    private readonly TextConfig _text;
    private readonly DebugConfig _debug;
    private readonly AppLogger _log;
    private readonly VoicepeakTargetResolver _targetResolver;
    private readonly ModifierIsolationCoordinator _modifierIsolationCoordinator;
    private readonly UiAutomationExecutor _uiAutomationExecutor;
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
        _uiAutomationExecutor = new UiAutomationExecutor();
    }

    // 依存を受け取ってUI操作を初期化
    

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

            int clearInputMaxPasses = Math.Max(1, _inputTiming.ClearInputMaxPasses);
            for (int pass = 0; pass < clearInputMaxPasses; pass++)
            {
                ClearInputState before = ReadClearInputState(mainHwnd);
                if (IsClearCompleted(before.Read, before.VisibleBlockCount))
                {
                    return true;
                }

                int visibleBlockCount = Math.Max(1, before.VisibleBlockCount);
                if (!RunSelectAllDeleteCycle(mainHwnd, selectAllKey, selectAllMode, useSelectAllModifier, visibleBlockCount, actionDelayMs))
                {
                    return false;
                }
            }

            return LogIncompleteClearInputAndReturnResult(ReadClearInputState(mainHwnd));
        });
    }

    // 全選択後にDeleteを二回送信するサイクル
    private bool RunSelectAllDeleteCycle(IntPtr mainHwnd, VirtualKey selectAllKey, ModifierOverrideMode selectAllMode, bool useSelectAllModifier, int visibleBlockCount, int actionDelayMs)
    {
        int cycleCount = Math.Max(1, visibleBlockCount);
        for (int i = 0; i < cycleCount; i++)
        {
            SleepActionDelay(actionDelayMs);
            if (!SendShortcutWithOptionalModifier(mainHwnd, "clear_input_select_all", selectAllKey, selectAllMode, useSelectAllModifier, "clear_input_select_all_override_reset_failed"))
            {
                return false;
            }

            SleepActionDelay(actionDelayMs);

            if (!PressDeleteCore(mainHwnd))
            {
                return false;
            }

            SleepActionDelay(actionDelayMs);

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
        return ReadInputSnapshot(mainHwnd).VisibleBlockCount;
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
        ReadInputSnapshot snapshot = ReadInputSnapshot(mainHwnd);
        return new ClearInputState(snapshot.Read, snapshot.VisibleBlockCount);
    }

    // UIA読み取りを1回で実行して共通スナップショットを返す
    public ReadInputSnapshot ReadInputSnapshot(IntPtr mainHwnd)
    {
        return _uiAutomationExecutor.Invoke(() => ReadInputSnapshotCore(mainHwnd));
    }

    // 発話開始後のsafe pointを通知
    public void NotifyPlaybackSafePoint()
    {
    }

    // テスト互換用の可視入力欄数算出
    private static int EstimateVisibleBlockCount(IntPtr mainHwnd)
    {
        return ReadInputSnapshotCore(mainHwnd).VisibleBlockCount;
    }

    internal static ReadInputSnapshot ReadInputSnapshotCore(IntPtr mainHwnd)
    {
        if (mainHwnd == IntPtr.Zero)
        {
            return new ReadInputSnapshot(ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0), 0);
        }

        try
        {
            Interop.UIAutomationClient.IUIAutomation automation = CreateOfficialUiaAutomation();
            if (automation == null)
            {
                return new ReadInputSnapshot(ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0), 0);
            }

            using ComScope automationScope = new ComScope(automation);
            Interop.UIAutomationClient.IUIAutomationElement root = automation.ElementFromHandle(mainHwnd);
            if (root == null)
            {
                return new ReadInputSnapshot(ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0), 0);
            }

            using ComScope rootScope = new ComScope(root);
            List<OfficialComTextCandidateInfo> candidates = null;
            try
            {
                candidates = CollectTextCandidatesOfficialCom(automation, root);
                ReadInputResult read = BuildReadInputResultOfficialCom(candidates);
                return new ReadInputSnapshot(read, candidates.Count);
            }
            finally
            {
                ReleaseOfficialComCandidates(candidates);
            }
        }
        catch
        {
            return new ReadInputSnapshot(ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0), 0);
        }
    }

    internal static ReadInputResult ReadInputTextDetailedCore(IntPtr mainHwnd)
    {
        if (mainHwnd == IntPtr.Zero)
        {
            return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
        }

        try
        {
            Interop.UIAutomationClient.IUIAutomation automation = CreateOfficialUiaAutomation();
            if (automation == null)
            {
                return ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);
            }

            using ComScope automationScope = new ComScope(automation);
            Interop.UIAutomationClient.IUIAutomationElement root = automation.ElementFromHandle(mainHwnd);
            if (root == null)
            {
                return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
            }

            using ComScope rootScope = new ComScope(root);
            List<OfficialComTextCandidateInfo> candidates = null;
            try
            {
                candidates = CollectTextCandidatesOfficialCom(automation, root);
                return BuildReadInputResultForValidationOfficialCom(candidates);
            }
            finally
            {
                ReleaseOfficialComCandidates(candidates);
            }
        }
        catch
        {
            return ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);
        }
    }

    // 仮想クリップボード経由で文字列をペースト
    public bool TypeText(IntPtr mainHwnd, string text)
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
        bool virtualClipboardSet = false;
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

            if (!_modifierIsolationCoordinator.SetVirtualClipboardText(send, "type_text"))
            {
                _log.Warn($"type_text_target_send_failed reason=clip_set_failed hwnd=0x{mainHwnd.ToInt64():X}");
                return false;
            }

            virtualClipboardSet = true;

            if (!ShortcutParser.TryParsePasteShortcutKey(_ui.PasteShortcutKey, out VirtualKey pasteKey))
            {
                return false;
            }

            if (!ShortcutParser.TryParsePasteShortcutModifier(_ui.PasteShortcutModifier, out ModifierOverrideMode pasteMode, out bool usePasteModifier))
            {
                return false;
            }

            if (!SendShortcutWithOptionalModifier(
                    mainHwnd,
                    "type_text_paste",
                    pasteKey,
                    pasteMode,
                    useModifier: usePasteModifier,
                    "type_text_paste_override_reset_failed"))
            {
                _log.Warn($"type_text_target_send_failed reason=paste_failed hwnd=0x{mainHwnd.ToInt64():X}");
                return false;
            }

            _log.Info("type_text_route_selected route=ctrl_v_virtualized");
            return true;
        }
        finally
        {
            if (virtualClipboardSet)
            {
                _modifierIsolationCoordinator.ClearVirtualClipboard("type_text");
            }

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
        {
            SleepActionDelay(_inputTiming.ActionDelayMs);
            return PressDeleteCore(mainHwnd);
        });
    }

    // Deleteキー送信の共通実体
    private bool PressDeleteCore(IntPtr mainHwnd)
    {
        return SendKey(mainHwnd, VirtualKey.Delete);
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

    internal static bool IsValidPasteShortcutModifier(string raw)
    {
        return ShortcutParser.IsValidPasteShortcutModifier(raw);
    }

    internal static bool IsValidPasteShortcutKey(string raw)
    {
        return ShortcutParser.IsValidPasteShortcutKey(raw);
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

        // ペースト修飾子の妥当性を判定
        public static bool IsValidPasteShortcutModifier(string raw)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out _, out _);
        }

        // ペーストキーの妥当性を判定
        public static bool IsValidPasteShortcutKey(string raw)
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

        // ペースト修飾子を解析
        public static bool TryParsePasteShortcutModifier(string raw, out ModifierOverrideMode mode, out bool useModifier)
        {
            return TryParseShortcutModifier(raw, ModifierAllowance.CtrlAlt, out mode, out useModifier);
        }

        // ペーストキーを解析
        public static bool TryParsePasteShortcutKey(string raw, out VirtualKey key)
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
        return _uiAutomationExecutor.Invoke(() => ReadInputTextDetailedCore(mainHwnd));
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

    private static bool SendWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr result;
        IntPtr ok = SendMessageTimeout(hWnd, msg, wParam, lParam, SmtoAbortIfHung, 1000, out result);
        return ok != IntPtr.Zero;
    }


    private static List<OfficialComTextCandidateInfo> CollectTextCandidatesOfficialCom(
        Interop.UIAutomationClient.IUIAutomation automation,
        Interop.UIAutomationClient.IUIAutomationElement root)
    {
        List<OfficialComTextCandidateInfo> result = new List<OfficialComTextCandidateInfo>();
        if (automation == null || root == null)
        {
            return result;
        }

        Interop.UIAutomationClient.IUIAutomationCondition condition = null;
        Interop.UIAutomationClient.IUIAutomationElementArray matches = null;
        try
        {
            condition = CreateTextCandidateConditionOfficialCom(automation);
            if (condition == null)
            {
                return result;
            }

            matches = root.FindAll(Interop.UIAutomationClient.TreeScope.TreeScope_Descendants, condition);
            if (matches == null)
            {
                return result;
            }

            int length = matches.Length;
            for (int i = 0; i < length; i++)
            {
                Interop.UIAutomationClient.IUIAutomationElement element = null;
                try
                {
                    element = matches.GetElement(i);
                    if (element == null)
                    {
                        continue;
                    }

                    if (TryBuildTextCandidateOfficialCom(element, out OfficialComTextCandidateInfo candidate))
                    {
                        result.Add(candidate);
                        element = null;
                    }
                }
                finally
                {
                    ReleaseComObjectIfNeeded(element);
                }
            }
        }
        finally
        {
            ReleaseComObjectIfNeeded(matches);
            ReleaseComObjectIfNeeded(condition);
        }

        return result;
    }

    // 候補型の絞り込み条件を構築
    private static Interop.UIAutomationClient.IUIAutomationCondition CreateTextCandidateConditionOfficialCom(
        Interop.UIAutomationClient.IUIAutomation automation)
    {
        if (automation == null)
        {
            return null;
        }

        Interop.UIAutomationClient.IUIAutomationCondition editCondition = null;
        Interop.UIAutomationClient.IUIAutomationCondition textCondition = null;
        Interop.UIAutomationClient.IUIAutomationCondition documentCondition = null;
        Interop.UIAutomationClient.IUIAutomationCondition typeCondition = null;
        Interop.UIAutomationClient.IUIAutomationCondition nameEmptyCondition = null;
        Interop.UIAutomationClient.IUIAutomationCondition firstOr = null;
        Interop.UIAutomationClient.IUIAutomationCondition finalCondition = null;
        try
        {
            editCondition = automation.CreatePropertyCondition(UiaControlTypePropertyId, UiaControlTypeEdit);
            textCondition = automation.CreatePropertyCondition(UiaControlTypePropertyId, UiaControlTypeText);
            documentCondition = automation.CreatePropertyCondition(UiaControlTypePropertyId, UiaControlTypeDocument);
            firstOr = automation.CreateOrCondition(editCondition, textCondition);
            typeCondition = automation.CreateOrCondition(firstOr, documentCondition);
            nameEmptyCondition = automation.CreatePropertyCondition(UiaNamePropertyId, string.Empty);
            finalCondition = automation.CreateAndCondition(typeCondition, nameEmptyCondition);
            return finalCondition;
        }
        finally
        {
            ReleaseComObjectIfNeeded(firstOr);
            ReleaseComObjectIfNeeded(nameEmptyCondition);
            ReleaseComObjectIfNeeded(typeCondition);
            ReleaseComObjectIfNeeded(documentCondition);
            ReleaseComObjectIfNeeded(textCondition);
            ReleaseComObjectIfNeeded(editCondition);
        }
    }

    // 候補条件でCOM要素を判定
    private static bool TryBuildTextCandidateOfficialCom(
        Interop.UIAutomationClient.IUIAutomationElement element,
        out OfficialComTextCandidateInfo candidate)
    {
        candidate = default(OfficialComTextCandidateInfo);
        if (element == null)
        {
            return false;
        }

        int controlTypeId = Convert.ToInt32(element.GetCurrentPropertyValue(UiaControlTypePropertyId));
        string name = Convert.ToString(element.GetCurrentPropertyValue(UiaNamePropertyId)) ?? string.Empty;
        if (!IsCollectTextCandidateTarget(controlTypeId, name))
        {
            return false;
        }

        candidate = new OfficialComTextCandidateInfo(element);
        return true;
    }

    internal static bool IsCollectTextCandidateTarget(int controlTypeId, string name)
    {
        bool allowedControlType = controlTypeId == UiaControlTypeEdit
                                  || controlTypeId == UiaControlTypeDocument
                                  || controlTypeId == UiaControlTypeText;
        return allowedControlType && name != null && name.Length == 0;
    }

    private static ReadInputResult BuildReadInputResultOfficialCom(List<OfficialComTextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
        }

        OfficialComTextCandidateInfo primaryCandidate = candidates[0];
        string primary = TryGetElementTextOfficialCom(primaryCandidate.Element);
        string normalizedPrimary = NormalizeForPanelCompare(primary);
        int totalLength = EstimateTotalInputTextLengthOfficialCom(candidates);
        if (totalLength < normalizedPrimary.Length)
        {
            totalLength = normalizedPrimary.Length;
        }

        return ReadInputResult.Ok(normalizedPrimary, totalLength, ReadInputSource.PrimaryUiA);
    }

    private static ReadInputResult BuildReadInputResultForValidationOfficialCom(List<OfficialComTextCandidateInfo> candidates)
    {
        OfficialComTextCandidateInfo? single = FindSingleInputBoxOfficialCom(candidates);
        if (!single.HasValue || single.Value.Element == null)
        {
            return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
        }

        string primary = TryGetElementTextOfficialCom(single.Value.Element);
        string normalizedPrimary = NormalizeForPanelCompare(primary);
        int totalLength = NormalizeForLength(normalizedPrimary).Length;
        return ReadInputResult.Ok(normalizedPrimary, totalLength, ReadInputSource.PrimaryUiA);
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

    // 候補配列から最良入力欄を選択
    private static OfficialComTextCandidateInfo? FindSingleInputBoxOfficialCom(List<OfficialComTextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count != 1)
        {
            return null;
        }

        return candidates[0];
    }

    // 候補群の総文字数を算出
    private static int EstimateTotalInputTextLengthOfficialCom(List<OfficialComTextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            string text = NormalizeForLength(TryGetElementTextOfficialCom(candidates[i].Element));
            if (text.Length > 0)
            {
                sum += text.Length;
            }
        }

        return sum;
    }

    // 公式COM経路で要素文字列を取得
    private static string TryGetElementTextOfficialCom(Interop.UIAutomationClient.IUIAutomationElement element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        object valuePatternObj = null;
        object textPatternObj = null;
        Interop.UIAutomationClient.IUIAutomationTextRange range = null;
        try
        {
            string valuePropertyText = Convert.ToString(element.GetCurrentPropertyValue(UiaValueValuePropertyId)) ?? string.Empty;
            if (!string.IsNullOrEmpty(valuePropertyText))
            {
                return valuePropertyText;
            }

            valuePatternObj = element.GetCurrentPattern(UiaValuePatternId);
            if (valuePatternObj is Interop.UIAutomationClient.IUIAutomationValuePattern valuePattern)
            {
                string value = valuePattern.CurrentValue;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            textPatternObj = element.GetCurrentPattern(UiaTextPatternId);
            if (textPatternObj is Interop.UIAutomationClient.IUIAutomationTextPattern textPattern)
            {
                range = textPattern.DocumentRange;
                if (range != null)
                {
                    return (range.GetText(-1) ?? string.Empty).TrimEnd('\r', '\n');
                }
            }
        }
        catch
        {
        }
        finally
        {
            ReleaseComObjectIfNeeded(range);
            ReleaseComObjectIfNeeded(textPatternObj);
            ReleaseComObjectIfNeeded(valuePatternObj);
        }

        return string.Empty;
    }

    // 候補COM要素を一括解放
    private static void ReleaseOfficialComCandidates(List<OfficialComTextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            ReleaseComObjectIfNeeded(candidates[i].Element);
        }
    }

    // 公式COMオートメーションを生成
    private static Interop.UIAutomationClient.IUIAutomation CreateOfficialUiaAutomation()
    {
        Type type = Type.GetTypeFromCLSID(ClsidCUIAutomation, throwOnError: false);
        if (type == null)
        {
            type = Type.GetTypeFromCLSID(ClsidCUIAutomation8, throwOnError: false);
        }

        if (type == null)
        {
            return null;
        }

        object instance = Activator.CreateInstance(type);
        return instance as Interop.UIAutomationClient.IUIAutomation;
    }

    // COMオブジェクトを安全に解放
    private static void ReleaseComObjectIfNeeded(object comObject)
    {
        if (comObject == null)
        {
            return;
        }

        if (!Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
        }
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

    private readonly struct OfficialComTextCandidateInfo
    {
        public Interop.UIAutomationClient.IUIAutomationElement Element { get; }

        public OfficialComTextCandidateInfo(Interop.UIAutomationClient.IUIAutomationElement element)
        {
            Element = element;
        }
    }

    private readonly struct ComScope : IDisposable
    {
        private readonly object _comObject;

        public ComScope(object comObject)
        {
            _comObject = comObject;
        }

        public void Dispose()
        {
            ReleaseComObjectIfNeeded(_comObject);
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

    private sealed class UiAutomationExecutor : IDisposable
    {
        private readonly BlockingCollection<UiAutomationWorkItem> _queue = new BlockingCollection<UiAutomationWorkItem>();
        private readonly Thread _thread;

        public UiAutomationExecutor()
        {
            _thread = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "voicepeak-uia-worker"
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public T Invoke<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            UiAutomationWorkItem item = new UiAutomationWorkItem(() => action());
            _queue.Add(item);
            item.Wait();
            if (item.Error != null)
            {
                throw item.Error;
            }

            return (T)item.Result;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            if (Thread.CurrentThread != _thread && _thread.IsAlive)
            {
                _thread.Join();
            }

            _queue.Dispose();
        }

        private void WorkerMain()
        {
            foreach (UiAutomationWorkItem item in _queue.GetConsumingEnumerable())
            {
                item.Execute();
            }
        }
    }

    private sealed class UiAutomationWorkItem
    {
        private readonly Func<object> _action;
        private readonly ManualResetEventSlim _completed = new ManualResetEventSlim(false);

        public UiAutomationWorkItem(Func<object> action)
        {
            _action = action;
        }

        public object Result { get; private set; }
        public Exception Error { get; private set; }

        public void Execute()
        {
            try
            {
                Result = _action();
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                _completed.Set();
            }
        }

        public void Wait()
        {
            _completed.Wait();
            _completed.Dispose();
        }
    }

    public void Dispose()
    {
        _modifierIsolationCoordinator.Dispose();
        _uiAutomationExecutor.Dispose();
    }
}
