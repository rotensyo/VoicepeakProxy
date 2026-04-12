# VoicepeakProxy for 棒読みちゃん

棒読みちゃんから受け取った文字列を `VoicepeakProxyCore.dll` 経由でVOICEPEAKへ転送し、読み上げを行うプラグインです。

## 構成

- `Plugin_VoicepeakProxy/`
  - 棒読みちゃんプラグイン本体です。
- `VoicepeakProxyWorker/`
  - `VoicepeakProxyCore.dll` を動かすための補助プロセスです。
  - `VoicepeakOneShot.SpeakOnceWait` 経由でVOICEPEAK読み上げを実行します。
- `Shared/`
  - PluginとWorkerの共通モデルです。

## ビルド手順

### 1. Worker(.NET Framework 4.8)

```powershell
dotnet build "bouyomichan-plugin/VoicepeakProxyWorker/VoicepeakProxyWorker.csproj" -c Release
```

### 2. Plugin(.NET Framework 3.5)

棒読みちゃん本体への参照が必要です。

```powershell
msbuild "bouyomichan-plugin/Plugin_VoicepeakProxy/Plugin_VoicepeakProxy.csproj" /p:Configuration=Release /p:Platform=x86 /p:BouyomiChanExePath="{棒読みちゃん本体ディレクトリへのパス}\BouyomiChan.exe"
```

## 配置

棒読みちゃんフォルダへビルド生成物を配置します。

- `Plugin_VoicepeakProxy.dll`
- `VoicepeakProxyWorker/VoicepeakProxyWorker.exe`
- `VoicepeakProxyWorker/VoicepeakProxyCore.dll`
- `VoicepeakProxyWorker/VoicepeakProxyCore.deps/`
  - `EasyHook*`
  - `EasyLoad*`
  - `NAudio*`

## 仕様

- 初回起動時に設定ファイル `Plugin_VoicepeakProxy_setting.json` を自動生成します。
  - 設定は棒読みちゃんのプラグイン設定から修正できます。
- `プラグイン > VOICEPEAK自動起動用本体パス`を設定している場合、起動時に`voicepeak`プロセスが0件の場合のみVOICEPEAKを自動起動します。
  - 既に`voicepeak`プロセスが存在する場合や2つ以上起動している場合は自動起動を行いません。
- `プラグイン > VOICEPEAK自動起動用.vppファイルパス`を設定すると、VOICEPEAK自動起動時に指定した`.vpp`を開きます。
- 自動起動待機は最大30秒で、VOICEPEAKメインウィンドウの生成完了を待機してからWorker起動へ進みます。
- Worker実行ファイルは `VoicepeakProxyWorker/VoicepeakProxyWorker.exe` 固定で解決するため、ディレクトリ名は変更しないでください。
- PluginとWorkerは独自キューを持たず、棒読みちゃんのキュー順で1件ずつ処理します。
- ログは棒読みちゃんフォルダ直下の `Plugin_VoicepeakProxy_plugin.log` と `Plugin_VoicepeakProxy_worker.log` へそれぞれ出力します。
  - 容量削減のため、ログファイルの内容は起動ごとにクリアされます。
  - `Debug.logMinimumLevel`は`debug`/`info`/`warn`/`error`を指定でき、指定レベル未満のログは出力されません。

