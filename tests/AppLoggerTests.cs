using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class AppLoggerTests
{
    [TestMethod]
    public void AppLogger_ForwardsAllLevels()
    {
        // 各ログレベルを委譲
        TestLogger logger = new TestLogger();
        AppLogger appLogger = new AppLogger(logger);

        appLogger.Debug("d");
        appLogger.Info("i");
        appLogger.Warn("w");
        appLogger.Error("e");

        CollectionAssert.AreEqual(new[] { "d" }, logger.DebugMessages);
        CollectionAssert.AreEqual(new[] { "i" }, logger.InfoMessages);
        CollectionAssert.AreEqual(new[] { "w" }, logger.WarnMessages);
        CollectionAssert.AreEqual(new[] { "e" }, logger.ErrorMessages);
    }

    [TestMethod]
    public void AppLogger_ErrorWithDetail_ConcatenatesMessage()
    {
        // 詳細文字列を連結
        TestLogger logger = new TestLogger();
        AppLogger appLogger = new AppLogger(logger);

        appLogger.Error("failed", "reason=x");

        CollectionAssert.AreEqual(new[] { "failed reason=x" }, logger.ErrorMessages);
    }

    [TestMethod]
    public void AppLogger_NullLogger_UsesConsoleLogger()
    {
        // nullロガー時はコンソールへ出力
        TextWriter original = Console.Out;
        try
        {
            StringWriter writer = new StringWriter();
            Console.SetOut(writer);

            AppLogger appLogger = new AppLogger(null);
            appLogger.Info("hello");

            string output = writer.ToString();
            StringAssert.Contains(output, "[INFO] hello");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [TestMethod]
    public void ConsoleAppLogger_WritesLevelAndMessage()
    {
        // コンソールロガーの書式を確認
        TextWriter original = Console.Out;
        try
        {
            StringWriter writer = new StringWriter();
            Console.SetOut(writer);

            ConsoleAppLogger logger = new ConsoleAppLogger();
            logger.Warn("warned");

            string output = writer.ToString();
            StringAssert.Contains(output, "[WARN] warned");
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
