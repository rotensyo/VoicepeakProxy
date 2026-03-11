using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoicepeakProxyCore.Tests;

internal sealed class FakeVoicepeakUiController : IVoicepeakUiController
{
    public Func<Process, bool> IsAliveHandler { get; set; } = _ => true;
    public Func<bool> ClearInputHandler { get; set; } = () => true;
    public Func<IntPtr, string, int, bool> TypeTextHandler { get; set; } = (_, _, _) => true;
    public Func<IntPtr, bool> PressPlayHandler { get; set; } = _ => true;
    public Func<IntPtr, int, bool> MoveToStartHandler { get; set; } = (_, _) => true;
    public Func<IntPtr, int, bool> MoveToEndHandler { get; set; } = (_, _) => true;
    public Func<IntPtr, bool> PressBackspaceHandler { get; set; } = _ => true;
    public Func<IntPtr, bool> PressDeleteHandler { get; set; } = _ => true;
    public Func<IntPtr, ReadInputResult> ReadInputHandler { get; set; }
        = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
    public Func<int, (bool Success, Process Process, IntPtr Hwnd)> ResolveByPidHandler { get; set; }
        = _ => (false, null, IntPtr.Zero);
    public Func<(bool Success, Process Process, IntPtr Hwnd)> ResolveTargetHandler { get; set; }
        = () => (false, null, IntPtr.Zero);
    public Func<int> ProcessCountHandler { get; set; } = () => 0;

    public List<string> TypedTexts { get; } = new List<string>();
    public int ClearInputCalls { get; private set; }
    public int PressPlayCalls { get; private set; }
    public int MoveToStartCalls { get; private set; }
    public int MoveToEndCalls { get; private set; }
    public int PressBackspaceCalls { get; private set; }
    public int PressDeleteCalls { get; private set; }

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

    public int GetVoicepeakProcessCount() => ProcessCountHandler();

    public bool IsAlive(Process process) => IsAliveHandler(process);

    public bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        ClearInputCalls++;
        return ClearInputHandler();
    }

    public bool TypeText(IntPtr mainHwnd, string text, int charDelayMs)
    {
        TypedTexts.Add(text ?? string.Empty);
        return TypeTextHandler(mainHwnd, text, charDelayMs);
    }

    public bool PressPlay(IntPtr mainHwnd)
    {
        PressPlayCalls++;
        return PressPlayHandler(mainHwnd);
    }

    public bool MoveToStart(IntPtr mainHwnd, int actionDelayMs)
    {
        MoveToStartCalls++;
        return MoveToStartHandler(mainHwnd, actionDelayMs);
    }

    public bool MoveToEnd(IntPtr mainHwnd, int actionDelayMs)
    {
        MoveToEndCalls++;
        return MoveToEndHandler(mainHwnd, actionDelayMs);
    }

    public bool PressBackspace(IntPtr mainHwnd)
    {
        PressBackspaceCalls++;
        return PressBackspaceHandler(mainHwnd);
    }

    public bool PressDelete(IntPtr mainHwnd)
    {
        PressDeleteCalls++;
        return PressDeleteHandler(mainHwnd);
    }

    public ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd) => ReadInputHandler(mainHwnd);
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
