using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoicepeakProxyCore.Tests;

[TestClass]
public class VirtualClipboardSessionStateTests
{
    [TestMethod]
    public void VirtualClipboardHandleStore_CreateHandle_ReturnsHandle()
    {
        // Unicode文字列ハンドルを確保
        IVirtualClipboardHandleStore store = new VirtualClipboardHandleStore();
        IntPtr handle = store.CreateHandle("テスト");
        try
        {
            Assert.AreNotEqual(IntPtr.Zero, handle);
        }
        finally
        {
            store.FreeHandle(handle);
        }
    }

    [TestMethod]
    public void TryCreateHandleForSession_CloseSession_FreesIssuedHandles()
    {
        // CloseClipboard相当で返却済みハンドルを解放
        FakeVirtualClipboardHandleStore store = new FakeVirtualClipboardHandleStore();
        VirtualClipboardSessionState state = new VirtualClipboardSessionState(store);

        state.SetPublishedText("abc");
        state.BeginSession(10);

        VirtualClipboardDataResult result = state.TryCreateHandleForSession(10, out IntPtr handle);

        Assert.AreEqual(VirtualClipboardDataResult.Provided, result);
        Assert.AreEqual(new IntPtr(101), handle);
        CollectionAssert.AreEqual(Array.Empty<IntPtr>(), store.FreedHandles);

        state.EndSession(10);

        CollectionAssert.AreEqual(new[] { new IntPtr(101) }, store.FreedHandles);
    }

    [TestMethod]
    public void ClearPublishedText_DoesNotFreeIssuedHandlesBeforeSessionEnd()
    {
        // 公開停止だけでは返却済みハンドルを解放しない
        FakeVirtualClipboardHandleStore store = new FakeVirtualClipboardHandleStore();
        VirtualClipboardSessionState state = new VirtualClipboardSessionState(store);

        state.SetPublishedText("abc");
        state.BeginSession(20);
        Assert.AreEqual(VirtualClipboardDataResult.Provided, state.TryCreateHandleForSession(20, out IntPtr handle));

        state.ClearPublishedText();

        Assert.IsFalse(state.CanAdvertiseFormat(20));
        CollectionAssert.AreEqual(Array.Empty<IntPtr>(), store.FreedHandles);

        state.EndSession(20);

        CollectionAssert.AreEqual(new[] { handle }, store.FreedHandles);
    }

    [TestMethod]
    public void InvalidateSessionData_DisablesVirtualDataAndFreesIssuedHandles()
    {
        // EmptyClipboard相当で返却済みハンドルを無効化
        FakeVirtualClipboardHandleStore store = new FakeVirtualClipboardHandleStore();
        VirtualClipboardSessionState state = new VirtualClipboardSessionState(store);

        state.SetPublishedText("abc");
        state.BeginSession(30);
        Assert.AreEqual(VirtualClipboardDataResult.Provided, state.TryCreateHandleForSession(30, out IntPtr first));

        state.InvalidateSessionData(30);

        Assert.IsFalse(state.CanAdvertiseFormat(30));
        Assert.AreEqual(VirtualClipboardDataResult.Unavailable, state.TryCreateHandleForSession(30, out _));
        CollectionAssert.AreEqual(new[] { first }, store.FreedHandles);
    }

    [TestMethod]
    public void BeginSession_ReplacesStaleSessionAndFreesOldHandles()
    {
        // セッション再開始時に未解放ハンドルを回収
        FakeVirtualClipboardHandleStore store = new FakeVirtualClipboardHandleStore();
        VirtualClipboardSessionState state = new VirtualClipboardSessionState(store);

        state.SetPublishedText("abc");
        state.BeginSession(40);
        Assert.AreEqual(VirtualClipboardDataResult.Provided, state.TryCreateHandleForSession(40, out IntPtr first));

        state.BeginSession(40);

        CollectionAssert.AreEqual(new[] { first }, store.FreedHandles);
        Assert.AreEqual(VirtualClipboardDataResult.Provided, state.TryCreateHandleForSession(40, out IntPtr second));
        Assert.AreEqual(new IntPtr(102), second);
    }

    private sealed class FakeVirtualClipboardHandleStore : IVirtualClipboardHandleStore
    {
        private int _nextHandleValue = 101;

        public List<IntPtr> FreedHandles { get; } = new List<IntPtr>();

        public IntPtr CreateHandle(string text)
        {
            return new IntPtr(_nextHandleValue++);
        }

        public void FreeHandle(IntPtr handle)
        {
            FreedHandles.Add(handle);
        }
    }
}
