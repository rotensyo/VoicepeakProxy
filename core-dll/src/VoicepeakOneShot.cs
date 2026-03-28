using System;
using System.Diagnostics;
using System.Threading;

namespace VoicepeakProxyCore;

// 単発実行結果
public enum SpeakOnceStatus
{
    Completed,
    InvalidRequest,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    PrepareFailed,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxSpeakingDurationExceeded,
    ProcessLost
}

// 単発実行結果
public sealed class SpeakOnceResult
{
    public SpeakOnceStatus Status { get; set; }
    public int SegmentsExecuted { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == SpeakOnceStatus.Completed;
}

// 単発実行入力
public sealed class SpeakOnceRequest
{
    public string Text { get; set; }
}

// 単発入力検証結果
public enum ValidateInputOnceStatus
{
    Completed,
    InvalidRequest,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    PrepareFailed,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxSpeakingDurationExceeded,
    ClearInputFailed,
    TypeTextFailed,
    ReadInputFailed,
    TextMismatch,
    ProcessLost
}

// 単発入力検証結果
public sealed class ValidateInputOnceResult
{
    public ValidateInputOnceStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string ActualText { get; set; } = string.Empty;
    public bool Succeeded => Status == ValidateInputOnceStatus.Completed;
}

// 単発入力削除結果
public enum ClearInputOnceStatus
{
    Completed,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    ClearInputFailed,
    ProcessLost
}

// 単発入力削除結果
public sealed class ClearInputOnceResult
{
    public ClearInputOnceStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == ClearInputOnceStatus.Completed;
}

// 単発実行APIの公開窓口
public static class VoicepeakOneShot
{
    private const string ModifierGuardUnavailableFatalReason = "reason=modifier_guard_unavailable_fatal";
    private const string ModifierGuardReleaseFailedFatalReason = "reason=modifier_guard_release_failed_fatal";

    // 単発入力検証
    public static ValidateInputOnceResult ValidateInputOnce(
        AppConfig config,
        IAppLogger logger = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger log = new AppLogger(logger ?? new ConsoleAppLogger());

        return ValidateInputOnceCore(
            config,
            log,
            new VoicepeakUiController(config.Ui, config.Prepare, config.Debug, log),
            new AudioSessionReader(log));
    }

    // 依存を差し替えて単発入力検証
    internal static ValidateInputOnceResult ValidateInputOnceCore(
        AppConfig config,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        string targetText = config.Prepare.BootValidationText ?? string.Empty;

        ResolveTargetResult resolved = ui.TryResolveTargetDetailed();
        if (!resolved.Success)
        {
            return BuildValidateInputResolveTargetFailedResult(resolved, log);
        }

        Process process = resolved.Process;
        IntPtr hwnd = resolved.MainHwnd;

        if (!TryBeginModifierIsolationSession(ui, process.Id, "oneshot_validate", log))
        {
            return new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.ProcessLost,
                ErrorMessage = ModifierGuardUnavailableFatalReason
            };
        }

        ValidateInputOnceResult result;
        bool sessionEnded;
        try
        {
            if (ui.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.Validation))
            {
                ui.TryPrimeInputContext(process, hwnd, InputContextPrimeReason.Validation);
            }

            BootValidationRunResult run = JobExecutionCore.RunBootValidationFlow(
                config,
                ui,
                audio,
                process,
                hwnd,
                log,
                targetText);
            if (run.Kind == BootValidationRunKind.Completed)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.Completed,
                    ActualText = run.InputValidate.ActualText
                };
            }
            else if (run.Kind == BootValidationRunKind.InputValidationFailed)
            {
                result = new ValidateInputOnceResult
                {
                    Status = MapValidateInputOnceStatus(run.InputValidate.Reason),
                    ErrorMessage = $"reason={run.InputValidate.Reason} cause={run.InputValidate.Cause}",
                    ActualText = run.InputValidate.ActualText
                };
            }
            else if (run.Kind == BootValidationRunKind.MoveToStartFailed)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.MoveToStartFailed,
                    ErrorMessage = "reason=move_to_start_failed"
                };
            }
            else if (run.Kind == BootValidationRunKind.PlayFailed)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.PlayFailed,
                    ErrorMessage = "reason=play_failed"
                };
            }
            else if (run.Kind == BootValidationRunKind.StartConfirmTimeout)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.StartConfirmTimeout,
                    ErrorMessage = "reason=start_confirm_failed"
                };
            }
            else if (run.Kind == BootValidationRunKind.MaxDuration)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.MaxSpeakingDurationExceeded,
                    ErrorMessage = "reason=max_speaking_duration"
                };
            }
            else if (run.Kind == BootValidationRunKind.ProcessLost)
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.ProcessLost,
                    ErrorMessage = "reason=process_lost"
                };
            }
            else
            {
                result = new ValidateInputOnceResult
                {
                    Status = ValidateInputOnceStatus.InvalidRequest,
                    ErrorMessage = "reason=unknown"
                };
            }
        }
        finally
        {
            sessionEnded = TryEndModifierIsolationSession(ui, "oneshot_validate", log);
        }

        if (!sessionEnded)
        {
            return new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.ProcessLost,
                ErrorMessage = ModifierGuardReleaseFailedFatalReason,
                ActualText = result?.ActualText ?? string.Empty
            };
        }

        return result;
    }

    // 単発入力削除
    public static ClearInputOnceResult ClearInputOnce(
        AppConfig config,
        IAppLogger logger = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger log = new AppLogger(logger ?? new ConsoleAppLogger());

        return ClearInputOnceCore(
            config,
            log,
            new VoicepeakUiController(config.Ui, config.Prepare, config.Debug, log));
    }

    // 依存を差し替えて単発入力削除
    internal static ClearInputOnceResult ClearInputOnceCore(
        AppConfig config,
        AppLogger log,
        IVoicepeakUiController ui)
    {
        ResolveTargetResult resolved = ui.TryResolveTargetDetailed();
        if (!resolved.Success)
        {
            return BuildClearInputResolveTargetFailedResult(resolved, log);
        }

        Process process = resolved.Process;
        IntPtr hwnd = resolved.MainHwnd;

        if (!TryBeginModifierIsolationSession(ui, process.Id, "oneshot_clear_input", log))
        {
            return new ClearInputOnceResult
            {
                Status = ClearInputOnceStatus.ProcessLost,
                ErrorMessage = ModifierGuardUnavailableFatalReason
            };
        }

        ClearInputOnceResult result;
        bool sessionEnded;
        try
        {
            if (!ui.IsAlive(process))
            {
                log.Error("対象プロセスが終了したため処理を中断しました。");
                result = new ClearInputOnceResult { Status = ClearInputOnceStatus.ProcessLost };
            }
            else
            {
            if (!ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false))
            {
                    result = new ClearInputOnceResult { Status = ClearInputOnceStatus.ClearInputFailed };
                }
                else
                {
                    result = new ClearInputOnceResult { Status = ClearInputOnceStatus.Completed };
                }
            }
        }
        finally
        {
            sessionEnded = TryEndModifierIsolationSession(ui, "oneshot_clear_input", log);
        }

        if (!sessionEnded)
        {
            return new ClearInputOnceResult
            {
                Status = ClearInputOnceStatus.ProcessLost,
                ErrorMessage = ModifierGuardReleaseFailedFatalReason
            };
        }

        return result;
    }

    // 単発実行
    public static SpeakOnceResult SpeakOnce(
        AppConfig config,
        SpeakOnceRequest request,
        IAppLogger logger = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger log = new AppLogger(logger ?? new ConsoleAppLogger());

        RequestValidationMode validation = config.Validation.RequestValidation;
        return SpeakOnceCore(
            config,
            request,
            log,
            validation,
            new VoicepeakUiController(config.Ui, config.Prepare, config.Debug, log),
            new AudioSessionReader(log));
    }

    // 依存を差し替えて単発実行
    internal static SpeakOnceResult SpeakOnceCore(
        AppConfig config,
        SpeakOnceRequest request,
        AppLogger log,
        RequestValidationMode validation,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        Job job;
        try
        {
            if (request == null)
            {
                throw new InvalidOperationException("request は null にできません");
            }

            SpeakRequest runtimeRequest = new SpeakRequest
            {
                Text = PauseTokenParser.StripTokens(request.Text),
                Mode = EnqueueMode.Queue,
                Interrupt = false
            };
            job = JobCompiler.Compile(runtimeRequest, config, validation);
        }
        catch (Exception ex)
        {
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.InvalidRequest,
                SegmentsExecuted = 0,
                ErrorMessage = ex.Message
            };
        }

        log.Info($"job_received jobId={job.JobId} mode={job.Mode} interrupt={job.Interrupt} source=oneshot");

        ResolveTargetResult resolved = ui.TryResolveTargetDetailed();
        if (!resolved.Success)
        {
            return BuildSpeakResolveTargetFailedResult(resolved, log);
        }

        Process process = resolved.Process;
        IntPtr hwnd = resolved.MainHwnd;

        if (!TryBeginModifierIsolationSession(ui, process.Id, "oneshot_speak_once", log))
        {
            log.Warn($"job_dropped jobId={job.JobId} reason=modifier_guard_unavailable_fatal");
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.ProcessLost,
                SegmentsExecuted = 0,
                ErrorMessage = ModifierGuardUnavailableFatalReason
            };
        }

        int executed = 0;
        SpeakOnceResult result;
        bool sessionEnded;
        try
        {
            for (int i = 0; i < job.Segments.Count; i++)
            {
                Segment seg = job.Segments[i];
                log.Info($"segment_start jobId={job.JobId} index={i}");

            long segmentStartAt = MonoClock.NowMs();
            bool prepared = JobExecutionCore.PrepareSegment(config, ui, process, hwnd, seg.Text, log, false);
            long readyAt = MonoClock.NowMs();
            if (!prepared)
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=prepare_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.PrepareFailed, SegmentsExecuted = executed };
                    goto SpeakOnceCore_Exit;
            }

            int adjustedPausePreMs = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, seg.PausePreMs, "pre", job.JobId, i, seg.Text, log);
            long scheduledAt = segmentStartAt + adjustedPausePreMs;
            long playAt = Math.Max(scheduledAt, readyAt);
            MonoClock.SleepUntil(playAt, () => false);

            if (!ui.IsAlive(process))
            {
                log.Error("対象プロセスが終了したため処理を中断しました。");
                log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                    goto SpeakOnceCore_Exit;
            }

            if (!ui.PrepareForPlayback(process, hwnd, config.Prepare.ActionDelayMs))
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=move_to_start_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.MoveToStartFailed, SegmentsExecuted = executed };
                    goto SpeakOnceCore_Exit;
            }

            if (!ui.PressPlay(hwnd))
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=play_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.PlayFailed, SegmentsExecuted = executed };
                    goto SpeakOnceCore_Exit;
            }

            log.Info($"play_pressed jobId={job.JobId} index={i}");
            StartConfirmResult startConfirm = MonitorStartConfirm(config, ui, audio, process, log);
            if (startConfirm == StartConfirmResult.Confirmed)
            {
                log.Info($"speak_start_confirmed jobId={job.JobId} index={i}");
                executed++;
                continue;
            }

            if (startConfirm == StartConfirmResult.Timeout)
            {
                log.Error("monitor_timeout reason=start_confirm");
                log.Warn($"job_dropped jobId={job.JobId} reason=start_confirm_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.StartConfirmTimeout, SegmentsExecuted = executed };
                    goto SpeakOnceCore_Exit;
            }

            log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                goto SpeakOnceCore_Exit;
            }

            result = new SpeakOnceResult
            {
                Status = SpeakOnceStatus.Completed,
                SegmentsExecuted = executed
            };
SpeakOnceCore_Exit:
            ;
        }
        finally
        {
            sessionEnded = TryEndModifierIsolationSession(ui, "oneshot_speak_once", log);
        }

        if (!sessionEnded)
        {
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.ProcessLost,
                SegmentsExecuted = executed,
                ErrorMessage = ModifierGuardReleaseFailedFatalReason
            };
        }

        return result;
    }

    // 単発実行
    public static SpeakOnceResult SpeakOnceWait(
        AppConfig config,
        SpeakOnceRequest request,
        IAppLogger logger = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger log = new AppLogger(logger ?? new ConsoleAppLogger());

        RequestValidationMode validation = config.Validation.RequestValidation;
        return SpeakOnceWaitCore(
            config,
            request,
            log,
            validation,
            new VoicepeakUiController(config.Ui, config.Prepare, config.Debug, log),
            new AudioSessionReader(log));
    }

    // 依存を差し替えて単発実行
    internal static SpeakOnceResult SpeakOnceWaitCore(
        AppConfig config,
        SpeakOnceRequest request,
        AppLogger log,
        RequestValidationMode validation,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        Job job;
        try
        {
            if (request == null)
            {
                throw new InvalidOperationException("request は null にできません");
            }

            SpeakRequest runtimeRequest = new SpeakRequest
            {
                Text = request.Text,
                Mode = EnqueueMode.Queue,
                Interrupt = false
            };
            job = JobCompiler.Compile(runtimeRequest, config, validation);
        }
        catch (Exception ex)
        {
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.InvalidRequest,
                SegmentsExecuted = 0,
                ErrorMessage = ex.Message
            };
        }

        log.Info($"job_received jobId={job.JobId} mode={job.Mode} interrupt={job.Interrupt} source=oneshot");

        ResolveTargetResult resolved = ui.TryResolveTargetDetailed();
        if (!resolved.Success)
        {
            return BuildSpeakResolveTargetFailedResult(resolved, log);
        }

        Process process = resolved.Process;
        IntPtr hwnd = resolved.MainHwnd;

        if (!TryBeginModifierIsolationSession(ui, process.Id, "oneshot_speak_wait", log))
        {
            log.Warn($"job_dropped jobId={job.JobId} reason=modifier_guard_unavailable_fatal");
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.ProcessLost,
                SegmentsExecuted = 0,
                ErrorMessage = ModifierGuardUnavailableFatalReason
            };
        }

        int executed = 0;
        SpeakOnceResult result;
        bool sessionEnded;
        try
        {
            for (int i = 0; i < job.Segments.Count; i++)
            {
                Segment seg = job.Segments[i];
                bool isLast = i == job.Segments.Count - 1;
                bool recoveryClickUsed = false;
                log.Info($"segment_start jobId={job.JobId} index={i}");

            long segmentStartAt = MonoClock.NowMs();
            bool prepared = JobExecutionCore.PrepareSegment(config, ui, process, hwnd, seg.Text, log, false);
            long readyAt = MonoClock.NowMs();
            if (!prepared)
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=prepare_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs, false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.PrepareFailed, SegmentsExecuted = executed };
                    goto SpeakOnceWaitCore_Exit;
            }

            int adjustedPausePreMs = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, seg.PausePreMs, "pre", job.JobId, i, seg.Text, log);
            long scheduledAt = segmentStartAt + adjustedPausePreMs;
            long playAt = Math.Max(scheduledAt, readyAt);
            MonoClock.SleepUntil(playAt, () => false);

            if (!ui.IsAlive(process))
            {
                log.Error("対象プロセスが終了したため処理を中断しました。");
                log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                    goto SpeakOnceWaitCore_Exit;
            }

            for (int startAttempt = 0; startAttempt <= config.Audio.StartConfirmMaxRetries; startAttempt++)
            {
                if (!ui.PrepareForPlayback(process, hwnd, config.Prepare.ActionDelayMs))
                {
                    log.Warn($"job_dropped jobId={job.JobId} reason=move_to_start_failed");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, false, killFocusAfterClear: false);
                        result = new SpeakOnceResult { Status = SpeakOnceStatus.MoveToStartFailed, SegmentsExecuted = executed };
                        goto SpeakOnceWaitCore_Exit;
                }

                if (!ui.PressPlay(hwnd))
                {
                    log.Warn($"job_dropped jobId={job.JobId} reason=play_failed");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, false, killFocusAfterClear: false);
                        result = new SpeakOnceResult { Status = SpeakOnceStatus.PlayFailed, SegmentsExecuted = executed };
                        goto SpeakOnceWaitCore_Exit;
                }

                log.Info($"play_pressed jobId={job.JobId} index={i}");
                SpeakMonitorResult speakResult = JobExecutionCore.MonitorSpeaking(
                    config,
                    ui,
                    audio,
                    process,
                    hwnd,
                    log,
                    () => false,
                    () => false,
                    null);
                if (speakResult.Kind == SpeakMonitorKind.Completed)
                {
                    log.Info($"speak_end_confirmed jobId={job.JobId} index={i}");
                    executed++;

                    int trailing = isLast
                        ? JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, job.TrailingPauseMs, "trailing", job.JobId, i, null, log)
                        : 0;
                    if (trailing > 0)
                    {
                        long waitUntil = speakResult.SegEndAtMs + trailing;
                        MonoClock.SleepUntil(waitUntil, () => false);
                    }

                    break;
                }

                if (speakResult.Kind == SpeakMonitorKind.StartTimeout)
                {
                    bool shouldRetry = JobExecutionCore.HandleStartTimeoutRetry(config, ui, process, hwnd, startAttempt, ref recoveryClickUsed);
                    if (shouldRetry)
                    {
                        log.Warn($"start_confirm_retry jobId={job.JobId} index={i} attempt={startAttempt + 1}");
                        continue;
                    }

                    log.Error("monitor_timeout reason=start_confirm");
                    log.Warn($"job_dropped jobId={job.JobId} reason=start_confirm_failed");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, false, killFocusAfterClear: false);
                        result = new SpeakOnceResult { Status = SpeakOnceStatus.StartConfirmTimeout, SegmentsExecuted = executed };
                        goto SpeakOnceWaitCore_Exit;
                }

                if (speakResult.Kind == SpeakMonitorKind.MaxDuration)
                {
                    log.Error("monitor_timeout reason=max_duration");
                    log.Warn($"job_dropped jobId={job.JobId} reason=max_speaking_duration");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, false, killFocusAfterClear: false);
                        result = new SpeakOnceResult { Status = SpeakOnceStatus.MaxSpeakingDurationExceeded, SegmentsExecuted = executed };
                        goto SpeakOnceWaitCore_Exit;
                }

                log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                    goto SpeakOnceWaitCore_Exit;
            }

                continue;
            }

            result = new SpeakOnceResult
            {
                Status = SpeakOnceStatus.Completed,
                SegmentsExecuted = executed
            };
SpeakOnceWaitCore_Exit:
            ;
        }
        finally
        {
            sessionEnded = TryEndModifierIsolationSession(ui, "oneshot_speak_wait", log);
        }

        if (!sessionEnded)
        {
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.ProcessLost,
                SegmentsExecuted = executed,
                ErrorMessage = ModifierGuardReleaseFailedFatalReason
            };
        }

        return result;
    }

    // 修飾キー中立化セッションを開始
    private static bool TryBeginModifierIsolationSession(IVoicepeakUiController ui, int processId, string operationName, AppLogger log)
    {
        if (ui.BeginModifierIsolationSession(processId, operationName))
        {
            return true;
        }

        log.Error($"modifier_guard_fatal phase=session_begin_failed op={operationName}");
        return false;
    }

    // 修飾キー中立化セッションを終了
    private static bool TryEndModifierIsolationSession(IVoicepeakUiController ui, string operationName, AppLogger log)
    {
        if (ui.EndModifierIsolationSession(operationName))
        {
            return true;
        }

        log.Error($"modifier_guard_fatal phase=session_end_failed op={operationName}");
        return false;
    }

    // 開始確認のみを監視
    private static StartConfirmResult MonitorStartConfirm(
        AppConfig config,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        Process process,
        AppLogger log)
    {
        long startDeadline = MonoClock.NowMs() + config.Audio.StartConfirmTimeoutMs;
        while (true)
        {
            if (!ui.IsAlive(process))
            {
                return StartConfirmResult.ProcessLost;
            }

            long now = MonoClock.NowMs();
            if (now > startDeadline)
            {
                return StartConfirmResult.Timeout;
            }

            AudioSessionSnapshot snap = audio.ReadPeak(process.Id);
            if (snap.Peak >= config.Audio.PeakThreshold)
            {
                log.Info("speak_start_confirmed");
                return StartConfirmResult.Confirmed;
            }

            Thread.Sleep(config.Audio.PollIntervalMs);
        }
    }

    // 対象解決失敗をSpeakOnce結果へ変換
    private static SpeakOnceResult BuildSpeakResolveTargetFailedResult(ResolveTargetResult resolved, AppLogger log)
    {
        if (resolved.FailureReason == ResolveTargetFailureReason.ProcessNotFound)
        {
            log.Error("voicepeak.exe が起動していません。");
            return new SpeakOnceResult { Status = SpeakOnceStatus.ProcessNotFound, SegmentsExecuted = 0 };
        }

        if (resolved.FailureReason == ResolveTargetFailureReason.MultipleProcesses)
        {
            log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {resolved.ProcessCount}）");
            return new SpeakOnceResult { Status = SpeakOnceStatus.MultipleProcesses, SegmentsExecuted = 0 };
        }

        log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
        return new SpeakOnceResult { Status = SpeakOnceStatus.TargetNotFound, SegmentsExecuted = 0 };
    }

    // 対象解決失敗をValidate結果へ変換
    private static ValidateInputOnceResult BuildValidateInputResolveTargetFailedResult(ResolveTargetResult resolved, AppLogger log)
    {
        if (resolved.FailureReason == ResolveTargetFailureReason.ProcessNotFound)
        {
            log.Error("voicepeak.exe が起動していません。");
            return new ValidateInputOnceResult { Status = ValidateInputOnceStatus.ProcessNotFound };
        }

        if (resolved.FailureReason == ResolveTargetFailureReason.MultipleProcesses)
        {
            log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {resolved.ProcessCount}）");
            return new ValidateInputOnceResult { Status = ValidateInputOnceStatus.MultipleProcesses };
        }

        log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
        return new ValidateInputOnceResult { Status = ValidateInputOnceStatus.TargetNotFound };
    }

    // 対象解決失敗をClearInput結果へ変換
    private static ClearInputOnceResult BuildClearInputResolveTargetFailedResult(ResolveTargetResult resolved, AppLogger log)
    {
        if (resolved.FailureReason == ResolveTargetFailureReason.ProcessNotFound)
        {
            log.Error("voicepeak.exe が起動していません。");
            return new ClearInputOnceResult { Status = ClearInputOnceStatus.ProcessNotFound };
        }

        if (resolved.FailureReason == ResolveTargetFailureReason.MultipleProcesses)
        {
            log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {resolved.ProcessCount}）");
            return new ClearInputOnceResult { Status = ClearInputOnceStatus.MultipleProcesses };
        }

        log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
        return new ClearInputOnceResult { Status = ClearInputOnceStatus.TargetNotFound };
    }

    private enum StartConfirmResult
    {
        Confirmed,
        Timeout,
        ProcessLost
    }

    // 入力検証失敗理由を公開ステータスへ変換
    private static ValidateInputOnceStatus MapValidateInputOnceStatus(string reason)
    {
        return reason switch
        {
            "process_not_alive" => ValidateInputOnceStatus.ProcessLost,
            "move_to_start_failed" => ValidateInputOnceStatus.PrepareFailed,
            "clear_input_failed" => ValidateInputOnceStatus.ClearInputFailed,
            "type_text_failed" => ValidateInputOnceStatus.TypeTextFailed,
            "read_input_failed" => ValidateInputOnceStatus.ReadInputFailed,
            "text_mismatch" => ValidateInputOnceStatus.TextMismatch,
            _ => ValidateInputOnceStatus.InvalidRequest
        };
    }

}
