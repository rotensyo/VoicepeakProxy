using System;
using System.Diagnostics;
using System.Threading;

namespace VoicepeakProxyCore;

// 発話監視結果の種類
internal enum SpeakMonitorKind
{
    Completed,
    Interrupted,
    StartTimeout,
    MaxDuration,
    ProcessLost
}

// 発話監視結果を保持
internal readonly struct SpeakMonitorResult
{
    public SpeakMonitorKind Kind { get; }
    public long SegEndAtMs { get; }

    private SpeakMonitorResult(SpeakMonitorKind kind, long segEndAtMs)
    {
        Kind = kind;
        SegEndAtMs = segEndAtMs;
    }

    public static SpeakMonitorResult Completed(long segEndAtMs) => new SpeakMonitorResult(SpeakMonitorKind.Completed, segEndAtMs);
    public static SpeakMonitorResult Interrupted() => new SpeakMonitorResult(SpeakMonitorKind.Interrupted, 0);
    public static SpeakMonitorResult StartTimeout() => new SpeakMonitorResult(SpeakMonitorKind.StartTimeout, 0);
    public static SpeakMonitorResult MaxDuration() => new SpeakMonitorResult(SpeakMonitorKind.MaxDuration, 0);
    public static SpeakMonitorResult ProcessLost() => new SpeakMonitorResult(SpeakMonitorKind.ProcessLost, 0);
}

// 開始確認ループ結果の種類
internal enum StartConfirmLoopKind
{
    Completed,
    Interrupted,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxDuration,
    ProcessLost
}

// 開始確認ループ結果を保持
internal readonly struct StartConfirmLoopResult
{
    public StartConfirmLoopKind Kind { get; }
    public long SegEndAtMs { get; }

    private StartConfirmLoopResult(StartConfirmLoopKind kind, long segEndAtMs)
    {
        Kind = kind;
        SegEndAtMs = segEndAtMs;
    }

    public static StartConfirmLoopResult Completed(long segEndAtMs) => new StartConfirmLoopResult(StartConfirmLoopKind.Completed, segEndAtMs);
    public static StartConfirmLoopResult Interrupted() => new StartConfirmLoopResult(StartConfirmLoopKind.Interrupted, 0);
    public static StartConfirmLoopResult MoveToStartFailed() => new StartConfirmLoopResult(StartConfirmLoopKind.MoveToStartFailed, 0);
    public static StartConfirmLoopResult PlayFailed() => new StartConfirmLoopResult(StartConfirmLoopKind.PlayFailed, 0);
    public static StartConfirmLoopResult StartConfirmTimeout() => new StartConfirmLoopResult(StartConfirmLoopKind.StartConfirmTimeout, 0);
    public static StartConfirmLoopResult MaxDuration() => new StartConfirmLoopResult(StartConfirmLoopKind.MaxDuration, 0);
    public static StartConfirmLoopResult ProcessLost() => new StartConfirmLoopResult(StartConfirmLoopKind.ProcessLost, 0);
}

// 開始確認失敗結果の種類
internal enum StartConfirmFailureKind
{
    None,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxDuration,
    ProcessLost
}

// 開始確認失敗時のハンドリング情報
internal readonly struct StartConfirmFailureDetail
{
    public StartConfirmFailureKind Kind { get; }
    public string DropReason { get; }
    public string MonitorTimeoutReason { get; }
    public bool RequiresFinalizeInput { get; }

    private StartConfirmFailureDetail(StartConfirmFailureKind kind, string dropReason, string monitorTimeoutReason, bool requiresFinalizeInput)
    {
        Kind = kind;
        DropReason = dropReason ?? string.Empty;
        MonitorTimeoutReason = monitorTimeoutReason ?? string.Empty;
        RequiresFinalizeInput = requiresFinalizeInput;
    }

    public static StartConfirmFailureDetail None()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.None, string.Empty, string.Empty, false);

    public static StartConfirmFailureDetail MoveToStartFailed()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.MoveToStartFailed, "move_to_start_failed", string.Empty, true);

    public static StartConfirmFailureDetail PlayFailed()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.PlayFailed, "play_failed", string.Empty, true);

    public static StartConfirmFailureDetail StartConfirmTimeout()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.StartConfirmTimeout, "start_confirm_failed", "start_confirm", true);

    public static StartConfirmFailureDetail MaxDuration()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.MaxDuration, "max_speaking_duration", "max_duration", true);

    public static StartConfirmFailureDetail ProcessLost()
        => new StartConfirmFailureDetail(StartConfirmFailureKind.ProcessLost, "process_lost", string.Empty, false);
}

// 入力検証結果を保持
internal readonly struct InputValidateResult
{
    public bool Success { get; }
    public string Reason { get; }
    public string Cause { get; }
    public string ActualText { get; }

    private InputValidateResult(bool success, string reason, string cause, string actualText)
    {
        Success = success;
        Reason = reason ?? string.Empty;
        Cause = cause ?? string.Empty;
        ActualText = actualText ?? string.Empty;
    }

    public static InputValidateResult Ok(string actualText)
        => new InputValidateResult(true, string.Empty, string.Empty, actualText ?? string.Empty);

    public static InputValidateResult Fail(string reason, string cause, string actualText)
        => new InputValidateResult(false, reason ?? "unknown", cause ?? string.Empty, actualText ?? string.Empty);
}

// 起動時検証結果の種類
internal enum BootValidationRunKind
{
    Completed,
    InputValidationFailed,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxDuration,
    ProcessLost
}

// 起動時検証結果を保持
internal readonly struct BootValidationRunResult
{
    public BootValidationRunKind Kind { get; }
    public InputValidateResult InputValidate { get; }

    private BootValidationRunResult(BootValidationRunKind kind, InputValidateResult inputValidate)
    {
        Kind = kind;
        InputValidate = inputValidate;
    }

    public static BootValidationRunResult Completed(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.Completed, inputValidate);

    public static BootValidationRunResult InputValidationFailed(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.InputValidationFailed, inputValidate);

    public static BootValidationRunResult MoveToStartFailed(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.MoveToStartFailed, inputValidate);

    public static BootValidationRunResult PlayFailed(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.PlayFailed, inputValidate);

    public static BootValidationRunResult StartConfirmTimeout(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.StartConfirmTimeout, inputValidate);

    public static BootValidationRunResult MaxDuration(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.MaxDuration, inputValidate);

    public static BootValidationRunResult ProcessLost(InputValidateResult inputValidate)
        => new BootValidationRunResult(BootValidationRunKind.ProcessLost, inputValidate);
}

// 単発常駐で共有する実行ロジック
internal static class JobExecutionCore
{
    // 再生中の先頭移動を実行
    public static bool MoveToStartDuringPlayback(IVoicepeakUiController ui, IntPtr hwnd, int actionDelayMs)
    {
        return ui.MoveToStart(hwnd, actionDelayMs);
    }

    // セグメント入力準備を実行
    public static bool PrepareSegment(AppConfig config, IVoicepeakUiController ui, Process process, IntPtr hwnd, string text, AppLogger log)
    {
        if (!ui.IsAlive(process))
        {
            log.Warn("prepare_failed_detail reason=process_not_alive cause=voicepeak_process_exited_or_unavailable");
            return false;
        }

        if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs))
        {
            log.Warn("prepare_failed_detail reason=prepare_text_input_failed cause=shortcut_not_applied_or_context_mismatch");
            return false;
        }

        if (!TryClearInputWithRetry(config, ui, process, hwnd, log, "prepare"))
        {
            return false;
        }

        string expected = InputTextNormalizer.NormalizeForTyping(text);
        if (!TryTypeTextWithRetry(config, ui, process, hwnd, expected, log, "prepare"))
        {
            return false;
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, config.InputTiming.PostTypeWaitPerCharMs, config.InputTiming.PostTypeWaitMinMs);
        if (postTypeWaitMs > 0)
        {
            Thread.Sleep(postTypeWaitMs);
        }

        return true;
    }

    // 文字入力後の反映確認と再入力リトライ
    internal static bool TryTypeTextWithRetry(
        AppConfig config,
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        string expected,
        AppLogger log,
        string context)
    {
        int maxRetries = Math.Max(0, config.InputTiming.TypeTextRetryMaxRetries);
        int maxAttempts = maxRetries + 1;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool isRetry = attempt > 0;
            if (isRetry)
            {
                SleepRetryWait(config.InputTiming.TypeTextRetryWaitMs);
                if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs))
                {
                    Warn(log, $"{context}_failed_detail reason=prepare_text_input_failed_on_retry cause=shortcut_not_applied_or_context_mismatch attempt={attempt}");
                    return false;
                }
            }

            if (!ui.TypeText(hwnd, expected))
            {
                if (attempt >= maxRetries)
                {
                    string textDetail = BuildTypeTextLogDetail(expected);
                    Warn(log, isRetry
                        ? $"{context}_failed_detail reason=type_text_failed_on_retry cause=paste_apply_failed attempt={attempt}{textDetail}"
                        : $"{context}_failed_detail reason=type_text_failed cause=paste_apply_failed{textDetail}");
                    return false;
                }

                Warn(log, isRetry
                    ? $"{context}_retry_detail reason=type_text_failed_before_retry cause=paste_apply_failed attempt={attempt}"
                    : $"{context}_retry_detail reason=type_text_failed_before_retry cause=paste_apply_failed attempt=0");
                continue;
            }

            if (expected.Length == 0 || !IsInputEmptyState(ui, hwnd))
            {
                return true;
            }

            if (attempt >= maxRetries)
            {
                string textDetail = BuildTypeTextLogDetail(expected);
                Warn(log, isRetry
                    ? $"{context}_failed_detail reason=typed_text_not_reflected_after_retry cause=input_context_not_focused attempt={attempt}{textDetail}"
                    : $"{context}_failed_detail reason=typed_text_not_reflected cause=input_context_not_focused{textDetail}");
                return false;
            }

            Warn(log, isRetry
                ? $"{context}_retry_detail reason=typed_text_not_reflected_before_retry cause=input_context_not_focused attempt={attempt}"
                : $"{context}_retry_detail reason=typed_text_not_reflected_before_wait cause=input_context_not_focused");
        }

        return false;
    }

    // 入力クリア失敗時の再試行
    internal static bool TryClearInputWithRetry(AppConfig config, IVoicepeakUiController ui, Process process, IntPtr hwnd, AppLogger log, string context)
    {
        int maxRetries = Math.Max(0, config.InputTiming.ClearInputRetryMaxRetries);
        int maxAttempts = maxRetries + 1;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs))
            {
                return true;
            }

            if (attempt >= maxRetries)
            {
                Warn(log, $"{context}_failed_detail reason=clear_input_failed cause=move_to_start_or_delete_not_applied");
                return false;
            }

            Warn(log, $"{context}_retry_detail reason=clear_input_failed_before_retry cause=move_to_start_or_delete_not_applied attempt={attempt}");
            SleepRetryWait(config.InputTiming.ClearInputRetryWaitMs);
        }

        return false;
    }

    // リトライ待機を実行
    private static void SleepRetryWait(int waitMs)
    {
        if (waitMs <= 0)
        {
            return;
        }

        Thread.Sleep(waitMs);
    }

    // 入力欄が空状態か判定
    private static bool IsInputEmptyState(IVoicepeakUiController ui, IntPtr hwnd)
    {
        ReadInputResult read = ui.ReadInputTextDetailed(hwnd);
        int visibleBlockCount = ui.GetVisibleInputBlockCount(hwnd);
        return VoicepeakUiController.IsClearCompleted(read, visibleBlockCount);
    }

    // 再生開始と終了を監視
    public static SpeakMonitorResult MonitorSpeaking(
        AppConfig config,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        Process process,
        IntPtr hwnd,
        AppLogger log,
        Func<bool> stopRequested,
        Func<bool> interruptRequested,
        Action onInterrupt)
    {
        long monitorStartAt = MonoClock.NowMs();
        long startDeadline = monitorStartAt + config.Audio.StartConfirmTimeoutMs;
        bool startConfirmed = false;
        long speakingStartedAt = -1;
        long belowSince = -1;

        while (true)
        {
            if (stopRequested())
            {
                return SpeakMonitorResult.Interrupted();
            }

            if (interruptRequested())
            {
                onInterrupt?.Invoke();
                MoveToStartDuringPlayback(ui, hwnd, config.InputTiming.ActionDelayMs);
                return SpeakMonitorResult.Interrupted();
            }

            if (!ui.IsAlive(process))
            {
                return SpeakMonitorResult.ProcessLost();
            }

            long now = MonoClock.NowMs();
            AudioSessionSnapshot snap = audio.ReadPeak(process.Id);
            float peak = snap.Peak;

            if (!startConfirmed)
            {
                if (peak >= config.Audio.PeakThreshold)
                {
                    startConfirmed = true;
                    speakingStartedAt = now;
                    log.Info("speak_start_confirmed");
                }
                else if (now > startDeadline)
                {
                    return SpeakMonitorResult.StartTimeout();
                }
            }

            if (startConfirmed && config.Audio.MaxSpeakingDurationSec > 0)
            {
                long maxMs = config.Audio.MaxSpeakingDurationSec * 1000L;
                if ((now - speakingStartedAt) > maxMs)
                {
                    MoveToStartDuringPlayback(ui, hwnd, config.InputTiming.ActionDelayMs);
                    return SpeakMonitorResult.MaxDuration();
                }
            }

            if (startConfirmed)
            {
                if (peak < config.Audio.PeakThreshold)
                {
                    if (belowSince < 0)
                    {
                        belowSince = now;
                    }
                    else if ((now - belowSince) >= config.Audio.StopConfirmMs)
                    {
                        return SpeakMonitorResult.Completed(now);
                    }
                }
                else
                {
                    belowSince = -1;
                }
            }

            Thread.Sleep(config.Audio.PollIntervalMs);
        }
    }

    // 開始確認失敗後の再試行可否を判定
    public static bool HandleStartTimeoutRetry(
        AppConfig config,
        int startAttempt)
    {
        bool hasNextAttempt = startAttempt < config.Audio.StartConfirmMaxRetries;
        return hasNextAttempt;
    }

    // 開始確認リトライループを共通化
    public static StartConfirmLoopResult RunStartConfirmLoop(
        AppConfig config,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        Process process,
        IntPtr hwnd,
        AppLogger log,
        Func<bool> stopRequested,
        Func<bool> interruptRequested,
        Action onInterrupt,
        Action<int> onRetry,
        Action onPlayPressed)
    {
        for (int startAttempt = 0; startAttempt <= config.Audio.StartConfirmMaxRetries; startAttempt++)
        {
            if (!ui.PrepareForPlayback(process, hwnd, config.InputTiming.ActionDelayMs))
            {
                return StartConfirmLoopResult.MoveToStartFailed();
            }

            if (!ui.PressPlay(hwnd))
            {
                return StartConfirmLoopResult.PlayFailed();
            }

            onPlayPressed?.Invoke();

            SpeakMonitorResult speakResult = MonitorSpeaking(
                config,
                ui,
                audio,
                process,
                hwnd,
                log,
                stopRequested,
                interruptRequested,
                onInterrupt);
            if (speakResult.Kind == SpeakMonitorKind.Completed)
            {
                return StartConfirmLoopResult.Completed(speakResult.SegEndAtMs);
            }

            if (speakResult.Kind == SpeakMonitorKind.Interrupted)
            {
                return StartConfirmLoopResult.Interrupted();
            }

            if (speakResult.Kind == SpeakMonitorKind.StartTimeout)
            {
                bool shouldRetry = HandleStartTimeoutRetry(config, startAttempt);
                if (shouldRetry)
                {
                    onRetry?.Invoke(startAttempt + 1);
                    continue;
                }

                return StartConfirmLoopResult.StartConfirmTimeout();
            }

            if (speakResult.Kind == SpeakMonitorKind.MaxDuration)
            {
                return StartConfirmLoopResult.MaxDuration();
            }

            if (speakResult.Kind == SpeakMonitorKind.ProcessLost)
            {
                return StartConfirmLoopResult.ProcessLost();
            }
        }

        return StartConfirmLoopResult.StartConfirmTimeout();
    }

    // 開始確認失敗時の共通判定
    public static StartConfirmFailureDetail ClassifyStartConfirmFailure(StartConfirmLoopResult loopResult)
    {
        return loopResult.Kind switch
        {
            StartConfirmLoopKind.MoveToStartFailed => StartConfirmFailureDetail.MoveToStartFailed(),
            StartConfirmLoopKind.PlayFailed => StartConfirmFailureDetail.PlayFailed(),
            StartConfirmLoopKind.StartConfirmTimeout => StartConfirmFailureDetail.StartConfirmTimeout(),
            StartConfirmLoopKind.MaxDuration => StartConfirmFailureDetail.MaxDuration(),
            StartConfirmLoopKind.ProcessLost => StartConfirmFailureDetail.ProcessLost(),
            _ => StartConfirmFailureDetail.None()
        };
    }

    // 起動時検証相当の入力と発話確認を実行
    public static BootValidationRunResult RunBootValidationFlow(
        AppConfig config,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        Process process,
        IntPtr hwnd,
        AppLogger log,
        string targetText)
    {
        string target = targetText ?? string.Empty;
        InputValidateResult bootValidate = InputValidateResult.Fail("unknown", "unknown", string.Empty);
        bool bootInputOk = false;
        for (int attempt = 0; attempt <= config.Validation.ValidationMaxRetries; attempt++)
        {
            bootValidate = ValidateInputText(config, ui, process, hwnd, target, useProbeGuardChars: true);
            if (bootValidate.Success)
            {
                bootInputOk = true;
                break;
            }

            log.Warn(
                "boot_validation_retry_failed " +
                $"attempt={attempt} reason={bootValidate.Reason} cause={bootValidate.Cause} " +
                $"expected=\"{SanitizeForLog(target)}\" actual=\"{SanitizeForLog(bootValidate.ActualText)}\"");

            bool hasNextAttempt = attempt < config.Validation.ValidationMaxRetries;
            if (hasNextAttempt && config.Validation.ValidationRetryIntervalMs > 0)
            {
                Thread.Sleep(config.Validation.ValidationRetryIntervalMs);
            }
        }

        if (!bootInputOk)
        {
            log.Error("boot_validation_fail " +
                $"stage=input_validate reason={bootValidate.Reason} cause={bootValidate.Cause} " +
                $"expected=\"{SanitizeForLog(target)}\" actual=\"{SanitizeForLog(bootValidate.ActualText)}\"");
            return BootValidationRunResult.InputValidationFailed(bootValidate);
        }

        if (string.Equals(target, string.Empty, StringComparison.Ordinal))
        {
            log.Info("boot_validation_skip_speech reason=empty_boot_text");
            log.Info("boot_validation_ok");
            return BootValidationRunResult.Completed(bootValidate);
        }

        StartConfirmLoopResult loopResult = RunStartConfirmLoop(
            config,
            ui,
            audio,
            process,
            hwnd,
            log,
            () => false,
            () => false,
            null,
            attempt => log.Warn($"boot_start_confirm_retry attempt={attempt}"),
            null);
        if (loopResult.Kind == StartConfirmLoopKind.Completed)
        {
            ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs);
            log.Info("boot_validation_ok");
            return BootValidationRunResult.Completed(bootValidate);
        }

        if (loopResult.Kind == StartConfirmLoopKind.MoveToStartFailed)
        {
            log.Error("起動時動作チェック失敗: 先頭移動ショートカットの実行に失敗しました。");
            return BootValidationRunResult.MoveToStartFailed(bootValidate);
        }

        if (loopResult.Kind == StartConfirmLoopKind.PlayFailed)
        {
            log.Error("起動時動作チェック失敗: 再生ボタンの押下に失敗しました。");
            return BootValidationRunResult.PlayFailed(bootValidate);
        }

        if (loopResult.Kind == StartConfirmLoopKind.MaxDuration)
        {
            log.Error("起動時動作チェック失敗: 音声の終了が確認できませんでした。");
            return BootValidationRunResult.MaxDuration(bootValidate);
        }

        if (loopResult.Kind == StartConfirmLoopKind.ProcessLost)
        {
            return BootValidationRunResult.ProcessLost(bootValidate);
        }

        log.Error("起動時動作チェック失敗: 音声の再生が確認できませんでした。");
        return BootValidationRunResult.StartConfirmTimeout(bootValidate);
    }

    // 1回分の入力検証を実行
    public static InputValidateResult ValidateInputText(
        AppConfig config,
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        string text,
        bool useProbeGuardChars)
    {
        if (!ui.IsAlive(process))
        {
            return InputValidateResult.Fail("process_not_alive", "voicepeak_process_exited_or_unavailable", string.Empty);
        }

        if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs))
        {
            return InputValidateResult.Fail("move_to_start_failed", "shortcut_not_applied_or_context_mismatch", string.Empty);
        }

        if (!ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs))
        {
            return InputValidateResult.Fail("clear_input_failed", "move_to_start_or_delete_not_applied", string.Empty);
        }

        string expected = InputTextNormalizer.NormalizeForValidation(text);
        string toType = useProbeGuardChars ? ("A" + expected) : expected;
        if (!ui.TypeText(hwnd, toType))
        {
            return InputValidateResult.Fail("type_text_failed", "paste_apply_failed", string.Empty);
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, config.InputTiming.PostTypeWaitPerCharMs, config.InputTiming.PostTypeWaitMinMs);
        if (postTypeWaitMs > 0)
        {
            Thread.Sleep(postTypeWaitMs);
        }

        if (useProbeGuardChars)
        {
            if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs))
            {
                return InputValidateResult.Fail("move_to_start_failed", "shortcut_not_applied_or_context_mismatch", string.Empty);
            }

            if (!ui.PressDelete(hwnd))
            {
                return InputValidateResult.Fail("delete_failed", "key_message_not_applied", string.Empty);
            }
        }

        ReadInputResult read = ui.ReadInputTextDetailed(hwnd);
        if (!read.Success)
        {
            return InputValidateResult.Fail("read_input_failed", "read_input_source_" + read.Source.ToString(), string.Empty);
        }

        string actual = InputTextNormalizer.NormalizeForValidation(read.Text);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return InputValidateResult.Fail("text_mismatch", BuildInputMismatchCause(expected, actual, useProbeGuardChars), actual);
        }

        return InputValidateResult.Ok(actual);
    }

    // 入力不一致の原因を分類
    public static string BuildInputMismatchCause(string expected, string actual, bool usedProbeGuardChars)
    {
        string a = actual ?? string.Empty;
        if (a.Length == 0)
        {
            return "actual_empty_or_read_failed";
        }

        if (usedProbeGuardChars)
        {
            string expectedWithGuards = "A" + (expected ?? string.Empty);
            if (string.Equals(a, expectedWithGuards, StringComparison.Ordinal))
            {
                return "guard_chars_not_removed";
            }

            if (a.StartsWith("A", StringComparison.Ordinal))
            {
                return "leading_guard_remaining_move_to_start_or_delete_issue";
            }
        }

        if (!string.IsNullOrEmpty(expected) && a.IndexOf(expected, StringComparison.Ordinal) >= 0)
        {
            return "contains_expected_but_not_exact";
        }

        return "actual_unexpected_or_target_mismatch";
    }

    // job終了時の入力欄クリアを共通化
    public static void FinalizeJobInput(
        AppConfig config,
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        bool killFocusAfterClear)
    {
        if (ui == null || process == null || hwnd == IntPtr.Zero)
        {
            return;
        }

        TryClearInputWithRetry(config, ui, process, hwnd, null, "finalize");
        if (killFocusAfterClear)
        {
            ui.KillFocus(hwnd);
        }
    }

    // ログがある場合のみ警告を出力
    private static void Warn(AppLogger log, string message)
    {
        if (log != null)
        {
            log.Warn(message);
        }
    }

    // 文字入力失敗ログに対象文字列を付与
    private static string BuildTypeTextLogDetail(string text)
    {
        string safe = text ?? string.Empty;
        return $" textLength={safe.Length} text=\"{SanitizeForLog(safe)}\"";
    }

    // 入力後待機時間を算出
    public static int ComputePostTypeWaitMs(string text, int perCharMs, int minMs)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        string[] parts = text.Split(new[] { '。' }, StringSplitOptions.None);
        int maxLen = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            int len = parts[i].Length;
            if (len > maxLen)
            {
                maxLen = len;
            }
        }

        int calculated = maxLen * perCharMs;
        return Math.Max(minMs, calculated);
    }

    // pause値から補正時間を差し引き
    public static int AdjustPauseByStopConfirmAndPlayDelay(AppConfig config, int pauseMs, string phase, int segmentIndex)
    {
        int compensation;
        if (string.Equals(phase, "pre", StringComparison.Ordinal))
        {
            // 先頭は再生押下待機のみを補正
            compensation = config.Ui.DelayBeforePlayShortcutMs;
            // 中間以降は停止検知待機も補正
            if (segmentIndex > 0)
            {
                compensation += config.Audio.StopConfirmMs;
            }
        }
        else
        {
            // 末尾は停止検知待機のみを補正
            compensation = config.Audio.StopConfirmMs;
        }

        int adjusted = pauseMs - compensation;
        return adjusted > 0 ? adjusted : 0;
    }

    // ログ向けに制御文字を置換
    private static string SanitizeForLog(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }
}
