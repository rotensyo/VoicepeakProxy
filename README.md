# VoicepeakProxyCore

`VoicepeakProxyCore`は、VOICEPEAKをUI自動操作で制御するWindows環境向けDLLです。

- 常駐ランタイムを起動し、複数の発話要求をキュー処理できます
- 常駐ランタイムを起動せず、1回だけ同期実行する単発APIを実行できます
- 初期化以外でウィンドウフォーカスを奪わず、VOICEPEAKが他のウィンドウの背面にあっても実行可能です

## 前提条件
- Windows環境であること
- `.NET Framework 4.8`実行環境があること
- `voicepeak.exe`が起動していること
- config内のショートカット設定がVOICEPEAK側と一致していること
- VOICEPEAKが1プロセスだけ起動していること

## 注意点
- 本DLLはVOICEPEAKのUI構造に依存し、UI仕様やショートカット設定が変わると動作しなくなる可能性があります
- 先頭移動ショートカットがF1-F12以外の場合、**発話時点で最後にクリックされたウィンドウ内要素が文字入力欄**である必要があります。
  - 通常は初期化時にクリック操作を行いますが、**起動中にウィンドウをクリックしてしまったた場合**などは、**手動で文字入力欄を一度クリック**してから発話を再実行してください。
  - 先頭移動ショートカットをF1-F12にした場合はこの操作が不要になります。


## 最短利用例

### 単発実行

```csharp
using VoicepeakProxyCore;

var config = new AppConfig();

SpeakOnceResult result = VoicepeakOneShot.SpeakOnce(
    config,
    new SpeakOnceRequest { Text = "こんにちは。テストです。" });

Console.WriteLine($"status={result.Status} ok={result.Succeeded} segments={result.SegmentsExecuted}");
```

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

## 公開API概要
### 常駐実行用
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

### 単発実行用
- `VoicepeakOneShot.SpeakOnce(AppConfig config, SpeakOnceRequest request, IAppLogger logger = null)`
  - 1回だけ実行し、発話の開始を確認したら即完了とします。
  - 戻り値は`SpeakOnceResult`です
- `VoicepeakOneShot.SpeakOnceWait(AppConfig config, SpeakOnceRequest request, IAppLogger logger = null)`
  - 1回だけ実行し、発話終了まで待機してから完了します。
  - 戻り値は`SpeakOnceResult`です

### 例外
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
