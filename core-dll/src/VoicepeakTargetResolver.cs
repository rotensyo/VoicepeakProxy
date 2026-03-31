using System;
using System.Diagnostics;

namespace VoicepeakProxyCore;

// 対象プロセス解決を担当
internal sealed class VoicepeakTargetResolver
{
    private readonly IVoicepeakProcessApi _processApi;
    // 単一操作経路前提のためロックは設けない
    private int _cachedVoicepeakPid;

    // 依存を保持
    public VoicepeakTargetResolver(IVoicepeakProcessApi processApi)
    {
        _processApi = processApi;
    }

    // キャッシュpidを外部連携
    public int CachedVoicepeakPid
    {
        get => _cachedVoicepeakPid;
        set => _cachedVoicepeakPid = value;
    }

    // 対象解決と失敗理由を返す
    public ResolveTargetResult TryResolveTargetDetailed()
    {
        if (!TryResolveVoicepeakPidDetailed(out int pid, out ResolveTargetFailureReason reason, out int processCount))
        {
            return new ResolveTargetResult
            {
                Success = false,
                FailureReason = reason,
                ProcessCount = processCount,
                Process = null,
                MainHwnd = IntPtr.Zero
            };
        }

        if (!TryResolveTargetByPid(pid, out Process process, out IntPtr mainHwnd))
        {
            return new ResolveTargetResult
            {
                Success = false,
                FailureReason = ResolveTargetFailureReason.TargetNotFound,
                ProcessCount = processCount,
                Process = null,
                MainHwnd = IntPtr.Zero
            };
        }

        return new ResolveTargetResult
        {
            Success = true,
            FailureReason = ResolveTargetFailureReason.None,
            ProcessCount = processCount,
            Process = process,
            MainHwnd = mainHwnd
        };
    }

    // pidから対象を解決
    public bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd)
    {
        process = null;
        mainHwnd = IntPtr.Zero;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            process = _processApi.GetProcessById(pid);
        }
        catch
        {
            return false;
        }

        try
        {
            if (process == null || process.HasExited)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        mainHwnd = _processApi.WaitMainWindowHandle(process, 3000);
        if (mainHwnd == IntPtr.Zero)
        {
            return false;
        }

        _cachedVoicepeakPid = process.Id;
        return true;
    }

    // 対象プロセス数を返す
    public int GetVoicepeakProcessCount()
    {
        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        return matches.Length;
    }

    // PID解決と失敗理由を返す
    private bool TryResolveVoicepeakPidDetailed(out int pid, out ResolveTargetFailureReason reason, out int processCount)
    {
        pid = 0;
        reason = ResolveTargetFailureReason.TargetNotFound;
        processCount = 0;

        int cachedPid = _cachedVoicepeakPid;
        if (cachedPid > 0)
        {
            if (IsValidVoicepeakProcess(cachedPid, out Process cachedProcess))
            {
                pid = cachedProcess.Id;
                processCount = 1;
                reason = ResolveTargetFailureReason.None;
                return true;
            }

            _cachedVoicepeakPid = 0;
        }

        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        processCount = matches?.Length ?? 0;
        if (processCount <= 0)
        {
            reason = ResolveTargetFailureReason.ProcessNotFound;
            return false;
        }

        if (processCount > 1)
        {
            reason = ResolveTargetFailureReason.MultipleProcesses;
            return false;
        }

        Process process = matches[0];
        if (process == null)
        {
            reason = ResolveTargetFailureReason.TargetNotFound;
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                reason = ResolveTargetFailureReason.TargetNotFound;
                return false;
            }

            pid = process.Id;
            _cachedVoicepeakPid = pid;
            reason = ResolveTargetFailureReason.None;
            return true;
        }
        catch
        {
            reason = ResolveTargetFailureReason.TargetNotFound;
            return false;
        }
    }

    // キャッシュpidの妥当性を判定
    private bool IsValidVoicepeakProcess(int pid, out Process process)
    {
        process = null;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            process = _processApi.GetProcessById(pid);
            if (process == null || process.HasExited)
            {
                return false;
            }

            Process[] matches = _processApi.GetProcessesByName("voicepeak");
            if (matches == null || matches.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < matches.Length; i++)
            {
                Process candidate = matches[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.Id == pid)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
