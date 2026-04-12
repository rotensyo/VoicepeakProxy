using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class AudioSessionReaderTests
{
    [TestMethod]
    public void ReadPeak_NoMatchingSession_ReturnsDefaultSnapshot()
    {
        // 一致セッションなしは既定値
        FakeAudioSessionSource source = new FakeAudioSessionSource
        {
            ReadSessionsHandler = () => new[]
            {
                new AudioSessionInfo(1, 0.1f, "AudioSessionStateInactive")
            }
        };

        AudioSessionReader reader = new AudioSessionReader(new AppLogger(new TestLogger()), source);
        AudioSessionSnapshot result = reader.ReadPeak(2);

        Assert.IsFalse(result.Found);
        Assert.AreEqual(0f, result.Peak);
        Assert.AreEqual("Unknown", result.StateLabel);
    }

    [TestMethod]
    public void ReadPeak_UsesMaxPeakAndActiveState()
    {
        // 最大peakとActive状態を採用
        FakeAudioSessionSource source = new FakeAudioSessionSource
        {
            ReadSessionsHandler = () => new[]
            {
                new AudioSessionInfo(5, 0.2f, "AudioSessionStateInactive"),
                new AudioSessionInfo(5, 0.8f, "AudioSessionStateActive"),
                new AudioSessionInfo(5, 0.4f, "AudioSessionStateInactive")
            }
        };

        AudioSessionReader reader = new AudioSessionReader(new AppLogger(new TestLogger()), source);
        AudioSessionSnapshot result = reader.ReadPeak(5);

        Assert.IsTrue(result.Found);
        Assert.AreEqual(0.8f, result.Peak);
        Assert.AreEqual("AudioSessionStateActive", result.StateLabel);
    }

    [TestMethod]
    public void ReadPeak_Exception_LogsWarningAndReturnsDefault()
    {
        // 例外時は警告して既定値を返却
        TestLogger logger = new TestLogger();
        FakeAudioSessionSource source = new FakeAudioSessionSource
        {
            ReadSessionsHandler = () => throw new InvalidOperationException("boom")
        };

        AudioSessionReader reader = new AudioSessionReader(new AppLogger(logger), source);
        AudioSessionSnapshot result = reader.ReadPeak(5);

        Assert.IsFalse(result.Found);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("audio_session_read_failed")));
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("boom")));
    }
}
