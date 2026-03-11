using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class EngineQueueTests
{
    [TestMethod]
    public void Enqueue_Queue_AppendsLast()
    {
        // queueは末尾追加
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "queue" }, null);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "second", mode = "queue" }, null);

            CollectionAssert.AreEqual(new[] { "first", "second" }, ReflectionTestHelper.GetQueuedSegmentTexts(engine));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_Next_AddsFirst()
    {
        // nextは先頭追加
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "queue" }, null);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "second", mode = "next" }, null);

            CollectionAssert.AreEqual(new[] { "second", "first" }, ReflectionTestHelper.GetQueuedSegmentTexts(engine));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_Flush_ClearsPendingQueue()
    {
        // flushは待機キューを置換
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "queue" }, null);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "second", mode = "queue" }, null);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "third", mode = "flush" }, null);

            CollectionAssert.AreEqual(new[] { "third" }, ReflectionTestHelper.GetQueuedSegmentTexts(engine));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_QueueLimit_AppliesOnlyToQueue()
    {
        // 上限はqueueのみに適用
        AppConfig config = CreateConfig();
        config.Server.MaxQueuedJobs = 1;
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(config, cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            EnqueueResult first = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "queue" }, null);
            EnqueueResult second = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "second", mode = "queue" }, null);
            EnqueueResult third = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "third", mode = "next" }, null);

            Assert.AreEqual(202, first.StatusCode);
            Assert.AreEqual(429, second.StatusCode);
            Assert.AreEqual(202, third.StatusCode);
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_QueueLimit_DoesNotBlockFlush()
    {
        // 上限超過でもflushは受理
        AppConfig config = CreateConfig();
        config.Server.MaxQueuedJobs = 0;
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(config, cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            EnqueueResult result = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "only", mode = "flush" }, null);

            Assert.AreEqual(202, result.StatusCode);
            CollectionAssert.AreEqual(new[] { "only" }, ReflectionTestHelper.GetQueuedSegmentTexts(engine));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_QueueMode_ClearsInterruptFlag()
    {
        // queue時は割込み要求を解除
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "queue", interrupt = true }, null);

            Assert.IsFalse((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
            object queue = ReflectionTestHelper.GetField(engine, "_queue");
            object firstJob = null;
            foreach (object job in (System.Collections.IEnumerable)queue)
            {
                firstJob = job;
                break;
            }

            Assert.IsNotNull(firstJob);
            Assert.IsFalse((bool)ReflectionTestHelper.GetProperty(firstJob, "Interrupt"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_NextMode_SetsInterruptRequested()
    {
        // next時は割込み要求を設定
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "next", interrupt = true }, null);

            Assert.IsTrue((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_FlushMode_SetsInterruptRequested()
    {
        // flush時は割込み要求を設定
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "flush", interrupt = true }, null);

            Assert.IsTrue((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
            object queue = ReflectionTestHelper.GetField(engine, "_queue");
            object firstJob = null;
            foreach (object job in (System.Collections.IEnumerable)queue)
            {
                firstJob = job;
                break;
            }

            Assert.IsNotNull(firstJob);
            Assert.IsTrue((bool)ReflectionTestHelper.GetProperty(firstJob, "Interrupt"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_NextMode_WithInterruptFalse_DoesNotSetInterruptRequested()
    {
        // nextでinterrupt falseは未設定
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "next", interrupt = false }, null);

            Assert.IsFalse((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_FlushMode_WithInterruptFalse_DoesNotSetInterruptRequested()
    {
        // flushでinterrupt falseは未設定
        using CancellationTokenSource cts = new CancellationTokenSource();
        TestLogger logger = new TestLogger();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, logger);
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { text = "first", mode = "flush", interrupt = false }, null);

            Assert.IsFalse((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    private static void PauseWorkerConsumption(object engine)
    {
        // ワーカー消費を一時停止
        ReflectionTestHelper.SetField(engine, "_state", ReflectionTestHelper.ParseCoreEnum("WorkerState", "ExecutingPrePlayWait"));
    }

    private static AppConfig CreateConfig()
    {
        // 既定設定を返却
        return new AppConfig();
    }
}
