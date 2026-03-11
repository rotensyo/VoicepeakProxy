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
        Assert.AreEqual(1000, config.Audio.StartConfirmWindowMs);
        Assert.AreEqual(0, config.Audio.StartConfirmMaxRetries);
        Assert.AreEqual(300, config.Audio.StopConfirmMs);
        Assert.AreEqual("初期化完了", config.Prepare.BootValidationText);
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
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmWindowMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StopConfirmMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.ActionDelayMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.BootValidationMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.BootValidationRetryIntervalMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.PostTypeWaitPerCharMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Prepare.PostTypeWaitMinMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Ui.PlayPreShortcutDelayMs = -1));
    }

    [TestMethod]
    public void Validate_InvalidShortcut_Throws()
    {
        // 無効ショートカットを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.MoveToStartShortcut = "Delete"));

        StringAssert.Contains(ex.Message, "ui.moveToStartShortcut");
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
            config.Audio.StartConfirmWindowMs = 1;
            config.Audio.StartConfirmMaxRetries = 0;
            config.Audio.StopConfirmMs = 1;
            config.Prepare.ActionDelayMs = 0;
            config.Prepare.BootValidationMaxRetries = 0;
            config.Prepare.BootValidationRetryIntervalMs = 0;
            config.Prepare.PostTypeWaitPerCharMs = 0;
            config.Prepare.PostTypeWaitMinMs = 0;
            config.Ui.PlayPreShortcutDelayMs = 0;
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
