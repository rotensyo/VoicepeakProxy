using System;
using System.Threading;

namespace VoicepeakProxyCore;

// 常駐実行APIの公開窓口
public sealed class VoicepeakRuntime : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly VoicepeakEngine _engine;
    private readonly AppLogger _log;
    private int _disposed;
    private int _accepting;

    // 公開API初期化時に依存解決を有効化
    static VoicepeakRuntime()
    {
        DependencyResolver.EnsureInitialized();
    }

    private VoicepeakRuntime(VoicepeakEngine engine, CancellationTokenSource cts, AppLogger log)
    {
        _engine = engine;
        _cts = cts;
        _log = log;
        _accepting = 1;
    }

    public bool IsShutdownRequested => _engine.IsShutdownRequested;

    // 起動時検証後に常駐実行を開始
    public static VoicepeakRuntime Start(AppConfig config, IAppLogger logger = null)
    {
        return StartCore(config, logger, (cfg, cts, appLogger) => new VoicepeakEngine(cfg, cts, appLogger));
    }

    // テスト用にエンジン生成を差し替えて起動
    internal static VoicepeakRuntime StartCore(
        AppConfig config,
        IAppLogger logger,
        Func<AppConfig, CancellationTokenSource, AppLogger, VoicepeakEngine> engineFactory)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        AppConfigValidator.Validate(config);
        AppLogger appLogger = new AppLogger(logger ?? new ConsoleAppLogger(), config.Debug.LogMinimumLevel);
        appLogger.Info("boot_start");

        CancellationTokenSource cts = new CancellationTokenSource();
        VoicepeakEngine engine = null;
        try
        {
            engine = engineFactory != null
                ? engineFactory(config, cts, appLogger)
                : new VoicepeakEngine(config, cts, appLogger);
            if (engine == null)
            {
                throw new InvalidOperationException("engineFactory returned null.");
            }

            bool ok = engine.BootValidate(config.Runtime.BootValidation);
            if (!ok)
            {
                appLogger.Error("boot_validation_failed");
                throw new InvalidOperationException("Boot validation failed.");
            }

            appLogger.Info("runtime_started");
            return new VoicepeakRuntime(engine, cts, appLogger);
        }
        catch
        {
            engine?.Dispose();
            cts.Dispose();
            throw;
        }
    }

    // 発話要求をキューへ受理
    public EnqueueResult Enqueue(SpeakRequest request)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _accepting) == 0)
        {
            throw new InvalidOperationException("Runtime is stopping and cannot accept new requests.");
        }

        return _engine.Enqueue(request);
    }

    // 新規受理を停止して終了処理へ遷移
    public void Stop()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref _accepting, 0);
        _log.Info("runtime_stopping");
        _cts.Cancel();
        _engine.Stop();
    }

    // 資源を破棄
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref _accepting, 0);
        _cts.Cancel();
        _engine.Dispose();
        _cts.Dispose();
        _log.Info("runtime_disposed");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(VoicepeakRuntime));
        }
    }
}
