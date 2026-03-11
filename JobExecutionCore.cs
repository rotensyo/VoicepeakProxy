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

// 単発常駐で共有する実行ロジック
internal static class JobExecutionCore
{
    // セグメント入力準備を実行
    public static bool PrepareSegment(AppConfig config, IVoicepeakUiController ui, Process process, IntPtr hwnd, string text, AppLogger log)
    {
        if (!ui.IsAlive(process))
        {
            log.Warn("prepare_failed_detail reason=process_not_alive cause=voicepeak_process_exited_or_unavailable");
            return false;
        }

        if (!ui.ClearInput(process, hwnd, config.Prepare.ActionDelayMs))
        {
            log.Warn("prepare_failed_detail reason=clear_input_failed cause=move_to_start_or_delete_not_applied");
            return false;
        }

        string expected = InputTextNormalizer.Normalize(text);
        int charDelay = config.Prepare.CharDelayBaseMs;
        if (!ui.TypeText(hwnd, expected, charDelay))
        {
            log.Warn("prepare_failed_detail reason=type_text_failed cause=wm_char_input_failed");
            return false;
        }

        int postTypeWaitMs = ComputePostTypeWaitMs(expected, config.Prepare.PostTypeWaitPerCharMs, config.Prepare.PostTypeWaitMinMs);
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
        long startDeadline = monitorStartAt + config.Audio.StartConfirmWindowMs;
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
                ui.MoveToStart(hwnd, config.Prepare.ActionDelayMs);
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
                    ui.MoveToStart(hwnd, config.Prepare.ActionDelayMs);
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
        int compensation = config.Audio.StopConfirmMs + config.Ui.PlayPreShortcutDelayMs;
        int requiredByFormulaMs = compensation;
        if (string.Equals(phase, "pre", StringComparison.Ordinal))
        {
            string normalized = InputTextNormalizer.Normalize(nextText);
            int typingMs = normalized.Length * config.Prepare.CharDelayBaseMs;
            int postTypeWaitMs = ComputePostTypeWaitMs(normalized, config.Prepare.PostTypeWaitPerCharMs, config.Prepare.PostTypeWaitMinMs);
            requiredByFormulaMs += typingMs + postTypeWaitMs;
        }

        if (pauseMs > 0 && pauseMs < requiredByFormulaMs)
        {
            log.Warn($"pause_too_short jobId={jobId} index={segmentIndex} phase={phase} pauseMs={pauseMs} requiredMs={requiredByFormulaMs}");
        }

        int adjusted = pauseMs - compensation;
        return adjusted > 0 ? adjusted : 0;
    }
}
