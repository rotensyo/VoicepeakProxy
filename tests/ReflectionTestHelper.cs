using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

internal static class ReflectionTestHelper
{
    private static readonly Assembly CoreAssembly = typeof(AppConfig).Assembly;

    public static Type GetCoreType(string typeName)
    {
        return CoreAssembly.GetType("VoicepeakProxyCore." + typeName, throwOnError: true);
    }

    public static object CreateCoreInstance(string typeName, params object[] args)
    {
        try
        {
            return Activator.CreateInstance(
                GetCoreType(typeName),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: args,
                culture: null);
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            throw;
        }
    }

    public static object InvokeCoreStatic(string typeName, string methodName, params object[] args)
    {
        MethodInfo method = FindMethod(GetCoreType(typeName), methodName, isStatic: true, args.Length);
        return Invoke(method, target: null, args);
    }

    public static object InvokeCoreInstance(object target, string methodName, params object[] args)
    {
        MethodInfo method = FindMethod(target.GetType(), methodName, isStatic: false, args.Length);
        return Invoke(method, target, args);
    }

    public static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return field.GetValue(target);
    }

    public static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field.SetValue(target, value);
    }

    public static object GetProperty(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return property.GetValue(target);
    }

    public static object CreateAppLogger(TestLogger logger)
    {
        return CreateCoreInstance("AppLogger", logger);
    }

    public static object CreateVoicepeakUiController(UiConfig ui, DebugConfig debug, TestLogger logger)
    {
        return CreateCoreInstance("VoicepeakUiController", ui, debug, CreateAppLogger(logger));
    }

    public static object CreateVoicepeakEngine(AppConfig config, CancellationTokenSource cts, TestLogger logger)
    {
        return CreateCoreInstance("VoicepeakEngine", config, cts, CreateAppLogger(logger));
    }

    public static object ParseCoreEnum(string typeName, string value)
    {
        return Enum.Parse(GetCoreType(typeName), value);
    }

    public static List<string> GetQueuedSegmentTexts(object engine)
    {
        object queue = GetField(engine, "_queue");
        List<string> result = new List<string>();
        foreach (object node in (IEnumerable)queue)
        {
            object segments = GetProperty(node, "Segments");
            IEnumerator enumerator = ((IEnumerable)segments).GetEnumerator();
            if (!enumerator.MoveNext())
            {
                result.Add(string.Empty);
                continue;
            }

            object firstSegment = enumerator.Current;
            result.Add((string)GetProperty(firstSegment, "Text"));
        }

        return result;
    }

    public static List<(string Text, int PausePreMs)> GetSegments(object job)
    {
        List<(string Text, int PausePreMs)> result = new List<(string Text, int PausePreMs)>();
        foreach (object segment in (IEnumerable)GetProperty(job, "Segments"))
        {
            result.Add(((string)GetProperty(segment, "Text"), (int)GetProperty(segment, "PausePreMs")));
        }

        return result;
    }

    public static T RunInSta<T>(Func<T> func)
    {
        T result = default;
        Exception error = null;

        Thread thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result;
    }

    public static T ThrowsInner<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (TargetInvocationException ex)
        {
            if (ex.InnerException is T typed)
            {
                return typed;
            }

            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
        }

        throw new AssertFailedException(typeof(T).Name + " was not thrown.");
    }

    private static MethodInfo FindMethod(Type type, string methodName, bool isStatic, int argCount)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.Name == methodName && method.GetParameters().Length == argCount)
            {
                return method;
            }
        }

        throw new MissingMethodException(type.FullName, methodName);
    }

    private static object Invoke(MethodInfo method, object target, object[] args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            throw;
        }
    }

    internal sealed class MessageRecorderWindow : NativeWindow, IDisposable
    {
        public List<(int Msg, IntPtr WParam)> Messages { get; } = new List<(int Msg, IntPtr WParam)>();

        public MessageRecorderWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            Messages.Add((m.Msg, m.WParam));
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
