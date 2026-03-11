using System;
using System.Diagnostics;
using System.Threading;

namespace VoicepeakProxyCore;

// 単発実行結果
public sealed class SpeakOnceResult
{
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int SegmentsExecuted { get; set; }
}

// 単発実行入力
public sealed class SpeakOnceRequest
{
    public string Text { get; set; }
}

// 単発実行APIの公開窓口
public static class VoicepeakOneShot
{
    // 入力検証を無効化して単発実行
    public static SpeakOnceResult SpeakOnceNoValidation(
        AppConfig config,
        SpeakOnceRequest request,
        IAppLogger logger = null)
    {
        return SpeakOnce(config, request, logger, RequestValidationMode.Disabled);
    }

    // 入力検証モードを指定して単発実行
    public static SpeakOnceResult SpeakOnce(
        AppConfig config,
        SpeakOnceRequest request,
        IAppLogger logger = null,
        RequestValidationMode? validationOverride = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger log = new AppLogger(logger ?? new ConsoleAppLogger());

        RequestValidationMode validation = validationOverride ?? config.Validation.RequestValidation;
        return SpeakOnceCore(
            config,
            request,
            log,
            validation,
            new VoicepeakUiController(config.Ui, config.Debug, log),
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
                text = request.Text,
                mode = "queue",
                interrupt = false
            };
            job = JobCompiler.Compile(runtimeRequest, config, validation);
        }
        catch (Exception ex)
        {
            return new SpeakOnceResult
            {
                Success = false,
                Reason = "invalid_request:" + ex.Message,
                SegmentsExecuted = 0
            };
        }

        log.Info($"job_received jobId={job.JobId} mode={job.Mode} interrupt={job.Interrupt} source=oneshot");

        int processCount = ui.GetVoicepeakProcessCount();
        if (processCount <= 0)
        {
            log.Error("voicepeak.exe が起動していません。");
            return new SpeakOnceResult { Success = false, Reason = "process_not_found", SegmentsExecuted = 0 };
        }

        if (processCount > 1)
        {
            log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {processCount}）");
            return new SpeakOnceResult { Success = false, Reason = "multiple_processes", SegmentsExecuted = 0 };
        }

        if (!ui.TryResolveTarget(out Process process, out IntPtr hwnd))
        {
            log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
            return new SpeakOnceResult { Success = false, Reason = "target_not_found", SegmentsExecuted = 0 };
        }

        int executed = 0;
        for (int i = 0; i < job.Segments.Count; i++)
        {
            Segment seg = job.Segments[i];
            bool isLast = i == job.Segments.Count - 1;
            log.Info($"segment_start jobId={job.JobId} index={i}");

            long segmentStartAt = MonoClock.NowMs();
            bool prepared = JobExecutionCore.PrepareSegment(config, ui, process, hwnd, seg.Text, log);
            long readyAt = MonoClock.NowMs();
            if (!prepared)
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=prepare_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs);
                return new SpeakOnceResult { Success = false, Reason = "prepare_failed", SegmentsExecuted = executed };
            }

            int adjustedPausePreMs = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, seg.PausePreMs, "pre", job.JobId, i, seg.Text, log);
            long scheduledAt = segmentStartAt + adjustedPausePreMs;
            long playAt = Math.Max(scheduledAt, readyAt);
            MonoClock.SleepUntil(playAt, () => false);

            if (!ui.IsAlive(process))
            {
                log.Error("対象プロセスが終了したため処理を中断しました。");
                log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
                return new SpeakOnceResult { Success = false, Reason = "process_lost", SegmentsExecuted = executed };
            }

            if (!ui.MoveToStart(hwnd, config.Prepare.ActionDelayMs))
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=move_to_start_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs);
                return new SpeakOnceResult { Success = false, Reason = "move_to_start_failed", SegmentsExecuted = executed };
            }

            if (!ui.PressPlay(hwnd))
            {
                log.Warn($"job_dropped jobId={job.JobId} reason=play_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs);
                return new SpeakOnceResult { Success = false, Reason = "play_failed", SegmentsExecuted = executed };
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

                continue;
            }

            if (speakResult.Kind == SpeakMonitorKind.StartTimeout)
            {
                log.Error("monitor_timeout reason=start_confirm");
                log.Warn($"job_dropped jobId={job.JobId} reason=start_confirm_failed");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs);
                return new SpeakOnceResult { Success = false, Reason = "start_confirm_failed", SegmentsExecuted = executed };
            }

            if (speakResult.Kind == SpeakMonitorKind.MaxDuration)
            {
                log.Error("monitor_timeout reason=max_duration");
                log.Warn($"job_dropped jobId={job.JobId} reason=max_speaking_duration");
                ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs);
                return new SpeakOnceResult { Success = false, Reason = "max_speaking_duration", SegmentsExecuted = executed };
            }

            log.Warn($"job_dropped jobId={job.JobId} reason=process_lost");
            return new SpeakOnceResult { Success = false, Reason = "process_lost", SegmentsExecuted = executed };
        }

        return new SpeakOnceResult
        {
            Success = true,
            Reason = "completed",
            SegmentsExecuted = executed
        };
    }

}
