using System;
using System.Diagnostics;

namespace VoicepeakProxyCore;

// クリック注入の契機を表す
internal enum InputContextPrimeReason
{
    Validation,
    BeforeTextFocusWhenUnprimed,
    StartTimeoutRetry
}

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

// UI操作の依存を抽象化
internal interface IVoicepeakUiController
{
    bool TryResolveTarget(out Process process, out IntPtr mainHwnd);
    ResolveTargetResult TryResolveTargetDetailed();
    bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd);
    int GetVoicepeakProcessCount();
    bool IsAlive(Process process);
    bool ShouldAttemptPrimeInputContext(Process process, IntPtr mainHwnd, InputContextPrimeReason reason);
    bool TryPrimeInputContext(Process process, IntPtr mainHwnd, InputContextPrimeReason reason);
    bool PrepareForTextInput(Process process, IntPtr mainHwnd, int actionDelayMs, bool allowCompositePrimeBeforeTextFocusWhenUnprimed);
    bool PrepareForPlayback(Process process, IntPtr mainHwnd, int actionDelayMs);
    bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs, bool allowCompositePrimeBeforeTextFocusWhenUnprimed);
    bool TypeText(IntPtr mainHwnd, string text, int charDelayMs);
    bool PressPlay(IntPtr mainHwnd);
    bool MoveToStart(IntPtr mainHwnd, int actionDelayMs);
    bool PressDelete(IntPtr mainHwnd);
    bool KillFocus(IntPtr mainHwnd);
    bool BeginModifierIsolationSession(int voicepeakProcessId, string operationName);
    bool EndModifierIsolationSession(string operationName);
    ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd);
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
