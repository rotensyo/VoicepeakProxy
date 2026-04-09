using System;
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

        Assert.AreEqual(500, config.Runtime.MaxQueuedJobs);
        Assert.AreEqual(50, config.Audio.PollIntervalMs);
        Assert.AreEqual(1000, config.Audio.StartConfirmTimeoutMs);
        Assert.AreEqual(0, config.Audio.StartConfirmMaxRetries);
        Assert.AreEqual(300, config.Audio.StopConfirmMs);
        Assert.AreEqual("初期化完了", config.Validation.ValidationText);
        Assert.AreEqual(0, config.InputTiming.KeyStrokeIntervalMs);
        Assert.AreEqual(1000, config.InputTiming.TypeTextRetryWaitMs);
        Assert.AreEqual(2, config.InputTiming.TypeTextRetryMaxRetries);
        Assert.AreEqual(1000, config.InputTiming.ClearInputRetryWaitMs);
        Assert.AreEqual(2, config.InputTiming.ClearInputRetryMaxRetries);
        Assert.AreEqual(5, config.InputTiming.PostTypeWaitPerCharMs);
        Assert.AreEqual(300, config.InputTiming.PostTypeWaitMinMs);
        Assert.AreEqual(10, config.InputTiming.ClearInputMaxPasses);
        Assert.AreEqual(500, config.Hook.HookCommandTimeoutMs);
        Assert.AreEqual(300, config.Hook.HookConnectTimeoutMs);
        Assert.AreEqual(8000, config.Hook.HookConnectTotalWaitMs);
        Assert.AreEqual("ctrl", config.Ui.MoveToStartModifier);
        Assert.AreEqual("cursor up", config.Ui.MoveToStartKey);
        Assert.AreEqual("ctrl", config.Ui.ClearInputSelectAllModifier);
        Assert.AreEqual("a", config.Ui.ClearInputSelectAllKey);
        Assert.AreEqual(string.Empty, config.Ui.PlayShortcutModifier);
        Assert.AreEqual("spacebar", config.Ui.PlayShortcutKey);
        Assert.AreEqual(BootValidationMode.Required, config.Runtime.BootValidation);
        Assert.AreEqual("warn", config.Debug.LogMinimumLevel);
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
            Runtime = null,
            Audio = null,
            Validation = null,
            Hook = null,
            Ui = null,
            InputTiming = null,
            Text = null,
            Debug = null
        };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("AppConfigValidator", "Validate", config));

        StringAssert.Contains(ex.Message, "validation は null にできません");
    }

    [TestMethod]
    public void Validate_InvalidNumericValues_Throw()
    {
        // 数値境界を検証
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Runtime.MaxQueuedJobs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.PollIntervalMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StartConfirmMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Audio.StopConfirmMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.ActionDelayMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Validation.ValidationMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Validation.ValidationRetryIntervalMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.PostTypeWaitPerCharMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.PostTypeWaitMinMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.KeyStrokeIntervalMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.TypeTextRetryWaitMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.TypeTextRetryMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.ClearInputRetryWaitMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.ClearInputRetryMaxRetries = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.InputTiming.ClearInputMaxPasses = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Hook.HookCommandTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Hook.HookConnectTimeoutMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Hook.HookConnectTotalWaitMs = 0));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Ui.DelayBeforePlayShortcutMs = -1));
        Assert.ThrowsException<InvalidOperationException>(() => ValidateWith(config => config.Debug.LogMinimumLevel = "error"));
    }

    [TestMethod]
    public void Validate_BlankMoveToStartKey_Throws()
    {
        // 先頭移動キー設定の空白を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.MoveToStartKey = "   "));

        StringAssert.Contains(ex.Message, "ui.moveToStartKey");
    }

    [TestMethod]
    public void Validate_MoveToStartModifierAndKey_IsAllowed()
    {
        // ctrlとcursor upを許可
        ValidateWith(config =>
        {
            config.Ui.MoveToStartModifier = "ctrl";
            config.Ui.MoveToStartKey = "cursor up";
        });
    }

    [TestMethod]
    public void Validate_InvalidMoveToStartModifier_Throws()
    {
        // 修飾子は空文字とctrlとalt以外を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.MoveToStartModifier = "shift"));

        StringAssert.Contains(ex.Message, "ui.moveToStartModifier");
    }

    [TestMethod]
    public void Validate_InvalidClearInputSelectAllModifier_Throws()
    {
        // 全選択修飾子は空文字とctrlとalt以外を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.ClearInputSelectAllModifier = "shift"));

        StringAssert.Contains(ex.Message, "ui.clearInputSelectAllModifier");
    }

    [TestMethod]
    public void Validate_InvalidClearInputSelectAllKey_Throws()
    {
        // 全選択キー不正を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.ClearInputSelectAllKey = ""));

        StringAssert.Contains(ex.Message, "ui.clearInputSelectAllKey");
    }

    [TestMethod]
    public void Validate_InvalidPlayShortcutKey_Throws()
    {
        // 再生キー不正を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.PlayShortcutKey = "Delete"));

        StringAssert.Contains(ex.Message, "ui.playShortcutKey");
    }

    [TestMethod]
    public void Validate_InvalidPlayShortcutModifier_Throws()
    {
        // 再生修飾子不正を拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Ui.PlayShortcutModifier = "meta"));

        StringAssert.Contains(ex.Message, "ui.playShortcutModifier");
    }

    [TestMethod]
    public void Validate_PlayShortcutModifierAndKey_IsAllowed()
    {
        // 再生修飾子と再生キーの有効値を許可
        ValidateWith(config =>
        {
            config.Ui.PlayShortcutModifier = "shift";
            config.Ui.PlayShortcutKey = "spacebar";
        });
    }

    [TestMethod]
    public void Validate_ValidationTextNull_Throws()
    {
        // 起動検証文字列nullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Validation.ValidationText = null));

        StringAssert.Contains(ex.Message, "validation.validationText");
    }

    [TestMethod]
    public void Validate_EmptyValidationText_IsAllowed()
    {
        // 空文字は許可
        ValidateWith(config => config.Validation.ValidationText = string.Empty);
    }

    [TestMethod]
    public void Validate_ReplaceRulesNull_Throws()
    {
        // 置換ルールnullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ValidateWith(config => config.Text.ReplaceRules = null));

        StringAssert.Contains(ex.Message, "text.replaceRules");
    }

    [TestMethod]
    public void Validate_BoundaryValues_AreAllowed()
    {
        // 下限境界値を許容
        ValidateWith(config =>
        {
            config.Runtime.MaxQueuedJobs = 0;
            config.Audio.PollIntervalMs = 1;
            config.Audio.StartConfirmTimeoutMs = 1;
            config.Audio.StartConfirmMaxRetries = 0;
            config.Audio.StopConfirmMs = 1;
            config.InputTiming.ActionDelayMs = 0;
            config.Validation.ValidationMaxRetries = 0;
            config.Validation.ValidationRetryIntervalMs = 0;
            config.InputTiming.PostTypeWaitPerCharMs = 0;
            config.InputTiming.PostTypeWaitMinMs = 0;
            config.InputTiming.KeyStrokeIntervalMs = 0;
            config.InputTiming.TypeTextRetryWaitMs = 0;
            config.InputTiming.TypeTextRetryMaxRetries = 0;
            config.InputTiming.ClearInputRetryWaitMs = 0;
            config.InputTiming.ClearInputRetryMaxRetries = 0;
            config.InputTiming.ClearInputMaxPasses = 1;
            config.Hook.HookCommandTimeoutMs = 1;
            config.Hook.HookConnectTimeoutMs = 1;
            config.Hook.HookConnectTotalWaitMs = 1;
            config.Ui.DelayBeforePlayShortcutMs = 0;
            config.Debug.LogMinimumLevel = "info";
        });
    }

    [TestMethod]
    public void Validate_EachNullSection_ThrowsMatchingMessage()
    {
        // 各必須セクション名を確認
        StringAssert.Contains(AssertSectionNullThrows(config => config.Runtime = null).Message, "runtime は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Audio = null).Message, "audio は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Validation = null).Message, "validation は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Hook = null).Message, "hook は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Ui = null).Message, "ui は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.InputTiming = null).Message, "inputTiming は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Text = null).Message, "text は null");
        StringAssert.Contains(AssertSectionNullThrows(config => config.Debug = null).Message, "debug は null");
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
