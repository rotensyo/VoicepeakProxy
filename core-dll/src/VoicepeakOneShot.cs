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

    // 公開API初期化時に依存解決を有効化
    static VoicepeakOneShot()
    {
        DependencyResolver.EnsureInitialized();
    }

    // 単発セッションを開始
    public static VoicepeakOneShotSession Start(AppConfig config, IAppLogger logger = null)
    {
        AppLogger log = InitializeApiCall(config, logger);
        return new VoicepeakOneShotSession(config, log);
    }

    // 依存を差し替えて単発入力検証
    internal static ValidateInputOnceResult ValidateInputOnceCore(
        AppConfig config,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        string targetText = config.Validation.ValidationText ?? string.Empty;

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
            BootValidationRunResult run = JobExecutionCore.RunBootValidationFlow(
                config,
                ui,
                audio,
                process,
                hwnd,
                log,
                targetText);
            result = BuildValidateInputResult(run);
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
                if (!JobExecutionCore.TryClearInputWithRetry(config, ui, process, hwnd, log, "oneshot_clear"))
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

    // 依存を差し替えて単発実行
    internal static SpeakOnceResult SpeakOnceCore(
        AppConfig config,
        SpeakOnceRequest request,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        return ExecuteSpeakCore(
            config,
            request,
            log,
            ui,
            audio,
            waitForCompletion: false,
            stripPauseTokens: true,
            operationName: "oneshot_speak_once");
    }

    // 依存を差し替えて単発実行
    internal static SpeakOnceResult SpeakOnceWaitCore(
        AppConfig config,
        SpeakOnceRequest request,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio)
    {
        return ExecuteSpeakCore(
            config,
            request,
            log,
            ui,
            audio,
            waitForCompletion: true,
            stripPauseTokens: false,
            operationName: "oneshot_speak_wait");
    }

    // 公開API呼び出しの共通初期化
    internal static AppLogger InitializeApiCall(AppConfig config, IAppLogger logger)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        return new AppLogger(logger ?? new ConsoleAppLogger(), config.Debug.LogMinimumLevel);
    }

    // 単発実行の共通本体
    private static SpeakOnceResult ExecuteSpeakCore(
        AppConfig config,
        SpeakOnceRequest request,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        bool waitForCompletion,
        bool stripPauseTokens,
        string operationName)
    {
        Job job;
        if (!TryCompileSpeakJob(request, config, stripPauseTokens, out job, out SpeakOnceResult invalidRequestResult))
        {
            return invalidRequestResult;
        }

        log.Info($"job_received jobId={job.JobId} mode={job.Mode} interrupt={job.Interrupt} source=oneshot");

        if (waitForCompletion && job.IsDelayOnly)
        {
            log.Info($"delay_only_start jobId={job.JobId} delayMs={job.TrailingPauseMs} source=oneshot");
            long waitUntil = MonoClock.NowMs() + job.TrailingPauseMs;
            MonoClock.SleepUntil(waitUntil, () => false);
            log.Info($"delay_only_end jobId={job.JobId} source=oneshot");
            return new SpeakOnceResult
            {
                Status = SpeakOnceStatus.Completed,
                SegmentsExecuted = 0
            };
        }

        ResolveTargetResult resolved = ui.TryResolveTargetDetailed();
        if (!resolved.Success)
        {
            return BuildSpeakResolveTargetFailedResult(resolved, log);
        }

        Process process = resolved.Process;
        IntPtr hwnd = resolved.MainHwnd;

        if (!TryBeginModifierIsolationSession(ui, process.Id, operationName, log))
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
        SpeakOnceResult result = null;
        bool sessionEnded;
        try
        {
            for (int i = 0; i < job.Segments.Count; i++)
            {
                Segment seg = job.Segments[i];
                bool isLast = i == job.Segments.Count - 1;
                log.Info($"segment_start jobId={job.JobId} index={i}");

                if (!TryPrepareSpeakSegment(config, ui, process, hwnd, seg, log, job.JobId, i))
                {
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.PrepareFailed, SegmentsExecuted = executed };
                    break;
                }

                if (!ui.IsAlive(process))
                {
                    log.Error("対象プロセスが終了したため処理を中断しました。");
                    log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                    break;
                }

                if (waitForCompletion)
                {
                    StartConfirmLoopResult loopResult = JobExecutionCore.RunStartConfirmLoop(
                        config,
                        ui,
                        audio,
                        process,
                        hwnd,
                        log,
                        () => false,
                        () => false,
                        null,
                        attempt => log.Warn($"start_confirm_retry jobId={job.JobId} index={i} attempt={attempt}"),
                        () => log.Info($"play_pressed jobId={job.JobId} index={i}"));
                    if (loopResult.Kind == StartConfirmLoopKind.Completed)
                    {
                        log.Info($"speak_end_confirmed jobId={job.JobId} index={i}");
                        executed++;

                        int trailing = isLast
                            ? JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, job.TrailingPauseMs, "trailing", i)
                            : 0;
                        if (trailing > 0)
                        {
                            long waitUntil = loopResult.SegEndAtMs + trailing;
                            MonoClock.SleepUntil(waitUntil, () => false);
                        }

                        continue;
                    }

                    result = BuildSpeakWaitFailureResult(config, ui, process, hwnd, loopResult, executed, job.JobId, log);
                    break;
                }

                if (!ui.PrepareForPlayback(process, hwnd, config.InputTiming.ActionDelayMs))
                {
                    log.Warn($"job_dropped jobId={job.JobId} reason=move_to_start_failed");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, killFocusAfterClear: false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.MoveToStartFailed, SegmentsExecuted = executed };
                    break;
                }

                if (!ui.PressPlay(hwnd))
                {
                    log.Warn($"job_dropped jobId={job.JobId} reason=play_failed");
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, killFocusAfterClear: false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.PlayFailed, SegmentsExecuted = executed };
                    break;
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
                    JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, killFocusAfterClear: false);
                    result = new SpeakOnceResult { Status = SpeakOnceStatus.StartConfirmTimeout, SegmentsExecuted = executed };
                    break;
                }

                log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                result = new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
                break;
            }

            result ??= new SpeakOnceResult
            {
                Status = SpeakOnceStatus.Completed,
                SegmentsExecuted = executed
            };
        }
        finally
        {
            sessionEnded = TryEndModifierIsolationSession(ui, operationName, log);
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

    // 単発実行のジョブ生成を共通化
    private static bool TryCompileSpeakJob(
        SpeakOnceRequest request,
        AppConfig config,
        bool stripPauseTokens,
        out Job job,
        out SpeakOnceResult invalidRequestResult)
    {
        job = null;
        invalidRequestResult = null;
        try
        {
            if (request == null)
            {
                throw new InvalidOperationException("request は null にできません");
            }

            SpeakRequest runtimeRequest = new SpeakRequest
            {
                Text = stripPauseTokens ? PauseTokenParser.StripTokens(request.Text) : request.Text,
                Mode = EnqueueMode.Queue,
                Interrupt = false
            };
            job = JobCompiler.Compile(runtimeRequest, config);
            return true;
        }
        catch (Exception ex)
        {
            invalidRequestResult = new SpeakOnceResult
            {
                Status = SpeakOnceStatus.InvalidRequest,
                SegmentsExecuted = 0,
                ErrorMessage = ex.Message
            };
            return false;
        }
    }

    // セグメント準備と事前待機を共通化
    private static bool TryPrepareSpeakSegment(
        AppConfig config,
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        Segment segment,
        AppLogger log,
        string jobId,
        int segmentIndex)
    {
        long segmentStartAt = MonoClock.NowMs();
        bool prepared = JobExecutionCore.PrepareSegment(config, ui, process, hwnd, segment.Text, log);
        long readyAt = MonoClock.NowMs();
        if (!prepared)
        {
            log.Warn($"job_dropped jobId={jobId} reason=prepare_failed");
            JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, killFocusAfterClear: false);
            return false;
        }

        int adjustedPausePreMs = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, segment.PausePreMs, "pre", segmentIndex);
        long scheduledAt = segmentStartAt + adjustedPausePreMs;
        if (segment.PausePreMs > 0 && readyAt > scheduledAt)
        {
            long lagMs = readyAt - scheduledAt;
            log.Warn($"pause_overridden_by_ready_at jobId={jobId} index={segmentIndex} scheduledAt={scheduledAt} readyAt={readyAt} lagMs={lagMs}");
        }
        long playAt = Math.Max(scheduledAt, readyAt);
        MonoClock.SleepUntil(playAt, () => false);
        return true;
    }

    // 待機付き単発実行の失敗結果を構築
    private static SpeakOnceResult BuildSpeakWaitFailureResult(
        AppConfig config,
        IVoicepeakUiController ui,
        Process process,
        IntPtr hwnd,
        StartConfirmLoopResult loopResult,
        int executed,
        string jobId,
        AppLogger log)
    {
        StartConfirmFailureDetail failure = JobExecutionCore.ClassifyStartConfirmFailure(loopResult);
        if (failure.Kind == StartConfirmFailureKind.None)
        {
            return new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed };
        }

        if (!string.IsNullOrEmpty(failure.MonitorTimeoutReason))
        {
            log.Error($"monitor_timeout reason={failure.MonitorTimeoutReason}");
        }

        log.Warn($"job_dropped jobId={jobId} reason={failure.DropReason}");
        if (failure.RequiresFinalizeInput)
        {
            JobExecutionCore.FinalizeJobInput(config, ui, process, hwnd, killFocusAfterClear: false);
        }

        return failure.Kind switch
        {
            StartConfirmFailureKind.MoveToStartFailed => new SpeakOnceResult { Status = SpeakOnceStatus.MoveToStartFailed, SegmentsExecuted = executed },
            StartConfirmFailureKind.PlayFailed => new SpeakOnceResult { Status = SpeakOnceStatus.PlayFailed, SegmentsExecuted = executed },
            StartConfirmFailureKind.StartConfirmTimeout => new SpeakOnceResult { Status = SpeakOnceStatus.StartConfirmTimeout, SegmentsExecuted = executed },
            StartConfirmFailureKind.MaxDuration => new SpeakOnceResult { Status = SpeakOnceStatus.MaxSpeakingDurationExceeded, SegmentsExecuted = executed },
            _ => new SpeakOnceResult { Status = SpeakOnceStatus.ProcessLost, SegmentsExecuted = executed }
        };
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
        ResolveTargetFailureReason reason = NormalizeAndLogResolveTargetFailure(resolved, log);
        return new SpeakOnceResult
        {
            Status = MapSpeakResolveTargetFailureStatus(reason),
            SegmentsExecuted = 0
        };
    }

    // 対象解決失敗をValidate結果へ変換
    private static ValidateInputOnceResult BuildValidateInputResolveTargetFailedResult(ResolveTargetResult resolved, AppLogger log)
    {
        ResolveTargetFailureReason reason = NormalizeAndLogResolveTargetFailure(resolved, log);
        return new ValidateInputOnceResult
        {
            Status = MapValidateResolveTargetFailureStatus(reason)
        };
    }

    // 対象解決失敗をClearInput結果へ変換
    private static ClearInputOnceResult BuildClearInputResolveTargetFailedResult(ResolveTargetResult resolved, AppLogger log)
    {
        ResolveTargetFailureReason reason = NormalizeAndLogResolveTargetFailure(resolved, log);
        return new ClearInputOnceResult
        {
            Status = MapClearResolveTargetFailureStatus(reason)
        };
    }

    // 対象解決失敗理由をSpeak結果へ変換
    private static SpeakOnceStatus MapSpeakResolveTargetFailureStatus(ResolveTargetFailureReason reason)
    {
        return MapResolveTargetFailureStatus(reason, SpeakOnceStatus.ProcessNotFound, SpeakOnceStatus.MultipleProcesses, SpeakOnceStatus.TargetNotFound);
    }

    // 対象解決失敗理由をValidate結果へ変換
    private static ValidateInputOnceStatus MapValidateResolveTargetFailureStatus(ResolveTargetFailureReason reason)
    {
        return MapResolveTargetFailureStatus(reason, ValidateInputOnceStatus.ProcessNotFound, ValidateInputOnceStatus.MultipleProcesses, ValidateInputOnceStatus.TargetNotFound);
    }

    // 対象解決失敗理由をClear結果へ変換
    private static ClearInputOnceStatus MapClearResolveTargetFailureStatus(ResolveTargetFailureReason reason)
    {
        return MapResolveTargetFailureStatus(reason, ClearInputOnceStatus.ProcessNotFound, ClearInputOnceStatus.MultipleProcesses, ClearInputOnceStatus.TargetNotFound);
    }

    // 起動時検証結果を単発入力検証結果へ変換
    private static ValidateInputOnceResult BuildValidateInputResult(BootValidationRunResult run)
    {
        return run.Kind switch
        {
            BootValidationRunKind.Completed => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.Completed,
                ActualText = run.InputValidate.ActualText
            },
            BootValidationRunKind.InputValidationFailed => new ValidateInputOnceResult
            {
                Status = MapValidateInputOnceStatus(run.InputValidate.Reason),
                ErrorMessage = $"reason={run.InputValidate.Reason} cause={run.InputValidate.Cause}",
                ActualText = run.InputValidate.ActualText
            },
            BootValidationRunKind.MoveToStartFailed => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.MoveToStartFailed,
                ErrorMessage = "reason=move_to_start_failed"
            },
            BootValidationRunKind.PlayFailed => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.PlayFailed,
                ErrorMessage = "reason=play_failed"
            },
            BootValidationRunKind.StartConfirmTimeout => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.StartConfirmTimeout,
                ErrorMessage = "reason=start_confirm_failed"
            },
            BootValidationRunKind.MaxDuration => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.MaxSpeakingDurationExceeded,
                ErrorMessage = "reason=max_speaking_duration"
            },
            BootValidationRunKind.ProcessLost => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.ProcessLost,
                ErrorMessage = "reason=process_lost"
            },
            _ => new ValidateInputOnceResult
            {
                Status = ValidateInputOnceStatus.InvalidRequest,
                ErrorMessage = "reason=unknown"
            }
        };
    }

    // 対象解決失敗の理由を統一的に正規化して記録
    private static ResolveTargetFailureReason NormalizeAndLogResolveTargetFailure(ResolveTargetResult resolved, AppLogger log)
    {
        ResolveTargetFailureReason reason = ResolveTargetFailureMapper.NormalizeReason(resolved);
        ResolveTargetFailureMapper.LogFailure(log, reason, resolved?.ProcessCount ?? 0);
        return reason;
    }

    // 対象解決失敗理由を各結果ステータスへ共通変換
    private static TStatus MapResolveTargetFailureStatus<TStatus>(ResolveTargetFailureReason reason, TStatus processNotFound, TStatus multipleProcesses, TStatus targetNotFound)
        where TStatus : struct
    {
        return reason switch
        {
            ResolveTargetFailureReason.ProcessNotFound => processNotFound,
            ResolveTargetFailureReason.MultipleProcesses => multipleProcesses,
            _ => targetNotFound
        };
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

// 単発実行セッション
public sealed class VoicepeakOneShotSession : IDisposable
{
    private readonly AppConfig _config;
    private readonly AppLogger _log;
    private readonly UiaProcessHost _uiaHost;
    private readonly VoicepeakUiController _ui;
    private readonly AudioSessionReader _audio;
    private bool _disposed;

    // セッションを初期化
    internal VoicepeakOneShotSession(AppConfig config, AppLogger log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _uiaHost = new UiaProcessHost(_config.Debug.UiaProbeRecycleIntervalSec, _log);
        _ui = new VoicepeakUiController(
            _config.Ui,
            _config.InputTiming,
            _config.Hook,
            _config.Text,
            _config.Debug,
            _log,
            processApi: null,
            uiaProcessHost: _uiaHost,
            ownsUiaProcessHost: false);
        _audio = new AudioSessionReader(_log);
    }

    // 入力検証を実行
    public ValidateInputOnceResult ValidateInputOnce()
    {
        ThrowIfDisposed();
        return VoicepeakOneShot.ValidateInputOnceCore(_config, _log, _ui, _audio);
    }

    // 入力欄クリアを実行
    public ClearInputOnceResult ClearInputOnce()
    {
        ThrowIfDisposed();
        return VoicepeakOneShot.ClearInputOnceCore(_config, _log, _ui);
    }

    // 開始確認までの単発実行
    public SpeakOnceResult SpeakOnce(SpeakOnceRequest request)
    {
        ThrowIfDisposed();
        return VoicepeakOneShot.SpeakOnceCore(_config, request, _log, _ui, _audio);
    }

    // 終了確認までの単発実行
    public SpeakOnceResult SpeakOnceWait(SpeakOnceRequest request)
    {
        ThrowIfDisposed();
        return VoicepeakOneShot.SpeakOnceWaitCore(_config, request, _log, _ui, _audio);
    }

    // リソースを解放
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _audio.Dispose();
        _ui.Dispose();
        _uiaHost.Dispose();
    }

    // 破棄済みを検証
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VoicepeakOneShotSession));
        }
    }
}
