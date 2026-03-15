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
    private int _preferredVoicepeakPid;

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
        _ui = ui ?? new VoicepeakUiController(config.Ui, config.Prepare, config.Debug, _log);
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

        string target = _config.Prepare.BootValidationText ?? string.Empty;
        InputValidateResult bootValidate = InputValidateResult.Fail("unknown", "unknown", string.Empty);
        bool bootInputOk = false;
        int charDelay = _config.Prepare.CharDelayBaseMs;

        for (int attempt = 0; attempt <= _config.Prepare.BootValidationMaxRetries; attempt++)
        {
            bootValidate = RunInputValidate(process, hwnd, target, charDelay, useProbeGuardChars: true);
            if (bootValidate.Success)
            {
                bootInputOk = true;
                break;
            }

            _log.Warn(
                "boot_validation_retry_failed " +
                $"attempt={attempt} reason={bootValidate.Reason} cause={bootValidate.Cause} " +
                $"expected=\"{SanitizeForLog(target)}\" actual=\"{SanitizeForLog(bootValidate.ActualText)}\"");

            bool hasNextAttempt = attempt < _config.Prepare.BootValidationMaxRetries;
            if (hasNextAttempt && _config.Prepare.BootValidationRetryIntervalMs > 0)
            {
                Thread.Sleep(_config.Prepare.BootValidationRetryIntervalMs);
            }
        }

        if (!bootInputOk)
        {
            _log.Error("boot_validation_fail " +
                $"stage=input_validate reason={bootValidate.Reason} cause={bootValidate.Cause} " +
                $"expected=\"{SanitizeForLog(target)}\" actual=\"{SanitizeForLog(bootValidate.ActualText)}\"");
            return mode == BootValidationMode.Optional;
        }

        if (string.Equals(_config.Prepare.BootValidationText, string.Empty, StringComparison.Ordinal))
        {
            _log.Info("boot_validation_skip_speech reason=empty_boot_text");
            _log.Info("boot_validation_ok");
            return true;
        }

        bool recoveryClickUsed = false;
        for (int startAttempt = 0; startAttempt <= _config.Audio.StartConfirmMaxRetries; startAttempt++)
        {
            if (!_ui.PrepareForPlayback(process, hwnd, _config.Prepare.ActionDelayMs))
            {
                _log.Error("起動時動作チェック失敗: 先頭移動ショートカットの実行に失敗しました。");
                return mode == BootValidationMode.Optional;
            }

            if (!_ui.PressPlay(hwnd))
            {
                _log.Error("起動時動作チェック失敗: 再生ボタンの押下に失敗しました。");
                return mode == BootValidationMode.Optional;
            }

            long monitorStartAt = MonoClock.NowMs();
            long startDeadline = monitorStartAt + _config.Audio.StartConfirmWindowMs;
            bool started = false;
            long speakingStartedAt = -1;
            long belowSince = -1;

            while (true)
            {
                if (!IsProcessAlive(process))
                {
                    OnProcessLost();
                    return mode == BootValidationMode.Optional;
                }

                long now = MonoClock.NowMs();
                AudioSessionSnapshot snap = _audio.ReadPeak(process.Id);

                if (!started)
                {
                    if (snap.Peak >= _config.Audio.PeakThreshold)
                    {
                        started = true;
                        speakingStartedAt = now;
                    }
                    else if (now > startDeadline)
                    {
                        bool hasNextAttempt = startAttempt < _config.Audio.StartConfirmMaxRetries;
                        if (hasNextAttempt)
                        {
                            if (!recoveryClickUsed
                                && _ui.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry))
                            {
                                _ui.TryPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry);
                                recoveryClickUsed = true;
                            }

                            _log.Warn($"boot_start_confirm_retry attempt={startAttempt + 1}");
                            break;
                        }

                        _log.Error("起動時動作チェック失敗: 音声の再生が確認できませんでした。");
                        return mode == BootValidationMode.Optional;
                    }
                }
                else
                {
                    if (_config.Audio.MaxSpeakingDurationSec > 0)
                    {
                        long maxMs = _config.Audio.MaxSpeakingDurationSec * 1000L;
                        if ((now - speakingStartedAt) > maxMs)
                        {
                            _log.Error("起動時動作チェック失敗: 音声の終了が確認できませんでした。");
                            JobExecutionCore.MoveToStartDuringPlayback(_config, _ui, hwnd, _config.Prepare.ActionDelayMs);
                            return mode == BootValidationMode.Optional;
                        }
                    }

                    if (snap.Peak < _config.Audio.PeakThreshold)
                    {
                        if (belowSince < 0)
                        {
                            belowSince = now;
                        }
                        else if ((now - belowSince) >= _config.Audio.StopConfirmMs)
                        {
                            _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                            _log.Info("boot_validation_ok");
                            return true;
                        }
                    }
                    else
                    {
                        belowSince = -1;
                    }
                }

                Thread.Sleep(_config.Audio.PollIntervalMs);
            }
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
                        if (_queue.Count >= _config.Server.MaxQueuedJobs)
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

        for (int i = 0; i < job.Segments.Count; i++)
        {
            Segment seg = job.Segments[i];
            bool isLast = i == job.Segments.Count - 1;
            bool recoveryClickUsed = false;
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
                _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                return;
            }

            if (ConsumeInterruptIfAny(log: true))
            {
                _log.Info("interrupt_applied state=ExecutingInputValidate");
                DropJob(job, "interrupt");
                _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
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
                _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                return;
            }

            if (!IsProcessAlive(process))
            {
                OnProcessLost();
                DropJob(job, "process_lost");
                return;
            }

            for (int startAttempt = 0; startAttempt <= _config.Audio.StartConfirmMaxRetries; startAttempt++)
            {
                if (!_ui.PrepareForPlayback(process, hwnd, _config.Prepare.ActionDelayMs))
                {
                    DropJob(job, "move_to_start_failed");
                    _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                    return;
                }

                if (!_ui.PressPlay(hwnd))
                {
                    DropJob(job, "play_failed");
                    _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                    return;
                }

                _log.Info($"play_pressed jobId={job.JobId} index={i}");

                lock (_gate)
                {
                    _state = WorkerState.Speaking;
                }

                SpeakMonitorResult speakResult = JobExecutionCore.MonitorSpeaking(
                    _config,
                    _ui,
                    _audio,
                    process,
                    hwnd,
                    _log,
                    () => _stopping || _appCts.IsCancellationRequested,
                    () => _interruptRequested,
                    () => ConsumeInterruptIfAny(log: false));
                if (speakResult.Kind == SpeakMonitorKind.Completed)
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
                        long waitUntil = speakResult.SegEndAtMs + trailing;
                        MonoClock.SleepUntil(waitUntil, () => _stopping || _appCts.IsCancellationRequested || _interruptRequested);
                    }

                    if (_interruptRequested)
                    {
                        ConsumeInterruptIfAny(log: false);
                        _log.Info("interrupt_applied state=Stopping");
                        DropJob(job, "interrupt");
                        return;
                    }

                    break;
                }

                if (speakResult.Kind == SpeakMonitorKind.Interrupted)
                {
                    _log.Info("interrupt_applied state=Speaking");
                    DropJob(job, "interrupt");
                    _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                    return;
                }

                if (speakResult.Kind == SpeakMonitorKind.StartTimeout)
                {
                    bool shouldRetry = JobExecutionCore.HandleStartTimeoutRetry(_config, _ui, process, hwnd, startAttempt, ref recoveryClickUsed);
                    if (shouldRetry)
                    {
                        _log.Warn($"start_confirm_retry jobId={job.JobId} index={i} attempt={startAttempt + 1}");
                        continue;
                    }

                    _log.Error("monitor_timeout reason=start_confirm");
                    DropJob(job, "start_confirm_failed");
                    _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                    return;
                }

                if (speakResult.Kind == SpeakMonitorKind.MaxDuration)
                {
                    _log.Error("monitor_timeout reason=max_duration");
                    DropJob(job, "max_speaking_duration");
                    _ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true);
                    return;
                }

                if (speakResult.Kind == SpeakMonitorKind.ProcessLost)
                {
                    OnProcessLost();
                    DropJob(job, "process_lost");
                    return;
                }
            }
        }
    }


    private InputValidateResult RunInputValidate(Process process, IntPtr hwnd, string text, int charDelay, bool useProbeGuardChars)
    {
        if (!IsProcessAlive(process))
        {
            return InputValidateResult.Fail("process_not_alive", "voicepeak_process_exited_or_unavailable", string.Empty);
        }

        if (!_ui.PrepareForTextInput(process, hwnd, _config.Prepare.ActionDelayMs, true))
        {
            return InputValidateResult.Fail("move_to_start_failed", "shortcut_not_applied_or_context_mismatch", string.Empty);
        }

        if (!_ui.ClearInput(process, hwnd, _config.Prepare.ActionDelayMs, true))
        {
            return InputValidateResult.Fail("clear_input_failed", "move_to_start_or_delete_not_applied", string.Empty);
        }

        string expected = InputTextNormalizer.Normalize(text);
        string toType = useProbeGuardChars ? ("A" + expected) : expected;
        if (!_ui.TypeText(hwnd, toType, charDelay))
        {
            return InputValidateResult.Fail("type_text_failed", "wm_char_input_failed", string.Empty);
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, _config.Prepare.PostTypeWaitPerCharMs, _config.Prepare.PostTypeWaitMinMs);
        if (postTypeWaitMs > 0)
        {
            Thread.Sleep(postTypeWaitMs);
        }

        if (useProbeGuardChars)
        {
            if (!_ui.PrepareForTextInput(process, hwnd, _config.Prepare.ActionDelayMs, true))
            {
                return InputValidateResult.Fail("move_to_start_failed", "shortcut_not_applied_or_context_mismatch", string.Empty);
            }

            if (!_ui.PressDelete(hwnd))
            {
                return InputValidateResult.Fail("delete_failed", "key_message_not_applied", string.Empty);
            }

        }

        ReadInputResult read = _ui.ReadInputTextDetailed(hwnd);
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

    internal static int ComputePostTypeWaitMs(string text, int perCharMs, int minMs)
    {
        return JobExecutionCore.ComputePostTypeWaitMs(text, perCharMs, minMs);
    }

    internal static string BuildInputMismatchCause(string expected, string actual, bool usedProbeGuardChars)
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

    private bool TryResolveTarget(out Process process, out IntPtr hwnd)
    {
        process = null;
        hwnd = IntPtr.Zero;

        int preferredPid = _preferredVoicepeakPid;
        if (preferredPid > 0)
        {
            if (_ui.TryResolveTargetByPid(preferredPid, out process, out hwnd))
            {
                return true;
            }
        }

        if (!_ui.TryResolveTarget(out process, out hwnd))
        {
            process = null;
            hwnd = IntPtr.Zero;
            return false;
        }

        if (process != null && process.Id > 0)
        {
            _preferredVoicepeakPid = process.Id;
        }

        return true;
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
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _log.Error("対象プロセスが終了したため処理を中断しました。");
        _appCts.Cancel();
    }

    private void LogTargetResolveFailure()
    {
        int processCount = _ui.GetVoicepeakProcessCount();
        if (processCount <= 0)
        {
            _log.Error("voicepeak.exe が起動していません。");
            return;
        }

        if (processCount > 1)
        {
            _log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {processCount}）");
            return;
        }

        _log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
    }

    private static string EscapeForJson(string s)
    {
        return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string SanitizeForLog(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }

    private readonly struct InputValidateResult
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

}
