using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;
using System.Diagnostics;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class ExecutionLogicTests
{
    [TestMethod]
    public void ComputePostTypeWaitMs_Empty_ReturnsZero()
    {
        // 空文字は待機なし
        int actual = (int)ReflectionTestHelper.InvokeCoreStatic("JobExecutionCore", "ComputePostTypeWaitMs", string.Empty, 4, 100);

        Assert.AreEqual(0, actual);
    }

    [TestMethod]
    public void ComputePostTypeWaitMs_UsesLongestChunk()
    {
        // 最長チャンクで算出
        int actual = (int)ReflectionTestHelper.InvokeCoreStatic("JobExecutionCore", "ComputePostTypeWaitMs", "abc。d", 10, 5);

        Assert.AreEqual(30, actual);
    }

    [TestMethod]
    public void ComputePostTypeWaitMs_RespectsMinimum()
    {
        // 最小待機を優先
        int actual = (int)ReflectionTestHelper.InvokeCoreStatic("JobExecutionCore", "ComputePostTypeWaitMs", "a。b", 2, 100);

        Assert.AreEqual(100, actual);
    }

    [TestMethod]
    public void ComputePostTypeWaitMs_Null_ReturnsZero()
    {
        // null文字列は待機なし
        int actual = JobExecutionCore.ComputePostTypeWaitMs(null, 4, 100);

        Assert.AreEqual(0, actual);
    }

    [TestMethod]
    public void ComputePostTypeWaitMs_NoDelimiter_UsesWholeLength()
    {
        // 区切りなしは全文長で算出
        int actual = JobExecutionCore.ComputePostTypeWaitMs("abcd", 3, 0);

        Assert.AreEqual(12, actual);
    }

    [TestMethod]
    public void AdjustPauseByStopConfirmAndPlayDelay_SubtractsCompensationAndWarns()
    {
        // 補正後下限と警告を検証
        AppConfig config = new AppConfig();
        config.Audio.StopConfirmMs = 300;
        config.Ui.DelayBeforePlayShortcutMs = 60;
        config.InputTiming.PostTypeWaitPerCharMs = 4;
        config.InputTiming.PostTypeWaitMinMs = 100;
        TestLogger logger = new TestLogger();

        int actual = (int)ReflectionTestHelper.InvokeCoreStatic(
            "JobExecutionCore",
            "AdjustPauseByStopConfirmAndPlayDelay",
            config,
            200,
            "pre",
            "job-1",
            0,
            "hello",
            ReflectionTestHelper.CreateAppLogger(logger));

        Assert.AreEqual(0, actual);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("pause_too_short")));
    }

    [TestMethod]
    public void AdjustPauseByStopConfirmAndPlayDelay_TrailingPhase_DoesNotAddTypingCost()
    {
        // trailingは入力待機を加算しない
        AppConfig config = new AppConfig();
        config.Audio.StopConfirmMs = 30;
        config.Ui.DelayBeforePlayShortcutMs = 20;
        TestLogger logger = new TestLogger();

        int actual = JobExecutionCore.AdjustPauseByStopConfirmAndPlayDelay(config, 120, "trailing", "job-1", 0, null, new AppLogger(logger));

        Assert.AreEqual(70, actual);
        Assert.AreEqual(0, logger.WarnMessages.Count);
    }

    [TestMethod]
    public void PrepareSegment_ReturnsFalseWhenProcessNotAlive()
    {
        // プロセス未生存で失敗
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            IsAliveHandler = _ => false
        };
        TestLogger logger = new TestLogger();

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(logger));

        Assert.IsFalse(actual);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("process_not_alive")));
    }

    [TestMethod]
    public void PrepareSegment_ReturnsFalseWhenClearInputFails()
    {
        // クリア失敗で中断
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ClearInputHandler = () => false
        };
        TestLogger logger = new TestLogger();

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(logger));

        Assert.IsFalse(actual);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("clear_input_failed")));
    }

    [TestMethod]
    public void PrepareSegment_ClearInputRetry_SucceedsWithinConfiguredRetries()
    {
        // 入力クリア失敗を再試行で回復
        AppConfig config = new AppConfig();
        config.InputTiming.ClearInputRetryMaxRetries = 2;
        config.InputTiming.ClearInputRetryWaitMs = 0;
        int clearAttempts = 0;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ClearInputHandler = () =>
            {
                clearAttempts++;
                return clearAttempts >= 2;
            },
            ReadInputHandler = _ => ReadInputResult.Ok("hello", 5, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(config, ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(new TestLogger()));

        Assert.IsTrue(actual);
        Assert.AreEqual(2, ui.ClearInputCalls);
    }

    [TestMethod]
    public void PrepareSegment_UsesPrepareForTextInputBeforeClearAndType()
    {
        // 入力準備の順序を固定
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok("hello", 5, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(new TestLogger()));

        Assert.IsTrue(actual);
        CollectionAssert.AreEqual(new[] { "prepare_text", "clear_input", "type_text" }, ui.CallLog);
    }

    [TestMethod]
    public void PrepareSegment_ReturnsFalseWhenTypeFails()
    {
        // 文字入力失敗で中断
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            TypeTextHandler = (_, _) => false
        };
        TestLogger logger = new TestLogger();

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(logger));

        Assert.IsFalse(actual);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("type_text_failed")));
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("text=\"hello\"")));
    }

    [TestMethod]
    public void PrepareSegment_InputEmptyAfterType_RetriesOnceAndSucceeds()
    {
        // 入力反映なし時は1回だけ再入力して成功
        AppConfig config = new AppConfig();
        config.InputTiming.TypeTextRetryWaitMs = 0;
        Queue<ReadInputResult> reads = new Queue<ReadInputResult>();
        reads.Enqueue(ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA));
        reads.Enqueue(ReadInputResult.Ok("hello", 5, ReadInputSource.PrimaryUiA));
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => reads.Count > 0 ? reads.Dequeue() : ReadInputResult.Ok("hello", 5, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(config, ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(new TestLogger()));

        Assert.IsTrue(actual);
        Assert.AreEqual(2, ui.PrepareForTextInputCalls);
        Assert.AreEqual(2, ui.TypedTexts.Count);
    }

    [TestMethod]
    public void PrepareSegment_InputEmptyAfterRetry_ReturnsFalse()
    {
        // 再入力後も反映なしなら失敗
        AppConfig config = new AppConfig();
        config.InputTiming.TypeTextRetryWaitMs = 0;
        config.InputTiming.TypeTextRetryMaxRetries = 1;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };
        TestLogger logger = new TestLogger();

        bool actual = JobExecutionCore.PrepareSegment(config, ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(logger));

        Assert.IsFalse(actual);
        Assert.AreEqual(2, ui.PrepareForTextInputCalls);
        Assert.AreEqual(2, ui.TypedTexts.Count);
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("typed_text_not_reflected_after_retry")));
        Assert.IsTrue(logger.WarnMessages.Exists(m => m.Contains("text=\"hello\"")));
    }

    [TestMethod]
    public void PrepareSegment_InputEmptyAfterType_RespectsRetryMaxRetries()
    {
        // 再入力回数設定どおりに試行
        AppConfig config = new AppConfig();
        config.InputTiming.TypeTextRetryMaxRetries = 2;
        config.InputTiming.TypeTextRetryWaitMs = 0;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok(string.Empty, 0, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(config, ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(new TestLogger()));

        Assert.IsFalse(actual);
        Assert.AreEqual(3, ui.PrepareForTextInputCalls);
        Assert.AreEqual(3, ui.TypedTexts.Count);
    }

    [TestMethod]
    public void PrepareSegment_NormalizesTextBeforeTyping()
    {
        // 正規化後文字列を送信
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok("a b", 3, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "  a\r\n b  ", new AppLogger(new TestLogger()));

        Assert.IsTrue(actual);
        CollectionAssert.AreEqual(new[] { "a\nb" }, ui.TypedTexts);
    }

    [TestMethod]
    public void PrepareSegment_CallsPrepareForTextInputOnce()
    {
        // 入力準備は1回だけ実行
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok("hello", 5, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        bool actual = JobExecutionCore.PrepareSegment(new AppConfig(), ui, Process.GetCurrentProcess(), IntPtr.Zero, "hello", new AppLogger(new TestLogger()));

        Assert.IsTrue(actual);
        Assert.AreEqual(1, ui.PrepareForTextInputCalls);
    }

    [TestMethod]
    public void ValidateInputText_RemovesNewlinesAndTrimsBeforeTyping()
    {
        // 検証入力は改行除去とtrimを適用
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            ReadInputHandler = _ => ReadInputResult.Ok("A B", 3, ReadInputSource.PrimaryUiA),
            VisibleInputBlockCountHandler = _ => 1
        };

        object result = ReflectionTestHelper.InvokeCoreStatic(
            "JobExecutionCore",
            "ValidateInputText",
            new AppConfig(),
            ui,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            "  A\r\n B  ",
            false);

        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(result, "Success"));
        CollectionAssert.AreEqual(new[] { "A B" }, ui.TypedTexts);
    }

    [TestMethod]
    public void HandleStartTimeoutRetry_RetryAvailable_ReturnsTrue()
    {
        // 次試行があれば再試行する
        AppConfig config = new AppConfig();
        config.Audio.StartConfirmMaxRetries = 2;

        bool actual = JobExecutionCore.HandleStartTimeoutRetry(config, 0);

        Assert.IsTrue(actual);
    }

    [TestMethod]
    public void HandleStartTimeoutRetry_LastAttempt_ReturnsFalse()
    {
        // 最終試行では再試行しない
        AppConfig config = new AppConfig();
        config.Audio.StartConfirmMaxRetries = 1;

        bool actual = JobExecutionCore.HandleStartTimeoutRetry(config, 1);

        Assert.IsFalse(actual);
    }

    [TestMethod]
    public void MonitorSpeaking_StopRequested_ReturnsInterrupted()
    {
        // 停止要求で即中断
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            CreateMonitorConfig(),
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => true,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.Interrupted, result.Kind);
    }

    [TestMethod]
    public void MonitorSpeaking_InterruptRequested_ReturnsInterruptedAndMovesToStart()
    {
        // 割込み要求で先頭移動して中断
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        bool callbackCalled = false;

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            CreateMonitorConfig(),
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => true,
            () => callbackCalled = true);

        Assert.AreEqual(SpeakMonitorKind.Interrupted, result.Kind);
        Assert.IsTrue(callbackCalled);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.AreEqual(1, ui.MoveToStartCalls);
    }

    [TestMethod]
    public void MonitorSpeaking_InterruptRequested_WithModifierShortcut_MovesToStartWithoutPrePlay()
    {
        // 修飾子ショートカットは先頭移動のみを実行
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        bool callbackCalled = false;
        AppConfig config = CreateMonitorConfig();
        config.Ui.MoveToStartModifier = "ctrl";
        config.Ui.MoveToStartKey = "cursor up";

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => true,
            () => callbackCalled = true);

        Assert.AreEqual(SpeakMonitorKind.Interrupted, result.Kind);
        Assert.IsTrue(callbackCalled);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.AreEqual(1, ui.MoveToStartCalls);
    }

    [TestMethod]
    public void MonitorSpeaking_ProcessLost_ReturnsProcessLost()
    {
        // プロセス消失を返却
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController
        {
            IsAliveHandler = _ => false
        };
        FakeAudioSessionReader audio = new FakeAudioSessionReader();

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            CreateMonitorConfig(),
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.ProcessLost, result.Kind);
    }

    [TestMethod]
    public void MonitorSpeaking_StartTimeout_ReturnsStartTimeout()
    {
        // 開始確認失敗を返却
        AppConfig config = CreateMonitorConfig();
        config.Audio.StartConfirmTimeoutMs = 1;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.StartTimeout, result.Kind);
    }

    [TestMethod]
    public void MonitorSpeaking_DelayedStart_DoesNotCountTowardMaxDuration()
    {
        // 開始前待機は最大発話時間に含めない
        AppConfig config = CreateMonitorConfig();
        config.Audio.StartConfirmTimeoutMs = 1100;
        config.Audio.MaxSpeakingDurationSec = 1;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.StartTimeout, result.Kind);
    }

    [TestMethod]
    public void MonitorSpeaking_MaxDuration_ReturnsMaxDurationAndMovesToStart()
    {
        // 最大発話時間超過を返却
        AppConfig config = CreateMonitorConfig();
        config.Audio.MaxSpeakingDurationSec = 1;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.MaxDuration, result.Kind);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.AreEqual(1, ui.MoveToStartCalls);
    }

    [TestMethod]
    public void MonitorSpeaking_MaxDuration_WithModifierShortcut_MovesToStartWithoutPrePlay()
    {
        // 修飾子ショートカットは先頭移動のみを実行
        AppConfig config = CreateMonitorConfig();
        config.Ui.MoveToStartModifier = "ctrl";
        config.Ui.MoveToStartKey = "cursor up";
        config.Audio.MaxSpeakingDurationSec = 1;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" };

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.MaxDuration, result.Kind);
        Assert.AreEqual(0, ui.PressPlayCalls);
        Assert.AreEqual(1, ui.MoveToStartCalls);
    }

    [TestMethod]
    public void MonitorSpeaking_Completed_ReturnsCompleted()
    {
        // 開始後に停止確認で完了
        AppConfig config = CreateMonitorConfig();
        config.Audio.StopConfirmMs = 2;
        FakeVoicepeakUiController ui = new FakeVoicepeakUiController();
        FakeAudioSessionReader audio = new FakeAudioSessionReader();
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 1f, StateLabel = "AudioSessionStateActive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Snapshots.Enqueue(new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" });
        audio.Fallback = new AudioSessionSnapshot { Found = true, Peak = 0f, StateLabel = "AudioSessionStateInactive" };

        SpeakMonitorResult result = JobExecutionCore.MonitorSpeaking(
            config,
            ui,
            audio,
            Process.GetCurrentProcess(),
            IntPtr.Zero,
            new AppLogger(new TestLogger()),
            () => false,
            () => false,
            null);

        Assert.AreEqual(SpeakMonitorKind.Completed, result.Kind);
        Assert.IsTrue(result.SegEndAtMs > 0);
    }

    [TestMethod]
    public void BuildInputMismatchCause_ReturnsExpectedReasons()
    {
        // 不一致理由分類を固定
        Assert.AreEqual(
            "actual_empty_or_read_failed",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", string.Empty, true));
        Assert.AreEqual(
            "guard_chars_not_removed",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", "Aabc", true));
        Assert.AreEqual(
            "leading_guard_remaining_move_to_start_or_delete_issue",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", "AabcX", true));
        Assert.AreEqual(
            "contains_expected_but_not_exact",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", "abcB", true));
        Assert.AreEqual(
            "contains_expected_but_not_exact",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", "xabcx", false));
        Assert.AreEqual(
            "actual_unexpected_or_target_mismatch",
            ReflectionTestHelper.InvokeCoreStatic("VoicepeakEngine", "BuildInputMismatchCause", "abc", "zzz", false));
    }

    private static AppConfig CreateMonitorConfig()
    {
        return new AppConfig
        {
            Audio =
            {
                PollIntervalMs = 1,
                StartConfirmTimeoutMs = 10,
                StopConfirmMs = 1,
                PeakThreshold = 0.5f,
                MaxSpeakingDurationSec = 0
            }
        };
    }
}
