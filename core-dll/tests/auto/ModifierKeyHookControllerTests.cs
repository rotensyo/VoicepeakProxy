using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class ModifierKeyHookControllerTests
{
    [TestMethod]
    public void EnsureInjected_WhenConnectAvailable_ReusesWithoutInject()
    {
        FakeModifierHookConnection connection = new FakeModifierHookConnection();
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(500, 100, 500, platform);

        bool ok = controller.EnsureInjected(123, new AppLogger(new TestLogger()));

        Assert.IsTrue(ok);
        Assert.AreEqual(0, platform.InjectCalls);
        Assert.AreEqual(1, platform.ConnectCalls);
    }

    [TestMethod]
    public void SetModifierOverride_WhenConnected_SendsOverrideCommand()
    {
        string lastCommand = null;
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                lastCommand = command;
                response = command == "PING" ? "PONG" : "OK";
                return true;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetModifierOverride(ModifierOverrideMode.Ctrl, log);

        Assert.IsTrue(ok);
        Assert.AreEqual("OVERRIDE|CTRL", lastCommand);
    }

    [TestMethod]
    public void SetModifierOverride_WhenAlt_SendsAltCommand()
    {
        string lastCommand = null;
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                lastCommand = command;
                response = command == "PING" ? "PONG" : "OK";
                return true;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetModifierOverride(ModifierOverrideMode.Alt, log);

        Assert.IsTrue(ok);
        Assert.AreEqual("OVERRIDE|ALT", lastCommand);
    }

    [TestMethod]
    public void SetModifierOverride_WhenShift_SendsShiftCommand()
    {
        string lastCommand = null;
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                lastCommand = command;
                response = command == "PING" ? "PONG" : "OK";
                return true;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetModifierOverride(ModifierOverrideMode.Shift, log);

        Assert.IsTrue(ok);
        Assert.AreEqual("OVERRIDE|SHIFT", lastCommand);
    }

    [TestMethod]
    public void SetVirtualClipboardText_WhenConnected_SendsClipSetCommand()
    {
        string lastCommand = null;
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                lastCommand = command;
                response = command == "PING" ? "PONG" : "OK";
                return true;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetVirtualClipboardText("あいうえお", log);

        Assert.IsTrue(ok);
        string expectedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("あいうえお"));
        Assert.AreEqual($"CLIP_SET|{expectedPayload}", lastCommand);
    }

    [TestMethod]
    public void ClearVirtualClipboard_WhenConnected_SendsClipClearCommand()
    {
        string lastCommand = null;
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                lastCommand = command;
                response = command == "PING" ? "PONG" : "OK";
                return true;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.ClearVirtualClipboard(log);

        Assert.IsTrue(ok);
        Assert.AreEqual("CLIP_CLEAR", lastCommand);
    }

    [TestMethod]
    public void SetVirtualClipboardText_WhenFirstSendFails_ReconnectsAndRetries()
    {
        int sendAttempt = 0;
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true, true }),
            CreateConnection = () => new FakeModifierHookConnection
            {
                SendHandler = (string command, int timeoutMs, out string response) =>
                {
                    sendAttempt++;
                    response = sendAttempt == 1 ? string.Empty : "OK";
                    return sendAttempt > 1;
                }
            }
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetVirtualClipboardText("text", log);

        Assert.IsTrue(ok);
        Assert.AreEqual(2, sendAttempt);
        Assert.AreEqual(2, platform.ConnectCalls);
    }

    [TestMethod]
    public void SetModifierOverride_WhenFailed_ThenRetryReconnects()
    {
        int sendAttempt = 0;
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true, true }),
            CreateConnection = () => new FakeModifierHookConnection
            {
                SendHandler = (string command, int timeoutMs, out string response) =>
                {
                    sendAttempt++;
                    response = sendAttempt == 1 ? string.Empty : "OK";
                    return sendAttempt > 1;
                }
            }
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool ok = controller.SetModifierOverride(ModifierOverrideMode.Ctrl, log);

        Assert.IsTrue(ok);
        Assert.AreEqual(2, sendAttempt);
        Assert.AreEqual(2, platform.ConnectCalls);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SetEnabled_WhenCommandTimeout_ReturnsFalse(bool enabledState)
    {
        FakeModifierHookConnection connection = new FakeModifierHookConnection
        {
            SendHandler = (string command, int timeoutMs, out string response) =>
            {
                response = string.Empty;
                return false;
            }
        };
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true }),
            ConnectionToReturn = connection
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool enabled = controller.SetEnabled(enabledState, log);

        Assert.IsFalse(enabled);
        Assert.AreEqual(1, connection.SendCalls);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SetEnabled_WhenFirstSendFails_ReconnectsAndRetriesOnce(bool enabledState)
    {
        int sendAttempt = 0;
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { true, true }),
            CreateConnection = () => new FakeModifierHookConnection
            {
                SendHandler = (string command, int timeoutMs, out string response) =>
                {
                    sendAttempt++;
                    response = sendAttempt == 1 ? string.Empty : "OK";
                    return sendAttempt > 1;
                }
            }
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 500, platform);
        AppLogger log = new AppLogger(new TestLogger());

        Assert.IsTrue(controller.EnsureInjected(123, log));
        bool enabled = controller.SetEnabled(enabledState, log);

        Assert.IsTrue(enabled);
        Assert.AreEqual(2, sendAttempt);
        Assert.AreEqual(2, platform.ConnectCalls);
    }

    [TestMethod]
    public void EnsureInjected_WhenConnectAlwaysFails_StopsWithinTotalWaitBudget()
    {
        FakeModifierHookPlatform platform = new FakeModifierHookPlatform
        {
            ConnectResults = new Queue<bool>(new[] { false, false, false, false, false, false }),
            InjectResult = false
        };
        ModifierKeyHookController controller = new ModifierKeyHookController(100, 100, 450, platform);

        bool ok = controller.EnsureInjected(123, new AppLogger(new TestLogger()));

        Assert.IsFalse(ok);
        Assert.IsTrue(platform.ConnectCalls >= 2);
        Assert.IsTrue(platform.SleepCalls > 0);
    }

    private sealed class FakeModifierHookPlatform : IModifierHookPlatform
    {
        public Queue<bool> ConnectResults { get; set; } = new Queue<bool>();
        public FakeModifierHookConnection ConnectionToReturn { get; set; } = new FakeModifierHookConnection();
        public Func<IModifierHookConnection> CreateConnection { get; set; }
        public bool InjectResult { get; set; } = true;
        public int InjectCalls { get; private set; }
        public int ConnectCalls { get; private set; }
        public int SleepCalls { get; private set; }

        public bool Inject(int pid, string injectionLibraryPath, string pipeName)
        {
            InjectCalls++;
            return InjectResult;
        }

        public bool TryConnect(string pipeName, int timeoutMs, out IModifierHookConnection connection, out Exception error)
        {
            ConnectCalls++;
            bool ok = ConnectResults.Count > 0 && ConnectResults.Dequeue();
            if (ok)
            {
                connection = CreateConnection != null ? CreateConnection() : ConnectionToReturn;
                error = null;
                return true;
            }

            connection = null;
            error = new TimeoutException("fake timeout");
            return false;
        }

        public void Sleep(int milliseconds)
        {
            SleepCalls++;
        }
    }

    private sealed class FakeModifierHookConnection : IModifierHookConnection
    {
        public delegate bool SendDelegate(string command, int timeoutMs, out string response);
        public SendDelegate SendHandler { get; set; }
        public bool IsConnected { get; set; } = true;
        public int SendCalls { get; private set; }

        public bool Send(string command, int timeoutMs, out string response)
        {
            SendCalls++;
            if (SendHandler != null)
            {
                return SendHandler(command, timeoutMs, out response);
            }

            response = command == "PING" ? "PONG" : "OK";
            return true;
        }

        public void Dispose()
        {
            IsConnected = false;
        }
    }
}
