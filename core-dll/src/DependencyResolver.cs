using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoicepeakProxyCore;

// 依存DLL解決を初期化
internal static class DependencyResolver
{
    private static int _initialized;
    private static string _depsDir = string.Empty;

    // 依存解決を一度だけ有効化
    internal static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        string asmDir = Path.GetDirectoryName(typeof(DependencyResolver).Assembly.Location) ?? string.Empty;
        string depsDir = Path.Combine(asmDir, "VoicepeakProxyCore.deps");
        if (!Directory.Exists(depsDir))
        {
            return;
        }

        _depsDir = depsDir;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDeps;

        // ネイティブ依存解決を補助
        SetDllDirectory(_depsDir);

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (currentPath.IndexOf(_depsDir, StringComparison.OrdinalIgnoreCase) < 0)
        {
            Environment.SetEnvironmentVariable("PATH", _depsDir + Path.PathSeparator + currentPath);
        }
    }

    // 依存ディレクトリから管理DLLを解決
    private static Assembly ResolveFromDeps(object sender, ResolveEventArgs args)
    {
        if (string.IsNullOrEmpty(_depsDir))
        {
            return null;
        }

        string name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string dllPath = Path.Combine(_depsDir, name + ".dll");
        if (!File.Exists(dllPath))
        {
            return null;
        }

        return Assembly.LoadFrom(dllPath);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
