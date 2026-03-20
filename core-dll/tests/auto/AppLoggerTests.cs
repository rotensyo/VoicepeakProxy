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
        TestLogger logger = new TestLogger();
        AppLogger appLogger = new AppLogger(logger);

        appLogger.Error("failed", "reason=x");

        CollectionAssert.AreEqual(new[] { "failed reason=x" }, logger.ErrorMessages);
    }

    [TestMethod]
    public void AppLogger_NullLogger_UsesConsoleLogger()
    {
        TextWriter original = Console.Out;
        try
        {
            StringWriter writer = new StringWriter();
            Console.SetOut(writer);

            AppLogger appLogger = new AppLogger(null);
            appLogger.Info("hello");

            StringAssert.Contains(writer.ToString(), "[INFO] hello");
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
