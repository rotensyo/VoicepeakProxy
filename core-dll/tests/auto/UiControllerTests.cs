using System;
using System.Collections.Generic;
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
    private const int WmChar = 0x0102;
    private const int VkReturn = 0x0D;
    private const int VkDelete = 0x2E;
    private const int VkPageUp = 0x21;
    private const int VkUp = 0x26;
    private const int VkF3 = 0x72;

    [TestMethod]
    public void IsValidPlayShortcutModifier_ReturnsExpectedValues()
    {
        // 再生修飾子は空文字とctrlとaltとshiftを許可
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutModifier(string.Empty));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutModifier("CTRL"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutModifier("alt"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutModifier("shift"));
        Assert.IsFalse(VoicepeakUiController.IsValidPlayShortcutModifier(null));
    }

    [TestMethod]
    public void IsValidPlayShortcutKey_ReturnsExpectedValues()
    {
        // 再生キーは英数記号を含む許可集合のみ有効
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("F3"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("Spacebar"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("Home"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("A"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("9"));
        Assert.IsTrue(VoicepeakUiController.IsValidPlayShortcutKey("@"));
        Assert.IsFalse(VoicepeakUiController.IsValidPlayShortcutKey("Ctrl+F4"));
        Assert.IsFalse(VoicepeakUiController.IsValidPlayShortcutKey("Shift+Space"));
        Assert.IsFalse(VoicepeakUiController.IsValidPlayShortcutKey("Alt+F3"));
    }

    [TestMethod]
    public void IsValidMoveToStartModifier_ReturnsExpectedValues()
    {
        // 先頭移動修飾子は空文字とctrlとaltを許可
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartModifier(string.Empty));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartModifier("CTRL"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartModifier("alt"));
        Assert.IsFalse(VoicepeakUiController.IsValidMoveToStartModifier("shift"));
        Assert.IsFalse(VoicepeakUiController.IsValidMoveToStartModifier(null));
    }

    [TestMethod]
    public void IsValidMoveToStartKey_ReturnsExpectedValues()
    {
        // 先頭移動キーは英数記号を含む許可集合のみ有効
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("cursor up"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("CURSOR LEFT"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("F3"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("home"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("Z"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("0"));
        Assert.IsTrue(VoicepeakUiController.IsValidMoveToStartKey("@"));
        Assert.IsFalse(VoicepeakUiController.IsValidMoveToStartKey("up"));
        Assert.IsFalse(VoicepeakUiController.IsValidMoveToStartKey("delete"));
    }

    [TestMethod]
    public void IsValidClearInputSelectAllModifier_ReturnsExpectedValues()
    {
        // 全選択修飾子は空文字とctrlとaltを許可
        Assert.IsTrue(VoicepeakUiController.IsValidClearInputSelectAllModifier(string.Empty));
        Assert.IsTrue(VoicepeakUiController.IsValidClearInputSelectAllModifier("CTRL"));
        Assert.IsTrue(VoicepeakUiController.IsValidClearInputSelectAllModifier("alt"));
        Assert.IsFalse(VoicepeakUiController.IsValidClearInputSelectAllModifier("shift"));
        Assert.IsFalse(VoicepeakUiController.IsValidClearInputSelectAllModifier(null));
    }

    [TestMethod]
    public void IsValidClearInputSelectAllKey_ReturnsExpectedValues()
    {
        // 全選択キーは共通キー集合を許可
        Assert.IsTrue(VoicepeakUiController.IsValidClearInputSelectAllKey("A"));
        Assert.IsTrue(VoicepeakUiController.IsValidClearInputSelectAllKey("@"));
        Assert.IsFalse(VoicepeakUiController.IsValidClearInputSelectAllKey(""));
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
    public void ShouldTraverseChildren_StopsAtNonClientUpperLayers()
    {
        // 非クライアント上位層の深掘りを止める
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.TitleBar));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.MenuBar));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.Menu));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.Button));

        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.Window));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.Pane));
        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "ShouldTraverseChildren", ControlType.Edit));
    }

    [TestMethod]
    public void EnqueueRootCandidateChildren_EnqueuesTextEditDocumentOnly()
    {
        // root直下では候補型のみを探索対象へ投入
        int count = ReflectionTestHelper.RunInSta(() =>
        {
            using Form form = new Form
            {
                Text = string.Empty,
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
            using Button button = new Button
            {
                Text = "button",
                Left = 20,
                Top = 60,
                Width = 100
            };
            using Panel panel = new Panel
            {
                Left = 20,
                Top = 100,
                Width = 100,
                Height = 40
            };

            form.Controls.Add(textBox);
            form.Controls.Add(button);
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            AutomationElement root = AutomationElement.FromHandle(form.Handle);
            Queue<AutomationElement> queue = new Queue<AutomationElement>();
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "EnqueueRootCandidateChildren", root, queue);
            return queue.Count;
        });

        Assert.AreEqual(1, count);
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
    public void IsClearCompleted_RequiresSingleInputBox()
    {
        // 完全削除判定は入力欄1件を必須
        ReadInputResult cleared = ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA);
        ReadInputResult hasText = ReadInputResult.Ok("a", 1, ReadInputSource.PrimaryUiA);
        ReadInputResult failed = ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);

        Assert.IsTrue((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsClearCompleted", cleared, 1));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsClearCompleted", cleared, 2));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsClearCompleted", hasText, 1));
        Assert.IsFalse((bool)ReflectionTestHelper.InvokeCoreStatic("VoicepeakUiController", "IsClearCompleted", failed, 1));
    }

    [TestMethod]
    public void ClearInput_UsesClearInputMaxPasses_ForSelectAllDeleteCycle()
    {
        // クリア処理は設定した最大パス回数で全選択削除を繰り返す
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartModifier = string.Empty,
                MoveToStartKey = "F3",
                ClearInputSelectAllModifier = "ctrl",
                ClearInputSelectAllKey = "a"
            };
            InputTimingConfig inputTiming = new InputTimingConfig
            {
                ClearInputMaxPasses = 2
            };
            VoicepeakUiController controller = new VoicepeakUiController(
                ui,
                inputTiming,
                new HookConfig(),
                new TextConfig(),
                new DebugConfig(),
                new AppLogger(new TestLogger()),
                new FakeVoicepeakProcessApi());

            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            bool ok = controller.ClearInput(null, window.Handle, 0);
            Assert.IsFalse(ok);
            return window.Messages.ToArray();
        });

        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkF3));
        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkF3));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x41));
        Assert.AreEqual(2, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == 0x41));
        Assert.AreEqual(4, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkDelete));
        Assert.AreEqual(4, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkDelete));
    }

    [TestMethod]
    public void ClearInput_WithModifierShortcut_DoesNotUsePageUpPath()
    {
        // 修飾子ショートカット設定ではPageUp経路を使用しない
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartModifier = "ctrl",
                MoveToStartKey = "cursor up"
            };
            InputTimingConfig inputTiming = new InputTimingConfig
            {
                ClearInputMaxPasses = 1
            };
            VoicepeakUiController controller = new VoicepeakUiController(
                ui,
                inputTiming,
                new HookConfig(),
                new TextConfig(),
                new DebugConfig(),
                new AppLogger(new TestLogger()),
                new FakeVoicepeakProcessApi());

            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            bool ok = controller.ClearInput(null, window.Handle, 0);
            Assert.IsFalse(ok);
            return window.Messages.ToArray();
        });

        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkPageUp));
        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkUp));
    }

    [TestMethod]
    public void TypeText_NonVoicepeakWindow_ReturnsFalse()
    {
        // 非VOICEPEAKウィンドウでは入力失敗
        bool actual = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig();
            TestLogger logger = new TestLogger();
            object controller = ReflectionTestHelper.CreateCoreInstance("VoicepeakUiController", ui, new InputTimingConfig(), new HookConfig(), new TextConfig(), new DebugConfig(), ReflectionTestHelper.CreateAppLogger(logger));
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();

            return (bool)ReflectionTestHelper.InvokeCoreInstance(controller, "TypeText", window.Handle, "A\nB");
        });

        Assert.IsFalse(actual);
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
            GetProcessByIdHandler = _ => current,
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
    public void TryResolveTarget_WhenCachedPidIsValid_UsesPidResolutionWithoutNameLookup()
    {
        // 有効キャッシュpidを優先して名前探索を行わない
        Process current = Process.GetCurrentProcess();
        int getProcessesByNameCalls = 0;
        int getProcessByIdCalls = 0;
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ =>
            {
                getProcessesByNameCalls++;
                return new[] { current, current };
            },
            GetProcessByIdHandler = _ =>
            {
                getProcessByIdCalls++;
                return current;
            },
            WaitMainWindowHandleHandler = (_, _) => new IntPtr(321)
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);
        ReflectionTestHelper.SetField(controller, "_cachedVoicepeakPid", current.Id);

        bool actual = controller.TryResolveTarget(out Process process, out IntPtr hwnd);

        Assert.IsTrue(actual);
        Assert.AreEqual(1, getProcessesByNameCalls);
        Assert.AreEqual(2, getProcessByIdCalls);
        Assert.IsNotNull(process);
        Assert.AreEqual(current.Id, process.Id);
        Assert.AreEqual(new IntPtr(321), hwnd);
    }

    [TestMethod]
    public void TryResolveTarget_WhenCachedPidIsInvalid_FallsBackToNameLookupAndUpdatesCache()
    {
        // 無効キャッシュpid時は名前探索へフォールバックして更新
        Process current = Process.GetCurrentProcess();
        int getProcessesByNameCalls = 0;
        int getProcessByIdCalls = 0;
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessByIdHandler = pid =>
            {
                getProcessByIdCalls++;
                if (pid == 99999)
                {
                    throw new InvalidOperationException("not found");
                }

                return current;
            },
            GetProcessesByNameHandler = _ =>
            {
                getProcessesByNameCalls++;
                return new[] { current };
            },
            WaitMainWindowHandleHandler = (_, _) => new IntPtr(444)
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);
        ReflectionTestHelper.SetField(controller, "_cachedVoicepeakPid", 99999);

        bool actual = controller.TryResolveTarget(out Process process, out IntPtr hwnd);

        Assert.IsTrue(actual);
        Assert.AreEqual(2, getProcessByIdCalls);
        Assert.AreEqual(1, getProcessesByNameCalls);
        Assert.IsNotNull(process);
        Assert.AreEqual(current.Id, process.Id);
        Assert.AreEqual(new IntPtr(444), hwnd);
        Assert.AreEqual(current.Id, (int)ReflectionTestHelper.GetField(controller, "_cachedVoicepeakPid"));
    }

    [TestMethod]
    public void TryResolveTarget_WhenMultipleProcessesExist_ReturnsFalse()
    {
        // 複数候補時は失敗
        Process current = Process.GetCurrentProcess();
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => new[] { current, current }
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        bool actual = controller.TryResolveTarget(out Process process, out IntPtr hwnd);

        Assert.IsFalse(actual);
        Assert.IsNull(process);
        Assert.AreEqual(IntPtr.Zero, hwnd);
    }

    [TestMethod]
    public void TryResolveTargetDetailed_NoProcess_ReturnsProcessNotFound()
    {
        // 詳細解決で未起動理由を返す
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => Array.Empty<Process>()
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        ResolveTargetResult actual = controller.TryResolveTargetDetailed();

        Assert.IsFalse(actual.Success);
        Assert.AreEqual(ResolveTargetFailureReason.ProcessNotFound, actual.FailureReason);
        Assert.AreEqual(0, actual.ProcessCount);
    }

    [TestMethod]
    public void TryResolveTargetDetailed_MultipleProcesses_ReturnsMultipleProcesses()
    {
        // 詳細解決で複数起動理由を返す
        Process current = Process.GetCurrentProcess();
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => new[] { current, current }
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        ResolveTargetResult actual = controller.TryResolveTargetDetailed();

        Assert.IsFalse(actual.Success);
        Assert.AreEqual(ResolveTargetFailureReason.MultipleProcesses, actual.FailureReason);
        Assert.AreEqual(2, actual.ProcessCount);
    }

    [TestMethod]
    public void TryResolveTargetDetailed_WindowNotFound_ReturnsTargetNotFound()
    {
        // 詳細解決でウィンドウ未取得理由を返す
        Process current = Process.GetCurrentProcess();
        FakeVoicepeakProcessApi api = new FakeVoicepeakProcessApi
        {
            GetProcessesByNameHandler = _ => new[] { current },
            GetProcessByIdHandler = _ => current,
            WaitMainWindowHandleHandler = (_, _) => IntPtr.Zero
        };
        VoicepeakUiController controller = CreateController(new UiConfig(), api);

        ResolveTargetResult actual = controller.TryResolveTargetDetailed();

        Assert.IsFalse(actual.Success);
        Assert.AreEqual(ResolveTargetFailureReason.TargetNotFound, actual.FailureReason);
        Assert.AreEqual(1, actual.ProcessCount);
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
    public void TryResolveTargetByPid_MainWindowMissing_ReturnsFalse()
    {
        // メインウィンドウ未取得を失敗扱い
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
            UiConfig ui = new UiConfig { PlayShortcutModifier = string.Empty, PlayShortcutKey = "spacebar", DelayBeforePlayShortcutMs = 0 };
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
    public void PressPlay_WithCtrlModifier_SendsSinglePlayKey()
    {
        // 再生修飾子指定時も主キー単体を送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                PlayShortcutModifier = "ctrl",
                PlayShortcutKey = "spacebar",
                DelayBeforePlayShortcutMs = 0
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            bool result = controller.PressPlay(window.Handle);
            Assert.IsTrue(result);
            return window.Messages.ToArray();
        });

        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x20));
        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == 0x20));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x11));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == 0x11));
    }

    [TestMethod]
    public void PressPlay_WithShiftModifier_SendsSinglePlayKey()
    {
        // Shift修飾子指定時も主キー単体を送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                PlayShortcutModifier = "shift",
                PlayShortcutKey = "A",
                DelayBeforePlayShortcutMs = 0
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            bool result = controller.PressPlay(window.Handle);
            Assert.IsTrue(result);
            return window.Messages.ToArray();
        });

        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x41));
        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == 0x41));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == 0x10));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == 0x10));
    }

    [TestMethod]
    public void PressPlay_CompositeShortcut_DoesNotPrimeFocusBeforeSpace()
    {
        // Space停止はKillFocusのみでフォーカス投入しない
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig { PlayShortcutModifier = string.Empty, PlayShortcutKey = "spacebar", DelayBeforePlayShortcutMs = 0, MoveToStartModifier = "ctrl", MoveToStartKey = "cursor up" };
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
                MoveToStartModifier = string.Empty,
                MoveToStartKey = "F3"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsTrue(messages.Any(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkF3));
    }

    [TestMethod]
    public void MoveToStart_CtrlModifier_UsesSingleUpStroke()
    {
        // Ctrl修飾先頭移動はUpを単発送信
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartModifier = "ctrl",
                MoveToStartKey = "cursor up"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());
            window.Messages.Clear();

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(0, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkPageUp));
        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyDown && m.WParam.ToInt32() == VkUp));
        Assert.AreEqual(1, messages.Count(m => m.Msg == WmKeyUp && m.WParam.ToInt32() == VkUp));
    }

    [TestMethod]
    public void MoveToStart_CtrlModifier_DoesNotSendFocusMessages()
    {
        // Ctrl修飾先頭移動はフォーカスメッセージを送信しない
        var messages = ReflectionTestHelper.RunInSta(() =>
        {
            UiConfig ui = new UiConfig
            {
                MoveToStartModifier = "ctrl",
                MoveToStartKey = "cursor up"
            };
            using ReflectionTestHelper.MessageRecorderWindow window = new ReflectionTestHelper.MessageRecorderWindow();
            VoicepeakUiController controller = CreateController(ui, new FakeVoicepeakProcessApi());
            window.Messages.Clear();

            Assert.IsTrue(controller.MoveToStart(window.Handle, 0));
            return window.Messages.ToArray();
        });

        Assert.IsFalse(messages.Any(m => m.Msg == 0x0007), "set_focus_sent");
        Assert.IsFalse(messages.Any(m => m.Msg == 0x0008), "kill_focus_sent");
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

    [TestMethod]
    public void EndModifierIsolationSession_WhenDisableFails_ReturnsFalseAndClearsSessionState()
    {
        // 解除失敗時は再利用不能としてセッション状態を破棄
        VoicepeakUiController controller = CreateController(new UiConfig(), new FakeVoicepeakProcessApi());
        ReflectionTestHelper.SetField(controller, "_modifierIsolationSessionActive", true);
        ReflectionTestHelper.SetField(controller, "_modifierIsolationSessionProcessId", (uint)123);

        bool ok = controller.EndModifierIsolationSession("test_end_failure");

        Assert.IsFalse(ok);
        Assert.IsFalse((bool)ReflectionTestHelper.GetField(controller, "_modifierIsolationSessionActive"));
        Assert.AreEqual((uint)0, (uint)ReflectionTestHelper.GetField(controller, "_modifierIsolationSessionProcessId"));
    }

    private static VoicepeakUiController CreateController(
        UiConfig ui,
        IVoicepeakProcessApi processApi,
        InputTimingConfig inputTiming = null,
        HookConfig hook = null,
        TextConfig text = null,
        DebugConfig debug = null)
    {
        return new VoicepeakUiController(
            ui,
            inputTiming ?? new InputTimingConfig(),
            hook ?? new HookConfig(),
            text ?? new TextConfig(),
            debug ?? new DebugConfig(),
            new AppLogger(new TestLogger()),
            processApi);
    }
}
