using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class AppConfigValidationTests
{
    [TestMethod]
    public void AppConfig_DefaultValues_AreExpected()
    {
        // 既定値を確認
        AppConfig config = new AppConfig();

        Assert.AreEqual(500, config.Server.MaxQueuedJobs);
        Assert.AreEqual(50, config.Audio.PollIntervalMs);
        Assert.AreEqual(1000, config.Audio.StartConfirmTimeoutMs);
        Assert.AreEqual(0, config.Audio.StartConfirmMaxRetries);
        Assert.AreEqual(300, config.Audio.StopConfirmMs);
        Assert.AreEqual("初期化完了", config.Prepare.BootValidationText);
        Assert.AreEqual(5, config.Prepare.SequentialMoveToStartKeyDelayBaseMs);
        Assert.AreEqual(1, config.Prepare.DeleteKeyDelayBaseMs);
        Assert.AreEqual(20, config.Prepare.ClearInputMaxPasses);
        Assert.AreEqual(500, config.Prepare.HookCommandTimeoutMs);
        Assert.AreEqual(300, config.Prepare.HookConnectTimeoutMs);
        Assert.AreEqual(8000, config.Prepare.HookConnectTotalWaitMs);
        Assert.AreEqual("Ctrl+Up", config.Ui.MoveToStartShortcut);
        Assert.IsTrue(config.Ui.ClickAtValidationEnabled);
        Assert.IsFalse(config.Ui.ClickBeforeTextFocusWhenUninitializedEnabled);
        Assert.IsFalse(config.Ui.ClickOnStartTimeoutRetryEnabled);
        CollectionAssert.AreEqual(new[] { "。", "！", "？", "!", "?" }, config.Ui.SentenceBreakTriggers);
        Assert.AreEqual(BootValidationMode.Required, config.Validation.BootValidation);
        Assert.AreEqual(RequestValidationMode.Strict, config.Validation.RequestValidation);
    }

    [TestMethod]
    public void Validate_NullConfig_Throws()
    {
        // null設定を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("AppConfigValidator", "Validate", new object[] { null }));

        StringAssert.Contains(ex.Message, "config は null にできません");
    }

    [TestMethod]
    public void Validate_NullSections_Throw()
    {
        // 必須セクションnullを拒否
        AppConfig config = new AppConfig
        {
            Server = null,
            Audio = null,
            Prepare = null,
            Ui = null,
            TextTransform = null,
            Validation = null
        };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("AppConfigValidator", "Validate", config));

        StringAssert.Contains(ex.Message, "server は null にできません");
    }

    [TestMethod]
    public void Validate_InvalidNumericValues_Throw()
    {
        // 数値境界を検証
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Server.MaxQueuedJobs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.PollIntervalMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StopConfirmMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.ActionDelayMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.BootValidationMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.BootValidationRetryIntervalMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.PostTypeWaitPerCharMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.PostTypeWaitMinMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.SequentialMoveToStartKeyDelayBaseMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.DeleteKeyDelayBaseMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.ClearInputMaxPasses = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.HookCommandTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.HookConnectTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.HookConnectTotalWaitMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Ui.DelayBeforePlayShortcutMs = -1));
    }

    [TestMethod]
    public void Validate_BlankMoveToStartShortcut_Throws()
    {
        // 先頭移動設定の空白を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.MoveToStartShortcut = "   "));

        StringAssert.Contains(ex.Message, "ui.moveToStartShortcut");
    }

    [TestMethod]
    public void Validate_CtrlUpMoveToStartShortcut_IsAllowed()
    {
        // Ctrl+Upを許可
        ValidateWith(config => config.Ui.MoveToStartShortcut = "Ctrl+Up");
    }

    [TestMethod]
    public void Validate_InvalidPlayShortcut_Throws()
    {
        // 再生ショートカット不正を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.PlayShortcut = "Delete"));

        StringAssert.Contains(ex.Message, "ui.playShortcut");
    }

    [TestMethod]
    public void Validate_ModifierPlayShortcut_Throws()
    {
        // 修飾付き再生ショートカットを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.PlayShortcut = "Ctrl+F4"));

        StringAssert.Contains(ex.Message, "ui.playShortcut");
    }

    [TestMethod]
    public void Validate_BootValidationTextNull_Throws()
    {
        // 起動検証文字列nullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Prepare.BootValidationText = null));

        StringAssert.Contains(ex.Message, "prepare.bootValidationText");
    }

    [TestMethod]
    public void Validate_EmptyBootValidationText_IsAllowed()
    {
        // 空文字は許可
        ValidateWith(config => config.Prepare.BootValidationText = string.Empty);
    }

    [TestMethod]
    public void Validate_SentenceBreakTriggers_NullOrEmpty_Throw()
    {
        // 区切りトリガーの不正値を拒否
        InvalidOperationException nullEx = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.SentenceBreakTriggers = null));
        StringAssert.Contains(nullEx.Message, "ui.sentenceBreakTriggers");

        InvalidOperationException emptyEx = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.SentenceBreakTriggers = new List<string> { string.Empty }));
        StringAssert.Contains(emptyEx.Message, "ui.sentenceBreakTriggers[0]");
    }

    [TestMethod]
    public void Validate_SentenceBreakTriggers_NullElement_Throws()
    {
        // 区切りトリガーのnull要素を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.SentenceBreakTriggers = new List<string> { null }));

        StringAssert.Contains(ex.Message, "ui.sentenceBreakTriggers[0]");
    }

    [TestMethod]
    public void Validate_MultiCharacterSentenceBreakTrigger_IsAllowed()
    {
        // 複数文字トリガーを許可
        ValidateWith(config => config.Ui.SentenceBreakTriggers = new List<string> { "。", "。、。" });
    }

    [TestMethod]
    public void Validate_ReplaceRulesNull_Throws()
    {
        // 置換ルールnullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.TextTransform.ReplaceRules = null));

        StringAssert.Contains(ex.Message, "textTransform.replaceRules");
    }

    [TestMethod]
    public void Validate_BoundaryValues_AreAllowed()
    {
        // 下限境界値を許容
        ValidateWith(config =>
        {
            config.Server.MaxQueuedJobs = 0;
            config.Audio.PollIntervalMs = 1;
            config.Audio.StartConfirmTimeoutMs = 1;
            config.Audio.StartConfirmMaxRetries = 0;
            config.Audio.StopConfirmMs = 1;
            config.Prepare.ActionDelayMs = 0;
            config.Prepare.BootValidationMaxRetries = 0;
            config.Prepare.BootValidationRetryIntervalMs = 0;
            config.Prepare.PostTypeWaitPerCharMs = 0;
            config.Prepare.PostTypeWaitMinMs = 0;
            config.Prepare.SequentialMoveToStartKeyDelayBaseMs = 0;
            config.Prepare.DeleteKeyDelayBaseMs = 0;
            config.Prepare.ClearInputMaxPasses = 1;
            config.Prepare.HookCommandTimeoutMs = 1;
            config.Prepare.HookConnectTimeoutMs = 1;
            config.Prepare.HookConnectTotalWaitMs = 1;
            config.Ui.DelayBeforePlayShortcutMs = 0;
        });
    }

    [TestMethod]
    public void Validate_EachNullSection_ThrowsMatchingMessage()
    {
        // 各必須セクション名を確認
        StringAssert.Contains(AssertSectionNullThrows(config => config.Audio = null).Message, "audio は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Prepare = null).Message, "prepare は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Ui = null).Message, "ui は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.TextTransform = null).Message, "textTransform は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Validation = null).Message, "validation は null");
    }

    private static void ValidateWith(Action<AppConfig> configure)
    {
        // 設定を組み立てて検証
        AppConfig config = new AppConfig();
        configure(config);
        ReflectionTestHelper.InvokeCoreStatic("AppConfigValidator", "Validate", config);
    }

    private static InvalidOperationException AssertSectionNullThrows(Action<AppConfig> configure)
    {
        AppConfig config = new AppConfig();
        configure(config);
        return Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("AppConfigValidator", "Validate", config));
    }
}
