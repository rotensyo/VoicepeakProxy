using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace VoicepeakProxyCore;

// ワーカースレッドの内部状態
internal enum WorkerState
{
    Idle,
    ExecutingInputValidate,
    ExecutingPrePlayWait,
    Speaking,
    Stopping
}

// 常駐実行の内部エンジン
internal sealed class VoicepeakEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly CancellationTokenSource _appCts;
    private readonly object _gate = new object();
    private readonly LinkedList<Job> _queue = new LinkedList<Job>();
    private readonly AutoResetEvent _wake = new AutoResetEvent(false);
    private readonly IVoicepeakUiController _ui;
    private readonly IAudioSessionReader _audio;
    private readonly Thread _worker;
    private readonly AppLogger _log;

    private volatile bool _stopping;
    private volatile bool _shutdownRequested;
    private volatile bool _interruptRequested;
    private WorkerState _state = WorkerState.Idle;

    // バックグラウンドワーカーを開始
    public VoicepeakEngine(AppConfig config, CancellationTokenSource appCts, AppLogger log)
        : this(config, appCts, log, null, null, true)
    {
    }

    internal VoicepeakEngine(
        AppConfig config,
        CancellationTokenSource appCts,
        AppLogger log,
        IVoicepeakUiController ui,
        IAudioSessionReader audio,
        bool startWorker)
    {
        _config = config;
        _appCts = appCts;
        _log = log;
        _ui = ui ?? new VoicepeakUiController(config.Ui, config.InputTiming, config.Startup, config.Hook, config.Text, config.Debug, _log);
        _audio = audio ?? new AudioSessionReader(_log);
        _worker = null;
        if (startWorker)
        {
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "voicepeak-worker"
            };
            _worker.Start();
        }
    }

    public bool IsShutdownRequested => _shutdownRequested;

    // 起動時の入力再生検証を実行
    public bool BootValidate(BootValidationMode mode)
    {
        if (mode == BootValidationMode.Disabled)
        {
            _log.Info("boot_validation_skipped mode=disabled");
            return true;
        }

        int processCount = _ui.GetVoicepeakProcessCount();
        if (processCount > 1)
        {
            _log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {processCount}）");
            return false;
        }

        if (!TryResolveTarget(out Process process, out IntPtr hwnd))
        {
            LogTargetResolveFailure();
            _shutdownRequested = true;
            return mode == BootValidationMode.Optional;
        }

        if (_ui.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.Validation))
        {
            _ui.TryPrimeInputContext(process, hwnd, InputContextPrimeReason.Validation);
        }

        BootValidationRunResult result = JobExecutionCore.RunBootValidationFlow(
            _config,
            _ui,
            _audio,
            process,
            hwnd,
            _log,
            _config.Startup.BootValidationText);
        if (result.Kind == BootValidationRunKind.Completed)
        {
            return true;
        }

        if (result.Kind == BootValidationRunKind.ProcessLost)
        {
            OnProcessLost();
        }

        return mode == BootValidationMode.Optional;
    }

    // リクエストを受理してキューへ投入
    public EnqueueResult Enqueue(SpeakRequest req)
    {
        if (req == null)
        {
            throw new ArgumentNullException(nameof(req));
        }

        if (_stopping || _appCts.IsCancellationRequested || _shutdownRequested)
        {
            throw new InvalidOperationException("Runtime is stopping and cannot accept new requests.");
        }

        RequestValidationMode mode = _config.Validation.RequestValidation;
        try
        {
            Job job = JobCompiler.Compile(req, _config, mode);
            lock (_gate)
            {
                switch (job.Mode)
                {
                    case JobMode.Queue:
                        if (_queue.Count >= _config.Queue.MaxQueuedJobs)
                        {
                            return new EnqueueResult
                            {
                                Status = EnqueueStatus.QueueFull,
                                ErrorMessage = "キューが上限に達しています"
                            };
                        }

                        _queue.AddLast(job);
                        job.Interrupt = false;
                        break;

                    case JobMode.Next:
                        _queue.AddFirst(job);
                        break;

                    case JobMode.Flush:
                        _queue.Clear();
                        _queue.AddFirst(job);
                        break;
                }

                if (job.Interrupt)
                {
                    _interruptRequested = true;
                }
            }

            _log.Info($"job_received jobId={job.JobId} mode={job.Mode} interrupt={job.Interrupt}");
            _wake.Set();
            return new EnqueueResult { Status = EnqueueStatus.Accepted, JobId = job.JobId };
        }
        catch (Exception ex)
        {
            return new EnqueueResult
            {
                Status = EnqueueStatus.InvalidRequest,
                ErrorMessage = ex.Message
            };
        }
    }

    // ワーカースレッドを停止
    public void Stop()
    {
        _stopping = true;
        _wake.Set();
        _worker?.Join(2000);
    }

    // エンジン資源を破棄
    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }

    // キューを監視してジョブを実行
    private void WorkerLoop()
    {
        while (!_stopping && !_appCts.IsCancellationRequested)
        {
            Job job = null;
            lock (_gate)
            {
                if (_state == WorkerState.Idle && _queue.Count > 0)
                {
                    job = _queue.First.Value;
                    _queue.RemoveFirst();
                }
            }

            if (job == null)
            {
                _wake.WaitOne(100);
                continue;
            }

            ExecuteJob(job);
            lock (_gate)
            {
                _state = WorkerState.Idle;
            }
        }
    }

    // 1ジョブを順次実行
    private void ExecuteJob(Job job)
    {
        if (!TryResolveTarget(out Process process, out IntPtr hwnd))
        {
            LogTargetResolveFailure();
            OnProcessLost();
            DropJob(job, "process_lost");
            return;
        }

        // 対象プロセスの修飾キーを中立化して入力時の誤爆を防止
        if (!_ui.BeginModifierIsolationSession(process.Id, "runtime_job"))
        {
            DropJob(job, "modifier_guard_unavailable_fatal");
            OnModifierGuardFatal("session_begin_failed");
            return;
        }

        try
        {

            for (int i = 0; i < job.Segments.Count; i++)
            {
                Segment seg = job.Segments[i];
                bool isLast = i == job.Segments.Count - 1;
                _log.Info($"segment_start jobId={job.JobId} index={i}");

            lock (_gate)
            {
                _state = WorkerState.ExecutingInputValidate;
            }

            long segmentStartAt = MonoClock.NowMs();
            bool prepared = JobExecutionCore.PrepareSegment(_config, _ui, process, hwnd, seg.Text, _log, true);
            long readyAt = MonoClock.NowMs();

            if (!prepared)
            {
                DropJob(job, "prepare_failed");
                JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: false);
                return;
            }

            if (ConsumeInterruptIfAny(log: true))
            {
                _log.Info("interrupt_applied state=ExecutingInputValidate");
                DropJob(job, "interrupt");
                return;
            }

            lock (_gate)
            {
                _state = WorkerState.ExecutingPrePlayWait;
            }

            int adjustedPausePreMs = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(_config, seg.PausePreMs, "pre", job.JobId, i, seg.Text, _log);
            long scheduledAt = segmentStartAt + adjustedPausePreMs;
            long playAt = Math.Max(scheduledAt, readyAt);
            MonoClock.SleepUntil(playAt, () => _stopping || _appCts.IsCancellationRequested || _interruptRequested);
            if (_interruptRequested)
            {
                ConsumeInterruptIfAny(log: false);
                _log.Info("interrupt_applied state=ExecutingPrePlayWait");
                DropJob(job, "interrupt");
                return;
            }

            if (!IsProcessAlive(process))
            {
                OnProcessLost();
                DropJob(job, "process_lost");
                return;
            }

            StartConfirmLoopResult loopResult = JobExecutionCore.RunStartConfirmLoop(
                _config,
                _ui,
                _audio,
                process,
                hwnd,
                _log,
                () => _stopping || _appCts.IsCancellationRequested,
                () => _interruptRequested,
                () => ConsumeInterruptIfAny(log: false),
                attempt => _log.Warn($"start_confirm_retry jobId={job.JobId} index={i} attempt={attempt}"),
                () =>
                {
                    _log.Info($"play_pressed jobId={job.JobId} index={i}");
                    lock (_gate)
                    {
                        _state = WorkerState.Speaking;
                    }
                });
            if (loopResult.Kind == StartConfirmLoopKind.Completed)
            {
                _log.Info($"speak_end_confirmed jobId={job.JobId} index={i}");
                lock (_gate)
                {
                    _state = WorkerState.Stopping;
                }

                int trailing = isLast
                    ? JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(_config, job.TrailingPauseMs, "trailing", job.JobId, i, null, _log)
                    : 0;
                if (trailing > 0)
                {
                    long waitUntil = loopResult.SegEndAtMs + trailing;
                    MonoClock.SleepUntil(waitUntil, () => _stopping || _appCts.IsCancellationRequested || _interruptRequested);
                }

                if (_interruptRequested)
                {
                    ConsumeInterruptIfAny(log: false);
                    _log.Info("interrupt_applied state=Stopping");
                    DropJob(job, "interrupt");
                    return;
                }

                continue;
            }

            if (loopResult.Kind == StartConfirmLoopKind.Interrupted)
            {
                _log.Info("interrupt_applied state=Speaking");
                DropJob(job, "interrupt");
                return;
            }

            if (loopResult.Kind == StartConfirmLoopKind.MoveToStartFailed)
            {
                DropJob(job, "move_to_start_failed");
                JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: false);
                return;
            }

            if (loopResult.Kind == StartConfirmLoopKind.PlayFailed)
            {
                DropJob(job, "play_failed");
                JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: false);
                return;
            }

            if (loopResult.Kind == StartConfirmLoopKind.StartConfirmTimeout)
            {
                _log.Error("monitor_timeout reason=start_confirm");
                DropJob(job, "start_confirm_failed");
                JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: false);
                return;
            }

            if (loopResult.Kind == StartConfirmLoopKind.MaxDuration)
            {
                _log.Error("monitor_timeout reason=max_duration");
                DropJob(job, "max_speaking_duration");
                JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: false);
                return;
            }

            OnProcessLost();
            DropJob(job, "process_lost");
            return;
            }

            JobExecutionCore.FinalizeJobInput(_config, _ui, process, hwnd, true, killFocusAfterClear: true);
        }
        finally
        {
            // 処理完了後に対象プロセスの修飾キーを有効化
            if (!_ui.EndModifierIsolationSession("runtime_job"))
            {
                OnModifierGuardFatal("session_end_failed");
            }
        }
    }


    private InputValidateResult RunInputValidate(Process process, IntPtr hwnd, string text, int charDelay, bool useProbeGuardChars)
    {
        return JobExecutionCore.ValidateInputText(_config, _ui, process, hwnd, text, charDelay, useProbeGuardChars);
    }

    internal static int ComputePostTypeWaitMs(string text, int perCharMs, int minMs)
    {
        return JobExecutionCore.ComputePostTypeWaitMs(text, perCharMs, minMs);
    }

    internal static string BuildInputMismatchCause(string expected, string actual, bool usedProbeGuardChars)
    {
        return JobExecutionCore.BuildInputMismatchCause(expected, actual, usedProbeGuardChars);
    }

    private bool TryResolveTarget(out Process process, out IntPtr hwnd)
    {
        return _ui.TryResolveTarget(out process, out hwnd);
    }

    private bool IsProcessAlive(Process process) => _ui.IsAlive(process);

    private bool ConsumeInterruptIfAny(bool log)
    {
        bool hit = false;
        lock (_gate)
        {
            if (_interruptRequested)
            {
                hit = true;
                _interruptRequested = false;
            }
        }

        if (hit && log)
        {
            _log.Info("interrupt_applied");
        }

        return hit;
    }

    private void DropJob(Job job, string reason)
    {
        _log.Warn($"job_dropped jobId={job.JobId} reason={reason}");
    }

    private void OnProcessLost()
    {
        OnFatalRuntimeError("process_lost", "対象プロセスが終了したため処理を中断しました。");
    }

    // 修飾キー中立化セッション異常で停止
    private void OnModifierGuardFatal(string phase)
    {
        _log.Error($"modifier_guard_fatal phase={phase}");
        OnFatalRuntimeError("modifier_guard_unavailable_fatal", "修飾キー中立化セッションが利用できないため処理を中断しました。");
    }

    // 致命エラーで常駐処理を停止
    private void OnFatalRuntimeError(string reason, string message)
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _log.Error($"runtime_fatal reason={reason}");
        _log.Error(message);
        _appCts.Cancel();
    }

    private void LogTargetResolveFailure()
    {
        int processCount = _ui.GetVoicepeakProcessCount();
        ResolveTargetFailureReason reason = processCount <= 0
            ? ResolveTargetFailureReason.ProcessNotFound
            : processCount > 1
                ? ResolveTargetFailureReason.MultipleProcesses
                : ResolveTargetFailureReason.TargetNotFound;
        ResolveTargetFailureMapper.LogFailure(_log, reason, processCount);
    }

    private static string EscapeForJson(string s)
    {
        return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

}
