using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.UiaProbe;

// UIA読み取り専用サブプロセス
internal static class Program
{
    // エントリポイント
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveCoreAssembly;

        string pipeName = ParsePipeName(args);
        if (string.IsNullOrEmpty(pipeName))
        {
            return 1;
        }

        try
        {
            using NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
            server.WaitForConnection();
            using StreamReader reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, true);
            using StreamWriter writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

            int generation = Environment.TickCount;
            writer.WriteLine("READY\t" + generation);

            while (true)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (string.Equals(line, "QUIT", StringComparison.Ordinal))
                {
                    break;
                }

                if (!TryProcessRead(line, out string response))
                {
                    writer.WriteLine("ERR\tbad_request");
                    continue;
                }

                writer.WriteLine(response);
            }

            return 0;
        }
        catch
        {
            return 2;
        }
    }

    // coreアセンブリを親ディレクトリから解決
    private static Assembly ResolveCoreAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (!string.Equals(name, "VoicepeakProxyCore", StringComparison.Ordinal))
        {
            return null;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
        string parentDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
        string corePath = Path.Combine(parentDir, "VoicepeakProxyCore.dll");
        if (!File.Exists(corePath))
        {
            return null;
        }

        return Assembly.LoadFrom(corePath);
    }

    // パイプ名を抽出
    private static string ParsePipeName(string[] args)
    {
        if (args == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1] ?? string.Empty;
            }
        }

        return string.Empty;
    }

    // READ要求を処理
    private static bool TryProcessRead(string line, out string response)
    {
        response = string.Empty;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        string[] parts = line.Split('\t');
        if (parts.Length < 2 || !string.Equals(parts[0], "READ", StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out long hwndValue))
        {
            return false;
        }

        ReadInputSnapshot snapshot = VoicepeakUiController.ReadInputSnapshotCore(new IntPtr(hwndValue), logTextCandidates: false, log: null);
        string encodedText = Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshot.Read.Text ?? string.Empty));
        int successFlag = snapshot.Read.Success ? 1 : 0;
        response = string.Concat(
            "OK\t",
            successFlag.ToString(), "\t",
            snapshot.VisibleBlockCount.ToString(), "\t",
            snapshot.Read.TotalLength.ToString(), "\t",
            ((int)snapshot.Read.Source).ToString(), "\t",
            encodedText);
        return true;
    }
}
