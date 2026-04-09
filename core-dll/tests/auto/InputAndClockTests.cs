using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class InputAndClockTests
{
    [TestMethod]
    public void NormalizeForTyping_CompressesMixedWhitespaceNewlineRuns()
    {
        // 中間の空白改行混在ランを改行1つへ圧縮
        string actual = InputTextNormalizer.NormalizeForTyping("  \r\n abc \n \t def \n\r  ghi\r\n ");

        Assert.AreEqual("abc\ndef\nghi", actual);
    }

    [TestMethod]
    public void NormalizeForTyping_PreservesInnerSpacesWithoutNewline()
    {
        // 改行を含まない内部空白は維持
        string actual = InputTextNormalizer.NormalizeForTyping(" a  b ");

        Assert.AreEqual("a  b", actual);
    }

    [TestMethod]
    public void NormalizeForTyping_NewlineOnly_ReturnsEmpty()
    {
        // 改行空白のみは空文字
        string actual = InputTextNormalizer.NormalizeForTyping(" \r\n\n \t ");

        Assert.AreEqual(string.Empty, actual);
    }

    [TestMethod]
    public void NormalizeForTyping_RemovesNonBmpCharacters()
    {
        // 非BMP文字を除去
        string actual = InputTextNormalizer.NormalizeForTyping("A🤡\nB");

        Assert.AreEqual("A\nB", actual);
    }

    [TestMethod]
    public void NormalizeForTyping_RemovesEmojiSequencesAndKeepsPlainSymbols()
    {
        // 絵文字シーケンスは除去し単体記号は保持
        string actual = InputTextNormalizer.NormalizeForTyping("☠️ ☠ ❤️ © ™ 1️⃣");

        Assert.AreEqual("☠  © ™", actual);
    }

    [TestMethod]
    public void NormalizeForTyping_RemovesEmojiJoinControls()
    {
        // ZWJとキーキャップ記号を除去
        string source = string.Concat("A", '\u200D'.ToString(), "B", '\uFE0F'.ToString(), "C", '\u20E3'.ToString(), "D");
        string actual = InputTextNormalizer.NormalizeForTyping(source);

        Assert.AreEqual("ACD", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_RemovesNewlinesAndTrims()
    {
        // 検証向けは改行除去とtrim
        string actual = InputTextNormalizer.NormalizeForValidation("  \r\n abc \n def \r\n ");

        Assert.AreEqual("abc  def", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_Null_ReturnsEmpty()
    {
        // null入力を空文字へ正規化
        string actual = InputTextNormalizer.NormalizeForValidation(null);

        Assert.AreEqual(string.Empty, actual);
    }

    [TestMethod]
    public void NormalizeForValidation_WhitespaceOnly_ReturnsEmpty()
    {
        // 空白のみは空文字
        string actual = InputTextNormalizer.NormalizeForValidation("  \r\n  ");

        Assert.AreEqual(string.Empty, actual);
    }

    [TestMethod]
    public void NormalizeForValidation_PreservesInnerSpaces()
    {
        // 内部空白は保持
        string actual = InputTextNormalizer.NormalizeForValidation(" a  b ");

        Assert.AreEqual("a  b", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_NoNewlines_ReturnsTrimmedText()
    {
        // 改行なしはtrimのみ
        string actual = InputTextNormalizer.NormalizeForValidation(" abc ");

        Assert.AreEqual("abc", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_RemovesNonBmpCharacters()
    {
        // 非BMP文字は除去
        string actual = InputTextNormalizer.NormalizeForValidation("A🤡B");

        Assert.AreEqual("AB", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_RemovesEmojiSequencesAndKeepsPlainSymbols()
    {
        // 絵文字シーケンスは除去し単体記号は保持
        string actual = InputTextNormalizer.NormalizeForValidation(" ☠️ ☠ ❤️ © ™ 1️⃣ ");

        Assert.AreEqual("☠  © ™", actual);
    }

    [TestMethod]
    public void NormalizeForValidation_RemovesStandaloneSurrogates()
    {
        // 孤立サロゲートも除去
        string source = string.Concat("A", '\uD83E'.ToString(), "B", '\uDD21'.ToString(), "C");
        string actual = InputTextNormalizer.NormalizeForValidation(source);

        Assert.AreEqual("ABC", actual);
    }

    [TestMethod]
    public void MonoClock_NowMs_IsMonotonic()
    {
        // 単調増加を確認
        long first = (long)ReflectionTestHelper.InvokeCoreStatic("MonoClock", "NowMs");
        Thread.Sleep(10);
        long second = (long)ReflectionTestHelper.InvokeCoreStatic("MonoClock", "NowMs");

        Assert.IsTrue(second >= first);
    }

    [TestMethod]
    public void MonoClock_SleepUntil_StopsOnInterrupt()
    {
        // 割込み時に早期停止
        bool interrupted = false;
        long start = (long)ReflectionTestHelper.InvokeCoreStatic("MonoClock", "NowMs");
        long target = start + 500;

        ReflectionTestHelper.InvokeCoreStatic(
            "MonoClock",
            "SleepUntil",
            target,
            new System.Func<bool>(() =>
            {
                interrupted = true;
                return true;
            }),
            10);

        long end = (long)ReflectionTestHelper.InvokeCoreStatic("MonoClock", "NowMs");
        Assert.IsTrue(interrupted);
        Assert.IsTrue(end - start < 200);
    }

    [TestMethod]
    public void MonoClock_SleepUntil_ReturnsImmediatelyWhenTargetPassed()
    {
        // 期限経過済みなら即終了
        long start = MonoClock.NowMs();
        MonoClock.SleepUntil(start - 1, () => false, 10);
        long end = MonoClock.NowMs();

        Assert.IsTrue(end - start < 50);
    }

}
