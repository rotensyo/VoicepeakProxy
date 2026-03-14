using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class InputAndClockTests
{
    [TestMethod]
    public void Normalize_RemovesNewlinesAndTrims()
    {
        // 改行除去と前後空白除去を確認
        string actual = (string)ReflectionTestHelper.InvokeCoreStatic(
            "InputTextNormalizer",
            "Normalize",
            "  \r\n abc \n def \r\n ");

        Assert.AreEqual("abc  def", actual);
    }

    [TestMethod]
    public void Normalize_Null_ReturnsEmpty()
    {
        // null入力を空文字へ正規化
        string actual = (string)ReflectionTestHelper.InvokeCoreStatic(
            "InputTextNormalizer",
            "Normalize",
            new object[] { null });

        Assert.AreEqual(string.Empty, actual);
    }

    [TestMethod]
    public void Normalize_WhitespaceOnly_ReturnsEmpty()
    {
        // 空白のみは空文字
        string actual = InputTextNormalizer.Normalize("  \r\n  ");

        Assert.AreEqual(string.Empty, actual);
    }

    [TestMethod]
    public void Normalize_PreservesInnerSpaces()
    {
        // 内部空白は保持
        string actual = InputTextNormalizer.Normalize(" a  b ");

        Assert.AreEqual("a  b", actual);
    }

    [TestMethod]
    public void Normalize_NoNewlines_ReturnsTrimmedText()
    {
        // 改行なしはtrimのみ
        string actual = InputTextNormalizer.Normalize(" abc ");

        Assert.AreEqual("abc", actual);
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
