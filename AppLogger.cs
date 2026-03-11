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
    private readonly IAppLogger _logger;

    public AppLogger(IAppLogger logger)
    {
        _logger = logger ?? new ConsoleAppLogger();
    }

    public void Debug(string message) => _logger.Debug(message);
    public void Info(string message) => _logger.Info(message);
    public void Warn(string message) => _logger.Warn(message);
    public void Error(string message) => _logger.Error(message);
    public void Error(string message, string detail) => _logger.Error(message + " " + detail);
}
