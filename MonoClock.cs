using System;
using System.Diagnostics;
using System.Threading;

namespace VoicepeakProxyCore;

// 単調時計を提供
internal static class MonoClock
{
    // 現在時刻をミリ秒で取得
    public static long NowMs()
    {
        return Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
    }

    // 条件監視しながら目標時刻まで待機
    public static void SleepUntil(long targetMs, Func<bool> interruptChecker, int granularityMs = 10)
    {
        while (true)
        {
            if (interruptChecker())
            {
                return;
            }

            long now = NowMs();
            long remain = targetMs - now;
            if (remain <= 0)
            {
                return;
            }

            int sleep = (int)Math.Min(granularityMs, remain);
            if (sleep <= 0)
            {
                sleep = 1;
            }

            Thread.Sleep(sleep);
        }
    }
}
