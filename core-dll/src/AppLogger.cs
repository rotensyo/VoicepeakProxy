using System;

namespace VoicepeakProxyCore;

// ログ出力先を抽象化
public interface IAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

// コンソールへログ出力
public sealed class ConsoleAppLogger : IAppLogger
{
    public void Debug(string message) => Write("DEBUG", message);
    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Console.WriteLine($"{ts} [{level}] {message}");
    }
}

// 実行単位のログ窓口
internal sealed class AppLogger
{
    private enum LogMinimumLevel
    {
        Debug = 10,
        Info = 20,
        Warn = 30,
        Error = 40
    }

    private readonly IAppLogger _logger;
    private readonly LogMinimumLevel _minimumLevel;

    public AppLogger(IAppLogger logger)
        : this(logger, null)
    {
    }

    public AppLogger(IAppLogger logger, string minimumLevel)
    {
        _logger = logger ?? new ConsoleAppLogger();
        _minimumLevel = ParseMinimumLevel(minimumLevel);
    }

    // DEBUGログを最小レベルで抑制
    public void Debug(string message)
    {
        if (_minimumLevel > LogMinimumLevel.Debug)
        {
            return;
        }

        _logger.Debug(message);
    }

    // INFOログを最小レベルで抑制
    public void Info(string message)
    {
        if (_minimumLevel > LogMinimumLevel.Info)
        {
            return;
        }

        _logger.Info(message);
    }

    // WARNログを最小レベルで抑制
    public void Warn(string message)
    {
        if (_minimumLevel > LogMinimumLevel.Warn)
        {
            return;
        }

        _logger.Warn(message);
    }

    // ERRORログを最小レベルで抑制
    public void Error(string message)
    {
        if (_minimumLevel > LogMinimumLevel.Error)
        {
            return;
        }

        _logger.Error(message);
    }

    // 詳細付きERRORログを出力
    public void Error(string message, string detail)
    {
        if (_minimumLevel > LogMinimumLevel.Error)
        {
            return;
        }

        _logger.Error(message + " " + detail);
    }

    // 文字列から最小レベルを解決
    private static LogMinimumLevel ParseMinimumLevel(string level)
    {
        string normalized = (level ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "info":
                return LogMinimumLevel.Info;
            case "warn":
                return LogMinimumLevel.Warn;
            case "error":
                return LogMinimumLevel.Error;
            default:
                return LogMinimumLevel.Debug;
        }
    }
}
