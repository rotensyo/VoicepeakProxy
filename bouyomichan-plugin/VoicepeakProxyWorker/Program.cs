using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace VoicepeakProxyWorker;

// Worker起動エントリ
internal static class Program
{
    private static int Main(string[] args)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string pipeName = "voicepeak_proxycore_bridge";
        string settingsPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_setting.json");
        string logPath = Path.Combine(baseDir, "Plugin_VoicepeakProxy_worker.log");
        bool initSettingsMode = false;
        int ownerPid = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? string.Empty;
            if (string.Equals(arg, "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipeName = args[++i];
                continue;
            }

            if (string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                settingsPath = args[++i];
                continue;
            }

            if (string.Equals(arg, "--log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                logPath = args[++i];
                continue;
            }

            if (string.Equals(arg, "--init-settings", StringComparison.OrdinalIgnoreCase))
            {
                initSettingsMode = true;
                continue;
            }

            if (string.Equals(arg, "--owner-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int parsed;
                if (int.TryParse(args[++i], out parsed))
                {
                    ownerPid = parsed;
                }

                continue;
            }

        }

        using (WorkerFileLogger logger = new WorkerFileLogger(logPath))
        {
            if (initSettingsMode)
            {
                SettingsBootstrapper.EnsureCreated(settingsPath, logger);
                return 0;
            }

            logger.Info("worker_start pipe=" + pipeName + " settings=" + settingsPath + " ownerPid=" + ownerPid);
            string mutexName = BuildMutexName(pipeName);
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    logger.Info("worker_already_running mutex=" + mutexName);
                    return 0;
                }

                WorkerHost host = new WorkerHost(pipeName, settingsPath, ownerPid, logger);
                bool runSucceeded = host.Run();
                if (!runSucceeded)
                {
                    logger.Warn("worker_startup_validation_failed_exit");
                    return 2;
                }
            }
        }

        return 0;
    }

    // パイプ名からMutex名を生成
    private static string BuildMutexName(string pipeName)
    {
        string source = pipeName ?? "voicepeak_proxycore_bridge";
        StringBuilder builder = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        return "Local\\VoicepeakProxyWorker_" + builder;
    }
}
