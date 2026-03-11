using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class JobCompilerTests
{
    [TestMethod]
    public void Compile_StrictNullText_Throws()
    {
        // strictでtext nullを拒否
        SpeakRequest request = new SpeakRequest { text = null, mode = "queue" };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict));

        StringAssert.Contains(ex.Message, "text は null");
    }

    [TestMethod]
    public void Compile_NullRequest_Throws()
    {
        // request nullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new object[] { null, new AppConfig(), RequestValidationMode.Strict }));

        StringAssert.Contains(ex.Message, "リクエストボディが必要");
    }

    [TestMethod]
    public void Compile_StrictBlankMode_Throws()
    {
        // strictでmode空白を拒否
        SpeakRequest request = new SpeakRequest { text = "hello", mode = "  " };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict));

        StringAssert.Contains(ex.Message, "mode は queue|next|flush");
    }

    [TestMethod]
    public void Compile_LenientNullText_DefaultsToQueueWithEmptyText()
    {
        // lenientで既定値を補完
        SpeakRequest request = new SpeakRequest { text = null, mode = null };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Lenient);

        Assert.AreEqual("Queue", ReflectionTestHelper.GetProperty(job, "Mode").ToString());
        CollectionAssert.AreEqual(new[] { ("", 0) }, ReflectionTestHelper.GetSegments(job));
    }

    [TestMethod]
    public void Compile_DefaultMode_InLenientAndDisabled_IsQueue()
    {
        // lenientとdisabledの既定modeを確認
        SpeakRequest lenientRequest = new SpeakRequest { text = "hello", mode = null };
        object lenientJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", lenientRequest, new AppConfig(), RequestValidationMode.Lenient);
        Assert.AreEqual("Queue", ReflectionTestHelper.GetProperty(lenientJob, "Mode").ToString());

        SpeakRequest disabledRequest = new SpeakRequest { text = "hello", mode = null };
        object disabledJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", disabledRequest, new AppConfig(), RequestValidationMode.Disabled);
        Assert.AreEqual("Queue", ReflectionTestHelper.GetProperty(disabledJob, "Mode").ToString());
    }

    [TestMethod]
    public void Compile_LenientBlankMode_DefaultsToQueue()
    {
        // lenientで空白modeはqueue補完
        SpeakRequest request = new SpeakRequest { text = "hello", mode = "   " };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Lenient);

        Assert.AreEqual("Queue", ReflectionTestHelper.GetProperty(job, "Mode").ToString());
    }

    [TestMethod]
    public void Compile_DisabledEmptyMode_RemainsInvalid()
    {
        // disabledでも空modeは不正
        SpeakRequest request = new SpeakRequest { text = "hello", mode = string.Empty };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Disabled));

        StringAssert.Contains(ex.Message, "mode は queue|next|flush");
    }

    [TestMethod]
    public void Compile_StrictInvalidMode_Throws()
    {
        // strictで未知modeを拒否
        SpeakRequest request = new SpeakRequest { text = "hello", mode = "later" };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict));

        StringAssert.Contains(ex.Message, "mode は queue|next|flush");
    }

    [TestMethod]
    public void Compile_DisabledWhitespaceMode_RemainsInvalid()
    {
        // disabledで空白modeは不正
        SpeakRequest request = new SpeakRequest { text = "hello", mode = "   " };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Disabled));

        StringAssert.Contains(ex.Message, "mode は queue|next|flush");
    }

    [TestMethod]
    public void Compile_Mode_IsTrimmedAndCaseInsensitive()
    {
        // modeのtrimと大小無視を確認
        SpeakRequest request = new SpeakRequest { text = "hello", mode = "  FluSh  " };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        Assert.AreEqual("Flush", ReflectionTestHelper.GetProperty(job, "Mode").ToString());
    }

    [TestMethod]
    public void Compile_NextAndFlush_PreserveInterruptFlag()
    {
        // nextとflushはinterruptを保持
        object nextJob = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = "hello", mode = "next", interrupt = true },
            new AppConfig(),
            RequestValidationMode.Strict);
        object flushJob = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = "hello", mode = "flush", interrupt = true },
            new AppConfig(),
            RequestValidationMode.Strict);

        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(nextJob, "Interrupt"));
        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(flushJob, "Interrupt"));
    }

    [TestMethod]
    public void Compile_ReplaceRules_AreAppliedInOrder()
    {
        // 置換ルール適用順を固定
        AppConfig config = new AppConfig();
        config.TextTransform.ReplaceRules = new List<ReplaceRule>
        {
            new ReplaceRule { From = "a", To = "b" },
            new ReplaceRule { From = "b", To = "c" }
        };

        SpeakRequest request = new SpeakRequest { text = "a", mode = "queue" };
        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, config, RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("c", 0) }, ReflectionTestHelper.GetSegments(job));
    }

    [TestMethod]
    public void Compile_ReplaceRules_NullAndEmptyFrom_AreIgnored()
    {
        // 無効置換ルールを無視
        AppConfig config = new AppConfig();
        config.TextTransform.ReplaceRules = new List<ReplaceRule>
        {
            null,
            new ReplaceRule { From = string.Empty, To = "x" },
            new ReplaceRule { From = "a", To = null }
        };

        object job = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = "a", mode = "queue" },
            config,
            RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { (string.Empty, 0) }, ReflectionTestHelper.GetSegments(job));
    }

    [TestMethod]
    public void Compile_ReplaceRules_DoNotAffectPauseTokens()
    {
        // pause句は置換対象外
        AppConfig config = new AppConfig();
        config.TextTransform.ReplaceRules = new List<ReplaceRule>
        {
            new ReplaceRule { From = "pause", To = "broken" },
            new ReplaceRule { From = "B", To = "C" }
        };

        SpeakRequest request = new SpeakRequest { text = "A[[pause:100]]B", mode = "queue" };
        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, config, RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("A", 0), ("C", 100) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(0, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }

    [TestMethod]
    public void Compile_MultiplePauseTokens_AreAccumulated()
    {
        // 連続pauseを加算
        SpeakRequest request = new SpeakRequest
        {
            text = "A[[pause:100]][[pause:200]]B[[pause:300]][[pause:-1]]",
            mode = "queue"
        };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("A", 0), ("B", 300) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(300, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }

    [TestMethod]
    public void Compile_PauseOnlyInput_CreatesEmptySegment()
    {
        // pauseのみ入力でもセグメントを生成
        SpeakRequest request = new SpeakRequest { text = "[[pause:250]]", mode = "queue" };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("", 0) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(250, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }

    [TestMethod]
    public void Compile_EmptyText_CreatesEmptySegment()
    {
        // 空文字も空セグメント化
        object job = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = string.Empty, mode = "queue" },
            new AppConfig(),
            RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { (string.Empty, 0) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(0, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }

    [TestMethod]
    public void Compile_PauseAtBeginningAndEnd_SplitsIntoExpectedSegments()
    {
        // 先頭末尾pauseを分離
        object job = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = "[[pause:100]]A[[pause:200]]", mode = "queue" },
            new AppConfig(),
            RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("A", 100) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(200, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }

    [TestMethod]
    public void Compile_WhitespaceText_IsPreserved()
    {
        // 空白本文はそのまま保持
        object job = ReflectionTestHelper.InvokeCoreStatic(
            "JobCompiler",
            "Compile",
            new SpeakRequest { text = "  ", mode = "queue" },
            new AppConfig(),
            RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("  ", 0) }, ReflectionTestHelper.GetSegments(job));
    }

    [TestMethod]
    public void Compile_InvalidPauseSyntax_RemainsText()
    {
        // 不正pause記法は通常文字列扱い
        SpeakRequest request = new SpeakRequest { text = "A[[pause:x]]B", mode = "queue" };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("A[[pause:x]]B", 0) }, ReflectionTestHelper.GetSegments(job));
    }
}
