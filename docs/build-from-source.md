# ビルド方法と生成物

## 前提
- Windows
- `.NET SDK`導入済み
- `.NET Framework 4.8`ターゲット開発環境
- `msbuild`を実行できる環境(Visual Studio Build Tools等)

## ビルド(Release)
プロジェクトルートで実行します。

### 1. core-dll(VoicepeakProxyCore.dll)
```powershell
dotnet build "core-dll/VoicepeakProxyCore.csproj" -c Release
```

主な生成物:

- `core-dll/bin/Release/net48/VoicepeakProxyCore.dll`
- `core-dll/bin/Release/net48/VoicepeakProxyCore.deps/`
  - `EasyHook*`
  - `EasyLoad*`
  - `NAudio*`
  - `VoicepeakProxyCore.UiaProbe.exe`

### 2. Worker(VoicepeakProxyWorker.exe)
```powershell
dotnet build "bouyomichan-plugin/VoicepeakProxyWorker/VoicepeakProxyWorker.csproj" -c Release
```

主な生成物:

- `bouyomichan-plugin/VoicepeakProxyWorker/bin/Release/VoicepeakProxyWorker/VoicepeakProxyWorker.exe`
- `bouyomichan-plugin/VoicepeakProxyWorker/bin/Release/VoicepeakProxyWorker/VoicepeakProxyCore.dll`
- `bouyomichan-plugin/VoicepeakProxyWorker/bin/Release/VoicepeakProxyWorker/VoicepeakProxyCore.deps/`
  - `EasyHook*`
  - `EasyLoad*`
  - `NAudio*`
  - `VoicepeakProxyCore.UiaProbe.exe`

### 3. 棒読みちゃんPlugin(Plugin_VoicepeakProxy.dll)
棒読みちゃん本体への参照が必要です。

```powershell
msbuild "bouyomichan-plugin/Plugin_VoicepeakProxy/Plugin_VoicepeakProxy.csproj" /p:Configuration=Release /p:Platform=x86 /p:BouyomiChanExePath="{棒読みちゃん本体ディレクトリへのパス}\BouyomiChan.exe"
```

主な生成物:

- `bouyomichan-plugin/Plugin_VoicepeakProxy/bin/Release/Plugin_VoicepeakProxy.dll`

## 補足
- `core-dll`と`Worker`はReleaseビルド時に依存ライブラリを`VoicepeakProxyCore.deps`にまとめます。そのまま使用してください。
- Pluginビルドでは`BouyomiChanExePath`の指定が必須です。
