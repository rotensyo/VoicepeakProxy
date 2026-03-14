# VoicepeakProxyCore

`VoicepeakProxyCore`は、VOICEPEAKをUI自動操作で制御する`.NET Framework 4.8`向けDLLです。

- 常駐ランタイムを起動し、複数の発話要求をキュー処理できます
- ワーカーループを起動せず、1回だけ同期実行する単発APIを使えます
- `[[pause:NNN]]`記法、文字列置換、割り込み、音声ピーク監視に対応します

## 前提条件

- Windows環境であること
- `.NET Framework 4.8`実行環境があること
- `voicepeak.exe`が起動していること
- VOICEPEAK側のショートカット設定が`AppConfig.Ui`と一致していること
- VOICEPEAKが1プロセスだけ起動していること

重要な制約です。

- 先頭移動ショートカットは`F1-F12`または`Ctrl+Up`を使用してください
- 本DLLはVOICEPEAKのUI構造に依存します
- UI仕様やショートカット設定が変わると動作しなくなる可能性があります

## 最短利用例

### 常駐ランタイム

```csharp
using VoicepeakProxyCore;

var config = new AppConfig();
using var runtime = VoicepeakRuntime.Start(config);

EnqueueResult result = runtime.Enqueue(new SpeakRequest
{
    Text = "こんにちは。テストです。",
    Mode = EnqueueMode.Queue,
    Interrupt = false
});

Console.WriteLine($"status={result.Status} jobId={result.JobId} error={result.ErrorMessage}");
```

### 単発実行

```csharp
using VoicepeakProxyCore;

var config = new AppConfig();

SpeakOnceResult result = VoicepeakOneShot.SpeakOnce(
    config,
    new SpeakOnceRequest { Text = "単発読み上げです。" });

Console.WriteLine($"status={result.Status} ok={result.Succeeded} segments={result.SegmentsExecuted}");
```

## 公開API概要

- `VoicepeakRuntime.Start(AppConfig config, IAppLogger logger = null)`
  - 常駐ランタイムを起動します
  - 設定検証と起動時バリデーションを行います
- `VoicepeakRuntime.Enqueue(SpeakRequest request)`
  - 発話要求を非同期受理します
  - 戻り値は`EnqueueResult`です
- `VoicepeakRuntime.Stop()`
  - 新規受理を停止します
- `VoicepeakRuntime.Dispose()`
  - ランタイムを破棄します
- `VoicepeakOneShot.SpeakOnce(AppConfig config, SpeakOnceRequest request, IAppLogger logger = null)`
  - 1回だけ同期実行します
  - 戻り値は`SpeakOnceResult`です

例外方針の要点です。

- `config == null`は`ArgumentNullException`
- 常駐ランタイムの`request == null`は`ArgumentNullException`
- `Stop()`後の`Enqueue(...)`は`InvalidOperationException`
- `Dispose()`後の`Enqueue(...)`は`ObjectDisposedException`
- 入力内容不正は`EnqueueResult.Status`または`SpeakOnceResult.Status`で返す場合があります

## 詳細ドキュメント

- API詳細:`docs/usage/api-reference.md`
- 設定詳細:`docs/usage/configuration.md`
- トラブルシューティング:`docs/usage/troubleshooting.md`
- 実行モデルと状態遷移:`docs/dev/runtime-model.md`
- ソースからのビルド:`docs/dev/build-from-source.md`
