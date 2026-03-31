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
    // 再生中の先頭移動を文脈別に実行
    public static bool MoveToStartDuringPlayback(AppConfig config, IVoicepeakUiController ui, IntPtr hwnd, int actionDelayMs)
    {
        if (!VoicepeakUiController.IsFunctionKeyMoveToStartShortcut(config.Ui.MoveToStartShortcut))
        {
            if (!ui.PressPlay(hwnd))
            {
                return false;
            }
        }

        return ui.MoveToStart(hwnd, actionDelayMs);
    }

    // セグメント入力準備を実行
    public static bool PrepareSegment(AppConfig config, IVoicepeakUiController ui, Process process, IntPtr hwnd, string text, AppLogger log, bool allowCompositePrimeBeforeTextFocusWhenUnprimed)
    {
        if (!ui.IsAlive(process))
        {
            log.Warn("prepare_failed_detail reason=process_not_alive cause=voicepeak_process_exited_or_unavailable");
            return false;
        }

        if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs, allowCompositePrimeBeforeTextFocusWhenUnprimed))
        {
            log.Warn("prepare_failed_detail reason=prepare_text_input_failed cause=shortcut_not_applied_or_context_mismatch");
            return false;
        }

        if (!ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs, allowCompositePrimeBeforeTextFocusWhenUnprimed))
        {
            log.Warn("prepare_failed_detail reason=clear_input_failed cause=move_to_start_or_delete_not_applied");
            return false;
        }

        string expected = InputTextNormalizer.Normalize(text);
        int charDelay = config.InputTiming.CharDelayBaseMs;
        if (!ui.TypeText(hwnd, expected, charDelay))
        {
            log.Warn("prepare_failed_detail reason=type_text_failed cause=wm_char_input_failed");
            return false;
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, config.InputTiming.PostTypeWaitPerCharMs, config.InputTiming.PostTypeWaitMinMs);
        if (postTypeWaitMs > 0)
        {
            Thread.Sleep(postTypeWaitMs);
        }

        return true;
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
                MoveToStartDuringPlayback(config, ui, hwnd, config.InputTiming.ActionDelayMs);
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
                    MoveToStartDuringPlayback(config, ui, hwnd, config.InputTiming.ActionDelayMs);
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
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        int startAttempt,
        ref bool recoveryClickUsed)
    {
        bool hasNextAttempt = startAttempt < config.Audio.StartConfirmMaxRetries;
        if (!hasNextAttempt)
        {
            return false;
        }

        if (!recoveryClickUsed
            && ui.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry))
        {
            ui.TryPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry);
            recoveryClickUsed = true;
        }

        return true;
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
        bool recoveryClickUsed = false;
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
                bool shouldRetry = HandleStartTimeoutRetry(config, ui, process, hwnd, startAttempt, ref recoveryClickUsed);
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
        int charDelay = config.InputTiming.CharDelayBaseMs;

        for (int attempt = 0; attempt <= config.Startup.BootValidationMaxRetries; attempt++)
        {
            bootValidate = ValidateInputText(config, ui, process, hwnd, target, charDelay, useProbeGuardChars: true);
            if (bootValidate.Success)
            {
                bootInputOk = true;
                break;
            }

            log.Warn(
                "boot_validation_retry_failed " +
                $"attempt={attempt} reason={bootValidate.Reason} cause={bootValidate.Cause} " +
                $"expected=\"{SanitizeForLog(target)}\" actual=\"{SanitizeForLog(bootValidate.ActualText)}\"");

            bool hasNextAttempt = attempt < config.Startup.BootValidationMaxRetries;
            if (hasNextAttempt && config.Startup.BootValidationRetryIntervalMs > 0)
            {
                Thread.Sleep(config.Startup.BootValidationRetryIntervalMs);
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
            ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs, true);
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
        int charDelay,
        bool useProbeGuardChars)
    {
        if (!ui.IsAlive(process))
        {
            return InputValidateResult.Fail("process_not_alive", "voicepeak_process_exited_or_unavailable", string.Empty);
        }

        if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs, true))
        {
            return InputValidateResult.Fail("move_to_start_failed", "shortcut_not_applied_or_context_mismatch", string.Empty);
        }

        if (!ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs, true))
        {
            return InputValidateResult.Fail("clear_input_failed", "move_to_start_or_delete_not_applied", string.Empty);
        }

        string expected = InputTextNormalizer.Normalize(text);
        string toType = useProbeGuardChars ? ("A" + expected) : expected;
        if (!ui.TypeText(hwnd, toType, charDelay))
        {
            return InputValidateResult.Fail("type_text_failed", "wm_char_input_failed", string.Empty);
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, config.InputTiming.PostTypeWaitPerCharMs, config.InputTiming.PostTypeWaitMinMs);
        if (postTypeWaitMs > 0)
        {
            Thread.Sleep(postTypeWaitMs);
        }

        if (useProbeGuardChars)
        {
            if (!ui.PrepareForTextInput(process, hwnd, config.InputTiming.ActionDelayMs, true))
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

        string actual = InputTextNormalizer.Normalize(read.Text);
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
        bool allowCompositePrimeBeforeTextFocusWhenUnprimed,
        bool killFocusAfterClear)
    {
        if (ui == null || process == null || hwnd == IntPtr.Zero)
        {
            return;
        }

        ui.ClearInput(process, hwnd, config.InputTiming.ActionDelayMs, allowCompositePrimeBeforeTextFocusWhenUnprimed);
        if (killFocusAfterClear)
        {
            ui.KillFocus(hwnd);
        }
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
    public static int AdjustPauseByStopConfirmAndPlayDelay(AppConfig config, int pauseMs, string phase, string jobId, int segmentIndex, string nextText, AppLogger log)
    {
        int compensation = config.Audio.StopConfirmMs + config.Ui.DelayBeforePlayShortcutMs;
        int requiredByFormulaMs = compensation;
        if (string.Equals(phase, "pre", StringComparison.Ordinal))
        {
            string normalized = InputTextNormalizer.Normalize(nextText);
            int typingMs = normalized.Length * config.InputTiming.CharDelayBaseMs;
            int postTypeWaitMs = ComputePostTypeWaitMs(normalized, config.InputTiming.PostTypeWaitPerCharMs, config.InputTiming.PostTypeWaitMinMs);
            requiredByFormulaMs += typingMs + postTypeWaitMs;
        }

        if (pauseMs > 0 && pauseMs < requiredByFormulaMs)
        {
            log.Warn($"pause_too_short jobId={jobId} index={segmentIndex} phase={phase} pauseMs={pauseMs} requiredMs={requiredByFormulaMs}");
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
