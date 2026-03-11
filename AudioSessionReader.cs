using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace VoicepeakProxyCore;

// 音声ピーク値を取得
internal sealed class AudioSessionReader : IAudioSessionReader
{
    private readonly AppLogger _log;
    private readonly IAudioSessionSource _source;

    public AudioSessionReader(AppLogger log)
        : this(log, new NAudioSessionSource())
    {
    }

    internal AudioSessionReader(AppLogger log, IAudioSessionSource source)
    {
        _log = log;
        _source = source ?? new NAudioSessionSource();
    }

    // 対象プロセスの音声状態を読み取り
    public AudioSessionSnapshot ReadPeak(int processId)
    {
        AudioSessionSnapshot snap = new AudioSessionSnapshot
        {
            Found = false,
            Peak = 0f,
            StateLabel = "Unknown"
        };

        try
        {
            foreach (AudioSessionInfo session in _source.ReadSessions())
            {
                if (session.ProcessId != processId)
                {
                    continue;
                }

                snap.Found = true;
                float peak = session.Peak;
                if (peak > snap.Peak)
                {
                    snap.Peak = peak;
                }

                string state = session.StateLabel ?? "Unknown";
                if (string.Equals(state, "AudioSessionStateActive", StringComparison.Ordinal))
                {
                    snap.StateLabel = state;
                }
                else if (!string.Equals(snap.StateLabel, "AudioSessionStateActive", StringComparison.Ordinal))
                {
                    snap.StateLabel = state;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("audio_session_read_failed " + ex.Message);
        }

        return snap;
    }
}

// 音声セッション列挙を抽象化
internal interface IAudioSessionSource
{
    IEnumerable<AudioSessionInfo> ReadSessions();
}

// セッション情報を保持
internal readonly struct AudioSessionInfo
{
    public AudioSessionInfo(int processId, float peak, string stateLabel)
    {
        ProcessId = processId;
        Peak = peak;
        StateLabel = stateLabel ?? "Unknown";
    }

    public int ProcessId { get; }
    public float Peak { get; }
    public string StateLabel { get; }
}

// NAudioからセッションを列挙
internal sealed class NAudioSessionSource : IAudioSessionSource
{
    public IEnumerable<AudioSessionInfo> ReadSessions()
    {
        using MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        SessionCollection sessions = device.AudioSessionManager.Sessions;

        for (int i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl session = sessions[i];
            yield return new AudioSessionInfo(
                (int)session.GetProcessID,
                session.AudioMeterInformation.MasterPeakValue,
                session.State.ToString());
        }
    }
}

// 音声状態の読み取り結果
internal struct AudioSessionSnapshot
{
    public bool Found;
    public float Peak;
    public string StateLabel;
}
