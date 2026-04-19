using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoicepeakProxyCore.Tests;

internal sealed class FakeVoicepeakUiController : IVoicepeakUiController
{
    public Func<Process, bool> IsAliveHandler { get; set; } = _ => true;
    public Func<Process, IntPtr, int, bool> PrepareForTextInputHandler { get; set; } = (_, _, _) => true;
    public Func<Process, IntPtr, int, bool> PrepareForPlaybackHandler { get; set; } = (_, _, _) => true;
    public Func<bool> ClearInputHandler { get; set; } = () => true;
    public Func<IntPtr, string, bool> TypeTextHandler { get; set; } = (_, _) => true;
    public Func<IntPtr, bool> PressPlayHandler { get; set; } = _ => true;
    public Func<IntPtr, int, bool> MoveToStartHandler { get; set; } = (_, _) => true;
    public Func<IntPtr, bool> PressDeleteHandler { get; set; } = _ => true;
    public Func<IntPtr, bool> KillFocusHandler { get; set; } = _ => true;
    public Func<int, string, bool> BeginModifierIsolationSessionHandler { get; set; } = (_, _) => true;
    public Func<string, bool> EndModifierIsolationSessionHandler { get; set; } = _ => true;
    public Func<IntPtr, ReadInputResult> ReadInputHandler { get; set; }
        = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
    public Func<IntPtr, int> VisibleInputBlockCountHandler { get; set; } = _ => 1;
    public Func<ResolveTargetResult> ResolveTargetDetailedHandler { get; set; } = null;
    public Func<int, (bool Success, Process Process, IntPtr Hwnd)> ResolveByPidHandler { get; set; }
        = _ => (false, null, IntPtr.Zero);
    public Func<(bool Success, Process Process, IntPtr Hwnd)> ResolveTargetHandler { get; set; }
        = () => (false, null, IntPtr.Zero);
    public Func<int> ProcessCountHandler { get; set; } = () => 0;

    public List<string> TypedTexts { get; } = new List<string>();
    public List<string> CallLog { get; } = new List<string>();
    public int PrepareForTextInputCalls { get; private set; }
    public int PrepareForPlaybackCalls { get; private set; }
    public int ClearInputCalls { get; private set; }
    public int PressPlayCalls { get; private set; }
    public int MoveToStartCalls { get; private set; }
    public int PressDeleteCalls { get; private set; }
    public int KillFocusCalls { get; private set; }
    public int BeginModifierIsolationSessionCalls { get; private set; }
    public int EndModifierIsolationSessionCalls { get; private set; }
    public int GetVoicepeakProcessCountCalls { get; private set; }
    public int TryResolveTargetDetailedCalls { get; private set; }

    public bool TryResolveTarget(out Process process, out IntPtr mainHwnd)
    {
        (bool success, Process resolvedProcess, IntPtr resolvedHwnd) = ResolveTargetHandler();
        process = resolvedProcess;
        mainHwnd = resolvedHwnd;
        return success;
    }

    public bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd)
    {
        (bool success, Process resolvedProcess, IntPtr resolvedHwnd) = ResolveByPidHandler(pid);
        process = resolvedProcess;
        mainHwnd = resolvedHwnd;
        return success;
    }

    public int GetVoicepeakProcessCount()
    {
        GetVoicepeakProcessCountCalls++;
        return ProcessCountHandler();
    }

    // 失敗理由付きの対象解決を模擬
    public ResolveTargetResult TryResolveTargetDetailed()
    {
        TryResolveTargetDetailedCalls++;
        if (ResolveTargetDetailedHandler != null)
        {
            return ResolveTargetDetailedHandler();
        }

        (bool success, Process resolvedProcess, IntPtr resolvedHwnd) = ResolveTargetHandler();
        if (success)
        {
            return new ResolveTargetResult
            {
                Success = true,
                Process = resolvedProcess,
                MainHwnd = resolvedHwnd,
                FailureReason = ResolveTargetFailureReason.None,
                ProcessCount = 1
            };
        }

        int processCount = GetVoicepeakProcessCount();
        ResolveTargetFailureReason reason = processCount <= 0
            ? ResolveTargetFailureReason.ProcessNotFound
            : processCount > 1
                ? ResolveTargetFailureReason.MultipleProcesses
                : ResolveTargetFailureReason.TargetNotFound;
        return new ResolveTargetResult
        {
            Success = false,
            Process = null,
            MainHwnd = IntPtr.Zero,
            FailureReason = reason,
            ProcessCount = processCount
        };
    }

    public bool IsAlive(Process process) => IsAliveHandler(process);

    public bool PrepareForTextInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        PrepareForTextInputCalls++;
        CallLog.Add("prepare_text");
        return PrepareForTextInputHandler(process, mainHwnd, actionDelayMs);
    }

    public bool PrepareForPlayback(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        PrepareForPlaybackCalls++;
        CallLog.Add("prepare_playback");
        return PrepareForPlaybackHandler(process, mainHwnd, actionDelayMs);
    }

    public bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        ClearInputCalls++;
        CallLog.Add("clear_input");
        return ClearInputHandler();
    }

    public bool TypeText(IntPtr mainHwnd, string text)
    {
        TypedTexts.Add(text ?? string.Empty);
        CallLog.Add("type_text");
        return TypeTextHandler(mainHwnd, text);
    }

    public bool PressPlay(IntPtr mainHwnd)
    {
        PressPlayCalls++;
        CallLog.Add("press_play");
        return PressPlayHandler(mainHwnd);
    }

    public bool MoveToStart(IntPtr mainHwnd, int actionDelayMs)
    {
        MoveToStartCalls++;
        CallLog.Add("move_to_start");
        return MoveToStartHandler(mainHwnd, actionDelayMs);
    }

    public bool PressDelete(IntPtr mainHwnd)
    {
        PressDeleteCalls++;
        CallLog.Add("press_delete");
        return PressDeleteHandler(mainHwnd);
    }

    public bool KillFocus(IntPtr mainHwnd)
    {
        KillFocusCalls++;
        CallLog.Add("kill_focus");
        return KillFocusHandler(mainHwnd);
    }

    public bool BeginModifierIsolationSession(int voicepeakProcessId, string operationName)
    {
        BeginModifierIsolationSessionCalls++;
        CallLog.Add("modifier_session_begin");
        return BeginModifierIsolationSessionHandler(voicepeakProcessId, operationName);
    }

    public bool EndModifierIsolationSession(string operationName)
    {
        EndModifierIsolationSessionCalls++;
        CallLog.Add("modifier_session_end");
        return EndModifierIsolationSessionHandler(operationName);
    }

    public ReadInputSnapshot ReadInputSnapshot(IntPtr mainHwnd)
        => new ReadInputSnapshot(ReadInputHandler(mainHwnd), VisibleInputBlockCountHandler(mainHwnd));

    public ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd) => ReadInputHandler(mainHwnd);

    public int GetVisibleInputBlockCount(IntPtr mainHwnd) => VisibleInputBlockCountHandler(mainHwnd);
}

internal sealed class FakeAudioSessionReader : IAudioSessionReader
{
    public Queue<AudioSessionSnapshot> Snapshots { get; } = new Queue<AudioSessionSnapshot>();
    public AudioSessionSnapshot Fallback { get; set; }
        = new AudioSessionSnapshot { Found = false, Peak = 0f, StateLabel = "Unknown" };

    public AudioSessionSnapshot ReadPeak(int processId)
    {
        if (Snapshots.Count > 0)
        {
            return Snapshots.Dequeue();
        }

        return Fallback;
    }
}

internal sealed class FakeAudioSessionSource : IAudioSessionSource
{
    public Func<IEnumerable<AudioSessionInfo>> ReadSessionsHandler { get; set; }
        = () => Array.Empty<AudioSessionInfo>();

    public IEnumerable<AudioSessionInfo> ReadSessions() => ReadSessionsHandler();
}

internal sealed class FakeVoicepeakProcessApi : IVoicepeakProcessApi
{
    public Func<string, Process[]> GetProcessesByNameHandler { get; set; } = _ => Array.Empty<Process>();
    public Func<int, Process> GetProcessByIdHandler { get; set; } = _ => throw new ArgumentException();
    public Func<Process, int, IntPtr> WaitMainWindowHandleHandler { get; set; } = (_, _) => IntPtr.Zero;

    public Process[] GetProcessesByName(string processName) => GetProcessesByNameHandler(processName);

    public Process GetProcessById(int pid) => GetProcessByIdHandler(pid);

    public IntPtr WaitMainWindowHandle(Process process, int timeoutMs) => WaitMainWindowHandleHandler(process, timeoutMs);
}
