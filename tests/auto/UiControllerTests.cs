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
    private const int WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;
    private const int VkDelete = 0x2E;
    private const int VkPageUp = 0x21;
    private const int VkUp = 0x26;
    private const int VkF3 = 0x72;

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
    public void IsValidMoveToStartShortcut_AllowsAnyNonBlankValue()
    {
        // 先頭移動設定は非空文字列を許可
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "Ctrl+Up"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "Alt+Up"));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "Delete"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", ""));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsValidMoveToStartShortcut", "   "));
        Assert.IsFalse(VoicepeakUiController.IsValidMoveToStartShortcut(null));
    }

    [TestMethod]
    public void IsFunctionKeyMoveToStartShortcut_ReturnsExpectedValues()
    {
        // Fキー系のみ独自ルート対象
        Assert.IsTrue(VoicepeakUiController.IsFunctionKeyMoveToStartShortcut("F3"));
        Assert.IsFalse(VoicepeakUiController.IsFunctionKeyMoveToStartShortcut("Ctrl+Up"));
        Assert.IsFalse(VoicepeakUiController.IsFunctionKeyMoveToStartShortcut("Home"));
        Assert.IsFalse(VoicepeakUiController.IsFunctionKeyMoveToStartShortcut("Delete"));
    }

    [TestMethod]
    public void ShouldAttemptPrimeInputContext_FunctionShortcut_ReturnsFalse()
    {
        // Fキー独自ルートではprimeしない
        VoicepeakUiController controller = CreateController(new UiConfig { MoveToStartShortcut = "F3" }, new FakeVoicepeakProcessApi());
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
    public void IsCollectTextCandidateTarget_UsesAllowedControlTypeAndStrictEmptyName()
    {
        // 候補条件は型と空文字名のみを許可
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Edit, string.Empty));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Document, string.Empty));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Text, string.Empty));

        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Button, string.Empty));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Edit, "name"));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Edit, " "));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCollectTextCandidateTarget", ControlType.Edit, null));
    }

    [TestMethod]
    public void EstimateVisibleBlockCount_EmptyTextInput_IsCounted()
    {
        // 空文字入力欄も候補数として数える
        int count = ReflectionTestHelper.RunInSta(() =>
        {
            using Form form = new Form
            {
                Text = "",
                Width = 400,
                Height = 200
            };
            using TextBox textBox = new TextBox
            {
                Name = string.Empty,
                AccessibleName = string.Empty,
                Text = string.Empty,
                Left = 20,
                Top = 20,
                Width = 200
            };

            form.Controls.Add(textBox);
            form.Show();
            Application.DoEvents();

            return (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "EstimateVisibleBlockCount", form.Handle);
        });

        Assert.IsTrue(count >= 1, $"count={count}");
    }

    [TestMethod]
    public void IsCompositeClearCompleted_RequiresSingleInputBox()
    {
        // 完全削除判定は入力欄1件を必須
        ReadInputResult cleared = ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
        ReadInputResult hasText = ReadInputResult.Ok("a", 1, ReadInputSource.PrimaryUiA);
        ReadInputResult failed = ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);

        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCompositeClearCompleted", cleared, 1));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCompositeClearCompleted", cleared, 2));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCompositeClearCompleted", hasText, 1));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsCompositeClearCompleted", failed, 1));
    }

    [TestMethod]
    public void ComputeCompositeDeleteSteps_AddsInputBoxCount()
    {
        // 削除ステップは文字数と入力欄数を加算
        ReadInputResult ok = ReadInputResult.Ok("abc", 3, ReadInputSource.PrimaryUiA);
        ReadInputResult negative = ReadInputResult.Ok(string.Empty, -1, ReadInputSource.PrimaryUiA);
        ReadInputResult failed = ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 5);

        Assert.AreEqual(15, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeCompositeDeleteSteps", ok, 2));
        Assert.AreEqual(12, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeCompositeDeleteSteps", negative, 2));
        Assert.AreEqual(12, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeCompositeDeleteSteps", failed, 2));
    }

    [TestMethod]
    public void ComputeNonCompositeDeleteSteps_AddsInputBoxCountAndKeepsMinimum()
    {
        // 非複合削除は入力欄件数を加算し最小値を維持
        ReadInputResult ok = ReadInputResult.Ok("abc", 3, ReadInputSource.PrimaryUiA);
        ReadInputResult negative = ReadInputResult.Ok(string.Empty, -1, ReadInputSource.PrimaryUiA);
        ReadInputResult failed = ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 5);

        Assert.AreEqual(18, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeNonCompositeDeleteSteps", ok, 5));
        Assert.AreEqual(11, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeNonCompositeDeleteSteps", negative, 1));
        Assert.AreEqual(14, (int)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ComputeNonCompositeDeleteSteps", failed, 4));
    }

    [TestMethod]
    public void ClearInput_UsesPrepareClearInputMaxPasses_ForNonCompositePath()
    {
        // 非複合経路は設定した最大試行回数でループ
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartShortcut = "F3"
            };
            PrepareConfig prepare = new PrepareConfig
            {
                ClearInputMaxPasses = 2,
                DeleteKeyDelayBaseMs = 0
            };
            VoicepeakUiController controller = new VoicepeakUiController(
                ui,
                prepare,
                new DebugConfig(),
                new AppLogger(new TestLogger()),
                new FakeVoicepeakProcessApi());

            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            bool ok = controller.ClearInput(null, window.Handle, 0, false);
            Assert.IsFalse(ok);
            return window.Messages.ToArray();
        });

        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkF3));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkF3));
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
            UiConfig ui = new UiConfig { PlayShortcut = "Space", DelayBeforePlayShortcutMs = 0 };
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
    public void PressPlay_CompositeShortcut_DoesNotPrimeFocusBeforeSpace()
    {
        // Space停止はKillFocusのみでフォーカス投入しない
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig { PlayShortcut = "Space", DelayBeforePlayShortcutMs = 0, MoveToStartShortcut = "Ctrl+Up" };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            bool result = controller.PressPlay(window.Handle);
            Assert.IsTrue(result);
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == 0x0008));
        Assert.IsFalse(messages.Any(m => m.Msg == 0x0006));
        Assert.IsFalse(messages.Any(m => m.Msg == 0x0007));
        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x20));
    }

    [TestMethod]
    public void MoveToStart_FunctionShortcut_SendsConfiguredShortcutKey()
    {
        // Fキー独自ルートは設定ショートカットを送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartShortcut = "F3"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkF3));
    }

    [TestMethod]
    public void MoveToStart_CompositeShortcut_UsesPageUpAndUpByInjectedEnterCountPlusOne()
    {
        // 複合先頭移動はEnter回数+1だけPageUpとUpを送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartShortcut = "Ctrl+Up",
                SendEnterAfterSentenceBreak = true
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            Assert.IsTrue(controller.TypeText(window.Handle, "A。B。C", 0));
            window.Messages.Clear();

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == 0x0008));
        Assert.IsTrue(messages.Any(m => m.Msg == 0x0007));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkUp));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkUp));
    }

    [TestMethod]
    public void MoveToStart_CompositeShortcut_SendsKillFocusAndSetFocus()
    {
        // 非F系先頭移動はKillFocusとSetFocusを送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartShortcut = "Ctrl+Up"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());
            window.Messages.Clear();

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == 0x0007), "set_focus_not_sent");
        Assert.IsTrue(messages.Any(m => m.Msg == 0x0008), "kill_focus_not_sent");
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
    public void RunCompositeClearCycle_SendsPageUpUpAndDelete()
    {
        // 複合削除サイクルはPageUpとUpの後にDeleteを送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            object controller = ReflectionTestHelper.CreateVoicepeakUiController(
                new UiConfig { MoveToStartShortcut = "Ctrl+Up" },
                new DebugConfig(),
                new TestLogger());

            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            bool ok = (bool)ReflectionTestHelper.InvokeCoreInstance(controller, "RunCompositeClearCycle", window.Handle, 2, 3, 0);
            Assert.IsTrue(ok);
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == 0x0008));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkUp));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkUp));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkDelete));
        Assert.AreEqual(3, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkDelete));
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
