using System.IO;
using BouyomiVoicepeakBridge.Shared;
using VoicepeakProxyCore;

namespace VoicepeakProxyWorker;

// 設定ファイル初期化を提供
internal static class SettingsBootstrapper
{
    // 既定設定を生成
    public static PluginSettingsFile CreateDefaultSettings()
    {
        PluginSettingsFile file = new PluginSettingsFile();
        AppConfig coreDefaults = new AppConfig();
        file.AppConfig = AppConfigMapper.MapFromCoreDefaults(coreDefaults);
        file.Normalize();
        return file;
    }

    // 設定ファイルを作成
    public static void EnsureCreated(string settingsPath, WorkerFileLogger logger)
    {
        if (File.Exists(settingsPath))
        {
            logger.Info("settings_init_skipped_exists path=" + settingsPath);
            return;
        }

        PluginSettingsFile file = CreateDefaultSettings();
        JsonFileStore.Save(settingsPath, file);
        logger.Info("settings_init_created path=" + settingsPath);
    }
}
