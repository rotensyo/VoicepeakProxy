using System;
using System.Diagnostics;

namespace VoicepeakProxyCore;

// 対象解決失敗理由
internal enum ResolveTargetFailureReason
{
    None,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound
}

// 対象解決結果
internal sealed class ResolveTargetResult
{
    public bool Success { get; set; }
    public Process Process { get; set; }
    public IntPtr MainHwnd { get; set; }
    public ResolveTargetFailureReason FailureReason { get; set; }
    public int ProcessCount { get; set; }
}

// 対象解決失敗を共通表現へ変換
internal static class ResolveTargetFailureMapper
{
    // 対象解決失敗をログへ出力
    public static void LogFailure(AppLogger log, ResolveTargetFailureReason reason, int processCount)
    {
        if (reason == ResolveTargetFailureReason.ProcessNotFound)
        {
            log.Error("voicepeak.exe が起動していません。");
            return;
        }

        if (reason == ResolveTargetFailureReason.MultipleProcesses)
        {
            log.Error($"voicepeak.exe が複数起動しています。1つだけ起動してください。（検出数: {processCount}）");
            return;
        }

        log.Error("対象ウィンドウを取得できませんでした。アプリの状態を確認してください。");
    }

    // 解決結果から失敗理由を正規化
    public static ResolveTargetFailureReason NormalizeReason(ResolveTargetResult resolved)
    {
        if (resolved == null)
        {
            return ResolveTargetFailureReason.TargetNotFound;
        }

        return resolved.FailureReason == ResolveTargetFailureReason.None
            ? ResolveTargetFailureReason.TargetNotFound
            : resolved.FailureReason;
    }
}

// UI操作の依存を抽象化
internal interface IVoicepeakUiController
{
    // 対象解決を成功可否のみで返す
    bool TryResolveTarget(out Process process, out IntPtr mainHwnd);
    // 対象解決結果と失敗理由を返す
    ResolveTargetResult TryResolveTargetDetailed();
    bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd);
    int GetVoicepeakProcessCount();
    bool IsAlive(Process process);
    bool PrepareForTextInput(Process process, IntPtr mainHwnd, int actionDelayMs);
    bool PrepareForPlayback(Process process, IntPtr mainHwnd, int actionDelayMs);
    bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs);
    bool TypeText(IntPtr mainHwnd, string text);
    int GetVisibleInputBlockCount(IntPtr mainHwnd);
    bool PressPlay(IntPtr mainHwnd);
    bool MoveToStart(IntPtr mainHwnd, int actionDelayMs);
    bool PressDelete(IntPtr mainHwnd);
    bool KillFocus(IntPtr mainHwnd);
    bool BeginModifierIsolationSession(int voicepeakProcessId, string operationName);
    bool EndModifierIsolationSession(string operationName);
    ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd);
    ReadInputSnapshot ReadInputSnapshot(IntPtr mainHwnd);
}

// 音声監視の依存を抽象化
internal interface IAudioSessionReader
{
    AudioSessionSnapshot ReadPeak(int processId);
}

// プロセス解決を抽象化
internal interface IVoicepeakProcessApi
{
    Process[] GetProcessesByName(string processName);
    Process GetProcessById(int pid);
    IntPtr WaitMainWindowHandle(Process process, int timeoutMs);
}
