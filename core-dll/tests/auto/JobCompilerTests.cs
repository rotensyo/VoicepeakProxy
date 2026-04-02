using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class JobCompilerTests
{
    [TestMethod]
    public void Compile_NullRequest_Throws()
    {
        // request nullを拒否
        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new object[] { null, new AppConfig(), RequestValidationMode.Strict }));

        StringAssert.Contains(ex.Message, "request は null");
    }

    [TestMethod]
    public void Compile_StrictNullText_Throws()
    {
        // strictでText nullを拒否
        SpeakRequest request = new SpeakRequest { Text = null, Mode = EnqueueMode.Queue };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict));

        StringAssert.Contains(ex.Message, "text は null");
    }

    [TestMethod]
    public void Compile_DisabledNullText_AlsoThrows()
    {
        // disabledでもText nullを拒否
        SpeakRequest request = new SpeakRequest { Text = null, Mode = EnqueueMode.Queue };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Disabled));

        StringAssert.Contains(ex.Message, "text は null");
    }

    [TestMethod]
    public void Compile_EmptyText_Throws()
    {
        // 空文字入力を拒否
        SpeakRequest request = new SpeakRequest { Text = string.Empty, Mode = EnqueueMode.Queue };

        InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
            ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict));

        StringAssert.Contains(ex.Message, "text は空文字");
    }

    [TestMethod]
    public void Compile_PauseOnlyInput_CreatesDelayOnlyJob()
    {
        // pauseのみ入力は待機専用ジョブ化
        SpeakRequest request = new SpeakRequest { Text = "[[pause:100]][[pause:200]]", Mode = EnqueueMode.Queue };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        Assert.AreEqual("Queue", ReflectionTestHelper.GetProperty(job, "Mode").ToString());
        CollectionAssert.AreEqual(new[] { ("", 0) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(300, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(job, "IsDelayOnly"));
    }

    [TestMethod]
    public void Compile_Mode_UsesEnumAsIs()
    {
        // mode enumをそのまま反映
        object nextJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new SpeakRequest { Text = "a", Mode = EnqueueMode.Next }, new AppConfig(), RequestValidationMode.Strict);
        object flushJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new SpeakRequest { Text = "a", Mode = EnqueueMode.Flush }, new AppConfig(), RequestValidationMode.Strict);

        Assert.AreEqual("Next", ReflectionTestHelper.GetProperty(nextJob, "Mode").ToString());
        Assert.AreEqual("Flush", ReflectionTestHelper.GetProperty(flushJob, "Mode").ToString());
    }

    [TestMethod]
    public void Compile_NextAndFlush_PreserveInterruptFlag()
    {
        // nextとflushはinterruptを保持
        object nextJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new SpeakRequest { Text = "hello", Mode = EnqueueMode.Next, Interrupt = true }, new AppConfig(), RequestValidationMode.Strict);
        object flushJob = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new SpeakRequest { Text = "hello", Mode = EnqueueMode.Flush, Interrupt = true }, new AppConfig(), RequestValidationMode.Strict);

        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(nextJob, "Interrupt"));
        Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(flushJob, "Interrupt"));
    }

    [TestMethod]
    public void Compile_ReplaceRules_AreAppliedInOrder()
    {
        // 置換ルール適用順を固定
        AppConfig config = new AppConfig();
        config.Text.ReplaceRules = new List<ReplaceRule>
        {
            new ReplaceRule { From = "a", To = "b" },
            new ReplaceRule { From = "b", To = "c" }
        };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", new SpeakRequest { Text = "a", Mode = EnqueueMode.Queue }, config, RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("c", 0) }, ReflectionTestHelper.GetSegments(job));
    }

    [TestMethod]
    public void Compile_MultiplePauseTokens_AreAccumulated()
    {
        // pauseトークンを加算
        SpeakRequest request = new SpeakRequest
        {
            Text = "A[[pause:100]][[pause:200]]B[[pause:300]][[pause:-1]]",
            Mode = EnqueueMode.Queue
        };

        object job = ReflectionTestHelper.InvokeCoreStatic("JobCompiler", "Compile", request, new AppConfig(), RequestValidationMode.Strict);

        CollectionAssert.AreEqual(new[] { ("A", 0), ("B", 300) }, ReflectionTestHelper.GetSegments(job));
        Assert.AreEqual(300, (int)ReflectionTestHelper.GetProperty(job, "TrailingPauseMs"));
    }
}
