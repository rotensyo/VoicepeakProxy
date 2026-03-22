# bouyomichan-plugin

棒読みちゃんから受け取った文字列を`VoicepeakProxyCore.dll`経由でVOICEPEAKへ読み上げる連携実装です。

## 構成

- `Plugin_VoicepeakProxy/`
  - 棒読みちゃんプラグイン本体です。
  - `TalkTaskStarted`を受けて文字列をWorkerへ転送します。
  - 棒読みちゃん既定音声は常に抑止します。
- `VoicepeakProxyWorker/`
  - 補助プロセスです。
  - `VoicepeakOneShot.SpeakOnceWait`でVOICEPEAK読み上げを実行します。
- `Shared/`
  - PluginとWorkerの共通モデルです。

## ビルド

### 1) Worker(.NET Framework 4.8)

```powershell
dotnet build "bouyomichan-plugin/VoicepeakProxyWorker/VoicepeakProxyWorker.csproj" -c Release
```

### 2) Plugin(.NET Framework 3.5)

棒読みちゃん本体への参照が必要です。

```powershell
msbuild "bouyomichan-plugin/Plugin_VoicepeakProxy/Plugin_VoicepeakProxy.csproj" /p:Configuration=Release /p:Platform=x86 /p:BouyomiChanExePath="D:\Program Files\BouyomiChan_0_1_11_0_Beta21\BouyomiChan.exe"
```

## 配置

棒読みちゃんフォルダへ以下を配置します。

- `Plugin_VoicepeakProxy.dll`
- `VoicepeakProxyWorker/VoicepeakProxyWorker.exe`
- `VoicepeakProxyWorker/VoicepeakProxyCore.dll`
- `VoicepeakProxyWorker/NAudio.Core.dll`
- `VoicepeakProxyWorker/NAudio.Wasapi.dll`

## 設定

- 初回起動時に`Plugin_VoicepeakProxy_setting.json`を自動生成します。
- 設定画面は棒読みちゃんのプラグイン設定ボタンから開きます。
- `AppConfig`の各項目はタブ分割で編集できます。
- Plugin起動時にWorkerは必ず起動し、起動時検証が成功した場合のみ受信を開始します。
- Worker起動待機は最大30秒で、500ms間隔で受信準備完了を確認します。
- ログは棒読みちゃんフォルダ直下の`Plugin_VoicepeakProxy_plugin.log`と`Plugin_VoicepeakProxy_worker.log`へ出力します。

## 仕様

- テキストは`ReplaceWord`優先で取得し、空の場合のみ`SourceText`を使用します。
- 棒読みちゃん既定音声は常に抑止します。
- Worker未接続時は送信スレッドが異常終了し、以降の送信を停止します。
