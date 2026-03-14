using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class UiControllerTests
{
    private const int WmKeyDown = 0x0100;
    private const int VkReturn = 0x0D;

    [TestMethod]
    public void IsValidShortcut_AcceptsSupportedFormats()
    {
        // 許可ショートカット形式を確認
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "F3"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "Ctrl+F4"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "Shift+Space"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "Home"));
    }

    [TestMethod]
    public void IsValidShortcut_RejectsUnsupportedFormats()
    {
        // 非対応ショートカット形式を拒否
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "Delete"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "Enter"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", "F13"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidShortcut", ""));
    }

    [TestMethod]
    public void IsValidShortcut_RejectsModifierOnlyAndEmptyPart()
    {
        // 主キー不備を拒否
        Assert.IsFalse(VoicepeakUiController.IsValidShortcut("Ctrl"));
        Assert.IsFalse(VoicepeakUiController.IsValidShortcut("Ctrl+"));
    }

    [TestMethod]
    public void IsValidMoveToStartShortcut_AcceptsCtrlUp()
    {
        // 先頭移動専用でCtrl+Upを許可
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "Ctrl+Up"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "Alt+Up"));
    }

    [TestMethod]
    public void IsCompositeMoveToStartShortcut_ReturnsExpectedValues()
    {
        // 複合先頭移動だけを識別
        Assert.IsTrue(VoicepeakUiController.IsCompositeMoveToStartShortcut("Ctrl+Up"));
        Assert.IsFalse(VoicepeakUiController.IsCompositeMoveToStartShortcut("Home"));
    }

    [TestMethod]
    public void ShouldAttemptPrimeInputContext_NonCompositeShortcut_ReturnsFalse()
    {
        // 単一ショートカットではprimeしない
        VoicepeakUiController controller = CreateController(new UiConfig { MoveToStartShortcut = "Home" }, new FakeVoicepeakProcessApi());
        Process process = Process.GetCurrentProcess();
        IntPtr hwnd = new IntPtr(123);

        Assert.IsFalse(controller.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.Validation));
        Assert.IsFalse(controller.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.BeforeTextFocusWhenUnprimed));
        Assert.IsFalse(controller.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry));
    }

    [TestMethod]
    public void ShouldAttemptPrimeInputContext_BeforeTextFocus_UsesFlagAndPrimeState()
    {
        // 未prime時だけ入力前primeを許可
        UiConfig ui = new UiConfig
        {
            MoveToStartShortcut = "Ctrl+Up",
            CompositePrimeBeforeTextFocusWhenUnprimedEnabled = true
        };
        VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());
        Process process = Process.GetCurrentProcess();
        IntPtr hwnd = new IntPtr(123);

        Assert.IsTrue(controller.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.BeforeTextFocusWhenUnprimed));

        ReflectionTestHelper.SetField(controller, "_primedProcessId", process.Id);
        ReflectionTestHelper.SetField(controller, "_primedMainHwnd", hwnd);

        Assert.IsFalse(controller.ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.BeforeTextFocusWhenUnprimed));
    }

    [TestMethod]
    public void ShouldAttemptPrimeInputContext_StartTimeoutRetry_UsesDedicatedFlag()
    {
        // 再試行時修正クリックは専用フラグで制御
        UiConfig disabled = new UiConfig
        {
            MoveToStartShortcut = "Ctrl+Up",
            CompositeRecoveryClickOnStartTimeoutRetryEnabled = false
        };
        UiConfig enabled = new UiConfig
        {
            MoveToStartShortcut = "Ctrl+Up",
            CompositeRecoveryClickOnStartTimeoutRetryEnabled = true
        };
        Process process = Process.GetCurrentProcess();
        IntPtr hwnd = new IntPtr(123);

        Assert.IsFalse(CreateController(disabled, new FakeVoicepeakProcessApi())
            .ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry));
        Assert.IsTrue(CreateController(enabled, new FakeVoicepeakProcessApi())
            .ShouldAttemptPrimeInputContext(process, hwnd, InputContextPrimeReason.StartTimeoutRetry));
    }

    [TestMethod]
    public void IsExcludedControlType_AndName_WorkAsSpecified()
    {
        // 除外対象の型と名称を確認
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedControlType", ControlType.ComboBox));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedControlType", ControlType.Slider));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedControlType", ControlType.ScrollBar));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedControlType", ControlType.Edit));

        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedName", "感情"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedName", "  設定  "));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedName", string.Empty));
    }

    [TestMethod]
    public void IsExcludedName_NonExcluded_IsFalse()
    {
        // 非除外名称はfalse
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsExcludedName", "本文"));
    }

    [TestMethod]
    public void TypeText_DoesNotSendEnterWhenDisabled()
    {
        // 設定無効時はEnter送信なし
        int enterCount = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                SendEnterAfterSentenceBreak = false
            };
            TestLogger logger = new TestLogger();
            object controller = ReflectionTestHelper.CreateVoicepeakUiController(ui, new DebugConfig(), logger);
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();

            bool result = (bool)ReflectionTestHelper.InvokeCoreInstance(controller, "TypeText", window.Handle, "A。B", 0);
            Assert.IsTrue(result);
            return window.Messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkReturn);
        });

        Assert.AreEqual(0, enterCount);
    }

    [TestMethod]
    public void TypeText_UsesLongestSentenceBreakTrigger()
    {
        // 最長一致トリガーを優先
        int enterCount = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                SendEnterAfterSentenceBreak = true
            };
            ui.SentenceBreakTriggers.Clear();
            ui.SentenceBreakTriggers.Add("。");
            ui.SentenceBreakTriggers.Add("。、。");

            TestLogger logger = new TestLogger();
            object controller = ReflectionTestHelper.CreateVoicepeakUiController(ui, new DebugConfig(), logger);
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();

            bool result = (bool)ReflectionTestHelper.InvokeCoreInstance(controller, "TypeText", window.Handle, "A。、。B。C", 0);
            Assert.IsTrue(result);
            return window.Messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkReturn);
        });

        Assert.AreEqual(2, enterCount);
    }

    [TestMethod]
    public void TypeText_DoesNotSendEnterAtSegmentEnd()
    {
        // セグメント末尾のEnter送信を抑止
        int enterCount = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                SendEnterAfterSentenceBreak = true
            };
            ui.SentenceBreakTriggers.Clear();
            ui.SentenceBreakTriggers.Add("。");

            TestLogger logger = new TestLogger();
            object controller = ReflectionTestHelper.CreateVoicepeakUiController(ui, new DebugConfig(), logger);
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();

            bool result = (bool)ReflectionTestHelper.InvokeCoreInstance(controller, "TypeText", window.Handle, "A。B。", 0);
            Assert.IsTrue(result);
            return window.Messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkReturn);
        });

        Assert.AreEqual(1, enterCount);
    }

    [TestMethod]
    public void GetVoicepeakProcessCount_UsesProcessApi()
    {
        // プロセス数をAPIから取得
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => new[] { Process.GetCurrentProcess(), Process.GetCurrentProcess() }
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        int actual = controller.GetVoicepeakProcessCount();

        Assert.AreEqual(2, actual);
    }

    [TestMethod]
    public void TryResolveTarget_NoProcess_ReturnsFalse()
    {
        // プロセス不在を返却
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi();
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        bool actual = controller.TryResolveTarget(out Process process, out IntPtr hwnd);

        Assert.IsFalse(actual);
        Assert.IsNull(process);
        Assert.AreEqual(IntPtr.Zero, hwnd);
    }

    [TestMethod]
    public void TryResolveTarget_WhenSingleProcessExists_ReturnsProcessAndHandle()
    {
        // 単一候補を解決
        Process current = Process.GetCurrentProcess();
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => new[] { current },
            WaitMainWindowHandleHandler = (_, _) => new IntPtr(123)
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        bool actual = controller.TryResolveTarget(out Process process, out IntPtr hwnd);

        Assert.IsTrue(actual);
        Assert.IsNotNull(process);
        Assert.AreEqual(current.Id, process.Id);
        Assert.AreEqual(new IntPtr(123), hwnd);
    }

    [TestMethod]
    public void TryResolveTargetByPid_InvalidInputs_ReturnFalse()
    {
        // 不正pidを拒否
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessByIdHandler = _ => throw new InvalidOperationException()
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        Assert.IsFalse(controller.TryResolveTargetByPid(0, out _, out _));
        Assert.IsFalse(controller.TryResolveTargetByPid(-1, out _, out _));
        Assert.IsFalse(controller.TryResolveTargetByPid(1, out _, out _));
    }

    [TestMethod]
    public void TryResolveTargetByPid_ProcessNameMismatch_ReturnsFalse()
    {
        // voicepeak以外のpidを拒否
        Process current = Process.GetCurrentProcess();
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessByIdHandler = _ => current
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        bool actual = controller.TryResolveTargetByPid(current.Id, out _, out _);

        Assert.IsFalse(actual);
    }

    [TestMethod]
    public void PressPlay_SendsKillFocusAndShortcut()
    {
        // 再生前にフォーカス解除してショートカット送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig { PlayShortcut = "Space", PlayPreShortcutDelayMs = 0 };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            bool result = controller.PressPlay(window.Handle);
            Assert.IsTrue(result);
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == 0x0008));
        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x20));
    }

    [TestMethod]
    public void MoveToStart_SendsConfiguredShortcutKey()
    {
        // 先頭移動ショートカットを送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartShortcut = "Home"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x24));
    }

    [TestMethod]
    public void PressDelete_SendsExpectedKey()
    {
        // deleteキー送信を確認
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(new UiConfig(), new FakeVoicepeakProcessApi());

            Assert.IsTrue(controller.PressDelete(window.Handle));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x2E));
    }

    [TestMethod]
    public void ReadInputTextDetailed_NoCandidate_ReturnsNoCandidate()
    {
        // 候補なしはNoCandidate
        var result = ReflectionTestHelper.RunInSta(() =>
        {
            using Form form = new Form();
            form.Show();
            Application.DoEvents();

            VoicepeakUiController controller = CreateController(new UiConfig(), new FakeVoicepeakProcessApi());
            return controller.ReadInputTextDetailed(form.Handle);
        });

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ReadInputSource.NoCandidate, result.Source);
    }

    private static VoicepeakUiController CreateController(UiConfig ui, IVoicepeakProcessApi processApi)
    {
        return new VoicepeakUiController(ui, new DebugConfig(), new AppLogger(new TestLogger()), processApi);
    }
}
