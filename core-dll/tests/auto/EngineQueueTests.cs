using System.Collections;
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
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Queue });
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "second", Mode = EnqueueMode.Queue });

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
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Queue });
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "second", Mode = EnqueueMode.Next });

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
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);

            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Queue });
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "second", Mode = EnqueueMode.Queue });
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "third", Mode = EnqueueMode.Flush });

            CollectionAssert.AreEqual(new[] { "third" }, ReflectionTestHelper.GetQueuedSegmentTexts(engine));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_QueueLimit_ReturnsQueueFull()
    {
        // 上限超過でQueueFull
        AppConfig config = CreateConfig();
        config.Queue.MaxQueuedJobs = 1;
        using CancellationTokenSource cts = new CancellationTokenSource();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(config, cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);

            EnqueueResult first = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Queue });
            EnqueueResult second = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "second", Mode = EnqueueMode.Queue });
            EnqueueResult third = (EnqueueResult)ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "third", Mode = EnqueueMode.Next });

            Assert.AreEqual(EnqueueStatus.Accepted, first.Status);
            Assert.AreEqual(EnqueueStatus.QueueFull, second.Status);
            Assert.AreEqual(EnqueueStatus.Accepted, third.Status);
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    [TestMethod]
    public void Enqueue_QueueMode_ClearsInterruptFlag()
    {
        // queue時はinterrupt無効化
        using CancellationTokenSource cts = new CancellationTokenSource();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Queue, Interrupt = true });

            Assert.IsFalse((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
            object firstJob = GetFirstQueuedJob(engine);
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
        // next時はinterrupt要求
        using CancellationTokenSource cts = new CancellationTokenSource();
        object engine = ReflectionTestHelper.CreateVoicepeakEngine(CreateConfig(), cts, new TestLogger());
        try
        {
            PauseWorkerConsumption(engine);
            ReflectionTestHelper.InvokeCoreInstance(engine, "Enqueue", new SpeakRequest { Text = "first", Mode = EnqueueMode.Next, Interrupt = true });

            Assert.IsTrue((bool)ReflectionTestHelper.GetField(engine, "_interruptRequested"));
        }
        finally
        {
            ReflectionTestHelper.InvokeCoreInstance(engine, "Dispose");
        }
    }

    private static object GetFirstQueuedJob(object engine)
    {
        object queue = ReflectionTestHelper.GetField(engine, "_queue");
        foreach (object job in (IEnumerable)queue)
        {
            return job;
        }

        return null;
    }

    private static void PauseWorkerConsumption(object engine)
    {
        // ワーカー消費を停止
        ReflectionTestHelper.SetField(engine, "_state", ReflectionTestHelper.ParseCoreEnum("WorkerState", "ExecutingPrePlayWait"));
    }

    private static AppConfig CreateConfig() => new AppConfig();
}
